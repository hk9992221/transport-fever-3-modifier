using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace TransportFever3Trainer
{
    static class Ui
    {
        // Bounds are already laid out in monitor pixels. Remember logical point
        // sizes before handle creation so reparenting / DPI transitions cannot
        // leave a page scaled twice. All fonts follow the root monitor together.
        internal sealed class FontSnapshot : IDisposable
        {
            readonly Control root;
            readonly int initialDpi;
            readonly Dictionary<Control, Font> fonts = new Dictionary<Control, Font>();
            readonly Dictionary<int, Dictionary<Font, Font>> scaled = new Dictionary<int, Dictionary<Font, Font>>();
            internal FontSnapshot(Control root) { this.root = root; initialDpi = root.DeviceDpi; Capture(root); }
            void Capture(Control control)
            {
                fonts[control] = control.Font;
                if (control is NumericUpDown || control is ComboBox) return;
                foreach (Control child in control.Controls) Capture(child);
            }
            internal void Restore()
            {
                int dpi = root.DeviceDpi; float factor = dpi / (float)Math.Max(1, initialDpi);
                foreach (var entry in fonts)
                {
                    var control = entry.Key; if (control.IsDisposed) continue;
                    var font = control.Font; var logical = entry.Value;
                    if (Math.Abs(font.SizeInPoints - logical.SizeInPoints * factor) <= .01f && font.Style == logical.Style && font.FontFamily.Name == logical.FontFamily.Name) continue;
                    Font expected = logical;
                    if (dpi != initialDpi)
                    {
                        Dictionary<Font, Font> cache;
                        if (!scaled.TryGetValue(dpi, out cache)) { cache = new Dictionary<Font, Font>(); scaled[dpi] = cache; }
                        if (!cache.TryGetValue(logical, out expected)) { expected = new Font(logical.FontFamily, logical.Size * factor, logical.Style, logical.Unit); cache[logical] = expected; }
                    }
                    control.Font = expected;
                }
            }
            public void Dispose() { foreach (var cache in scaled.Values) foreach (var font in cache.Values) font.Dispose(); scaled.Clear(); }
        }
        // The offline layout check can exercise the sizing rules at different scales.
        // Normal runs always use the current control's monitor DPI.
        internal static float LayoutTestScale;
        internal static float Scale(Control c)
        {
            if (LayoutTestScale > 0) return LayoutTestScale;
            // Nested controls may still report their creation DPI after they
            // are reparented. Use the top-level window for the whole layout.
            var window = c as Form ?? c.FindForm();
            return Math.Max(1f, (window == null ? c.DeviceDpi : window.DeviceDpi) / 96f);
        }
        internal static int Px(Control c, int value) { return (int)Math.Round(value * Scale(c)); }
        internal static Size FitFixedClient(Size preferred, Size maximum)
        {
            int units = Math.Max(1, Math.Min(Math.Min(preferred.Width / 16, preferred.Height / 10), Math.Min(maximum.Width / 16, maximum.Height / 10)));
            return new Size(units * 16, units * 10);
        }
        internal static Rectangle AspectBounds(Rectangle area)
        {
            if (area.Width <= 0 || area.Height <= 0) return new Rectangle(area.Location, Size.Empty);
            int width = area.Width, height = Math.Max(1, (int)Math.Floor(width / 1.6));
            if (height > area.Height) { height = area.Height; width = Math.Max(1, (int)Math.Floor(height * 1.6)); }
            return new Rectangle(area.Left + (area.Width - width) / 2, area.Top + (area.Height - height) / 2, width, height);
        }
        internal static Size MinimumAspectClient(Size required, Size available)
        {
            int units = Math.Max((required.Width + 15) / 16, (required.Height + 9) / 10);
            units = Math.Max(1, Math.Min(units, Math.Min(available.Width / 16, available.Height / 10)));
            return new Size(units * 16, units * 10);
        }
        internal static Rectangle ResizeAspectBounds(Rectangle proposed, int edge, Size frame, Size current, Size minimum, Size maximum)
        {
            int width = Math.Max(1, proposed.Width - frame.Width), height = Math.Max(1, proposed.Height - frame.Height);
            bool byWidth = edge == 1 || edge == 2 || (edge != 3 && edge != 6 &&
                Math.Abs(width - current.Width) / (double)Math.Max(1, current.Width) >= Math.Abs(height - current.Height) / (double)Math.Max(1, current.Height));
            double wanted = byWidth ? width : height * 1.6;
            int lower = Math.Max(minimum.Width, (int)Math.Ceiling(minimum.Height * 1.6));
            int upper = Math.Max(1, Math.Min(maximum.Width, (int)Math.Floor(maximum.Height * 1.6)));
            width = Math.Max(1, (int)Math.Round(Math.Max(Math.Min(lower, upper), Math.Min(wanted, upper))));
            height = Math.Max(1, (int)Math.Round(width / 1.6));
            width += frame.Width; height += frame.Height;
            bool left = edge == 1 || edge == 4 || edge == 7, top = edge == 3 || edge == 4 || edge == 5;
            return new Rectangle(left ? proposed.Right - width : proposed.Left, top ? proposed.Bottom - height : proposed.Top, width, height);
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
            return Math.Max(1, Math.Min(3, (width + Px(parent, 12)) / (Px(parent, 320) + Px(parent, 12))));
        }
    }
}
