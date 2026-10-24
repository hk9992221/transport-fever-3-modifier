using System;using System.Collections.Generic;using System.Drawing;using System.Drawing.Drawing2D;using System.Globalization;using System.IO;using System.Linq;using System.Reflection;using System.Threading.Tasks;using System.Web.Script.Serialization;using System.Windows.Forms;

namespace TransportFever3Trainer {

static class Data {

 internal static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=4194304};

 internal static Dictionary<string,object> Obj(object o){return o as Dictionary<string,object>??new Dictionary<string,object>();}

 internal static object Get(object o,string key){object x;return Obj(o).TryGetValue(key,out x)?x:null;}

 internal static string Str(object o,string k,string d=""){return Convert.ToString(Get(o,k)??d,CultureInfo.InvariantCulture);}

 internal static bool Bool(object o,string k){return Get(o,k) is bool&&(bool)Get(o,k);}

 internal static double Num(object o,string k,double d=0){double x;return double.TryParse(Str(o,k),NumberStyles.Float,CultureInfo.InvariantCulture,out x)?x:d;}

 internal static List<Dictionary<string,object>> Rows(object o){var a=o as object[];return a==null?new List<Dictionary<string,object>>():a.Select(Obj).ToList();}

 internal static IEnumerable<string> RowsOfIds(object o){var a=o as object[];return a==null?Enumerable.Empty<string>():a.Select(x=>Convert.ToString(x,CultureInfo.InvariantCulture));}
 internal static string Resource(string n){using(var r=new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream("TransportFever3Trainer."+n)))return r.ReadToEnd();}

 internal static string Lua(string s){return "\""+s.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n")+"\"";}

 internal static string LuaObject(object value){return LuaValue(Json.DeserializeObject(Json.Serialize(value)));}
 internal static string LuaValue(object value){if(value==null)return "nil";if(value is bool)return (bool)value?"true":"false";if(value is string)return Lua((string)value);var dict=value as Dictionary<string,object>;if(dict!=null)return "{"+string.Join(",",dict.Select(x=>"["+Lua(x.Key)+"]="+LuaValue(x.Value)))+"}";var array=value as object[];if(array!=null)return "{"+string.Join(",",array.Select(LuaValue))+"}";return Convert.ToString(value,CultureInfo.InvariantCulture);}
 internal static string LuaIds(IEnumerable<string> ids){return "{"+string.Join(",",ids.Select(Lua))+"}";}

}

class BlueSlider:Control {

 public double Minimum=.05,Maximum=10;double value=1;bool drag;public event EventHandler ValueChanged;

 public double Value{get{return value;}set{double v=Math.Max(Minimum,Math.Min(Maximum,value));if(Math.Abs(this.value-v)<.000001)return;this.value=v;Invalidate();if(ValueChanged!=null)ValueChanged(this,EventArgs.Empty);}}

 public BlueSlider(){Height=28;TabStop=true;Cursor=Cursors.Hand;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque|ControlStyles.Selectable,true);}

 protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent==null?Theme.Card:Parent.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int radius=Ui.Px(this,8),x=radius+1,y=Height/2,w=Math.Max(1,Width-x*2);float ratio=(float)((Value-Minimum)/Math.Max(.000001,Maximum-Minimum));int pos=x+(int)(w*ratio);using(var p=new Pen(Theme.Line,Ui.Px(this,4))){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLine(p,x,y,x+w,y);}using(var p=new Pen(Enabled?Theme.Purple:Theme.Muted,Ui.Px(this,4))){p.StartCap=p.EndCap=LineCap.Round;e.Graphics.DrawLine(p,x,y,pos,y);}using(var b=new SolidBrush(Theme.Card))e.Graphics.FillEllipse(b,pos-radius,y-radius,radius*2,radius*2);using(var p=new Pen(Enabled?Theme.Purple:Theme.Muted,Ui.Px(this,2)))e.Graphics.DrawEllipse(p,pos-radius,y-radius,radius*2,radius*2);}

 void MoveValue(int x){int edge=Ui.Px(this,8)+1;Value=Minimum+(Maximum-Minimum)*Math.Max(0,Math.Min(1,(x-(double)edge)/Math.Max(1,Width-edge*2)));}

 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(Enabled&&e.Button==MouseButtons.Left){Focus();drag=true;Capture=true;MoveValue(e.X);}}

 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(drag)MoveValue(e.X);}

 protected override void OnMouseUp(MouseEventArgs e){drag=false;Capture=false;base.OnMouseUp(e);}

 protected override void OnMouseCaptureChanged(EventArgs e){drag=false;base.OnMouseCaptureChanged(e);}

 protected override bool IsInputKey(Keys k){return (k&Keys.KeyCode)==Keys.Left||(k&Keys.KeyCode)==Keys.Right||base.IsInputKey(k);}

 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Left||e.KeyCode==Keys.Right){Value+=(e.KeyCode==Keys.Right?1:-1)*(Maximum-Minimum)/100;e.Handled=true;}base.OnKeyDown(e);}

}

