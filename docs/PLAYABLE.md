# Padova exploration prototype

Open `Assets/Scenes/PadovaPlayable.unity` and press Play, or launch `.context/build/PadovaPlayable.app` after building. The game starts on foot in Piazza della Frutta.

## Controls

| Input | On foot | Driving | Sightseeing flight |
| --- | --- | --- | --- |
| Arrow keys (or WASD) | Camera-relative movement | Accelerate / reverse / steer | Climb / descend / bank |
| Shift | Run | — | Faster flight |
| Space | Jump | Brake | Air brake |
| Return | Enter nearby parked or traffic car | Exit when stopped | — |
| J, then 1 / 2 / 0 | Activities: taxi / sprint / abandon (car required) | Activities: taxi / sprint / abandon | — |
| M | Open / close map | Open / close map | Open / close map |
| F | Start airborne tour | Start tour after stopping | Return to previous walking position |
| R | Return to market | Reset car to its parking position | End flight |
| Q / E, click-drag | Orbit camera | — | — |
| C / scroll | Centre camera / zoom | — | — |
| Tab | Toggle running | — | — |

The map pauses simulation. Drag to pan, scroll to zoom, click to set a waypoint, and press M or Esc to close it. Blue squares mark drivable cars. The distance label is straight-line distance; active activities also draw road-route dots. Eight places can be discovered by exploring near them.

Three compact cars are available on Via Dante, Corso Milano and Via Roma. They use Rigidbody/WheelCollider suspension, steering, braking and architecture collision. Up to 34 autonomous cars follow lanes derived from surveyed road centrelines; Return takes over a nearby traffic car. Lane direction and width are gameplay assumptions, not surveyed traffic rules.

J opens activities. Taxi fares require stopping at the passenger and destination, with distance-based pay, a speed bonus and collision deductions. The Garibaldi–Prato sprint has a countdown, ordered checkpoints and a medal reward. Money and the best race time persist locally.

F starts the aircraft already airborne. Flight assistance keeps it above the skyline and turns it back near the map boundary. There is no airport, takeoff, landing or flight-simulator physics. Ending flight restores the previous walking position.

## City and evidence

The detailed core retains Claude's modelled Palazzo della Ragione, façades, textured surfaces, porticoes and roofs. Additional landmark geometry covers the clock tower, Capitanio frontage, Duomo/baptistery and the Ancient Courtyard of Palazzo del Bo. These are reference-informed models with simplified ornament, not scanned reconstructions.

A 3 × 2.6 km envelope adds 19,355 municipal **volume units**, source road/green/water polygons and lower-detail roof geometry. Buildings can contain several volume units. The map uses the same survey outlines. Sixty-four animated pedestrians navigate a baked central NavMesh; twelve market stalls, eleven lamps, six benches and trees populate the scene. Individual trees and street objects are authored placements.

See [architecture and fidelity](ARCHITECTURE.md), [survey provenance](REAL_WORLD_DATA.md), and [landmark references and expansion limits](../Data/World/landmarks.md). DBT records date from 2007. Ordinary façades and roofs are typology; material textures are reusable surfaces, not site scans. Interiors and the whole city are not finished.

## Rebuild and verify

The saved scene, plans, assets and NavMesh work offline. Recreating the world from those inputs:

```sh
unity status --project-path "$PWD"
unity command set_autotick --enable true --interval_ms 50 --project-path "$PWD"
unity command recompile --project-path "$PWD"
unity command recompile_status --project-path "$PWD"
unity command run_script --file AgentScripts/BuildPadovaCity.cs --entry BuildPadovaCity.Build --project-path "$PWD"
unity command run_script --file AgentScripts/BuildOpenWorld.cs --entry BuildOpenWorld.Build --project-path "$PWD"
unity command run_tests --mode editor --async_tests true --project-path "$PWD"
unity command test_status --project-path "$PWD"
python3 Tools/verify_world.py --build --measure
```

`BuildOpenWorld` imports versioned `Data/World` files, saves vehicle meshes and pedestrian assets through Unity APIs, regenerates the surroundings and bakes pedestrian navigation. If the original skin files are absent from `.context/playable/Kenney/Skins`, it uses the already imported assets.

Re-exporting geography additionally needs the original municipal archive under `.context/geodata/padova-dbt`, the regional road snapshot `.context/world/street-network.json`, and the existing Python/CGAL setup:

```sh
.context/geo-venv/bin/python Tools/world/expand.py
.context/geo-venv/bin/python Tools/world/map.py
.context/geo-venv/bin/python Tools/world/verify_sources.py
```

`verify_world.py` reuses the fresh Player PID, focus checks and source fingerprint from `verify_playable.py`. Tests send actual input and assert animation, movement, map pause/waypoints, car travel/steering/braking/exit, aircraft climb/bank/return, NPC route progress, traffic takeover, taxi payment and race completion. Activity checks use position fixtures for distant targets; they test transitions and rewards, not a complete driven route. Screenshots are written under `.context/world/`; the report is `.context/playable-verification.json`. The Player closes after testing. Use `--case car` to rerun a failed scenario without rebuilding unchanged source.

Short performance samples are not a sustained thermal benchmark. A representative 20-minute M4 Air session remains an acceptance gate.

## Verification, 2026-09-28

- 15 EditMode tests passed; the six walking/geometry tests were rerun after ground tessellation and passed again.
- Final macOS Development build succeeded in 13.97 seconds with **zero errors and warnings**. All 11 native input/state scenarios passed: animation, four arrow directions/camera controls, walk/run, jump, collision, recovery, map, driving, flight, crowd movement and character grounding. Evidence: `.context/world/final-gameplay-verification.json`.
- The source comparison verified 19,355 volume units, 159,468 original outline vertices and 2,184 suspended clearances.
- A fresh 20-second walking interval without RPC polling recorded 60.00 FPS, 12.19 ms GPU time, a worst sampled frame of 18.63 ms and 541.6 MB Unity-allocated memory (`.context/world/quiet-performance.json`). This is a short window, not sustained certification.
- RPC-heavy runs recorded repeated roughly one-second stalls. Later attempts to collect an independent flight window encountered Pipeline command timeouts after the Player had become ready. The cause is unresolved. The successful gameplay report is retained separately from `.context/world/quiet-focus-failure.json`; do not claim stable flight or long-session performance from these runs.

## Gameplay continuation, 2026-10-08

- 23 EditMode tests passed. The macOS Development build succeeded in 16.98 seconds with zero errors and warnings.
- Native checks passed for traffic movement/takeover/map pause, taxi input/pickup/payment/persistence/cancellation/reset, all 12 sprint checkpoints and single payout, and 64 moving pedestrians on valid navigation. Taxi and race destination fixtures are explicitly recorded; this is not proof of a full driven route.
- Foreground native checks passed for map and driving. Further checks encountered macOS focus loss, including the existing flight scenario. The new-feature native gate completed with a temporary, in-memory Input System `IgnoreFocus` setting; saved settings were unchanged by that workaround. Test wallet and race records were restored.
- Final short sample: 60 FPS, 16.15 ms GPU, 559.7 MB Unity-allocated memory. These RPC-driven samples do not establish sustained performance.
- Evidence: `.context/activity-editmode-tests.json`, `.context/world/activities-native-verification.json`, and `.context/world/activities-menu-native.png`. These files and the local build are gitignored.
