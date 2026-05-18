# Webgiya Godot Porting Plan

## Reference Overview

The reference project is a Vite/TypeScript WebGPU demo using Three.js. It renders several scene presets, builds a GPU BVH, spawns and maintains a surfel pool, integrates lighting through compute passes, and resolves indirect lighting into the final composite.

Key reference modules:

- `src/main.ts`: renderer orchestration, scene switching, GPU pass sequence, final composite.
- `src/content.ts`: scene presets, models, camera defaults, lighting presets.
- `src/scene.ts`: camera, orbit controls, base directional light.
- `src/lighting.ts`: light controls and environment-map sun direction.
- `src/surfel*.ts`: surfel allocation, aging, hash grid, radial depth, integration, resolve.
- `src/sceneBvh.ts`: BVH generation and GPU ray query buffers.

## Godot Baseline

The first Godot slice establishes the verification loop and visual baseline:

- Godot 4.7 Mono Forward+ desktop project.
- C# scene controller as the main implementation language.
- Scene preset switcher matching the reference preset list.
- Imported reference GLTF/GLB/HDR assets.
- Sponza uses the non-Draco `sponza.glb` asset because Godot 4.7 beta did not load `Sponza-Draco.glb` in verification.
- Cornell-box primitive reconstruction in Godot.
- Orbit camera, pan, zoom, and keyboard movement.
- Screenshot capture for current scene and all presets.
- Headless multi-screenshot CLI with a maximum 5 second delay.
- Automated screenshot sweeps now enforce a minimum 2 second delay after preset/view camera changes.
- Screenshot sweeps across output modes and camera view variants.
- UI-free screenshot option for image-diff captures.
- GPU-instanced surfel preview/debug mode generated from scene mesh samples.
- Surfel preview now defaults to reference-style visible sampling: dense surface candidates are projected into 8x8 camera tiles, the nearest visible candidate per tile is retained, and the old whole-scene area sampler remains available as `--surfel-sampling=geometry`.
- Imported surfel preview colors now sample albedo textures by UV, with scene transport albedo boost applied where the reference defines it.
- Screenshot runs can export structured surfel records as JSON sidecars with position, normal, camera-relative radius, albedo, bounds, and camera metadata for later RenderingDevice/compositor integration.
- Optional low-count surfel-derived colored OmniLight field for bounced color bleeding experiments in combined and indirect modes.
- Built-in image comparison command with JSON metrics and visual diff output.
- Browser-based reference screenshot helper for the original Vite/WebGPU project.
- Reference helper warns when headless browser capture returns the loading overlay instead of a completed WebGPU frame.
- Direct, indirect, and combined output modes approximated with Godot SDFGI, SSIL, ambient, and directional light controls.
- Forward+ is now the accepted desktop target renderer for this port instead of a custom WebGPU-equivalent surfel GI pipeline.
- Render quality presets configure viewport antialiasing/debanding, per-scene shadow distance/bias, SDFGI bounce feedback, SSAO, and SSIL.
- Screenshot runs can export render metadata sidecars with camera, light, shadow, quality, SDFGI, SSAO, and SSIL settings.
- Visual matrix runner captures the prioritized Cornell/Sponza/model scenes across direct, indirect, and combined output, then compares against valid reference screenshots when available.
- Per-preset Forward+ tuning now covers exposure, sky contribution, SDFGI sky reads, SDFGI feedback, SSAO/SSIL radius, and color adjustment so scene-specific fixes do not regress every preset.
- Runtime light controls for azimuth, elevation, intensity, auto-animation, and animation speed.
- Runtime surfel preview controls for visibility, size, and sample budget.
- Runtime surfel light controls for enable, count, and energy.
- Forward+ lighting with high-resolution directional shadows, SDFGI, SSIL, SSAO, glow, and HDR sky fallback.

## Work Remaining For 1:1 Rendering

Godot does not expose the same WebGPU compute render graph through normal scene rendering. A close port should be staged:

1. Keep the current Godot scene/preset/screenshot harness as the quality gate.
2. Validate all asset imports and fix scale/material differences per preset.
3. Tune Forward+ presets against stable reference screenshots instead of porting the WebGPU compute pipeline.
4. Audit imported material differences per asset: normal maps, alpha, roughness, metallic, and color-space differences.
5. Match the reference direct, indirect, combined, and debug overlay intent within Godot's Forward+ constraints.
6. Use the reference capture helper and built-in compare command to tune each scene/camera preset.
7. Replace the current invalid headless reference screenshots with captures from a normal WebGPU-capable browser session; the local headless path currently returns the reference app error overlay.

## Acceptance Targets

- Every reference preset loads in Godot or reports a concrete import blocker.
- Camera defaults match the reference scene definitions.
- Screenshot automation works from both the running app and headless CLI.
- Image comparison reports objective metrics for reference-vs-Godot screenshots.
- Visual baseline uses GPU lighting features available in Godot before custom compute GI is added.
