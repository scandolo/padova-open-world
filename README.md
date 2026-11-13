# Padova Open World

A native macOS open-world game built with **Unity 6.3 LTS and the Universal Render Pipeline (URP)**. Start in Piazza della Frutta, walk through central Padova, take a car anywhere in the city, run taxi fares, outrun the police, or see it all from an assisted sightseeing plane.

The city uses public municipal geography: real street plans, building outlines, courtyards, recorded elevations and portico clearances. Architectural detail is modelled in 3D. **Ordinary façades and roofs use documented local typology; they are not measured elevations or scans of every building.** Photographs are not projected onto façades.

## What is in the game

- **The whole comune of Padova, generated from data**: 100,134 surveyed building volumes, every carriageway, sidewalk, cycle path, canal, garden wall and tree row, streamed around the player in 256 m tiles. Nothing outside the historic core is modelled by hand: see [the city engine](docs/CITY_ENGINE.md).
- A hand-detailed historic core around Palazzo della Ragione, plus Blender landmarks (the Santo, Prato della Valle, Santa Giustina and others).
- A drivable road graph of the whole city (999 km of streets with one-way rules and speed classes), lane markings, zebra crossings, street lamps and painted kerbside parking bays, all derived from the survey graph.
- **Ten vehicle types** with arcade physics: utilitaria, berlina, van, taxi, police pantera, scooter, Ape, bus, tram, bicycle. Parked vehicles line the streets and any of them can be taken; traffic and pedestrians fill the whole city.
- **Day and night**: a moving sun, sunsets, lit windows across the city, lamplight on the streets and headlights. T skips to the next part of the day.
- **Sound, all synthesised at start-up** (no audio files): an engine with gears per vehicle type, tyres, horn, crashes, passing traffic, sirens, church bells on the hour, birds by day and crickets by night.
- **Jobs** (J): taxi fares, deliveries from real kitchens to real house numbers, the Garibaldi–Prato sprint and sprints to any landmark. Police pursuits with wanted stars.
- **Secrets**: 24 crested Padovana hens loose in the parks, spritz, espresso and gelato at the real bars and cafés, and a few things only a Padovano would look for.
- A title screen and pause menu with three picture presets, an interactive city map with waypoints, 255 places to discover, and an assisted sightseeing plane.

Development, builds and tests run locally. Unity Cloud, Unity Version Control, Cloud Build and paid Unity Gaming Services are disconnected.

## Requirements

### Open and play

| Requirement | Project baseline |
| --- | --- |
| Computer | Apple Silicon Mac; development target: MacBook Air M4 with 16 GB RAM |
| Unity Editor | **6000.3.25f1**, Apple Silicon, pinned in [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt) |
| Unity license | An active Editor license; local development was verified with Unity Personal |
| Source control | Git |
| First import | Internet access for Unity Package Manager to restore the pinned packages |

Use Unity Hub to install the pinned Editor and its Mac build support. Open the root folder containing `Assets/`, `Packages/` and `ProjectSettings/`. Other platforms and Editor versions have not been validated.

The city plans, materials, character assets, vehicle meshes and pedestrian NavMesh are included in the repository. **Playing the saved scene does not require Python, Blender, CGAL or a fresh download of the survey.** After packages are restored, the game uses local assets and has no live map-service dependency.

Key packages are pinned in [manifest.json](Packages/manifest.json) and [packages-lock.json](Packages/packages-lock.json):

| Package | Version | Purpose |
| --- | --- | --- |
| Universal Render Pipeline | 17.3.0 | Materials, lighting, shadows and post-processing |
| Input System | 1.20.0 | Keyboard and pointer controls |
| AI Navigation | 2.0.14 | Pedestrian navigation |
| Test Framework | 1.6.0 | Unity EditMode tests |
| Unity Pipeline | 0.8.0-exp.1 | Local Editor and Development Player automation |

### Optional development tools

- **Unity CLI**: tested with `1.0.0-beta.11`; needed for the terminal workflow below. The game can also be opened and built through Unity Hub and the Editor.
- **Python 3.9–3.12** for geographic processing and image generation. The pinned toolchain was exercised with Python 3.9.6. Install [requirements.txt](requirements.txt).
- **Xcode Command Line Tools and Homebrew CGAL** for rebuilding roof geometry. The C++ helper uses C++17, GMP and MPFR, with Apple Silicon Homebrew paths under `/opt/homebrew`.
- The Python native verification scripts use the standard library; they do not need the geographic packages.

Unity CLI's documented macOS installer is optional; open a new terminal after installing and check `unity --version`:

```sh
curl -fsSL https://unity.com/install.sh | UNITY_CLI_CHANNEL=beta bash
```

