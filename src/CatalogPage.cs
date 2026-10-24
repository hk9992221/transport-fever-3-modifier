using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace TransportFever3Trainer
{
    sealed class CatalogPage : Panel
    {
        readonly MainForm form;
        public readonly string Kind;
        readonly TextBox search, nameEditor;
        readonly FlowLayoutPanel filters;
        readonly Panel body, detailViewport, detailContent;
        readonly ListView list;
        readonly ImageList rowImages = new ImageList();
        readonly Surface detail;
        readonly Label title, description, selection, footnote, nameLabel, nameHint;
        readonly PillButton act, favorite, focus, back, refresh, rename, localName;
        readonly ThemedComboBox targets;
        readonly Dictionary<string, Label> numberLabels = new Dictionary<string, Label>();
        public readonly Dictionary<string, NumericUpDown> Numbers = new Dictionary<string, NumericUpDown>();
        List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
        Dictionary<string, object> selected;
        string filter = "全部", signature = "", stockSignature = "", renameRequest, lastScope = "";
        readonly Stopwatch renameWatch = new Stopwatch();
        bool populating, arranging;
        int contentHeight;
        int P(int value) { return Ui.Px(this, value); }

        public CatalogPage(MainForm parent, string kind)
        {
            form = parent; Kind = kind; BackColor = Theme.Back;
            search = new TextBox { Font = Theme.F(11), BackColor = Theme.Card, ForeColor = Theme.Ink, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "搜索名称或描述", Name = "catalog-search" };
            search.TextChanged += delegate { Refill(); };
            Controls.Add(search);
            filters = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Back };
            Controls.Add(filters);
            string[] names = kind == "cargo" ? new[] { "全部", "货物", "收藏", "最近使用" } : new[] { "全部", "城镇", "产业", "收藏", "最近使用" };
            foreach (string text in names)
            {
                string value = text;
                var button = new PillButton { Text = value, Primary = value == filter, Tag = value, Name = "catalog-filter" };
                filters.Controls.Add(button);
                button.Click += delegate { filter = value; foreach (PillButton b in filters.Controls) b.Primary = b.Text == filter; Refill(); };
            }
            refresh = new PillButton { Text = "刷新目录", Name = "catalog-refresh" };
            refresh.Click += delegate { form.Request(new { op = "refresh" }); };
            filters.Controls.Add(refresh);
            body = new Panel { BackColor = Theme.Back };
            Controls.Add(body);
            list = new ListView { View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true,
                HideSelection = false, MultiSelect = false, CheckBoxes = kind == "cargo", BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Card, ForeColor = Theme.Ink, Font = Theme.F(11), OwnerDraw = true, ShowItemToolTips = true, Name = "catalog-list" };
            list.Columns.Add("名称", 320); list.Columns.Add("类型", 90);
            rowImages.ImageSize = new Size(1, 42); list.SmallImageList = rowImages;
            list.DrawItem += delegate(object sender, DrawListViewItemEventArgs e) { e.DrawDefault = false; };
            list.DrawSubItem += DrawRow;
            list.SelectedIndexChanged += delegate { if (!populating) Choose(); };
            list.ItemChecked += delegate { if (!populating) { FillTargets(); UpdateActions(); } };
            body.Controls.Add(list);
            detail = new Surface { Padding = new Padding(12) }; body.Controls.Add(detail);
            detailViewport = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, AutoScroll = true };
            detail.Controls.Add(detailViewport);
            detailContent = new Panel { BackColor = Theme.Card, Tag = "scroll-content" }; detailViewport.Controls.Add(detailContent);
            title = Label("选择列表中的条目", 16, true); title.Name = "entity-title";
            description = Label("可搜索游戏名称或描述。", 10); description.ForeColor = Theme.Muted;
            selection = Label("", 9); selection.ForeColor = Theme.Muted;
            footnote = Label("", 9); footnote.ForeColor = Theme.Muted;
            act = Button(kind == "cargo" ? "添加所选货物" : "应用城镇容量", true, Execute);
            favorite = Button("收藏 / 取消收藏", false, delegate {
                if (selected == null) return;
                var saved = form.Prefs.Set(Kind, false); string id = Data.Str(selected, "id");
                if (saved.Contains(id)) saved.Remove(id); else saved.Add(id);
                form.SavePrefs(); Refill();
            });
            focus = Button("镜头定位", false, delegate { if (selected != null) form.Request(new { op = "focus", id = Data.Str(selected, "id") }); });
            back = Button("返回上一镜头", false, delegate { form.Request(new { op = "return" }); });
            nameLabel = Label("游戏名称", 10, true);
            nameEditor = new TextBox { Font = Theme.F(11), MaxLength = 80, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Input,
                ForeColor = Theme.Ink, AccessibleName = "游戏名称或自定义显示名称", Name = "entity-name" };
            nameEditor.TextChanged += delegate { UpdateActions(); };
            detailContent.Controls.Add(nameEditor);
            rename = Button("保存到游戏", true, SaveGameName); rename.Name = "save-game-name";
            localName = Button("保存显示名称", false, SaveLocalName); localName.Name = "save-display-name";
            nameHint = Label("游戏名称优先；读不到时可设置本工具的显示名称。", 9); nameHint.ForeColor = Theme.Muted;
            if (kind == "cargo")
            {
                AddNumber("count", "添加数量", 1, 10000, 1, 0);
                targets = new ThemedComboBox { Font = Theme.F(10), AccessibleName = "兼容的目标库存", Name = "cargo-target" };
                targets.SelectedIndexChanged += delegate { UpdateActions(); }; detailContent.Controls.Add(targets);
            }
            else
            {
                AddNumber("res", "住宅容量", 1, 100000, 500, 0);
                AddNumber("commercial", "商业容量", 1, 100000, 200, 0);
                AddNumber("industry", "工业容量", 1, 100000, 200, 0);
                AddNumber("productivity", "产业生产倍率", .05m, 100, 2, 2);
            }
            SizeChanged += delegate { Arrange(); };
            Disposed += delegate { rowImages.Dispose(); };
            Arrange(); UpdateActions();
        }
        Label Label(string text, float size, bool bold = false)
        { var label = MainForm.LabelFor(text, size, bold); detailContent.Controls.Add(label); return label; }
        PillButton Button(string text, bool primary, Action callback)
        {
            var button = new PillButton { Text = text, Primary = primary, Name = "catalog-" + detailContent.Controls.Count };
            button.Click += delegate { callback(); }; detailContent.Controls.Add(button); return button;
        }
        void AddNumber(string key, string text, decimal min, decimal max, decimal value, int digits)
        {
            Numbers[key] = new ThemedNumericUpDown { Minimum = min, Maximum = max, Value = value, DecimalPlaces = digits,
                Increment = digits == 0 ? 1 : .05m, Font = Theme.F(11), TextAlign = HorizontalAlignment.Center, Name = "number-" + key };
            numberLabels[key] = Label(text, 10); detailContent.Controls.Add(Numbers[key]);
        }
        void DrawRow(object sender, DrawListViewSubItemEventArgs e)
        {
            Color fill = e.Item.Selected ? Theme.AccentSoft : e.ItemIndex % 2 == 0 ? Theme.Card : Theme.Alternate;
            using (var brush = new SolidBrush(fill)) e.Graphics.FillRectangle(brush, e.Bounds);
            var rect = e.Bounds; rect.Inflate(-P(9), 0);
            if (e.ColumnIndex == 0 && list.CheckBoxes)
            {
                var box = new Rectangle(e.Bounds.Left + P(8), e.Bounds.Top + (e.Bounds.Height - P(14)) / 2, P(14), P(14));
                using (var brush = new SolidBrush(e.Item.Checked ? Theme.Purple : Theme.Card)) e.Graphics.FillRectangle(brush, box);
                using (var pen = new Pen(e.Item.Checked ? Theme.Purple : Theme.Muted)) e.Graphics.DrawRectangle(pen, box);
                if (e.Item.Checked) using (var pen = new Pen(Color.White, P(2))) e.Graphics.DrawLines(pen, new[] {
                    new Point(box.X + P(3), box.Y + P(7)), new Point(box.X + P(6), box.Y + P(10)), new Point(box.X + P(11), box.Y + P(4)) });
                rect.X = e.Bounds.X + P(30); rect.Width = Math.Max(0, e.Bounds.Width - P(34));
            }
            TextRenderer.DrawText(e.Graphics, e.SubItem.Text, list.Font, rect, e.ColumnIndex == 1 ? Theme.Muted : Theme.Ink,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
        public void Arrange()
        {
            if (arranging || body == null) return; arranging = true;
            var bodyOffset = body.AutoScrollPosition; var detailOffset = detailViewport.AutoScrollPosition;
            try
            {
                int w = ClientSize.Width, gap = P(14);
                search.SetBounds(0, P(4), w, P(32));
                foreach (PillButton button in filters.Controls)
                { button.Size = new Size(P(button.Text == "最近使用" ? 100 : 86), P(32)); button.Margin = new Padding(0, 0, P(7), P(7)); }
                filters.SetBounds(0, P(46), w, Ui.FlowHeight(filters, w, P(39)));
                int top = filters.Bottom + P(8); body.SetBounds(0, top, w, Math.Max(0, Height - top));
                bool stack = w < P(900);
                body.AutoScroll = stack;
                body.AutoScrollMinSize = Size.Empty;
                int canvasWidth = Math.Max(1, body.ClientSize.Width - (stack ? P(19) : 0));
                int left = stack ? canvasWidth : (int)((canvasWidth - gap) * .55);
                int detailWidth = stack ? canvasWidth : canvasWidth - left - gap;
                int listHeight = stack ? P(210) : body.ClientSize.Height;
                list.SetBounds(0, 0, Math.Max(1, left), Math.Max(1, listHeight));
                if (rowImages.ImageSize.Height != P(42)) rowImages.ImageSize = new Size(1, Math.Min(256, P(42)));
                list.Columns[1].Width = P(78); list.Columns[0].Width = Math.Max(P(120), list.ClientSize.Width - P(100));
                detail.Padding = new Padding(P(12));
                detail.SetBounds(stack ? 0 : left + gap, stack ? listHeight + gap : 0, Math.Max(1, detailWidth), Math.Max(1, body.ClientSize.Height));
                detailViewport.AutoScroll = !stack;
                int dw = Math.Max(1, detailWidth - P(24) - (!stack ? P(19) : 0));
                int pad = P(8), inner = Math.Max(1, dw - pad * 2);
                title.SetBounds(pad, P(6), inner, P(52));
                description.SetBounds(pad, P(66), inner, P(46));
                selection.SetBounds(pad, P(117), inner, P(24));
                int y = P(151);
                foreach (var pair in Numbers)
                {
                    bool show = Kind == "cargo" || selected == null || ((Data.Str(selected, "category") == "产业") == (pair.Key == "productivity"));
                    pair.Value.Visible = numberLabels[pair.Key].Visible = show;
                    if (!show) continue;
                    int inputWidth = Math.Min(P(155), inner / 2);
                    numberLabels[pair.Key].SetBounds(pad, y + P(3), Math.Max(1, inner - inputWidth - P(12)), P(28));
                    pair.Value.SetBounds(dw - pad - inputWidth, y, inputWidth, P(32)); y += P(45);
                }
                if (targets != null) { targets.SetBounds(pad, y, inner, P(34)); y += P(46); }
                act.SetBounds(pad, y, inner, P(36)); y += P(44);
                favorite.SetBounds(pad, y, inner, P(34)); y += P(42);
                focus.Visible = back.Visible = Kind == "world";
                if (Kind == "world")
                {
                    int half = (inner - P(8)) / 2;
                    focus.SetBounds(pad, y, half, P(34)); back.SetBounds(pad + half + P(8), y, inner - half - P(8), P(34)); y += P(50);
                }
                foreach (Control c in new Control[] { nameLabel, nameEditor, rename, localName, nameHint }) c.Visible = Kind == "world";
                if (Kind == "world")
                {
                    nameLabel.SetBounds(pad, y, inner, P(26)); y += P(31);
                    nameEditor.SetBounds(pad, y, inner, P(32)); y += P(42);
                    rename.SetBounds(pad, y, inner, P(36)); y += P(44);
                    localName.Visible = selected == null || Data.Str(selected, "gameName").Length == 0;
                    if (localName.Visible) { localName.SetBounds(pad, y, inner, P(34)); y += P(42); }
                    nameHint.SetBounds(pad, y, inner, P(56)); y += P(62);
                }
                footnote.SetBounds(pad, y, inner, P(62)); y += P(72);
                contentHeight = y; detailContent.SetBounds(0, 0, dw, y);
                detailViewport.AutoScrollMinSize = new Size(0, y);
                if (stack)
                { detail.Height = y + P(24); body.AutoScrollMinSize = new Size(0, listHeight + gap + detail.Height + P(4)); }
                body.AutoScrollPosition = new Point(-bodyOffset.X, -bodyOffset.Y);
                detailViewport.AutoScrollPosition = new Point(-detailOffset.X, -detailOffset.Y);
            }
            finally { arranging = false; }
        }
        public void UpdateRows(object data)
        {
            var next = Data.Rows(data); if (Kind == "cargo") next = next.Where(x => Data.Str(x, "id") != "0").ToList();
            string sig = Data.Json.Serialize(next) + form.DisplayNameRevision;
            if (sig == signature) return; signature = sig; rows = next; Refill();
        }
        public void InvalidateNames() { signature = ""; UpdateRows(Data.Get(form.State, Kind == "cargo" ? "catalog" : "worlds")); stockSignature = ""; UpdateStocks(); }
        public void UpdateStocks()
        {
            if (Kind != "cargo") return;
            string sig = Data.Json.Serialize(Data.Get(form.State, "stocks")) + form.DisplayNameRevision;
            if (sig == stockSignature) return; stockSignature = sig; FillTargets(); UpdateActions();
        }
        void Refill()
        {
            if (list == null) return;
            var checkedIds = new HashSet<string>(list.CheckedItems.Cast<ListViewItem>().Select(x => Data.Str(x.Tag, "id")));
            string chosen = selected == null ? "" : Data.Str(selected, "id"), query = search.Text.Trim();
            var fav = form.Prefs.Set(Kind, false); var recent = form.Prefs.Set(Kind, true);
            var visible = rows.Where(x => (filter == "全部" || filter == Data.Str(x, "category") || filter == "收藏" && fav.Contains(Data.Str(x, "id")) || filter == "最近使用" && recent.Contains(Data.Str(x, "id"))) &&
                (query.Length == 0 || (form.DisplayName(x) + " " + Data.Str(x, "description")).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0))
                .OrderBy(x => filter == "最近使用" ? recent.IndexOf(Data.Str(x, "id")) : 0).ThenBy(form.DisplayName).ToList();
            populating = true; list.BeginUpdate();
            try
            {
                list.Items.Clear();
                foreach (var row in visible)
                {
                    var item = new ListViewItem(form.DisplayName(row)) { Tag = row, Checked = checkedIds.Contains(Data.Str(row, "id")), ToolTipText = form.DisplayName(row) + "\n" + Data.Str(row, "description") };
                    item.SubItems.Add(Data.Str(row, "category")); list.Items.Add(item); if (Data.Str(row, "id") == chosen) item.Selected = true;
                }
            }
            finally { list.EndUpdate(); populating = false; }
            Choose();
        }
        void Choose()
        {
            string previous = selected == null ? "" : Data.Str(selected, "id");
            selected = list.SelectedItems.Count > 0 ? Data.Obj(list.SelectedItems[0].Tag) : null;
            title.Text = selected == null ? "选择列表中的条目" : form.DisplayName(selected);
            description.Text = selected == null ? "支持搜索、收藏和最近使用。" : Data.Str(selected, "description");
            if (Kind == "world" && selected != null)
            {
                if (previous != Data.Str(selected, "id")) foreach (var pair in Numbers)
                    pair.Value.Value = Math.Max(pair.Value.Minimum, Math.Min(pair.Value.Maximum, (decimal)Data.Num(selected, pair.Key, pair.Key == "productivity" ? 1 : 500)));
                if (previous != Data.Str(selected, "id") || (!nameEditor.ContainsFocus && renameRequest == null)) nameEditor.Text = form.DisplayName(selected);
                act.Text = Data.Str(selected, "category") == "产业" ? "设置产业生产倍率" : "应用城镇容量";
            }
            if (Kind == "cargo") FillTargets(); Arrange(); UpdateActions();
        }
        IEnumerable<Dictionary<string, object>> Picked()
        {
            var picks = list.CheckedItems.Cast<ListViewItem>().Select(x => Data.Obj(x.Tag)).ToList();
            if (picks.Count == 0 && selected != null) picks.Add(selected); return picks;
        }
        void FillTargets()
        {
            if (targets == null) return;
            string old = targets.SelectedItem is Choice ? ((Choice)targets.SelectedItem).Id : "";
            var ids = Picked().Select(x => Data.Str(x, "id")).ToList();
            populating = true; targets.BeginUpdate();
            try
            {
                targets.Items.Clear();
                foreach (var stock in Data.Rows(Data.Get(form.State, "stocks")))
                    if (ids.Count > 0 && ids.All(id => Data.Rows(Data.Get(stock, "slots")).Any(slot => Data.RowsOfIds(Data.Get(slot, "allowed")).Contains(id))))
                        targets.Items.Add(new Choice(Data.Str(stock, "id"), form.DisplayName(stock)));
                if (targets.Items.Count > 0) { targets.SelectedIndex = 0; for (int i = 0; i < targets.Items.Count; i++) if (((Choice)targets.Items[i]).Id == old) targets.SelectedIndex = i; }
            }
            finally { targets.EndUpdate(); populating = false; }
        }
        public void UpdateActions()
        {
            if (act == null || populating) return;
            if (lastScope != form.NameScope) { lastScope = form.NameScope; renameRequest = null; }
            if (renameRequest != null)
            {
                var result = Data.Get(form.State, "renameResult"); string state = Data.Str(result, "status");
                if (Data.Str(result, "request") == renameRequest && state != "pending")
                { renameRequest = null; form.Notify(Data.Str(form.State, "message")); }
                else if (renameWatch.ElapsedMilliseconds > 15000)
                { renameRequest = null; form.Notify("游戏尚未确认名称，请恢复模拟后刷新目录。"); }
            }
            var picks = Picked().ToList(); bool online = form.CaptureReady && !form.Busy;
            selection.Text = Kind == "cargo" ? "已选 " + picks.Count + " 项 · 仅显示兼容库存" : selected == null ? "" : Data.Str(selected, "gameName").Length > 0 ? "显示游戏中的名称" : "尚未读取到游戏名称";
            MainForm.Enable(refresh, online); MainForm.Enable(favorite, selected != null);
            MainForm.Enable(focus, online && selected != null && form.IsAvailable("focus")); MainForm.Enable(back, online && form.IsAvailable("return"));
            string op = Kind == "cargo" ? "add" : selected != null && Data.Str(selected, "category") == "产业" ? "industry_value" : "town_capacity";
            MainForm.Enable(act, online && form.IsAvailable(op) && (Kind == "world" ? selected != null : picks.Count > 0 && targets.SelectedItem != null));
            if (Kind == "world")
            {
                bool pending = renameRequest != null;
                bool valid = selected != null && ValidName(nameEditor.Text) && !pending;
                nameLabel.Text = selected != null && Data.Str(selected, "gameName").Length > 0 ? "游戏名称" : "自定义名称";
                MainForm.Enable(nameEditor, selected != null && !pending);
                MainForm.Enable(rename, online && valid && form.IsAvailable("rename_entity"));
                MainForm.Enable(localName, online && valid && Data.Str(selected, "gameName").Length == 0);
                rename.Text = pending ? "等待游戏确认…" : "保存到游戏";
                nameHint.Text = Data.Str(selected, "gameName").Length > 0 ? "名称读取自游戏。保存到游戏后，请在游戏中保存存档。" : "游戏名称优先。显示名称只用于本工具的当前地图会话，不修改游戏。";
            }
            footnote.Text = online && !form.IsAvailable(op) ? form.UnavailableReason(op) : Kind == "cargo" ? "货物直接添加到兼容库存；乘客由人口模拟生成。库存空间不足时请减少数量。" : "";
        }
        static bool ValidName(string value) { return !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value.Trim()) <= 256 && !value.Any(char.IsControl); }
        void SaveGameName()
        {
            if (selected == null || !ValidName(nameEditor.Text)) return;
            renameRequest = Guid.NewGuid().ToString("N"); renameWatch.Restart();
            form.Request(new { op = "rename_entity", id = Data.Str(selected, "id"), world = Data.Get(form.State, "world"), name = nameEditor.Text.Trim(), request = renameRequest },
                delegate(bool success, string message) { if (!success) { renameRequest = null; form.Notify(message); } UpdateActions(); });
            UpdateActions();
        }
        void SaveLocalName()
        {
            if (selected == null || !ValidName(nameEditor.Text) || Data.Str(selected, "gameName").Length > 0) return;
            form.SaveDisplayName(selected, nameEditor.Text.Trim());
        }
        void Execute()
        {
            if (Kind == "cargo")
            {
                var stock = targets.SelectedItem as Choice; if (stock == null) return;
                foreach (var row in Picked()) { form.Request(new { op = "add", id = Data.Str(row, "id"), target = stock.Id, count = Numbers["count"].Value }); form.Remember(Kind, Data.Str(row, "id")); }
            }
            else if (selected != null)
            {
                string id = Data.Str(selected, "id");
                if (Data.Str(selected, "category") == "产业") form.Request(new { op = "industry_value", id = id, value = Numbers["productivity"].Value });
                else form.Request(new { op = "town_capacity", id = id, res = Numbers["res"].Value, commercial = Numbers["commercial"].Value, industry = Numbers["industry"].Value });
                form.Remember(Kind, id);
            }
        }
        public void SelectFirst() { if (list.Items.Count > 0) { list.Items[0].Selected = true; Choose(); } }
        internal void SelectForLayoutCheck(int index)
        { if (index >= 0 && index < list.Items.Count) { list.SelectedItems.Clear(); list.Items[index].Selected = true; Choose(); } }
    }
}
