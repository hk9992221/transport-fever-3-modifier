using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TransportFever3Trainer
{
    sealed class CameraPage : Panel
    {
        readonly MainForm form;
        readonly Panel canvas = new Panel { Tag = "scroll-content" };
        readonly Surface cameraCard = new Surface(), bookmarkCard = new Surface();
        readonly Dictionary<string, NumericUpDown> numbers = new Dictionary<string, NumericUpDown>();
        readonly Dictionary<string, Label> labels = new Dictionary<string, Label>();
        readonly Label cameraTitle, bookmarkTitle, cameraNote, bookmarkNote;
        readonly TextBox bookmarkName;
        readonly ThemedComboBox bookmarks;
        readonly PillButton read, apply, back, save, jump, remove;
        bool loaded, arranging;
        string lastWorld = "";
        int P(int value) { return Ui.Px(this, value); }

        public CameraPage(MainForm parent)
        {
            form = parent; AutoScroll = true; BackColor = Theme.Back; canvas.BackColor = Theme.Back;
            Controls.Add(canvas); canvas.Controls.Add(cameraCard); canvas.Controls.Add(bookmarkCard);
            cameraTitle = MainForm.LabelFor("镜头位置", 17, true); cameraCard.Controls.Add(cameraTitle);
            bookmarkTitle = MainForm.LabelFor("我的镜头书签", 17, true); bookmarkCard.Controls.Add(bookmarkTitle);
            string[] keys = { "x", "y", "z", "w", "v" }, text = { "地图 X", "地图 Y", "镜头距离", "旋转角度", "俯仰角度" };
            for (int i = 0; i < keys.Length; i++)
            {
                var label = MainForm.LabelFor(text[i], 11); labels[keys[i]] = label; cameraCard.Controls.Add(label);
                var number = new ThemedNumericUpDown { Minimum = i == 2 ? 1 : i == 4 ? .01m : i == 3 ? -3.142m : -100000,
                    Maximum = i == 4 ? 1.56m : i == 3 ? 3.142m : 100000, Value = i == 2 ? 500 : i == 4 ? 1 : 0,
                    DecimalPlaces = 3, Increment = 1, Font = Theme.F(11), TextAlign = HorizontalAlignment.Center, Name = "camera-" + keys[i] };
                numbers[keys[i]] = number; cameraCard.Controls.Add(number);
            }
            read = Button(cameraCard, "读取当前镜头", delegate { LoadValues(); });
            apply = Button(cameraCard, "应用镜头位置", delegate { MoveCamera(numbers.ToDictionary(x => x.Key, x => (object)x.Value.Value)); });
            back = Button(cameraCard, "返回上一镜头", delegate { form.Request(new { op = "return" }); });
            cameraNote = MainForm.LabelFor("可通过世界管理的“镜头定位”快速找到城镇与产业。", 10); cameraNote.ForeColor = Theme.Muted; cameraCard.Controls.Add(cameraNote);
            bookmarkName = new TextBox { Font = Theme.F(11), BackColor = Theme.Input, ForeColor = Theme.Ink, Text = "我的位置", MaxLength = 80,
                AccessibleName = "镜头书签名称", Name = "bookmark-name" }; bookmarkCard.Controls.Add(bookmarkName);
            save = Button(bookmarkCard, "保存当前镜头", delegate {
                var current = Data.Obj(Data.Get(form.State, "camera")); if (current.Count == 0) return;
                var mark = new Dictionary<string, object>(current); mark["name"] = bookmarkName.Text.Trim().Length > 0 ? bookmarkName.Text.Trim() : "我的位置";
                form.Prefs.bookmarks.Add(mark); form.SavePrefs(); Fill();
            });
            bookmarks = new ThemedComboBox { Font = Theme.F(11), AccessibleName = "已保存的镜头书签", Name = "bookmark-list" };
            bookmarkCard.Controls.Add(bookmarks);
            jump = Button(bookmarkCard, "前往所选书签", delegate { if (bookmarks.SelectedIndex >= 0) MoveCamera(form.Prefs.bookmarks[bookmarks.SelectedIndex]); });
            remove = Button(bookmarkCard, "删除所选书签", delegate { if (bookmarks.SelectedIndex >= 0) { form.Prefs.bookmarks.RemoveAt(bookmarks.SelectedIndex); form.SavePrefs(); Fill(); } });
            bookmarkNote = MainForm.LabelFor("书签保存在本机“配置”目录，重新打开仍保留。不同地图的坐标不通用。", 10);
            bookmarkNote.ForeColor = Theme.Muted; bookmarkCard.Controls.Add(bookmarkNote);
            SizeChanged += delegate { Arrange(); }; Fill();
        }
        PillButton Button(Control parent, string text, Action callback)
        {
            var button = new PillButton { Text = text, Name = "camera-action-" + parent.Controls.Count };
            button.Click += delegate { callback(); }; parent.Controls.Add(button); return button;
        }
        public void Arrange()
        {
            if (arranging || cameraTitle == null) return; arranging = true;
            var old = AutoScrollPosition;
            try
            {
                bool stack = Width < P(900); int gap = P(16), width = Math.Max(1, ClientSize.Width - P(19));
                int cardWidth = stack ? width : (width - gap) / 2, cardHeight = P(600);
                cameraCard.SetBounds(0, P(4), cardWidth, cardHeight);
                bookmarkCard.SetBounds(stack ? 0 : cardWidth + gap, stack ? cardHeight + gap + P(4) : P(4), stack ? cardWidth : width - cardWidth - gap, cardHeight);
                int pad = P(22), inner = Math.Max(1, cardWidth - pad * 2), labelWidth = inner / 2;
                cameraTitle.SetBounds(pad, P(18), inner, P(42));
                int y = P(82);
                foreach (var pair in numbers)
                {
                    labels[pair.Key].SetBounds(pad, y, labelWidth - P(10), P(32));
                    pair.Value.SetBounds(pad + labelWidth, y, inner - labelWidth, P(34)); y += P(52);
                }
                int half = (inner - P(10)) / 2;
                read.SetBounds(pad, y + P(4), half, P(36)); apply.SetBounds(pad + half + P(10), y + P(4), inner - half - P(10), P(36));
                back.SetBounds(pad, y + P(50), inner, P(36));
                cameraNote.SetBounds(pad, y + P(102), inner, P(80));
                int markInner = Math.Max(1, bookmarkCard.Width - pad * 2);
                bookmarkTitle.SetBounds(pad, P(18), markInner, P(42));
                bookmarkName.SetBounds(pad, P(88), markInner, P(34));
                save.SetBounds(pad, P(138), markInner, P(36));
                bookmarks.SetBounds(pad, P(216), markInner, P(34));
                jump.SetBounds(pad, P(268), markInner, P(36));
                remove.SetBounds(pad, P(316), markInner, P(36));
                bookmarkNote.SetBounds(pad, P(382), markInner, P(90));
                int height = stack ? cardHeight * 2 + gap + P(10) : cardHeight + P(10);
                canvas.SetBounds(0, 0, width, height); AutoScrollMinSize = new Size(0, height);
                AutoScrollPosition = new Point(-old.X, -old.Y);
            }
            finally { arranging = false; }
        }
        void MoveCamera(Dictionary<string, object> values)
        { form.Request(new { op = "camera", x = Data.Num(values, "x"), y = Data.Num(values, "y"), z = Data.Num(values, "z", 500), w = Data.Num(values, "w"), v = Data.Num(values, "v", 1) }); }
        void Fill()
        {
            bookmarks.Items.Clear(); foreach (var mark in form.Prefs.bookmarks) bookmarks.Items.Add(Data.Str(mark, "name", "我的位置"));
            if (bookmarks.Items.Count > 0) bookmarks.SelectedIndex = 0;
        }
        void LoadValues()
        {
            var current = Data.Obj(Data.Get(form.State, "camera")); if (current.Count == 0) return;
            foreach (var pair in numbers) pair.Value.Value = Math.Max(pair.Value.Minimum, Math.Min(pair.Value.Maximum, (decimal)Data.Num(current, pair.Key)));
            loaded = true;
        }
        public void UpdateState()
        {
            bool online = form.CaptureReady && !form.Busy;
            string world = form.NameScope; if (world != lastWorld) { loaded = false; lastWorld = world; }
            MainForm.Enable(read, online && form.IsAvailable("camera")); MainForm.Enable(apply, online && form.IsAvailable("camera"));
            MainForm.Enable(save, online && form.IsAvailable("camera")); MainForm.Enable(back, online && form.IsAvailable("return"));
            MainForm.Enable(jump, online && bookmarks.Items.Count > 0 && form.IsAvailable("camera")); MainForm.Enable(remove, bookmarks.Items.Count > 0);
            if (!loaded && form.CaptureReady) LoadValues();
        }
    }
}
