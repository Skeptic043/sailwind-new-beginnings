using System;
using System.Collections.Generic;
using System.Linq;

namespace NewBeginnings
{
    // Store exact runtime object names, not scene indices: optional boats may
    // receive a different index on each launch. Missing mods retain their saved
    // exclusions so the choice still applies if the mod returns later.
    internal sealed class IndividualExclusions
    {
        private readonly HashSet<string> excludedPorts =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> excludedBoats =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal bool IsPortExcluded(PortChoice port) =>
            port != null && excludedPorts.Contains(port.IdentityName);

        internal bool IsBoatExcluded(BoatChoice boat) =>
            boat != null && excludedBoats.Contains(boat.IdentityName);

        internal bool SetPortExcluded(PortChoice port, bool excluded) =>
            port != null && Set(excludedPorts, port.IdentityName, excluded);

        internal bool SetBoatExcluded(BoatChoice boat, bool excluded) =>
            boat != null && Set(excludedBoats, boat.IdentityName, excluded);

        internal IndividualExclusions Copy()
        {
            var copy = new IndividualExclusions();
            copy.ReplaceWith(this);
            return copy;
        }

        internal void ReplaceWith(IndividualExclusions source)
        {
            if (source == null || ReferenceEquals(source, this)) return;
            excludedPorts.Clear();
            excludedBoats.Clear();
            excludedPorts.UnionWith(source.excludedPorts);
            excludedBoats.UnionWith(source.excludedBoats);
        }

        internal string SavePorts() => Encode(excludedPorts);
        internal string SaveBoats() => Encode(excludedBoats);

        internal static IndividualExclusions Load(string ports, string boats)
        {
            var result = new IndividualExclusions();
            Decode(ports, result.excludedPorts);
            Decode(boats, result.excludedBoats);
            return result;
        }

        private static bool Set(HashSet<string> names, string name, bool excluded)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return excluded ? names.Add(name) : names.Remove(name);
        }

        private static string Encode(HashSet<string> names) => string.Join(";",
            names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .Select(Uri.EscapeDataString));

        private static void Decode(string saved, HashSet<string> names)
        {
            foreach (var part in (saved ?? "").Split(new[] { ';' },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                var name = Uri.UnescapeDataString(part);
                if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
            }
        }
    }
}
