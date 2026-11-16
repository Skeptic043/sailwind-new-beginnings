"""Extract the production scenario, roll and open-water types for the scenario checks."""
from pathlib import Path

HERE = Path(__file__).resolve().parent
SRC = HERE.parents[1] / "src"


def block(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def statement(source, signature):
    start = source.index(signature)
    return source[start:source.index(";", start) + 1]


def enum(source, signature):
    start = source.index(signature)
    return source[start:source.index("}", start) + 1]


scenarios = (SRC / "Scenarios.cs").read_text(encoding="utf-8-sig")
catalog = (SRC / "SelectionCatalog.cs").read_text(encoding="utf-8-sig")
sea = (SRC / "AtSeaStart.cs").read_text(encoding="utf-8-sig")
equipment = (SRC / "AdditionalEquipment.cs").read_text(encoding="utf-8-sig")
parts = [
    enum(scenarios, "internal enum ScenarioPlace"),
    block(scenarios, "internal sealed class Scenario\n"),
    block(scenarios, "internal static class Scenarios"),
    enum(catalog, "internal enum BoatSize"),
    enum(catalog, "internal enum PortPool"),
    block(catalog, "internal sealed class SelectionSettings"),
    block(sea, "internal static class OpenWaterPicker"),
    "internal static class AdditionalEquipment {\n" + statement(equipment, "internal const string EmptyRumBarrelKey") + "\n" +
    statement(equipment, "internal const string LargeWaterBottleKey") + "\n" +
    block(equipment, "internal static void NormalizeSelection(") + "\n}",
    block(equipment, "internal sealed class RandomCandidate"),
]
out = HERE / "generated" / "Production.cs"
out.parent.mkdir(exist_ok=True)
out.write_text("// Generated from src by generate.py; do not edit.\nusing System;\nusing System.Collections.Generic;\nusing System.Linq;\nusing UnityEngine;\n\nnamespace NewBeginnings {\n" + "\n\n".join(parts) + "\n}\n", encoding="utf-8")
print("Generated scenario, selection and open-water sources.")
