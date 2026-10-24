using System;
using System.Drawing;
using System.Windows.Forms;

namespace TransportFever3Trainer
{
    static class Ui
    {
        // The offline layout check can exercise the sizing rules at different scales.
        // Normal runs always use the current control's monitor DPI.
        internal static float LayoutTestScale;
        internal static float Scale(Control c)
        {
            return LayoutTestScale > 0 ? LayoutTestScale : Math.Max(1f, c.DeviceDpi / 96f);
        }
        internal static int Px(Control c, int value) { return (int)Math.Round(value * Scale(c)); }
        internal static Size FitFixedClient(Size preferred, Size maximum)
        {
            int units = Math.Max(1, Math.Min(Math.Min(preferred.Width / 16, preferred.Height / 10), Math.Min(maximum.Width / 16, maximum.Height / 10)));
            return new Size(units * 16, units * 10);
        }
        internal static int ButtonWidth(Control button, int logicalMinimum)
        { return Math.Max(Px(button, logicalMinimum), TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width + Px(button, 26)); }
        internal static int ButtonHeight(Control button, int logicalMinimum)
        { return Math.Max(Px(button, logicalMinimum), TextRenderer.MeasureText(button.Text, button.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Height + Px(button, 10)); }
        internal static int WrappedHeight(Control label, int width, int logicalMinimum)
        { return Math.Max(Px(label, logicalMinimum), TextRenderer.MeasureText(label.Text, label.Font, new Size(Math.Max(1, width), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl).Height + Px(label, 4)); }
        internal static void Bounds(Control c, int x, int y, int w, int h)
        {
            c.SetBounds(x, y, Math.Max(0, w), Math.Max(0, h));
        }
        internal static int FlowHeight(FlowLayoutPanel panel, int availableWidth, int buttonHeight)
        {
            int used = 0, rows = 1;
            foreach (Control c in panel.Controls)
            {
                int next = c.Width + c.Margin.Horizontal;
                if (used > 0 && used + next > availableWidth) { rows++; used = 0; }
                used += next;
            }
            return rows * buttonHeight;
        }
        internal static int FeatureColumns(Control parent, int width)
        {
            return Math.Max(1, Math.Min(3, (width + Px(parent, 16)) / (Px(parent, 440) + Px(parent, 16))));
        }
    }
}
