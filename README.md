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

`--screenshot-delay` is clamped to 5 seconds.

Extra verification options:

```powershell
godot --path . --scene res://scenes/Main.tscn -- --screenshots --screenshot-scenes=cornell-box,sponza --screenshot-modes=direct,indirect,combined --screenshot-views=default,left,right,high --screenshot-delay=0.5
```

## Controls

- Left mouse drag: orbit.
- Right or middle mouse drag: pan.
- Mouse wheel: zoom.
- WASD: move target on the view plane.
- Q/E: move target down/up.
- M: cycle output mode.
- R: reset camera to current scene preset.
- 1-6: load scene preset.
- F12: capture current scene.
- Ctrl+F12: capture all scene presets.
