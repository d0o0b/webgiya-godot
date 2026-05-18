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
    private async Task RunScreenshotSequence(Dictionary<string, string> args)
    {
        var dir = args.TryGetValue("screenshot-dir", out var dirArg) ? dirArg : "screenshots";
        var delay = ParseFloat(args.GetValueOrDefault("screenshot-delay"), 1.0f);
        delay = ClampAutomatedScreenshotDelay(delay);
        _hideUiDuringScreenshots = args.ContainsKey("screenshot-hide-ui");
        var sceneIds = args.TryGetValue("screenshot-scenes", out var scenes)
            ? scenes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : _presets.Select(preset => preset.Id).ToArray();
        var modes = args.TryGetValue("screenshot-modes", out var rawModes)
            ? rawModes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ParseGiMode)
                .Where(mode => mode.HasValue)
                .Select(mode => mode!.Value)
                .ToArray()
            : new[] { _giMode };
        var views = args.TryGetValue("screenshot-views", out var rawViews)
            ? rawViews.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : new[] { "default" };

        foreach (var mode in modes)
        {
            SetGiMode(mode);
            foreach (var sceneId in sceneIds)
            {
                var index = _presets.FindIndex(preset => preset.Id == sceneId);
                if (index < 0)
                {
                    GD.PushWarning($"Unknown screenshot scene '{sceneId}'");
                    continue;
                }

                LoadPreset(index);
                foreach (var view in views)
                {
                    ApplyScreenshotView(view);
                    await CaptureScreenshot(delay, dir);
                }
            }
        }

        if (args.TryGetValue("screenshot-quit", out var quitValue) && quitValue == "false")
        {
            return;
        }

        GetTree().Quit();
    }

    private void ApplyScreenshotView(string viewName)
    {
        _screenshotViewName = viewName.ToLowerInvariant();
        ResetCameraToCurrentPreset();

        switch (_screenshotViewName)
        {
            case "left":
                _yaw -= 0.45f;
                break;
            case "right":
                _yaw += 0.45f;
                break;
            case "high":
                _pitch = Mathf.Clamp(_pitch + 0.35f, -1.45f, 1.45f);
                break;
            case "close":
                _distance = Mathf.Max(0.25f, _distance * 0.55f);
                break;
            default:
                _screenshotViewName = "default";
                break;
        }

        UpdateCameraTransform();
    }

    private async Task CaptureAllPresets(float delay, string directory)
    {
        delay = ClampAutomatedScreenshotDelay(delay);
        for (var i = 0; i < _presets.Count; i++)
        {
            LoadPreset(i);
            await CaptureScreenshot(delay, directory);
        }
    }

    private async Task CaptureScreenshot(float delay, string directory)
    {
        delay = Mathf.Clamp(delay, 0.0f, MaxScreenshotDelaySeconds);
        var previousUiVisible = _uiLayer.Visible;
        if (_hideUiDuringScreenshots)
        {
            _uiLayer.Visible = false;
        }

        try
        {
            if (delay > 0.0f)
            {
                await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
            }

            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            var image = GetViewport().GetTexture().GetImage();
            var absoluteDirectory = MakeScreenshotDirectory(directory);
            var preset = _presets[_currentPresetIndex];
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            var debugSuffix = _surfelPreviewEnabled ? "-surfels" : "";
            var lightSuffix = _surfelLightsEnabled && _giMode != GiOutputMode.Direct ? "-surfelgi" : "";
            var path = System.IO.Path.Combine(absoluteDirectory, $"{preset.Id}-{_giMode.ToString().ToLowerInvariant()}-{_screenshotViewName}{debugSuffix}{lightSuffix}-{timestamp}.png");
            var error = image.SavePng(path);
            if (error == Error.Ok)
            {
                SetStatus($"Saved screenshot: {path}");
                GD.Print($"Saved screenshot: {path}");
                if (_exportSurfelDataDuringScreenshots)
                {
                    ExportSurfelData(Path.ChangeExtension(path, ".surfels.json"));
                }
                if (_exportRenderMetadataDuringScreenshots)
                {
                    ExportRenderMetadata(Path.ChangeExtension(path, ".render.json"), delay);
                }
            }
            else
            {
                SetStatus($"Screenshot failed: {error}");
                GD.PushError($"Screenshot failed: {error}");
            }
        }
        finally
        {
            _uiLayer.Visible = previousUiVisible;
        }
    }

    private static float ClampAutomatedScreenshotDelay(float delay)
    {
        return Mathf.Clamp(delay, MinAutomatedScreenshotDelaySeconds, MaxScreenshotDelaySeconds);
    }

    private void ExportSurfelData(string path)
    {
        var samples = CreateSurfelSamples(Mathf.Min(_surfelPreviewBudget, _surfelExportLimit));
        var json = BuildSurfelExportJson(samples);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json, Encoding.UTF8);
        GD.Print($"Saved surfel data: {path}");
    }

    private void ExportRenderMetadata(string path, float delay)
    {
        var preset = _presets[_currentPresetIndex];
        var json = $$"""
        {
          "schema": "webgiya-godot.render-report.v1",
          "scene": "{{JsonEscape(preset.Id)}}",
          "mode": "{{_giMode.ToString().ToLowerInvariant()}}",
          "view": "{{JsonEscape(_screenshotViewName)}}",
          "renderQuality": "{{_renderQuality.ToString().ToLowerInvariant()}}",
          "screenshotDelay": {{FormatFloat(delay)}},
          "cameraPosition": {{JsonVector(_camera.GlobalPosition)}},
          "cameraTarget": {{JsonVector(_target)}},
          "light": {
            "intensity": {{FormatFloat(_lightIntensity)}},
            "azimuthDeg": {{FormatFloat(_lightAzimuthDeg)}},
            "elevationDeg": {{FormatFloat(_lightElevationDeg)}},
            "shadowMaxDistance": {{FormatFloat(_presetShadowMaxDistance)}},
            "shadowBias": {{FormatFloat(_presetShadowBias)}},
            "shadowNormalBias": {{FormatFloat(_presetShadowNormalBias)}}
          },
          "forwardPlusApproximation": {
            "ambientEnergy": {{FormatFloat(_presetAmbientEnergy)}},
            "indirectIntensity": {{FormatFloat(_indirectIntensity)}},
            "tonemapExposure": {{FormatFloat(_presetTonemapExposure)}},
            "ambientSkyContribution": {{FormatFloat(_presetAmbientSkyContribution)}},
            "occlusionShadowStrength": {{FormatFloat(_presetOcclusionShadowStrength)}},
            "bleedReduction": {{FormatFloat(_presetBleedReduction)}},
            "albedoBoost": {{FormatFloat(_presetAlbedoBoost)}},
            "sdfgiBounceFeedback": {{FormatFloat(_presetSdfgiBounceFeedback)}},
            "sdfgiReadSkyLight": {{_presetSdfgiReadSkyLight.ToString().ToLowerInvariant()}},
            "ssaoRadius": {{FormatFloat(_presetSsaoRadius)}},
            "ssilRadius": {{FormatFloat(_presetSsilRadius)}},
            "adjustmentContrast": {{FormatFloat(_presetAdjustmentContrast)}},
            "adjustmentSaturation": {{FormatFloat(_presetAdjustmentSaturation)}}
          }
        }
        """;

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json, Encoding.UTF8);
        GD.Print($"Saved render metadata: {path}");
    }

    private string BuildSurfelExportJson(List<SurfelSample> samples)
    {
        var preset = _presets[_currentPresetIndex];
        var bounds = ComputeSurfelBounds(samples);
        var builder = new StringBuilder(samples.Count * 128 + 2048);
        builder.AppendLine("{");
        builder.AppendLine($"  \"schema\": \"webgiya-godot.surfel-export.v1\",");
        builder.AppendLine($"  \"scene\": \"{JsonEscape(preset.Id)}\",");
        builder.AppendLine($"  \"mode\": \"{_giMode.ToString().ToLowerInvariant()}\",");
        builder.AppendLine($"  \"view\": \"{JsonEscape(_screenshotViewName)}\",");
        builder.AppendLine($"  \"sampling\": \"{SurfelSamplingModeName(_surfelSamplingMode)}\",");
        builder.AppendLine($"  \"count\": {samples.Count},");
        builder.AppendLine($"  \"surfelBaseRadius\": {FormatFloat(0.24f)},");
        builder.AppendLine($"  \"surfelGridCellDiameter\": {FormatFloat(0.2f)},");
        builder.AppendLine($"  \"surfelGridSize\": 32,");
        builder.AppendLine($"  \"boundsMin\": {JsonVector(bounds.Min)},");
        builder.AppendLine($"  \"boundsMax\": {JsonVector(bounds.Max)},");
        builder.AppendLine($"  \"cameraPosition\": {JsonVector(_camera.GlobalPosition)},");
        builder.AppendLine($"  \"cameraTarget\": {JsonVector(_target)},");
        builder.AppendLine("  \"samples\": [");
        for (var i = 0; i < samples.Count; i++)
        {
            var sample = samples[i];
            builder.Append("    {");
            builder.Append($"\"p\":{JsonVector(sample.Position)},");
            builder.Append($"\"n\":{JsonVector(sample.Normal)},");
            builder.Append($"\"r\":{FormatFloat(EstimateReferenceSurfelRadius(sample.Position))},");
            builder.Append($"\"albedo\":{JsonColor(sample.Color)}");
            builder.Append(i == samples.Count - 1 ? "}\n" : "},\n");
        }
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private readonly struct SurfelBounds
    {
        public SurfelBounds(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public Vector3 Min { get; }
        public Vector3 Max { get; }
    }

    private static SurfelBounds ComputeSurfelBounds(List<SurfelSample> samples)
    {
        if (samples.Count == 0)
        {
            return new SurfelBounds(Vector3.Zero, Vector3.Zero);
        }

        var min = samples[0].Position;
        var max = samples[0].Position;
        for (var i = 1; i < samples.Count; i++)
        {
            min = min.Min(samples[i].Position);
            max = max.Max(samples[i].Position);
        }

        return new SurfelBounds(min, max);
    }

    private float EstimateReferenceSurfelRadius(Vector3 position)
    {
        const float baseRadius = 0.24f;
        const float gridCellDiameter = 0.2f;
        const float gridSize = 32.0f;
        var cascadeRadius = gridCellDiameter * gridSize * 0.5f;
        var distance = position.DistanceTo(_camera.GlobalPosition);
        return baseRadius * Mathf.Max(1.0f, distance / cascadeRadius);
    }

    private static string JsonVector(Vector3 value)
    {
        return $"[{FormatFloat(value.X)},{FormatFloat(value.Y)},{FormatFloat(value.Z)}]";
    }

    private static string JsonColor(Color value)
    {
        return $"[{FormatFloat(value.R)},{FormatFloat(value.G)},{FormatFloat(value.B)}]";
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private static string MakeScreenshotDirectory(string directory)
    {
        var path = directory;
        if (!System.IO.Path.IsPathRooted(path))
        {
            path = System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), path);
        }

        Directory.CreateDirectory(path);
        return path;
    }
}
