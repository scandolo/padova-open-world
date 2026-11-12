# Central Padova: architectural reconstruction

The playable scene now shows modelled 3D architecture instead of GIS blocks or photo billboards. Every building stands on its surveyed outline, at its surveyed eave height, with its surveyed porticoes. Architectural detail is modelled geometry: window reveals, shutters, cornices, arcades, roofs and the Palazzo della Ragione. No photograph is projected onto a façade.

## What is evidence and what is typology

| Element | Source | Status |
| --- | --- | --- |
| Footprints, courtyards | DBT 2007 `UN_VOL` outlines | Survey |
| Eave heights | `UN_VOL_QGR`; shared roofs may raise an eave by at most 0.35 m, the survey precision (tested) | Survey |
| Porticoes | `UN_VOL_POR` 02/03 "suspended" units, clearance from `UN_VOL_INH` (2.9–8 m, median 4.5 m) | Survey |
| Which walls face a street, courtyard, portico or neighbour | Computed from adjacent units' vertical extents | Derived from survey |
| Street, pedestrian and sidewalk areas, curbs | `AR_STR`, `AC_PED`, `AR_MARC`; one fitted ground plane | Survey plan, approximate levels |
| Landmark identity and use (churches, towers, Palazzo della Ragione's 35 m height) | OpenStreetMap, ODbL (`Data/PadovaCentro/osm/landmarks.json`) | Crowd-sourced |
| Hipped roofs | CGAL straight skeleton of each outline at Veneto coppi pitch (~19°) | Typology: the survey has no ridges |
| Window rhythm, shutters, cornices, shopfronts, plaster colours, column spacing | Padova's recurring façade vocabulary, chosen deterministically per unit | Typology, **not** per-building fact |
| Palazzo della Ragione | Survey outlines for hall, loggias and shop arcades; OSM ridge height; bay rhythm drawn from the Descouens 2017 photograph as a reference | Modelled landmark |

Ordinary façades are therefore *plausible for Padova*, not verified per building. Do not describe them as surveyed façades. Replace them building by building as licensed elevation evidence arrives; the footprint, height and portico data must stay untouched.

## Pipeline

```sh
brew install cgal                                         # once; used only by the offline roof tool
.context/geo-venv/bin/pip install pillow                  # once
.context/geo-venv/bin/python Tools/architecture/make_textures.py   # needs .context/polyhaven downloads
.context/geo-venv/bin/python Tools/architecture/plan_city.py       # writes Assets/Geography/PadovaCentro/CityPlan.json
unity command run_script --file AgentScripts/BuildPadovaCity.cs --entry BuildPadovaCity.Build
unity command run_script --file AgentScripts/CaptureViews.cs --entry CaptureViews.All   # .context/views/*.png
unity command run_tests --mode editor
python3 Tools/verify_playable.py --build --measure
```

`plan_city.py` classifies wall exposure and writes a compact, versioned plan of about 6 MB. `PadovaCity` (`Assets/Scripts/Architecture`) expands it into chunked meshes and colliders in about 0.4 s at load, and in the Editor for preview. Generated meshes use `HideFlags.DontSave`, so neither the scene nor Git stores millions of vertices.

`roof_skeleton.cpp` uses CGAL, which is GPL. It is an offline data tool compiled into `.context/bin`. It is not linked into the game; only its numeric output ships.

## Wider city and exploration

`Tools/world/expand.py` exports `Data/World/WorldPlan.json`: 19,355 additional DBT volume units over 3 × 2.6 km, 7,221 road/green/water polygon parts, 652 road-network segments and 6,530 authored tree positions inside mapped green areas. `Tools/world/verify_sources.py` compares all 159,468 outline vertices, all eaves/heights and 2,184 suspended clearances directly against the original municipal shapefile. `Tools/world/map.py` draws the map from the same coordinates.

`WorldContext` builds the surroundings in 150 m chunks and culls rendering/collision by distance. The core uses two visual detail levels with independent colliders. It keeps its existing ground fit; the surrounding terrain interpolates nominal survey base elevations and blends into the core. This is a navigation approximation, not a terrain scan. No second ground collider lies under the core.

`Landmarks.cs` adds the clock dial/gateway, Capitanio frontage, Duomo/baptistery domes and Bo double courtyard colonnade. [Sources and simplifications](../Data/World/landmarks.md) identify the actual source units. `Streetscape` adds authored market stalls, lamps and benches. `WorldShapes` contains original street-object and vehicle geometry.

`Padova.World` contains pedestrian navigation, WheelCollider cars, AI traffic, police, activities, an assisted arcade aircraft, map, minimap, waypoint and discovery HUD (`Hud.cs` holds the shared look). `BuildOpenWorld.cs` saves the scene and bakes the pedestrian NavMesh. See [controls and regeneration](PLAYABLE.md).

## Blender landmark models

The Basilica del Santo and Prato della Valle are procedural Blender models, generated headlessly by `Tools/blender/build_landmarks.py` from frozen survey evidence in `Data/Landmarks/sources.json`. The script writes an editable `.blend` to `Blender/Landmarks/` and an FBX to `Assets/Art/Landmarks/`. `AgentScripts/BuildLandmarks.cs` remaps their materials onto the existing Padova materials and places them at their anchors. `Assets/Art/Landmarks/Landmarks.json` tells `WorldContext` which DBT units, canal patches and ground cells the models replace. Survey versus typology, the known conflicts and the rebuild commands are in [Data/Landmarks/README.md](../Data/Landmarks/README.md).

Blender UVs are metric like MeshSink, but shifted per face by whole 8.8 m periods. Absolute UVs of several hundred metres lose fragment precision on Metal and render a single texel.

## Remaining limits

- Ordinary façades, roof shapes and much landmark ornament are typology, not measured elevations. Other landmarks, including Palazzo Moroni and the Gran Guardia, still have generic treatments.
- The detailed core and lower-detail surroundings differ in façade detail. The finite map has boundaries; it is not all of Padova.
- Pedestrians walk central routes. Cars are drivable, with authored AI lanes on surveyed centrelines, taxi fares, a checkpoint sprint and police pursuit. Traffic rules and lane offsets are gameplay assumptions; lanes that would clip architecture are closed to traffic rather than altered. Interiors remain absent.
- Ground is interpolated, and waterways are visual context. There is no hydrology or water-driving simulation.
- Flight starts airborne with assistance; there is no runway, landing or flight-simulator model.
- The M4 Air performance target still needs a representative 20-minute measurement.
