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
godot --path . --scene res://scenes/Main.tscn -- --screenshots --screenshot-scenes=cornell-box,sponza --screenshot-modes=direct,indirect,combined --screenshot-views=default,left,right,high --screenshot-delay=0.5
```

Use `--screenshot-hide-ui` when capturing images for diff comparison. Use `--surfel-debug` to render the GPU-instanced surfel preview. The debug surfel overlay samples visible hand-built box faces and imported mesh triangles by surface area. Use `--surfel-size=<value>` and `--surfel-budget=<count>` for automated surfel debug captures.

Surfel lighting options:

```powershell
godot --path . --scene res://scenes/Main.tscn -- --screenshots --screenshot-scenes=cornell-box --screenshot-modes=combined --surfel-light-count=32 --surfel-light-energy=0.45
```

The colored surfel-derived OmniLight approximation is off by default because point-light placement can reveal artifacts in simple Cornell-style scenes. Enable it with `--surfel-lights=true` when tuning color bleeding. `--surfel-light-count` is clamped to 64.

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
