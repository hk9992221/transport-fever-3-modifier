using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TransportFever3Trainer
{
    sealed class IndustryCargoRef
    {
        public string CargoId = "", Role = "required", Group = "";
        public string RoleText
        {
            get
            {
                if (Role == "alternate") return "替代原料";
                if (Role == "boost") return "可选 / 增产材料";
                if (Role == "byproduct") return "副产品";
                if (Role == "output") return "产物";
                return "必需原料";
            }
        }
    }

    sealed class IndustryEntry
    {
        public Dictionary<string, object> Row;
        public string EntityId = "", ConstructionId = "", StockListId = "", TypeKey = "unknown", TypeName = "类型未识别";
        public string GameName = "", Name = "产业", PositionStatus = "unknown", PositionError = "", PositionSource = "";
        public string RecipeStatus = "unavailable", RecipeError = "";
        public bool RolesVerified;
        public int Level, MaxLevel;
        public double X = double.NaN, Y = double.NaN;
        public List<IndustryCargoRef> Inputs = new List<IndustryCargoRef>(), Outputs = new List<IndustryCargoRef>();
        public List<string> InputIds = new List<string>(), OutputIds = new List<string>();
        public bool HasPosition { get { return IndustryRelations.Finite(X) && IndustryRelations.Finite(Y); } }
        public bool RecipeRead { get { return RecipeStatus != "unavailable"; } }
    }

    sealed class TownNeed
    {
        public string CargoId = "", Status = "unknown";
        public List<int> LandUses = new List<int>();
    }

    sealed class TownDistrict
    {
        public int LandUse;
        public double X = double.NaN, Y = double.NaN;
    }

    sealed class TownEntry
    {
        public Dictionary<string, object> Row;
        public string EntityId = "", GameName = "", Name = "城镇", NeedsStatus = "unread", DistrictStatus = "partial", DistrictError = "";
        public List<TownNeed> Needs = new List<TownNeed>();
        public List<TownDistrict> Districts = new List<TownDistrict>();
        public TownNeed Need(string cargoId) { return Needs.FirstOrDefault(n => n.CargoId == cargoId); }
    }

    sealed class IndustryIndex
    {
        public int Revision;
        public bool Complete;
        public int IndustryCount, TownCount, UnreadIndustryCount, UnreadTownCount;
        public double GeneratedAt;
        public readonly List<IndustryEntry> Industries = new List<IndustryEntry>();
        public readonly List<TownEntry> Towns = new List<TownEntry>();
        public readonly Dictionary<string, string> CargoNames = new Dictionary<string, string>();
        public IndustryEntry Find(string entityId) { return Industries.FirstOrDefault(e => e.EntityId == entityId); }
        public string CargoName(string cargoId)
        {
            string name; return CargoNames.TryGetValue(cargoId, out name) && name.Length > 0 ? name : "货物 " + cargoId;
        }
        public void Reset()
        {
            Industries.Clear(); Towns.Clear(); CargoNames.Clear();
            Revision = 0; Complete = false; IndustryCount = TownCount = UnreadIndustryCount = UnreadTownCount = 0; GeneratedAt = 0;
        }
    }

    sealed class IndustryCandidate
    {
        public string TargetId = "", TargetKind = "产业", Name = "", TypeName = "", Relation = "", RelationKey = "", TownStatus = "", CargoId = "", PositionStatus = "unknown";
        public double? Distance;
        public IndustryEntry Entry;
        public TownEntry Town;
        public int LandUse;
        public string DistanceText
        {
            get
            {
                string partial = TargetKind == "城镇" && PositionStatus == "partial" ? "（部分需求区坐标未读取）" : "";
                if (!Distance.HasValue) return TargetKind == "城镇" ? "至需求区中心（直线）距离未知" + partial : "距离未知";
                string value = CargoTargets.DistanceText(Distance.Value);
                return TargetKind == "城镇" ? "至需求区中心（直线）" + value + partial : "直线距离 " + value;
            }
        }
    }

    sealed class IndustryGroup
    {
        public string CargoId = "", CargoName = "", Direction = "output", TownRuleState = "none", TownRuleText = "", Role = "", RoleText = "";
        public readonly List<IndustryCandidate> Industries = new List<IndustryCandidate>();
        public readonly List<IndustryCandidate> Towns = new List<IndustryCandidate>();
        public int Total { get { return Industries.Count + Towns.Count; } }
        public string Caption
        {
            get
            {
                string prefix = Direction == "input" ? "[原料] " : "[产物] ";
                string role = RoleText.Length > 0 && Role != "required" && Role != "output" ? "（" + RoleText + "）" : "";
                if (Direction == "input") return prefix + CargoName + role + " · 供应产业 " + Industries.Count;
                return prefix + CargoName + role + " · 下游产业 " + Industries.Count + " · 供给城镇 " + Towns.Count;
            }
        }
    }

    sealed class IndustryAnalysis
    {
        public IndustryEntry Entry;
        public IndustryIndex Index;
        public readonly List<IndustryGroup> Groups = new List<IndustryGroup>();
        public IEnumerable<IndustryGroup> Inputs { get { return Groups.Where(g => g.Direction == "input"); } }
        public IEnumerable<IndustryGroup> Outputs { get { return Groups.Where(g => g.Direction == "output"); } }
    }

    sealed class IndustryCatalogPage
    {
        public int Revision;
        public bool Complete;
        public int IndustryCount, TownCount, UnreadIndustryCount, UnreadTownCount, Offset, Total;
        public double GeneratedAt;
        public string Message = "";
        public readonly List<IndustryEntry> Items = new List<IndustryEntry>();
        public readonly List<TownEntry> Towns = new List<TownEntry>();
        public readonly Dictionary<string, string> CargoNames = new Dictionary<string, string>();

        public static IndustryCatalogPage Read(object reply, string request, string world, string scope)
        {
            var root = Data.Obj(reply);
            if (!Data.Bool(root, "ok") || Data.Str(root, "request") != request || Data.Str(root, "world") != world || Data.Str(root, "nameScope") != scope)
                throw new InvalidDataException("产业索引回复与当前请求或地图不一致，请重新读取。");
            var catalog = Data.Obj(Data.Get(root, "catalog"));
            if (catalog.Count == 0) throw new InvalidDataException("游戏未返回产业索引。");
            var page = new IndustryCatalogPage
            {
                Revision = (int)Data.Num(catalog, "revision", Data.Num(root, "revision", 0)),
                Complete = Data.Bool(catalog, "complete"),
                IndustryCount = (int)Data.Num(catalog, "industryCount"),
                TownCount = (int)Data.Num(catalog, "townCount"),
                UnreadIndustryCount = (int)Data.Num(catalog, "unreadIndustryCount"),
                UnreadTownCount = (int)Data.Num(catalog, "unreadTownCount"),
                Offset = (int)Data.Num(catalog, "offset"),
                Total = (int)Data.Num(catalog, "total", Data.Num(catalog, "industryCount")),
                GeneratedAt = Data.Num(catalog, "generatedAt"),
                Message = Data.Str(catalog, "message", Data.Str(root, "message"))
            };
            if (page.Revision <= 0) throw new InvalidDataException("产业索引缺少有效的版本号。");
            foreach (var row in Data.Rows(Data.Get(catalog, "items"))) page.Items.Add(ReadEntry(row));
            foreach (var row in Data.Rows(Data.Get(catalog, "towns"))) page.Towns.Add(ReadTown(row));
            foreach (var row in Data.Rows(Data.Get(catalog, "cargo")))
            {
                string id = Data.Str(row, "id"), name = Data.Str(row, "name");
                if (id.Length > 0 && name.Length > 0) page.CargoNames[id] = name;
            }
            return page;
        }

        static IndustryEntry ReadEntry(Dictionary<string, object> row)
        {
            var entry = new IndustryEntry
            {
                Row = row,
                EntityId = Data.Str(row, "entityId"),
                ConstructionId = Data.Str(row, "constructionId"),
                StockListId = Data.Str(row, "stockListId"),
                TypeKey = Data.Str(row, "typeKey", "unknown"),
                TypeName = Data.Str(row, "typeName", "类型未识别"),
                GameName = Data.Str(row, "gameName"),
                Name = Data.Str(row, "name", "产业"),
                PositionStatus = Data.Str(row, "positionStatus", "unknown"),
                PositionError = Data.Str(row, "positionError"),
                PositionSource = Data.Str(row, "positionSource"),
                RecipeStatus = Data.Str(row, "recipeStatus", "unavailable"),
                RecipeError = Data.Str(row, "recipeError"),
                RolesVerified = Data.Bool(row, "rolesVerified"),
                Level = (int)Data.Num(row, "level"),
                MaxLevel = (int)Data.Num(row, "maxLevel")
            };
            if (entry.EntityId.Length == 0) throw new InvalidDataException("产业索引包含缺少实体编号的记录。");
            var position = Data.Get(row, "position");
            entry.X = Data.Num(position, "x", double.NaN); entry.Y = Data.Num(position, "y", double.NaN);
            if (!entry.HasPosition) { entry.X = entry.Y = double.NaN; entry.PositionStatus = "unknown"; }
            foreach (var input in Data.Rows(Data.Get(row, "inputs"))) AddCargo(entry.Inputs, input, "required");
            foreach (var output in Data.Rows(Data.Get(row, "outputs"))) AddCargo(entry.Outputs, output, "output");
            entry.InputIds = Dedup(entry.Inputs.Select(x => x.CargoId));
            entry.OutputIds = Dedup(entry.Outputs.Select(x => x.CargoId));
            return entry;
        }

        static void AddCargo(List<IndustryCargoRef> list, Dictionary<string, object> row, string fallback)
        {
            string id = Data.Str(row, "cargoId");
            if (id.Length == 0 || id == "0") return;
            var reference = new IndustryCargoRef { CargoId = id, Role = Data.Str(row, "role", fallback), Group = Data.Str(row, "group") };
            var existing = list.FirstOrDefault(x => x.CargoId == id);
            if (existing == null) list.Add(reference);
            else if (reference.Role == "required") existing.Role = reference.Role;
        }

        static TownEntry ReadTown(Dictionary<string, object> row)
        {
            var town = new TownEntry
            {
                Row = row,
                EntityId = Data.Str(row, "entityId"),
                GameName = Data.Str(row, "gameName"),
                Name = Data.Str(row, "name", "城镇"),
                NeedsStatus = Data.Str(row, "needsStatus", "unread"),
                DistrictStatus = Data.Str(row, "districtStatus", "partial"),
                DistrictError = Data.Str(row, "districtError")
            };
            if (town.EntityId.Length == 0) throw new InvalidDataException("城镇索引包含缺少实体编号的记录。");
            foreach (var needRow in Data.Rows(Data.Get(row, "needs")))
            {
                string id = Data.Str(needRow, "cargoId");
                if (id.Length == 0 || id == "0") continue;
                var need = new TownNeed { CargoId = id, Status = Data.Str(needRow, "status", "unknown") };
                var uses = Data.Get(needRow, "landUses") as object[];
                if (uses != null) foreach (var value in uses)
                {
                    double n = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    if (IndustryRelations.Finite(n)) need.LandUses.Add((int)n);
                }
                town.Needs.Add(need);
            }
            foreach (var districtRow in Data.Rows(Data.Get(row, "districts")))
            {
                var district = new TownDistrict
                {
                    LandUse = (int)Data.Num(districtRow, "landUse"),
                    X = Data.Num(districtRow, "x", double.NaN),
                    Y = Data.Num(districtRow, "y", double.NaN)
                };
                if (district.LandUse > 0 && IndustryRelations.Finite(district.X) && IndustryRelations.Finite(district.Y)) town.Districts.Add(district);
            }
            return town;
        }

        static List<string> Dedup(IEnumerable<string> ids)
        {
            var seen = new HashSet<string>(); var result = new List<string>();
            foreach (var id in ids) if (id.Length > 0 && seen.Add(id)) result.Add(id);
            return result;
        }
    }

    static class IndustryRelations
    {
        public static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }

        public static void Merge(IndustryIndex index, IndustryCatalogPage page)
        {
            if (page.Offset != index.Industries.Count)
                throw new InvalidDataException("产业目录分页不连续，请重新读取。");
            if (page.Offset == 0 && page.Revision != index.Revision)
            {
                index.Reset(); index.Revision = page.Revision;
            }
            foreach (var entry in page.Items) if (index.Industries.All(e => e.EntityId != entry.EntityId)) index.Industries.Add(entry);
            foreach (var town in page.Towns) if (index.Towns.All(t => t.EntityId != town.EntityId)) index.Towns.Add(town);
            foreach (var pair in page.CargoNames) index.CargoNames[pair.Key] = pair.Value;
            index.Revision = page.Revision; index.Complete = page.Complete;
            index.IndustryCount = page.IndustryCount; index.TownCount = page.TownCount;
            index.UnreadIndustryCount = page.UnreadIndustryCount; index.UnreadTownCount = page.UnreadTownCount;
            index.GeneratedAt = page.GeneratedAt;
        }

        public static bool IsCurrent(object reply, string request, string world, string scope, int expectedRevision)
        {
            var root = Data.Obj(reply);
            return Data.Bool(root, "ok") && Data.Str(root, "request") == request && Data.Str(root, "world") == world &&
                Data.Str(root, "nameScope") == scope && (int)Data.Num(root, "revision", Data.Num(Data.Get(root, "catalog"), "revision")) == expectedRevision;
        }

        public static double? Distance(double ax, double ay, double bx, double by)
        {
            if (!Finite(ax) || !Finite(ay) || !Finite(bx) || !Finite(by)) return null;
            double dx = bx - ax, dy = by - ay, distance = Math.Sqrt(dx * dx + dy * dy);
            return Finite(distance) ? (double?)distance : null;
        }

        public static double? TownDistance(TownEntry town, string cargoId, double ax, double ay)
        {
            var need = town.Need(cargoId); if (need == null) return null;
            double? best = null;
            foreach (var district in town.Districts)
            {
                if (need.LandUses.Count > 0 && !need.LandUses.Contains(district.LandUse)) continue;
                var distance = Distance(ax, ay, district.X, district.Y);
                if (distance.HasValue && (!best.HasValue || distance.Value < best.Value)) best = distance;
            }
            return best;
        }

        static string RoleText(IEnumerable<IndustryCargoRef> refs, string cargoId)
        {
            var reference = refs.FirstOrDefault(x => x.CargoId == cargoId);
            return reference == null ? "必需原料" : reference.RoleText;
        }

        public static IndustryAnalysis Analyze(IndustryEntry analysis, IndustryIndex index)
        {
            var result = new IndustryAnalysis { Entry = analysis, Index = index };
            var inputIds = new List<string>(); foreach (var reference in analysis.Inputs) if (!inputIds.Contains(reference.CargoId)) inputIds.Add(reference.CargoId);
            foreach (var cargoId in inputIds)
            {
                var inputRef = analysis.Inputs.First(x => x.CargoId == cargoId);
                var group = new IndustryGroup { CargoId = cargoId, CargoName = index.CargoName(cargoId), Direction = "input", TownRuleState = "none", Role = inputRef.Role, RoleText = inputRef.RoleText };
                foreach (var supplier in index.Industries)
                {
                    if (supplier.EntityId == analysis.EntityId || !supplier.OutputIds.Contains(cargoId)) continue;
                    var supplierRole = supplier.Outputs.FirstOrDefault(x => x.CargoId == cargoId);
                    var candidate = new IndustryCandidate
                    {
                        TargetId = supplier.EntityId, TargetKind = "产业", Name = Display(supplier), TypeName = supplier.TypeName,
                        Relation = supplierRole != null && supplierRole.Role == "byproduct" ? "作为副产品生产该原料" : "生产该原料",
                        RelationKey = "supply", CargoId = cargoId, Entry = supplier,
                        Distance = Distance(analysis.X, analysis.Y, supplier.X, supplier.Y),
                        PositionStatus = supplier.PositionStatus
                    };
                    group.Industries.Add(candidate);
                }
                group.Industries.Sort(CompareCandidates);
                result.Groups.Add(group);
            }
            var outputIds = new List<string>(); foreach (var reference in analysis.Outputs) if (!outputIds.Contains(reference.CargoId)) outputIds.Add(reference.CargoId);
            foreach (var cargoId in outputIds)
            {
                var outputRef = analysis.Outputs.First(x => x.CargoId == cargoId);
                var group = new IndustryGroup { CargoId = cargoId, CargoName = index.CargoName(cargoId), Direction = "output", Role = outputRef.Role, RoleText = outputRef.RoleText };
                foreach (var consumer in index.Industries)
                {
                    if (consumer.EntityId == analysis.EntityId || !consumer.InputIds.Contains(cargoId)) continue;
                    var candidate = new IndustryCandidate
                    {
                        TargetId = consumer.EntityId, TargetKind = "产业", Name = Display(consumer), TypeName = consumer.TypeName,
                        Relation = "消耗该产品 · " + RoleText(consumer.Inputs, cargoId), RelationKey = "consume", CargoId = cargoId,
                        Entry = consumer, Distance = Distance(analysis.X, analysis.Y, consumer.X, consumer.Y),
                        PositionStatus = consumer.PositionStatus
                    };
                    group.Industries.Add(candidate);
                }
                foreach (var town in index.Towns)
                {
                    var need = town.Need(cargoId); if (need == null) continue;
                    string landUses = need.LandUses.Count == 0 ? "" : " · 需求区 " + string.Join("、", need.LandUses.Select(LandUseName).ToArray());
                    var candidate = new IndustryCandidate
                    {
                        TargetId = town.EntityId, TargetKind = "城镇", Name = Display(town), TypeName = "城镇",
                        Relation = (need.Status == "active" ? "当前需求" : need.Status == "zero" ? "可供给但当前未启用" : "需求状态未读取") + landUses,
                        RelationKey = need.Status, CargoId = cargoId, Town = town,
                        Distance = TownDistance(town, cargoId, analysis.X, analysis.Y),
                        PositionStatus = town.DistrictStatus == "complete" ? "ok" : "partial"
                    };
                    candidate.LandUse = need.LandUses.Count > 0 ? need.LandUses[0] : 0;
                    group.Towns.Add(candidate);
                }
                group.Industries.Sort(CompareCandidates);
                group.Towns.Sort(CompareCandidates);
                if (group.Towns.Count > 0) { group.TownRuleState = "ready"; group.TownRuleText = "当前地图已确认相关城镇需求。"; }
                else if (index.UnreadTownCount > 0 || (index.TownCount == 0 && !index.Complete) || index.Towns.Any(t => t.NeedsStatus == "unread" || t.NeedsStatus == "partial"))
                {
                    group.TownRuleState = "unread";
                    int pending = index.UnreadTownCount > 0 ? index.UnreadTownCount : index.Towns.Count(t => t.NeedsStatus == "unread" || t.NeedsStatus == "partial");
                    group.TownRuleText = "城镇需求读取未完成" + (pending > 0 ? "（" + pending + " 座待核验）" : "") + "，无法确认该产品是否有供给用途。";
                }
                else { group.TownRuleState = "none"; group.TownRuleText = "该产品未识别到城镇供给用途。"; }
                result.Groups.Add(group);
            }
            return result;
        }

        static string LandUseName(int landUse)
        {
            if (landUse == 1) return "住宅";
            if (landUse == 2) return "商业";
            if (landUse == 3) return "工业";
            return "需求区 " + landUse;
        }

        static string Display(IndustryEntry entry)
        {
            if (entry.GameName.Length > 0) return entry.GameName;
            if (entry.Name.Length > 0) return entry.Name;
            return "产业 " + entry.EntityId;
        }
        static string Display(TownEntry town)
        {
            if (town.GameName.Length > 0) return town.GameName;
            if (town.Name.Length > 0) return town.Name;
            return "城镇 " + town.EntityId;
        }

        public static int CompareCandidates(IndustryCandidate a, IndustryCandidate b)
        {
            bool ad = a.Distance.HasValue, bd = b.Distance.HasValue;
            if (ad != bd) return ad ? -1 : 1;
            if (ad)
            {
                int byDistance = a.Distance.Value.CompareTo(b.Distance.Value);
                if (byDistance != 0) return byDistance;
            }
            int byName = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            if (byName != 0) return byName;
            return string.Compare(a.TargetId, b.TargetId, StringComparison.Ordinal);
        }

        public static List<IndustryCandidate> Page(IEnumerable<IndustryCandidate> candidates, int offset, int limit)
        {
            if (offset < 0) offset = 0;
            if (limit < 1) limit = 100;
            if (limit > 100) limit = 100;
            return candidates.Skip(offset).Take(limit).ToList();
        }

        public static IndustryIndex Synthetic()
        {
            var index = new IndustryIndex { Revision = 7, Complete = true, GeneratedAt = 1000 };
            index.CargoNames["1"] = "小麦"; index.CargoNames["2"] = "肉"; index.CargoNames["3"] = "羊毛"; index.CargoNames["4"] = "工具";
            index.CargoNames["5"] = "木板"; index.CargoNames["6"] = "燃料"; index.CargoNames["7"] = "沙发"; index.CargoNames["8"] = "面粉";
            var farm = new IndustryEntry { EntityId = "20", TypeKey = "farm", TypeName = "畜牧场", GameName = "北原畜牧场", Name = "北原畜牧场", RecipeStatus = "verified", RolesVerified = true, X = 0, Y = 0 };
            farm.Inputs.Add(new IndustryCargoRef { CargoId = "1", Role = "required" });
            farm.Outputs.Add(new IndustryCargoRef { CargoId = "2", Role = "output" });
            farm.Outputs.Add(new IndustryCargoRef { CargoId = "3", Role = "output" });
            farm.InputIds.Add("1"); farm.OutputIds.Add("2"); farm.OutputIds.Add("3");
            index.Industries.Add(farm);
            for (int i = 0; i < 23; i++)
            {
                var entry = new IndustryEntry { EntityId = (100 + i).ToString(CultureInfo.InvariantCulture), TypeKey = "farm", TypeName = "农场", GameName = "小麦农场 " + (i + 1), Name = "小麦农场 " + (i + 1), RecipeStatus = "verified", RolesVerified = true,
                    X = 1000 + i * 100, Y = 500, PositionStatus = "ok" };
                entry.Outputs.Add(new IndustryCargoRef { CargoId = "1", Role = "output" }); entry.OutputIds.Add("1");
                index.Industries.Add(entry);
            }
            var warehouse = new IndustryEntry { EntityId = "40", TypeKey = "warehouse", TypeName = "仓库", GameName = "普通仓库", Name = "普通仓库", RecipeStatus = "verified", X = 10, Y = 0 };
            index.Industries.Add(warehouse);
            foreach (var spec in new[] {
                new { id = "30", name = "肉联厂", cargo = "2" }, new { id = "31", name = "罐头厂", cargo = "2" },
                new { id = "32", name = "纺织厂", cargo = "3" }, new { id = "33", name = "综合加工厂", cargo = "2,3" } })
            {
                var entry = new IndustryEntry { EntityId = spec.id, TypeKey = "factory", TypeName = "工厂", GameName = spec.name, Name = spec.name, RecipeStatus = "verified", RolesVerified = true, X = 200 + int.Parse(spec.id), Y = 100 };
                foreach (var cargo in spec.cargo.Split(',')) { entry.Inputs.Add(new IndustryCargoRef { CargoId = cargo, Role = "required" }); entry.InputIds.Add(cargo); }
                index.Industries.Add(entry);
            }
            var optional = new IndustryEntry { EntityId = "50", TypeKey = "toolworks", TypeName = "工具厂", GameName = "工具厂", Name = "工具厂", RecipeStatus = "verified", RolesVerified = true, X = 300, Y = 300 };
            optional.Inputs.Add(new IndustryCargoRef { CargoId = "1", Role = "alternate" });
            optional.Inputs.Add(new IndustryCargoRef { CargoId = "4", Role = "boost" });
            optional.Outputs.Add(new IndustryCargoRef { CargoId = "8", Role = "output" });
            optional.InputIds.Add("1"); optional.InputIds.Add("4"); optional.OutputIds.Add("8");
            index.Industries.Add(optional);
            index.Towns.Add(SyntheticTown("200", "新港", "2", "active", 1, 50, 0));
            index.Towns.Add(SyntheticTown("201", "南岭", "2", "active", 2, 80, 100));
            index.Towns.Add(SyntheticTown("202", "西川", "3", "zero", 1, 120, 0));
            index.Towns.Add(new TownEntry { EntityId = "203", GameName = "东谷", Name = "东谷", NeedsStatus = "unread", DistrictStatus = "partial" });
            index.IndustryCount = index.Industries.Count; index.TownCount = index.Towns.Count;
            return index;
        }

        static TownEntry SyntheticTown(string id, string name, string cargo, string status, int landUse, double x, double y)
        {
            var town = new TownEntry { EntityId = id, GameName = name, Name = name, NeedsStatus = "verified", DistrictStatus = "complete" };
            var need = new TownNeed { CargoId = cargo, Status = status }; need.LandUses.Add(landUse); town.Needs.Add(need);
            town.Districts.Add(new TownDistrict { LandUse = landUse, X = x, Y = y });
            return town;
        }

        public static void Validate()
        {
            var index = Synthetic();
            var analysis = Analyze(index.Find("20"), index);
            if (analysis.Inputs.Count() != 1 || analysis.Inputs.First().Industries.Count != 23)
                throw new InvalidOperationException("产业推荐未返回全部 23 个小麦供应实体。");
            if (analysis.Inputs.First().Industries.Any(c => c.TargetId == "40"))
                throw new InvalidOperationException("没有生产规则的仓库被列为供应产业。");
            if (analysis.Outputs.Count() != 2)
                throw new InvalidOperationException("多产物产业遗漏了产物分组。");
            var meat = analysis.Outputs.First(g => g.CargoId == "2");
            if (meat.Industries.Count != 3 || meat.Towns.Count != 2)
                throw new InvalidOperationException("肉的产业或城镇候选数量不正确。");
            if (meat.Industries.Count(c => c.TargetId == "30") != 1 || meat.Industries.Count(c => c.TargetId == "33") != 1)
                throw new InvalidOperationException("同一货物组内出现重复候选或漏掉多产物消费者。");
            if (meat.Towns.Any(c => c.TargetId == "203"))
                throw new InvalidOperationException("需求读取失败的城镇被当成已确认消费者。");
            var wool = analysis.Outputs.First(g => g.CargoId == "3");
            if (wool.Towns.Count != 1 || wool.Towns[0].RelationKey != "zero")
                throw new InvalidOperationException("零需求城镇状态未区分。");
            if (!meat.Industries.Where(c => c.Distance.HasValue).SequenceEqual(meat.Industries.Where(c => c.Distance.HasValue).OrderBy(c => c.Distance.Value)))
                throw new InvalidOperationException("产业候选未按距离排序。");
            if (meat.Industries.Last().Distance.HasValue && meat.Industries.Any(c => !c.Distance.HasValue))
                throw new InvalidOperationException("距离未知的候选没有排在末尾。");
            var optional = Analyze(index.Find("50"), index);
            if (optional.Inputs.First(g => g.CargoId == "1").Direction != "input" || optional.Inputs.First(g => g.CargoId == "4").Direction != "input")
                throw new InvalidOperationException("替代或增产输入未保留为原料分组。");
            if (RoleText(index.Find("50").Inputs, "1") != "替代原料" || RoleText(index.Find("50").Inputs, "4") != "可选 / 增产材料")
                throw new InvalidOperationException("替代原料或增产材料语义被错误合并。");
            if (Distance(0, 0, 3, 4) != 5) throw new InvalidOperationException("直线距离计算错误。");
            if (Distance(double.NaN, 0, 3, 4).HasValue || Distance(double.PositiveInfinity, 0, 3, 4).HasValue)
                throw new InvalidOperationException("NaN 或无穷坐标产生了有效距离。");
            var page = Page(analysis.Inputs.First().Industries, 20, 100);
            if (page.Count != 3) throw new InvalidOperationException("分页最后一页数量错误。");
            var huge = Enumerable.Range(0, 1001).Select(i => new IndustryCandidate { TargetId = i.ToString(CultureInfo.InvariantCulture), Name = "产业 " + i }).ToList();
            if (Page(huge, 0, 100).Count != 100 || Page(huge, 1000, 100).Count != 1) throw new InvalidOperationException("1001 条结果分页不完整。");
            var reply = Data.Json.DeserializeObject("{\"ok\":true,\"request\":\"r1\",\"world\":1,\"nameScope\":\"m1\",\"revision\":7,\"catalog\":{\"revision\":7,\"industryCount\":1,\"items\":[{\"entityId\":\"1\",\"typeName\":\"农场\"}]}}");
            if (!IsCurrent(reply, "r1", "1", "m1", 7) || IsCurrent(reply, "r1", "2", "m1", 7) || IsCurrent(reply, "r1", "1", "m2", 7) || IsCurrent(reply, "r1", "1", "m1", 8))
                throw new InvalidOperationException("过期或跨地图的产业回复未被拒绝。");
            var sample = Data.Json.DeserializeObject("{\"ok\":true,\"request\":\"r2\",\"world\":\"1\",\"nameScope\":\"m1\",\"revision\":3,\"catalog\":{\"revision\":3,\"complete\":false,\"industryCount\":2,\"townCount\":1,\"unreadIndustryCount\":1,\"unreadTownCount\":0,\"offset\":0,\"total\":2,"
                + "\"items\":[{\"entityId\":\"1\",\"typeKey\":\"farm\",\"typeName\":\"农场\",\"gameName\":\"\",\"name\":\"农场一\",\"position\":{\"x\":10,\"y\":20},\"positionStatus\":\"ok\",\"recipeStatus\":\"partial\",\"rolesVerified\":false,\"inputs\":[{\"cargoId\":\"1\",\"role\":\"required\"}],\"outputs\":[{\"cargoId\":\"2\",\"role\":\"output\"}]},"
                + "{\"entityId\":\"2\",\"typeKey\":\"factory\",\"typeName\":\"工厂\",\"name\":\"工厂\",\"positionStatus\":\"unknown\",\"recipeStatus\":\"unavailable\",\"inputs\":[],\"outputs\":[]}],"
                + "\"towns\":[{\"entityId\":\"9\",\"name\":\"新港\",\"needsStatus\":\"verified\",\"districtStatus\":\"complete\",\"needs\":[{\"cargoId\":\"2\",\"status\":\"active\",\"landUses\":[1,2]}],\"districts\":[{\"landUse\":1,\"x\":5,\"y\":6},{\"landUse\":2,\"x\":7,\"y\":8}]}],"
                + "\"cargo\":[{\"id\":\"1\",\"name\":\"小麦\"},{\"id\":\"2\",\"name\":\"肉\"}]}}");
            var parsed = IndustryCatalogPage.Read(sample, "r2", "1", "m1");
            if (parsed.Items.Count != 2 || parsed.Towns.Count != 1 || parsed.Items[0].InputIds.Count != 1 || parsed.Items[0].X != 10 || parsed.Items[1].HasPosition)
                throw new InvalidOperationException("产业目录 JSON 规范化失败。");
            if (parsed.CargoNames["2"] != "肉" || parsed.Towns[0].Need("2").LandUses.Count != 2 || parsed.Towns[0].Districts.Count != 2)
                throw new InvalidOperationException("城镇需求或货物名称没有从目录回复中读取。");
        }
    }
}
