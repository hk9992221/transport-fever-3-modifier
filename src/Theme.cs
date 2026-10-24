using System;using System.Collections;using System.Collections.Generic;using System.Drawing;using System.Drawing.Drawing2D;using System.Globalization;using System.IO;using System.Linq;using System.Text;using System.Threading;using System.Web.Script.Serialization;using System.Windows.Forms;
namespace TransportFever3Trainer {
static class Theme {public static Color Back=Color.FromArgb(47,48,59),Panel=Color.FromArgb(55,56,68),Card=Color.FromArgb(61,62,75),Input=Color.FromArgb(42,43,54),Purple=Color.FromArgb(112,99,245),Line=Color.FromArgb(77,78,91),Ink=Color.FromArgb(244,244,250),Muted=Color.FromArgb(180,182,199),Green=Color.FromArgb(114,224,175);public static Font F(float s=10,bool b=false){return new Font("Microsoft YaHei UI",s,b?FontStyle.Bold:FontStyle.Regular);}public static GraphicsPath Path(Rectangle r,int rad){var p=new GraphicsPath();p.AddArc(r.X,r.Y,rad,rad,180,90);p.AddArc(r.Right-rad,r.Y,rad,rad,270,90);p.AddArc(r.Right-rad,r.Bottom-rad,rad,rad,0,90);p.AddArc(r.X,r.Bottom-rad,rad,rad,90,90);p.CloseFigure();return p;}}
class Surface:Panel {public int Radius=14;public Surface(){DoubleBuffered=true;BackColor=Theme.Panel;}protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var p=Theme.Path(new Rectangle(0,0,Width-1,Height-1),Radius))using(var pen=new Pen(Theme.Line))e.Graphics.DrawPath(pen,p);base.OnPaint(e);}}
class PillButton:Control,IButtonControl {
 bool primary,hovered,pressed,spaceDown,isDefault;
 public bool Primary {get{return primary;}set{if(primary==value)return;primary=value;Invalidate();}}
 public DialogResult DialogResult {get;set;}
 public PillButton(){BackColor=Theme.Card;ForeColor=Theme.Ink;Font=Theme.F(9,true);Cursor=Cursors.Hand;Height=34;TabStop=true;AccessibleRole=AccessibleRole.PushButton;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque|ControlStyles.Selectable|ControlStyles.StandardClick,true);SetStyle(ControlStyles.StandardDoubleClick,false);}
 public void NotifyDefault(bool value){isDefault=value;Invalidate();}
 public void PerformClick(){if(CanSelect)OnClick(EventArgs.Empty);}
 protected override void OnClick(EventArgs e){if(!Enabled)return;Focus();var form=FindForm();if(form!=null&&DialogResult!=DialogResult.None)form.DialogResult=DialogResult;base.OnClick(e);}
 protected override bool IsInputKey(Keys keyData){return (keyData&Keys.KeyCode)==Keys.Space||base.IsInputKey(keyData);}
 protected override bool ProcessDialogKey(Keys keyData){if(keyData==Keys.Enter&&CanSelect){PerformClick();return true;}return base.ProcessDialogKey(keyData);}
 protected override bool ProcessMnemonic(char charCode){if(CanSelect&&IsMnemonic(charCode,Text)){Select();PerformClick();return true;}return base.ProcessMnemonic(charCode);}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space){spaceDown=true;pressed=true;Invalidate();e.Handled=true;e.SuppressKeyPress=true;}base.OnKeyDown(e);}
 protected override void OnKeyUp(KeyEventArgs e){if(e.KeyCode==Keys.Space&&spaceDown){spaceDown=pressed=false;Invalidate();e.Handled=true;PerformClick();}base.OnKeyUp(e);}
 protected override void OnMouseEnter(EventArgs e){hovered=true;Invalidate();base.OnMouseEnter(e);}
 protected override void OnMouseLeave(EventArgs e){hovered=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){pressed=true;Invalidate();}base.OnMouseDown(e);}
 protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
 protected override void OnMouseCaptureChanged(EventArgs e){pressed=false;Invalidate();base.OnMouseCaptureChanged(e);}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){spaceDown=pressed=false;base.OnLostFocus(e);Invalidate();}
 protected override void OnParentBackColorChanged(EventArgs e){base.OnParentBackColorChanged(e);Invalidate();}
 protected override void OnEnabledChanged(EventArgs e){hovered=pressed=spaceDown=false;base.OnEnabledChanged(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){
  e.Graphics.Clear(Parent==null?Theme.Back:Parent.BackColor);
  if(Width<2||Height<2)return;
  e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  Color fill=Enabled?(Primary?Theme.Purple:BackColor):Theme.Input;
  if(Enabled&&pressed)fill=ControlPaint.Dark(fill,.08f);else if(Enabled&&hovered)fill=ControlPaint.Light(fill,.06f);
  using(var p=Theme.Path(new Rectangle(0,0,Width-1,Height-1),Math.Min(12,Math.Min(Width-1,Height-1))))using(var b=new SolidBrush(fill))e.Graphics.FillPath(b,p);
  TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Enabled?ForeColor:Theme.Muted,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);
  if(Enabled&&((Focused&&ShowFocusCues)||isDefault)&&Width>10&&Height>10)using(var p=Theme.Path(new Rectangle(3,3,Width-7,Height-7),Math.Min(8,Math.Min(Width-7,Height-7))))using(var pen=new Pen(Theme.Muted)){pen.DashStyle=DashStyle.Dot;e.Graphics.DrawPath(pen,p);}
 }
}
class Switch:Control {
 bool isChecked;
 public event EventHandler CheckedChanged;
 public bool Checked {get{return isChecked;}set{if(isChecked==value)return;isChecked=value;Invalidate();if(CheckedChanged!=null)CheckedChanged(this,EventArgs.Empty);}}
 public Switch(){Size=new Size(36,22);Cursor=Cursors.Hand;TabStop=true;AccessibleRole=AccessibleRole.CheckButton;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque|ControlStyles.Selectable,true);}
 protected override void OnClick(EventArgs e){if(Enabled){Focus();Checked=!Checked;}base.OnClick(e);}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space){e.Handled=true;e.SuppressKeyPress=true;OnClick(EventArgs.Empty);}base.OnKeyDown(e);}
 protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
 protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
 protected override void OnParentBackColorChanged(EventArgs e){base.OnParentBackColorChanged(e);Invalidate();}
 protected override void OnEnabledChanged(EventArgs e){base.OnEnabledChanged(e);Invalidate();}
 protected override void OnPaint(PaintEventArgs e){
  e.Graphics.Clear(Parent==null?Theme.Card:Parent.BackColor);
  float scale=Math.Min(ClientSize.Width/36f,ClientSize.Height/22f),x=(ClientSize.Width-36*scale)/2,y=(ClientSize.Height-22*scale)/2;
  e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  var track=new Rectangle((int)(x+scale),(int)(y+4*scale),Math.Max(14,(int)(31*scale)),Math.Max(2,(int)(14*scale)));
  using(var p=Theme.Path(track,Math.Min(track.Width,track.Height)))using(var b=new SolidBrush(Enabled&&Checked?Theme.Purple:Color.FromArgb(115,117,132)))e.Graphics.FillPath(b,p);
  using(var b=new SolidBrush(Enabled?Color.White:Theme.Muted))e.Graphics.FillEllipse(b,x+(Checked?18:1)*scale,y+3*scale,16*scale,16*scale);
  if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,Rectangle.Inflate(ClientRectangle,-1,-1),Theme.Muted,Parent==null?Theme.Card:Parent.BackColor);
 }
}

}
