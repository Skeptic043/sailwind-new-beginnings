from pathlib import Path

HERE = Path(__file__).resolve().parent
source = (HERE.parents[1] / "src" / "StarterCargo.cs").read_text(encoding="utf-8-sig")


def block(signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 1
    end = opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def expression(signature):
    start = source.index(signature)
    return source[start:source.index(";", start) + 1]


patches = "\n".join(block(name) for name in (
    "private static class ReleaseEquipmentCrateOnOpenPatch",
    "private static class ReleaseStarterCupOnPickupPatch",
    "private static void ReleaseForCupPickup()",
))
patches += "\n" + expression("private static bool IsNativeCup(ShipItem item)")
roster = expression("var survivors = CollectItems(__instance.transform)")
template = (HERE / "Handoff.template.cs").read_text(encoding="utf-8")
output = HERE / "generated" / "Handoff.cs"
output.parent.mkdir(exist_ok=True)
output.write_text(template.replace("/* CURRENT_PATCHES */", patches).replace("/* CURRENT_ROSTER */", roster), encoding="utf-8")
print("Generated current StarterCargo handoff patches and surviving roster.")