```sh
# Optional: geographic/material tools only
python3 -m venv .context/geo-venv
.context/geo-venv/bin/python -m pip install -r requirements.txt

# Optional: rebuild the CGAL roof helper
xcode-select --install
brew install cgal
```

## Quickstart

### 1. Clone the repository

```sh
git clone https://github.com/scandolo/padova-open-world.git
cd padova-open-world
```

### 2. Open the project

In Unity Hub, choose **Add project from disk**, select this folder and open it with **6000.3.25f1**. Wait for package restoration and the initial asset import to finish.

With Unity CLI installed, the equivalent is:

```sh
unity open .
```

### 3. Start playing

Open **`Assets/Scenes/PadovaPlayable.unity`** in the Project window and press **Play**. The city is built from the included plans and compiled packs as you move. The character starts in Piazza della Frutta; press Enter on the title screen.

`Assets/Scenes/PadovaCentroSurvey.unity` is a separate cartographic inspection scene with source-record selection; it is not the game scene.

### Controls

| Key | On foot | At the wheel | Flight |
| --- | --- | --- | --- |
| W A S D / arrows | Walk | Drive and steer | Climb, descend, bank |
| Shift | Sprint | Turbo | Faster |
| Space | Jump | Handbrake | Slower |
| E (or Return) | Take a parked vehicle, or steal one from traffic | Get out | — |
| Q | — | Horn | — |
| R | — | Put the car back on the road | End flight |
| F | Sightseeing flight | Sightseeing flight | Back to where you were |
| B | Order at a bar, café or gelateria | — | — |
| J, then 1–4 | Jobs: taxi, sprint, deliveries, sprint to a landmark (0 quits) | same | — |
| M | City map: drag, scroll, click for a waypoint | same | same |
| T | Skip to the next part of the day | same | same |
| C / mouse drag / scroll | Camera view / look around / zoom | same | — |
| H / Esc | Key hints / menu (picture preset, volume, controls) | same | same |

### 4. Build a native Mac app

In **File → Build Profiles**, select macOS and ensure `Assets/Scenes/PadovaPlayable.unity` is the enabled scene. Use **Build and Run**. Keep local output in `Builds/` or `.context/build/`, which are ignored by Git.

For an automated Development Player build and gameplay verification, leave the Editor open and run:

```sh
mkdir -p .context/build .context/world
unity status --project-path "$PWD" --format json
unity command set_autotick --enable true --interval_ms 50 --project-path "$PWD"
python3 Tools/verify_world.py --build --measure
```

The script builds `.context/build/PadovaPlayable.app`, launches it, sends input, checks game state, captures screenshots and closes the Player. To play that build afterward:

```sh
open .context/build/PadovaPlayable.app
```

This `.app` is generated locally and is not included in Git. Development builds contain a localhost Pipeline command server; use a normal non-Development build for distribution.

## Technical overview: how the city was built

### 1. Import the real geography

[Tools/geodata/padova.py](Tools/geodata/padova.py) reads the public **Comune di Padova / Regione del Veneto DBT 2007**. Source snapshots, attributes, hashes and derived core data are recorded under `Data/PadovaCentro/`.

Coordinates are transformed from **EPSG:6706** to **EPSG:7791**, then rebased around **11.8744° E, 45.4077° N**. Unity X points east, Z points to grid north, and one unit is one metre. Vertical coordinates subtract a 15 m reference offset. Polygon outlines and courtyard holes are retained.

### 2. Turn the survey into an architectural plan

[Tools/architecture/plan_city.py](Tools/architecture/plan_city.py) examines neighbouring volumes to classify street façades, party walls, courtyard walls and porticoes. It uses recorded eave elevations and valid `UN_VOL_INH` clearances for suspended volumes.

The offline C++ helper, [roof_skeleton.cpp](Tools/architecture/roof_skeleton.cpp), uses CGAL straight skeletons to construct hipped roofs. Roof pitch is typology: the source does not provide surveyed ridges. The result is `Assets/Geography/PadovaCentro/CityPlan.json`.

### 3. Generate the 3D city in Unity

`Assets/Scripts/Architecture/` reads the plans and builds meshes through Unity APIs:

| Component | Responsibility |
| --- | --- |
| `PadovaCity` / `MeshSink` | Chunked geometry, material slots, ground, collision and two visual detail levels |
| `FacadeBuilder` | Modelled openings, reveals, shutters, cornices, shopfronts, columns and arcades |
| `RagioneBuilder` | Palazzo della Ragione's halls, loggias, shops and hull-shaped roof |
| `LandmarkBuilder` | Landmark-specific additions for the clock, Capitanio, Duomo/baptistery and Bo |
| `Streetscape` / `WorldShapes` | Original market, street-object and vehicle geometry |

