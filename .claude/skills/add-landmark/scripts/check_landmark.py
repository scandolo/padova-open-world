#!/usr/bin/env python3
"""Quality gate for one landmark: evidence, model report, integration and docs.

python3 .claude/skills/add-landmark/scripts/check_landmark.py <slug> [--strict]

Run from the project root after the Blender build (and again after Unity integration).
FAIL = the landmark is not done; WARN = review and justify in the final report.
--strict turns warnings into failures (use before committing).
"""
import ast
import json
import math
import re
import sys
from pathlib import Path

ROOT = Path.cwd()
TRIANGLE_WARN, TRIANGLE_FAIL = 120_000, 200_000
results = []


def report(level, message):
    results.append((level, message))


def check(condition, message, level="FAIL"):
    report("PASS" if condition else level, message)
    return condition


def literal(path, name):
    """Read a top-level list/dict/str assignment without importing (bpy is unavailable here)."""
    for node in ast.parse(path.read_text()).body:
        if isinstance(node, ast.Assign) and any(getattr(t, "id", None) == name for t in node.targets):
            try:
                return ast.literal_eval(node.value)
            except ValueError:
                return ast.unparse(node.value)
    return None


def first_ring(u):
    n = u["ringSizes"][0]
    return [(u["outline"][2 * i], u["outline"][2 * i + 1]) for i in range(n)]


def point_in(ring, x, y):
    inside = False
    for (x0, y0), (x1, y1) in zip(ring, ring[1:] + ring[:1]):
        if (y0 > y) != (y1 > y) and x < (x1 - x0) * (y - y0) / (y1 - y0) + x0:
            inside = not inside
    return inside


