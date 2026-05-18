using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class Main : Node3D
{
    private void LoadPreset(int index)
    {
        index = Mathf.PosMod(index, _presets.Count);
        _currentPresetIndex = index;
        _sceneSelect.Select(index);

        var preset = _presets[index];
        SetStatus($"Loading {preset.Label}");
        ClearScene();
        CapturePresetSettings(preset);
        ConfigureEnvironment(preset);
        ConfigureLight(preset);

        switch (preset.Kind)
        {
            case "cornell":
                BuildCornellScene();
                break;
            case "leonardo":
                AddImportedScene("res://assets/models/leonardo.glb", Vector3.Zero, Vector3.Zero, new Vector3(7, 7, 7));
                break;
            case "occlusion":
                AddImportedScene("res://assets/models/testocc.glb", new Vector3(0, 0.2f, 0), Vector3.Zero, Vector3.One);
                break;
            case "marble-bust":
                BuildMarbleBustScene();
                break;
            case "sponza":
                AddImportedScene("res://assets/models/sponza.glb", Vector3.Zero, Vector3.Zero, Vector3.One);
                break;
            case "beast":
                AddImportedScene("res://assets/models/sponza.glb", Vector3.Zero, Vector3.Zero, Vector3.One);
                AddImportedScene(
                    "res://assets/models/inferno-beast-from-space-from-jurafjvs-cc0-2.glb",
                    new Vector3(0, 1.83f, 0),
                    new Vector3(0, Mathf.Pi / 2.0f, 0),
                    new Vector3(2, 2, 2));
                break;
        }

        SetOrbitFromCamera(preset.CameraPosition, preset.CameraTarget);
        SyncControls();
        ApplyOutputMode();
        RebuildSurfelPreview();
        RebuildSurfelLights();
        SetStatus($"{preset.Label} loaded");
    }

    private void CapturePresetSettings(ScenePreset preset)
    {
        _lightAzimuthDeg = preset.LightAzimuthDeg;
        _lightElevationDeg = preset.LightElevationDeg;
        _lightIntensity = preset.LightIntensity;
        _indirectIntensity = preset.IndirectEnergy;
        _presetAmbientEnergy = preset.AmbientEnergy;
        _presetIndirectEnergy = 1.0f;
        _presetOcclusionShadowStrength = preset.OcclusionShadowStrength;
        _presetBleedReduction = preset.BleedReduction;
        _presetAlbedoBoost = preset.AlbedoBoost;
        _presetShadowMaxDistance = preset.ShadowMaxDistance;
        _presetShadowBias = preset.ShadowBias;
        _presetShadowNormalBias = preset.ShadowNormalBias;
        _presetSdfgiBounceFeedback = preset.SdfgiBounceFeedback;
        _presetSdfgiReadSkyLight = preset.SdfgiReadSkyLight;
        _presetSsaoRadius = preset.SsaoRadius;
        _presetSsilRadius = preset.SsilRadius;
        _presetTonemapExposure = preset.TonemapExposure;
        _presetAmbientSkyContribution = preset.AmbientSkyContribution;
        _presetAdjustmentContrast = preset.AdjustmentContrast;
        _presetAdjustmentSaturation = preset.AdjustmentSaturation;
        _lightSpeed = 0.5f;
        _animateLight.ButtonPressed = false;
        _lightAnimationTime = 0.0f;
    }

    private void ConfigureEnvironment(ScenePreset preset)
    {
        _environment = new Godot.Environment();
        _environment.Set("background_mode", 2);
        _environment.Set("background_color", new Color(0.125f, 0.125f, 0.145f));
        _environment.Set("ambient_light_color", Colors.White);
        _environment.Set("ambient_light_source", 3);
        _environment.Set("ambient_light_sky_contribution", _presetAmbientSkyContribution);
        _environment.Set("reflected_light_source", 2);
        _environment.Set("tonemap_mode", 3);
        _environment.Set("tonemap_exposure", _presetTonemapExposure);
        _environment.Set("tonemap_white", 1.0f);
        _environment.Set("ssao_enabled", true);
        _environment.Set("ssao_radius", 2.0f);
        _environment.Set("ssao_intensity", 0.9f + _presetOcclusionShadowStrength * 1.25f);
        _environment.Set("ssil_enabled", true);
        _environment.Set("ssil_radius", 5.0f);
        _environment.Set("sdfgi_enabled", true);
        _environment.Set("sdfgi_bounce_feedback", 0.65f);
        _environment.Set("sdfgi_read_sky_light", _presetSdfgiReadSkyLight);
        _environment.Set("glow_enabled", true);
        _environment.Set("glow_intensity", _renderQuality == RenderQuality.Balanced ? 0.04f : 0.08f);
        _environment.Set("adjustment_enabled", true);
        _environment.Set("adjustment_brightness", 1.0f);
        _environment.Set("adjustment_contrast", _presetAdjustmentContrast);
        _environment.Set("adjustment_saturation", _presetAdjustmentSaturation);
        ApplyPresetEnvironmentQuality();

        var texture = ResourceLoader.Load<Texture2D>(preset.HdrPath);
        if (texture != null)
        {
            var skyMaterial = new PanoramaSkyMaterial { Panorama = texture };
            var sky = new Sky { SkyMaterial = skyMaterial };
            _environment.Set("sky", sky);
        }

        _worldEnvironment.Environment = _environment;
    }

    private void ConfigureLight(ScenePreset preset)
    {
        ApplyManualLightAngles();
        ApplySunEnergy();
        ApplyPresetShadowQuality();
    }

    private void ApplyPresetEnvironmentQuality()
    {
        if (_environment == null)
        {
            return;
        }

        _environment.Set("ssao_radius", _presetSsaoRadius);
        _environment.Set("ssao_intensity", 0.9f + _presetOcclusionShadowStrength * 1.25f);
        _environment.Set("ssil_radius", _presetSsilRadius);
        _environment.Set("sdfgi_bounce_feedback", _presetSdfgiBounceFeedback);
    }

    private void ApplyPresetShadowQuality()
    {
        _sun.Set("directional_shadow_max_distance", _presetShadowMaxDistance);
        _sun.Set("shadow_bias", _presetShadowBias);
        _sun.Set("shadow_normal_bias", _presetShadowNormalBias);
        _sun.Set("directional_shadow_fade_start", 0.82f);
    }

    private void ApplyManualLightAngles()
    {
        if (_syncingControls || _animateLight.ButtonPressed)
        {
            return;
        }

        var az = Mathf.DegToRad(_lightAzimuthDeg);
        var el = Mathf.DegToRad(_lightElevationDeg);
        const float radius = 40.0f;
        _sun.Position = new Vector3(
            radius * Mathf.Cos(el) * Mathf.Cos(az),
            radius * Mathf.Sin(el),
            radius * Mathf.Cos(el) * Mathf.Sin(az));
        _sun.LookAt(Vector3.Zero);
    }

    private void SetGiMode(GiOutputMode mode)
    {
        _giMode = mode;
        if (_giModeSelect.Selected != (int)mode)
        {
            _giModeSelect.Select((int)mode);
        }

        ApplyOutputMode();
    }

    private void ApplyOutputMode()
    {
        if (_environment == null)
        {
            return;
        }

        var indirectScale = _giMode == GiOutputMode.Direct ? 0.0f : _indirectIntensity;
        _environment.Set("ambient_light_energy", Mathf.Max(0.02f, _presetAmbientEnergy * indirectScale));
        _environment.Set("sdfgi_enabled", indirectScale > 0.0f);
        _environment.Set("sdfgi_energy", _presetIndirectEnergy * indirectScale);
        var bleedScale = Mathf.Lerp(1.15f, 0.72f, Mathf.Clamp(_presetBleedReduction, 0.0f, 1.0f));
        _environment.Set("ssil_intensity", _giMode == GiOutputMode.Direct ? 0.0f : indirectScale * bleedScale);
        ApplySunEnergy();
        RebuildSurfelLights();
    }

    private void ApplySunEnergy()
    {
        _sun.LightEnergy = _giMode == GiOutputMode.Indirect ? 0.0f : _lightIntensity;
    }

    private void SyncControls()
    {
        _syncingControls = true;
        _giModeSelect.Select((int)_giMode);
        _indirectSlider.Value = _indirectIntensity;
        _azimuthSlider.Value = _lightAzimuthDeg;
        _elevationSlider.Value = _lightElevationDeg;
        _lightIntensitySlider.Value = _lightIntensity;
        _lightSpeedSlider.Value = _lightSpeed;
        _surfelDebug.ButtonPressed = _surfelPreviewEnabled;
        _surfelSizeSlider.Value = _surfelPreviewSize;
        _surfelBudgetSlider.Value = _surfelPreviewBudget;
        _surfelLightsToggle.ButtonPressed = _surfelLightsEnabled;
        _surfelLightCountSlider.Value = _surfelLightCount;
        _surfelLightEnergySlider.Value = _surfelLightEnergy;
        _syncingControls = false;
    }
}