sealed class FeatureCard:Surface {

 public readonly Dictionary<string,object> Spec;public readonly Switch Toggle=new Switch();public readonly NumericUpDown Number=new ThemedNumericUpDown();public readonly BlueSlider Slider=new BlueSlider();public readonly PillButton Run=new PillButton(),Hotkey=new PillButton();readonly System.Windows.Forms.Timer valueTimer=new System.Windows.Forms.Timer{Interval=200};readonly Label title,note;bool updating;public bool Supported=true;

 public string Id{get{return Data.Str(Spec,"id");}}public string Kind{get{return Data.Str(Spec,"kind");}}

 public event Action<FeatureCard> Requested;public event Action<FeatureCard> KeyRequested;

 public FeatureCard(Dictionary<string,object> spec){Spec=spec;Height=126;BackColor=Theme.Card;Radius=12;title=MainForm.LabelFor(Data.Str(spec,"name"),11,true);note=MainForm.LabelFor(Data.Str(spec,"note"),9);note.ForeColor=Theme.Muted;Controls.Add(title);Controls.Add(note);Hotkey.Text="设置快捷键";Hotkey.Font=Theme.F(9);Hotkey.BackColor=Theme.Panel;Controls.Add(Hotkey);Hotkey.Click+=delegate{if(KeyRequested!=null)KeyRequested(this);};

  if(Kind=="action"||Kind=="number_action"){if(Kind=="number_action"){Number.Minimum=(decimal)Data.Num(spec,"min",1);Number.Maximum=(decimal)Data.Num(spec,"max",500);Number.Value=(decimal)Data.Num(spec,"default",50);Number.DecimalPlaces=(int)Data.Num(spec,"digits",0);Number.Increment=Number.DecimalPlaces==0?1:.05m;Number.BorderStyle=BorderStyle.None;Number.BackColor=Theme.Input;Number.ForeColor=Theme.Ink;Number.TextAlign=HorizontalAlignment.Center;Number.Font=Theme.F(10,true);Controls.Add(Number);}Run.Text="执行";Run.Primary=true;Controls.Add(Run);Run.Click+=delegate{if(Requested!=null)Requested(this);};}else{Controls.Add(Toggle);Toggle.CheckedChanged+=delegate{if(!updating){valueTimer.Stop();if(Requested!=null)Requested(this);}};if(Kind=="value"){Number.Minimum=(decimal)Data.Num(spec,"min");Number.Maximum=(decimal)Data.Num(spec,"max",10);Number.DecimalPlaces=(int)Data.Num(spec,"digits",2);Number.Increment=Number.DecimalPlaces==0?1:.05m;Number.Value=(decimal)Data.Num(spec,"default",1);Number.BackColor=Theme.Input;Number.ForeColor=Theme.Ink;Number.BorderStyle=BorderStyle.None;Number.TextAlign=HorizontalAlignment.Center;Number.Font=Theme.F(9,true);Slider.Minimum=(double)Number.Minimum;Slider.Maximum=(double)Number.Maximum;Slider.Value=(double)Number.Value;Controls.Add(Number);Controls.Add(Slider);Number.ValueChanged+=delegate{bool user=!updating;bool old=updating;updating=true;Slider.Value=(double)Number.Value;updating=old;if(user&&Toggle.Checked){valueTimer.Stop();valueTimer.Start();}};valueTimer.Tick+=delegate{valueTimer.Stop();if(Toggle.Checked&&Toggle.Enabled&&Requested!=null)Requested(this);};Disposed+=delegate{valueTimer.Dispose();};Slider.ValueChanged+=delegate{if(!updating){updating=true;Number.Value=Math.Round((decimal)Slider.Value,Number.DecimalPlaces);updating=false;}};Slider.MouseUp+=delegate{if(Toggle.Checked&&Requested!=null)Requested(this);};Slider.KeyUp+=delegate{if(Toggle.Checked&&Requested!=null)Requested(this);};}}

  SizeChanged+=delegate{Arrange();};Arrange();

 }