def main(slug, strict):
    blender = ROOT / "Tools/blender"
    module = blender / (slug + ".py")
    if not check(module.exists(), f"model module {module.relative_to(ROOT)} exists"):
        return
    name = literal(module, "NAME")
    check(isinstance(name, str) and re.fullmatch(r"[A-Za-z][A-Za-z0-9]*", name or ""), f"NAME is a space-free FBX name ({name})")
    check(literal(module, "VIEWS") is not None, "VIEWS defined for preview renders")
    check(slug in (literal(blender / "build_landmarks.py", "REGISTRY") or []), "registered in build_landmarks.REGISTRY")
    extract = (ROOT / "Tools/landmarks/extract_sources.py").read_text()
    check(f'"{slug}": extract_{slug}' in extract, "extractor registered in extract_sources.EXTRACTORS")
    body = extract[extract.find(f"def extract_{slug}"):]
    body = body[:body.find("\ndef ", 1)] if "\ndef " in body[1:] else body
    check("TODO" not in body, "extractor implemented (no TODO)")

    sources = json.loads((ROOT / "Data/Landmarks/sources.json").read_text())
    check(slug in sources, "evidence frozen in Data/Landmarks/sources.json")
    world = json.loads((ROOT / "Data/World/WorldPlan.json").read_text())
    ids = {u["id"] for u in world["units"]}
    core = json.loads((ROOT / "Assets/Geography/PadovaCentro/CityPlan.json").read_text())
    core_ids = {u["id"] for u in core["units"]}

    # Evidence ledger
    ledger = ROOT / "Data/Landmarks" / (slug + "-evidence.md")
    if check(ledger.exists(), f"evidence ledger {ledger.relative_to(ROOT)} exists"):
        text = ledger.read_text()
        check("<" not in re.sub(r"<!--.*?-->", "", text, flags=re.S).replace("<=", "").replace("< ", ""), "ledger has no template placeholders")
        section = lambda title: (re.search(rf"## {title}.*?\n(.*?)(\n## |\Z)", text, re.S) or [None, ""])[1]
        check(len(re.findall(r"^- \S", section("Signature"), re.M)) >= 3, "ledger lists >= 3 signature features")
        rows = [r for r in section("Dimension").splitlines() if r.startswith("|") and not r.startswith("| ---") and "Element" not in r]
        check(len(rows) >= 5, f"dimension table has >= 5 rows ({len(rows)})")
        tiers = " ".join(rows).lower()
        check("survey" in tiers, "dimension table cites survey values")
        check(all(r.count("|") >= 6 and all(c.strip() for c in r.strip("|").split("|")[:4]) for r in rows), "every dimension row has value, tier and source")
        check(bool(section("Conflicts").strip()), "conflicts section filled (write 'none found' explicitly if so)")

    # Build report
    build = ROOT / "Data/Landmarks" / (slug + "-build.json")
    if not check(build.exists(), "Blender build report exists (run build_landmarks.py)"):
        return finish(strict)
    b = json.loads(build.read_text())
    tris = sum(v["triangles"] for k, v in b["objects"].items() if not k.endswith("_Collision"))
    check(tris <= TRIANGLE_FAIL, f"visual triangles {tris} <= {TRIANGLE_FAIL}")
    if tris > TRIANGLE_WARN:
        report("WARN", f"visual triangles {tris} above the {TRIANGLE_WARN} budget: justify or simplify")
    col = next((v for k, v in b["objects"].items() if k.endswith("_Collision")), None)
    check(col and 0 < col["triangles"] <= 20_000, "simple _Collision mesh present (<= 20k triangles)")
    ax, _, az = b["anchor"]
    ex = world["extent"]
    check(ex[0] <= ax <= ex[2] and ex[1] <= az <= ex[3], f"anchor ({ax:.0f}, {az:.0f}) inside WorldPlan extent")
    rep = b.get("replaces", {})
    units = rep.get("units", [])
    check(all(u in ids for u in units), "replaced units exist in WorldPlan")
    check(not any(u in core_ids for u in units), "no replaced unit belongs to the core CityPlan (needs LandmarkBuilder instead)")
    patch_ids = {p["id"] for p in world["patches"]}
    check(all(p in patch_ids for p in rep.get("patches", [])), "replaced patches exist in WorldPlan")
    check(len(rep.get("ownGround", [])) % 4 == 0, "ownGround is a flat list of x0,z0,x1,z1 rectangles")
    if rep.get("ownGround"):
        check(any(k.endswith("_Ground") for k in b["objects"]), "landmark owning ground provides a _Ground mesh")
    places = b.get("places", [])
    check(bool(places), "discovery place declared")
    footprints = [u for u in world["units"] if u["id"] in set(units)]
    for p in places:
        check(math.hypot(p["x"] - ax, p["z"] - az) < 250, f"place '{p['name']}' within 250 m of the anchor")
        check("<" not in p["name"] + p.get("source", ""), f"place '{p['name']}' has a real name and source")
        inside = any(point_in(first_ring(u), p["x"], p["z"]) for u in footprints)
        check(not inside, f"place '{p['name']}' is on open ground, not inside a surveyed footprint")
    others = [json.loads(f.read_text()) for f in (ROOT / "Data/Landmarks").glob("*-build.json") if f != build]
    claimed = {u for o in others for u in o.get("replaces", {}).get("units", [])}
    check(not claimed & set(units), "no unit claimed by another landmark")
    renders = list((ROOT / ".context/landmarks-renders").glob(slug + "-*.png"))
    if len(renders) < 2:
        report("WARN", "fewer than 2 Blender preview renders in .context/landmarks-renders (run with --render and look at them)")

    # Unity integration
    fbx = ROOT / "Assets/Art/Landmarks" / (b["name"] + ".fbx")
    check(fbx.exists() and Path(str(fbx) + ".meta").exists(), "FBX exported and imported by Unity (.meta present)")
    manifest = json.loads((ROOT / "Assets/Art/Landmarks/Landmarks.json").read_text())
    check(any(m["name"] == b["name"] for m in manifest["models"]), "model listed in Landmarks.json")
    scene = (ROOT / "Assets/Scenes/PadovaPlayable.unity").read_text(errors="ignore")
    check(b["name"] in scene, "model instance saved in PadovaPlayable.unity (run BuildLandmarks.Build)")
    tests = (ROOT / "Assets/Tests/EditMode/LandmarkModelTests.cs").read_text()
    if b["name"] not in tests:
        report("WARN", "no landmark-specific EditMode test mentions " + b["name"])
    views = (ROOT / "AgentScripts/CaptureViews.cs").read_text()
    if slug.split("_")[0] not in views.lower() and b["name"].lower() not in views.lower():
        report("WARN", "no CaptureViews entry for this landmark")

    # Documentation
    readme = (ROOT / "Data/Landmarks/README.md").read_text()
    display = places[0]["name"] if places else b["name"]
    check(display.split()[-1].lower() in readme.lower(), "Data/Landmarks/README.md has a section for the landmark")
    check(display.split()[-1].lower() in (ROOT / "Data/World/landmarks.md").read_text().lower(), "Data/World/landmarks.md row added")
    if display.split()[-1].lower() not in (ROOT / "docs/THIRD_PARTY_ASSETS.md").read_text().lower():
        report("WARN", "docs/THIRD_PARTY_ASSETS.md does not mention the landmark's reference sources")
    finish(strict)


def finish(strict):
    for level, message in results:
        print(f"{level:4}  {message}")
    fails = sum(l == "FAIL" for l, _ in results) + (sum(l == "WARN" for l, _ in results) if strict else 0)
    print(f"\n{sum(l == 'PASS' for l, _ in results)} pass, {sum(l == 'WARN' for l, _ in results)} warn, "
          f"{sum(l == 'FAIL' for l, _ in results)} fail")
    sys.exit(1 if fails else 0)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    main(sys.argv[1], "--strict" in sys.argv)
    finish("--strict" in sys.argv)
