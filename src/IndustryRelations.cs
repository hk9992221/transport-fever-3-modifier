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
        public List<string> Roles = new List<string>();
        public List<string> Sources = new List<string>();
        public int Alternatives = 1;
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
        // Every observed role is kept, so a cargo that is required in one
        // rule and optional in another is not collapsed into one verdict.
        public string RoleSummary
        {
            get
            {
                var labels = new List<string>();
                foreach (var role in Roles)
                {
                    string text;
                    if (role == "alternate") text = "替代原料";
                    else if (role == "boost") text = "可选 / 增产材料";
                    else if (role == "byproduct") text = "副产品";
                    else if (role == "output") text = "产物";
                    else text = "必需原料";
                    if (!labels.Contains(text)) labels.Add(text);
                }
                if (labels.Count == 0) return RoleText;
                return string.Join(" / ", labels.ToArray());
            }
        }
    }

    sealed class IndustryEntry
    {
        public Dictionary<string, object> Row;
        public string EntityId = "", ConstructionId = "", StockListId = "", TypeKey = "unknown", TypeName = "类型未识别";
        public string GameName = "", Name = "产业", PositionStatus = "unknown", PositionError = "", PositionSource = "";
        public string RecipeStatus = "unavailable", RecipeError = "";
        public string InputStatus = "unavailable", OutputStatus = "unavailable", RoleStatus = "unavailable";
        public int UnparsedRuleCount;
        public bool RolesVerified;
        public int Level, MaxLevel;
        public double X = double.NaN, Y = double.NaN;
        public List<IndustryCargoRef> Inputs = new List<IndustryCargoRef>(), Outputs = new List<IndustryCargoRef>();
        public List<string> InputIds = new List<string>(), OutputIds = new List<string>();
        public bool HasPosition { get { return IndustryRelations.Finite(X) && IndustryRelations.Finite(Y); } }
        public bool RecipeRead { get { return RecipeStatus != "unavailable"; } }
        // A direction is only known to be empty when it was read and no cargo
        // was found. An unread or partially read direction must never be
        // presented as "there is no such relation".
        public bool InputsKnownEmpty { get { return InputStatus == "empty"; } }
        public bool OutputsKnownEmpty { get { return OutputStatus == "empty"; } }
        public bool InputsUncertain { get { return InputStatus == "unavailable" || InputStatus == "partial"; } }
        public bool OutputsUncertain { get { return OutputStatus == "unavailable" || OutputStatus == "partial"; } }
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
        public bool CargoCatalogueComplete;
        public int IndustryCount, TownCount, CargoCount, UnreadIndustryCount, UnreadTownCount;
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
            Revision = 0; Complete = false; CargoCatalogueComplete = false;
            IndustryCount = TownCount = CargoCount = UnreadIndustryCount = UnreadTownCount = 0; GeneratedAt = 0;
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
        public string SupplyState = "unknown", SupplyText = "", DownstreamState = "unknown", DownstreamText = "";
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
        public bool CargoCatalogueComplete;
        public string Part = "industry";
        public int IndustryCount, TownCount, CargoCount, UnreadIndustryCount, UnreadTownCount, Offset, Total;
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
                CargoCatalogueComplete = Data.Bool(catalog, "cargoCatalogueComplete"),
                Part = Data.Str(catalog, "part", "industry"),
                IndustryCount = (int)Data.Num(catalog, "industryCount"),
                TownCount = (int)Data.Num(catalog, "townCount"),
                CargoCount = (int)Data.Num(catalog, "cargoCount"),
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
                InputStatus = Data.Str(row, "inputStatus", "unavailable"),
                OutputStatus = Data.Str(row, "outputStatus", "unavailable"),
                RoleStatus = Data.Str(row, "roleStatus", "unavailable"),
                UnparsedRuleCount = (int)Data.Num(row, "unparsedRuleCount"),
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
            // A cargo id is only dropped when it is not a number at all. The
            // id 0 is a legitimate cargo id (the passenger cargo) and the
            // resource catalogue of the game decides whether it exists, so
            // it must not be removed here.
            if (!IsCargoId(id)) return;
            var reference = new IndustryCargoRef { CargoId = id, Role = Data.Str(row, "role", fallback), Group = Data.Str(row, "group") };
            foreach (var role in Data.RowsOfIds(Data.Get(row, "roles"))) if (!reference.Roles.Contains(role)) reference.Roles.Add(role);
            foreach (var source in Data.RowsOfIds(Data.Get(row, "sources"))) if (!reference.Sources.Contains(source)) reference.Sources.Add(source);
            double alternatives = Data.Num(row, "alternatives", 1);
            reference.Alternatives = alternatives >= 1 && alternatives <= 64 ? (int)alternatives : 1;
            var existing = list.FirstOrDefault(x => x.CargoId == id);
            if (existing == null) { list.Add(reference); return; }
            // Evidence from another source must be merged, never overwrite the
            // role and source evidence that was already collected.
            foreach (var role in reference.Roles) if (!existing.Roles.Contains(role)) existing.Roles.Add(role);
            foreach (var source in reference.Sources) if (!existing.Sources.Contains(source)) existing.Sources.Add(source);
            if (reference.Alternatives > existing.Alternatives) existing.Alternatives = reference.Alternatives;
            if (reference.Role == "required" || reference.Role == "output") existing.Role = reference.Role;
            if (existing.Group.Length == 0) existing.Group = reference.Group;
        }

        // A cargo id must be a non-negative integer. A fractional value is
        // never rounded onto another cargo and a non-numeric value is not an
        // id at all.
        public static bool IsCargoId(string id)
        {
            if (id == null || id.Length == 0) return false;
            foreach (char c in id) if (c < '0' || c > '9') return false;
            if (id.Length > 9) return false;
            return true;
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
                if (!IndustryCatalogPage.IsCargoId(id)) continue;
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
            if (page.Part == "town" || page.Part == "cargo")
            {
                if (index.Revision != page.Revision) throw new InvalidDataException("产业目录分片版本不一致，请重新读取。");
                if (page.Part == "town")
                {
                    foreach (var town in page.Towns) if (index.Towns.All(t => t.EntityId != town.EntityId)) index.Towns.Add(town);
                }
                else
                {
                    foreach (var pair in page.CargoNames) index.CargoNames[pair.Key] = pair.Value;
                }
            }
            else
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
            }
            index.Revision = page.Revision; index.Complete = page.Complete;
            index.CargoCatalogueComplete = page.CargoCatalogueComplete;
            index.IndustryCount = Math.Max(index.IndustryCount, page.IndustryCount);
            index.TownCount = Math.Max(index.TownCount, page.TownCount);
            index.CargoCount = Math.Max(index.CargoCount, page.CargoCount);
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
            return reference == null ? "必需原料" : reference.RoleSummary;
        }

        public static IndustryAnalysis Analyze(IndustryEntry analysis, IndustryIndex index)
        {
            var result = new IndustryAnalysis { Entry = analysis, Index = index };
            var inputIds = new List<string>(); foreach (var reference in analysis.Inputs) if (!inputIds.Contains(reference.CargoId)) inputIds.Add(reference.CargoId);
            foreach (var cargoId in inputIds)
            {
                var inputRef = analysis.Inputs.First(x => x.CargoId == cargoId);
                var group = new IndustryGroup { CargoId = cargoId, CargoName = index.CargoName(cargoId), Direction = "input", TownRuleState = "none", Role = inputRef.Role, RoleText = inputRef.RoleSummary };
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
                // A supplier list is only complete when every industry on the
                // map was read. Otherwise the missing suppliers are unknown,
                // not absent.
                int uncertainSuppliers = index.Industries.Count(e => e.OutputsUncertain);
                if (group.Industries.Count == 0)
                {
                    group.SupplyState = uncertainSuppliers > 0 ? "unread" : "none";
                    group.SupplyText = uncertainSuppliers > 0
                        ? "尚未确认供应该原料的产业，" + uncertainSuppliers + " 座产业的产物规则未读取，结果可能不完整。"
                        : "当前地图未找到生产该原料的产业。";
                }
                else
                {
                    group.SupplyState = uncertainSuppliers > 0 ? "partial" : "complete";
                    group.SupplyText = uncertainSuppliers > 0
                        ? "已确认 " + group.Industries.Count + " 个供应产业，另有 " + uncertainSuppliers + " 座产业的产物规则未读取，结果可能不完整。"
                        : "全部供应产业按直线距离由近到远排列。";
                }
                result.Groups.Add(group);
            }
            var outputIds = new List<string>(); foreach (var reference in analysis.Outputs) if (!outputIds.Contains(reference.CargoId)) outputIds.Add(reference.CargoId);
            foreach (var cargoId in outputIds)
            {
                var outputRef = analysis.Outputs.First(x => x.CargoId == cargoId);
                var group = new IndustryGroup { CargoId = cargoId, CargoName = index.CargoName(cargoId), Direction = "output", Role = outputRef.Role, RoleText = outputRef.RoleSummary };
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
                // The downstream list is only complete when every industry on
                // the map was read. A failure to read another industry's
                // inputs must not be reported as "this product has no
                // downstream".
                int uncertainConsumers = index.Industries.Count(e => e.InputsUncertain);
                if (group.Industries.Count == 0)
                {
                    group.DownstreamState = uncertainConsumers > 0 ? "unread" : "none";
                    group.DownstreamText = uncertainConsumers > 0
                        ? "尚未确认下游产业，" + uncertainConsumers + " 座产业的原料规则未读取，结果可能不完整。"
                        : "当前地图未找到消耗该产品的产业。";
                }
                else
                {
                    group.DownstreamState = uncertainConsumers > 0 ? "partial" : "complete";
                    group.DownstreamText = uncertainConsumers > 0
                        ? "已确认 " + group.Industries.Count + " 个下游产业，另有 " + uncertainConsumers + " 座产业的原料规则未读取，结果可能不完整。"
                        : "";
                }
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
            var index = new IndustryIndex { Revision = 7, Complete = true, CargoCatalogueComplete = true, GeneratedAt = 1000 };
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
                new { id = "32", name = "纺织厂", cargo = "3" }, new { id = "33", name = "综合加工厂", cargo = "2,3" },
                new { id = "34", name = "位置未知加工厂", cargo = "2" } })
            {
                bool unknown = spec.id == "34";
                var entry = new IndustryEntry { EntityId = spec.id, TypeKey = "factory", TypeName = "工厂", GameName = spec.name, Name = spec.name, RecipeStatus = "verified", RolesVerified = true,
                    X = unknown ? double.NaN : 200 + int.Parse(spec.id), Y = unknown ? double.NaN : 100, PositionStatus = unknown ? "unknown" : "ok" };
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

        // The scrap-style regression: a producer and a consumer of the same
        // cargo that only appears in a rule. The consumer must be found, the
        // cargo id must survive the whole pipeline and the warehouse must not
        // become a relation.
        public static IndustryIndex ScrapFixture()
        {
            var index = new IndustryIndex { Revision = 11, Complete = true, CargoCatalogueComplete = true, GeneratedAt = 2000 };
            index.CargoNames["0"] = "旅客";
            index.CargoNames["1"] = "铁矿石";
            index.CargoNames["9"] = "废料";
            index.CargoNames["10"] = "钢材";
            var mill = new IndustryEntry { EntityId = "1001", TypeKey = "steel_mill", TypeName = "钢铁厂", GameName = "钢铁厂", Name = "钢铁厂",
                RecipeStatus = "verified", RolesVerified = true, InputStatus = "verified", OutputStatus = "verified", RoleStatus = "verified",
                X = 10, Y = 10, PositionStatus = "ok" };
            mill.Inputs.Add(new IndustryCargoRef { CargoId = "1", Role = "required", Roles = new List<string> { "required" }, Sources = new List<string> { "rule" } });
            mill.Inputs.Add(new IndustryCargoRef { CargoId = "9", Role = "required", Roles = new List<string> { "required", "alternate" }, Sources = new List<string> { "rule" }, Alternatives = 2 });
            mill.Outputs.Add(new IndustryCargoRef { CargoId = "10", Role = "output", Roles = new List<string> { "output" }, Sources = new List<string> { "rule" } });
            mill.InputIds.Add("1"); mill.InputIds.Add("9"); mill.OutputIds.Add("10");
            index.Industries.Add(mill);
            var yard = new IndustryEntry { EntityId = "1002", TypeKey = "scrap_yard", TypeName = "废料场", GameName = "废料场", Name = "废料场",
                RecipeStatus = "verified", RolesVerified = true, InputStatus = "empty", OutputStatus = "verified", RoleStatus = "verified",
                X = 50, Y = 50, PositionStatus = "ok" };
            yard.Outputs.Add(new IndustryCargoRef { CargoId = "9", Role = "output", Roles = new List<string> { "output" }, Sources = new List<string> { "official" } });
            yard.OutputIds.Add("9");
            index.Industries.Add(yard);
            // A warehouse can store scrap but does not consume it, so it must
            // never appear as a downstream industry.
            var warehouse = new IndustryEntry { EntityId = "1003", TypeKey = "warehouse", TypeName = "仓库", GameName = "仓库", Name = "仓库",
                RecipeStatus = "empty", InputStatus = "empty", OutputStatus = "empty", RoleStatus = "verified", X = 70, Y = 70, PositionStatus = "ok" };
            index.Industries.Add(warehouse);
            index.IndustryCount = index.Industries.Count;
            return index;
        }

        // Every direction of every industry must report whether it was read,
        // and a result set must never claim completeness while part of the
        // map is still unread.
        public static void ValidateCompleteness()
        {
            var index = ScrapFixture();
            var mill = index.Find("1001");
            // The mill consumes scrap, so the scrap group of the mill must
            // list the scrap yard as the supplier.
            var millAnalysis = Analyze(mill, index);
            var scrapInput = millAnalysis.Inputs.FirstOrDefault(g => g.CargoId == "9");
            if (scrapInput == null) throw new InvalidOperationException("消耗的废料没有生成原料分组。");
            if (scrapInput.Industries.Count != 1 || scrapInput.Industries[0].TargetId != "1002")
                throw new InvalidOperationException("生产废料的产业没有被识别为原料供应产业。");
            if (scrapInput.Industries.Any(c => c.TargetId == "1003"))
                throw new InvalidOperationException("可存放该货物的仓库被误识别为供应产业。");
            // Nothing consumes the steel, so the steel group must be an
            // empty downstream and must not borrow another relation.
            var steel = millAnalysis.Outputs.FirstOrDefault(g => g.CargoId == "10");
            if (steel == null) throw new InvalidOperationException("产物钢材没有生成分组。");
            if (steel.Industries.Count != 0) throw new InvalidOperationException("钢材被误判为有下游产业。");
            if (steel.DownstreamState != "none") throw new InvalidOperationException("完整读取后的空结果被误标为待核验。");

            var yard = Analyze(index.Find("1002"), index);
            var scrapGroup = yard.Outputs.First(g => g.CargoId == "9");
            if (scrapGroup.Industries.Count != 1 || scrapGroup.Industries[0].TargetId != "1001")
                throw new InvalidOperationException("废料的消费产业没有被识别。");
            if (scrapGroup.Industries.Any(c => c.TargetId == "1003"))
                throw new InvalidOperationException("可存放该货物的仓库被误识别为消费产业。");
            if (!scrapGroup.Industries[0].Relation.Contains("替代原料"))
                throw new InvalidOperationException("同一货物的多来源角色证据被覆盖。");

            // An unread rule elsewhere on the map must not look like "there
            // is no downstream".
            var uncertain = new IndustryIndex { Revision = 12, Complete = false, CargoCatalogueComplete = true };
            uncertain.CargoNames["9"] = "废料";
            var producer = new IndustryEntry { EntityId = "2001", TypeName = "废料场", RecipeStatus = "verified", InputStatus = "empty", OutputStatus = "verified", RoleStatus = "verified" };
            producer.Outputs.Add(new IndustryCargoRef { CargoId = "9", Role = "output" }); producer.OutputIds.Add("9");
            uncertain.Industries.Add(producer);
            var broken = new IndustryEntry { EntityId = "2002", TypeName = "状态未知加工厂", RecipeStatus = "unavailable", InputStatus = "unavailable", OutputStatus = "unavailable", RoleStatus = "unavailable" };
            uncertain.Industries.Add(broken);
            var unknown = Analyze(producer, uncertain);
            var unknownGroup = unknown.Outputs.First(g => g.CargoId == "9");
            if (unknownGroup.Industries.Count != 0) throw new InvalidOperationException("未读取的产业被当成了消费产业。");
            if (unknownGroup.DownstreamState != "unread")
                throw new InvalidOperationException("未读取完整时没有把下游状态标记为待核验。");
            if (!unknownGroup.DownstreamText.Contains("未读取"))
                throw new InvalidOperationException("未读取完整时没有给出待核验说明。");

            // A fully read map with no consumer is a confirmed empty result.
            var complete = new IndustryIndex { Revision = 13, Complete = true, CargoCatalogueComplete = true };
            complete.CargoNames["9"] = "废料";
            var onlyProducer = new IndustryEntry { EntityId = "3001", TypeName = "废料场", RecipeStatus = "verified", InputStatus = "empty", OutputStatus = "verified", RoleStatus = "verified" };
            onlyProducer.Outputs.Add(new IndustryCargoRef { CargoId = "9", Role = "output" }); onlyProducer.OutputIds.Add("9");
            complete.Industries.Add(onlyProducer);
            var confirmed = Analyze(onlyProducer, complete);
            var confirmedGroup = confirmed.Outputs.First(g => g.CargoId == "9");
            if (confirmedGroup.DownstreamState != "none")
                throw new InvalidOperationException("完整读取后的空结果被误标为待核验。");

            // Cargo id 0 is a real cargo id and must not be filtered away.
            var zeroPage = Data.Json.DeserializeObject("{\"ok\":true,\"request\":\"z1\",\"world\":\"1\",\"nameScope\":\"m1\",\"revision\":4,\"catalog\":{\"revision\":4,\"part\":\"industry\",\"industryCount\":1,\"townCount\":0,\"cargoCount\":2,\"offset\":0,\"total\":1,\"items\":[{\"entityId\":\"1\",\"typeName\":\"农场\",\"recipeStatus\":\"verified\",\"inputStatus\":\"verified\",\"outputStatus\":\"verified\",\"roleStatus\":\"verified\",\"inputs\":[{\"cargoId\":\"0\",\"role\":\"required\",\"roles\":[\"required\"],\"sources\":[\"rule\"]}],\"outputs\":[]}]}}");
            var zeroParsed = IndustryCatalogPage.Read(zeroPage, "z1", "1", "m1");
            if (zeroParsed.Items[0].InputIds.Count != 1 || zeroParsed.Items[0].InputIds[0] != "0")
                throw new InvalidOperationException("合法的货物编号 0 被跨层丢弃。");
            if (!IndustryCatalogPage.IsCargoId("0") || IndustryCatalogPage.IsCargoId("1.5") || IndustryCatalogPage.IsCargoId("-2") || IndustryCatalogPage.IsCargoId("abc") || IndustryCatalogPage.IsCargoId(""))
                throw new InvalidOperationException("货物编号的合法性判断不正确。");
            // A fractional id must not be rounded onto another cargo.
            var fractional = Data.Json.DeserializeObject("{\"ok\":true,\"request\":\"z2\",\"world\":\"1\",\"nameScope\":\"m1\",\"revision\":4,\"catalog\":{\"revision\":4,\"part\":\"industry\",\"industryCount\":1,\"townCount\":0,\"cargoCount\":0,\"offset\":0,\"total\":1,\"items\":[{\"entityId\":\"1\",\"typeName\":\"农场\",\"inputs\":[{\"cargoId\":\"1.5\",\"role\":\"required\"}],\"outputs\":[]}]}}");
            var fractionalParsed = IndustryCatalogPage.Read(fractional, "z2", "1", "m1");
            if (fractionalParsed.Items[0].InputIds.Count != 0)
                throw new InvalidOperationException("非法的小数货物编号被取整成了另一个货物。");
        }

        // A machine readable audit of the current index: for every cargo the
        // producing, consuming and town candidate sets are listed together
        // with the read status that produced them.
        public static List<object> Audit(IndustryIndex index, string source)
        {
            var rows = new List<object>();
            var cargoIds = new SortedSet<string>();
            foreach (var entry in index.Industries)
            {
                foreach (var id in entry.InputIds) cargoIds.Add(id);
                foreach (var id in entry.OutputIds) cargoIds.Add(id);
            }
            foreach (var town in index.Towns) foreach (var need in town.Needs) cargoIds.Add(need.CargoId);
            foreach (var id in index.CargoNames.Keys) cargoIds.Add(id);
            foreach (var cargoId in cargoIds)
            {
                var producers = index.Industries.Where(e => e.OutputIds.Contains(cargoId)).Select(e => e.EntityId).OrderBy(x => x).ToList();
                var consumers = index.Industries.Where(e => e.InputIds.Contains(cargoId)).Select(e => e.EntityId).OrderBy(x => x).ToList();
                var towns = index.Towns.Where(t => t.Need(cargoId) != null).Select(t => t.EntityId).OrderBy(x => x).ToList();
                int uncertainInputs = index.Industries.Count(e => e.InputsUncertain);
                int uncertainOutputs = index.Industries.Count(e => e.OutputsUncertain);
                int uncertainTowns = index.Towns.Count(t => t.NeedsStatus == "unread" || t.NeedsStatus == "partial");
                string verdict;
                if (uncertainInputs > 0 || uncertainOutputs > 0 || uncertainTowns > 0) verdict = "unverified";
                else if (consumers.Count == 0 && producers.Count == 0 && towns.Count == 0) verdict = "notInCurrentMap";
                else verdict = "consistent";
                rows.Add(new
                {
                    cargoId = cargoId,
                    cargoName = index.CargoNames.ContainsKey(cargoId) ? index.CargoNames[cargoId] : "货物 " + cargoId,
                    producers = producers,
                    consumers = consumers,
                    townCandidates = towns,
                    uncertainInputIndustries = uncertainInputs,
                    uncertainOutputIndustries = uncertainOutputs,
                    uncertainTowns = uncertainTowns,
                    verdict = verdict
                });
            }
            return rows;
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
            if (meat.Industries.Count != 4 || meat.Towns.Count != 2)
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
            if (meat.Industries.Last().Distance.HasValue || meat.Industries.Last().PositionStatus != "unknown" || !meat.Industries.Any(c => !c.Distance.HasValue))
                throw new InvalidOperationException("距离未知的候选没有排在末尾或被赋予了假距离。");
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
            if (!parsed.CargoNames.ContainsKey("2")) throw new InvalidOperationException("样本目录缺少货物 2。");
            if (parsed.CargoNames["2"] != "肉" || parsed.Towns[0].Need("2").LandUses.Count != 2 || parsed.Towns[0].Districts.Count != 2)
                throw new InvalidOperationException("城镇需求或货物名称没有从目录回复中读取。");
            var parts = new IndustryIndex(); var partsRevision = 3;
            var industryPart = IndustryCatalogPage.Read(Data.Json.DeserializeObject("{\"ok\":true,\"request\":\"p1\",\"world\":\"1\",\"nameScope\":\"m1\",\"revision\":3,\"catalog\":{\"revision\":3,\"part\":\"industry\",\"industryCount\":1,\"townCount\":1,\"cargoCount\":1,\"offset\":0,\"total\":1,\"items\":[{\"entityId\":\"1\",\"typeName\":\"农场\",\"outputs\":[{\"cargoId\":\"2\",\"role\":\"output\"}]}]}}"), "p1", "1", "m1");
            Merge(parts, industryPart);
            var townPart = IndustryCatalogPage.Read(Data.Json.DeserializeObject("{\"ok\":true,\"request\":\"p2\",\"world\":\"1\",\"nameScope\":\"m1\",\"revision\":3,\"catalog\":{\"revision\":3,\"part\":\"town\",\"industryCount\":1,\"townCount\":1,\"cargoCount\":1,\"offset\":0,\"total\":1,\"towns\":[{\"entityId\":\"9\",\"name\":\"新港\",\"needsStatus\":\"verified\",\"districtStatus\":\"complete\",\"needs\":[{\"cargoId\":\"2\",\"status\":\"active\",\"landUses\":[1]}],\"districts\":[{\"landUse\":1,\"x\":5,\"y\":6}]}]}}"), "p2", "1", "m1");
            Merge(parts, townPart);
            var cargoPart = IndustryCatalogPage.Read(Data.Json.DeserializeObject("{\"ok\":true,\"request\":\"p3\",\"world\":\"1\",\"nameScope\":\"m1\",\"revision\":3,\"catalog\":{\"revision\":3,\"part\":\"cargo\",\"industryCount\":1,\"townCount\":1,\"cargoCount\":1,\"offset\":0,\"total\":1,\"cargo\":[{\"id\":\"2\",\"name\":\"肉\"}]}}"), "p3", "1", "m1");
            Merge(parts, cargoPart);
            if (!parts.CargoNames.ContainsKey("2")) throw new InvalidOperationException("分片合并后缺少货物 2。");
            if (parts.Industries.Count != 1 || parts.Towns.Count != 1 || parts.CargoCount != 1 || parts.CargoNames["2"] != "肉" || parts.Revision != partsRevision)
                throw new InvalidOperationException("产业、城镇与货物分片没有合并为完整索引。");
            ValidateCompleteness();

            // The audit output must be machine readable and must classify every
            // cargo of the map.
            var audit = Audit(index, "offline");
            if (audit.Count == 0) throw new InvalidOperationException("审计没有产生任何货物记录。");
            var serialized = Data.Json.Serialize(audit);
            if (!serialized.Contains("\"verdict\"") || !serialized.Contains("\"consumers\"") || !serialized.Contains("\"producers\""))
                throw new InvalidOperationException("审计结果缺少货物集合或结论字段。");
            var auditRows = Data.Rows(Data.Json.DeserializeObject(serialized));
            if (auditRows.Count != audit.Count) throw new InvalidOperationException("审计结果无法按 JSON 读回。");
            foreach (var row in auditRows)
            {
                string verdict = Data.Str(row, "verdict");
                if (verdict != "consistent" && verdict != "notInCurrentMap" && verdict != "unverified")
                    throw new InvalidOperationException("审计结论取值不在规格允许的范围内。");
                if (Data.Str(row, "cargoId").Length == 0) throw new InvalidOperationException("审计记录缺少货物编号。");
            }
        }
    }
}
