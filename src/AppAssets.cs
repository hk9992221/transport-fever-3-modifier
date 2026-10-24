using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Windows.Forms;

namespace TransportFever3Trainer
{
    static class AppAssets
    {
        public static readonly Bitmap Artwork = ReadArtwork();
        static Bitmap ReadArtwork()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TransportFever3Trainer.App.png"))
            using (var image = Image.FromStream(stream)) return new Bitmap(image);
        }
        public static Icon ReadIcon()
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TransportFever3Trainer.App.ico"))
            using (var icon = new Icon(stream)) return (Icon)icon.Clone();
        }
    }
    class AppMark : Control
    {
        public AppMark() { AccessibleName = "交通工具图标"; TabStop = false; SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Card : Parent.BackColor);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.DrawImage(AppAssets.Artwork, ClientRectangle);
        }
    }
}
