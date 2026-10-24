using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using TransportFever3.Interop;

namespace TransportFever3Trainer
{
    sealed class MainForm : WindowChrome
    {
        sealed class RequestItem { public object Command; public string Scope; public Action<bool, string> Completed; }
        public readonly UserPrefs Prefs = UserPrefs.Load();
        public Dictionary<string, object> State = new Dictionary<string, object>();
        public string StateJson { get { return Data.Json.Serialize(State); } }
        public bool CaptureReady { get { return bridge != null && bridge.Ready && Data.Bool(State, "playing"); } }
        public bool Busy { get { return sending || requests.Count > 0; } }
        public int DisplayNameRevision { get; private set; }
        public string NameScope { get { return !Data.Bool(State, "playing") ? "" : (bridge == null ? "preview" : bridge.ProcessStartTicks.ToString(CultureInfo.InvariantCulture)) + ":" + Data.Str(State, "nameScope"); } }
        readonly bool offline;
        Bridge bridge; bool connecting, sending, closing, arranging;
        readonly Queue<RequestItem> requests = new Queue<RequestItem>(); RequestItem activeRequest;
        readonly Surface sidebar = new Surface { Name = "sidebar" }, header = new Surface { Name = "page-header" };
        readonly Panel pages = new Panel();
        readonly FlowLayoutPanel navigationBar = new FlowLayoutPanel(), actions = new FlowLayoutPanel();
        readonly Label heading, stats, status, online, appTitle, appCaption, pageHint;
        readonly AppMark mark = new AppMark();
        readonly PillButton connect, reset;
        readonly ToolTip tooltips = new ToolTip();
        readonly Dictionary<string, FeatureCard> cards = new Dictionary<string, FeatureCard>();
        readonly Dictionary<string, Panel> sections = new Dictionary<string, Panel>();
        readonly Dictionary<string, Panel> pageViews = new Dictionary<string, Panel>();
        readonly Dictionary<string, PillButton> navigation = new Dictionary<string, PillButton>();
        readonly List<CatalogPage> catalogs = new List<CatalogPage>();
        readonly Dictionary<int, string> hotkeys = new Dictionary<int, string>();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer { Interval = 150 };
        Panel featurePage, featureScroll, featuresContent;
        FlowLayoutPanel groups;
        CameraPage camera; TextBox diagnosis;
        string page = "features", group = "全部", lastSnapshot = "";
        int P(int n) { return Ui.Px(this, n); }
        protected override Size MinimumLogicalSize { get { return new Size(620, 440); } }
        protected override Size PreferredClientLayout { get { return new Size(1280, 800); } }
        Rectangle LayoutArea { get { return Ui.AspectBounds(ClientRectangle); } }
        public static Label LabelFor(string text, float size = 10, bool bold = false)
        { return new Label { Text = text, ForeColor = Theme.Ink, Font = Theme.F(size, bold), BackColor = Color.Transparent, AutoSize = false, AutoEllipsis = true, UseMnemonic = false }; }
        public static void Enable(Control control, bool value) { if (control.Enabled != value) control.Enabled = value; }

