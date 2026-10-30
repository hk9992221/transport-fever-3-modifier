using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TransportFever3Trainer
{
    sealed class IndustryChoice
    {
        public string Id = "", Label = "";
        public override string ToString() { return Label; }
    }

    sealed class IndustryNavState
    {
        public string EntityId = "", Search = "", TypeKey = "*", Direction = "*", GroupDirection = "", GroupCargo = "";
        public int Visible = 100, ScrollY;
    }

    sealed class IndustryResultRow : Surface
    {
        public readonly IndustryCandidate Candidate;
        readonly Label name, meta;
        readonly PillButton locate, analyze;
        public IndustryResultRow(IndustryCandidate candidate, Action<IndustryCandidate> onLocate, Action<IndustryCandidate> onAnalyze)
        {
            Candidate = candidate; Radius = 8; BackColor = Theme.Card; BorderVisible = true; Name = "industry-result-row";
            name = MainForm.LabelFor(candidate.Name, 10, true);
            meta = MainForm.LabelFor(candidate.TypeName + " · " + candidate.Relation + " · " + candidate.DistanceText, 8.5f);
            meta.ForeColor = Theme.Muted;
            locate = new PillButton { Text = candidate.TargetKind == "城镇" ? "定位城镇" : "定位建筑", Font = Theme.F(8.5f), Name = "industry-row-locate" };
            locate.Click += delegate { onLocate(candidate); };
            analyze = new PillButton { Text = "查看产业链", Font = Theme.F(8.5f), Name = "industry-row-analyze" };
            analyze.Click += delegate { onAnalyze(candidate); };
            Controls.Add(name); Controls.Add(meta); Controls.Add(locate);
            if (candidate.TargetKind != "城镇") Controls.Add(analyze);
        }
        public void Arrange(int width)
        {
            int p = Ui.Px(this, 10), buttonHeight = Ui.ButtonHeight(locate, 26);
            int locateWidth = Ui.ButtonWidth(locate, 76), analyzeWidth = analyze.Parent == null ? 0 : Ui.ButtonWidth(analyze, 88);
            int buttonsWidth = locateWidth + (analyze.Parent == null ? 0 : analyzeWidth + Ui.Px(this, 6));
            name.SetBounds(p, Ui.Px(this, 5), Math.Max(1, width - buttonsWidth - p * 2), Ui.Px(this, 22));
            meta.SetBounds(p, Ui.Px(this, 27), Math.Max(1, width - buttonsWidth - p * 2), Ui.Px(this, 26));
            int right = width - p;
            if (analyze.Parent != null)
            {
                analyze.SetBounds(right - analyzeWidth, (Height - buttonHeight) / 2, analyzeWidth, buttonHeight);
                right -= analyzeWidth + Ui.Px(this, 6);
            }
            locate.SetBounds(right - locateWidth, (Height - buttonHeight) / 2, locateWidth, buttonHeight);
        }
        public void UpdateState(bool online, bool focusAvailable)
        {
            MainForm.Enable(locate, online && focusAvailable);
            if (analyze.Parent != null) MainForm.Enable(analyze, online);
        }
    }

    sealed class IndustryPage : Panel
    {
        readonly MainForm form;
        readonly Panel canvas = new Panel { Tag = "scroll-content" };
        readonly Surface selectorCard = new Surface(), analysisCard = new Surface(), inputCard = new Surface(), outputCard = new Surface(), resultsCard = new Surface();
        readonly TextBox search;
        readonly ThemedComboBox typeFilter, buildingFilter, directionFilter;
        readonly ListView inputList, outputList;
        readonly PillButton clearFilters, cameraRecommend, refreshIndex, locateAnalysis, backAnalysis, loadMore;
        readonly Label analysisTitle, analysisMeta, analysisTags, analysisNote, matchInfo, indexStatus, inputTitle, outputTitle;
        readonly Label resultsTitle, resultsNote, resultsEmpty;
        readonly Panel resultsScroll, resultsContent;
        readonly List<IndustryResultRow> rowControls = new List<IndustryResultRow>();
        IndustryIndex index = new IndustryIndex(), pendingIndex;
        readonly Stack<IndustryNavState> history = new Stack<IndustryNavState>();
        IndustryAnalysis analysis;
        IndustryGroup activeGroup;
        string selectedType = "*", selectedDirection = "*", lastScope = "", pageError = "";
        int seenMetaRevision, visibleCount = 100, requestSerial, cargoTotal, townTotal;
        bool arranging, populating, loading, preview, recommending, catalogComplete, activated, retriedInitial;
        int P(int value) { return Ui.Px(this, value); }

        public IndustryPage(MainForm parent)
        {
            form = parent; AutoScroll = true; BackColor = Theme.Back; canvas.BackColor = Theme.Back;
            Controls.Add(canvas); canvas.Controls.Add(selectorCard); canvas.Controls.Add(analysisCard);
            canvas.Controls.Add(inputCard); canvas.Controls.Add(outputCard); canvas.Controls.Add(resultsCard);

            search = new TextBox { Font = Theme.F(10), BackColor = Theme.Card, ForeColor = Theme.Ink, BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "搜索产业名称、类型或货物", Name = "industry-search" };
            search.TextChanged += delegate { if (!populating) { visibleCount = 100; RefillBuildings(false); UpdateActions(); } };
            selectorCard.Controls.Add(search);
            clearFilters = new PillButton { Text = "清除筛选", Name = "industry-clear-filters" };
            clearFilters.Click += delegate
            {
                populating = true;
                try { search.Clear(); selectedType = "*"; selectedDirection = "*"; FillTypeChoices(); FillDirection(); } finally { populating = false; }
                RefillBuildings(false); UpdateActions();
            };
            selectorCard.Controls.Add(clearFilters);
            typeFilter = new ThemedComboBox { Font = Theme.F(10), AccessibleName = "产业类型筛选", Name = "industry-type-filter" };
            typeFilter.SelectedIndexChanged += delegate
            {
                if (populating) return; var choice = typeFilter.SelectedItem as IndustryChoice;
                selectedType = choice == null ? "*" : choice.Id; visibleCount = 100; RefillBuildings(false); UpdateActions();
            };
            selectorCard.Controls.Add(typeFilter);
            buildingFilter = new ThemedComboBox { Font = Theme.F(10), AccessibleName = "手动选择建筑", Name = "industry-building-filter" };
            buildingFilter.SelectedIndexChanged += delegate
            {
                if (populating) return; var choice = buildingFilter.SelectedItem as IndustryChoice;
                if (choice == null || choice.Id.Length == 0) return;
                Navigate(choice.Id, true);
            };
            selectorCard.Controls.Add(buildingFilter);
            directionFilter = new ThemedComboBox { Font = Theme.F(10), AccessibleName = "货物方向筛选", Name = "industry-direction-filter" };
            directionFilter.SelectedIndexChanged += delegate
            {
                if (populating) return; var choice = directionFilter.SelectedItem as IndustryChoice;
                selectedDirection = choice == null ? "*" : choice.Id; visibleCount = 100; RefillBuildings(false); UpdateActions();
            };
            selectorCard.Controls.Add(directionFilter);
            cameraRecommend = new PillButton { Text = "镜头推荐", Name = "industry-camera-recommend" };
            cameraRecommend.Click += delegate { RecommendFromCamera(); };
            refreshIndex = new PillButton { Text = "刷新产业数据", Name = "industry-refresh" };
            refreshIndex.Click += delegate { RefreshIndex(); };
            selectorCard.Controls.Add(cameraRecommend); selectorCard.Controls.Add(refreshIndex);
            matchInfo = MainForm.LabelFor("", 8.5f); matchInfo.ForeColor = Theme.Muted; matchInfo.Name = "industry-match-info"; selectorCard.Controls.Add(matchInfo);
            indexStatus = MainForm.LabelFor("连接游戏并进入地图后，可查看产业推荐。", 8.5f); indexStatus.ForeColor = Theme.Muted; indexStatus.Name = "industry-index-status"; selectorCard.Controls.Add(indexStatus);

            analysisTitle = MainForm.LabelFor("尚未选择产业", 14, true); analysisTitle.Name = "industry-analysis-title"; analysisCard.Controls.Add(analysisTitle);
            analysisMeta = MainForm.LabelFor("", 9); analysisMeta.ForeColor = Theme.Muted; analysisMeta.Name = "industry-analysis-meta"; analysisCard.Controls.Add(analysisMeta);
            analysisTags = MainForm.LabelFor("", 9); analysisTags.ForeColor = Theme.Ink; analysisTags.Name = "industry-analysis-tags"; analysisCard.Controls.Add(analysisTags);
            locateAnalysis = new PillButton { Text = "定位当前建筑", Name = "industry-locate-analysis" };
            locateAnalysis.Click += delegate { if (analysis != null) form.Request(new { op = "focus", id = analysis.Entry.EntityId }); };
            analysisCard.Controls.Add(locateAnalysis);
            backAnalysis = new PillButton { Text = "返回上一分析", Name = "industry-back-analysis" };
            backAnalysis.Click += delegate { Back(); };
            analysisCard.Controls.Add(backAnalysis);
            analysisNote = MainForm.LabelFor("距离基准始终为当前分析建筑；定位候选不会改变分析起点。", 8.5f);
            analysisNote.ForeColor = Theme.Muted; analysisNote.Name = "industry-analysis-note"; analysisCard.Controls.Add(analysisNote);

            inputTitle = MainForm.LabelFor("原料供应", 11, true); inputCard.Controls.Add(inputTitle);
            outputTitle = MainForm.LabelFor("产品去向", 11, true); outputCard.Controls.Add(outputTitle);
            inputList = GroupList(inputCard); outputList = GroupList(outputCard);
            inputList.SelectedIndexChanged += delegate { SelectGroup(inputList, outputList); };
            outputList.SelectedIndexChanged += delegate { SelectGroup(outputList, inputList); };

            resultsTitle = MainForm.LabelFor("选择货物分组", 11, true); resultsCard.Controls.Add(resultsTitle);
            resultsNote = MainForm.LabelFor("", 8.5f); resultsNote.ForeColor = Theme.Muted; resultsCard.Controls.Add(resultsNote);
            resultsScroll = new Panel { AutoScroll = true, BackColor = Theme.Card, Name = "industry-results-scroll" };
            resultsContent = new Panel { BackColor = Theme.Card, Tag = "scroll-content" };
            resultsScroll.Controls.Add(resultsContent); resultsCard.Controls.Add(resultsScroll);
            resultsEmpty = MainForm.LabelFor("选择产业后，这里会列出全部原料供应与产品去向。", 9); resultsEmpty.ForeColor = Theme.Muted;
            resultsEmpty.TextAlign = ContentAlignment.MiddleCenter; resultsEmpty.Name = "industry-results-empty"; resultsCard.Controls.Add(resultsEmpty);
            loadMore = new PillButton { Text = "加载更多", Name = "industry-load-more" };
            loadMore.Click += delegate { visibleCount += 100; RebuildResults(false); Arrange(); };
            resultsCard.Controls.Add(loadMore);

            SizeChanged += delegate { Arrange(); };
            FillDirection();
            Arrange(); UpdateActions();
        }

        ListView GroupList(Control parent)
        {
            var list = new ListView
            {
                View = View.Details, HeaderStyle = ColumnHeaderStyle.None, FullRowSelect = true, HideSelection = false,
                MultiSelect = false, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Card, ForeColor = Theme.Ink,
                Font = Theme.F(10), Name = "industry-group-list"
            };
            list.Columns.Add("货物", 150); list.Columns.Add("产业", 50); list.Columns.Add("城镇", 50);
            parent.Controls.Add(list); return list;
        }

        void FillDirection()
        {
            populating = true;
            try
            {
                directionFilter.Items.Clear();
                directionFilter.Items.Add(new IndustryChoice { Id = "*", Label = "全部方向" });
                directionFilter.Items.Add(new IndustryChoice { Id = "consume", Label = "消耗该货物" });
                directionFilter.Items.Add(new IndustryChoice { Id = "produce", Label = "生产该货物" });
                int found = 0; for (int i = 0; i < directionFilter.Items.Count; i++) if (((IndustryChoice)directionFilter.Items[i]).Id == selectedDirection) { found = i; break; }
                if (found == 0 && selectedDirection != "*") selectedDirection = "*";
                directionFilter.SelectedIndex = found;
            }
            finally { populating = false; }
        }

        public void Activate()
        {
            activated = true;
            if (form.CaptureReady && index.Industries.Count == 0 && !catalogComplete && !loading && !preview) StartLoad(false);
            UpdateState();
        }

        public void UpdateState()
        {
            if (arranging) return;
            bool online = form.CaptureReady;
            string scope = form.NameScope;
            if (scope != lastScope)
            {
                lastScope = scope; seenMetaRevision = 0;
                index.Reset(); pendingIndex = null; analysis = null; activeGroup = null; history.Clear(); catalogComplete = false;
                currentAnalysisId = ""; resumeGroupDirection = ""; resumeGroupCargo = ""; resumeVisible = 100; resumeScrollY = 0; cargoTotal = townTotal = 0;
                loading = false; pageError = ""; retriedInitial = false; selectedType = "*"; selectedDirection = "*";
                preview = false; search.Text = ""; FillDirection(); FillTypeChoices(); RefreshAnalysisCard(); RefreshGroups(); RebuildResults(true);
            }
            if (!online)
            {
                if (index.Industries.Count == 0) indexStatus.Text = "连接游戏并进入地图后，可查看产业推荐。";
                UpdateActions(); return;
            }
            var meta = Data.Obj(Data.Get(form.State, "industryIndex"));
            int metaRevision = (int)Data.Num(meta, "revision");
            if (activated && !preview && metaRevision > 0 && metaRevision != seenMetaRevision)
            {
                seenMetaRevision = metaRevision;
                if (!loading && (index.Revision != metaRevision || !catalogComplete)) StartLoad(false);
            }
            if (activated && !preview && !loading && index.Industries.Count == 0 && !catalogComplete)
            {
                if (pageError.Length == 0) StartLoad(false);
                else if (!retriedInitial && form.IsAvailable("industry")) { retriedInitial = true; pageError = ""; StartLoad(false); }
            }
            UpdateIndexStatus(meta);
            UpdateActions();
        }

        void UpdateIndexStatus(Dictionary<string, object> meta)
        {
            if (loading)
            {
                var source = pendingIndex ?? index;
                int loaded = source.Industries.Count, total = Math.Max(source.IndustryCount, source.Industries.Count);
                indexStatus.Text = total > 0 ? "正在读取产业目录：已加载 " + loaded + " / 共 " + total : "正在读取产业目录…";
                return;
            }
            if (pageError.Length > 0) { indexStatus.Text = "产业索引读取失败：" + pageError + (index.Industries.Count > 0 ? "；仍显示上次结果" : ""); return; }
            if (index.Revision == 0) { indexStatus.Text = "产业目录尚未读取。"; return; }
            int known = index.Industries.Count, totalCount = index.IndustryCount > 0 ? index.IndustryCount : known;
            string text = "索引 v" + index.Revision + " · 产业 " + totalCount + " / 城镇 " + Math.Max(index.TownCount, index.Towns.Count) +
                (catalogComplete ? " · 已读取完整目录" : " · 已确认 " + known + "，仍有 " + Math.Max(0, totalCount - known) + " 个待读取");
            if (index.UnreadIndustryCount > 0) text += " · 配方未读取 " + index.UnreadIndustryCount;
            if (index.UnreadTownCount > 0) text += " · 城镇需求未读取 " + index.UnreadTownCount;
            indexStatus.Text = text;
            matchInfo.Text = "当前匹配 " + Filtered().Count() + " 座 / 共 " + totalCount + " 座";
        }

        string resumeGroupDirection = "", resumeGroupCargo = "";
        int resumeVisible = 100, resumeScrollY;
        void StartLoad(bool refresh)
        {
            if (preview || !form.CaptureReady) return;
            if (currentAnalysisId.Length == 0 && analysis != null) currentAnalysisId = analysis.Entry.EntityId;
            loading = true; pageError = ""; catalogComplete = false; cargoTotal = townTotal = 0; pendingIndex = new IndustryIndex();
            resumeGroupDirection = activeGroup == null ? "" : activeGroup.Direction;
            resumeGroupCargo = activeGroup == null ? "" : activeGroup.CargoId;
            resumeVisible = visibleCount; resumeScrollY = resultsScroll.AutoScrollPosition.Y;
            seenMetaRevision = 0;
            UpdateActions();
            if (refresh) RequestRefresh(); else RequestCatalogPage(0);
        }

        void RequestRefresh()
        {
            string request = "industry-refresh-" + (++requestSerial).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string scope = form.NameScope, world = Data.Str(form.State, "world"), map = Data.Str(form.State, "nameScope");
            
            form.RequestReply(new { op = "industry_refresh", request = request, world = Data.Get(form.State, "world") }, delegate(bool success, string message, object reply)
            {
                if (scope != form.NameScope) return;
                
                if (!success)
                {
                    pendingIndex = null; loading = false; pageError = message; catalogComplete = index.Industries.Count > 0;
                    UpdateIndexStatus(new Dictionary<string, object>()); UpdateActions(); return;
                }
                var root = Data.Obj(reply);
                seenMetaRevision = (int)Data.Num(root, "revision");
                RequestCatalogPage(0);
            });
        }

        void RequestCatalogPage(int offset, string part = "industry")
        {
            if (preview || !form.CaptureReady) return;
            string request = "industry-catalog-" + (++requestSerial).ToString(System.Globalization.CultureInfo.InvariantCulture);
            string scope = form.NameScope, world = Data.Str(form.State, "world"), map = Data.Str(form.State, "nameScope");
            form.RequestReply(new { op = "industry_catalog", request = request, world = Data.Get(form.State, "world"), offset = offset, limit = 100, part = part },
                delegate(bool success, string message, object reply)
                {
                    if (scope != form.NameScope) return;
                    if (!success)
                    {
                        pendingIndex = null; loading = false; pageError = message; catalogComplete = index.Industries.Count > 0;
                        UpdateIndexStatus(new Dictionary<string, object>()); UpdateActions(); return;
                    }
                    try
                    {
                        if (pendingIndex == null) pendingIndex = new IndustryIndex();
                        var page = IndustryCatalogPage.Read(reply, request, world, map);
                        if (!IndustryRelations.IsCurrent(reply, request, world, map, page.Revision))
                            throw new InvalidDataException("产业目录回复与当前请求或地图不一致，请重新读取。");
                        if (page.Revision != pendingIndex.Revision)
                        {
                            if (part != "industry" || offset > 0) { pendingIndex.Reset(); RequestCatalogPage(0, "industry"); return; }
                            pendingIndex.Reset();
                        }
                        ApplyDisplayNames(page);
                        int before = part == "industry" ? pendingIndex.Industries.Count : part == "town" ? pendingIndex.Towns.Count : pendingIndex.CargoNames.Count;
                        IndustryRelations.Merge(pendingIndex, page);
                        townTotal = Math.Max(townTotal, page.TownCount);
                        cargoTotal = Math.Max(cargoTotal, page.CargoCount);
                        if (part == "industry")
                        {
                            int expectedTotal = Math.Max(page.Total, pendingIndex.IndustryCount);
                            if (pendingIndex.Industries.Count == before && pendingIndex.Industries.Count < expectedTotal)
                                throw new InvalidDataException("产业目录分页没有前进，请刷新产业数据。");
                            if (pendingIndex.Industries.Count < expectedTotal) { RequestCatalogPage(pendingIndex.Industries.Count, "industry"); return; }
                            if (townTotal > 0) { RequestCatalogPage(0, "town"); return; }
                            if (cargoTotal > 0) { RequestCatalogPage(0, "cargo"); return; }
                        }
                        else if (part == "town")
                        {
                            if (pendingIndex.Towns.Count == before && pendingIndex.Towns.Count < page.Total)
                                throw new InvalidDataException("城镇目录分页没有前进，请刷新产业数据。");
                            if (pendingIndex.Towns.Count < page.Total) { RequestCatalogPage(pendingIndex.Towns.Count, "town"); return; }
                            if (cargoTotal > 0) { RequestCatalogPage(0, "cargo"); return; }
                        }
                        else
                        {
                            if (pendingIndex.CargoNames.Count == before && pendingIndex.CargoNames.Count < page.Total)
                                throw new InvalidDataException("货物目录分页没有前进，请刷新产业数据。");
                            if (pendingIndex.CargoNames.Count < page.Total) { RequestCatalogPage(pendingIndex.CargoNames.Count, "cargo"); return; }
                        }
                        CompleteLoad();
                    }
                    catch (Exception e)
                    {
                        pendingIndex = null; loading = false; pageError = e.Message; catalogComplete = index.Industries.Count > 0;
                        UpdateIndexStatus(new Dictionary<string, object>()); UpdateActions();
                    }
                });
        }

        void CompleteLoad()
        {
            index = pendingIndex; pendingIndex = null;
            loading = false; catalogComplete = true; pageError = "";
            var keepEntry = currentAnalysisId.Length == 0 ? null : index.Find(currentAnalysisId);
            if (keepEntry != null) analysis = IndustryRelations.Analyze(keepEntry, index);
            else if (analysis != null && index.Find(analysis.Entry.EntityId) == null) analysis = null;
            FillTypeChoices();
            activeGroup = null; visibleCount = Math.Max(100, resumeVisible);
            RefreshAnalysisCard(); RefreshGroups(resumeGroupDirection, resumeGroupCargo);
            RebuildResults(false);
            resultsScroll.AutoScrollPosition = new Point(0, Math.Abs(resumeScrollY));
            UpdateActions();
        }

        string currentAnalysisId = "";

        void ApplyDisplayNames(IndustryCatalogPage page)
        {
            foreach (var entry in page.Items)
            {
                string local = form.DisplayName(entry.Row);
                if (local.Length > 0) entry.Name = local;
            }
            foreach (var town in page.Towns)
            {
                string local = form.DisplayName(town.Row);
                if (local.Length > 0) town.Name = local;
            }
        }

        void RefreshIndex()
        {
            if (preview) return;
            if (!form.CaptureReady) { form.Notify("请先连接游戏并进入地图。", true); return; }
            if (loading) return;
            StartLoad(true);
        }

        void RecommendFromCamera()
        {
            if (preview) return;
            if (recommending || !form.CaptureReady) { form.Notify("请先连接游戏并进入地图。", true); return; }
            if (index.Industries.Count == 0) { form.Notify("产业目录尚未读取完成，请稍候或刷新产业数据。", true); return; }
            string scope = form.NameScope; recommending = true; UpdateActions();
            form.ReadCamera(false, delegate(bool success, string message)
            {
                if (scope != form.NameScope) return;
                recommending = false;
                if (!success) { form.Notify("镜头读取失败：" + message, true); UpdateActions(); return; }
                var camera = Data.Get(form.State, "camera");
                double cx = Data.Num(camera, "x", double.NaN), cy = Data.Num(camera, "y", double.NaN);
                if (!IndustryRelations.Finite(cx) || !IndustryRelations.Finite(cy))
                { form.Notify("未读取到有效的镜头中心坐标，请重试。", true); UpdateActions(); return; }
                var filtered = Filtered().ToList();
                if (filtered.Count == 0) { form.Notify("当前筛选范围内没有可分析的产业。", true); UpdateActions(); return; }
                IndustryEntry best = null; double bestDistance = double.PositiveInfinity;
                foreach (var entry in filtered)
                {
                    if (!entry.HasPosition) continue;
                    var distance = IndustryRelations.Distance(cx, cy, entry.X, entry.Y);
                    if (!distance.HasValue) continue;
                    if (distance.Value < bestDistance || (distance.Value == bestDistance && (best == null ||
                        string.Compare(entry.Name, best.Name, StringComparison.CurrentCultureIgnoreCase) < 0 ||
                        (string.Compare(entry.Name, best.Name, StringComparison.CurrentCultureIgnoreCase) == 0 && string.Compare(entry.EntityId, best.EntityId, StringComparison.Ordinal) < 0))))
                    { best = entry; bestDistance = distance.Value; }
                }
                if (best == null) { form.Notify("当前筛选范围内的产业都没有有效坐标，无法按镜头推荐。", true); UpdateActions(); return; }
                Navigate(best.EntityId, true);
                form.Notify("已在当前筛选范围内推荐：" + best.Name + "（直线距离 " + CargoTargets.DistanceText(bestDistance) + "）。");
                UpdateActions();
            });
        }

        IEnumerable<IndustryEntry> Filtered()
        {
            string query = search.Text.Trim();
            var cargoIds = new HashSet<string>();
            if (query.Length > 0 && selectedDirection != "*")
            {
                foreach (var pair in index.CargoNames)
                    if (pair.Value.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) cargoIds.Add(pair.Key);
                foreach (var entry in index.Industries)
                    foreach (var reference in entry.Inputs.Concat(entry.Outputs))
                    {
                        if (index.CargoName(reference.CargoId).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) cargoIds.Add(reference.CargoId);
                    }
            }
            foreach (var entry in index.Industries)
            {
                if (selectedType != "*" && entry.TypeKey != selectedType) continue;
                if (query.Length > 0)
                {
                    bool nameMatch = entry.Name.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                        entry.GameName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0 ||
                        entry.TypeName.IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0;
                    bool cargoMatch = false;
                    foreach (var reference in entry.Inputs.Concat(entry.Outputs))
                        if (index.CargoName(reference.CargoId).IndexOf(query, StringComparison.CurrentCultureIgnoreCase) >= 0) { cargoMatch = true; break; }
                    if (!nameMatch && !cargoMatch) continue;
                    if (selectedDirection != "*" && cargoIds.Count > 0)
                    {
                        if (selectedDirection == "consume" && !entry.InputIds.Any(id => cargoIds.Contains(id))) continue;
                        if (selectedDirection == "produce" && !entry.OutputIds.Any(id => cargoIds.Contains(id))) continue;
                    }
                }
                yield return entry;
            }
        }

        void FillTypeChoices()
        {
            populating = true;
            try
            {
                typeFilter.Items.Clear();
                typeFilter.Items.Add(new IndustryChoice { Id = "*", Label = "全部产业（" + index.Industries.Count + "）" });
                foreach (var group in index.Industries.GroupBy(e => e.TypeKey).OrderBy(g => g.First().TypeName, StringComparer.CurrentCultureIgnoreCase))
                {
                    var first = group.First();
                    string label = (first.TypeName == "类型未识别" ? "其他产业 / 类型未识别" : first.TypeName) + "（" + group.Count() + "）";
                    typeFilter.Items.Add(new IndustryChoice { Id = group.Key, Label = label });
                }
                int found = 0;
                for (int i = 0; i < typeFilter.Items.Count; i++) if (((IndustryChoice)typeFilter.Items[i]).Id == selectedType) { found = i; break; }
                if (found == 0 && selectedType != "*") selectedType = "*";
                typeFilter.SelectedIndex = found;
            }
            finally { populating = false; }
            RefillBuildings(false);
        }

        void RefillBuildings(bool selectAnalysis)
        {
            if (buildingFilter == null) return;
            string current = analysis == null ? "" : analysis.Entry.EntityId;
            populating = true;
            try
            {
                buildingFilter.Items.Clear();
                var filtered = Filtered().OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                buildingFilter.Items.Add(new IndustryChoice { Id = "", Label = filtered.Count == 0 ? "没有匹配的产业" : "手动选择建筑（当前匹配 " + filtered.Count + " 座）" });
                foreach (var entry in filtered) buildingFilter.Items.Add(new IndustryChoice { Id = entry.EntityId, Label = EntryLabel(entry) });
                int found = 0;
                for (int i = 1; i < buildingFilter.Items.Count; i++) if (((IndustryChoice)buildingFilter.Items[i]).Id == current) { found = i; break; }
                buildingFilter.SelectedIndex = found;
                buildingFilter.DropDownWidth = Math.Max(buildingFilter.Width, IsHandleCreated ? Math.Min(P(720), Screen.FromControl(this).WorkingArea.Width - P(32)) : P(720));
            }
            finally { populating = false; }
            if (selectAnalysis && analysis != null) { }
        }

        string EntryLabel(IndustryEntry entry)
        {
            int duplicates = index.Industries.Count(e => e.Name == entry.Name);
            string hint = duplicates > 1 && entry.HasPosition ? " · " + entry.X.ToString("0") + "," + entry.Y.ToString("0") : "";
            return entry.Name + " · " + entry.TypeName + hint;
        }

        void Navigate(string entityId, bool pushHistory)
        {
            var target = index.Find(entityId);
            if (target == null) { form.Notify("目标已不存在，请刷新产业数据。", true); return; }
            if (pushHistory && analysis != null && analysis.Entry.EntityId != entityId) history.Push(CaptureState());
            currentAnalysisId = entityId;
            analysis = IndustryRelations.Analyze(target, index);
            activeGroup = null; visibleCount = 100;
            RefreshAnalysisCard(); RefreshGroups(); RebuildResults(true); Arrange();
            populating = true;
            try
            {
                for (int i = 0; i < buildingFilter.Items.Count; i++)
                    if (((IndustryChoice)buildingFilter.Items[i]).Id == entityId) { buildingFilter.SelectedIndex = i; break; }
            }
            finally { populating = false; }
            UpdateActions();
        }

        IndustryNavState CaptureState()
        {
            return new IndustryNavState
            {
                EntityId = analysis == null ? "" : analysis.Entry.EntityId,
                Search = search.Text, TypeKey = selectedType, Direction = selectedDirection,
                GroupDirection = activeGroup == null ? "" : activeGroup.Direction,
                GroupCargo = activeGroup == null ? "" : activeGroup.CargoId,
                Visible = visibleCount, ScrollY = resultsScroll.AutoScrollPosition.Y
            };
        }

        void Back()
        {
            if (history.Count == 0) { form.Notify("还没有上一座分析建筑。"); return; }
            var state = history.Pop();
            populating = true;
            try
            {
                search.Text = state.Search; selectedType = state.TypeKey; selectedDirection = state.Direction;
                FillDirection(); FillTypeChoices();
            }
            finally { populating = false; }
            var target = index.Find(state.EntityId);
            if (target == null) { form.Notify("上一座分析建筑已不存在，请刷新产业数据。", true); analysis = null; RefreshAnalysisCard(); RefreshGroups(); RebuildResults(true); return; }
            currentAnalysisId = state.EntityId;
            analysis = IndustryRelations.Analyze(target, index);
            activeGroup = null; visibleCount = state.Visible;
            RefreshAnalysisCard(); RefreshGroups(state.GroupDirection, state.GroupCargo); RebuildResults(true);
            populating = true;
            try
            {
                for (int i = 0; i < buildingFilter.Items.Count; i++)
                    if (((IndustryChoice)buildingFilter.Items[i]).Id == state.EntityId) { buildingFilter.SelectedIndex = i; break; }
            }
            finally { populating = false; }
            Arrange();
            resultsScroll.AutoScrollPosition = new Point(0, Math.Abs(state.ScrollY));
            UpdateActions();
        }

        void RefreshAnalysisCard()
        {
            if (analysis == null)
            {
                analysisTitle.Text = "尚未选择产业";
                analysisMeta.Text = "从上方按类型选择，或使用镜头推荐。";
                analysisTags.Text = ""; analysisNote.Text = "距离基准始终为当前分析建筑；定位候选不会改变分析起点。";
                return;
            }
            var entry = analysis.Entry;
            analysisTitle.Text = entry.Name;
            string level = entry.Level > 0 ? " · 等级 " + entry.Level + (entry.MaxLevel > 0 ? " / " + entry.MaxLevel : "") : "";
            string recipe = entry.RecipeStatus == "verified" ? "配方已读取" + (entry.RolesVerified ? "" : "（配比/替代关系未读取）") : entry.RecipeStatus == "partial" ? "配方部分读取" : "生产规则未读取";
            analysisMeta.Text = entry.TypeName + level + " · " + recipe + (entry.PositionStatus == "ok" ? "" : " · 坐标未知");
            var inputs = entry.Inputs.GroupBy(x => x.CargoId).Select(g => { var first = g.First(); return index.CargoName(first.CargoId) + (first.Role != "required" ? "（" + first.RoleText + "）" : ""); }).ToList();
            var outputs = entry.Outputs.GroupBy(x => x.CargoId).Select(g => { var first = g.First(); return index.CargoName(first.CargoId) + (first.Role == "byproduct" ? "（副产品）" : ""); }).ToList();
            analysisTags.Text = "原料：" + (inputs.Count == 0 ? (entry.RecipeRead ? "无" : "未读取") : string.Join("、", inputs)) +
                "\r\n产物：" + (outputs.Count == 0 ? (entry.RecipeRead ? "无" : "未读取") : string.Join("、", outputs));
            analysisNote.Text = entry.RecipeStatus == "unavailable" ? "生产规则未读取，暂时不能生成上下游候选。" + (entry.RecipeError.Length > 0 ? " " + entry.RecipeError : "") :
                "距离基准：" + entry.Name + "（直线距离）。定位候选不会改变分析起点。";
        }

        void RefreshGroups() { RefreshGroups(null, null); }

        void RefreshGroups(string preferredDirection, string preferredCargo)
        {
            string groupDirection = activeGroup == null ? (preferredDirection ?? "") : activeGroup.Direction;
            string groupCargo = activeGroup == null ? (preferredCargo ?? "") : activeGroup.CargoId;
            populating = true;
            try
            {
                inputList.Items.Clear(); outputList.Items.Clear();
                if (analysis != null)
                {
                    foreach (var group in analysis.Groups)
                    {
                        string role = group.RoleText.Length > 0 && group.Role != "required" && group.Role != "output" ? "（" + group.RoleText + "）" : "";
                        var item = new ListViewItem(new[] { group.CargoName + role, group.Industries.Count.ToString(), group.Direction == "output" ? group.Towns.Count.ToString() : "-" }) { Tag = group };
                        if (group.Direction == "input") inputList.Items.Add(item); else outputList.Items.Add(item);
                    }
                    if (inputList.Items.Count == 0)
                        inputList.Items.Add(new ListViewItem(new[] { analysis.Entry.RecipeRead ? "该产业无需生产原料" : "生产规则未读取", "-", "-" }) { ForeColor = Theme.Muted });
                    if (outputList.Items.Count == 0)
                        outputList.Items.Add(new ListViewItem(new[] { analysis.Entry.RecipeRead ? "无产物规则" : "生产规则未读取", "-", "-" }) { ForeColor = Theme.Muted });
                }
                activeGroup = null;
                if (groupDirection.Length > 0) activeGroup = analysis == null ? null : analysis.Groups.FirstOrDefault(g => g.Direction == groupDirection && g.CargoId == groupCargo);
                if (activeGroup == null && analysis != null) activeGroup = analysis.Groups.FirstOrDefault();
                SelectGroupItem();
            }
            finally { populating = false; }
            RefreshGroupTitles();
        }

        void SelectGroupItem()
        {
            if (activeGroup == null) return;
            var list = activeGroup.Direction == "input" ? inputList : outputList;
            foreach (ListViewItem item in list.Items) if (item.Tag == activeGroup) { item.Selected = true; break; }
        }

        void RefreshGroupTitles()
        {
            inputTitle.Text = analysis == null ? "原料供应" : "原料供应（" + analysis.Inputs.Sum(g => g.Industries.Count) + " 个供应候选）";
            outputTitle.Text = analysis == null ? "产品去向" : "产品去向（" + analysis.Outputs.Sum(g => g.Industries.Count) + " 个下游 · " + analysis.Outputs.Sum(g => g.Towns.Count) + " 个城镇）";
        }

        void SelectGroup(ListView selected, ListView other)
        {
            if (populating) return;
            if (selected.SelectedItems.Count == 0) return;
            populating = true;
            try { other.SelectedItems.Clear(); } finally { populating = false; }
            activeGroup = selected.SelectedItems[0].Tag as IndustryGroup; visibleCount = Math.Max(100, visibleCount);
            RebuildResults(true); Arrange(); UpdateActions();
        }

        void RebuildResults(bool resetScroll)
        {
            int oldScroll = resultsScroll.AutoScrollPosition.Y;
            resultsScroll.SuspendLayout();
            try
            {
                rowControls.Clear();
                foreach (Control control in resultsContent.Controls.Cast<Control>().ToList()) { resultsContent.Controls.Remove(control); control.Dispose(); }
                resultsEmpty.Visible = false; loadMore.Visible = false; resultsNote.Text = "";
                if (analysis == null) { resultsTitle.Text = "选择货物分组"; resultsEmpty.Text = "选择产业后，这里会列出全部原料供应与产品去向。"; resultsEmpty.Visible = true; resultsContent.SetBounds(0, 0, 1, 1); return; }
                if (activeGroup == null)
                {
                    resultsTitle.Text = analysis.Entry.RecipeRead ? "暂无上下游分组" : "生产规则未读取";
                    resultsEmpty.Text = analysis.Entry.RecipeRead ? "该产业没有已识别的原料或产物。" : "无法读取该产业的生产规则，请刷新后重试。";
                    resultsEmpty.Visible = true; resultsContent.SetBounds(0, 0, 1, 1); return;
                }
                resultsTitle.Text = (activeGroup.Direction == "input" ? "原料供应 → " : "产品去向 → ") + activeGroup.CargoName;
                resultsNote.Text = activeGroup.Direction == "input" ? "全部供应产业按直线距离由近到远排列；距离未知者排在末尾。" : activeGroup.TownRuleText;
                int width = Math.Max(1, resultsScroll.ClientSize.Width - P(19)), y = 0;
                if (activeGroup.Direction == "input")
                    AppendSection("供应产业（" + activeGroup.Industries.Count + "）", width, ref y, activeGroup.Industries, "当前地图未找到生产该原料的产业。", true);
                else
                    AppendSection("下游产业（" + activeGroup.Industries.Count + "）", width, ref y, activeGroup.Industries, "当前地图未找到消耗该产品的产业。", true);
                if (activeGroup.Direction == "output")
                    AppendSection("供给城镇（" + activeGroup.Towns.Count + "）", width, ref y, activeGroup.Towns, activeGroup.TownRuleState == "unread" ? "城镇需求读取未完成，无法确认该产品是否有供给用途。" : "该产品未识别到城镇供给用途。", false);
                resultsContent.SetBounds(0, 0, width, Math.Max(1, y));
                resultsScroll.AutoScrollMinSize = new Size(0, Math.Max(1, y));
                int shown = Math.Min(activeGroup.Industries.Count, visibleCount) + (activeGroup.Direction == "output" ? Math.Min(activeGroup.Towns.Count, visibleCount) : 0);
                int total = activeGroup.Total;
                loadMore.Visible = shown < total;
                loadMore.Text = "加载更多（已显示 " + shown + " / 共 " + total + "）";
            }
            finally
            {
                resultsScroll.ResumeLayout(true);
                if (resetScroll) resultsScroll.AutoScrollPosition = Point.Empty; else resultsScroll.AutoScrollPosition = new Point(0, Math.Abs(oldScroll));
            }
            UpdateRowStates();
        }

        void AppendSection(string title, int width, ref int y, List<IndustryCandidate> candidates, string emptyText, bool industries)
        {
            var header = MainForm.LabelFor(title, 9.5f, true);
            header.Name = "industry-result-section";
            header.SetBounds(P(4), y, width - P(8), P(24));
            resultsContent.Controls.Add(header); y += P(28);
            var page = candidates.Take(Math.Max(0, Math.Min(candidates.Count, visibleCount))).ToList();
            if (page.Count == 0)
            {
                var empty = MainForm.LabelFor(emptyText, 9); empty.ForeColor = Theme.Muted; empty.Name = "industry-result-empty";
                empty.SetBounds(P(8), y, width - P(16), P(28)); resultsContent.Controls.Add(empty); y += P(36); return;
            }
            foreach (var candidate in page)
            {
                var row = new IndustryResultRow(candidate, LocateCandidate, AnalyzeCandidate);
                row.SetBounds(P(4), y, Math.Max(1, width - P(8)), P(60));
                resultsContent.Controls.Add(row); row.Arrange(row.Width); rowControls.Add(row); y += P(66);
            }
        }

        void LocateCandidate(IndustryCandidate candidate)
        {
            if (!form.CaptureReady) { form.Notify("请先连接游戏。", true); return; }
            form.Request(new { op = "focus", id = candidate.TargetId });
        }

        void AnalyzeCandidate(IndustryCandidate candidate)
        {
            if (index.Find(candidate.TargetId) == null) { form.Notify("目标已不存在，请刷新产业数据。", true); return; }
            Navigate(candidate.TargetId, true);
        }

        void UpdateRowStates()
        {
            bool online = form.CaptureReady, focus = online && form.IsAvailable("focus");
            foreach (var row in rowControls) row.UpdateState(online, focus);
        }

        public void UpdateActions()
        {
            bool online = form.CaptureReady && !form.Busy;
            MainForm.Enable(search, true); MainForm.Enable(typeFilter, index.Industries.Count > 0);
            MainForm.Enable(buildingFilter, index.Industries.Count > 0); MainForm.Enable(directionFilter, true);
            MainForm.Enable(clearFilters, search.Text.Length > 0 || selectedType != "*" || selectedDirection != "*");
            MainForm.Enable(cameraRecommend, online && index.Industries.Count > 0 && !recommending);
            MainForm.Enable(refreshIndex, online && !loading);
            cameraRecommend.Text = recommending ? "读取镜头中…" : "镜头推荐";
            refreshIndex.Text = loading ? "正在读取…" : "刷新产业数据";
            bool hasAnalysis = analysis != null;
            MainForm.Enable(locateAnalysis, online && hasAnalysis && form.IsAvailable("focus"));
            MainForm.Enable(backAnalysis, online && history.Count > 0);
            if (hasAnalysis && form.CaptureReady && !form.IsAvailable("focus"))
                analysisNote.Text = "定位能力不可用：" + form.UnavailableReason("focus");
            if (!loading && index.Revision > 0) matchInfo.Text = "当前匹配 " + Filtered().Count() + " 座 / 共 " + Math.Max(index.IndustryCount, index.Industries.Count) + " 座";
            UpdateRowStates();
        }

        public void Arrange()
        {
            if (arranging || selectorCard == null || analysisTitle == null) return;
            arranging = true;
            var old = AutoScrollPosition;
            try
            {
                AutoScrollPosition = Point.Empty;
                int width = Math.Max(1, ClientSize.Width - P(19)), pad = P(12), gap = P(10);
                int inner = Math.Max(1, width - pad * 2);
                bool wide = width >= P(760);
                selectorCard.SetBounds(0, 0, width, P(1));
                int y = pad;
                int clearWidth = Ui.ButtonWidth(clearFilters, 76), directionWidth = Math.Min(P(128), inner / 4);
                int searchHeight = Ui.ButtonHeight(clearFilters, 30);
                search.SetBounds(pad, y, Math.Max(1, inner - clearWidth - directionWidth - gap * 2), searchHeight);
                clearFilters.SetBounds(search.Right + gap, y, clearWidth, searchHeight);
                directionFilter.SetBounds(Math.Max(clearFilters.Right + gap, width - pad - directionWidth), y, directionWidth, searchHeight);
                y += searchHeight + P(8);
                int cameraWidth = Ui.ButtonWidth(cameraRecommend, 82), refreshWidth = Ui.ButtonWidth(refreshIndex, 96);
                int typeWidth = Math.Min(P(230), Math.Max(P(120), inner * 30 / 100));
                if (wide)
                {
                    int buildingWidth = Math.Max(P(160), inner - typeWidth - cameraWidth - refreshWidth - gap * 3);
                    typeFilter.SetBounds(pad, y, typeWidth, P(32));
                    buildingFilter.SetBounds(typeFilter.Right + gap, y, buildingWidth, P(32));
                    cameraRecommend.SetBounds(buildingFilter.Right + gap, y, cameraWidth, P(32));
                    refreshIndex.SetBounds(cameraRecommend.Right + gap, y, Math.Max(P(90), width - pad - cameraRecommend.Right - gap), P(32));
                    y += P(38);
                }
                else
                {
                    int buildingWidth = Math.Max(P(120), inner - typeWidth - gap);
                    typeFilter.SetBounds(pad, y, typeWidth, P(32));
                    buildingFilter.SetBounds(typeFilter.Right + gap, y, buildingWidth, P(32));
                    y += P(38);
                    cameraRecommend.SetBounds(pad, y, Math.Min(P(120), inner / 2), P(32));
                    refreshIndex.SetBounds(cameraRecommend.Right + gap, y, Math.Min(P(150), inner - cameraRecommend.Width - gap), P(32));
                    y += P(38);
                }
                matchInfo.SetBounds(pad, y, inner, P(22)); y += P(22);
                indexStatus.SetBounds(pad, matchInfo.Bottom + P(2), inner, Ui.WrappedHeight(indexStatus, inner, 22)); y = indexStatus.Bottom + pad;
                selectorCard.Height = y; selectorCard.SetBounds(0, 0, width, y);

                int cardTop = selectorCard.Bottom + gap;
                int titleWidth = wide ? Math.Min(P(420), inner - P(300)) : inner;
                analysisTitle.SetBounds(pad, P(12), Math.Max(P(120), titleWidth), P(26));
                if (wide)
                {
                    int backWidth = Ui.ButtonWidth(backAnalysis, 100), locateWidth = Ui.ButtonWidth(locateAnalysis, 100);
                    backAnalysis.SetBounds(width - pad - backWidth, P(10), backWidth, P(30));
                    locateAnalysis.SetBounds(backAnalysis.Left - gap - locateWidth, P(10), locateWidth, P(30));
                }
                else
                {
                    int half = (inner - gap) / 2;
                    locateAnalysis.SetBounds(pad, P(42), half, P(30));
                    backAnalysis.SetBounds(pad + half + gap, P(42), inner - half - gap, P(30));
                }
                int metaTop = wide ? P(42) : P(80);
                analysisMeta.SetBounds(pad, metaTop, inner, P(22));
                analysisTags.SetBounds(pad, analysisMeta.Bottom + P(2), inner, P(40));
                analysisNote.SetBounds(pad, analysisTags.Bottom + P(2), inner, Ui.WrappedHeight(analysisNote, inner, 22));
                analysisCard.SetBounds(0, cardTop, width, analysisNote.Bottom + pad);
                int groupsTop = analysisCard.Bottom + gap;
                if (wide)
                {
                    int half = (inner - gap) / 2;
                    LayoutGroupCard(inputCard, 0, groupsTop, half, P(132));
                    LayoutGroupCard(outputCard, half + gap, groupsTop, inner - half - gap, P(132));
                }
                else
                {
                    LayoutGroupCard(inputCard, 0, groupsTop, inner, P(112));
                    LayoutGroupCard(outputCard, 0, inputCard.Bottom + gap, inner, P(112));
                }
                int resultsTop = (wide ? inputCard.Bottom : outputCard.Bottom) + gap;
                resultsCard.SetBounds(0, resultsTop, width, P(330));
                LayoutResults();
                int height = resultsCard.Bottom + pad;
                canvas.SetBounds(0, 0, width, height);
                AutoScrollMinSize = new Size(0, height);
                AutoScrollPosition = new Point(-old.X, -old.Y);
            }
            finally { arranging = false; }
        }

        void LayoutGroupCard(Surface card, int x, int top, int width, int height)
        {
            card.SetBounds(x, top, width, height); card.Padding = new Padding(P(12));
            var title = card == inputCard ? inputTitle : outputTitle;
            var list = card == inputCard ? inputList : outputList;
            int pad = P(12), inner = Math.Max(1, width - pad * 2);
            title.SetBounds(pad, P(10), inner, P(24));
            list.SetBounds(pad, title.Bottom + P(4), inner, Math.Max(P(48), height - title.Bottom - P(18)));
            list.Columns[1].Width = P(48); list.Columns[2].Width = P(48); list.Columns[0].Width = Math.Max(P(80), list.ClientSize.Width - P(100));
        }

        void LayoutResults()
        {
            int pad = P(12), inner = Math.Max(1, resultsCard.Width - pad * 2);
            resultsTitle.SetBounds(pad, P(8), inner, P(24));
            resultsNote.SetBounds(pad, resultsTitle.Bottom + P(2), inner, Ui.WrappedHeight(resultsNote, inner, 20));
            int loadHeight = P(30), scrollTop = resultsNote.Bottom + P(6);
            int scrollHeight = Math.Max(P(120), resultsCard.Height - scrollTop - loadHeight - P(18));
            resultsScroll.SetBounds(pad, scrollTop, inner, scrollHeight);
            loadMore.SetBounds(pad, resultsScroll.Bottom + P(4), Ui.ButtonWidth(loadMore, 130), loadHeight);
            resultsEmpty.SetBounds(pad, scrollTop, inner, scrollHeight);
            int contentWidth = Math.Max(1, resultsScroll.ClientSize.Width - P(19));
            resultsContent.Width = contentWidth;
            int y = 0;
            foreach (Control control in resultsContent.Controls)
            {
                if (control is IndustryResultRow) { control.Width = Math.Max(1, contentWidth - P(8)); ((IndustryResultRow)control).Arrange(control.Width); }
                else if (control is Label) control.Width = Math.Max(1, contentWidth - P(8));
            }
            // Reflow after width changes.
            foreach (Control control in resultsContent.Controls)
            {
                control.Top = y; y += control.Height + (control is IndustryResultRow ? P(6) : P(4));
            }
            resultsContent.Height = Math.Max(1, y);
            resultsScroll.AutoScrollMinSize = new Size(0, Math.Max(1, y));
        }

        public void LoadPreview(IndustryIndex previewIndex)
        {
            preview = true; pendingIndex = null; index.Reset();
            index.Revision = previewIndex.Revision; index.Complete = previewIndex.Complete; index.IndustryCount = previewIndex.IndustryCount;
            index.TownCount = previewIndex.TownCount; index.UnreadIndustryCount = previewIndex.UnreadIndustryCount; index.UnreadTownCount = previewIndex.UnreadTownCount;
            index.GeneratedAt = previewIndex.GeneratedAt;
            foreach (var entry in previewIndex.Industries) index.Industries.Add(entry);
            foreach (var town in previewIndex.Towns) index.Towns.Add(town);
            foreach (var pair in previewIndex.CargoNames) index.CargoNames[pair.Key] = pair.Value;
            catalogComplete = true; loading = false; pageError = "";
            FillTypeChoices(); RefreshAnalysisCard(); RefreshGroups(); RebuildResults(true);
            Navigate("20", false);
            Arrange();
        }

        internal void ValidateIndustryFlow()
        {
            if (index.Industries.Count == 0) throw new InvalidOperationException("产业离线预览未加载。");
            if (analysis == null || analysis.Entry.EntityId != "20") throw new InvalidOperationException("产业分析起点未载入。");
            if (analysis.Inputs.First().Industries.Count != 23) throw new InvalidOperationException("原料供应没有完整显示 23 个实体。");
            if (analysis.Outputs.Count() != 2) throw new InvalidOperationException("多产物分组未完整显示。");
            var meat = analysis.Outputs.First(g => g.CargoId == "2");
            activeGroup = meat; RebuildResults(true); Arrange(); Application.DoEvents();
            if (rowControls.Count != meat.Industries.Count + meat.Towns.Count) throw new InvalidOperationException("结果行数量与候选总数不一致。");
            if (!rowControls.Any(r => r.Candidate.TargetKind == "城镇" && r.Candidate.RelationKey == "active"))
                throw new InvalidOperationException("城镇供给结果未显示。");
            visibleCount = 2; RebuildResults(true);
            if (rowControls.Count >= meat.Industries.Count + meat.Towns.Count) throw new InvalidOperationException("分页没有限制显示数量。");
            visibleCount = 1000; RebuildResults(true);
            if (rowControls.Count != meat.Industries.Count + meat.Towns.Count) throw new InvalidOperationException("加载更多无法取得全部结果。");
            Navigate("30", true);
            if (analysis.Entry.EntityId != "30" || history.Count == 0) throw new InvalidOperationException("查看产业链未建立历史。");
            Back();
            if (analysis.Entry.EntityId != "20" || history.Count != 0) throw new InvalidOperationException("返回上一分析失败。");
            Navigate("20", true);
            if (history.Count != 0) throw new InvalidOperationException("相邻重复分析被重复入栈。");
            search.Text = "肉"; selectedDirection = "*"; selectedType = "*";
            activeGroup = analysis.Outputs.First(g => g.CargoId == "2"); visibleCount = 1000;
            RebuildResults(true); Arrange(); Application.DoEvents();
            int scrollMax = Math.Max(0, resultsScroll.DisplayRectangle.Height - resultsScroll.ClientSize.Height);
            if (scrollMax < 40) throw new InvalidOperationException("历史滚动测试缺少可滚动结果。");
            resultsScroll.AutoScrollPosition = new Point(0, 80); Application.DoEvents();
            int savedScroll = resultsScroll.AutoScrollPosition.Y;
            if (savedScroll >= 0) throw new InvalidOperationException("结果列表没有记录滚动偏移。");
            Navigate("30", true); Back();
            if (search.Text != "肉" || activeGroup == null || activeGroup.CargoId != "2" || visibleCount < 1000)
                throw new InvalidOperationException("返回上一分析没有恢复筛选、分组或已加载数量。");
            if (resultsScroll.AutoScrollPosition.Y != savedScroll)
                throw new InvalidOperationException("返回上一分析没有恢复滚动位置。");
            visibleCount = 2; RebuildResults(true); Arrange(); Application.DoEvents();
            resultsScroll.AutoScrollPosition = new Point(0, 40); Application.DoEvents();
            int loadScroll = resultsScroll.AutoScrollPosition.Y;
            loadMore.PerformClick(); Application.DoEvents();
            if (loadMore.Visible || rowControls.Count != activeGroup.Total || resultsScroll.AutoScrollPosition.Y != loadScroll)
                throw new InvalidOperationException("加载更多没有保留滚动位置或完整结果。");
            resumeGroupDirection = "output"; resumeGroupCargo = "2"; resumeVisible = 1000; resumeScrollY = savedScroll;
            pendingIndex = IndustryRelations.Synthetic(); CompleteLoad(); Arrange(); Application.DoEvents();
            if (activeGroup == null || activeGroup.CargoId != "2" || visibleCount < 1000)
                throw new InvalidOperationException("刷新完成没有恢复分组或已加载数量。");
            if (resultsScroll.AutoScrollPosition.Y != savedScroll)
                throw new InvalidOperationException("刷新完成没有恢复滚动位置。");
        }
    }
}
