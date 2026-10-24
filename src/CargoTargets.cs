using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace TransportFever3Trainer
{
    sealed class CargoAllocation
    {
        public int[] Slots, Amounts;
        public int Total { get { return Amounts.Sum(); } }
    }
    sealed class CargoTarget
    {
        public Dictionary<string, object> Row;
        public string Id, Name, Kind;
        public bool Fits;
        public CargoAllocation Allocation;
        public bool Clipped;
        public double? Distance;
        public override string ToString()
        { return Name + " · " + Kind + (Distance.HasValue ? " · " + CargoTargets.DistanceText(Distance.Value) : "") + (!Fits ? " · 已满" : Clipped ? " · 按剩余空间添加" : ""); }
    }
    static class CargoTargets
    {
        static bool Finite(double n) { return !double.IsNaN(n) && !double.IsInfinity(n); }
        public static string DistanceText(double value)
        { return value < 1000 ? value.ToString("N0", CultureInfo.CurrentCulture) + " 米" : (value / 1000).ToString("0.0", CultureInfo.CurrentCulture) + " 千米"; }
        static HashSet<string> Allowed(object slot)
        {
            var set = new HashSet<string>(Data.RowsOfIds(Data.Get(slot, "allowed")));
            if (!Data.Bool(slot, "mixed") && Data.Num(slot, "count") > 0)
                set.IntersectWith(new[] { Data.Str(slot, "cargo") });
            return set;
        }
        // Capacitated matching: distinct cargo types may share a mixed slot, but
        // a non-mixed slot can accept only one of the selected types.
        public static int[] Plan(object stock, IList<string> ids, int count)
        {
            var allocation = Allocate(stock, ids, count);
            return allocation == null || allocation.Amounts.Any(n => n != count) ? null : allocation.Slots;
        }
        static int[] Match(HashSet<string>[] allowed, int[] units, IList<string> ids, IEnumerable<int> active, int[] order)
        {
            var occupants = allowed.Select(s => new List<int>()).ToArray();
            var assigned = Enumerable.Repeat(-1, ids.Count).ToArray();
            Func<int, bool[], bool> place = null;
            place = delegate(int cargo, bool[] seen) {
                foreach (int slot in order)
                {
                    if (seen[slot] || units[slot] == 0 || !allowed[slot].Contains(ids[cargo])) continue;
                    seen[slot] = true;
                    if (occupants[slot].Count < units[slot]) { occupants[slot].Add(cargo); assigned[cargo] = slot; return true; }
                    for (int i = 0; i < occupants[slot].Count; i++)
                        if (place(occupants[slot][i], seen)) { occupants[slot][i] = cargo; assigned[cargo] = slot; return true; }
                }
                return false;
            };
            foreach (int cargo in active) place(cargo, new bool[allowed.Length]);
            return assigned;
        }
        public static CargoAllocation Allocate(object stock, IList<string> ids, int count)
        {
            if (count < 1 || count > 10000 || ids.Count == 0 || ids.Count > 64) return null;
            var slots = Data.Rows(Data.Get(stock, "slots")).OrderBy(s => Data.Num(s, "index")).ToList(); var allowed = slots.Select(Allowed).ToArray();
            var free = slots.Select(s => { double n = Data.Num(s, "capacity", double.NaN) - Data.Num(s, "count", double.NaN); return !Finite(n) || n < 1 ? 0 : (int)Math.Min(int.MaxValue, Math.Floor(n)); }).ToArray();
            var mixed = slots.Select(s => Data.Bool(s, "mixed")).ToArray();
            var locked = Enumerable.Repeat(-1, slots.Count).ToArray();
            var order = Enumerable.Range(0, slots.Count).ToArray();
            var fullUnits = order.Select(s => free[s] < count ? 0 : mixed[s] ? Math.Min(ids.Count, free[s] / count) : 1).ToArray();
            var all = Enumerable.Range(0, ids.Count).ToArray();
            var full = Match(allowed, fullUnits, ids, all, order);
            var result = new CargoAllocation { Slots = Enumerable.Repeat(-1, ids.Count).ToArray(), Amounts = new int[ids.Count] };
            Action<int, int, int> take = delegate(int cargo, int slot, int amount) {
                if (result.Slots[cargo] < 0) result.Slots[cargo] = slot;
                result.Amounts[cargo] += amount; free[slot] -= amount; if (!mixed[slot]) locked[slot] = cargo;
            };
            foreach (int cargo in all) if (full[cargo] >= 0) take(cargo, full[cargo], count);
            var waiting = all.Where(c => result.Amounts[c] == 0).ToArray();
            order = order.OrderBy(s => allowed[s].Count).ThenByDescending(s => free[s]).ThenBy(s => s).ToArray();
            var units = Enumerable.Range(0, slots.Count).Select(s => free[s] < 1 || locked[s] >= 0 ? 0 : mixed[s] ? Math.Min(waiting.Length, free[s]) : 1).ToArray();
            var partial = Match(allowed, units, ids, waiting, order);
            foreach (int slot in order)
            {
                var occupants = waiting.Where(c => partial[c] == slot).ToArray();
                for (int i = 0; i < occupants.Length; i++) take(occupants[i], slot, Math.Min(count, free[slot] / (occupants.Length - i)));
            }
            // Use remaining compatible compartments too, up to the requested
            // amount for each cargo. Full inventories simply contribute zero.
            foreach (int cargo in all) foreach (int slot in order)
                if (result.Amounts[cargo] < count && free[slot] > 0 && allowed[slot].Contains(ids[cargo]) && (mixed[slot] || locked[slot] < 0 || locked[slot] == cargo))
                    take(cargo, slot, Math.Min(count - result.Amounts[cargo], free[slot]));
            return result;
        }
        public static List<CargoTarget> Find(object stocks, IList<string> ids, int count, object camera, Func<Dictionary<string, object>, string> displayName)
        {
            var result = new List<CargoTarget>(); if (ids.Count == 0 || ids.Count > 64 || count < 1 || count > 10000) return result;
            foreach (var row in Data.Rows(stocks))
            {
                var slots = Data.Rows(Data.Get(row, "slots"));
                var allowed = slots.Select(Allowed).ToArray();
                if (!ids.All(id => allowed.Any(s => s.Contains(id)))) continue;
                var allocation = Allocate(row, ids, count);
                var target = new CargoTarget { Row = row, Id = Data.Str(row, "id"), Name = displayName(row), Kind = Data.Str(row, "category", "库存"), Allocation = allocation, Fits = allocation != null && allocation.Total > 0, Clipped = allocation != null && allocation.Amounts.Any(n => n < count) };
                result.Add(target);
            }
            return RefreshDistances(result, camera);
        }
        public static List<CargoTarget> RefreshDistances(IEnumerable<CargoTarget> choices, object camera)
        {
            var result = choices.ToList();
            double cx = Data.Num(camera, "x", double.NaN), cy = Data.Num(camera, "y", double.NaN);
            foreach (var target in result)
            {
                target.Distance = null; var position = Data.Get(target.Row, "position");
                double x = Data.Num(position, "x", double.NaN), y = Data.Num(position, "y", double.NaN);
                if (Finite(cx) && Finite(cy) && Finite(x) && Finite(y))
                { double dx = x - cx, dy = y - cy, distance = Math.Sqrt(dx * dx + dy * dy); if (Finite(distance)) target.Distance = distance; }
            }
            return result.OrderByDescending(t => t.Fits).ThenBy(t => t.Distance ?? double.PositiveInfinity).ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(t => t.Id, StringComparer.Ordinal).ToList();
        }
        public static CargoTarget Recommend(IEnumerable<CargoTarget> choices)
        { return choices.Where(t => t.Fits && t.Distance.HasValue).OrderBy(t => t.Distance.Value).ThenBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault(); }
    }
}
