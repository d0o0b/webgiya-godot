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
    private void BuildUi()
    {
        _uiLayer = new CanvasLayer { Name = "Ui" };
        AddChild(_uiLayer);

        var controls = new HBoxContainer
        {
            Name = "SceneSwitcher",
            Position = new Vector2(15, 15),
            CustomMinimumSize = new Vector2(420, 36),
        };
        _uiLayer.AddChild(controls);

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.12f, 0.12f, 0.15f, 0.86f),
            BorderColor = new Color(0.29f, 0.29f, 0.35f, 0.35f),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 12,
        };
        controls.AddThemeStyleboxOverride("panel", style);

        _sceneSelect = new OptionButton { CustomMinimumSize = new Vector2(230, 34) };
        for (var i = 0; i < _presets.Count; i++)
        {
            _sceneSelect.AddItem(_presets[i].Label, i);
        }
        _sceneSelect.ItemSelected += index => LoadPreset((int)index);
        controls.AddChild(_sceneSelect);

        var next = new Button { Text = "Next", CustomMinimumSize = new Vector2(70, 34) };
        next.Pressed += () => LoadPreset((_currentPresetIndex + 1) % _presets.Count);
        controls.AddChild(next);

        _animateLight = new CheckButton { Text = "Auto light", CustomMinimumSize = new Vector2(110, 34) };
        controls.AddChild(_animateLight);

        var shot = new Button { Text = "Shot", CustomMinimumSize = new Vector2(70, 34) };
        shot.Pressed += () => _ = CaptureScreenshot(0.0f, "screenshots");
        controls.AddChild(shot);

        var renderControls = new HBoxContainer
        {
            Name = "RenderControls",
            Position = new Vector2(15, 58),
            CustomMinimumSize = new Vector2(980, 36),
        };
        _uiLayer.AddChild(renderControls);

        _giModeSelect = new OptionButton { CustomMinimumSize = new Vector2(130, 34) };
        _giModeSelect.AddItem("Direct", (int)GiOutputMode.Direct);
        _giModeSelect.AddItem("Indirect", (int)GiOutputMode.Indirect);
        _giModeSelect.AddItem("Combined", (int)GiOutputMode.Combined);
        _giModeSelect.ItemSelected += index => SetGiMode((GiOutputMode)index);
        renderControls.AddChild(_giModeSelect);

        renderControls.AddChild(MakeLabel("Indirect"));
        _indirectSlider = MakeSlider(0.0, 3.0, 0.01);
        _indirectSlider.ValueChanged += value =>
        {
            _indirectIntensity = (float)value;
            if (_syncingControls)
            {
                return;
            }

            ApplyOutputMode();
        };
        renderControls.AddChild(_indirectSlider);

        renderControls.AddChild(MakeLabel("Azimuth"));
        _azimuthSlider = MakeSlider(-180.0, 180.0, 0.1);
        _azimuthSlider.ValueChanged += value =>
        {
            _lightAzimuthDeg = (float)value;
            ApplyManualLightAngles();
        };
        renderControls.AddChild(_azimuthSlider);

        renderControls.AddChild(MakeLabel("Elevation"));
        _elevationSlider = MakeSlider(-5.0, 89.0, 0.1);
        _elevationSlider.ValueChanged += value =>
        {
            _lightElevationDeg = (float)value;
            ApplyManualLightAngles();
        };
        renderControls.AddChild(_elevationSlider);

        renderControls.AddChild(MakeLabel("Light"));
        _lightIntensitySlider = MakeSlider(0.0, 20.0, 0.01);
        _lightIntensitySlider.ValueChanged += value =>
        {
            _lightIntensity = (float)value;
            ApplySunEnergy();
        };
        renderControls.AddChild(_lightIntensitySlider);

        renderControls.AddChild(MakeLabel("Speed"));
        _lightSpeedSlider = MakeSlider(0.1, 5.0, 0.01);
        _lightSpeedSlider.ValueChanged += value => _lightSpeed = (float)value;
        renderControls.AddChild(_lightSpeedSlider);

        var debugControls = new HBoxContainer
        {
            Name = "DebugControls",
            Position = new Vector2(15, 100),
            CustomMinimumSize = new Vector2(1040, 36),
        };
        _uiLayer.AddChild(debugControls);

        _surfelDebug = new CheckButton { Text = "Surfels", CustomMinimumSize = new Vector2(110, 34) };
        _surfelDebug.Toggled += SetSurfelPreviewEnabled;
        debugControls.AddChild(_surfelDebug);

        debugControls.AddChild(MakeLabel("Size"));
        _surfelSizeSlider = MakeSlider(0.005, 0.15, 0.001);
        _surfelSizeSlider.ValueChanged += value =>
        {
            _surfelPreviewSize = (float)value;
            if (_syncingControls)
            {
                return;
            }

            RebuildSurfelPreview();
        };
        debugControls.AddChild(_surfelSizeSlider);

        debugControls.AddChild(MakeLabel("Budget"));
        _surfelBudgetSlider = MakeSlider(512.0, 32768.0, 512.0);
        _surfelBudgetSlider.ValueChanged += value =>
        {
            _surfelPreviewBudget = Mathf.RoundToInt((float)value);
            if (_syncingControls)
            {
                return;
            }

            RebuildSurfelPreview();
            RebuildSurfelLights();
        };
        debugControls.AddChild(_surfelBudgetSlider);

        _surfelLightsToggle = new CheckButton { Text = "GI Lights", CustomMinimumSize = new Vector2(110, 34) };
        _surfelLightsToggle.Toggled += SetSurfelLightsEnabled;
        debugControls.AddChild(_surfelLightsToggle);

        debugControls.AddChild(MakeLabel("Count"));
        _surfelLightCountSlider = MakeSlider(0.0, 64.0, 1.0);
        _surfelLightCountSlider.ValueChanged += value =>
        {
            _surfelLightCount = Mathf.RoundToInt((float)value);
            if (_syncingControls)
            {
                return;
            }

            RebuildSurfelLights();
        };
        debugControls.AddChild(_surfelLightCountSlider);

        debugControls.AddChild(MakeLabel("Energy"));
        _surfelLightEnergySlider = MakeSlider(0.0, 2.0, 0.01);
        _surfelLightEnergySlider.ValueChanged += value =>
        {
            _surfelLightEnergy = (float)value;
            if (_syncingControls)
            {
                return;
            }

            RebuildSurfelLights();
        };
        debugControls.AddChild(_surfelLightEnergySlider);

        _statusLabel = new Label
        {
            Text = "",
            Position = new Vector2(15, 142),
            CustomMinimumSize = new Vector2(1040, 24),
            Modulate = new Color(0.92f, 0.92f, 0.92f, 0.88f),
        };
        _uiLayer.AddChild(_statusLabel);
    }

    private static Label MakeLabel(string text)
    {
        return new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(62, 34),
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private static HSlider MakeSlider(double min, double max, double step)
    {
        return new HSlider
        {
            MinValue = min,
            MaxValue = max,
            Step = step,
            CustomMinimumSize = new Vector2(100, 34),
        };
    }
}