 void Arrange(){int w=Width,p=Ui.Px(this,16),row=Ui.Px(this,48);Hotkey.Name="feature-hotkey";Hotkey.SetBounds(w-Ui.Px(this,118),Ui.Px(this,12),Ui.Px(this,102),Ui.Px(this,27));title.SetBounds(p,Ui.Px(this,12),Math.Max(1,w-Ui.Px(this,150)),Ui.Px(this,29));note.SetBounds(p,Ui.Px(this,88),Math.Max(1,w-p*2),Ui.Px(this,32));if(Kind=="action"||Kind=="number_action"){Run.Name="feature-run";Run.SetBounds(Kind=="action"?p:w-Ui.Px(this,116),row,Ui.Px(this,100),Ui.Px(this,34));if(Kind=="number_action"){Number.Name="feature-number";Number.SetBounds(p,row,Math.Max(1,Math.Min(Ui.Px(this,240),w-Ui.Px(this,148))),Ui.Px(this,30));}else note.SetBounds(Ui.Px(this,132),row,Math.Max(1,w-Ui.Px(this,148)),Ui.Px(this,62));}else{Toggle.Name="feature-toggle";Toggle.SetBounds(p,row,Ui.Px(this,40),Ui.Px(this,24));if(Kind=="value"){Number.Name="feature-number";Number.SetBounds(w-Ui.Px(this,128),row,Ui.Px(this,112),Ui.Px(this,30));Slider.Name="feature-slider";Slider.SetBounds(Ui.Px(this,72),row,Math.Max(1,w-Ui.Px(this,216)),Ui.Px(this,28));}else note.SetBounds(Ui.Px(this,72),row,Math.Max(1,w-Ui.Px(this,88)),Ui.Px(this,64));}}

 public void Capability(object cap,bool connected){Supported=Data.Bool(cap,"supported");string reason=Data.Str(cap,"reason");note.Text=!connected||Supported?Data.Str(Spec,"note"):reason.Length>0?reason:"当前版本尚未验证";Toggle.Enabled=connected&&(Supported||Toggle.Checked);Run.Enabled=connected&&Supported;Hotkey.Enabled=true;Slider.Enabled=Number.Enabled=!connected||Supported;title.ForeColor=connected&&!Supported?Theme.Muted:Theme.Ink;}

 public void State(bool on,object value=null){updating=true;Toggle.Checked=on;if(on&&Kind=="value"&&value!=null&&!(value is bool)&&!Number.ContainsFocus&&!Slider.ContainsFocus&&!Slider.Capture){decimal v;if(decimal.TryParse(Convert.ToString(value,CultureInfo.InvariantCulture),NumberStyles.Float,CultureInfo.InvariantCulture,out v))Number.Value=Math.Max(Number.Minimum,Math.Min(Number.Maximum,v));}updating=false;}

}



}
