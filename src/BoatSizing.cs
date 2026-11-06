using System;

namespace NewBeginnings
{
    // Random size class for a detected boat with no known identity. This is a
    // coarse pool filter, not a berth-fit test; placement keeps its own checks.
    internal static class BoatSizing
    {
        // World metres, copied from Ship Shuffle's measured-hull policy so both
        // mods sort an unlisted boat alike (Skeptic043/ship-shuffle
        // src/BoatSizing.cs at 5a9b68d, which owns the thresholds; change both
        // together). Both dimensions must fit a class.
        // A measured Large hull also keeps the existing extra berth distance.
        // Returns null for unusable geometry; the boat then stays manual-only.
        internal static BoatSize? FromHull(float length, float radius)
        {
            var diameter = 2f * radius;
            if (float.IsNaN(length) || float.IsInfinity(length) || length <= 0f ||
                float.IsNaN(diameter) || float.IsInfinity(diameter) || diameter <= 0f)
                return null;
            if (length <= 13f && diameter <= 4.4f) return BoatSize.Small;
            if (length <= 26f && diameter <= 6.8f) return BoatSize.Medium;
            return BoatSize.Large;
        }
    }
}
