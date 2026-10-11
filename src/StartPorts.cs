using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    /// <summary>Optional scene-bound starting-site description. Registration does not guarantee boat fit.</summary>
    public sealed class StartPortOptions
    {
        public string DisplayName { get; set; }
        public Transform BerthMarker { get; set; }
        public Transform ShoreMarker { get; set; }
        public Transform DockRoot { get; set; }
        public Transform SceneryRoot { get; set; }
        public GPButtonDockMooring FrontMooring { get; set; }
        public GPButtonDockMooring BackMooring { get; set; }
        public Shipyard Shipyard { get; set; }
    }

    /// <summary>Register after the native Port and supplied scene objects have initialized, on Unity's main thread.</summary>
    public static class StartPorts
    {
        internal sealed class Registration
        {
            internal string Key;
            internal Port Port;
            internal StartPortOptions Options;
        }
        private static readonly Dictionary<string, Registration> Sites = new Dictionary<string, Registration>(StringComparer.Ordinal);
        internal static int Revision { get; private set; }

        /// <summary>
        /// Accepts a scene-bound description of an actual registered native Port.
        /// Acceptance is not proof that a boat fits: live startup checks still apply.
        /// Identical repeat calls succeed; conflicting identities or dock scopes fail.
        /// Destroying, unloading or moving supplied objects invalidates the description.
        /// Empty options use the port name and island dock scope without creating scene objects.
        /// </summary>
        public static bool Register(string providerId, string siteId, Port port, StartPortOptions options, out string reason)
        {
            reason = null;
            Prune();
            if (string.IsNullOrWhiteSpace(providerId) || string.IsNullOrWhiteSpace(siteId) ||
                providerId.Contains("|") || siteId.Contains("|") || options == null)
            { reason = "A provider identity, stable site identity and options are required."; return false; }
            var island = port != null ? port.GetComponentInParent<IslandHorizon>() : null;
            if (!StartDestinations.ValidPort(port) || island == null)
            { reason = "The supplied native port is not uniquely registered on a loaded island."; return false; }
            if (!Valid(new Registration { Port = port, Options = options }))
            { reason = "Every supplied scene reference must still be loaded on the port's island."; return false; }
            foreach (var marker in new[] { options.BerthMarker, options.ShoreMarker, options.DockRoot, options.SceneryRoot })
                if (marker != null && (!StartDestinations.Loaded(marker) || marker.GetComponentInParent<IslandHorizon>() != island))
                { reason = "Every supplied scene marker/root must belong to the port's island."; return false; }
            if ((options.FrontMooring == null) != (options.BackMooring == null) ||
                options.FrontMooring != null && (options.FrontMooring == options.BackMooring ||
                options.FrontMooring.GetComponentInParent<IslandHorizon>() != island ||
                options.BackMooring.GetComponentInParent<IslandHorizon>() != island))
            { reason = "Supply two distinct real moorings on the port's island, or neither."; return false; }
            if (options.Shipyard != null && (!StartDestinations.Loaded(options.Shipyard) ||
                !options.Shipyard.enabled || options.Shipyard.GetComponentInParent<IslandHorizon>() != island))
            { reason = "The supplied shipyard must be a loaded enabled component on the port's island."; return false; }
            var key = "site:" + providerId.Trim() + "|" + siteId.Trim();
            if (Sites.TryGetValue(key, out var prior))
            {
                if (prior.Port == port && Same(prior.Options, options)) return true;
                reason = "This site identity is already registered with a different declaration.";
                return false;
            }
            if (Sites.Values.Any(item => item.Port == port &&
                ((item.Options.DockRoot != null ? item.Options.DockRoot : island.transform) ==
                    (options.DockRoot != null ? options.DockRoot : island.transform) ||
                 options.BerthMarker != null && item.Options.BerthMarker == options.BerthMarker)))
            { reason = "Another registration already describes this dock scope or berth marker."; return false; }
            // Copy data so callers cannot replace an accepted declaration through mutable options.
            Sites.Add(key, new Registration { Key = key, Port = port, Options = new StartPortOptions {
                DisplayName = options.DisplayName, BerthMarker = options.BerthMarker, ShoreMarker = options.ShoreMarker,
                DockRoot = options.DockRoot, SceneryRoot = options.SceneryRoot, FrontMooring = options.FrontMooring,
                BackMooring = options.BackMooring, Shipyard = options.Shipyard } });
            ++Revision;
            return true;
        }

        private static bool Same(StartPortOptions a, StartPortOptions b) => a.DisplayName == b.DisplayName &&
            a.BerthMarker == b.BerthMarker && a.ShoreMarker == b.ShoreMarker && a.DockRoot == b.DockRoot &&
            a.SceneryRoot == b.SceneryRoot && a.FrontMooring == b.FrontMooring &&
            a.BackMooring == b.BackMooring && a.Shipyard == b.Shipyard;

        internal static Registration[] Snapshot() { Prune(); return Sites.Values.ToArray(); }
        private static bool Alive(Component value) => ReferenceEquals(value, null) || StartDestinations.Loaded(value);
        private static bool Valid(Registration entry)
        {
            if (!StartDestinations.ValidPort(entry.Port)) return false;
            var island = entry.Port.GetComponentInParent<IslandHorizon>();
            if (!StartDestinations.Loaded(island)) return false;
            var o = entry.Options;
            foreach (var component in new Component[] { o.BerthMarker, o.ShoreMarker, o.DockRoot, o.SceneryRoot,
                o.FrontMooring, o.BackMooring, o.Shipyard })
                if (!Alive(component) || component != null && component.GetComponentInParent<IslandHorizon>() != island)
                    return false;
            return o.Shipyard == null || o.Shipyard.enabled;
        }
        private static void Prune()
        {
            foreach (var key in Sites.Where(item => !Valid(item.Value)).Select(item => item.Key).ToArray())
            { Sites.Remove(key); ++Revision; }
        }
    }
}
