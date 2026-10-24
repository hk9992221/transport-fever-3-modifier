using System;using System.IO;using System.IO.MemoryMappedFiles;using System.Diagnostics;using System.Text;using System.Linq;using System.Runtime.InteropServices;using System.Threading;using TransportFever3.Interop;

namespace TransportFever3Trainer {

static class Native {

 [DllImport("kernel32.dll", SetLastError=true)] internal static extern IntPtr OpenProcess(uint access, bool inherit, int id);

 [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool ReadProcessMemory(IntPtr h, IntPtr a, byte[] b, IntPtr n, out IntPtr read);

 [DllImport("kernel32.dll", SetLastError=true)] internal static extern bool WriteProcessMemory(IntPtr h, IntPtr a, byte[] b, IntPtr n, out IntPtr written);

 [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);

 [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr h, int id, uint modifiers, uint key);

 [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr h, int id);

}



sealed class Bridge:IDisposable {

public const int Size=2163248; Process gameProcess;MemoryMappedFile map;MemoryMappedViewAccessor view;int owner=Guid.NewGuid().GetHashCode();bool leased;public int Pid;public string Message="",Snapshot="{}",Response="",GamePath="",GameVersion="",GameBuild="",PeFingerprint="";public long ProcessStartTicks;public bool Ready{get;private set;}public bool Pending{get{return view!=null&&view.ReadUInt32(16)!=view.ReadUInt32(20);}}public int LastResult{get{return view==null?0:view.ReadInt32(24);}}

public void Connect(){var ps=Process.GetProcessesByName("TransportFever3");if(ps.Length!=1)throw new Exception(ps.Length==0?"请先打开《狂热运输3》并进入场景":"请只保留一个游戏进程");gameProcess=ps[0];Pid=gameProcess.Id;ReadIdentity();FindModule(Pid,"TransportFever3Runtime_v6.dll");try{map=MemoryMappedFile.OpenExisting(Protocol.MapPrefix+Pid);view=map.CreateViewAccessor(0,Size);ValidateProtocol();if(view.ReadInt32(8)==1){Claim();Poll();Query("return __SZTF3F6.dispatch({op=\"read\"})",30000);return;}view.Dispose();map.Dispose();view=null;map=null;}catch(FileNotFoundException){}Inject(Pid,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"运行模块","TransportFever3Runtime_v6.dll"),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"运行模块","TransportFever3Runtime_v6.dll"));for(int i=0;i<240;i++){if(view==null)try{map=MemoryMappedFile.OpenExisting(Protocol.MapPrefix+Pid);view=map.CreateViewAccessor(0,Size);}catch(FileNotFoundException){}if(view!=null){ValidateProtocol();if(view.ReadInt32(8)==1){Claim();Poll();Query("return __SZTF3F6.dispatch({op=\"read\"})",30000);return;}if(view.ReadInt32(8)<0)throw new Exception(ReadText(48,512));}Thread.Sleep(50);}throw new Exception("模块等待游戏主线程超时，请切回游戏后重新连接");}

void ValidateProtocol(){if(view.ReadInt32(0)!=Protocol.Magic||view.ReadInt32(4)!=Protocol.RuntimeProtocol)throw new Exception("运行模块协议不匹配，请完全退出游戏后使用本版重新连接");}
void ReadIdentity(){GamePath=Path.GetFullPath(gameProcess.MainModule.FileName);ProcessStartTicks=gameProcess.StartTime.ToUniversalTime().Ticks;try{GameVersion=FileVersionInfo.GetVersionInfo(GamePath).FileVersion??"";}catch{}try{using(var file=File.Open(GamePath,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new BinaryReader(file)){if(reader.ReadUInt16()!=0x5A4D)throw new InvalidDataException("游戏 PE 头无效");file.Position=0x3C;int pe=reader.ReadInt32();if(pe<64||pe>file.Length-0x110)throw new InvalidDataException("游戏 PE 偏移无效");file.Position=pe;if(reader.ReadUInt32()!=0x4550)throw new InvalidDataException("游戏 PE 签名无效");file.Position=pe+8;uint stamp=reader.ReadUInt32();file.Position=pe+0x50;uint size=reader.ReadUInt32();PeFingerprint="PE "+stamp.ToString("X8")+" / 图像大小 "+size.ToString("X8");}}catch(Exception e){PeFingerprint="读取失败："+e.Message;}try{var install=Directory.GetParent(GamePath);var common=install.Parent;var steamapps=common.Parent;string manifest=Path.Combine(steamapps.FullName,"appmanifest_3493540.acf");if(File.Exists(manifest)){var match=System.Text.RegularExpressions.Regex.Match(File.ReadAllText(manifest),"\"buildid\"\\s*\"([0-9]+)\"");if(match.Success)GameBuild=match.Groups[1].Value;}}catch{} }
public string IdentityText{get{return "实际路径："+GamePath+"\r\n连接进程："+Pid+" / 启动标识 "+ProcessStartTicks+"\r\n游戏版本："+(GameVersion.Length>0?GameVersion:"未提供版本资源")+" / Steam Build "+(GameBuild.Length>0?GameBuild:"未知")+"\r\n实际构建："+PeFingerprint+"\r\n运行模块：v6 / 协议 "+Protocol.RuntimeProtocol+"，唯一函数定位与 Lua 功能依赖分别验证";}}
void Claim(){using(var guard=new Mutex(false,Protocol.MapPrefix+Pid+"_claim")){try{guard.WaitOne();}catch(AbandonedMutexException){}try{int active=view.ReadInt32(44),tick=view.ReadInt32(12);if(active!=0&&active!=owner&&unchecked((uint)(Environment.TickCount-tick))<4000)throw new Exception("已有另一份修改器连接此游戏，请先关闭它");if(owner==0)owner=1;if(view.ReadUInt32(16)!=view.ReadUInt32(20))throw new Exception("上一连接仍有未完成请求，请等待游戏执行完成后再连接；不要重复提交操作");view.Write(44,owner);leased=true;}finally{guard.ReleaseMutex();}}}



string ReadText(int o,int cap){byte[] b=new byte[cap];view.ReadArray(o,b,0,cap);int n=Array.IndexOf(b,(byte)0);return Encoding.UTF8.GetString(b,0,n<0?cap:n);}

public void Poll(){if(view==null)return;if(leased&&view.ReadInt32(44)!=owner)throw new Exception("当前连接已由另一份修改器接管");if(gameProcess==null||gameProcess.HasExited)throw new Exception("游戏进程已退出，请重新打开游戏后连接");view.Write(12,unchecked((uint)Environment.TickCount));Ready=view.ReadInt32(8)==1;Message=ReadText(48,512);for(int i=0;i<3;i++){uint g=view.ReadUInt32(40);if((g&1)!=0)continue;int n=(int)view.ReadUInt32(36);if(n<0||n>=1048576)throw new Exception("状态长度无效");string data=ReadText(1114672,n+1);if(view.ReadUInt32(40)==g){if(data.Length>0)Snapshot=data;break;}}if(!Pending){int n=(int)view.ReadUInt32(28);if(n>=0&&n<1048576)Response=ReadText(66096,n+1);}}

public void Send(string code){if(!Ready)throw new Exception("请先连接游戏");if(Pending)throw new Exception("上一项操作正在执行，请稍候");byte[] b=Encoding.UTF8.GetBytes(code);if(b.Length>=65536)throw new Exception("本次选择过多，请分批执行");view.WriteArray(560,b,0,b.Length);view.Write(32,(uint)b.Length);Thread.MemoryBarrier();view.Write(16,unchecked(view.ReadUInt32(16)+1));}

public string Query(string code,int timeout=5000){Send(code);var watch=Stopwatch.StartNew();while(Pending&&watch.ElapsedMilliseconds<timeout){Poll();Thread.Sleep(20);}Poll();if(Pending)throw new Exception("游戏正在暂停或加载，请回到游戏后重试");if(LastResult!=1)throw new Exception(Response.Length>0?Response:Message);return Response;}

public void Dispose(){try{if(view!=null&&leased&&view.ReadInt32(44)==owner){view.Write(12,unchecked((uint)(Environment.TickCount-6001)));view.Write(44,0);}leased=false;}finally{Ready=false;if(gameProcess!=null){gameProcess.Dispose();gameProcess=null;}if(view!=null)view.Dispose();if(map!=null)map.Dispose();view=null;map=null;}}

 public static void Inject(int pid,string dll,string runtime){

  string path=Path.GetFullPath(dll);if(!File.Exists(path))throw new FileNotFoundException("增强模块缺失",path);

  IntPtr h=Native.OpenProcess(0x43a,false,pid);if(h==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法加载增强模块，请与游戏使用相同权限");

  try {

   long remote=FindModule(pid,Path.GetFileName(path));

   if(remote==0){byte[] data=Encoding.Unicode.GetBytes(path+"\0");IntPtr memory=VirtualAllocEx(h,IntPtr.Zero,new IntPtr(data.Length),0x3000,4);if(memory==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

    bool canFree=true;IntPtr count;try{if(!Native.WriteProcessMemory(h,memory,data,new IntPtr(data.Length),out count)||count.ToInt64()!=data.Length)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

     IntPtr loader=GetProcAddress(GetModuleHandle("kernel32.dll"),"LoadLibraryW"),owner;if(loader==IntPtr.Zero||!GetModuleHandleEx(6,loader,out owner))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"无法定位系统加载器");var ownerPath=new StringBuilder(32768);if(GetModuleFileName(owner,ownerPath,ownerPath.Capacity)==0)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());long remoteOwner=FindModule(pid,Path.GetFileName(ownerPath.ToString()));if(remoteOwner==0)throw new Exception("无法定位目标进程的系统加载模块");

     IntPtr t=CreateRemoteThread(h,IntPtr.Zero,IntPtr.Zero,new IntPtr(remoteOwner+loader.ToInt64()-owner.ToInt64()),memory,0,IntPtr.Zero);if(t==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());

     try{if(WaitForSingleObject(t,10000)!=0){canFree=false;throw new TimeoutException("增强模块加载超时，请重启游戏后重试");}}finally{Native.CloseHandle(t);}

    }finally{if(canFree)VirtualFreeEx(h,memory,IntPtr.Zero,0x8000);}

    remote=FindModule(pid,Path.GetFileName(path));if(remote==0)throw new Exception("游戏未能加载增强模块");

   }

   IntPtr local=LoadLibraryEx(path,IntPtr.Zero,1);if(local==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());long begin;

   try{IntPtr export=GetProcAddress(local,"Start");if(export==IntPtr.Zero)throw new Exception("增强模块入口缺失");begin=remote+export.ToInt64()-local.ToInt64();}finally{FreeLibrary(local);}

   byte[] payload=Encoding.UTF8.GetBytes(Path.GetFullPath(runtime)+"\0"); IntPtr arg=VirtualAllocEx(h,IntPtr.Zero,new IntPtr(payload.Length),0x3000,4),written; if(arg==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); bool release=true;try{if(!Native.WriteProcessMemory(h,arg,payload,new IntPtr(payload.Length),out written)||written.ToInt64()!=payload.Length)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());IntPtr worker=CreateRemoteThread(h,IntPtr.Zero,IntPtr.Zero,new IntPtr(begin),arg,0,IntPtr.Zero);if(worker==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());try{if(WaitForSingleObject(worker,10000)!=0){release=false;throw new TimeoutException("连接初始化超时");}uint result;if(!GetExitCodeThread(worker,out result))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());if(result!=0)throw new Exception(InitializationError(result));}finally{Native.CloseHandle(worker);}}finally{if(release)VirtualFreeEx(h,arg,IntPtr.Zero,0x8000);}

  }finally{Native.CloseHandle(h);}

 }

 static string InitializationError(uint result){string detail=result==100?"游戏 PE 或架构验证失败":result>=110&&result<=119?"游戏 Lua 函数唯一定位未通过（"+new[]{"gettop","loadbuffer","pcall","settop","tolstring"}[(result-110)/2]+" / "+(result%2==0?"未匹配":"匹配不唯一")+"）":result==120?"游戏 Lua ABI 验证未通过":result==121?"跨调用目标验证未通过":result==122?"Lua 可调用表结构锚点唯一验证未通过":result==13?"功能接口v6.lua缺失或不可读":result==10?"未找到游戏窗口主线程":"请导出诊断";return "增强模块初始化失败（返回 "+result+"）："+detail;}
 static string RuntimeArtifactHash(string path){using(var file=File.Open(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var hash=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(hash.ComputeHash(file)).Replace("-","");}
 static bool RuntimeHeaderMatches(string mapName){try{using(var memory=MemoryMappedFile.OpenExisting(mapName,MemoryMappedFileRights.Read))using(var read=memory.CreateViewAccessor(0,8,MemoryMappedFileAccess.Read))return read.ReadInt32(0)==Protocol.Magic&&read.ReadInt32(4)==Protocol.RuntimeProtocol;}catch{return false;}}
 static bool CanReuseRuntime(string expected,string resident,Func<bool> protocolVerified,out string reason){reason="";try{if(!File.Exists(expected)||!File.Exists(resident)){reason="运行模块文件缺失";return false;}if(RuntimeArtifactHash(expected)!=RuntimeArtifactHash(resident)){reason="同名运行模块内容不同";return false;}string expectedLua=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(expected)),"功能接口v6.lua"),residentLua=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(resident)),"功能接口v6.lua");if(!File.Exists(expectedLua)||!File.Exists(residentLua)){reason="功能接口文件缺失";return false;}if(RuntimeArtifactHash(expectedLua)!=RuntimeArtifactHash(residentLua)){reason="功能接口内容不同";return false;}if(!protocolVerified()){reason="驻留模块协议尚未通过 v6 验证";return false;}return true;}catch(Exception e){reason="运行产物无法核对："+e.Message;return false;}}
 static long FindModule(int pid,string name){using(var p=Process.GetProcessById(pid))foreach(ProcessModule m in p.Modules)if(m.ModuleName.Equals(name,StringComparison.OrdinalIgnoreCase)){string expected=Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"运行模块",name));if(name.Equals("TransportFever3Runtime_v6.dll",StringComparison.OrdinalIgnoreCase)&&!Path.GetFullPath(m.FileName).Equals(expected,StringComparison.OrdinalIgnoreCase)){string reason;if(!CanReuseRuntime(expected,m.FileName,()=>RuntimeHeaderMatches(Protocol.MapPrefix+pid),out reason))throw new Exception("同名运行模块来自另一目录且产物核对未通过："+reason+"；请完全重启游戏后再使用本版。驻留路径："+m.FileName);}return m.BaseAddress.ToInt64();}return 0;}



 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr VirtualAllocEx(IntPtr h,IntPtr a,IntPtr size,uint type,uint protect);

 [DllImport("kernel32.dll")]static extern bool VirtualFreeEx(IntPtr h,IntPtr a,IntPtr size,uint type);

 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr CreateRemoteThread(IntPtr h,IntPtr attr,IntPtr stack,IntPtr start,IntPtr arg,uint flags,IntPtr id);

 [DllImport("kernel32.dll")]static extern uint WaitForSingleObject(IntPtr h,uint timeout);

 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetModuleHandleEx(uint flags,IntPtr address,out IntPtr module);

 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern uint GetModuleFileName(IntPtr module,StringBuilder path,int size);

 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetModuleHandle(string name);

 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr h,string name);

 [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);

 [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr h);



[DllImport("kernel32.dll",SetLastError=true)]static extern bool GetExitCodeThread(IntPtr h,out uint exitCode);

}

}
