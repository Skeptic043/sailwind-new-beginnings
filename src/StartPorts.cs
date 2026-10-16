using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace NewBeginnings
{
    /// <summary>Optional author details for an automatically discovered native Port.</summary>
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

    /// <summary>Enrich a native Port after it and any supplied scene objects have initialized, on Unity's main thread.</summary>
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
        /// Accepts author details for an actual registered native Port.
        /// Acceptance is not proof that a boat fits: live startup checks still apply.
        /// One native Port may have one declaration. Identical repeat calls succeed;
        /// conflicting site identities or declarations fail.
        /// Destroying, unloading or moving supplied objects invalidates the description.
        /// Omitted options leave automatically discovered values unchanged.
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
            var accepted = Copy(options);
            if (!Valid(new Registration { Port = port, Options = accepted }))
            { reason = "Every supplied scene reference must still be loaded on the port's island."; return false; }
            foreach (var marker in new[] { accepted.BerthMarker, accepted.ShoreMarker, accepted.DockRoot, accepted.SceneryRoot })
                if (marker != null && (!StartDestinations.Loaded(marker) || marker.GetComponentInParent<IslandHorizon>() != island))
                { reason = "Every supplied scene marker/root must belong to the port's island."; return false; }
            if ((accepted.FrontMooring == null) != (accepted.BackMooring == null) ||
                accepted.FrontMooring != null && (accepted.FrontMooring == accepted.BackMooring ||
                accepted.FrontMooring.GetComponentInParent<IslandHorizon>() != island ||
                accepted.BackMooring.GetComponentInParent<IslandHorizon>() != island))
            { reason = "Supply two distinct real moorings on the port's island, or neither."; return false; }
            if (accepted.Shipyard != null && (!StartDestinations.Loaded(accepted.Shipyard) ||
                !accepted.Shipyard.enabled || accepted.Shipyard.GetComponentInParent<IslandHorizon>() != island))
            { reason = "The supplied shipyard must be a loaded enabled component on the port's island."; return false; }
            var key = "site:" + providerId.Trim() + "|" + siteId.Trim();
            if (Sites.TryGetValue(key, out var prior))
            {
                if (ReferenceEquals(prior.Port, port) && Same(prior.Options, accepted)) return true;
                reason = "This site identity is already registered with a different declaration.";
                return false;
            }
            if (Sites.Values.Any(item => ReferenceEquals(item.Port, port)))
            { reason = "This native port already has a different starting-location declaration."; return false; }
            // Copy data so callers cannot replace an accepted declaration through mutable options.
            Sites.Add(key, new Registration { Key = key, Port = port, Options = accepted });
            ++Revision;
            return true;
        }

        private static StartPortOptions Copy(StartPortOptions options) => new StartPortOptions {
            DisplayName = string.IsNullOrWhiteSpace(options.DisplayName) ? null : options.DisplayName.Trim(),
            BerthMarker = options.BerthMarker, ShoreMarker = options.ShoreMarker,
            DockRoot = options.DockRoot, SceneryRoot = options.SceneryRoot, FrontMooring = options.FrontMooring,
            BackMooring = options.BackMooring, Shipyard = options.Shipyard };

        private static bool Same(StartPortOptions a, StartPortOptions b) =>
            string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal) &&
            ReferenceEquals(a.BerthMarker, b.BerthMarker) && ReferenceEquals(a.ShoreMarker, b.ShoreMarker) &&
            ReferenceEquals(a.DockRoot, b.DockRoot) && ReferenceEquals(a.SceneryRoot, b.SceneryRoot) &&
            ReferenceEquals(a.FrontMooring, b.FrontMooring) && ReferenceEquals(a.BackMooring, b.BackMooring) &&
            ReferenceEquals(a.Shipyard, b.Shipyard);

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
