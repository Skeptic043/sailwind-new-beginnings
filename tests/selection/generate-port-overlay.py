from pathlib import Path

here = Path(__file__).resolve().parent
root = here.parents[1]


def read(rel):
    return (root / rel).read_text(encoding='utf-8')


def block(text, marker):
    start = text.index(marker)
    brace = text.index('{', start)
    depth, end = 1, brace + 1
    while depth:
        depth += (text[end] == '{') - (text[end] == '}')
        end += 1
    return text[start:end]


def statement(text, marker):
    start = text.index(marker)
    return text[start:text.index(';', start) + 1]


catalog = read('src/SelectionCatalog.cs')
destinations = read('src/StartDestinations.cs')
generated = '''// Generated from production source. Do not edit.
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
namespace NewBeginnings {
''' + block(catalog, 'internal enum PortPool') + '\n' + block(catalog, 'internal sealed class PortChoice') + '''
internal static partial class StartDestinations {
''' + '\n'.join([
    statement(destinations, 'internal static bool Loaded('),
    statement(destinations, 'internal static bool ValidPort(Port port) =>'),
    statement(destinations, 'internal static bool ValidPort(Port port, Port[] scenePorts) =>'),
    block(destinations, 'internal static bool BelongsToIsland('),
    block(destinations, 'internal static void Complete('),
]) + '\n} }\n'

(here / 'PortOverlay.generated.cs').write_text(generated, encoding='utf-8')
print('Generated current StartPorts destination-overlay boundary')
