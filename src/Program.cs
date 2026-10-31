using System;using System.Collections.Generic;using System.Drawing;using System.Globalization;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace TransportFever3Trainer {
static class Entry {
 [STAThread] static int Main(string[] args){
  if(BuildMode.Offline){string report=Environment.GetEnvironmentVariable("TF3_MODIFIER_LAYOUT_REPORT");if(string.IsNullOrWhiteSpace(report)&&args.Length>1)report=args[1];if(string.IsNullOrWhiteSpace(report))return 4;Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);using(var f=new MainForm(true,true)){f.ShowInTaskbar=false;f.Opacity=0;f.Show();Application.DoEvents();bool pass=f.ValidateLayout(report);f.Close();return pass?0:3;}}
  if(args.Length>2&&args[0]=="--script") {using(var b=new Bridge()){try{b.Connect();var log=new List<object>();foreach(var c in Data.Rows(Data.Json.DeserializeObject(File.ReadAllText(args[1],System.Text.Encoding.UTF8)))){double wait=Data.Num(c,"wait");if(wait>0){var watch=System.Diagnostics.Stopwatch.StartNew();while(watch.ElapsedMilliseconds<wait){b.Poll();Thread.Sleep(25);}continue;}string reply=b.Query("return __SZTF3F6.dispatch("+Data.LuaObject(c)+")",30000);for(int i=0;i<6;i++){b.Poll();Thread.Sleep(100);}log.Add(new{command=c,response=Data.Json.DeserializeObject(reply),snapshot=Data.Json.DeserializeObject(b.Snapshot)});}File.WriteAllText(args[2],Data.Json.Serialize(log),System.Text.Encoding.UTF8);return 0;}catch(Exception e){File.WriteAllText(args[2],e.ToString(),System.Text.Encoding.UTF8);return 1;}}}
  if(args.Length>1&&args[0]=="--audit") return IndustryAudit.Run(args[1]);
  Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);bool uiTest=args.Length>1&&args[0]=="--ui-test";bool capture=args.Length>1&&(args[0]=="--capture-live"||uiTest);int exit=0;
  using(var f=new MainForm(capture)){if(uiTest){f.Shown+=async delegate{await Task.Delay(500);exit=await f.Regression(args[1])?0:3;f.Close();};}
 if(capture&&!uiTest){f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-4000,-4000);var watch=System.Diagnostics.Stopwatch.StartNew();var timer=new System.Windows.Forms.Timer{Interval=250};timer.Tick+=delegate{if((!f.CaptureReady||watch.ElapsedMilliseconds<2000)&&watch.ElapsedMilliseconds<45000)return;timer.Stop();if(!f.CaptureReady)exit=2;f.ShowPage(args.Length>2?args[2]:"features");f.SelectFirst();Application.DoEvents();var path=Path.GetFullPath(args[1]);Directory.CreateDirectory(Path.GetDirectoryName(path));f.SaveWindowImage(path);File.WriteAllText(Path.ChangeExtension(path,".json"),f.StateJson,System.Text.Encoding.UTF8);f.Close();};f.Shown+=delegate{timer.Start();};}Application.Run(f);}return exit;
 }
}
sealed class Choice {public string Id,Name;public Choice(string id,string name){Id=id;Name=name;}public override string ToString(){return Name;}}
// The full cargo audit of the running game. The index is read through the
// same Lua adapter the page uses, so the audited sets are the sets the page
// shows and not a second, independent parser.
static class IndustryAudit {
 public static int Run(string output){
  var index=new IndustryIndex();
  string request="audit",scope="",world="";
  int revision=0;
  using(var bridge=new Bridge()){
   try{
    bridge.Connect();
    var metaReply=bridge.Query("return __SZTF3F6.dispatch({op='industry_refresh',request='"+request+"'})",30000);
    var meta=Data.Obj(Data.Json.DeserializeObject(metaReply));
    if(!Data.Bool(meta,"ok"))throw new InvalidOperationException("产业索引刷新失败："+Data.Str(meta,"message"));
    scope=Data.Str(meta,"nameScope");world=Data.Str(meta,"world");revision=(int)Data.Num(meta,"revision");
    foreach(var part in new[]{"industry","town","cargo"}){
     // The cursor follows the catalogue page metadata, not the merged count,
     // so a page that repeats an entity cannot make the loop stall.
     int offset=0,guard=0,total=0;
     while(true){
      string reply=bridge.Query("return __SZTF3F6.dispatch({op='industry_catalog',request='audit-"+part+"-"+offset+"',offset="+offset+",limit=100,part='"+part+"'})",30000);
      var root=Data.Obj(Data.Json.DeserializeObject(reply));
      if(!Data.Bool(root,"ok"))throw new InvalidOperationException("产业目录读取失败："+Data.Str(root,"message"));
      var page=IndustryCatalogPage.Read(root,Data.Str(root,"request"),world,scope);
      IndustryRelations.Merge(index,page);
      total=page.Total;
      int received=part=="industry"?page.Items.Count:part=="town"?page.Towns.Count:page.CargoNames.Count;
      if(received<=0)break;
      offset+=received;
      if(offset>=total)break;
      if(++guard>10000)throw new InvalidOperationException("产业目录分页没有结束。");
     }
    }
   }catch(Exception e){File.WriteAllText(output,Data.Json.Serialize(new{error=e.Message}),System.Text.Encoding.UTF8);return 1;}
  }
  var rows=IndustryRelations.Audit(index,"live-game");
  var report=new{
   generatedAt=DateTime.UtcNow.ToString("o"),source="live-game",revision=revision,world=world,nameScope=scope,
   auditMethod=IndustryRelations.AuditMethod,
   complete=index.Complete,cargoCatalogueComplete=index.CargoCatalogueComplete,
   industryCount=index.Industries.Count,townCount=index.Towns.Count,
   unreadIndustryCount=index.UnreadIndustryCount,unreadTownCount=index.UnreadTownCount,
   auditedCargoCount=rows.Count,rows=rows};
  File.WriteAllText(output,Data.Json.Serialize(report),System.Text.Encoding.UTF8);
  return 0;
 }
}
sealed class UserPrefs {
 public Dictionary<string,int> keys=new Dictionary<string,int>();public Dictionary<string,List<string>> favorites=new Dictionary<string,List<string>>(),recent=new Dictionary<string,List<string>>();public Dictionary<string,string> displayNames=new Dictionary<string,string>();public List<Dictionary<string,object>> bookmarks=new List<Dictionary<string,object>>();
 public static string FilePath{get{string home=Environment.GetEnvironmentVariable("TF3_MODIFIER_PORTABLE_HOME");if(string.IsNullOrWhiteSpace(home))home=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TransportFever3Modifier","UserData");return Path.Combine(home,"配置","用户设置.json");}}
 public static UserPrefs Load(){try{string path=FilePath;if(!File.Exists(path)){string legacy=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"配置","用户设置.json");if(File.Exists(legacy))path=legacy;}var p=Data.Json.Deserialize<UserPrefs>(File.ReadAllText(path,System.Text.Encoding.UTF8))??new UserPrefs();if(p.displayNames==null)p.displayNames=new Dictionary<string,string>();return p;}catch{return new UserPrefs();}}
 public void Save(){Directory.CreateDirectory(Path.GetDirectoryName(FilePath));string temp=FilePath+".tmp";File.WriteAllText(temp,Data.Json.Serialize(this),System.Text.Encoding.UTF8);if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);}
 public List<string> Set(string page,bool rec){var d=rec?recent:favorites;List<string> l;if(!d.TryGetValue(page,out l)){l=new List<string>();d[page]=l;}return l;}
}
sealed class KeyDialog:WindowChrome {
 public Keys Selected;Label help;public KeyDialog(string name,Keys initial){Text="快捷键 · "+name;Size=new Size(440,210);StartPosition=FormStartPosition.CenterParent;KeyPreview=true;Selected=initial;help=MainForm.LabelFor("按下要绑定的组合键",12,true);help.SetBounds(24,62,390,40);Controls.Add(help);var clear=new PillButton{Text="清除绑定"};clear.SetBounds(24,120,110,34);clear.Click+=delegate{Selected=Keys.None;DialogResult=DialogResult.OK;Close();};Controls.Add(clear);var cancel=new PillButton{Text="取消"};cancel.SetBounds(290,120,110,34);cancel.Click+=delegate{DialogResult=DialogResult.Cancel;Close();};Controls.Add(cancel);KeyDown+=delegate(object s,KeyEventArgs e){if(e.KeyCode==Keys.ControlKey||e.KeyCode==Keys.ShiftKey||e.KeyCode==Keys.Menu)return;if(e.KeyCode==Keys.Escape){DialogResult=DialogResult.Cancel;Close();return;}Selected=e.KeyData;DialogResult=DialogResult.OK;e.SuppressKeyPress=true;Close();};}
}
}