Generated city meshes use `HideFlags.DontSave`: the scene stores the plans and configuration and recreates those meshes on load. Supporting assets such as vehicles, the NavMesh and the older survey-inspection mesh library are saved as Unity assets. Materials combine reusable Poly Haven surfaces, normal maps and generated architectural patterns with URP lighting, an HDR sky and post-processing.

### 4. Extend beyond the core

[Tools/world/expand.py](Tools/world/expand.py) exports `Data/World/WorldPlan.json`, including the surrounding volumes, roads, green areas, waterways and landmark anchors. Unity imports a copy under `Assets/World/`.

`WorldContext` builds the surroundings in **150 m chunks** and enables rendering/collision by distance. Outer walls retain their source outlines and nominal bases/eaves; façades are simpler than in the core. Navigation terrain interpolates nearby nominal base elevations on a 50 m grid and blends into the core's ground surface. This is an approximation, not a terrain scan. Trees are authored placements within mapped green polygons, not surveyed individual trees.

[Tools/world/map.py](Tools/world/map.py) draws a north-up map from the same polygons used by the city. [verify_sources.py](Tools/world/verify_sources.py) compares the expansion's outlines, elevations and clearances with the original municipal shapefile.

### 5. Compile the whole comune

[Tools/city/compile_city.py](Tools/city/compile_city.py) turns the municipal survey into binary 256 m tiles (`Assets/StreamingAssets/City`, 67 MB); `roads.py` and `places.py` add the drivable graph, named places and house numbers. At run time `CityStreamer` meshes tiles on worker threads: one material and three draw calls per distant tile, modelled façades from the same `FacadeBuilder` near the player. [docs/CITY_ENGINE.md](docs/CITY_ENGINE.md) has the pipeline, the pack format and what is evidence versus typology.

### 6. Add gameplay and verification

- **Walking:** `ThirdPersonMotor` uses a `CharacterController`; `ThirdPersonCamera` follows it and retracts at obstructions. Kenney animation curves are remapped to the imported skeleton.
- **Pedestrians:** `CityCrowd` spawns animated `NavMeshAgent` characters on an Editor-baked navigation mesh, with path checks and local avoidance.
- **Vehicles:** `CityCar` is a ray-cast arcade model on a `Rigidbody`; `VehicleCatalog` holds the handling of each type and `VehicleFactory` builds them from the Blender library. `CityTraffic`, `ParkedCars` and `StreetWalkers` fill the streets from the road graph.
- **Atmosphere:** `DayCycle` moves the sun and publishes the night level; `SoundBank` and `CityAudio` synthesise and mix every sound.
- **Game loop:** `Activities` (jobs), `Police`, `EasterEggs`, `GameMenu` and `Quality`.
- **Flight:** `TourPlane` implements assisted arcade motion, altitude limits and boundary recovery. `OpenWorld` manages entering/exiting vehicles, camera changes and safe return to walking.
- **Map and HUD:** `OpenWorld` provides pan/zoom, coordinate conversion, waypoints, discovery state and mode-specific controls.
- **Automation:** compiled `GameplayProbe` and `WorldProbe` commands expose state through Unity Pipeline. Python verification sends real Input System events and asserts observed results.

The runtime performs no GIS downloads or CGAL compilation. Geographic processing and roof generation happen offline; Unity consumes the prepared plans and assets.

## Repository layout

```text
Assets/
  Scenes/             Playable scene and survey inspection scene
  Scripts/
    Geography/        Source records and survey inspection
    Architecture/     Core city, façades, roofs, landmarks; City/ is the streamed city engine
    Gameplay/         Walking character, camera and probes
    World/            Vehicles, traffic, people, jobs, police, day and night, sound, menus
  Art/                Textures, materials, sky and character source assets
  Geography/          Imported core city plan and survey assets
  World/              Road and landmark plan, map sheets, city material, pedestrian NavMesh
  StreamingAssets/City/  Compiled city packs, road graph, places and addresses
  Resources/          Vehicle prefabs and run-time shaders
  Shaders/            City, glass and minimap shaders
  Tests/EditMode/     Project, survey, character and world tests
AgentScripts/         Unity API builders and offscreen capture scripts
Data/                 Geographic snapshots, generated plans and provenance
Tools/                Python processing, C++ roof helper and native verification
Packages/             Pinned Unity dependencies
ProjectSettings/      Editor, renderer, input and build configuration
docs/                 Development loop, fidelity, sources and controls
requirements.txt      Optional Python geographic/material toolchain
```

