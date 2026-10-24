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
        readonly Label title, description, selection, footnote, nameLabel, nameHint, resultCount, empty, targetLabel, recommendationText, targetSummary, actionSummary;
        readonly PillButton act, favorite, focus, back, refresh, rename, localName, clearSearch, clearPicked, useRecommended, locateTarget;
        readonly Surface recommendation, actionBar;
        readonly ThemedComboBox targets;
        readonly Dictionary<string, Label> numberLabels = new Dictionary<string, Label>();
        public readonly Dictionary<string, NumericUpDown> Numbers = new Dictionary<string, NumericUpDown>();
        List<Dictionary<string, object>> rows = new List<Dictionary<string, object>>();
        Dictionary<string, object> selected;
        readonly HashSet<string> checkedCargo = new HashSet<string>();
        List<CargoTarget> targetChoices = new List<CargoTarget>(); CargoTarget recommended;
        string filter = "全部", signature = "", stockSignature = "", targetPlanSignature = "", renameRequest, lastScope = "";
        string cargoRequest, renameEntityId; List<string> pendingCargoIds;
        readonly Stopwatch renameWatch = new Stopwatch(), cargoWatch = new Stopwatch();
        bool populating, arranging, settingName, nameDirty, cargoDelayed;
        int P(int value) { return Ui.Px(this, value); }

        public CatalogPage(MainForm parent, string kind)
        {
            form = parent; Kind = kind; BackColor = Theme.Back;
            search = new TextBox { Font = Theme.F(11), BackColor = Theme.Card, ForeColor = Theme.Ink, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "搜索名称或描述", Name = "catalog-search" };
            search.TextChanged += delegate { Refill(); };
            Controls.Add(search);
            clearSearch = new PillButton { Text = "清空搜索", Name = "clear-search" }; clearSearch.Click += delegate { search.Clear(); search.Focus(); }; Controls.Add(clearSearch);
            resultCount = MainForm.LabelFor("", 9); resultCount.ForeColor = Theme.Muted; Controls.Add(resultCount);
            filters = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Back };
            Controls.Add(filters);
            string[] names = kind == "cargo" ? new[] { "全部", "收藏", "最近使用" } : new[] { "全部", "城镇", "产业", "收藏", "最近使用" };
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
            if (kind == "cargo")
            {
                clearPicked = new PillButton { Text = "清空勾选", Name = "clear-cargo-selection" };
                clearPicked.Click += delegate { checkedCargo.Clear(); populating = true; foreach (ListViewItem item in list.Items) item.Checked = false; populating = false; FillTargets(); UpdateActions(); };
                filters.Controls.Add(clearPicked);
            }
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
            list.ItemChecked += delegate(object sender, ItemCheckedEventArgs e) { if (!populating) { string id = Data.Str(e.Item.Tag, "id"); if (e.Item.Checked) checkedCargo.Add(id); else checkedCargo.Remove(id); FillTargets(); UpdateActions(); } };
            body.Controls.Add(list);
            empty = MainForm.LabelFor("先连接游戏并进入地图。", 11); empty.TextAlign = ContentAlignment.MiddleCenter; empty.BackColor = Theme.Card; empty.Name = "catalog-empty"; body.Controls.Add(empty);
            detail = new Surface { Padding = new Padding(12) }; body.Controls.Add(detail);
            detailViewport = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Card, AutoScroll = true };
            detail.Controls.Add(detailViewport);
            detailContent = new Panel { BackColor = Theme.Card, Tag = "scroll-content" }; detailViewport.Controls.Add(detailContent);
            title = Label("选择列表中的条目", 16, true); title.Name = "entity-title";
            description = Label("可搜索游戏名称或描述。", 10); description.ForeColor = Theme.Muted;
            selection = Label("", 9); selection.ForeColor = Theme.Muted;
            footnote = Label("", 9); footnote.ForeColor = Theme.Muted;
            act = Button(kind == "cargo" ? "添加所选货物" : "应用城镇容量", true, Execute);
            actionBar = new Surface { Name = "catalog-action-bar" }; Controls.Add(actionBar); actionBar.Controls.Add(act);
            actionSummary = MainForm.LabelFor("请先选择目标。", 9); actionSummary.ForeColor = Theme.Muted; actionBar.Controls.Add(actionSummary);
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
            nameEditor.TextChanged += delegate { if (!settingName) nameDirty = true; UpdateActions(); };
            detailContent.Controls.Add(nameEditor);
            rename = Button("保存到游戏", true, SaveGameName); rename.Name = "save-game-name";
            localName = Button("保存显示名称", false, SaveLocalName); localName.Name = "save-display-name";
            nameHint = Label("游戏名称优先；读不到时可设置本工具的显示名称。", 9); nameHint.ForeColor = Theme.Muted;
            if (kind == "cargo")
            {
                AddNumber("count", "3 · 每种货物数量", 1, 10000, 1, 0);
                Numbers["count"].ValueChanged += delegate { stockSignature = ""; FillTargets(); UpdateActions(); };
                targets = new ThemedComboBox { Font = Theme.F(10), AccessibleName = "兼容的目标库存", Name = "cargo-target" };
                targets.SelectedIndexChanged += delegate { if (!populating) UpdateActions(); };
                targets.DropDownClosed += delegate { stockSignature = ""; FillTargets(); UpdateActions(); }; detailContent.Controls.Add(targets);
                targetLabel = Label("2 · 目标库存", 11, true);
                targetSummary = Label("", 9); targetSummary.ForeColor = Theme.Muted;
                recommendation = new Surface { BackColor = Theme.AccentSoft, Name = "cargo-recommendation" }; detailContent.Controls.Add(recommendation);
                recommendationText = MainForm.LabelFor("先选择货物，再推荐附近的库存。", 10); recommendation.Controls.Add(recommendationText);
                useRecommended = new PillButton { Text = "使用推荐", Name = "use-recommended-stock" };
                useRecommended.Click += delegate { if (recommended == null) return; for (int i = 0; i < targets.Items.Count; i++) if (((CargoTarget)targets.Items[i]).Id == recommended.Id) { targets.SelectedIndex = i; break; } };
                recommendation.Controls.Add(useRecommended);
                locateTarget = Button("定位所选建筑", false, delegate { var target = targets.SelectedItem as CargoTarget; if (target != null) form.Request(new { op = "focus", id = Data.Str(target.Row, "nameEntity") }); });
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
                int w = ClientSize.Width, gap = P(14); bool compact = Height < P(300);
                search.SetBounds(0, P(4), Math.Max(1, w - P(100)), P(32)); clearSearch.SetBounds(Math.Max(0, w - P(92)), P(4), P(92), P(32));
                foreach (PillButton button in filters.Controls)
                { button.Size = new Size(P(button.Text == "最近使用" ? compact ? 88 : 100 : button.Text.Length <= 2 ? compact ? 50 : 64 : compact ? 74 : 86), P(32)); button.Margin = new Padding(0, 0, P(compact ? 6 : 7), P(7)); }
                filters.SetBounds(0, P(46), w, Ui.FlowHeight(filters, w, P(39)));
                resultCount.SetBounds(P(2), filters.Bottom + P(2), Math.Max(1, w - P(4)), P(25));
                resultCount.Visible = !compact;
                bool stack = w < P(760);
                int top = compact ? filters.Bottom + P(8) : resultCount.Bottom + P(5), actionHeight = P(compact ? 62 : stack ? 76 : 64);
                actionBar.SetBounds(0, Math.Max(0, Height - actionHeight), w, actionHeight);
                actionSummary.SetBounds(P(12), P(compact ? 4 : 6), Math.Max(1, compact || stack ? w - P(24) : w - P(232)), P(compact ? 24 : stack ? 27 : 50));
                act.SetBounds(compact || stack ? P(12) : w - P(204), P(compact ? 30 : stack ? 37 : 14), compact || stack ? Math.Max(1, w - P(24)) : P(192), P(compact ? 28 : 32));
                body.SetBounds(0, top, w, Math.Max(0, actionBar.Top - P(compact ? 8 : 10) - top));
                body.AutoScroll = stack;
                body.AutoScrollMinSize = Size.Empty;
                int canvasWidth = Math.Max(1, body.ClientSize.Width - (stack ? P(19) : 0));
                int left = stack ? canvasWidth : (int)((canvasWidth - gap) * .55);
                int detailWidth = stack ? canvasWidth : canvasWidth - left - gap;
                int listHeight = stack ? P(210) : body.ClientSize.Height;
                list.SetBounds(0, 0, Math.Max(1, left), Math.Max(1, listHeight));
                empty.SetBounds(P(12), P(12), Math.Max(1, left - P(24)), Math.Max(1, listHeight - P(24))); empty.BringToFront();
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
                if (targets != null)
                {
                    targetLabel.SetBounds(pad, y, inner, P(28)); y += P(34);
                    recommendation.SetBounds(pad, y, inner, P(106));
                    recommendationText.SetBounds(P(10), P(7), Math.Max(1, inner - P(20)), P(50));
                    useRecommended.SetBounds(P(10), P(64), Math.Max(1, inner - P(20)), P(32)); y += P(116);
                    targets.SetBounds(pad, y, inner, P(34)); y += P(42);
                    targetSummary.SetBounds(pad, y, inner, P(48)); y += P(56);
                    locateTarget.SetBounds(pad, y, inner, P(32)); y += P(46);
                }
                foreach (var pair in Numbers)
                {
                    bool show = Kind == "cargo" || selected == null || ((Data.Str(selected, "category") == "产业") == (pair.Key == "productivity"));
                    pair.Value.Visible = numberLabels[pair.Key].Visible = show;
                    if (!show) continue;
                    int inputWidth = Math.Min(P(155), inner / 2);
                    numberLabels[pair.Key].SetBounds(pad, y + P(3), Math.Max(1, inner - inputWidth - P(12)), P(28));
                    pair.Value.SetBounds(dw - pad - inputWidth, y, inputWidth, P(32)); y += P(45);
                }
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
                detailContent.SetBounds(0, 0, dw, y);
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
            if (lastScope != form.NameScope)
            {
                lastScope = form.NameScope; selected = null; checkedCargo.Clear(); nameDirty = false;
                renameRequest = cargoRequest = null; pendingCargoIds = null; signature = stockSignature = targetPlanSignature = "";
            }
            var next = Data.Rows(data); if (Kind == "cargo") next = next.Where(x => Data.Str(x, "id") != "0").ToList();
            string sig = Data.Json.Serialize(next) + form.DisplayNameRevision;
            if (sig == signature) return; signature = sig; rows = next;
            checkedCargo.IntersectWith(rows.Select(x => Data.Str(x, "id"))); Refill();
        }
        public void InvalidateNames() { signature = ""; UpdateRows(Data.Get(form.State, Kind == "cargo" ? "catalog" : "worlds")); stockSignature = ""; UpdateStocks(); }
        public void UpdateStocks()
        {
            if (Kind != "cargo") return;
            var camera = Data.Get(form.State, "camera");
            string sig = Data.Json.Serialize(Data.Get(form.State, "stocks")) + ":" + Math.Round(Data.Num(camera, "x", double.NaN)) + ":" + Math.Round(Data.Num(camera, "y", double.NaN)) + ":" + form.DisplayNameRevision;
            if (sig == stockSignature) return; stockSignature = sig; FillTargets(); UpdateActions();
        }
        void Refill()
        {
            if (list == null) return;
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
                    var item = new ListViewItem(form.DisplayName(row)) { Tag = row, Checked = checkedCargo.Contains(Data.Str(row, "id")), ToolTipText = form.DisplayName(row) + "\n" + Data.Str(row, "description") };
                    item.SubItems.Add(Data.Str(row, "category")); list.Items.Add(item); if (Data.Str(row, "id") == chosen) item.Selected = true;
                }
            }
            finally { list.EndUpdate(); populating = false; }
            empty.Visible = list.Items.Count == 0;
            empty.Text = !Data.Bool(form.State, "playing") ? "先连接游戏并进入地图。" : rows.Count == 0 ? "目录暂未读取到内容，请刷新目录。" : "没有匹配条目，可清空搜索或切换分类。";
            Choose();
        }
        void Choose()
        {
            string previous = selected == null ? "" : Data.Str(selected, "id");
            selected = list.SelectedItems.Count > 0 ? Data.Obj(list.SelectedItems[0].Tag) : null;
            title.Text = Kind == "cargo" ? "1 · 选择货物" : selected == null ? "选择城镇或产业" : form.DisplayName(selected);
            description.Text = Kind == "cargo" ? "勾选可批量添加；未勾选时使用当前选中项。搜索不会清除已勾选的货物。" : selected == null ? "支持搜索、收藏和最近使用。" : Data.Str(selected, "description");
            if (Kind == "world" && selected != null)
            {
                if (previous != Data.Str(selected, "id")) foreach (var pair in Numbers)
                    pair.Value.Value = Math.Max(pair.Value.Minimum, Math.Min(pair.Value.Maximum, (decimal)Data.Num(selected, pair.Key, pair.Key == "productivity" ? 1 : 500)));
                if (previous != Data.Str(selected, "id")) nameDirty = false;
                if (!nameDirty && !nameEditor.ContainsFocus && renameRequest == null) SetNameText(form.DisplayName(selected));
                act.Text = Data.Str(selected, "category") == "产业" ? "设置产业生产倍率" : "应用城镇容量";
            }
            if (Kind == "cargo") FillTargets(); Arrange(); UpdateActions();
        }
        IEnumerable<Dictionary<string, object>> Picked()
        {
            var picks = rows.Where(x => checkedCargo.Contains(Data.Str(x, "id"))).ToList();
            if (picks.Count == 0 && selected != null) picks.Add(selected); return picks;
        }
        void FillTargets()
        {
            if (targets == null) return;
            string old = targets.SelectedItem is CargoTarget ? ((CargoTarget)targets.SelectedItem).Id : "";
            var ids = Picked().Select(x => Data.Str(x, "id")).ToList();
            string planSignature = form.NameScope + ":" + Data.Json.Serialize(Data.Get(form.State, "stocks")) + ":" + string.Join(",", ids) + ":" + Numbers["count"].Value + ":" + form.DisplayNameRevision;
            if (planSignature != targetPlanSignature)
            { targetPlanSignature = planSignature; targetChoices = CargoTargets.Find(Data.Get(form.State, "stocks"), ids, (int)Numbers["count"].Value, Data.Get(form.State, "camera"), form.DisplayName); }
            else targetChoices = CargoTargets.RefreshDistances(targetChoices, Data.Get(form.State, "camera"));
            recommended = CargoTargets.Recommend(targetChoices);
            if (targets.DroppedDown || cargoRequest != null) return;
            populating = true; targets.BeginUpdate();
            try
            {
                targets.Items.Clear();
                foreach (var target in targetChoices) targets.Items.Add(target);
                int found = targetChoices.FindIndex(t => t.Id == old);
                if (found >= 0) targets.SelectedIndex = found;
                else if (recommended != null) targets.SelectedIndex = targetChoices.IndexOf(recommended);
                else if (targetChoices.Count > 0) targets.SelectedIndex = 0;
            }
            finally { targets.EndUpdate(); populating = false; }
        }
        public void UpdateActions()
        {
            if (act == null || populating) return;
            if (renameRequest != null)
            {
                var result = Data.Get(form.State, "renameResult"); string state = Data.Str(result, "status");
                if (Data.Str(result, "request") == renameRequest && state != "pending")
                { if (state == "confirmed" && selected != null && Data.Str(selected, "id") == renameEntityId) nameDirty = false; renameRequest = null; form.Notify(Data.Str(form.State, "message"), state == "failed"); }
                else if (renameWatch.ElapsedMilliseconds > 15000)
                { renameRequest = null; form.Notify("游戏尚未确认名称，请恢复模拟后刷新目录。"); }
            }
            if (cargoRequest != null)
            {
                var result = Data.Get(form.State, "cargoResult"); string state = Data.Str(result, "status");
                if (Data.Str(result, "request") == cargoRequest && state != "pending")
                {
                    cargoRequest = null;
                    var addedIds = Data.RowsOfIds(Data.Get(result, "addedIds")).ToList(); if (addedIds.Count > 0) form.RememberMany(Kind, addedIds);
                    string amountMessage = "实际添加 " + Data.Num(result, "added").ToString("N0") + " 件" + (Data.Num(result, "planned") < Data.Num(result, "requested") ? "；超出容量的数量已自动调整。" : "。");
                    if (state == "completed") form.Notify(Data.Num(result, "added") == 0 ? "库存已满，无需添加。" : amountMessage);
                    else form.Notify(amountMessage + "部分库存操作失败，请刷新查看。", true);
                    pendingCargoIds = null;
                    FillTargets();
                }
                else if (!cargoDelayed && cargoWatch.ElapsedMilliseconds > 20000)
                { cargoDelayed = true; form.Notify("货物尚未确认，请恢复游戏运行并等待，避免重复提交。", true); }
            }
            var picks = Picked().ToList(); bool online = form.CaptureReady && !form.Busy;
            resultCount.Text = "显示 " + list.Items.Count + " / " + rows.Count + (Kind == "cargo" ? " · 已选 " + picks.Count + " 种" : "");
            MainForm.Enable(clearSearch, search.Text.Length > 0 && cargoRequest == null);
            MainForm.Enable(search, cargoRequest == null); MainForm.Enable(list, cargoRequest == null); MainForm.Enable(filters, cargoRequest == null);
            selection.Text = Kind == "cargo" ? picks.Count == 0 ? "请先选择要添加的货物" : string.Join("、", picks.Take(3).Select(form.DisplayName)) + (picks.Count > 3 ? "等 " + picks.Count + " 种" : "") : selected == null ? "" : Data.Str(selected, "gameName").Length > 0 ? "显示游戏中的名称" : "尚未读取到游戏名称";
            MainForm.Enable(refresh, online); MainForm.Enable(favorite, selected != null);
            favorite.Text = selected != null && form.Prefs.Set(Kind, false).Contains(Data.Str(selected, "id")) ? "取消收藏" : "收藏当前条目";
            MainForm.Enable(focus, online && selected != null && form.IsAvailable("focus")); MainForm.Enable(back, online && form.IsAvailable("return"));
            string op = Kind == "cargo" ? "add" : selected != null && Data.Str(selected, "category") == "产业" ? "industry_value" : "town_capacity";
            var chosenTarget = targets == null ? null : targets.SelectedItem as CargoTarget;
            if (chosenTarget != null) chosenTarget = targetChoices.FirstOrDefault(t => t.Id == chosenTarget.Id);
            bool fits = chosenTarget != null && targetChoices.Any(t => t.Id == chosenTarget.Id && t.Fits);
            MainForm.Enable(act, online && form.IsAvailable(op) && (Kind == "world" ? selected != null : picks.Count > 0 && fits && cargoRequest == null));
            actionSummary.Text = !form.CaptureReady ? "请先连接游戏并进入地图。" : Kind == "cargo" ? chosenTarget == null ? "请选择货物与目标库存。" : chosenTarget.Name + " · 预计添加 " + chosenTarget.Allocation.Total + " 件" : selected == null ? "选择城镇或产业后，可应用右侧参数。" : "当前目标：" + form.DisplayName(selected);
            if (Kind == "cargo")
            {
                MainForm.Enable(clearPicked, checkedCargo.Count > 0 && cargoRequest == null);
                MainForm.Enable(targets, targetChoices.Count > 0 && cargoRequest == null);
                MainForm.Enable(Numbers["count"], cargoRequest == null);
                MainForm.Enable(useRecommended, recommended != null && cargoRequest == null && (chosenTarget == null || chosenTarget.Id != recommended.Id));
                MainForm.Enable(locateTarget, online && chosenTarget != null && form.IsAvailable("focus"));
                useRecommended.Text = recommended != null && chosenTarget != null && chosenTarget.Id == recommended.Id ? "已选择推荐" : "使用推荐";
                recommendationText.Text = recommended != null ? "推荐：" + recommended.Name + "\r\n" + recommended.Kind + " · 距镜头中心 " + CargoTargets.DistanceText(recommended.Distance.Value) : picks.Count == 0 ? "选择货物后推荐附近的兼容库存。" : picks.Count > 64 ? "一次最多选择 64 种货物，请减少勾选。" : !targetChoices.Any(t => t.Fits) ? "暂无有空位的兼容库存。" : "位置或镜头数据不可用，请手动选择库存。";
                targetSummary.Text = chosenTarget == null ? "请选择兼容的仓库或生产建筑。" : "目标：" + chosenTarget.Name + (fits ? "\r\n预计添加 " + chosenTarget.Allocation.Total + " 件" + (chosenTarget.Clipped ? "，已按剩余空间调整。" : "。") : "\r\n库存已满，无需添加。");
                act.Text = cargoRequest != null ? "等待游戏执行…" : "添加所选货物";
                if (cargoRequest != null) actionSummary.Text = "已提交货物，正在等待游戏执行，请勿重复添加。";
            }
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
            footnote.Text = online && !form.IsAvailable(op) ? form.UnavailableReason(op) : Kind == "cargo" ? "空间不足时自动按剩余容量添加；实际数量以游戏执行结果为准。乘客由人口模拟生成。" : "";
        }
        static bool ValidName(string value) { return !string.IsNullOrWhiteSpace(value) && Encoding.UTF8.GetByteCount(value.Trim()) <= 256 && !value.Any(char.IsControl); }
        void SaveGameName()
        {
            if (selected == null || !ValidName(nameEditor.Text)) return;
            renameRequest = Guid.NewGuid().ToString("N"); renameEntityId = Data.Str(selected, "id"); renameWatch.Restart();
            form.Request(new { op = "rename_entity", id = Data.Str(selected, "id"), world = Data.Get(form.State, "world"), name = nameEditor.Text.Trim(), request = renameRequest },
                delegate(bool success, string message) { if (!success) { renameRequest = null; form.Notify(message); } UpdateActions(); });
            UpdateActions();
        }
        void SaveLocalName()
        {
            if (selected == null || !ValidName(nameEditor.Text) || Data.Str(selected, "gameName").Length > 0) return;
            form.SaveDisplayName(selected, nameEditor.Text.Trim());
            nameDirty = false;
        }
        void Execute()
        {
            if (Kind == "cargo")
            {
                var stock = targets.SelectedItem as CargoTarget; if (stock == null || cargoRequest != null) return;
                pendingCargoIds = Picked().Select(row => Data.Str(row, "id")).ToList();
                cargoRequest = Guid.NewGuid().ToString("N"); cargoWatch.Restart(); cargoDelayed = false;
                form.Request(new { op = "add", ids = pendingCargoIds.ToArray(), target = stock.Id, world = Data.Get(form.State, "world"), count = Numbers["count"].Value, request = cargoRequest },
                    delegate(bool success, string message) { if (!success) { cargoRequest = null; pendingCargoIds = null; form.Notify(message, true); } UpdateActions(); });
                UpdateActions();
            }
            else if (selected != null)
            {
                string id = Data.Str(selected, "id");
                Action<bool, string> done = delegate(bool success, string message) { if (success) form.Remember(Kind, id); };
                if (Data.Str(selected, "category") == "产业") form.Request(new { op = "industry_value", id = id, value = Numbers["productivity"].Value }, done);
                else form.Request(new { op = "town_capacity", id = id, res = Numbers["res"].Value, commercial = Numbers["commercial"].Value, industry = Numbers["industry"].Value }, done);
            }
        }
        public void SelectFirst() { if (list.Items.Count > 0) { list.Items[0].Selected = true; Choose(); } }
        void SetNameText(string value) { settingName = true; try { nameEditor.Text = value; } finally { settingName = false; } }
        internal void SelectForLayoutCheck(int index)
        { if (index >= 0 && index < list.Items.Count) { list.SelectedItems.Clear(); list.Items[index].Selected = true; Choose(); } }
        internal void ValidateCargoFlow()
        {
            if (Kind != "cargo" || list.Items.Count == 0) throw new InvalidOperationException("缺少离线货物示例。");
            var previousCamera = Data.Get(form.State, "camera");
            try
            {
                list.Items[0].Checked = true; string picked = Data.Str(list.Items[0].Tag, "id");
                search.Text = "__no_matching_cargo__";
                if (list.Items.Count != 0 || !Picked().Any(r => Data.Str(r, "id") == picked)) throw new Exception("搜索清除了已勾选货物");
                search.Clear();
                if (!list.Items.Cast<ListViewItem>().Any(i => Data.Str(i.Tag, "id") == picked && i.Checked)) throw new Exception("勾选状态未恢复");
                var manual = targetChoices.First(t => t.Id == "30"); targets.SelectedIndex = targetChoices.IndexOf(manual);
                form.State["camera"] = new Dictionary<string, object> { { "x", 60 }, { "y", 0 } }; UpdateStocks();
                if (((CargoTarget)targets.SelectedItem).Id != manual.Id) throw new Exception("移动镜头改变了手动目标");
            }
            finally { form.State["camera"] = previousCamera; checkedCargo.Clear(); search.Clear(); Refill(); UpdateStocks(); }
        }
    }
}
