# City engine: the whole comune from data

Every building, street, canal, garden wall and tree outside the hand-modelled core comes from one pipeline. Nothing in it is written for a particular place: a basilica, a warehouse and a garden shed take the same path and differ only by the attributes the survey gives them. Adding a district, or another city with the same kind of survey, means running the compiler, not modelling.

```text
DBT 2007 shapefiles ─┐
OSM tags (names/uses)┼─> Tools/city/compile_city.py ─> Assets/StreamingAssets/City/*.bin ─> CityStreamer ─> meshes
Data/City/zones.json ─┘        (offline, ~6 min)             (256 m tiles, 67 MB)            (worker threads)
```

## What is evidence and what is typology

| Element | Source | Status |
| --- | --- | --- |
| Footprints, courtyards, eave height, base, portico clearance of 100,134 volume units | DBT `UN_VOL`, `MN_UVOL` | Survey, float32, outline simplified by at most 5 cm |
| Which volumes form one building; building type and use | DBT `EDIFC` (`EDIFC_TY`, `EDIFC_USO`) | Survey |
| Names and more specific uses | OpenStreetMap tags joined by position | Crowd-sourced (ODbL) |
| Carriageways, sidewalks, cycle paths, tram and rail areas, squares, parking, water, green areas by type, sports grounds, fields | DBT area layers, resolved by priority into one surface per point | Survey plan; levels are typology (sidewalk +13 cm, water −1.7 m) |
| Garden and boundary walls with their heights | DBT `EL_DIV` | Survey line and height; material is typology |
| Tree rows | DBT `FIL_AL` | Surveyed line; spacing along it is typology |
| Trees inside gardens, tree areas and woods | scatter inside the surveyed polygon | Typology |
| Terrain | 64 m grid of the median surveyed base elevation, blended into the core's fitted plane | Interpolation, not a scan |
| Building class (townhouse, palazzo, condo, house, shed, church, tower, civic…) | rules over `EDIFC`, OSM tags, height and the hand-drawn historic-centre zone | Typology key |
| Roof form and pitch, gable or hip ends, flat roofs, domes over round church volumes, spires on bell towers | rules per class over the surveyed outline | Typology |
| Windows, shutters, cornices, shopfronts, balconies, colours | `CityStyle` per class, seeded per building | Typology, never per-building fact |

The compiler never moves a surveyed vertex or height. Hero assets keep priority: units listed in the core `CityPlan.json`, the Palazzo della Ragione set and `Assets/Art/Landmarks/Landmarks.json` are left out of the packs, and the ground inside the core extent and each landmark's own rectangle is cut out, so a hand-built model is an optional upgrade on top of the automatic city.

## Compile

```sh
python3 -m venv .context/city/venv && .context/city/venv/bin/pip install -r requirements.txt
python3 Tools/city/fetch_osm.py points areas buildings      # optional: names and uses
.context/city/venv/bin/python Tools/city/compile_city.py --jobs 6            # whole comune, about 6 minutes
.context/city/venv/bin/python Tools/city/compile_city.py --radius 700        # quick test area
```

Inputs are the municipal archives (`.context/geodata/padova-dbt` areas, `.context/city/dbt` lines and points; see [REAL_WORLD_DATA.md](REAL_WORLD_DATA.md) for the download) and the CGAL roof helper in `.context/bin`. The game itself needs none of this: the packs are versioned.

Class rules, roof rules and ground priorities are the tables at the top of `compile_city.py` (`OSM_CLASS`, `classify`, `refine_units`, `SLOPE`, `GABLE_SHARE`, `GROUND_LAYERS`, `LEVEL`). Change a rule, recompile, and the whole city follows.

## Pack format (version 1, little-endian)

`city.json` holds the tile size, extent, class and ground-kind tables, levels and attribution. `terrain.bin` is `"PCG1"`, x0, z0, step, pad (float32), nx, nz (uint32), then nz × nx float32. Each `r_<x>_<z>.bin` is a 1024 m region: `"PCR1"`, uint16 tiles per side (4), sixteen (uint32 offset, uint32 length) entries ordered by z then x, then the tiles.

A tile is `"PCT1"`, int16 tx, int16 tz, then four sections:

1. **Units**: uint16 count; per unit uint32 id (bit 30 set for `MN_UVOL` rows), uint8 part, class, flags (1 suspended, 2 minor, 4 simple, 8 has cap), roof (0 roof record, 1 flat, 2 dome, 3 spire), uint32 building (`EDIFC` row or 0xFFFFFFFF), float32 ground, wall top, surveyed eave, surveyed base, portico underside; uint8 ring count, uint16 ring sizes, float32 x,z pairs. Then either one flag byte per outline edge (simple: every edge is a free-standing street wall) or uint16 count of exposed wall pieces, each x0,z0,x1,z1,y0,y1 (float32), kind, flags (uint8; see `SegmentKind`, `SegmentFlags`). Then, if flagged, the triangulated footprint (uint16 count, uint16 indices).
2. **Roofs**: uint16 count; per roof uint8 class, uint32 building, float32 eave, slope, uint16 node and face counts; nodes x,z,t (roof height = eave + t × slope); faces uint8 n, uint8 flags (1 exposed eave, 2 vertical gable wall), uint16 node indices with the footprint edge first.
3. **Ground**: uint16 count; per patch uint8 kind, uint16 vertices, uint32 indices, float32 x,z pairs, uint16 triangles. Then uint32 n and n float32 for curbs and banks (x0,z0,x1,z1,low,high above terrain).
4. **Props**: uint16 trees (float32 x,z, uint8 height × 10, uint8 kind); uint16 walls (uint8 kind, float32 height, uint16 points, float32 x,z pairs).

`Assets/Scripts/Architecture/City/CityPack.cs` is the reader.

## Runtime

`CityStreamer` (created by `WorldContext`) keeps tiles loaded around the camera:

- **Distant version, all loaded tiles**: three meshes drawn with the single `Padova/City` material (`Assets/Shaders/City.shader`): ground, roofs with garden walls and trees, and walls. Vertex colour tints a texture-array slice; a façade grid in the second UV channel lets the shader draw windows, shutters and (with the global `_PadovaNight`) lit rooms. A tile costs three draw calls.
- **Modelled façades, near tiles**: within `DetailRadius` the walls are rebuilt by the same `FacadeBuilder` as the core district, styled by `CityStyle.For(seed, class)`. The window grid of the distant version uses the same bays and storeys, so the swap does not move openings.
- **Colliders** exist within `ColliderRadius`; meshes are cooked on a worker thread.
- Tile parsing and meshing run on worker threads (`TileMesher` is pure functions over tile data); the main thread applies at most two finished jobs per frame.

`WorldContext.Data` remains the gameplay view: `GroundAt`, road centrelines and named places. In the Editor, `WorldContext.RefreshVisibility(eye)` loads synchronously around a viewpoint (used by tests and `AgentScripts/Shoot.cs`).

## Limits

- Survey date is 2007. Class rules are heuristics: a slender modern volume is treated as a lift core, a round church volume as a dome, and so on; they will be wrong for individual buildings.
- Roads are flat on the terrain: flyovers and underpasses are not raised or sunk, and bridges are decks at street level over the sunken water.
- The core district and the Blender landmarks keep their own, older pipelines.
- The packs are about 67 MB; every recompile rewrites all region files.