        public MainForm(bool forCapture = false, bool offlineCheck = false)
        {
            // All bounds already use monitor-DPI pixels. Do not scale the same
            // dynamically created controls a second time through Form autoscaling.
            AutoScaleMode = AutoScaleMode.None; FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
            offline = offlineCheck; Text = "狂热运输3修改器 · v" + Protocol.Version; ClientSize = new Size(1220, 780);
            heading = LabelFor("常用功能", 20, true); heading.Name = "page-heading";
            pageHint = LabelFor("", 9); pageHint.ForeColor = Theme.Muted; pageHint.Name = "page-help";
            stats = LabelFor("连接游戏后显示公司与时间信息", 10); stats.ForeColor = Theme.Muted;
            online = LabelFor("● 游戏未连接", 10); online.ForeColor = Theme.Muted;
            status = LabelFor("请进入地图，再连接游戏。", 9); status.ForeColor = Theme.Muted; status.Name = "status-message";
            appTitle = LabelFor("狂热运输 3", 10, true); appCaption = LabelFor("修改器", 9); appCaption.ForeColor = Theme.Muted;
            sidebar.Controls.Add(mark); sidebar.Controls.Add(appTitle); sidebar.Controls.Add(appCaption);
            sidebar.Controls.Add(navigationBar); sidebar.Controls.Add(actions);
            header.Controls.Add(heading); header.Controls.Add(pageHint); header.Controls.Add(stats); header.Controls.Add(online);
            Controls.Add(sidebar); Controls.Add(header); Controls.Add(pages); Controls.Add(status);
            pages.BackColor = Theme.Back; navigationBar.BackColor = actions.BackColor = Theme.Card;
            navigationBar.Name = "page-navigation"; actions.Name = "sidebar-actions";
            navigationBar.FlowDirection = actions.FlowDirection = FlowDirection.TopDown;
            navigationBar.WrapContents = actions.WrapContents = false; navigationBar.AutoScroll = true;
            string[] names = { "常用功能", "货物添加", "世界管理", "镜头书签", "诊断" }, ids = { "features", "cargo", "world", "camera", "diagnostics" };
            for (int i = 0; i < names.Length; i++)
            {
                string id = ids[i]; var button = new PillButton { Text = names[i], Name = "navigation-" + id };
                button.Click += delegate { ShowPage(id); }; navigation[id] = button; navigationBar.Controls.Add(button);
            }
            connect = new PillButton { Text = "连接 / 刷新", Primary = true, Name = "connect-game" };
            connect.Click += delegate { ConnectGame(); }; actions.Controls.Add(connect);
            reset = new PillButton { Text = "关闭全部功能", Name = "reset-features" };
            reset.Click += delegate { Request(new { op = "reset" }); }; actions.Controls.Add(reset);
            BuildFeatures();
            foreach (string kind in new[] { "cargo", "world" })
            { var catalog = new CatalogPage(this, kind) { Dock = DockStyle.Fill }; catalogs.Add(catalog); AddPage(kind, catalog); }
            camera = new CameraPage(this) { Dock = DockStyle.Fill }; AddPage("camera", camera);
            var diagnosticsPage = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Theme.Card };
            diagnosis = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Card, ForeColor = Theme.Ink, Font = Theme.F(10), Dock = DockStyle.Fill };
            var export = new PillButton { Text = "导出诊断", Dock = DockStyle.Bottom, Height = 40, Name = "export-diagnostics" };
            export.Click += delegate {
                using (var dialog = new SaveFileDialog { Filter = "JSON 诊断|*.json", FileName = "狂热运输3-诊断.json" })
                    if (dialog.ShowDialog(this) == DialogResult.OK) File.WriteAllText(dialog.FileName, StateJson, System.Text.Encoding.UTF8);
            };
            diagnosticsPage.Controls.Add(diagnosis); diagnosticsPage.Controls.Add(export); AddPage("diagnostics", diagnosticsPage);
            SizeChanged += delegate { Arrange(); };
            DpiChanged += delegate { Arrange(); };
            timer.Tick += delegate { Poll(); };
            Shown += delegate { Arrange(); if (!offline) { RegisterKeys(); timer.Start(); ConnectGame(); } };
            FormClosing += delegate {
                closing = true; timer.Stop(); foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(Handle, id);
                if (bridge != null) bridge.Dispose(); tooltips.Dispose(); timer.Dispose();
            };
            ShowPage("features"); UpdateState(); Arrange();
        }
        protected override void OnLayout(LayoutEventArgs e) { base.OnLayout(e); Arrange(); }
        void AddPage(string id, Panel panel) { panel.Visible = false; pages.Controls.Add(panel); pageViews[id] = panel; }
        void Arrange()
        {
            if (arranging || heading == null || camera == null) return; arranging = true;
            try
            {
                var area = LayoutArea;
                int pad = P(16), gap = P(16), sideWidth = P(area.Width >= P(1000) ? 208 : 144);
                sidebar.SetBounds(area.Left + pad, area.Top + pad, sideWidth, Math.Max(1, area.Height - pad * 2));
                bool shortSidebar = sidebar.Height < P(420);
                mark.SetBounds(P(12), P(18), P(32), P(32));
                appTitle.SetBounds(P(56), P(14), Math.Max(1, sideWidth - P(68)), P(26));
                appCaption.SetBounds(P(56), P(42), Math.Max(1, sideWidth - P(68)), P(22));
                int innerWidth = sideWidth - P(24), actionHeight = P(shortSidebar ? 34 : 36);
                int actionGap = P(8), actionTotal = actionHeight * actions.Controls.Count + actionGap * (actions.Controls.Count - 1);
                actions.SetBounds(P(12), Math.Max(P(72), sidebar.Height - P(16) - actionTotal), innerWidth, actionTotal);
                foreach (PillButton button in actions.Controls)
                { button.Size = new Size(innerWidth, actionHeight); button.Margin = new Padding(0, 0, 0, button == reset ? 0 : actionGap); }
                int navTop = P(shortSidebar ? 72 : 92), navHeight = P(shortSidebar ? 32 : 40), navGap = P(shortSidebar ? 6 : 8);
                int navTotal = navigation.Count * navHeight + Math.Max(0, navigation.Count - 1) * navGap;
                var navOffset = navigationBar.AutoScrollPosition; navigationBar.SuspendLayout();
                try
                {
                    navigationBar.AutoScroll = false;
                    navigationBar.SetBounds(P(12), navTop, innerWidth, Math.Max(1, actions.Top - P(16) - navTop));
                    int navWidth = Math.Max(1, innerWidth - (navTotal > navigationBar.Height ? P(19) : 0));
                    int index = 0;
                    foreach (PillButton button in navigationBar.Controls)
                    { button.Size = new Size(navWidth, navHeight); button.Margin = new Padding(0, 0, 0, ++index == navigation.Count ? 0 : navGap); }
                }
                finally
                {
                    navigationBar.AutoScroll = true; navigationBar.ResumeLayout(true);
                    navigationBar.AutoScrollPosition = new Point(-navOffset.X, -navOffset.Y);
                }

                int contentLeft = sidebar.Right + gap, w = Math.Max(1, area.Right - contentLeft - pad); bool wide = w >= P(900);
                bool compactHeader = area.Height < P(560);
                header.SetBounds(contentLeft, area.Top + pad, w, P(compactHeader ? 96 : wide ? 112 : 160));
                online.Visible = stats.Visible = !compactHeader;
                appCaption.Text = compactHeader ? CaptureReady ? "已连接" : "未连接" : "修改器";
                appCaption.ForeColor = compactHeader && CaptureReady ? Theme.Green : Theme.Muted;
                heading.SetBounds(P(20), P(14), Math.Max(1, wide ? w / 2 - P(30) : w - P(40)), P(36));
                pageHint.SetBounds(P(20), P(52), Math.Max(1, wide ? w / 2 - P(30) : w - P(40)), P(compactHeader ? 34 : wide ? 38 : 22));
                online.SetBounds(P(20), P(wide ? 90 : 76), Math.Max(1, wide ? w / 2 - P(30) : w - P(40)), P(22));
                if (wide) stats.SetBounds(w / 2 + P(16), P(20), w / 2 - P(36), P(72));
                else stats.SetBounds(P(20), P(104), Math.Max(1, w - P(40)), P(48));
                int pageTop = header.Bottom + P(12), footer = P(28);
                pages.SetBounds(contentLeft, pageTop, w, Math.Max(1, area.Bottom - pageTop - pad - footer));
                status.SetBounds(contentLeft + P(2), area.Bottom - pad - footer + P(4), Math.Max(1, w - P(4)), footer);
                ArrangeFeatures(); foreach (var catalog in catalogs) catalog.Arrange(); camera.Arrange();
            }
            finally { arranging = false; }
        }
        void BuildFeatures()
        {
            featurePage = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Back };
            groups = new FlowLayoutPanel { WrapContents = true, BackColor = Theme.Back };
            featureScroll = new Panel { AutoScroll = true, BackColor = Theme.Back };
            featuresContent = new Panel { BackColor = Theme.Back, Tag = "scroll-content" };
            featurePage.Controls.Add(groups); featurePage.Controls.Add(featureScroll); featureScroll.Controls.Add(featuresContent);
            AddPage("features", featurePage);
            var specs = Data.Rows(Data.Json.DeserializeObject(Data.Resource("Features.json")));
            foreach (string category in new[] { "全部" }.Concat(specs.Select(x => Data.Str(x, "group")).Distinct()))
            {
                string value = category; var button = new PillButton { Text = category, Name = "feature-group" };
                button.Click += delegate { SetGroup(value); }; groups.Controls.Add(button);
            }
            foreach (var spec in specs)
            {
                string category = Data.Str(spec, "group"); Panel section;
                if (!sections.TryGetValue(category, out section))
                { section = new Panel { BackColor = Theme.Back }; section.Controls.Add(LabelFor(category, 11, true)); sections[category] = section; featuresContent.Controls.Add(section); }
                var card = new FeatureCard(spec); card.Requested += FeatureRequested; card.KeyRequested += BindKey;
                cards[card.Id] = card; section.Controls.Add(card);
            }
        }
        void SetGroup(string value)
        {
            group = value; foreach (PillButton button in groups.Controls) button.Primary = button.Text == value;
            foreach (var section in sections) section.Value.Visible = value == "全部" || section.Key == value;
            featureScroll.AutoScrollPosition = Point.Empty; ArrangeFeatures();
        }
        void ArrangeFeatures()
        {
            if (featurePage == null || featurePage.Width < 1) return;
            foreach (PillButton button in groups.Controls)
            { button.Size = new Size(P(button.Text == "全部" ? 68 : 130), P(32)); button.Margin = new Padding(0, 0, P(7), P(7)); button.Primary = button.Text == group; }
            groups.SetBounds(0, P(4), featurePage.Width, Ui.FlowHeight(groups, featurePage.Width, P(39)));
            featureScroll.SetBounds(0, groups.Bottom + P(8), featurePage.Width, Math.Max(1, featurePage.Height - groups.Bottom - P(8)));
            var offset = featureScroll.AutoScrollPosition;
            int width = Math.Max(1, featureScroll.ClientSize.Width - P(19)), gap = P(16), y = 0;
            int columns = Ui.FeatureColumns(this, width), cardWidth = Math.Max(1, (width - (columns - 1) * gap) / columns);
            foreach (var pair in sections)
            {
                bool visible = group == "全部" || group == pair.Key; pair.Value.Visible = visible; if (!visible) continue;
                var items = pair.Value.Controls.OfType<FeatureCard>().ToList();
                int rows = (items.Count + columns - 1) / columns;
                pair.Value.SetBounds(0, y, width, P(40) + rows * P(142));
                pair.Value.Controls.OfType<Label>().First().SetBounds(P(4), P(5), Math.Max(1, width - P(8)), P(28));
                for (int i = 0; i < items.Count; i++) items[i].SetBounds((i % columns) * (cardWidth + gap), P(40) + (i / columns) * P(142), cardWidth, P(126));
                y += pair.Value.Height + P(8);
            }
            featuresContent.SetBounds(0, 0, width, y); featureScroll.AutoScrollMinSize = new Size(0, y);
            featureScroll.AutoScrollPosition = new Point(-offset.X, -offset.Y);
        }
        public void ShowPage(string id)
        {
            if (id.StartsWith("features:")) { SetGroup(id.Substring(9)); id = "features"; }
            if (!pageViews.ContainsKey(id)) id = "features"; page = id;
            foreach (var view in pageViews) view.Value.Visible = view.Key == id;
            foreach (var button in navigation) button.Value.Primary = button.Key == id;
            heading.Text = id == "features" ? "常用功能 · " + cards.Count : id == "cargo" ? "货物添加" : id == "world" ? "世界管理" : id == "camera" ? "镜头书签" : "连接诊断";
            pageHint.Text = id == "features" ? "选择分类，设置数值后再开启功能。" : id == "cargo" ? "选择货物 → 选择库存 → 设置每种数量并添加。" : id == "world" ? "选择城镇或产业，再修改参数、名称或定位镜头。" : id == "camera" ? "读取镜头后调整并应用，常用地点可保存为书签。" : "导出诊断，查看连接与功能不可用的具体原因。";
            Arrange();
        }
        public void SelectFirst() { foreach (var catalog in catalogs) catalog.SelectFirst(); }
        public void Notify(string text, bool error = false) { if (closing) return; status.Text = text; status.ForeColor = error ? Color.FromArgb(180, 45, 52) : Theme.Muted; tooltips.SetToolTip(status, text); }
        public bool IsAvailable(string id) { return CaptureReady && Data.Bool(Data.Get(Data.Get(State, "capabilities"), id), "supported"); }
        public string UnavailableReason(string id) { return Data.Str(Data.Get(Data.Get(State, "capabilities"), id), "reason", "当前接口尚未通过验证"); }
        public void ConnectGame()
        {
            if (offline || connecting || sending || closing) return;
            if (bridge != null && bridge.Ready) { Request(new { op = "refresh" }); return; }
            connecting = true; Enable(connect, false); Notify("正在连接游戏…"); var candidate = new Bridge();
            Task.Run(delegate {
                try
                {
                    candidate.Connect(); if (closing) { candidate.Dispose(); return; }
                    BeginInvoke((Action)delegate { if (closing) { candidate.Dispose(); return; } if (bridge != null) bridge.Dispose(); bridge = candidate; connecting = false; Enable(connect, true); lastSnapshot = ""; Notify("已连接游戏"); Poll(); });
                }
                catch (Exception e) { candidate.Dispose(); if (closing) return; BeginInvoke((Action)delegate { if (!closing) { connecting = false; Enable(connect, true); Notify(e.Message); } }); }
            });
        }
        public void Request(object command, Action<bool, string> completed = null)
        {
            string error = null;
            if (!CaptureReady) error = "请先连接游戏并进入地图。";
            else
            {
                var data = Data.Obj(Data.Json.DeserializeObject(Data.Json.Serialize(command))); string op = Data.Str(data, "op"), id = op == "set" ? Data.Str(data, "id") : op;
                if (op != "reset" && op != "refresh" && op != "camera_read" && !(op == "set" && !Data.Bool(data, "enabled")) && !IsAvailable(id)) error = UnavailableReason(id);
            }
            if (error != null) { Notify(error, true); if (completed != null) completed(false, error); return; }
            requests.Enqueue(new RequestItem { Command = command, Scope = NameScope, Completed = completed }); SendNext();
        }
        public void ReadCamera(bool refreshStocks, Action<bool, string> completed)
        {
            string scope = NameScope, map = Data.Str(State, "nameScope"), world = Data.Str(State, "world"), request = Guid.NewGuid().ToString("N");
            Request(new { op = "camera_read", world = Data.Get(State, "world"), request = request, refreshStocks = refreshStocks },
                delegate(bool success, string message) {
                    if (scope != NameScope) { if (completed != null) completed(false, "地图或连接已切换，请重新读取镜头。"); return; }
                    if (success) try
                    {
                        var reply = CameraData.ReadReply(Data.Json.DeserializeObject(bridge.Response), request, world, map, refreshStocks);
                        State["camera"] = Data.Get(reply, "camera"); State["cameraError"] = ""; State["cameraAt"] = Data.Get(reply, "cameraAt");
                        if (refreshStocks) State["stocks"] = Data.Get(reply, "stocks");
                    }
                    catch (Exception e) { success = false; message = e.Message; }
                    if (!success) { State["camera"] = new Dictionary<string, object>(); State["cameraAt"] = null; State["cameraError"] = message; Notify(message, true); }
                    // The command reply is current even when the periodic shared
                    // snapshot has not ticked yet. Apply it before calculating.
                    UpdateState(); if (completed != null) completed(success, message);
                });
        }
        void CancelQueued(string message)
        { while (requests.Count > 0) { var entry = requests.Dequeue(); if (entry.Completed != null) entry.Completed(false, message); } }
        void SendNext()
        {
            if (sending || requests.Count == 0 || bridge == null) return;
            activeRequest = requests.Dequeue();
            if (!CaptureReady || activeRequest.Scope != NameScope)
            {
                string message = "地图或连接已切换，已取消尚未执行的操作。";
                var entry = activeRequest; activeRequest = null;
                if (entry.Completed != null) entry.Completed(false, message); CancelQueued(message); Notify(message, true); return;
            }
            try { bridge.Send("return __SZTF3F6.dispatch(" + Data.LuaObject(activeRequest.Command) + ")"); sending = true; Notify("正在提交操作…"); }
            catch (Exception e) { var entry = activeRequest; activeRequest = null; Notify(e.Message); if (entry.Completed != null) entry.Completed(false, e.Message); CancelQueued(e.Message); }
        }
        void FeatureRequested(FeatureCard card)
        {
            if (card.Kind == "action" || card.Kind == "number_action") Request(new { op = card.Id, value = card.Number.Value });
            else Request(new { op = "set", id = card.Id, enabled = card.Toggle.Checked, value = card.Kind == "value" ? card.Number.Value : 1 });
        }
        void Poll()
        {
            if (bridge == null || connecting || closing) return;
            try
            {
                bridge.Poll();
                if (lastSnapshot != bridge.Snapshot)
                {
                    lastSnapshot = bridge.Snapshot; State = Data.Obj(Data.Json.DeserializeObject(lastSnapshot));
                    string runtimeVersion = Data.Str(Data.Get(State, "diagnostics"), "version");
                    if (runtimeVersion.Length > 0 && runtimeVersion != Protocol.Version)
                        throw new InvalidOperationException("旧版运行模块仍在游戏进程中，请完全退出游戏后重新连接。");
                    State["connection"] = new { pid = bridge.Pid, processStartTicks = bridge.ProcessStartTicks, path = bridge.GamePath, gameVersion = bridge.GameVersion,
                        steamBuild = bridge.GameBuild, pe = bridge.PeFingerprint, runtimeProtocol = Protocol.RuntimeProtocol };
                    UpdateState();
                }
                if (sending && !bridge.Pending)
                {
                    sending = false; var entry = activeRequest; activeRequest = null;
                    bool sameMap = entry != null && entry.Scope == NameScope && CaptureReady;
                    bool success = bridge.LastResult == 1 && sameMap;
                    string message = !sameMap ? "地图或连接已切换，已取消尚未执行的操作。" : success ? Data.Str(Data.Json.DeserializeObject(bridge.Response), "message", "操作完成") : bridge.Response;
                    Notify(message, !success); if (entry != null && entry.Completed != null) entry.Completed(success, message);
                    if (!success) CancelQueued(message); SendNext();
                }
                online.Text = CaptureReady ? Busy ? "● 游戏已连接 · 操作执行中" : "● 游戏已连接" : "● 游戏未连接";
                if (LayoutArea.Height < P(560)) { appCaption.Text = CaptureReady ? "已连接" : "未连接"; appCaption.ForeColor = CaptureReady ? Theme.Green : Theme.Muted; }
                foreach (var catalog in catalogs) catalog.UpdateActions(); camera.UpdateState(); Enable(reset, CaptureReady && !Busy);
            }
            catch (Exception e)
            {
                Notify(e.Message, true); bridge.Dispose(); bridge = null; State = new Dictionary<string, object>(); sending = false;
                if (activeRequest != null && activeRequest.Completed != null) activeRequest.Completed(false, e.Message);
                activeRequest = null; CancelQueued(e.Message); UpdateState();
            }
        }
        void UpdateState()
        {
            bool playing = CaptureReady; online.Text = playing ? "● 游戏已连接" : "● 游戏未连接"; online.ForeColor = playing ? Theme.Green : Theme.Muted;
            stats.Text = playing ? "结余 " + Data.Num(State, "balance").ToString("N0") + " · 贷款 " + Data.Num(State, "loan").ToString("N0") + " · 等级 " + Data.Num(State, "rank") +
                "\r\n" + Data.Str(State, "date") + " · 模拟 " + Data.Num(State, "speed") + "x · 经验 " + Data.Num(State, "experience").ToString("N0") : "请打开游戏，进入地图后连接。";
            var settings = Data.Obj(Data.Get(State, "settings")); var caps = Data.Obj(Data.Get(State, "capabilities"));
            foreach (var item in cards)
            { object value; bool enabled = settings.TryGetValue(item.Key, out value); if (!Busy) item.Value.State(enabled, value); item.Value.Capability(Data.Get(caps, item.Key), playing); }
            foreach (var catalog in catalogs) { catalog.UpdateRows(Data.Get(State, catalog.Kind == "cargo" ? "catalog" : "worlds")); catalog.UpdateStocks(); }
            if (camera != null) camera.UpdateState(); Enable(reset, playing && !Busy);
            if (diagnosis != null)
            {
                var d = Data.Get(State, "diagnostics");
                string text = "狂热运输3修改器 · v" + Protocol.Version + "\r\n\r\n" + (bridge == null ? "尚未连接" : bridge.IdentityText) + "\r\n\r\n" +
                    "可用功能：" + cards.Keys.Count(id => Data.Bool(Data.Get(caps, id), "supported")) + " / " + cards.Count +
                    "\r\n货物：" + Data.Rows(Data.Get(State, "catalog")).Count + "\r\n城镇 / 产业：" + Data.Rows(Data.Get(State, "worlds")).Count +
                    "\r\n镜头中心：" + (CameraData.HasCenter(Data.Get(State, "camera")) ? Data.Num(Data.Get(State, "camera"), "x").ToString("N1") + ", " + Data.Num(Data.Get(State, "camera"), "y").ToString("N1") : "未读取") +
                    "\r\n镜头读取错误：" + Data.Str(State, "cameraError", "无") + "\r\n有坐标的库存：" + Data.Rows(Data.Get(State, "stocks")).Count(r => CameraData.HasCenter(Data.Get(r, "position"))) +
                    "\r\n库存坐标错误：" + (Data.Rows(Data.Get(State, "stocks")).Select(r => Data.Str(r, "positionError")).FirstOrDefault(e => e.Length > 0) ?? "无") +
                    "\r\n命令计数：" + Data.Num(d, "commands") + "\r\n待恢复参数：" + Data.Num(d, "backups") +
                    "\r\n\r\n状态：" + Data.Str(State, "message") + "\r\n错误：" + Data.Str(State, "error", "无") +
                    "\r\n\r\n游戏名称优先；读不到时可设置本工具当前地图会话的显示名称。" +
                    "\r\n保存到游戏的名称随游戏存档保留。资金、经验、日期和其他一次性修改不会随开关关闭而撤销。";
                foreach (var item in caps) if (!Data.Bool(item.Value, "supported")) text += "\r\n" + item.Key + "：" + Data.Str(item.Value, "reason");
                if (diagnosis.Text != text) diagnosis.Text = text;
            }
        }
        string NameKey(object row) { return NameScope + "/" + Data.Str(row, "nameEntity", Data.Str(row, "id")); }
        public string DisplayName(Dictionary<string, object> row)
        {
            string game = Data.Str(row, "gameName"); if (game.Length > 0) return game;
            bool entity = row.ContainsKey("nameEntity") || Data.Str(row, "category") == "城镇" || Data.Str(row, "category") == "产业";
            string local; if (entity && NameScope.Length > 0 && Prefs.displayNames.TryGetValue(NameKey(row), out local) && local.Length > 0) return local;
            return Data.Str(row, "name", "未命名");
        }
        public void SaveDisplayName(Dictionary<string, object> row, string value)
        {
            if (NameScope.Length == 0 || Data.Str(row, "gameName").Length > 0) return;
            Prefs.displayNames[NameKey(row)] = value; SavePrefs(); DisplayNameRevision++;
            foreach (var catalog in catalogs) catalog.InvalidateNames(); Notify("已保存显示名称，仅用于当前地图会话。");
        }
        public void SavePrefs() { try { Prefs.Save(); } catch (Exception e) { Notify("设置保存失败：" + e.Message); } }
        public void Remember(string kind, string id)
        { RememberMany(kind, new[] { id }); }
        public void RememberMany(string kind, IEnumerable<string> ids)
        { var recent = Prefs.Set(kind, true); foreach (string id in ids) { recent.Remove(id); recent.Insert(0, id); } if (recent.Count > 30) recent.RemoveRange(30, recent.Count - 30); SavePrefs(); }
        void BindKey(FeatureCard card)
        {
            int key; Prefs.keys.TryGetValue(card.Id, out key);
            using (var dialog = new KeyDialog(Data.Str(card.Spec, "name"), (Keys)key))
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    if (dialog.Selected == Keys.None) Prefs.keys.Remove(card.Id);
                    else { if (Prefs.keys.Any(p => p.Key != card.Id && p.Value == (int)dialog.Selected)) { Notify("该快捷键已绑定其他功能。"); return; } Prefs.keys[card.Id] = (int)dialog.Selected; }
                    SavePrefs(); RegisterKeys();
                }
        }
        void RegisterKeys()
        {
            if (offline) return; foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(Handle, id); hotkeys.Clear(); int next = 100;
            foreach (var card in cards)
            {
                int key; if (!Prefs.keys.TryGetValue(card.Key, out key)) { card.Value.Hotkey.Text = "设置快捷键"; continue; }
                Keys value = (Keys)key; uint modifiers = 0x4000;
                if ((value & Keys.Control) != 0) modifiers |= 2; if ((value & Keys.Shift) != 0) modifiers |= 4; if ((value & Keys.Alt) != 0) modifiers |= 1;
                if (Native.RegisterHotKey(Handle, next, modifiers, (uint)(value & Keys.KeyCode))) { hotkeys[next++] = card.Key; card.Value.Hotkey.Text = new KeysConverter().ConvertToString(value); }
                else { card.Value.Hotkey.Text = "绑定冲突"; Notify("快捷键被占用，请重新绑定。"); }
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x312)
            {
                string id;
                if (hotkeys.TryGetValue(message.WParam.ToInt32(), out id) && CaptureReady && !Busy && cards[id].Supported)
                { var card = cards[id]; if (card.Kind == "action" || card.Kind == "number_action") FeatureRequested(card); else card.Toggle.Checked = !card.Toggle.Checked; }
            }
            base.WndProc(ref message);
        }
        public bool ValidateLayout(string output)
        {
            var report = new List<object>();
            try
            {
                if (!offline || bridge != null) throw new InvalidOperationException("布局检查必须与游戏连接隔离。");
                bool accessBlocked = false; try { BuildMode.RequireLiveAccess(); } catch (InvalidOperationException) { accessBlocked = true; }
                if (!accessBlocked) throw new InvalidOperationException("离线检查没有阻止游戏连接。");
                report.Add(new { buildGuard = "live game access blocked", pass = true });
                if (AutoScaleMode != AutoScaleMode.None || FormBorderStyle != FormBorderStyle.Sizable || !MaximizeBox)
                    throw new Exception("主窗口未启用等比缩放、最大化和手动 DPI 布局");
                foreach (var maximum in new[] { new Size(1280, 720), new Size(1920, 1080), new Size(2560, 1440), new Size(800, 600) })
                {
                    var fit = Ui.FitFixedClient(new Size(2560, 1600), maximum);
                    if (fit.Width > maximum.Width || fit.Height > maximum.Height || fit.Width * 10 != fit.Height * 16)
                        throw new Exception("固定比例窗口超出屏幕或比例错误");
                }
                foreach (int edge in Enumerable.Range(1, 8)) foreach (var proposed in new[] { new Rectangle(20, 30, 1500, 700), new Rectangle(20, 30, 800, 950) })
                {
                    var resized = Ui.ResizeAspectBounds(proposed, edge, new Size(16, 39), new Size(1280, 800), new Size(704, 440), new Size(1904, 1000));
                    int cw = resized.Width - 16, ch = resized.Height - 39;
                    if (Math.Abs(cw * 10 - ch * 16) > 8 || cw < 704 || ch < 440 || cw > 1904 || ch > 1000)
                        throw new Exception("拖动缩放时比例或边界错误");
                    if ((edge == 1 || edge == 4 || edge == 7) && resized.Right != proposed.Right || (edge == 3 || edge == 4 || edge == 5) && resized.Bottom != proposed.Bottom)
                        throw new Exception("拖动缩放时对边未保持固定");
                }
                var restoreWindow = Bounds;
                WindowState = FormWindowState.Maximized; Application.DoEvents();
                var maximizedArea = LayoutArea;
                if (!ClientRectangle.Contains(maximizedArea) || Math.Abs(maximizedArea.Width * 10 - maximizedArea.Height * 16) > 16)
                    throw new Exception("最大化后的等比内容区域无效");
                WindowState = FormWindowState.Normal; Bounds = restoreWindow; Application.DoEvents();
                report.Add(new { window = "16:10, resizable from every edge, maximize enabled, single DPI scaling", pass = true });
                var cameraReply = Data.Json.DeserializeObject("{\"ok\":true,\"request\":\"camera-test\",\"world\":1,\"nameScope\":\"layout\",\"camera\":{\"x\":12,\"y\":34},\"stocks\":[]}");
                var freshCamera = CameraData.ReadReply(cameraReply, "camera-test", "1", "layout", true);
                if (!CameraData.HasCenter(Data.Get(freshCamera, "camera")) || CameraData.HasPose(Data.Get(freshCamera, "camera"))) throw new Exception("镜头中心和完整参数未分别验证");
                bool staleBlocked = false; try { CameraData.ReadReply(cameraReply, "old-request", "1", "layout", true); } catch (InvalidDataException) { staleBlocked = true; }
                if (!staleBlocked) throw new Exception("过期镜头回复未被拒绝");
                staleBlocked = false; try { CameraData.ReadReply(cameraReply, "camera-test", "2", "layout", true); } catch (InvalidDataException) { staleBlocked = true; }
                if (!staleBlocked) throw new Exception("其他地图的镜头回复未被拒绝");
                report.Add(new { cameraRead = "correlated response, current map, center versus pose", pass = true });
                var preview = new { playing = true, world = 1, nameScope = "layout", camera = new { x = 0, y = 0, z = 500, w = 0, v = 1 }, catalog = new[] { new { id = "1", name = "原油", category = "货物", description = "货物示例" }, new { id = "2", name = "木材", category = "货物", description = "货物示例" } },
                    worlds = new object[] { new { id = "20", name = "北港采油平台", gameName = "北港采油平台", category = "产业", productivity = 1, description = "产业名称读取示例" },
                        new { id = "21", name = "新港", gameName = "新港", category = "城镇", res = 500, commercial = 200, industry = 200, description = "城镇示例" },
                        new { id = "22", name = "采油平台", gameName = "", category = "产业", productivity = 1, description = "名称读取回退示例" } },
                    stocks = new object[] {
                        new { id = "30", nameEntity = "20", name = "北港采油平台", gameName = "北港采油平台", category = "产业", position = new { x = 200, y = 0 }, slots = new[] { new { index = 0, cargo = 1, mixed = true, allowed = new[] { "1", "2" }, capacity = 200, count = 0 } } },
                        new { id = "31", name = "已满仓库", category = "仓库", position = new { x = 10, y = 0 }, slots = new[] { new { index = 0, cargo = 1, mixed = true, allowed = new[] { "1", "2" }, capacity = 100, count = 100 } } },
                        new { id = "32", name = "附近仓库", category = "仓库", position = new { x = 50, y = 0 }, slots = new[] { new { index = 0, cargo = 1, mixed = true, allowed = new[] { "1", "2" }, capacity = 100, count = 0 } } },
                        new { id = "33", name = "未知位置库存", category = "仓库", slots = new[] { new { index = 0, cargo = 1, mixed = true, allowed = new[] { "1", "2" }, capacity = 1000, count = 0 } } }
                    } };
                State = Data.Obj(Data.Json.DeserializeObject(Data.Json.Serialize(preview))); UpdateState(); SelectFirst(); MinimumSize = Size.Empty;
                var unnamed = Data.Rows(Data.Get(State, "worlds"))[2]; string aliasKey = NameKey(unnamed), previousAlias;
                bool hadAlias = Prefs.displayNames.TryGetValue(aliasKey, out previousAlias);
                try
                {
                    Prefs.displayNames[aliasKey] = "本机显示名称";
                    if (DisplayName(unnamed) != "本机显示名称") throw new Exception("名称回退异常");
                    unnamed["gameName"] = "游戏名称";
                    if (DisplayName(unnamed) != "游戏名称") throw new Exception("游戏名称优先级异常");
                    unnamed["gameName"] = ""; State["nameScope"] = "different-map";
                    if (DisplayName(unnamed) != "采油平台") throw new Exception("显示名称跨地图误用");
                }
                finally { unnamed["gameName"] = ""; State["nameScope"] = "layout"; if (hadAlias) Prefs.displayNames[aliasKey] = previousAlias; else Prefs.displayNames.Remove(aliasKey); }
                report.Add(new { names = "game-first, fallback, map-isolation", pass = true });
                var cargoIds = new List<string> { "1", "2" };
                var choices = CargoTargets.Find(Data.Get(State, "stocks"), cargoIds, 30, Data.Get(State, "camera"), DisplayName);
                if (CargoTargets.Recommend(choices).Id != "32") throw new Exception("推荐没有排除已满库存或未知位置");
                choices = CargoTargets.Find(Data.Get(State, "stocks"), cargoIds, 60, Data.Get(State, "camera"), DisplayName);
                var clipped = CargoTargets.Recommend(choices);
                if (clipped.Id != "32" || !clipped.Clipped || clipped.Allocation.Total != 100) throw new Exception("多种货物的共享容量裁剪异常");
                var matching = Data.Json.DeserializeObject("{\"slots\":[{\"allowed\":[\"1\",\"2\"],\"capacity\":1,\"count\":0,\"mixed\":false},{\"allowed\":[\"1\"],\"capacity\":1,\"count\":0,\"mixed\":false}]}");
                var plan = CargoTargets.Plan(matching, cargoIds, 1);
                if (plan == null || plan[0] != 1 || plan[1] != 0) throw new Exception("货物槽位匹配异常");
                // Hidden pages have no native ListView handle yet, so changing
                // Checked cannot exercise the real ItemChecked event path.
                ShowPage("cargo"); Arrange(); Application.DoEvents();
                catalogs.First(c => c.Kind == "cargo").ValidateCargoFlow();
                report.Add(new { cargo = "nearest-compatible, shared-capacity, matching, selection-preservation", pass = true });
                var cases = new[] { new { w = 604, h = 401, scale = 1f }, new { w = 640, h = 480, scale = 1f }, new { w = 1280, h = 720, scale = 1f },
                    new { w = 1366, h = 768, scale = 1.25f }, new { w = 1920, h = 1080, scale = 1.5f },
                    new { w = 1920, h = 1080, scale = 2f }, new { w = 2560, h = 1440, scale = 1f }, new { w = 2560, h = 1440, scale = 2f } };
                foreach (var test in cases)
                {
                    Ui.LayoutTestScale = test.scale; ClientSize = new Size(test.w, test.h);
                    foreach (string id in pageViews.Keys)
                    {
                        ShowPage(id); SelectFirst(); Arrange(); Application.DoEvents(); var issues = new List<string>(); Audit(this, issues);
                        if (id == "cargo" || id == "world") catalogs.First(c => c.Kind == id).ValidateDetailScroll();
                        if (sidebar.Parent != this || navigationBar.Parent != sidebar || actions.Parent != sidebar || sidebar.Right >= pages.Left)
                            issues.Add("page navigation must stay in a separate left sidebar");
                        if (id == "features")
                        {
                            var sideBounds = sidebar.Bounds; var navPosition = navigationBar.PointToScreen(Point.Empty);
                            featureScroll.AutoScrollPosition = new Point(0, P(100)); Application.DoEvents();
                            if (sidebar.Bounds != sideBounds || navigationBar.PointToScreen(Point.Empty) != navPosition)
                                issues.Add("sidebar moved when page content scrolled");
                            featureScroll.AutoScrollPosition = Point.Empty;
                        }
                        if (issues.Count > 0) throw new InvalidOperationException(id + " " + test.w + "x" + test.h + ": " + string.Join("; ", issues));
                        report.Add(new { page = id, width = test.w, height = test.h, layoutScale = test.scale, monitorDpi = DeviceDpi, pass = true });
                        if (id == "world") foreach (int variant in new[] { 1, 2 })
                        {
                            catalogs.First(c => c.Kind == "world").SelectForLayoutCheck(variant); Arrange(); Application.DoEvents();
                            issues.Clear(); Audit(this, issues); if (issues.Count > 0) throw new Exception(string.Join("; ", issues));
                            report.Add(new { page = id, variant = variant, width = test.w, height = test.h, layoutScale = test.scale, pass = true });
                        }
                    }
                }
                File.WriteAllText(output, Data.Json.Serialize(report), System.Text.Encoding.UTF8); return true;
            }
            catch (Exception e) { report.Add(new { error = e.ToString(), pass = false }); File.WriteAllText(output, Data.Json.Serialize(report), System.Text.Encoding.UTF8); return false; }
            finally { Ui.LayoutTestScale = 0; }
        }
        static void Audit(Control parent, List<string> issues)
        {
            var scroll = parent as ScrollableControl; bool canScroll = scroll != null && scroll.AutoScroll;
            foreach (Control child in parent.Controls)
            {
                if (!child.Visible) continue;
                if (child.Name.Length > 0 && !canScroll && (child.Left < -2 || child.Top < -2 || child.Right > parent.ClientSize.Width + 2 || child.Bottom > parent.ClientSize.Height + 2))
                    issues.Add(child.Name + " exceeds " + parent.GetType().Name);
                if (child is PillButton && child.Name.Length > 0)
                {
                    var text = TextRenderer.MeasureText(child.Text, child.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                    if (text.Width > child.ClientSize.Width - Ui.Px(child, 14) || text.Height > child.ClientSize.Height)
                        issues.Add(child.Name + " button text is clipped");
                }
                Audit(child, issues);
            }
            if (parent is FeatureCard)
            {
                var controls = parent.Controls.Cast<Control>().Where(c => c.Visible && c.Name.Length > 0).ToList();
                for (int i = 0; i < controls.Count; i++) for (int j = i + 1; j < controls.Count; j++)
                    if (Rectangle.Intersect(controls[i].Bounds, controls[j].Bounds).Width > 2 && Rectangle.Intersect(controls[i].Bounds, controls[j].Bounds).Height > 2)
                        issues.Add("overlap: " + controls[i].Name + " / " + controls[j].Name);
            }
        }
        public async Task<bool> Regression(string output)
        {
            var log = new List<object>();
            try
            {
                for (int i = 0; i < 600 && !CaptureReady; i++) await Task.Delay(50);
                if (!CaptureReady) throw new Exception("连接失败");
                if (cards.Count != 19 || navigation.Count != 5) throw new Exception("功能菜单数量异常");
                ShowPage("features");
                foreach (string id in new[] { "vehicle_speed", "productivity" })
                {
                    var card = cards[id]; card.Toggle.Checked = true; await WaitToggle(id, true);
                    SetGroup(Data.Str(card.Spec, "group")); card.Toggle.Checked = false; await WaitToggle(id, false); SetGroup("全部");
                    log.Add(new { id = id, pass = true });
                }
                File.WriteAllText(output, Data.Json.Serialize(log), System.Text.Encoding.UTF8); return true;
            }
            catch (Exception e) { log.Add(new { error = e.ToString() }); File.WriteAllText(output, Data.Json.Serialize(log), System.Text.Encoding.UTF8); return false; }
        }
        async Task WaitToggle(string id, bool value)
        { for (int i = 0; i < 200; i++) { if (!Busy && Data.Obj(Data.Get(State, "settings")).ContainsKey(id) == value) return; await Task.Delay(50); } throw new Exception("开关状态未同步：" + id); }
    }
}
