using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TransportFever3Trainer
{
    static class Theme
    {
        public static readonly Color Back = Color.FromArgb(244, 247, 251);
        public static readonly Color Panel = Color.White;
        public static readonly Color Card = Color.White;
        public static readonly Color Input = Color.FromArgb(241, 245, 249);
        public static readonly Color Purple = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentSoft = Color.FromArgb(234, 241, 255);
        public static readonly Color Line = Color.FromArgb(220, 229, 239);
        public static readonly Color Ink = Color.FromArgb(23, 43, 69);
        public static readonly Color Muted = Color.FromArgb(97, 117, 138);
        public static readonly Color Green = Color.FromArgb(22, 124, 98);
        public static readonly Color Alternate = Color.FromArgb(248, 250, 252);
        public static Font F(float size = 10, bool bold = false)
        { return new Font("Microsoft YaHei UI", size, bold ? FontStyle.Bold : FontStyle.Regular); }
        public static GraphicsPath Path(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            if (r.Width < 2 || r.Height < 2) { path.AddRectangle(r); return path; }
            int d = Math.Max(1, Math.Min(radius, Math.Min(r.Width, r.Height)));
            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure(); return path;
        }
    }
    class Surface : Panel
    {
        public int Radius = 14;
        public Surface() { DoubleBuffered = true; BackColor = Theme.Card; }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e); if (Width < 2 || Height < 2) return;
            e.Graphics.Clear(Parent == null ? Theme.Back : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = Theme.Path(new Rectangle(0, 0, Width - 1, Height - 1), Ui.Px(this, Radius)))
            using (var brush = new SolidBrush(BackColor))
            using (var pen = new Pen(Theme.Line))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path); }
        }
    }
    class PillButton : Control, IButtonControl
    {
        bool primary, hovered, pressed, spaceDown, isDefault;
        public bool Primary { get { return primary; } set { if (primary != value) { primary = value; Invalidate(); } } }
        public DialogResult DialogResult { get; set; }
        public PillButton()
        {
            BackColor = Theme.Card; ForeColor = Theme.Ink; Font = Theme.F(10, true);
            Cursor = Cursors.Hand; Height = 36; TabStop = true; AccessibleRole = AccessibleRole.PushButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Opaque | ControlStyles.Selectable | ControlStyles.StandardClick, true);
            SetStyle(ControlStyles.StandardDoubleClick, false);
        }
        public void NotifyDefault(bool value) { isDefault = value; Invalidate(); }
        public void PerformClick() { if (CanSelect) OnClick(EventArgs.Empty); }
        protected override void OnClick(EventArgs e)
        {
            if (!Enabled) return; Focus(); var form = FindForm();
            if (form != null && DialogResult != DialogResult.None) form.DialogResult = DialogResult;
            base.OnClick(e);
        }
        protected override bool IsInputKey(Keys key) { return (key & Keys.KeyCode) == Keys.Space || base.IsInputKey(key); }
        protected override bool ProcessDialogKey(Keys key)
        { if (key == Keys.Enter && CanSelect) { PerformClick(); return true; } return base.ProcessDialogKey(key); }
        protected override bool ProcessMnemonic(char key)
        { if (CanSelect && IsMnemonic(key, Text)) { Select(); PerformClick(); return true; } return base.ProcessMnemonic(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (e.KeyCode == Keys.Space) { spaceDown = pressed = true; Invalidate(); e.Handled = e.SuppressKeyPress = true; } base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e)
        { if (e.KeyCode == Keys.Space && spaceDown) { spaceDown = pressed = false; Invalidate(); e.Handled = true; PerformClick(); } base.OnKeyUp(e); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { pressed = false; Invalidate(); base.OnMouseCaptureChanged(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { spaceDown = pressed = false; base.OnLostFocus(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { hovered = pressed = spaceDown = false; base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Back : Parent.BackColor); if (Width < 2 || Height < 2) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = !Enabled ? Theme.Input : Primary ? Theme.Purple : hovered ? Theme.AccentSoft : BackColor;
            if (Enabled && pressed) fill = Primary ? ControlPaint.Dark(fill, .08f) : Theme.Line;
            using (var path = Theme.Path(new Rectangle(0, 0, Width - 1, Height - 1), Ui.Px(this, 12)))
            using (var brush = new SolidBrush(fill))
            using (var border = new Pen(Primary && Enabled ? Theme.Purple : Theme.Line))
            { e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(border, path); }
            TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -Ui.Px(this, 7), 0),
                !Enabled ? Theme.Muted : Primary ? Color.White : ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (Enabled && ((Focused && ShowFocusCues) || isDefault) && Width > 10 && Height > 10)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -4, -4));
        }
    }
    class Switch : Control
    {
        bool isChecked; public event EventHandler CheckedChanged;
        public bool Checked { get { return isChecked; } set { if (isChecked == value) return; isChecked = value; Invalidate(); if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty); } }
        public Switch()
        {
            Size = new Size(40, 24); Cursor = Cursors.Hand; TabStop = true; AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Opaque | ControlStyles.Selectable, true);
        }
        protected override void OnClick(EventArgs e) { if (Enabled) { Focus(); Checked = !Checked; } base.OnClick(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { e.Handled = e.SuppressKeyPress = true; OnClick(EventArgs.Empty); } base.OnKeyDown(e); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent == null ? Theme.Card : Parent.BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var track = new Rectangle(1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
            using (var path = Theme.Path(track, track.Height))
            using (var brush = new SolidBrush(Enabled && Checked ? Theme.Purple : Color.FromArgb(191, 205, 221))) e.Graphics.FillPath(brush, path);
            int d = Math.Max(1, Height - 7), x = Checked ? Width - d - 4 : 3;
            using (var brush = new SolidBrush(Color.White)) e.Graphics.FillEllipse(brush, x, 3, d, d);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -1, -1));
        }
    }
}