`.context/` is local scratch space for downloads, virtual environments, reports, screenshots and builds. It is not part of a fresh clone. Conductor and agent MCP registrations are optional, machine-local development conveniences.

## Development and tests

With the project open:

```sh
unity status --project-path "$PWD" --format json
unity list --project-path "$PWD" --format json
unity command set_autotick --enable true --interval_ms 50 --project-path "$PWD"
unity command recompile --project-path "$PWD"
unity command recompile_status --project-path "$PWD" --format json
```

Wait for recompilation to finish and check `failed` and `errors` before continuing. A successful CLI invocation can still report compilation errors inside its result.

```sh
# Unity tests; poll test_status until complete and require a nonzero test count
unity command run_tests --mode editor --async_tests true --project-path "$PWD"
unity command test_status --project-path "$PWD" --format json

# Geographic tests using the versioned source snapshots
.context/geo-venv/bin/python -m unittest discover -s Tools/geodata -v
# City pack, road graph and address tests (numpy only)
.context/city/venv/bin/python -m unittest Tools/city/test_city.py

# Focused native verification, reusing an unchanged matching build
python3 Tools/verify_world.py --headless --case map,car,night,parked,jobs,secrets
```

`--headless` runs the Player hidden: full rendering and HUD, no window, no sound. The native verifier checks the build's source fingerprint, process ID, scene readiness and application focus. Reports and screenshots go into `.context/`. See [the development loop](docs/AGENT_DEVELOPMENT_LOOP.md) and [playable verification](docs/PLAYABLE.md) for evidence and known automation issues.

### Rebuild the scene from the included plans

These builders regenerate scene objects and save `PadovaPlayable.unity`; save any manual scene work first. Stop Play Mode before running them.

```sh
unity command run_script --file AgentScripts/BuildPadovaCity.cs --entry BuildPadovaCity.Build --project-path "$PWD"
unity command run_script --file AgentScripts/BuildOpenWorld.cs --entry BuildOpenWorld.Build --project-path "$PWD"
unity command run_script --file AgentScripts/CaptureViews.cs --entry CaptureViews.All --project-path "$PWD"
```

Run `BuildOpenWorld` after `BuildPadovaCity` so it reconnects the wider world to the rebuilt core and rebakes pedestrian navigation. These commands use the included plans and imported art; first-time players do not need to run them.

Regenerating the **geographic inputs** is a separate, optional workflow. It requires the original municipal archive, regional road snapshot and, for material regeneration, original image downloads in `.context/`. Those large source caches are not included. See [geographic sources and acquisition](docs/REAL_WORLD_DATA.md), [architectural pipeline](docs/ARCHITECTURE.md) and [expansion provenance](Data/World/landmarks.md) before running exporters.

## Fidelity and current limits

- Municipal records date from **2007**; the game is not a certified model of present-day Padova.
- Ordinary façade details, roof shapes, material finishes and much landmark ornament are typology or reference-informed models, rather than per-building scans.
- The world is finite. The surroundings have less architectural detail than the central district.
- No building interiors. Roads lie flat on the terrain: flyovers and underpasses are not raised or sunk.
- Sound is synthesised and has been checked by measurement, not by ear, in automated runs.
- Flight starts airborne. There is no airport, takeoff, landing or flight-simulator physics.
- The M4 Air target is sustained smooth play. Short walking samples reached 60 FPS, but RPC-heavy tests showed stalls and later Pipeline timeouts. Flight and 20-minute sustained/thermal performance are not certified.

## Sources and attribution

| Content | Source / license |
| --- | --- |
| Municipal geography and regional road network | Comune di Padova / Regione del Veneto, IODL 2.0 |
| Landmark names and uses | OpenStreetMap contributors, ODbL |
| Surface textures and sky | Poly Haven, CC0 |
| Character, skins and animations | Kenney, CC0 |
| Ragione modelling reference | Didier Descouens photograph, CC BY-SA 4.0; used as a drawing reference |

Detailed source identities, hashes, licenses and modelling limits are in [REAL_WORLD_DATA.md](docs/REAL_WORLD_DATA.md), [THIRD_PARTY_ASSETS.md](docs/THIRD_PARTY_ASSETS.md) and [Data/World/landmarks.md](Data/World/landmarks.md). CGAL is used by the offline roof tool and is not linked into the Player. Third-party data and art retain their respective licenses.

Commit `Assets/`, `Packages/`, `ProjectSettings/`, source tools, data and Unity `.meta` files. Keep `Library/`, `Temp/`, local credentials, logs, `.context/` and Player builds out of Git.
