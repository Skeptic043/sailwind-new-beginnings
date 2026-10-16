using UnityEngine;

namespace NewBeginnings
{
    internal static class DockSurfaces
    {
        internal static Vector3? Edge(Vector3 point)
        {
            if (point.x < -2.5f || point.x > 2.5f || point.z < 0 || point.z > 4)
                return null;
            return new Vector3(point.x, 0, point.z);
        }

        internal static Vector3? WithRoof(Vector3 point)
        {
            if (point.x < -3 || point.x > 3 || point.z < -3 || point.z > 3)
                return null;
            if (point.x > 1 && point.z > 1)
                return null;
            return new Vector3(point.x, 0, point.z);
        }
    }

    internal static partial class StarterCargo
    {
        static void RunPartialSupportChecks()
        {
            Reset();
            Plugin.Instance.Surface = DockSurfaces.Edge;
            var cheese = Crate(.709837139f, 1.269517899f, 1.028908968f);
            cheese.Planned = Quaternion.Euler(0, 75.478f, 0);
            AddItem(cheese);
            Check(StartArrival.TryProbeCargoSurface(Vector3.zero, out var center, out _) && !HasSupportFootprint(cheese, center), "dock-edge fixture must support the center without supporting the whole crate");
            Obstacle(new(0, 1, 0), new(30, 2, 30));
            Check(TryChooseGround(Vector3.zero, Plugin.Instance.Ground, cheese, null, out var point, out _) && !HasCargoClearance(cheese, point, PlacementRotation(cheese), Plugin.Instance.Ground), "clearance preference withheld a center-supported crate");
            Check(TryChooseLooseGround(cheese, Vector3.zero, Vector3.zero, new(), out point, out _) && DockSurfaces.Edge(point).HasValue, "loose crate cannot use the center-supported dock edge");
            Physics.obstacles.Clear();
            Obstacle(new(0, 1, 0), new(30, 2, 30), true);
            Check(!TryChooseGround(Vector3.zero, Plugin.Instance.Ground, cheese, null, out _, out _) && !TryChooseLooseGround(cheese, Vector3.zero, Vector3.zero, new(), out _, out _), "permissive scene clearance also admitted a boat embark volume");
        }
    }
}
