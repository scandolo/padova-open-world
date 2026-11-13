# Padova exploration prototype

Open `Assets/Scenes/PadovaPlayable.unity` and press Play, or launch `.context/build/PadovaPlayable.app` after building. The game starts on foot in Piazza della Frutta, behind a title screen.

## Controls

| Key | On foot | At the wheel | Sightseeing flight |
| --- | --- | --- | --- |
| W A S D / arrows | Camera-relative movement | Accelerate / brake-then-reverse / steer | Climb / descend / bank |
| Shift | Sprint | Turbo (drains in ~3 s, refills in ~8 s) | Faster |
| Space | Jump | Handbrake (lets the tail slide) | Air brake |
| E (or Return) | Take a parked vehicle, or steal one from traffic | Get out (below ~15 km/h) | — |
| Q | — | Horn | — |
| B | Order at a bar, café or gelateria within reach | — | — |
| J, then 1–4 / 0 | Jobs: taxi / classic sprint / deliveries / sprint to a landmark / quit | same | — |
| M | Open / close the map | same | same |
| T | Skip to the next part of the day | same | same |
| F | Start the airborne tour (not while wanted) | Start the tour after stopping | Return to the walk |
| R | — | Put the car back on the nearest lane | End flight |
| Mouse drag / scroll, C | Look around / zoom, camera view | same | — |
| H / Esc | Key hints / pause menu | same | same |

The game opens on a title screen (Enter plays). Esc pauses: resume, picture preset (Battery, Balanced, Sharp), volume, the controls on one page, back to the market, quit. The world stands still behind any menu and behind the map.

The HUD shows a rotating minimap, the street or place and the district you are in, wallet, clock and wanted stars, a speedometer with the turbo meter, contextual key prompts, and banners for fares, deliveries, medals, arrests, escapes and secrets.

## What there is to do

- **Any vehicle**: kerbside bays across the city hold parked cars, vans, scooters, Apes and bicycles; walk up and press E. Now and then one has an alarm. Ram one and it is knocked loose.
- **Taxi** (J, 1): pick up along the kerb, drop at a named place; pay by distance, a tip for speed, deductions per bump.
- **Deliveries** (J, 3): collect at the nearest real pizzeria or restaurant, deliver to three real house numbers against the clock.
- **Sprints** (J, 2 and 4): Corso Garibaldi to Prato della Valle with a saved best time, or from wherever you are to a landmark one to three kilometres away. Gold, silver, bronze.
- **Police**: stealing from traffic or a hit-and-run raises up to three stars; patrols come by road, ram and arrest. Lose them for 15 s to escape.
- **Day and night**: about 48 minutes a day, the dark hours passing faster; T skips ahead. After dark windows light up, lamps throw pools of light and headlights come on.
- **Exploring**: 255 places to discover, 41 districts announced as you enter them, the map with waypoints and the sightseeing plane.
- **Secrets**: 24 crested Padovana hens clucking in parks and squares, to be caught on foot (€25 each, and a golden one); a spritz, an espresso or a gelato at the real bars, cafés and gelaterie (each does something); the three "senza"; Galileo's tower after dark; the graduates at the Bo and the bells of the Santo for whoever sounds the horn there; and a cheat code from the 1980s.

Money, the best sprint time, caught hens and found secrets are saved locally.

## City and evidence

The detailed core retains the modelled Palazzo della Ragione, façades, porticoes and roofs, with landmark geometry for the clock tower, Capitanio frontage, Duomo, baptistery and the Bo. These are reference-informed models with simplified ornament, not scans.

Everything else is the [city engine](CITY_ENGINE.md): 100,134 municipal volume units over the whole comune with class-specific façades and roofs, the surveyed ground (carriageways, sidewalks, cycle paths, squares, water, gardens), boundary walls and tree rows, streamed in 256 m tiles. Lane markings, crossings, lamps, parking bays, parked vehicles, traffic and the people on the pavements are generated from the surveyed road graph. Sixty-four pedestrians on a baked NavMesh and twelve market stalls remain in the core.

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

## Whole-city verification, 2026-11-14

- 40 EditMode tests passed (city packs, road graph, parking geometry, sound synthesis, the sun's path, driving maths, landmarks, scene). 23 Python checks on the compiled packs, road graph and addresses passed.
- The macOS Development Player was built and run **hidden** (`Tools/verify_world.py --headless`, no window, no sound). All 24 scenarios passed: animation, controls, locomotion, jump, collision, recovery, map, car, drive, flight, crowd, traffic, taxi, race, police, night (clock skip, headlights, revs and gears, horn), parked (take a kerbside vehicle, walkers on the pavements), menu (title, pause, presets, controls sheet), jobs (a delivery run to three real addresses, a landmark sprint), secrets (a hen caught on foot, a spritz and an espresso bought, the three "senza", the cheat code), landmarks, santo, prato and visual.
- Fixtures are recorded in each result: cars are placed at job stops and checkpoints, and the player is placed beside a hen, a bar and the three places. Collection, timing, pay, catching and the secrets themselves are game logic driven by real key input.
- Short samples on the Balanced preset at 1600 × 1000: 60 FPS with 12.5–13.6 ms of GPU time in the centre. These are seconds-long windows in a hidden Player, not a sustained or thermal benchmark; the 20-minute M4 Air session remains an acceptance gate.
- Sound was checked by measurement (levels, loop seams, energy per band), not by ear: every automated run is muted.
