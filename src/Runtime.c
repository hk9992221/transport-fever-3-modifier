#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "minhook/include/MinHook.h"
#include "RuntimeSignatures.h"
#include "RuntimeScan.h"
#include "RuntimeLuaView.h"
#include "RuntimeCallables.h"
#define SZ 2163248
#define CMD 560
#define REPLY 66096
#define STATE 1114672
#define CAP 1048576
static uintptr_t base;
static uintptr_t targetGettop;
static unsigned char *shared;
static int (*gettopOriginal)(void*);
static int (*loadbuffer)(void*,const char*,size_t,const char*,const char*);
static int (*pcallfn)(void*,int,int,int,int,void*);
static void (*settop)(void*,int);
static const char*(*tolstring)(void*,int,size_t*);
static volatile LONG busy;static DWORD startupResult;static HANDLE sharedHandle;
static __thread int inside;
static DWORD lastTick,lastAttempt;
static uintptr_t currentL;static DWORD ownerThread;
static BOOL CALLBACK ownerWindow(HWND h,LPARAM arg){DWORD p;DWORD thread=GetWindowThreadProcessId(h,&p);if(p==GetCurrentProcessId()&&IsWindowVisible(h)&&GetWindow(h,GW_OWNER)==NULL){ownerThread=thread;return FALSE;}return TRUE;}
static char*bootstrap;
static wchar_t scriptPath[32768];
static uint32_t rd(int off){return *(volatile uint32_t*)(shared+off);}static void wr(int off,uint32_t val){*(volatile uint32_t*)(shared+off)=val;}
static void message(const char*s){snprintf((char*)shared+48,512,"%s",s?s:"");}
static char* readscript(){HANDLE f=CreateFileW(scriptPath,GENERIC_READ,FILE_SHARE_READ|FILE_SHARE_WRITE,NULL,OPEN_EXISTING,0,NULL);if(f==INVALID_HANDLE_VALUE)return NULL;DWORD n=GetFileSize(f,NULL),got=0;if(n==INVALID_FILE_SIZE||n>800000){CloseHandle(f);return NULL;}char*b=malloc(n+1);if(!b){CloseHandle(f);return NULL;}if(!ReadFile(f,b,n,&got,NULL)){free(b);CloseHandle(f);return NULL;}CloseHandle(f);b[got]=0;if(got>=3&&memcmp(b,"\xef\xbb\xbf",3)==0)memmove(b,b+3,got-2);return b;}
static unsigned imageSignature(IMAGE_NT_HEADERS64*nt,const unsigned char*pattern,const unsigned char*mask,size_t length,uintptr_t*found){
 unsigned hits=0;IMAGE_SECTION_HEADER*section=IMAGE_FIRST_SECTION(nt);
 for(unsigned i=0;i<nt->FileHeader.NumberOfSections;i++,section++){
  if(!(section->Characteristics&IMAGE_SCN_MEM_EXECUTE)||!(section->Characteristics&IMAGE_SCN_MEM_READ))continue;
  size_t size=section->Misc.VirtualSize;
  if(!size||section->VirtualAddress>nt->OptionalHeader.SizeOfImage||size>nt->OptionalHeader.SizeOfImage-section->VirtualAddress)return 3;
  const unsigned char*start=(void*)(base+section->VirtualAddress),*match=NULL;
  if(!readable((uintptr_t)start,size))return 3;
  unsigned count=sz_scan(start,size,pattern,mask,length,&match);
  if(count){*found=(uintptr_t)match;hits+=count;if(hits>1)return hits;}
 }
 return hits;
}
static DWORD resolveLua(void){
 if(!readable(base,sizeof(IMAGE_DOS_HEADER)))return 100;
 IMAGE_DOS_HEADER*d=(void*)base;
 if(d->e_magic!=IMAGE_DOS_SIGNATURE||d->e_lfanew<0||d->e_lfanew>1048576)return 100;
 IMAGE_NT_HEADERS64*nt=(void*)(base+(uintptr_t)d->e_lfanew);
 if(!readable((uintptr_t)nt,sizeof *nt)||nt->Signature!=IMAGE_NT_SIGNATURE||nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC||nt->FileHeader.SizeOfOptionalHeader!=sizeof(IMAGE_OPTIONAL_HEADER64)||!nt->FileHeader.NumberOfSections||nt->FileHeader.NumberOfSections>96)return 100;
 if(!readable((uintptr_t)IMAGE_FIRST_SECTION(nt),(size_t)nt->FileHeader.NumberOfSections*sizeof(IMAGE_SECTION_HEADER)))return 100;
 uintptr_t found[8]={0};unsigned count;
#define RESOLVE(index,name) count=imageSignature(nt,sig_##name,mask_##name,sizeof(sig_##name),&found[index]);if(count!=1)return 110+(index)*2+(count?1:0)
 RESOLVE(0,gettop);RESOLVE(1,loadbuffer);RESOLVE(2,pcall);RESOLVE(3,settop);RESOLVE(4,tolstring);
#undef RESOLVE
 if(imageSignature(nt,sig_indexaddr,mask_indexaddr,sizeof sig_indexaddr,&found[5])!=1||imageSignature(nt,sig_tableabi,mask_tableabi,sizeof sig_tableabi,&found[6])!=1)return 120;
 /* Both APIs must use the independently verified registry/index implementation. */
 if(*(unsigned char*)(found[2]+39)!=0xe8||*(unsigned char*)(found[4]+23)!=0xe8)return 121;
 if(found[2]+44+*(int32_t*)(found[2]+40)!=found[5]||found[4]+28+*(int32_t*)(found[4]+24)!=found[5])return 121;
 /* Independent proof of Table.metatable at +16 before inspecting protected callables. */
 if(imageSignature(nt,sig_getmetatable,mask_getmetatable,sizeof sig_getmetatable,&found[7])!=1)return 122;
 if(*(unsigned char*)(found[7]+7)!=0xe8||found[7]+12+*(int32_t*)(found[7]+8)!=found[5])return 122;
 targetGettop=found[0];gettopOriginal=(void*)found[0];loadbuffer=(void*)found[1];pcallfn=(void*)found[2];settop=(void*)found[3];tolstring=(void*)found[4];
 return 0;
}
static int run(void*L,const char*code,int offset,int capacity){
 int top=gettopOriginal(L);char prefix[16384];
 /* No target is called while collecting proof; expressions contain only verified raw paths. */
 if(!buildCallablePrefix(L,prefix,sizeof prefix))strcpy(prefix,"rawset(_G,'__SZTF3Callable6',{})\n");
 size_t prefixLength=strlen(prefix),codeLength=strlen(code);char*chunk=malloc(prefixLength+codeLength+1);
 if(!chunk){const char*error="接口请求内存分配失败";size_t length=strlen(error);message(error);
  if(offset>=0){memcpy(shared+offset,error,length+1);if(offset==REPLY)wr(28,(uint32_t)length);else if(offset==STATE)wr(36,(uint32_t)length);}return 97;
 }
 memcpy(chunk,prefix,prefixLength);memcpy(chunk+prefixLength,code,codeLength+1);
 int status=loadbuffer(L,chunk,prefixLength+codeLength,"@TF3Modifier/runtime","t");free(chunk);
 if(!status)status=pcallfn(L,0,1,0,0,NULL);
 size_t n=0;const char*s=tolstring(L,-1,&n);if(!s){s=status?"脚本执行失败":"{}";n=strlen(s);}if(n>=(size_t)capacity){s="接口返回的数据过长";n=strlen(s);status=99;}if(offset>=0){memcpy(shared+offset,s,n);shared[offset+n]=0;if(offset==REPLY)wr(28,(uint32_t)n);else if(offset==STATE)wr(36,(uint32_t)n);}if(status)message(s);settop(L,top);return status;
}
static int hooked(void*L){int n=gettopOriginal(L);if(inside||!shared||GetCurrentThreadId()!=ownerThread)return n;DWORD now=GetTickCount();uint32_t seq=rd(16);int pending=seq!=rd(20);if(!pending&&(DWORD)(now-lastTick)<350)return n;if(!pending&&(DWORD)(now-lastAttempt)<15)return n;if(InterlockedCompareExchange(&busy,1,0))return n;inside=1;lastAttempt=now;
 if(isgui(L)){
  uintptr_t global=*(uintptr_t*)((uintptr_t)L+24);if(currentL!=global){if(run(L,bootstrap,REPLY,CAP)){wr(8,(uint32_t)-1);goto done;}currentL=global;message("已连接游戏主线程内存接口");wr(8,1);}
  if(pending){char code[65536];uint32_t size=rd(32);if(size>=sizeof(code)){message("请求过长");wr(24,0);wr(20,seq);goto done;}memcpy(code,shared+CMD,size);code[size]=0;int result;
   if(strcmp(code,"--reload")==0){char*next=readscript();if(next){free(bootstrap);bootstrap=next;result=run(L,bootstrap,REPLY,CAP);}else{message("功能接口文件读取失败");result=98;}}
   else result=run(L,code,REPLY,CAP);wr(24,result?0:1);wr(20,seq);
  }
  if((DWORD)(now-lastTick)>=350){lastTick=now;uint32_t generation=rd(40);wr(40,generation+1);char code[512];int active=rd(44)!=0&&(DWORD)(now-rd(12))<4000;snprintf(code,sizeof code,"return __SZTF3F6.tick(%s)",active?"true":"false");if(run(L,code,STATE,CAP))wr(8,(uint32_t)-1);else wr(8,1);MemoryBarrier();wr(40,generation+2);}
 }
 done:inside=0;InterlockedExchange(&busy,0);return n;}
