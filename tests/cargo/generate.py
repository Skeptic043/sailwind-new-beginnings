from pathlib import Path
here = Path(__file__).resolve().parent
source = (here.parents[1] / 'src/StarterCargo.cs').read_text(encoding='utf-8')

def method(marker):
    start = source.index(marker)
    end = source.index('{', start) + 1
    depth = 1
    if '=>' in source[start:end]:
        return source[start:source.index(';', start) + 1]
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]
markers = [
    'private static Vector3 OarBaseGround(',
    'private static bool HasOarBaseSupport(',
    'private static Quaternion PlacementRotation(',
    'private sealed class CargoMove',
    'private sealed class CrateColumn',
    'private sealed class CargoShape',
    'private static bool TryCargoBounds(',
    'private static bool TryStackBounds(',
    'private static float StackBaseArea(',
    'private static CrateColumn NewCrateColumn(',
    'private static bool TryStackCrate(',
    'private static bool OverlapsEmbarkVolume(',
    'private static Vector3 BestEffortGround(',
    'private static Bounds PlanningBounds(',
    'private static void ResetPackedDistance(',
    'private static bool TryPlaceAll(',
    'private static bool PlanAndPlaceAll(',
    'private static bool TryChooseGround(',
    'private static bool TryChooseLooseGround(',
    'private static Vector3[] GroundDirections(',
    'private static IEnumerable<Vector3> CompactGroundSamples(',
    'private static bool NearerToFood(',
    'private static bool HasSupportFootprint(',
    'private static IEnumerator PlaceCapturedCore(',
    'private sealed class GroundSlot',
    'private static Vector3 PutBottomOnSurface(',
    'private static float FootprintRadius(',
    'private static bool IsSlotClear(',
    'private static bool HasCargoClearance(',
    'private static Bounds CargoClearanceBounds(',
    'private static bool IsInsideEmbarkVolume('
]
(here / 'Production.generated.cs').write_text('using System;using System.Collections;using System.Collections.Generic;using System.Linq;using UnityEngine;namespace NewBeginnings { internal static partial class StarterCargo {\n' + '\n'.join((method(x) for x in markers)) + '\n' + '\n'.join((source[source.index(x):source.index(';', source.index(x)) + 1] for x in ['private const int MaxColumnItems', 'private const float MaxColumnHeight'])) + '\n}}', encoding='utf-8')
print('Extracted actual cargo floor, footprint, spacing, solid and embark query methods')
