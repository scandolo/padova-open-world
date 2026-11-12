# Agent development loop

These commands were tested in the local Conductor workspace on 2026-09-28 with Unity CLI `1.0.0-beta.11`, Editor `6000.3.25f1` arm64, and Pipeline `0.8.0-exp.1`. Run them from the Unity project root. A new Conductor chat may need **Refresh MCP status** and a new session to discover newly configured MCP tools.

## 1. Preflight

The current exploration milestone uses `python3 Tools/verify_world.py --build --measure`. It covers cars, map, plane and pedestrians in addition to walking. See [PLAYABLE.md](PLAYABLE.md). No computer-use automation is required: scene construction, input, state checks and screenshots all use Unity Pipeline. Treat `ITERATION_AND_FIDELITY.md`'s photographic landmark discussion as history; the current architecture is modelled geometry.

Read [ITERATION_AND_FIDELITY.md](ITERATION_AND_FIDELITY.md) for verified gotchas: old Player descriptors, app focus, FBX curve paths, input-event timing and overlapping GIS ground. Use `python3 Tools/verify_playable.py --editor --case controls,jump` for a focused loop, then `--build --measure` for the final native gate. The compiled probe replaces runtime C# snippets for normal gameplay checks.

```sh
test "$CONDUCTOR_IS_LOCAL" = 1
unity --version
unity license status --format json
unity open .                 # if the Editor is not already running
unity status --format json   # expect state: ready
unity pipeline list --format json
unity command editor_status --format json
```

The Editor must be running for Pipeline Editor commands. Unity Personal must be active on this Mac. `unity auth status` can be signed out while an active Personal license and local Editor control still work; Unity Cloud is not required. If the Editor reports Safe Mode, fix compile errors before retrying Pipeline.

If the Editor closes between agent sessions, reopen it and wait for `unity status` to report `ready`. `unity open . --wait` keeps the launching terminal attached until the Editor exits; run that in a separate terminal when you need a long-lived Editor session.

Claude Code user MCP name: `unity-editor-mcp`. Codex user MCP name: `unity`. Both start `unity mcp`; the global Blender MCP is separate. The MCP server can start while the Editor is closed, but Editor tools need a running Editor. This setup exists on this Mac; a fresh machine needs its own CLI, Editor, license, and MCP registration.

## 2. Edit, compile, and test

```sh
unity command set_autotick --enable true --interval_ms 50 --persist true
# Edit code and assets. Use Unity APIs through Pipeline for scenes, GameObjects, and serialized assets.
unity command recompile
unity command recompile_status --format json  # poll until completed; inspect errors
unity command list_tests --mode editor --format json
unity command run_tests --mode editor --format json
```

EditMode tests verify project setup and the saved Padova survey's source counts, heights, portico treatment, mesh references and metric anchors. Geographic import tests run with `.context/geo-venv/bin/python -m unittest discover -s Tools/geodata -v`. Add focused tests for actual gameplay behavior as features arrive. Play Mode can be controlled with `unity command editor_play` and `unity command editor_stop`.

For a Game view image that includes screen-space UI, open the Game tab if needed, then run `capture_game_view --source screen`. If no Game view exists, this Editor command opens it:

```sh
unity command eval 'return UnityEditor.EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor")).titleContent.text;'
```

The Pipeline `capture_game_view --save_path` implementation writes under `Assets/` even when given an absolute path. Move inspection images to `.context/` afterward so they do not become game assets.

## 3. Development Player smoke test

The default game scene is now `PadovaPlayable.unity`. Run `python3 Tools/verify_playable.py --build` for the animated third-person character and gameplay checks. See [PLAYABLE.md](PLAYABLE.md). `verify_survey.py` still verifies the separate survey view.

For the current district, `python3 Tools/verify_survey.py --build` builds and launches `.context/build/PadovaCentro.app`, sends real keyboard and pointer input through Pipeline, asserts camera/selection state, takes three screenshots, samples counters, and closes the Player. Evidence is written to `.context/padova-player-verification.json`. See [REAL_WORLD_DATA.md](REAL_WORLD_DATA.md) for the scene's limits. The commands below retain the original blank-scene infrastructure smoke example.

The project config enables the runtime Pipeline server for Development builds. The package compiles the server out of normal release builds unless a special `ENABLE_RUNTIME_PIPELINE` define is deliberately added; do not add that define to shipping builds. The server binds to localhost and uses a per-run token.

```sh
unity command build --target StandaloneOSX \
  --outputPath .context/build/PadovaOpenWorld.app \
  --options '["Development","AllowDebugging"]' \
  --scenes '["Assets/Scenes/SampleScene.unity"]' \
  --confirm true --format json
unity command build_status --format json  # poll until completed; require Succeeded
./.context/build/PadovaOpenWorld.app/Contents/MacOS/padova-open-world \
  -logFile "$PWD/.context/player.log"
```

