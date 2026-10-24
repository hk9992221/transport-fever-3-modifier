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
        protected virtual Size PreferredClientLayout { get { return Size.Empty; } }
        bool aspectInitialized;
        [StructLayout(LayoutKind.Sequential)]
        struct NativeRect { public int Left, Top, Right, Bottom; }
        public WindowChrome()
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Theme.Back; Font = Theme.F(); DoubleBuffered = true;
            Icon = AppAssets.ReadIcon(); StartPosition = FormStartPosition.CenterScreen;
        }
        public void FitToScreen()
        {
            if (WindowState != FormWindowState.Normal) return;
            var area = Screen.FromControl(this).WorkingArea;
            int margin = Ui.Px(this, 12), maxWidth = Math.Max(240, area.Width - margin * 2), maxHeight = Math.Max(180, area.Height - margin * 2);
            if (!PreferredClientLayout.IsEmpty)
            {
                MinimumSize = Size.Empty;
                var frame = new Size(Width - ClientSize.Width, Height - ClientSize.Height);
                var available = new Size(maxWidth - frame.Width, maxHeight - frame.Height);
                var preferred = aspectInitialized ? ClientSize : new Size(Ui.Px(this, PreferredClientLayout.Width), Ui.Px(this, PreferredClientLayout.Height));
                ClientSize = Ui.FitFixedClient(preferred, available); aspectInitialized = true;
                var minimum = Ui.MinimumAspectClient(new Size(Ui.Px(this, MinimumLogicalSize.Width), Ui.Px(this, MinimumLogicalSize.Height)), available);
                MinimumSize = new Size(minimum.Width + frame.Width, minimum.Height + frame.Height);
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
            if (message.Msg == 0x214 && message.LParam != IntPtr.Zero && WindowState == FormWindowState.Normal && !PreferredClientLayout.IsEmpty)
            {
                var native = (NativeRect)Marshal.PtrToStructure(message.LParam, typeof(NativeRect));
                var proposed = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
                var frame = new Size(Width - ClientSize.Width, Height - ClientSize.Height);
                var screen = Screen.FromRectangle(proposed).WorkingArea;
                var result = Ui.ResizeAspectBounds(proposed, message.WParam.ToInt32(), frame, ClientSize,
                    new Size(Math.Max(1, MinimumSize.Width - frame.Width), Math.Max(1, MinimumSize.Height - frame.Height)),
                    new Size(screen.Width - frame.Width, screen.Height - frame.Height));
                Marshal.StructureToPtr(new NativeRect { Left = result.Left, Top = result.Top, Right = result.Right, Bottom = result.Bottom }, message.LParam, false);
                message.Result = new IntPtr(1); return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0x7E && IsHandleCreated && !PreferredClientLayout.IsEmpty)
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
