# Webgiya Godot

Godot 4.7 Mono desktop port of the WebGPU/Three.js Webgiya surfel GI demo.

## Run

```powershell
godot --path .
```

## Automated screenshots

```powershell
godot --headless --path . -- --screenshots --screenshot-scenes=cornell-box,marble-bust --screenshot-delay=2 --screenshot-dir=screenshots
```

Automated screenshot sweeps wait at least 2 seconds after loading a preset or moving the camera, and `--screenshot-delay` is clamped to 5 seconds.

Extra verification options:

```powershell
godot --path . --scene res://scenes/Main.tscn -- --screenshots --render-quality=ultra --screenshot-scenes=cornell-box,sponza --screenshot-modes=direct,indirect,combined --screenshot-views=default,left,right,high --screenshot-delay=0.5
```

`--render-quality` accepts `balanced`, `high`, or `ultra`. `high` is the default desktop target.

Use `--screenshot-hide-ui` when capturing images for diff comparison. Use `--surfel-debug` to render the GPU-instanced surfel preview. The default `--surfel-sampling=reference` mode approximates the reference G-buffer allocation pass by projecting dense surface candidates into 8x8 camera tiles and keeping the nearest visible surfel per tile. Use `--surfel-sampling=geometry` for the old whole-scene area sampler. The overlay uses surface-aligned GPU-instanced quads, visible hand-built box faces, imported mesh triangle candidates, and albedo UV colors for imported materials. Use `--surfel-size=<value>` and `--surfel-budget=<count>` for automated surfel debug captures.

Use `--export-render-report` to write a `.render.json` sidecar next to each screenshot with the active camera, light, shadow, Forward+ quality, surfel GI, and SSAO settings.

Use `--export-surfels` to write a `.surfels.json` sidecar next to each screenshot. The export contains scene, camera, bounds, position, normal, camera-relative radius, and albedo records for the sampled surfel set. Use `--surfel-export-limit=<count>` to cap the exported records.

Surfel lighting options:

```powershell
godot --path . --scene res://scenes/Main.tscn -- --screenshots --screenshot-scenes=cornell-box --screenshot-modes=combined --surfel-light-count=64 --surfel-light-energy=0.08
```

The colored surfel-derived OmniLight approximation is on by default and is the active GI approximation for indirect and combined modes. Disable it with `--no-surfel-lights` when isolating direct lighting or debugging surfel placement. `--surfel-light-count` defaults to 64 and is clamped to 64.

## Image comparison

```powershell
godot --headless --path . --scene res://scenes/Main.tscn -- --compare --compare-reference=screenshots\reference.png --compare-candidate=screenshots\candidate.png --compare-diff=screenshots\diff.png --compare-report=screenshots\compare.json
```

The compare command reports MAE, RMSE, max channel error, and percent of pixels over 0.02, 0.05, and 0.10 error thresholds.

Reference screenshots can be captured from the original Vite/WebGPU project with:

```powershell
.\tools\capture-reference.ps1 -Scenes cornell-box,sponza
```

On some Windows/browser builds, headless Edge may capture the reference loading overlay instead of a completed WebGPU frame. The script warns when the output is suspiciously small; in that case, capture the reference image from a normal WebGPU-capable browser session and use the same compare command.

Then compare a pair with:

```powershell
.\tools\compare-pair.ps1 -Reference screenshots\reference\cornell-box-reference.png -Candidate screenshots\cornell-box-combined-default-20260518-121129-131.png -Diff screenshots\cornell-diff.png -Report screenshots\cornell-compare.json
```

For the prioritized visual parity pass, run:

```powershell
.\tools\run-visual-matrix.ps1 -Scenes cornell-box,sponza,leonardo -Modes direct,indirect,combined -Views default -Delay 2
```

The matrix writes `screenshots\matrix\visual-matrix.md`, captures Godot candidates with `--render-quality=ultra`, and compares any reference screenshots that pass the minimum-size validity check. Current headless browser reference captures on this Windows setup return the reference app error overlay, so they are marked `invalid-reference` instead of being used for metrics.

## Controls

- Right mouse drag: look around.
- Right mouse drag + WASD/QE: fly camera.
- Alt + left mouse drag: orbit.
- Middle mouse drag: pan.
- Mouse wheel: zoom.
- Shift: speed boost while moving.
- M: cycle output mode.
- R: reset camera to current scene preset.
- G: toggle surfel preview.
- GI Lights toggle: enable or disable surfel-derived colored light approximation.
- 1-6: load scene preset.
- F12: capture current scene.
- Ctrl+F12: capture all scene presets.
