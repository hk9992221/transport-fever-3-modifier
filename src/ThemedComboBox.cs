using System;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;
namespace TransportFever3Trainer {
sealed class ThemedComboBox:ComboBox {
 public ThemedComboBox(){DrawMode=DrawMode.OwnerDrawFixed;DropDownStyle=ComboBoxStyle.DropDownList;FlatStyle=FlatStyle.Flat;BackColor=Theme.Input;ForeColor=Theme.Ink;Font=Theme.F(10);ItemHeight=24;}
 protected override void OnDrawItem(DrawItemEventArgs e){if(e.Index<0)return;Color back=(e.State&DrawItemState.Selected)!=0?Theme.Purple:Theme.Input;using(var brush=new SolidBrush(back))e.Graphics.FillRectangle(brush,e.Bounds);TextRenderer.DrawText(e.Graphics,GetItemText(Items[e.Index]),Font,new Rectangle(e.Bounds.X+5,e.Bounds.Y,e.Bounds.Width-10,e.Bounds.Height),Enabled?Theme.Ink:Theme.Muted,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);}
 protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==0xF&&IsHandleCreated){using(var g=Graphics.FromHwnd(Handle)){int x=ClientSize.Width-19;var box=new Rectangle(x,1,18,ClientSize.Height-2);using(var b=new SolidBrush(Theme.Input))g.FillRectangle(b,box);using(var p=new Pen(Theme.Line))g.DrawRectangle(p,0,0,ClientSize.Width-1,ClientSize.Height-1);int cy=ClientSize.Height/2;using(var p=new Pen(Enabled?Theme.Muted:Theme.Line,1.2f))g.DrawLines(p,new[]{new Point(x+5,cy-2),new Point(x+9,cy+2),new Point(x+13,cy-2)});}}}
}
}
