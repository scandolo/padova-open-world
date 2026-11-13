# Padova Open World agent instructions

Read [docs/PROJECT_BRIEF.md](docs/PROJECT_BRIEF.md) and [docs/AGENT_DEVELOPMENT_LOOP.md](docs/AGENT_DEVELOPMENT_LOOP.md) before changing the game. In this Conductor workspace, also read `.context/AUTOMATION_AUDIT.md` for machine-specific status and unresolved gaps. `.context` is gitignored and will not appear in a fresh clone.

Everything outside the hand-built core comes from the city engine: read [docs/CITY_ENGINE.md](docs/CITY_ENGINE.md) before touching `Tools/city`, `Assets/Scripts/Architecture/City` or the packs in `Assets/StreamingAssets/City`. Change a rule and recompile; do not fix individual buildings or streets by hand.

Central Padova must use real street and building evidence. Read [docs/REAL_WORLD_DATA.md](docs/REAL_WORLD_DATA.md) and [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) before editing the district. Preserve source identities, coordinates, courtyards, eave heights, portico locations/clearances and provenance; never alter them to suit a look. The user wants a good-looking, realistic city: ordinary façades are modelled from Padova's documented architectural vocabulary on the surveyed geometry and must be labelled as typology, not survey. Never project photographs onto façades as a substitute for modelling.

This is a local-only Unity 6.3 LTS Universal Render Pipeline project. Keep Unity Cloud, Unity Version Control, Cloud Build, and paid Unity Gaming Services disconnected. Work from the repository root in a **local Mac Conductor workspace**; a cloud agent cannot control this Mac's Editor or development Player.

Use Unity CLI and the `unity-pipeline` skill to inspect and modify the live Editor. Use `unity status` before Editor operations, `unity list` to discover available commands, and `--project-path` when more than one Editor may be open. For scene and asset changes, use Unity APIs through Pipeline so `.meta` files and serialized assets stay valid. Commit `Assets/`, `Packages/`, `ProjectSettings/`, and their `.meta` files; do not commit `Library/` or development builds.

After code changes, recompile and run focused tests. For a gameplay milestone, also build a macOS Development Player, run it, send input through its runtime Pipeline server, capture a screenshot, read performance counters, and record the outcome. A successful command or screenshot alone does not prove the game behaves correctly: add state checks and test scenarios for each new gameplay feature. Run the Player hidden (`python3 Tools/verify_world.py --headless ...`): it renders the full HUD without a window, takes no focus and stays silent.

Target smooth sustained play on a MacBook Air M4 with 16 GB, with scalable settings for other Apple Silicon MacBooks. Treat frame-rate and memory goals as hypotheses until measured in a representative district over a long session. Keep the first playable scope to one small district.
