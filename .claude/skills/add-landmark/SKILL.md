---
name: add-landmark
description: End-to-end pipeline for turning a real Italian landmark (church, basilica, tower, palazzo, piazza, bridge, monument, fountain, square with statues…) into a survey-faithful procedural Blender model in the Padova Open World graphic style and integrating it into the Unity game with tests and native verification. Use this whenever the user asks to model, rebuild, add, "fare in Blender", "modellare", "aggiungere al gioco" or improve any named Italian monument or public space — e.g. il Santo, Prato della Valle, Santa Giustina, Cappella degli Scrovegni, Arena di Verona, Torre di Pisa — even if they only say "can you make X as an asset" or name the place without mentioning Blender or Unity.
---

# Italian landmark → Blender → game

This skill captures the process that produced the Basilica del Santo and Prato della Valle models (`Tools/blender/santo.py`, `prato.py`). Follow it for any landmark. The user cares about three things simultaneously, and the process exists to protect all three:

1. **Truth.** Real positions, footprints and heights. Surveyed values are never bent to look nicer; every number has a source; everything not measured is labelled *typology*.
2. **Looks.** Good-looking, recognisable architecture in the game's existing style (its own PBR materials, metric tiling, modelled ornament). Never project photographs onto geometry.
3. **It works in the game.** Placed, colliding, walkable, discoverable, tested in the Editor and in a native Player, within the M4 Air performance budget.

Read `AGENTS.md`, `docs/ARCHITECTURE.md`, `docs/REAL_WORLD_DATA.md` and `Data/Landmarks/README.md` first; they hold project rules this skill assumes.

## Phase 0 — Isolation and scope (do not skip)

- Other agents often work in the same Conductor workspace. Run `git status` and look for fresh untracked files (e.g. `find . -newer AGENTS.md -mmin -120 -not -path './Library/*'`). If someone else is touching landmarks, Unity or Blender, stop and ask; if the user says continue, work in your own worktree (`git worktree add ../<name> -b <branch>`, `cp -cR Library ../<name>/Library`, symlink read-only `.context/geo-venv`, `.context/geodata`, `.context/polyhaven`) and open a separate Unity Editor with `--project-path`.
- Run Blender **headless** (`/Applications/Blender.app/Contents/MacOS/Blender -b --factory-startup --python …`), never through the shared Blender MCP session: separate processes cannot collide with other agents.
- Locate the landmark in game coordinates (see `references/evidence.md` → "Locating"). If it lies **outside** `Data/World/WorldPlan.json` `extent` (currently 3 × 2.6 km around Palazzo della Ragione), integration needs new geodata and world expansion first. Explain that to the user and agree the scope before modelling.
- Split the landmark into components (e.g. body, drums, domes, towers, façade, monument; canal, bridges, statues, fountain) — this list drives evidence gathering.

## Phase 1 — Evidence ledger (before any geometry)

Gather evidence in tiers and write it down in `Data/Landmarks/<slug>-evidence.md` (template: `assets/evidence-ledger.md`; worked examples: `Data/Landmarks/santo-evidence.md` for a building, `prato-evidence.md` for a public space with many small elements). Full source list and extraction recipes: `references/evidence.md`.

| Tier | Source | Use for |
| --- | --- | --- |
| 1 Survey | Municipal/regional DBT (UN_VOL, MN_*, PONTE, AB_CDA …), already in WorldPlan or the shapefiles | footprints, courtyards/holes, eaves (QGR), bases, clearances, water levels, bridge decks |
| 2 Mapped | OpenStreetMap (ODbL): artwork nodes, building:part, height, ref numbers | positions/identities of small elements (statues, obelisks), counts |
| 3 Published | Official sites, Wikipedia it, heritage catalogues | totals above the eave (spire tips, dome heights), counts, named dimensions |
| 4 Reference photos | Wikimedia Commons etc., **drawing reference only** | proportions, rhythm, ornament vocabulary, materials |