Launch the Player from a separate terminal or process so the agent can continue issuing commands. The runtime descriptor appears inside `PadovaOpenWorld.app`. For `--runtime-path`, pass the **app directory**, not the descriptor file:

```sh
unity command runtime_status --runtime-path "$PWD/.context/build/PadovaOpenWorld.app" --format json
unity command simulate_key --key W --action press --runtime-path "$PWD/.context/build/PadovaOpenWorld.app"
unity command eval "UnityEngine.ScreenCapture.CaptureScreenshot(\"$PWD/.context/player.png\"); return \"queued\";" \
  --runtime-path "$PWD/.context/build/PadovaOpenWorld.app"
unity command quit --runtime-path "$PWD/.context/build/PadovaOpenWorld.app"
```

The Player also exposes `simulate_pointer`, `set_target_framerate`, logs, C# evaluation, and `runtime_status` performance counters. A successful input command only proves delivery; compare a game-state value before and after input to prove the action worked. The original SampleScene has no player; the new survey scene has an inspection camera, not a walking character.

A normal macOS release build was also verified to succeed. It did not contain `Unity.Pipeline.dll`, `Unity.Pipeline.IlInterpreter.dll`, or a runtime port descriptor. The small `Unity.Pipeline.Attributes.dll` metadata assembly remained in the output; it is not the command server. Do not ship the Development Player used for agent testing.

## 3b. Headless runs (no window, no focus stealing)

Use `--headless` with `verify_playable.py` or `verify_world.py` whenever a person is using the Mac:

```sh
python3 Tools/verify_world.py --headless --build --measure
```

The Development Player starts with `-batchmode`: it opens no window, does not appear on screen and never takes focus. It still creates the Metal device on the Mac's GPU, runs at 60 FPS and accepts Pipeline key/pointer input; the Player reports itself as focused, so the focus gates pass without `--ignore-focus`. In batchmode, cameras do not render on their own, so `gameplay_capture` renders `Camera.main` into a RenderTexture (`GameplayProbe.RenderCamera`) instead of `ScreenCapture`. Screenshots show the 3D world but **not the IMGUI HUD** (minimap, speedometer, banners); check HUD values through the probe state, and use a normal windowed run when the HUD itself must be seen. Frame timings without on-screen presentation are not a performance benchmark; keep the 20-minute M4 Air measurement as a windowed run.

Verified on 2026-11-12, against the 2026-10-12 build, with captures injected through `eval_file` because that build predates `RenderCamera`. All world cases passed headless except `police`, which also failed in the windowed run of 2026-10-12. A window sampler (`CGWindowListCopyWindowInfo`, every 0.5 s) saw no game window. Run `landmarks` and `santo` in separate invocations: `landmarks` already discovers the Santo, so `santo` then waits for a discovery that never comes.

`--build` also needs an Editor. For no Editor window either, start it in batchmode from the project root and leave it running; Pipeline's Editor server answers as usual (`unity status`, `build`, `run_tests`):

```sh
/Applications/Unity/Hub/Editor/6000.3.25f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -projectPath "$PWD" -logFile "$PWD/.context/unity-editor.log" &
```

A batchmode Editor exits at startup with `No valid Unity Editor license found` / `'com.unity.editor.headless' was not found` when no licence is active (check `unity license status --format json`: `active` must be true). Sign in and activate Unity Personal through Unity Hub, then retry. `capture_game_view` needs a Game view window and does not work in a batchmode Editor; use `AgentScripts/CaptureViews.cs` or a headless Player instead. One Editor may hold a project at a time, so give parallel agents their own worktree (clone `Library/` with `cp -cR`).

Agent outputs: `.context/playable-verification.json` (pass/fail, state, performance samples), PNGs under `.context/world/` and `.context/padova-*.png` (including `padova-failure.png` on failure), `.context/playable-player.log` (Player log) and `.context/unity-editor.log` (Editor log).

## 4. Interpreting results

- Editor tests: require explicit passed counts and zero failed tests.
- Build: require `build_status.result == "Succeeded"`, not only a queued response.
- Runtime: require a reachable runtime server, expected scene, frame progression, a screenshot, and feature-specific state assertions.
- Performance: compare frame times and memory under a representative 20-minute route. The blank scene's FPS is not a game benchmark.
- Close the development Player after testing. Keep the Editor open only while an agent needs it; closing it stops the local Pipeline server and saves battery.

Multiple Conductor worktrees need separate local Unity project paths. The Unity CLI chooses by current directory or `--project-path`; use explicit paths if several Editors are open. A Conductor cloud workspace cannot access this Mac's Unity Editor. `.context/` is workspace-local and is not transferred to other worktrees, so this tracked guide is the portable handoff.
