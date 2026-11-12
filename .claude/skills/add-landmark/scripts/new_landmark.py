#!/usr/bin/env python3
"""Scaffold a landmark: python3 .claude/skills/add-landmark/scripts/new_landmark.py <slug> <FbxName> "<Display name>"
Creates Tools/blender/<slug>.py and Data/Landmarks/<slug>-evidence.md, registers the slug in
Tools/blender/build_landmarks.py (REGISTRY) and adds an extractor stub to Tools/landmarks/extract_sources.py."""
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent.parent
ROOT = Path.cwd()
slug, fbx, display = sys.argv[1], sys.argv[2], sys.argv[3]
assert re.fullmatch(r"[a-z][a-z0-9_]*", slug), "slug must be a python identifier"
module = ROOT / "Tools/blender" / (slug + ".py")
assert not module.exists(), module
module.write_text((HERE / "assets/landmark_template.py").read_text().replace("<FbxName>", fbx).replace("<Display name>", display).replace("<slug>", slug))
ledger = ROOT / "Data/Landmarks" / (slug + "-evidence.md")
ledger.write_text((HERE / "assets/evidence-ledger.md").read_text().replace("<Landmark>", display).replace("<slug>", slug))
build = ROOT / "Tools/blender/build_landmarks.py"
text = build.read_text()
text = re.sub(r"REGISTRY = \[(.*?)\]", lambda m: 'REGISTRY = [%s, "%s"]' % (m.group(1), slug), text, count=1)
build.write_text(text)
extract = ROOT / "Tools/landmarks/extract_sources.py"
text = extract.read_text()
stub = f'''def extract_{slug}(world):
    """TODO: survey evidence for {display}. Return (evidence dict, DBT layers read)."""
    return {{"units": world_units(world, {{}})}}, ["UN_VOL"]


'''
text = text.replace("# Register new landmarks here", stub + "# Register new landmarks here", 1)
text = re.sub(r"EXTRACTORS = \{(.*?)\}", lambda m: 'EXTRACTORS = {%s, "%s": extract_%s}' % (m.group(1), slug, slug), text, count=1)
extract.write_text(text)
print("created", module.relative_to(ROOT), ledger.relative_to(ROOT), "; registered", slug)
print("""Next:
 1. Fill Data/Landmarks/%(s)s-evidence.md (Phase 1-2) before modelling.
 2. Implement extract_%(s)s in Tools/landmarks/extract_sources.py (roles per unit), run it with .context/geo-venv/bin/python.
 3. Model in Tools/blender/%(s)s.py; build with --render and compare renders to the reference photos.
 4. Unity: BuildLandmarks.Build, CaptureViews entries, a %(n)s test in LandmarkModelTests.cs, run_tests, verify_world.py.
 5. Docs, then: python3 .claude/skills/add-landmark/scripts/check_landmark.py %(s)s --strict""" % {"s": slug, "n": fbx})
