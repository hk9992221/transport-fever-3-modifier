using System;using System.Drawing;using System.Drawing.Drawing2D;using System.IO;using System.Reflection;using System.Windows.Forms;
namespace TransportFever3Trainer {
static class BrandAssets {
 public const string PurchaseUrl="https://4i11l86pf48p1svunz05hyl6ochkgu6.taobao.com/category.htm?appUid=RAzN8HWW3WKEm6edPuN8LZSZyifmRwEpGuA261AYyzTdcVkpPhw&topIds=1085589283325&shopSearchKeyword=%E9%80%9F%E6%99%BA%E7%94%B5%E7%8E%A9&spm=a21n57.1.hoverItem.2";
 public static readonly Bitmap Artwork=ReadArtwork();
 static Bitmap ReadArtwork(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TransportFever3Trainer.Brand.png"))using(var image=Image.FromStream(stream))return new Bitmap(image);}
 public static Icon ReadIcon(){using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("TransportFever3Trainer.Brand.ico"))using(var icon=new Icon(stream))return (Icon)icon.Clone();}
}
class BrandMark:Control {
 public BrandMark(){TabStop=false;AccessibleRole=AccessibleRole.Graphic;AccessibleName="速智电玩图标";SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);}
 protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Parent==null?Theme.Back:Parent.BackColor);if(Width<2||Height<2)return;var state=e.Graphics.Save();e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;e.Graphics.PixelOffsetMode=PixelOffsetMode.HighQuality;using(var p=Theme.Path(new Rectangle(0,0,Width-1,Height-1),Math.Min(10,Math.Min(Width-1,Height-1)))){e.Graphics.SetClip(p);e.Graphics.DrawImage(BrandAssets.Artwork,ClientRectangle);}e.Graphics.Restore(state);}
 protected override void WndProc(ref Message m){if(m.Msg==0x84){m.Result=new IntPtr(-1);return;}base.WndProc(ref m);}
}
}