__declspec(dllexport) DWORD WINAPI Start(void*arg){if(shared)return startupResult;EnumWindows(ownerWindow,0);if(!ownerThread)return 10;base=(uintptr_t)GetModuleHandleW(NULL);DWORD verified=resolveLua();if(verified)return verified;
 HMODULE self=NULL;GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,(LPCWSTR)Start,&self);GetModuleFileNameW(self,scriptPath,32768);wchar_t*slash=wcsrchr(scriptPath,L'\\');if(!slash)return 12;wcscpy(slash+1,L"功能接口v6.lua");bootstrap=readscript();if(!bootstrap)return 13;
 char name[160];snprintf(name,sizeof name,"Local\\SZTransportFever3_v6_%lu",GetCurrentProcessId());HANDLE map=CreateFileMappingA(INVALID_HANDLE_VALUE,NULL,PAGE_READWRITE,0,SZ,name);if(!map){free(bootstrap);bootstrap=NULL;return 16;}shared=MapViewOfFile(map,FILE_MAP_ALL_ACCESS,0,0,SZ);if(!shared){CloseHandle(map);free(bootstrap);bootstrap=NULL;return 17;}sharedHandle=map;memset(shared,0,SZ);wr(0,0x54463331);wr(4,6);strcpy((char*)shared+STATE,"{\"playing\":false}");wr(36,17);message("Lua接口5/5及结构锚点3/3已通过，等待游戏界面脚本状态");
 int result=MH_Initialize();if(result!=MH_OK&&result!=MH_ERROR_ALREADY_INITIALIZED){startupResult=20+result;goto fail;}result=MH_CreateHook((void*)targetGettop,hooked,(void**)&gettopOriginal);if(result!=MH_OK){startupResult=40+result;goto fail;}result=MH_EnableHook((void*)targetGettop);if(result!=MH_OK){MH_RemoveHook((void*)targetGettop);startupResult=60+result;goto fail;}startupResult=0;return 0;
 fail:UnmapViewOfFile(shared);shared=NULL;CloseHandle(sharedHandle);sharedHandle=NULL;free(bootstrap);bootstrap=NULL;return startupResult;}
__declspec(dllexport) DWORD WINAPI Stop(void*arg){if(!shared)return 0;wr(12,0);wr(44,0);return 0;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID unused){if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(instance);return TRUE;}
