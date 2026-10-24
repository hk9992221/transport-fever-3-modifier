using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace TransportFever3Trainer {
sealed class ThemedComboBox:ComboBox {
 public ThemedComboBox(){DrawMode=DrawMode.OwnerDrawFixed;DropDownStyle=ComboBoxStyle.DropDownList;FlatStyle=FlatStyle.Flat;BackColor=Theme.Input;ForeColor=Theme.Ink;Font=Theme.F(10);ItemHeight=Ui.Px(this,24);}
 protected override void OnDpiChangedAfterParent(EventArgs e){base.OnDpiChangedAfterParent(e);ItemHeight=Ui.Px(this,24);}
 protected override void OnDrawItem(DrawItemEventArgs e){if(e.Index<0)return;Color back=(e.State&DrawItemState.Selected)!=0?Theme.AccentSoft:Theme.Input;using(var brush=new SolidBrush(back))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,GetItemText(Items[e.Index]),Font,new Rectangle(e.Bounds.X+Ui.Px(this,5),e.Bounds.Y,Math.Max(1,e.Bounds.Width-Ui.Px(this,10)),e.Bounds.Height),Enabled?Theme.Ink:Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
 protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0xF&&IsHandleCreated){using(var g=Graphics.FromHwnd(Handle)){int x=ClientSize.Width-Ui.Px(this,19);var box=new Rectangle(x,1,Ui.Px(this,18),ClientSize.Height-2);using(var b=new SolidBrush(Theme.Input))g.FillRectangle(b,box);using(var p=new Pen(Theme.Line))g.DrawRectangle(p,0,0,ClientSize.Width-1,ClientSize.Height-1);int cy=ClientSize.Height/2;using(var p=new Pen(Enabled?Theme.Muted:Theme.Line,1.2f))g.DrawLines(p,new[]{new Point(x+Ui.Px(this,5),cy-Ui.Px(this,2)),new Point(x+Ui.Px(this,9),cy+Ui.Px(this,2)),new Point(x+Ui.Px(this,13),cy-Ui.Px(this,2))});}}}
}
}
