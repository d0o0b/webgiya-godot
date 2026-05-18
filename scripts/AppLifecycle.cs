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
    public override void _Ready()
    {
        BuildWorld();
        BuildUi();

        var args = ParseArguments();
        _renderQuality = ParseRenderQuality(args.GetValueOrDefault("render-quality"), RenderQuality.High);
        _surfelSamplingMode = ParseSurfelSamplingMode(args.GetValueOrDefault("surfel-sampling"), SurfelSamplingMode.ReferenceVisible);
        ConfigureViewportQuality();
        _surfelPreviewEnabled = args.ContainsKey("surfel-debug");
        _surfelLightsEnabled = ParseBool(args.GetValueOrDefault("surfel-lights"), true) && !args.ContainsKey("no-surfel-lights");
        _exportSurfelDataDuringScreenshots = args.ContainsKey("export-surfels");
        _exportRenderMetadataDuringScreenshots = args.ContainsKey("export-render-report");
        _surfelLightCount = Mathf.Clamp(ParseInt(args.GetValueOrDefault("surfel-light-count"), _surfelLightCount), 0, 64);
        _surfelLightEnergy = Mathf.Clamp(ParseFloat(args.GetValueOrDefault("surfel-light-energy"), _surfelLightEnergy), 0.0f, 2.0f);
        _surfelPreviewSize = Mathf.Clamp(ParseFloat(args.GetValueOrDefault("surfel-size"), _surfelPreviewSize), 0.002f, 0.25f);
        _surfelPreviewBudget = Mathf.Clamp(ParseInt(args.GetValueOrDefault("surfel-budget"), _surfelPreviewBudget), 256, 65536);
        _surfelExportLimit = Mathf.Clamp(ParseInt(args.GetValueOrDefault("surfel-export-limit"), _surfelPreviewBudget), 1, 65536);
        LoadPreset(0);
        if (args.ContainsKey("compare"))
        {
            RunImageComparison(args);
            GetTree().Quit();
        }
        else if (args.ContainsKey("screenshots"))
        {
            _ = RunScreenshotSequence(args);
        }
    }

    public override void _Process(double delta)
    {
        UpdateKeyboardMovement((float)delta);
        UpdateCameraDrivenSurfels((float)delta);
        if (_animateLight.ButtonPressed)
        {
            _lightAnimationTime += (float)delta;
            var x = Mathf.Sin(_lightAnimationTime * _lightSpeed) * 10.0f;
            var z = Mathf.Cos(_lightAnimationTime * _lightSpeed) * 10.0f;
            _sun.Position = new Vector3(x, 20.5f, z);
            _sun.LookAt(Vector3.Zero);
            ApplySunEnergy();
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton button)
        {
            if (button.ButtonIndex == MouseButton.Left)
            {
                _orbiting = button.Pressed && button.AltPressed;
            }
            else if (button.ButtonIndex == MouseButton.Right)
            {
                _looking = button.Pressed;
                Input.MouseMode = _looking ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
            }
            else if (button.ButtonIndex == MouseButton.Middle)
            {
                _panning = button.Pressed;
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
            {
                _distance = Mathf.Max(0.25f, _distance * 0.9f);
                UpdateCameraTransform();
            }
            else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
            {
                _distance = Mathf.Min(200.0f, _distance * 1.1f);
                UpdateCameraTransform();
            }
        }
        else if (@event is InputEventMouseMotion motion)
        {
            if (_orbiting)
            {
                _yaw -= motion.Relative.X * 0.006f;
                _pitch = Mathf.Clamp(_pitch + motion.Relative.Y * 0.006f, -1.45f, 1.45f);
                UpdateCameraTransform();
            }
            else if (_looking)
            {
                LookCamera(motion.Relative);
            }
            else if (_panning)
            {
                PanCamera(motion.Relative);
            }
        }
        else if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            if (key.Keycode == Key.F12)
            {
                if (key.CtrlPressed)
                {
                    _ = CaptureAllPresets(1.0f, "screenshots");
                }
                else
                {
                    _ = CaptureScreenshot(0.0f, "screenshots");
                }
            }
            else if (key.Keycode >= Key.Key1 && key.Keycode <= Key.Key6)
            {
                var index = (int)(key.Keycode - Key.Key1);
                if (index < _presets.Count)
                {
                    LoadPreset(index);
                }
            }
            else if (key.Keycode == Key.M)
            {
                SetGiMode((GiOutputMode)(((int)_giMode + 1) % 3));
            }
            else if (key.Keycode == Key.R)
            {
                ResetCameraToCurrentPreset();
            }
            else if (key.Keycode == Key.Escape && _looking)
            {
                _looking = false;
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }
            else if (key.Keycode == Key.G)
            {
                SetSurfelPreviewEnabled(!_surfelPreviewEnabled);
            }
        }
    }
}
