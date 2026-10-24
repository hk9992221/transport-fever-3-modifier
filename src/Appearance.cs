using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TransportFever3Trainer
{
    class WindowChrome : Form
    {
        [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
        protected virtual Size MinimumLogicalSize { get { return new Size(340, 180); } }
        protected virtual Size FixedClientLayout { get { return Size.Empty; } }
        public WindowChrome()
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Back; Font = Theme.F(); DoubleBuffered = true;
            Icon = AppAssets.ReadIcon(); StartPosition = FormStartPosition.CenterScreen;
        }
        public void FitToScreen()
        {
            var area = Screen.FromControl(this).WorkingArea;
            int margin = Ui.Px(this, 12), maxWidth = Math.Max(240, area.Width - margin * 2), maxHeight = Math.Max(180, area.Height - margin * 2);
            if (!FixedClientLayout.IsEmpty)
            {
                MinimumSize = Size.Empty;
                var frame = new Size(Width - ClientSize.Width, Height - ClientSize.Height);
                ClientSize = Ui.FitFixedClient(new Size(Ui.Px(this, FixedClientLayout.Width), Ui.Px(this, FixedClientLayout.Height)),
                    new Size(maxWidth - frame.Width, maxHeight - frame.Height));
                MinimumSize = Size;
            }
            else MinimumSize = new Size(Math.Min(Ui.Px(this, MinimumLogicalSize.Width), maxWidth), Math.Min(Ui.Px(this, MinimumLogicalSize.Height), maxHeight));
            int width = Math.Min(Width, maxWidth), height = Math.Min(Height, maxHeight);
            SetBounds(Math.Max(area.Left, Math.Min(Left, area.Right - width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - height)), width, height);
        }
        protected override void OnShown(EventArgs e) { FitToScreen(); base.OnShown(e); PerformLayout(); }
        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            if (IsHandleCreated) BeginInvoke((Action)delegate { if (!IsDisposed) { FitToScreen(); PerformLayout(); } });
        }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x7E && IsHandleCreated && !FixedClientLayout.IsEmpty)
                BeginInvoke((Action)delegate { if (!IsDisposed) { FitToScreen(); PerformLayout(); } });
        }
        public void SaveWindowImage(string path)
        {
            using (var bitmap = new Bitmap(Width, Height))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    var dc = graphics.GetHdc();
                    try { if (!PrintWindow(Handle, dc, 0)) throw new InvalidOperationException("窗口预览生成失败"); }
                    finally { graphics.ReleaseHdc(dc); }
                }
                bitmap.Save(path);
            }
        }
    }
}