Rules that make the result trustworthy:
- Survey wins for anything it measures. Published totals govern only what the survey lacks (e.g. above-eave spires). Photo ratios only set relative proportions *between* anchored numbers.
- When tiers disagree (e.g. DBT eave vs. published façade height, a monument's survey height vs. published dimensions) keep the survey value, choose the least-bad model, and record the conflict in the ledger and final report. Visible conflicts are fine; hidden ones are not.
- Count things (domes, arches, statues, bays) from two sources when possible; counts are what viewers notice.
- Look at the photos yourself (download thumbnails to `.context/landmarks-ref/`, then Read them). Note the recognisable features in a short "signature list" — those must appear in the model.

Freeze the evidence: add an extractor to `Tools/landmarks/extract_sources.py` (`EXTRACTORS[slug]`), run it with the geo venv, commit `Data/Landmarks/sources.json`. Blender reads only that file, so a fresh clone can rebuild without downloads.

## Phase 2 — Proportion plan

Before coding, write a dimension table in the ledger: every element with value, tier, source, and derivation (e.g. "cone height 14 m = 0.86 × drum Ø from photo `santo3.jpg`, drum Ø from DBT"). Sanity-check sums (drum eave + cone + lantern + spire ≈ published total?) and cross-check a second photo. This is where most proportion errors are caught cheaply.

## Phase 3 — Blender model

Scaffold with `python3 .claude/skills/add-landmark/scripts/new_landmark.py <slug> <FbxName> "<Display name>"`. It creates `Tools/blender/<slug>.py` from `assets/landmark_template.py` (role dispatch onto the shared Santo vocabulary: drums/domes, campanili, turrets, plain surveyed volumes with cornices, corbel tables and windows), creates the evidence ledger, registers the slug in `REGISTRY` and adds an `extract_<slug>` stub to `EXTRACTORS`. Do the ledger (Phases 1–2) before filling in the model. Then implement `build(src, ground)` using `Tools/blender/lmcommon.py`. Conventions, helper catalogue and pitfalls: `references/blender-modelling.md`. The essentials:

- Build every surveyed volume on its exact outline, from survey base to survey eave. Put above-eave forms and ornament on top, anchored to those numbers.
- Material names must equal existing Unity materials in `Assets/Art/Padova/Materials` (Brick, Lead sheet, Istrian stone, Trachyte masonry, Coppi roof, Plaster 0–9 …). New ones go in `lmcommon.EXTRA` *and* `AgentScripts/BuildLandmarks.cs` `ExtraMaterials()`.
- Object naming drives Unity behaviour: `<Name>_Collision` (hidden MeshCollider, layer 9), `<Name>_Ground` (walkable, layer 8), `<Name>_Water` (no collider). Always provide a simple collision mesh from the survey volumes.
- Return `replaces` (DBT unit ids / patch ids / ground-cell rects the model now draws) and a discovery `places` entry on open, walkable ground near the landmark.
- Budget: aim ≤ ~120k triangles per landmark; reuse lathe/instanced detail instead of dense meshes.

Build and render previews: `Blender -b --factory-startup --python Tools/blender/build_landmarks.py -- <slug> --render`. Look at every render next to the reference photos and fix what is wrong before touching Unity (checklist in `references/blender-modelling.md` → "Visual QA").

## Phase 4 — Unity integration and verification

Details: `references/unity-integration.md`. Sequence:

1. `unity command run_script --file AgentScripts/BuildLandmarks.cs --entry BuildLandmarks.Build` (imports FBX, remaps materials, places at anchor with +90° X rotation, applies `Landmarks.json` exclusions, saves the scene). Check its report for `missing` materials.
2. Add views to `AgentScripts/CaptureViews.cs` (street level and aerial) and capture them; *look* at close-ups too — texture/UV faults only show near the camera.
3. Add landmark-specific assertions to `Assets/Tests/EditMode/LandmarkModelTests.cs` (collision bounds equal surveyed extents/eaves within 2 cm; published totals within 0.1 m; walkability probes). The generic manifest test already covers placement, hidden collision, mapped materials and discovery ground.
4. Recompile, `unity command run_tests --mode editor` (all tests must pass, count must be > 0).
5. Add a walking case to `Tools/verify_world.py` if the landmark has special gameplay (bridges, stairs, water). Run `python3 Tools/verify_world.py --editor --case landmarks,<case>` while iterating, then the gate `python3 Tools/verify_world.py --build --measure` and record FPS/GPU ms/memory.

## Phase 5 — Documentation, quality gate and report

- Add a section to `Data/Landmarks/README.md` (surveyed / published / typology / known conflicts / placement rules), a row to `Data/World/landmarks.md`, attributions to `docs/THIRD_PARTY_ASSETS.md`.
- Run the gate: `python3 .claude/skills/add-landmark/scripts/check_landmark.py <slug> --strict`. It verifies registration, frozen evidence, a complete ledger (signature list, ≥ 5 sourced dimension rows, conflicts), triangle and collision budgets, replaced records that really exist (and are not core units or claimed twice), a named discovery point on open ground, FBX import, scene placement, manifest, tests, capture views and docs. Fix every FAIL; justify any WARN in the report.
- Final message to the user: screenshots (Blender + Unity + native), what is survey vs typology, every conflict, test and native results with numbers, file locations, and what was *not* done. Do not commit or open a PR unless asked.

## Definition of done

The landmark is done when all of these hold, with evidence in the final message:
1. Every signature feature is visible in Blender renders and in Unity captures (street level and aerial), and was compared against the reference photos.
2. `check_landmark.py <slug> --strict` passes.
3. `unity command run_tests --mode editor`: all pass (count > 0), including the landmark's own test.
4. `python3 Tools/verify_world.py --build --measure`: all cases pass (rerun focus-lost cases without `--build`), 60 FPS target reported with GPU ms and memory.
5. Ledger, README section, landmarks row and attributions are written; every conflict is stated plainly.

## Why these rules exist (lessons from the Santo/Prato build)

- Absolute metric UVs (≈700 m) rendered brick as a single white texel on Metal; `lmcommon` now shifts UVs per face — keep that.
- Two coincident opposite faces (balustrades, cloaks, wings) z-fight; offset them a few centimetres.
- Façade details placed at the wrong offset vanish inside surveyed walls; compute offsets from the actual outline of the unit you decorate.
- Interpolation loops over numbered items must wrap within their ring/series, or they never terminate.
- The FBX arrives in Unity as (east, north, −up); `BuildLandmarks` rotates +90° about X. Don't "fix" it twice.
- Survey eaves can make a landmark look less like photos (flat roof around domes, low gable). That is acceptable; say so instead of raising walls.
