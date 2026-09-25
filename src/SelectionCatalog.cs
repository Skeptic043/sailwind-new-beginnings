using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NewBeginnings
{
    internal enum BoatSize { Small = 1, Medium = 2, Large = 3 }
    internal enum PortPool { AlAnkh, Emerald, Aestrin, FireFishLagoon, SmallIslands }

    internal sealed class PortChoice
    {
        internal Port Port;
        internal RecoveryPort Recovery;
        internal PortPool? Pool; // null means manual selection only (for example Chronos).
        internal string DisplayName;
        internal int Index => Port.portIndex;
        internal string IdentityName => Port.gameObject.name;
    }

    internal sealed class BoatChoice
    {
        internal PurchasableBoat Boat;
        internal SaveableObject Saveable;
        internal BoatSize? Size; // An unknown boat remains available for explicit selection.
        internal string DisplayName;
        internal string OriginGroup;
        internal int Index => Saveable.sceneIndex;
        internal string IdentityName => Boat.gameObject.name;
    }

    internal sealed class StartPair
    {
        internal PortChoice Port;
        internal BoatChoice Boat;
    }

    internal sealed class SelectionCatalog
    {
        internal readonly List<PortChoice> Ports = new List<PortChoice>();
        internal readonly List<BoatChoice> Boats = new List<BoatChoice>();
        internal readonly List<string> Rejections = new List<string>();

        private static readonly FieldInfo RecoveryPortsField = AccessTools.Field(typeof(Recovery), "ports");
        private static readonly FieldInfo SaveableField = AccessTools.Field(typeof(PurchasableBoat), "saveable");
        private static readonly FieldInfo PurchaseUiField = AccessTools.Field(typeof(PurchasableBoat), "purchaseUI");
        private static readonly HashSet<string> LoggedBoatInspectionFailures = new HashSet<string>();

        // PurchasableBoat also backs beta houses. Identify a hull by its native
        // boat components instead of a version-specific house flag. One component
        // is enough here so a damaged or incomplete boat still reserves its save
        // index before the full placement checks below.
        internal static bool IsBoatLike(SaveableObject saveable) =>
            saveable != null && (saveable.GetComponent<Rigidbody>() != null ||
                saveable.GetComponent<BoatDamage>() != null ||
                saveable.GetComponent("BoatProbes") != null ||
                saveable.GetComponent<BoatMooringRopes>() != null);

        // These names and indexes came from the installed level24. A name check protects
        // geography and sizing if another mod reuses an index for a different object.
        // Unknown ports remain manual-only until a user classifies their geography.
        private static readonly Dictionary<int, Tuple<string, BoatSize>> OfficialSizes =
            new Dictionary<int, Tuple<string, BoatSize>>
            {
                { 10, Tuple.Create("BOAT dhow small (10)", BoatSize.Small) },
                { 20, Tuple.Create("BOAT dhow medium (20)", BoatSize.Medium) },
                { 30, Tuple.Create("BOAT dhow large (30)", BoatSize.Large) },
                { 40, Tuple.Create("BOAT medi small (40)", BoatSize.Small) },
                { 50, Tuple.Create("BOAT medi medium (50)", BoatSize.Medium) },
                { 70, Tuple.Create("BOAT junk large (70)", BoatSize.Large) },
                { 80, Tuple.Create("BOAT junk medium (80)", BoatSize.Medium) },
                { 90, Tuple.Create("BOAT junk small singleroof(90)", BoatSize.Small) }
            };

        private static readonly Dictionary<int, string> OfficialBoatNames =
            new Dictionary<int, string>
            {
                { 10, "Dhow" }, { 20, "Sanbuq" }, { 30, "Baghlah" },
                { 40, "Cog" }, { 50, "Brig" }, { 70, "Jong" },
                { 80, "Junk" }, { 90, "Kakam" }
            };

        // Match mod boats by both scene index and complete runtime object name.
        // The Shattered Seas prefabs declare Clipper as Shroud Large (153) and
        // Sh'ba as Shroud Small (160); Sh'ba's cloned name is also live-logged.
        // Old Chronian identities come from its distributed prefabs and loader.
        // Unknown boats and Leopard's separate cutter remain manual-only.
        private const string UnknownBoatOrigin = "Other / unclassified";
        private static readonly Dictionary<int, Tuple<string, BoatSize, string, string>> ModBoats =
            new Dictionary<int, Tuple<string, BoatSize, string, string>>
            {
                { 144, Tuple.Create("BOAT happybayboat (144)(Clone)", BoatSize.Medium, "Happy Bay Boat", "Happy Bay Boat") },
                { 153, Tuple.Create("BOAT Shroud Large(Clone)", BoatSize.Large, "Clipper", "Shattered Seas") },
                { 160, Tuple.Create("BOAT Shroud Small (160)(Clone)", BoatSize.Medium, "Sh'ba", "Shattered Seas") },
                { 182, Tuple.Create("BOAT GLORIANA (182)(Clone)", BoatSize.Large, "Gloriana", "Old Chronian") },
                { 187, Tuple.Create("BOAT CHRONIAN (187)(Clone)", BoatSize.Large, "Aelasyl", "Old Chronian") },
                { 192, Tuple.Create("BOAT CAELANOR (192)(Clone)", BoatSize.Medium, "Caelanor", "Old Chronian") },
                { 197, Tuple.Create("BOAT GALLUS (197)(Clone)", BoatSize.Small, "Gallus", "Old Chronian") },
                { 207, Tuple.Create("BOAT LEOPARD (207)(Clone)", BoatSize.Large, "HMS Leopard", "HMS Leopard") },
                { 275, Tuple.Create("FFL Paraw", BoatSize.Small, "FFL Paraw", "FFL Paraw") }
            };

        // The three stock boat families are sold in these home archipelagos.
        // Match the exact scene object before applying this ordering to avoid
        // assigning a mod boat with a reused scene index to the wrong group.
        private static readonly Dictionary<int, PortPool> OfficialBoatHomes =
            new Dictionary<int, PortPool>
            {
                { 10, PortPool.AlAnkh }, { 20, PortPool.AlAnkh },
                { 30, PortPool.AlAnkh }, { 90, PortPool.Emerald },
                { 80, PortPool.Emerald }, { 70, PortPool.Emerald },
                { 40, PortPool.Aestrin }, { 50, PortPool.Aestrin }
            };

        private static readonly Dictionary<int, Tuple<string, PortPool?>> KnownPorts =
            new Dictionary<int, Tuple<string, PortPool?>>
            {
                { 0, Tuple.Create<string, PortPool?>("port A0 (Gold Rock)", PortPool.AlAnkh) },
                { 1, Tuple.Create<string, PortPool?>("port A1 (Alnilem)", PortPool.AlAnkh) },
                { 2, Tuple.Create<string, PortPool?>("port A2 (Neverdin)", PortPool.AlAnkh) },
                { 3, Tuple.Create<string, PortPool?>("port A3 (Fish Island)", PortPool.AlAnkh) },
                { 4, Tuple.Create<string, PortPool?>("port A4 (alchemist)", PortPool.AlAnkh) },
                { 5, Tuple.Create<string, PortPool?>("port A5 (Academy)", PortPool.AlAnkh) },
                { 6, Tuple.Create<string, PortPool?>("port A/M 6 (Oasis)", PortPool.SmallIslands) },
                { 9, Tuple.Create<string, PortPool?>("port E 9 (Dragon cliffs)", PortPool.Emerald) },
                { 10, Tuple.Create<string, PortPool?>("port E 10 sanctuary", PortPool.Emerald) },
                { 11, Tuple.Create<string, PortPool?>("port E 11 crab beach", PortPool.Emerald) },
                { 12, Tuple.Create<string, PortPool?>("port E 12 New Port", PortPool.Emerald) },
                { 13, Tuple.Create<string, PortPool?>("port E 13 Sage Hills", PortPool.Emerald) },
                { 14, Tuple.Create<string, PortPool?>("port E 14 Serpent Isle", PortPool.Emerald) },
                { 15, Tuple.Create<string, PortPool?>("port M 15 Fort", PortPool.Aestrin) },
                { 16, Tuple.Create<string, PortPool?>("port M 16 Sunspire", PortPool.Aestrin) },
                { 17, Tuple.Create<string, PortPool?>("port M 17 Mount Malefic", PortPool.Aestrin) },
                { 18, Tuple.Create<string, PortPool?>("port M 18 Siren Song", PortPool.Aestrin) },
                { 19, Tuple.Create<string, PortPool?>("port M 19 (Eastwind)", PortPool.Aestrin) },
                { 20, Tuple.Create<string, PortPool?>("port M/E 20 Happy Bay", PortPool.SmallIslands) },
                { 21, Tuple.Create<string, PortPool?>("port 21 M chronos", null) },
                { 22, Tuple.Create<string, PortPool?>("port L 22 Lagoon Bay", PortPool.FireFishLagoon) },
                { 23, Tuple.Create<string, PortPool?>("port L 23 Lagoon Fire Fish Town", PortPool.FireFishLagoon) },
                { 24, Tuple.Create<string, PortPool?>("port L 24 Lagoon Onna", PortPool.FireFishLagoon) },
                { 25, Tuple.Create<string, PortPool?>("port L 25 Lagoon Senna", PortPool.FireFishLagoon) },
                { 26, Tuple.Create<string, PortPool?>("port M 26 (Monastery)", PortPool.Aestrin) },
                { 27, Tuple.Create<string, PortPool?>("port M 27 (Valley)", PortPool.Aestrin) },
                { 28, Tuple.Create<string, PortPool?>("port M 28 (Cave)", PortPool.Aestrin) },
                { 29, Tuple.Create<string, PortPool?>("port E 29 (jungle)", PortPool.Emerald) },
                { 30, Tuple.Create<string, PortPool?>("port E 30 (swamp)", PortPool.Emerald) },
                { 31, Tuple.Create<string, PortPool?>("port A 31 (coffee)", PortPool.AlAnkh) },
                { 32, Tuple.Create<string, PortPool?>("port A 32 (mirage mountain)", PortPool.SmallIslands) },
                { 33, Tuple.Create<string, PortPool?>("port A 33 (flower)", PortPool.SmallIslands) }
            };

        internal static SelectionCatalog Discover(SelectionOverrides overrides)
        {
            var result = new SelectionCatalog();
            if (RecoveryPortsField == null || SaveableField == null || PurchaseUiField == null)
            {
                result.Rejections.Add("Sailwind ownership or recovery fields were not found in this game build.");
                return result;
            }

            var registered = RecoveryPortsField.GetValue(null) as List<RecoveryPort>;
            var seenPortIndexes = new HashSet<int>();
            if (Port.ports == null || registered == null)
                result.Rejections.Add("Ports or recovery berths have not registered yet.");
            else
            {
                foreach (var port in Port.ports.Where(item => item != null))
                {
                    var index = port.portIndex;
                    if (index == 7 && string.Equals(port.gameObject.name,
                        "port 7 (test port)", StringComparison.OrdinalIgnoreCase))
                    {
                        result.Rejections.Add("Port 7 is a scene test port, not a playable destination.");
                        continue;
                    }
                    if (!seenPortIndexes.Add(index))
                    {
                        result.Rejections.Add($"Port index {index} is registered more than once.");
                        continue;
                    }
                    // Island performance switching can deactivate a distant port after
                    // Port.Start registered it. Registration and a loaded scene are the
                    // stable identity checks; activeInHierarchy is camera-dependent.
                    if (!port.gameObject.scene.IsValid() ||
                        !port.gameObject.scene.isLoaded || !port.enabled)
                    {
                        result.Rejections.Add($"Port {index} is disabled or outside the loaded scene.");
                        continue;
                    }
                    if (index < 0 || index >= Port.ports.Length || !ReferenceEquals(Port.ports[index], port))
                    {
                        result.Rejections.Add($"Port {index} does not match Port.ports registration.");
                        continue;
                    }
                    if ((int)port.region < 0 || (int)port.region > 2)
                    {
                        result.Rejections.Add($"Port {index} has an unsupported starter region.");
                        continue;
                    }
                    var berths = registered.Where(item => item != null && item.parentPort == port &&
                        item.gameObject.scene.IsValid() && item.gameObject.scene.isLoaded && item.enabled &&
                        item.boatPos != null && item.mooringFront != null && item.mooringBack != null).ToArray();
                    if (berths.Length != 1)
                    {
                        result.Rejections.Add($"Port {index} has {berths.Length} complete direct recovery berths; expected one.");
                        continue;
                    }
                    var name = port.GetPortName();
                    result.Ports.Add(new PortChoice
                    {
                        Port = port,
                        Recovery = berths[0],
                        Pool = FindPool(port, overrides),
                        DisplayName = string.IsNullOrEmpty(name) ? port.gameObject.name : name
                    });
                }
            }

            var seenBoatIndexes = new HashSet<int>();
            foreach (var boat in Resources.FindObjectsOfTypeAll<PurchasableBoat>())
            {
                try
                {
                    if (boat == null || !boat.gameObject.scene.IsValid() ||
                        !boat.gameObject.scene.isLoaded || !boat.gameObject.activeInHierarchy || !boat.enabled)
                        continue;
                    var saveable = SaveableField.GetValue(boat) as SaveableObject;
                    if (!IsBoatLike(saveable)) continue;
                    var index = saveable.sceneIndex;
                    // Reserve every active boat identity before component validation. A second
                    // object with the same index makes native save ownership ambiguous even if
                    // only one of the two would otherwise pass this catalog's checks.
                    if (index >= 0 && !seenBoatIndexes.Add(index))
                    {
                        result.Rejections.Add($"Boat scene index {index} is ambiguous.");
                        result.Boats.RemoveAll(item => item.Index == index);
                        continue;
                    }
                    if (index < 0 || saveable.gameObject != boat.gameObject ||
                        PurchaseUiField.GetValue(boat) as GameObject == null || boat.isPurchased())
                    {
                        result.Rejections.Add($"Boat {boat.gameObject.name} ({index}) lacks normal unowned purchase state.");
                        continue;
                    }
                    var body = saveable.GetComponent<Rigidbody>();
                    var ropes = saveable.GetComponent<BoatMooringRopes>();
                    if (body == null || !ProbeVelocityGuard.CanGuard(saveable) ||
                        saveable.GetComponent<BoatDamage>() == null ||
                        saveable.GetComponent<BoatLocalItems>() == null ||
                        ropes == null || ropes.ropes == null || ropes.ropes.Length == 0 ||
                        ropes.ropes.Any(item => item == null) || ropes.GetAnchorController() == null)
                    {
                        result.Rejections.Add($"Boat {boat.gameObject.name} ({index}) lacks placement, mooring, or save components.");
                        continue;
                    }
                    var size = FindSize(index, boat.gameObject.name, overrides);
                    result.Boats.Add(new BoatChoice
                    {
                        Boat = boat, Saveable = saveable, Size = size,
                        DisplayName = FindBoatName(index, boat.gameObject.name),
                        OriginGroup = FindBoatOrigin(index, boat.gameObject.name)
                    });
                }
                catch (Exception exception)
                {
                    // A broken optional candidate must not hide other playable boats.
                    // A boat-like candidate already reserved its index above.
                    var failure = exception.GetType().Name + ": " + exception.Message;
                    result.Rejections.Add("A purchasable boat candidate could not be inspected: " + failure);
                    if (LoggedBoatInspectionFailures.Add(failure))
                        Plugin.Instance?.Warn("A purchasable boat candidate was skipped: " + failure);
                }
            }
            // Keep the manual selectors in geographic groups. A port with no
            // classified pool (including Chronos) remains selectable at the end.
            result.Ports.Sort((left, right) =>
            {
                var byPool = PoolSortKey(left.Pool).CompareTo(PoolSortKey(right.Pool));
                return byPool != 0 ? byPool : left.Index.CompareTo(right.Index);
            });
            result.Boats.Sort(CompareBoats);
            return result;
        }

        private static int CompareBoats(BoatChoice left, BoatChoice right) =>
            CompareBoatOrder(left.Index, left.IdentityName, left.Size, left.OriginGroup,
                right.Index, right.IdentityName, right.Size, right.OriginGroup);

        private static int CompareBoatOrder(int leftIndex, string leftName, BoatSize? leftSize,
            string leftOrigin, int rightIndex, string rightName, BoatSize? rightSize, string rightOrigin)
        {
            var leftHome = OfficialBoatHome(leftIndex, leftName);
            var rightHome = OfficialBoatHome(rightIndex, rightName);
            var byHome = PoolSortKey(leftHome).CompareTo(PoolSortKey(rightHome));
            if (byHome != 0) return byHome;
            if (!leftHome.HasValue)
            {
                // Exact known origins are grouped after vanilla. A reused index
                // or unrecognized prefab stays in an explicit final group.
                var leftGroup = leftOrigin ?? UnknownBoatOrigin;
                var rightGroup = rightOrigin ?? UnknownBoatOrigin;
                var byKnownOrigin = (leftGroup == UnknownBoatOrigin ? 1 : 0).CompareTo(
                    rightGroup == UnknownBoatOrigin ? 1 : 0);
                if (byKnownOrigin != 0) return byKnownOrigin;
                var byOrigin = StringComparer.OrdinalIgnoreCase.Compare(leftGroup, rightGroup);
                if (byOrigin != 0) return byOrigin;
            }
            var bySize = (leftSize.HasValue ? (int)leftSize.Value : int.MaxValue).CompareTo(
                rightSize.HasValue ? (int)rightSize.Value : int.MaxValue);
            return bySize != 0 ? bySize : leftIndex.CompareTo(rightIndex);
        }

        private static PortPool? OfficialBoatHome(int index, string name)
        {
            if (OfficialSizes.TryGetValue(index, out var official) &&
                string.Equals(name, official.Item1, StringComparison.OrdinalIgnoreCase) &&
                OfficialBoatHomes.TryGetValue(index, out var home))
                return home;
            return null;
        }

        private static int PoolSortKey(PortPool? pool)
        {
            switch (pool)
            {
                case PortPool.AlAnkh: return 0;
                case PortPool.Emerald: return 1;
                case PortPool.Aestrin: return 2;
                case PortPool.FireFishLagoon: return 3;
                case PortPool.SmallIslands: return 4;
                default: return 5;
            }
        }

        private static PortPool? FindPool(Port port, SelectionOverrides overrides)
        {
            if (overrides != null && overrides.TryPortPool(port.portIndex, port.gameObject.name, out var pool))
                return pool;
            if (KnownPorts.TryGetValue(port.portIndex, out var known) &&
                string.Equals(port.gameObject.name, known.Item1, StringComparison.OrdinalIgnoreCase))
                return known.Item2;
            return null;
        }

        private static BoatSize? FindSize(int index, string name, SelectionOverrides overrides)
        {
            if (overrides != null && overrides.TryBoatSize(index, name, out var overrideSize))
                return overrideSize;
            if (OfficialSizes.TryGetValue(index, out var official) &&
                string.Equals(name, official.Item1, StringComparison.OrdinalIgnoreCase))
                return official.Item2;
            if (ModBoats.TryGetValue(index, out var modBoat) &&
                string.Equals(name, modBoat.Item1, StringComparison.OrdinalIgnoreCase))
                return modBoat.Item2;
            if (IsDinghy(index, name)) return BoatSize.Small;
            return null;
        }

        private static string FindBoatName(int index, string name)
        {
            if (OfficialSizes.TryGetValue(index, out var official) &&
                string.Equals(name, official.Item1, StringComparison.OrdinalIgnoreCase))
                return OfficialBoatNames[index];
            if (ModBoats.TryGetValue(index, out var modBoat) &&
                string.Equals(name, modBoat.Item1, StringComparison.OrdinalIgnoreCase))
                return modBoat.Item3;
            if (IsDinghy(index, name)) return "Cutter";
            return name;
        }

        private static string FindBoatOrigin(int index, string name)
        {
            if (OfficialSizes.TryGetValue(index, out var official) &&
                string.Equals(name, official.Item1, StringComparison.OrdinalIgnoreCase))
                return "Sailwind";
            if (ModBoats.TryGetValue(index, out var modBoat) &&
                string.Equals(name, modBoat.Item1, StringComparison.OrdinalIgnoreCase))
                return modBoat.Item4;
            // Leopard's loader also instantiates its separate onboard cutter.
            // Group its exact identity without adding a random size class.
            if (index == 212 && string.Equals(name, "BOAT CUTTER (212)(Clone)",
                StringComparison.OrdinalIgnoreCase)) return "HMS Leopard";
            if (IsDinghy(index, name)) return "Dinghies";
            return UnknownBoatOrigin;
        }

        private static bool IsDinghy(int index, string name)
        {
            // Dinghies 1.0.11 replaces the prefab's serialized index 130 with the
            // first free save index before cloning it. Match the complete cloned
            // name and the loader's own assignment, not a fixed index observed in
            // one save. If its internals change, keep the boat manual-only.
            if (index <= 0 || !string.Equals(name, "DNG Cutter(Clone)",
                StringComparison.OrdinalIgnoreCase)) return false;
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(item =>
                string.Equals(item.GetName().Name, "Dinghies", StringComparison.Ordinal));
            var type = assembly?.GetType("Dinghies.IndexManager", false);
            var field = type?.GetField("indexMap", BindingFlags.Public | BindingFlags.Static);
            var assigned = field?.GetValue(null) as Dictionary<string, int>;
            return assigned != null && assigned.TryGetValue("DNG Cutter", out var dinghyIndex) &&
                dinghyIndex == index;
        }

        internal bool TryChoose(SelectionSettings settings,
            IndividualExclusions individualExclusions, System.Random random,
            out StartPair chosen, out string reason)
        {
            chosen = null;
            reason = null;
            if (settings == null || random == null)
            {
                reason = "Selection settings or random source are missing.";
                return false;
            }
            if (settings.RandomPort && settings.PortPools.Count == 0 ||
                settings.RandomBoat && settings.BoatSizes.Count == 0)
            {
                reason = "At least one random port pool and boat size pool must remain selected.";
                return false;
            }
            var ports = settings.RandomPort
                ? Ports.Where(item => item.Pool.HasValue && settings.PortPools.Contains(item.Pool.Value) &&
                    (individualExclusions == null || !individualExclusions.IsPortExcluded(item))).ToArray()
                : Ports.Where(item => item.Index == settings.PortIndex).ToArray();
            var boats = settings.RandomBoat
                ? Boats.Where(item => item.Size.HasValue && settings.BoatSizes.Contains(item.Size.Value) &&
                    (individualExclusions == null || !individualExclusions.IsBoatExcluded(item))).ToArray()
                : Boats.Where(item => item.Index == settings.BoatSceneIndex).ToArray();
            if (ports.Length == 0)
            {
                reason = settings.RandomPort && Ports.Any(item => item.Pool.HasValue &&
                    settings.PortPools.Contains(item.Pool.Value))
                    ? "Every island in the checked pools is individually excluded from random starts."
                    : settings.RandomPort ? "No validated ports match the checked geographic pools."
                    : $"Selected port {settings.PortIndex} is not a validated port.";
                return false;
            }
            if (boats.Length == 0)
            {
                reason = settings.RandomBoat && Boats.Any(item => item.Size.HasValue &&
                    settings.BoatSizes.Contains(item.Size.Value))
                    ? "Every boat in the checked sizes is individually excluded from random starts."
                    : settings.RandomBoat ? "No classified, validated boats match the checked size pools."
                    : $"Selected boat {settings.BoatSceneIndex} is not a validated boat.";
                return false;
            }

            // Sample uniformly among structurally usable choices without
            // allocating the full boat/port cross-product.
            var count = 0;
            foreach (var port in ports)
                foreach (var boat in boats)
                {
                    if (!FixedStart.IsPairViable(port, boat)) continue;
                    ++count;
                    if (random.Next(count) == 0)
                        chosen = new StartPair { Port = port, Boat = boat };
                }
            if (chosen != null) return true;
            reason = "No selected boat and port pair has a usable, unoccupied recovery berth.";
            return false;
        }
    }

    internal sealed class SelectionSettings
    {
        internal bool RandomPort { get; set; }
        internal bool RandomBoat { get; set; }
        internal int PortIndex { get; set; }
        internal int BoatSceneIndex { get; set; }
        internal readonly HashSet<PortPool> PortPools = new HashSet<PortPool>
            { PortPool.AlAnkh, PortPool.Emerald, PortPool.Aestrin };
        internal readonly HashSet<BoatSize> BoatSizes = new HashSet<BoatSize>
            { BoatSize.Small, BoatSize.Medium, BoatSize.Large };

        internal bool SetPortPool(PortPool pool, bool enabled)
        {
            if (!enabled && PortPools.Count == 1 && PortPools.Contains(pool)) return false;
            if (enabled) PortPools.Add(pool); else PortPools.Remove(pool);
            return true;
        }

        internal bool SetBoatSize(BoatSize size, bool enabled)
        {
            if (!enabled && BoatSizes.Count == 1 && BoatSizes.Contains(size)) return false;
            if (enabled) BoatSizes.Add(size); else BoatSizes.Remove(size);
            return true;
        }
    }

    internal sealed class SelectionOverrides
    {
        // Config strings use semicolon-separated records. Boat sizes:
        // sceneIndex|exactGameObjectName|Small (or Medium/Large).
        // Geographic pools: portIndex|exactGameObjectName|AlAnkh (or Emerald,
        // Aestrin, FireFishLagoon, SmallIslands).
        // Exact names guard against future index reuse.

        private readonly Dictionary<int, Tuple<string, BoatSize>> boatSizes =
            new Dictionary<int, Tuple<string, BoatSize>>();
        private readonly Dictionary<int, Tuple<string, PortPool>> portPools =
            new Dictionary<int, Tuple<string, PortPool>>();
        internal readonly List<string> Errors = new List<string>();

        internal static SelectionOverrides Parse(string boatSizeRecords,
            string portPoolRecords = null)
        {
            var result = new SelectionOverrides();
            foreach (var record in SplitRecords(boatSizeRecords))
            {
                var parts = record.Split('|');
                if (parts.Length != 3 || !int.TryParse(parts[0].Trim(), out var index) || index < 0 ||
                    string.IsNullOrWhiteSpace(parts[1]) || !Enum.TryParse(parts[2].Trim(), true, out BoatSize size) ||
                    !Enum.IsDefined(typeof(BoatSize), size) || result.boatSizes.ContainsKey(index))
                {
                    result.Errors.Add("Invalid or duplicate boat size override: " + record);
                    continue;
                }
                result.boatSizes.Add(index, Tuple.Create(parts[1].Trim(), size));
            }
            foreach (var record in SplitRecords(portPoolRecords))
            {
                var parts = record.Split('|');
                if (parts.Length != 3 || !int.TryParse(parts[0].Trim(), out var index) || index < 0 ||
                    string.IsNullOrWhiteSpace(parts[1]) ||
                    !Enum.TryParse(parts[2].Trim(), true, out PortPool pool) ||
                    !Enum.IsDefined(typeof(PortPool), pool) || result.portPools.ContainsKey(index))
                {
                    result.Errors.Add("Invalid or duplicate port pool override: " + record);
                    continue;
                }
                result.portPools.Add(index, Tuple.Create(parts[1].Trim(), pool));
            }
            return result;
        }

        private static IEnumerable<string> SplitRecords(string text)
        {
            return (text ?? "").Split(new[] { ';', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries).Select(item => item.Trim())
                .Where(item => item.Length > 0);
        }

        internal bool TryBoatSize(int index, string name, out BoatSize size)
        {
            size = default;
            if (!boatSizes.TryGetValue(index, out var entry) ||
                !string.Equals(name, entry.Item1, StringComparison.OrdinalIgnoreCase)) return false;
            size = entry.Item2;
            return true;
        }

        internal bool TryPortPool(int index, string name, out PortPool pool)
        {
            pool = default;
            if (!portPools.TryGetValue(index, out var entry) ||
                !string.Equals(name, entry.Item1, StringComparison.OrdinalIgnoreCase)) return false;
            pool = entry.Item2;
            return true;
        }

    }
}
