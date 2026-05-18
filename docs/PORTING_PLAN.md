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
- Screenshot sweeps across output modes and camera view variants.
- Direct, indirect, and combined output modes approximated with Godot SDFGI, SSIL, ambient, and directional light controls.
- Runtime light controls for azimuth, elevation, intensity, auto-animation, and animation speed.
- Forward+ lighting with high-resolution directional shadows, SDFGI, SSIL, SSAO, glow, and HDR sky fallback.

## Work Remaining For 1:1 Rendering

Godot does not expose the same WebGPU compute render graph through normal scene rendering. A close port should be staged:

1. Keep the current Godot scene/preset/screenshot harness as the quality gate.
2. Validate all asset imports and fix scale/material differences per preset.
3. Replace the current SDFGI/SSIL approximation with a custom RenderingDevice or CompositorEffect surfel pass if exact WebGPU parity is required.
4. Port the surfel data model: pool, alive list, radial depth moments, hash grid, dispatch args.
5. Port BVH or replace it with Godot-friendly acceleration data for compute ray queries.
6. Match the reference resolve/composite modes: direct, indirect, combined, debug overlays.
7. Add image-diff verification against reference screenshots for each scene/camera preset.

## Acceptance Targets

- Every reference preset loads in Godot or reports a concrete import blocker.
- Camera defaults match the reference scene definitions.
- Screenshot automation works from both the running app and headless CLI.
- Visual baseline uses GPU lighting features available in Godot before custom compute GI is added.
