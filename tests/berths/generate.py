from pathlib import Path
here = Path(__file__).resolve().parent
root = here.parents[1]
source = (root / 'src/StartBerthPlanner.cs').read_text(encoding='utf-8')

def method(marker):
    start = source.index(marker)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]
core = method('private static bool TryPlanCore(')
fixed = (root / 'src/FixedStart.cs').read_text(encoding='utf-8')
candidate_members = '\n'.join((method(marker) for marker in [
    'internal enum StartBerthMode',
    'private sealed class Survey',
    'private sealed class DockPair',
    'private static StartBerthPlan Make(',
    'private static StartBerthPlan OffshorePlan(',
    'private static bool TryPirateOffshore(',
    'private static bool TryLocalCleatCenter(',
    'private static bool TryFixedOffshoreHold(',
    'private static bool TryFortOuter(',
    'private static bool TrySirenSong(',
    'internal static bool TrySirenSongPose(',
    'internal static bool TryNativeShipyard(',
    'internal static bool TryFortShipyardShore(',
    'internal static void PrepareShipyardMoorings(',
    'private static bool Fail(',
    'private static Vector3 DockLanding(',
    'private static void Note(',
    'private static bool TrySurveyDock(',
    'private sealed class BestDock',
    'private static bool TryDockFallback(',
    'private static bool TryBestEffort(',
    'private static bool TryBestEffortOffshore(',
    'private enum DockRejection',
    'private sealed class DockReport',
    'private sealed class DockCandidate',
    'private sealed class ShoreFailures',
    'private static IEnumerable<DockPair> SupportedDockPairs(',
    'private static IEnumerable<DockCandidate> DockCandidates(',
    'private static IEnumerable<Vector3> DockOffsets('
]))
candidate_members += '\n' + method('private static bool RopePair(').replace('RopePair(', 'NativeRopePair(')
for marker in [
    'private const int MaximumCheckedPairs',
    'private static readonly Vector3 PirateOffshoreCenter',
    'private static readonly Vector3 SaffronOffshoreCenter',
    'private static bool Finite(float',
    'private static bool Finite(Vector3',
    'internal static bool RopeFits(',
    'private static bool PirateRequiresOffshore(',
    'private static string PiratePlanMessage('
]:
    start = source.index(marker)
    candidate_members += '\n' + source[start:source.index(';', start) + 1]
candidate_members = candidate_members.replace('internal static bool TryNativeShipyard(', 'private static bool TryNativeShipyard(').replace('internal static bool TryFortShipyardShore(', 'private static bool TryFortShipyardShore(').replace('internal static void PrepareShipyardMoorings(', 'private static void PrepareShipyardMoorings(')
catalog = (root / 'src/SelectionCatalog.cs').read_text(encoding='utf-8')
route_members = ''
for marker in ['internal bool IsSirenSong =>', 'internal bool NeedsPlannedBerth =>', 'internal int Index =>']:
    start = catalog.index(marker)
    route_members += catalog[start:catalog.index(';', start) + 1] + '\n'
candidate_members += '\nclass SirenRoute { internal RoutePort Port; internal RouteIsland Island; internal object Recovery; internal string SiteIdentity; ' + route_members + '}\n'
assert 'if (destination.IsSirenSong)' in core and core.index('return TrySirenSong(') < core.index('var cleats ='), 'Siren override fell through generic survey'
assert not (root / 'src/SirenSongCog.cs').exists(), 'Obsolete Cog load hook still compiled'
assert 'SirenSongCog' not in fixed, 'New-start Cog mutation still called'
assert 'if (!skipsNativeBerth) NativeBerth.TryResolve(' in fixed, 'Native preservation bypass lost planned-berth routing'
print('PASS exact Siren override routing and removed Cog startup/load path checks (structural only)')
(here / 'PirateCandidates.generated.cs').write_text((here / 'PirateCandidates.template.cs').read_text().replace('// CANDIDATE_PRODUCTION', candidate_members), encoding='utf-8')
