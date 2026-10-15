from pathlib import Path
here = Path(__file__).resolve().parent
root = here.parents[1]

def block(path, marker):
    text = (root / path).read_text()
    start = text.index(marker)
    end = text.index('{', start) + 1
    depth = 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]
s = 'using System;using System.Linq;using UnityEngine;\nnamespace NewBeginnings {internal static partial class StartDestinations {'
s += block('src/StartDestinations.cs', 'internal static bool BelongsToIsland(') + '}\ninternal static partial class FixedStart {'
s += block('src/FixedStart.cs', 'private static bool TryResolveRelocation(')
s += 'internal static bool ResolveRelocationForCheck(ResolvedStart s,out string reason)=>TryResolveRelocation(s,out reason);}}'
(here / 'NativeYards.generated.cs').write_text(s)
s = (root / 'src/FixedStart.cs').read_text()
assert 'var recovery = destination.NeedsPlannedBerth ? null : registered?.FirstOrDefault' in s
assert 'Port = port, Recovery = relocation.Recovery, Destination = destination, Berth = relocation.Berth' in s
assert 'Size = size, UseShipyardBerth = relocation.UseShipyardBerth' in s
