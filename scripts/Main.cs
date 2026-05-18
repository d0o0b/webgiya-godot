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
    private const string SurfelFaceMaskMeta = "surfel_face_mask";
    private const int FaceNegX = 1 << 0;
    private const int FacePosX = 1 << 1;
    private const int FaceNegY = 1 << 2;
    private const int FacePosY = 1 << 3;
    private const int FaceNegZ = 1 << 4;
    private const int FacePosZ = 1 << 5;
    private const int AllBoxFaces = FaceNegX | FacePosX | FaceNegY | FacePosY | FaceNegZ | FacePosZ;

    private enum GiOutputMode
    {
        Direct,
        Indirect,
        Combined,
    }

    private sealed class ScenePreset
    {
        public required string Id { get; init; }
        public required string Label { get; init; }
        public required string Kind { get; init; }
        public required string HdrPath { get; init; }
        public Vector3 CameraPosition { get; init; }
        public Vector3 CameraTarget { get; init; }
        public float LightIntensity { get; init; } = 2.0f;
        public float LightAzimuthDeg { get; init; } = 35.0f;
        public float LightElevationDeg { get; init; } = 50.0f;
        public float IndirectEnergy { get; init; } = 1.0f;
        public float AmbientEnergy { get; init; } = 0.55f;
    }

    private readonly struct SurfelSample
    {
        public SurfelSample(Vector3 position, Vector3 normal, Color color)
        {
            Position = position;
            Normal = normal;
            Color = color;
        }

        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Color Color { get; }
    }

    private readonly struct TriangleSurfaceSample
    {
        public TriangleSurfaceSample(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, float area)
        {
            A = a;
            B = b;
            C = c;
            Normal = normal;
            Area = area;
        }

        public Vector3 A { get; }
        public Vector3 B { get; }
        public Vector3 C { get; }
        public Vector3 Normal { get; }
        public float Area { get; }
    }

    private readonly List<ScenePreset> _presets = new()
    {
        new ScenePreset
        {
            Id = "cornell-box",
            Label = "Cornell Box",
            Kind = "cornell",
            HdrPath = "res://assets/exr/pizzo_pernice_puresky_2k.hdr",
            CameraPosition = new Vector3(0, 2.3f, 11),
            CameraTarget = new Vector3(0, 2.3f, 1),
        },
        new ScenePreset
        {
            Id = "leonardo",
            Label = "Leonardo",
            Kind = "leonardo",
            HdrPath = "res://assets/exr/hay_bales_1k.exr",
            CameraPosition = new Vector3(0, 3, 13),
            CameraTarget = new Vector3(0, 2, 0),
        },
        new ScenePreset
        {
            Id = "occlusion",
            Label = "Occlusion test",
            Kind = "occlusion",
            HdrPath = "res://assets/exr/hay_bales_1k.exr",
            CameraPosition = new Vector3(13, 3, 0),
            CameraTarget = new Vector3(0, 2, 0),
        },
        new ScenePreset
        {
            Id = "marble-bust",
            Label = "Marble Bust",
            Kind = "marble-bust",
            HdrPath = "res://assets/exr/kloppenheim_05_puresky_2k.hdr",
            CameraPosition = new Vector3(0, -0.3f, 3),
            CameraTarget = Vector3.Zero,
        },
        new ScenePreset
        {
            Id = "sponza",
            Label = "Sponza (Heavy)",
            Kind = "sponza",
            HdrPath = "res://assets/exr/pizzo_pernice_puresky_2k.hdr",
            CameraPosition = new Vector3(5, 4, -0.5f),
            CameraTarget = new Vector3(-1.6f, 3.8f, -0.6f),
            LightIntensity = 10.0f,
            LightAzimuthDeg = 9.6f,
            LightElevationDeg = 47.4f,
            IndirectEnergy = 1.7f,
            AmbientEnergy = 0.85f,
        },
        new ScenePreset
        {
            Id = "beast",
            Label = "Sponza with Beast",
            Kind = "beast",
            HdrPath = "res://assets/exr/qwantani_noon_puresky_1k.exr",
            CameraPosition = new Vector3(4.2f, 0.5f, -0.5f),
            CameraTarget = new Vector3(-1.6f, 2.8f, -0.6f),
            LightIntensity = 10.0f,
            LightAzimuthDeg = -7.7f,
            LightElevationDeg = 74.9f,
            IndirectEnergy = 1.4f,
            AmbientEnergy = 0.85f,
        },
    };

    private readonly Dictionary<string, Color> _textureAverageColorCache = new(StringComparer.Ordinal);
    private Camera3D _camera = null!;
    private DirectionalLight3D _sun = null!;
    private WorldEnvironment _worldEnvironment = null!;
    private Godot.Environment _environment = null!;
    private Node3D _sceneRoot = null!;
    private MultiMeshInstance3D _surfelPreview = null!;
    private Node3D _surfelLightRoot = null!;
    private CanvasLayer _uiLayer = null!;
    private OptionButton _sceneSelect = null!;
    private OptionButton _giModeSelect = null!;
    private Label _statusLabel = null!;
    private CheckButton _animateLight = null!;
    private CheckButton _surfelDebug = null!;
    private CheckButton _surfelLightsToggle = null!;
    private HSlider _indirectSlider = null!;
    private HSlider _azimuthSlider = null!;
    private HSlider _elevationSlider = null!;
    private HSlider _lightIntensitySlider = null!;
    private HSlider _lightSpeedSlider = null!;
    private HSlider _surfelSizeSlider = null!;
    private HSlider _surfelBudgetSlider = null!;
    private HSlider _surfelLightCountSlider = null!;
    private HSlider _surfelLightEnergySlider = null!;
    private int _currentPresetIndex;
    private Vector3 _target = Vector3.Zero;
    private float _yaw;
    private float _pitch;
    private float _distance = 8.0f;
    private bool _orbiting;
    private bool _panning;
    private bool _looking;
    private float _lightAnimationTime;
    private float _lightAzimuthDeg = 35.0f;
    private float _lightElevationDeg = 50.0f;
    private float _lightIntensity = 2.0f;
    private float _lightSpeed = 0.5f;
    private float _indirectIntensity = 1.0f;
    private float _presetAmbientEnergy = 0.55f;
    private float _presetIndirectEnergy = 1.0f;
    private GiOutputMode _giMode = GiOutputMode.Combined;
    private bool _syncingControls;
    private string _screenshotViewName = "default";
    private bool _surfelPreviewEnabled;
    private bool _surfelLightsEnabled;
    private bool _hideUiDuringScreenshots;
    private float _surfelPreviewSize = 0.035f;
    private int _surfelPreviewBudget = 8192;
    private int _surfelLightCount = 24;
    private float _surfelLightEnergy = 0.16f;
    private const float MaxScreenshotDelaySeconds = 5.0f;

    public override void _Ready()
    {
        BuildWorld();
        BuildUi();

        var args = ParseArguments();
        _surfelPreviewEnabled = args.ContainsKey("surfel-debug");
        _surfelLightsEnabled = ParseBool(args.GetValueOrDefault("surfel-lights"), false) && !args.ContainsKey("no-surfel-lights");
        _surfelLightCount = Mathf.Clamp(ParseInt(args.GetValueOrDefault("surfel-light-count"), _surfelLightCount), 0, 64);
        _surfelLightEnergy = Mathf.Clamp(ParseFloat(args.GetValueOrDefault("surfel-light-energy"), _surfelLightEnergy), 0.0f, 2.0f);
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

    private void BuildWorld()
    {
        _sceneRoot = new Node3D { Name = "SceneRoot" };
        AddChild(_sceneRoot);

        _camera = new Camera3D
        {
            Name = "Camera",
            Current = true,
            Fov = 60.0f,
            Near = 0.1f,
            Far = 2000.0f,
        };
        AddChild(_camera);

        _sun = new DirectionalLight3D
        {
            Name = "DirectionalLight",
            ShadowEnabled = true,
            LightEnergy = 2.0f,
        };
        _sun.Set("directional_shadow_mode", 2);
        _sun.Set("directional_shadow_max_distance", 80.0f);
        _sun.Set("shadow_bias", 0.02f);
        _sun.Set("shadow_normal_bias", 1.0f);
        AddChild(_sun);

        _worldEnvironment = new WorldEnvironment { Name = "WorldEnvironment" };
        AddChild(_worldEnvironment);

        var surfelMaterial = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _surfelPreview = new MultiMeshInstance3D
        {
            Name = "SurfelPreview",
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = surfelMaterial,
        };
        AddChild(_surfelPreview);

        _surfelLightRoot = new Node3D { Name = "SurfelLights" };
        AddChild(_surfelLightRoot);
    }

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
        _environment.Set("ambient_light_sky_contribution", 1.0f);
        _environment.Set("reflected_light_source", 2);
        _environment.Set("tonemap_mode", 3);
        _environment.Set("tonemap_exposure", 0.85f);
        _environment.Set("tonemap_white", 1.0f);
        _environment.Set("ssao_enabled", true);
        _environment.Set("ssao_radius", 2.0f);
        _environment.Set("ssao_intensity", 1.6f);
        _environment.Set("ssil_enabled", true);
        _environment.Set("ssil_radius", 5.0f);
        _environment.Set("sdfgi_enabled", true);
        _environment.Set("sdfgi_bounce_feedback", 0.65f);
        _environment.Set("sdfgi_read_sky_light", true);
        _environment.Set("glow_enabled", true);
        _environment.Set("glow_intensity", 0.08f);

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
        _environment.Set("ssil_intensity", _giMode == GiOutputMode.Direct ? 0.0f : indirectScale);
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

    private void ClearScene()
    {
        foreach (var child in _sceneRoot.GetChildren())
        {
            _sceneRoot.RemoveChild(child);
            child.QueueFree();
        }

        ClearSurfelLights();
    }

    private void BuildCornellScene()
    {
        var group = new Node3D { Name = "CornellBox" };
        group.Position = new Vector3(0, -0.5f, 0);
        group.Scale = new Vector3(4, 4, 4);
        _sceneRoot.AddChild(group);

        const float boxWidth = 2.0f;
        const float boxDepth = 2.0f;
        const float boxHeight = 1.5f;
        const float wall = 0.02f;
        var white = MakeMaterial(new Color(1, 1, 1), 0.95f);
        var red = MakeMaterial(new Color(1, 0, 0), 0.9f);
        var green = MakeMaterial(new Color(0, 1, 0), 0.9f);

        AddBox(group, "Floor", new Vector3(boxWidth + wall * 2, wall, boxDepth + wall), new Vector3(0, -wall * 0.5f, -wall * 0.5f), Vector3.Zero, white, FacePosY);
        AddBox(group, "LeftWall", new Vector3(wall, boxHeight, boxDepth), new Vector3(-boxWidth * 0.5f - wall * 0.5f, boxHeight * 0.5f, 0), Vector3.Zero, red, FacePosX);
        AddBox(group, "RightWall", new Vector3(wall, boxHeight, boxDepth), new Vector3(boxWidth * 0.5f + wall * 0.5f, boxHeight * 0.5f, 0), Vector3.Zero, green, FaceNegX);
        AddBox(group, "BackWall", new Vector3(boxWidth + wall * 2, boxHeight, wall), new Vector3(0, boxHeight * 0.5f, -boxDepth * 0.5f - wall * 0.5f), Vector3.Zero, white, FacePosZ);

        var panelWidth = 0.75f + wall;
        var panelDepth = boxDepth + wall;
        var ceilingY = boxHeight + wall * 0.5f;
        var ceilingX = boxWidth * 0.5f - panelWidth * 0.5f + wall;
        AddBox(group, "CeilingLeft", new Vector3(panelWidth, wall, panelDepth), new Vector3(-ceilingX, ceilingY, -0.5f * wall), Vector3.Zero, white, FaceNegY);
        AddBox(group, "CeilingRight", new Vector3(panelWidth, wall, panelDepth), new Vector3(ceilingX, ceilingY, -0.5f * wall), Vector3.Zero, white, FaceNegY);
        AddBox(group, "TallBox", new Vector3(0.5f, 0.7f, 0.5f), new Vector3(-0.3f, 0.35f, -0.2f), new Vector3(0, Mathf.Pi * 0.25f, 0), white, AllBoxFaces & ~FaceNegY);
        AddBox(group, "ShortBox", new Vector3(0.4f, 0.4f, 0.4f), new Vector3(0.4f, 0.2f, 0.4f), new Vector3(0, Mathf.Pi * -0.1f, 0), white, AllBoxFaces & ~FaceNegY);
    }

    private void BuildMarbleBustScene()
    {
        var group = new Node3D { Name = "MarbleBustScene", Position = new Vector3(0, -0.9f, 0), Scale = new Vector3(3, 3, 3) };
        _sceneRoot.AddChild(group);

        var white = MakeMaterial(new Color(0.67f, 0.67f, 0.67f), 0.85f);
        var red = MakeMaterial(new Color(1, 0, 0), 0.9f);
        var blue = MakeMaterial(new Color(0, 0, 1), 0.9f);
        const float wall = 0.02f;

        AddBox(group, "Floor", new Vector3(1.0f + wall * 2, wall, 1.0f + wall), new Vector3(0, -wall * 0.5f, 0), Vector3.Zero, white, FacePosY);
        AddBox(group, "Back", new Vector3(1.0f + wall * 2, wall, 1.0f / 1.5f), new Vector3(0, 0.33f, -0.5f), new Vector3(Mathf.Pi / 2.0f, 0, 0), white, FacePosY);
        AddBox(group, "RedReflector", new Vector3(0.3f, wall, 0.3f), new Vector3(-0.2f, 0.4f, 0), new Vector3(Mathf.Pi / 2.0f, 0, -Mathf.Pi / 4.0f), red);
        AddBox(group, "BlueReflector", new Vector3(0.3f, wall, 0.3f), new Vector3(0.2f, 0.4f, 0), new Vector3(Mathf.Pi / 2.0f, 0, Mathf.Pi / 4.0f), blue);

        AddImportedScene("res://assets/models/marble_bust/marble_bust_01_4k.gltf", Vector3.Zero, Vector3.Zero, Vector3.One, group);
    }

    private Node3D? AddImportedScene(string[] paths, Vector3 position, Vector3 rotation, Vector3 scale, Node3D? parent = null)
    {
        foreach (var path in paths)
        {
            var instance = AddImportedScene(path, position, rotation, scale, parent, warnOnFailure: false);
            if (instance != null)
            {
                return instance;
            }
        }

        SetStatus($"Import failed: {string.Join(", ", paths)}");
        GD.PushWarning($"Could not load any fallback: {string.Join(", ", paths)}");
        return null;
    }

    private MeshInstance3D AddBox(Node3D parent, string name, Vector3 size, Vector3 position, Vector3 rotation, Material material, int surfelFaceMask = AllBoxFaces)
    {
        var mesh = new MeshInstance3D
        {
            Name = name,
            Mesh = new BoxMesh { Size = size },
            Position = position,
            Rotation = rotation,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.On,
        };
        mesh.SetMeta(SurfelFaceMaskMeta, surfelFaceMask);
        mesh.Set("gi_mode", 1);
        parent.AddChild(mesh);
        return mesh;
    }

    private StandardMaterial3D MakeMaterial(Color color, float roughness)
    {
        return new StandardMaterial3D
        {
            AlbedoColor = color,
            Roughness = roughness,
            Metallic = 0.0f,
        };
    }

    private Node3D? AddImportedScene(string path, Vector3 position, Vector3 rotation, Vector3 scale, Node3D? parent = null, bool warnOnFailure = true)
    {
        var packed = ResourceLoader.Load<PackedScene>(path);
        if (packed == null)
        {
            if (warnOnFailure)
            {
                SetStatus($"Import failed: {path}");
                GD.PushWarning($"Could not load {path}");
            }
            return null;
        }

        var instance = packed.Instantiate<Node3D>();
        instance.Name = System.IO.Path.GetFileNameWithoutExtension(path);
        instance.Position = position;
        instance.Rotation = rotation;
        instance.Scale = scale;
        (parent ?? _sceneRoot).AddChild(instance);
        ConfigureGeometry(instance);
        return instance;
    }

    private void ConfigureGeometry(Node node)
    {
        if (node is MeshInstance3D mesh)
        {
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
            mesh.Set("gi_mode", 1);
        }

        foreach (var child in node.GetChildren())
        {
            ConfigureGeometry(child);
        }
    }

    private void SetSurfelPreviewEnabled(bool enabled)
    {
        _surfelPreviewEnabled = enabled;
        if (_surfelPreview != null)
        {
            _surfelPreview.Visible = enabled;
        }

        if (!_syncingControls && enabled)
        {
            RebuildSurfelPreview();
        }
    }

    private void SetSurfelLightsEnabled(bool enabled)
    {
        _surfelLightsEnabled = enabled;
        if (_syncingControls)
        {
            return;
        }

        RebuildSurfelLights();
    }

    private void RebuildSurfelPreview()
    {
        if (_surfelPreview == null)
        {
            return;
        }

        if (!_surfelPreviewEnabled)
        {
            _surfelPreview.Multimesh = null;
            _surfelPreview.Visible = false;
            return;
        }

        var samples = CreateSurfelSamples(_surfelPreviewBudget);

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = new BoxMesh { Size = Vector3.One },
            InstanceCount = samples.Count,
            VisibleInstanceCount = samples.Count,
        };

        var basis = Basis.Identity.Scaled(Vector3.One * _surfelPreviewSize);
        for (var i = 0; i < samples.Count; i++)
        {
            multimesh.SetInstanceTransform(i, new Transform3D(basis, samples[i].Position));
            multimesh.SetInstanceColor(i, samples[i].Color);
        }

        _surfelPreview.Multimesh = multimesh;
        _surfelPreview.Visible = true;
        GD.Print($"Built surfel preview: {samples.Count} surfels");
    }

    private void RebuildSurfelLights()
    {
        if (_surfelLightRoot == null)
        {
            return;
        }

        ClearSurfelLights();
        if (!_surfelLightsEnabled || _surfelLightCount <= 0 || _giMode == GiOutputMode.Direct)
        {
            _surfelLightRoot.Visible = false;
            return;
        }

        var samples = CreateSurfelSamples(Mathf.Clamp(Mathf.Max(_surfelPreviewBudget, _surfelLightCount * 64), 256, 32768));
        if (samples.Count == 0)
        {
            _surfelLightRoot.Visible = false;
            return;
        }

        var selected = SelectSurfelLightSamples(samples, _surfelLightCount);
        if (selected.Count == 0)
        {
            _surfelLightRoot.Visible = false;
            return;
        }

        var range = EstimateSurfelLightRange(samples);
        var center = EstimateSurfelCenter(samples);
        var energyBase = _surfelLightEnergy * Mathf.Max(0.0f, _indirectIntensity) / Mathf.Sqrt(Mathf.Max(1.0f, selected.Count));
        for (var i = 0; i < selected.Count; i++)
        {
            var sample = selected[i];
            var colorfulness = Colorfulness(sample.Color);
            var normal = sample.Normal.LengthSquared() > 0.001f ? sample.Normal.Normalized() : (center - sample.Position).Normalized();
            var light = new OmniLight3D
            {
                Name = $"SurfelLight{i + 1:00}",
                Position = sample.Position + normal * Mathf.Min(0.18f, range * 0.08f),
                LightColor = sample.Color,
                LightEnergy = energyBase * Mathf.Lerp(0.85f, 1.55f, Mathf.Clamp(colorfulness, 0.0f, 1.0f)),
                LightSpecular = 0.0f,
                ShadowEnabled = false,
                OmniRange = range,
                OmniAttenuation = 0.8f,
            };
            _surfelLightRoot.AddChild(light);
        }

        _surfelLightRoot.Visible = true;
        GD.Print($"Built surfel lights: {selected.Count} lights, range {range:0.00}, energy {energyBase:0.000}");
    }

    private void ClearSurfelLights()
    {
        if (_surfelLightRoot == null)
        {
            return;
        }

        foreach (var child in _surfelLightRoot.GetChildren())
        {
            _surfelLightRoot.RemoveChild(child);
            child.QueueFree();
        }
    }

    private List<SurfelSample> CreateSurfelSamples(int budget)
    {
        budget = Mathf.Clamp(budget, 1, 32768);
        var samples = new List<SurfelSample>(budget);
        var meshCount = Mathf.Max(1, CountMeshInstances(_sceneRoot));
        CollectSurfelSamples(_sceneRoot, samples, budget, meshCount);
        return samples;
    }

    private static List<SurfelSample> SelectSurfelLightSamples(List<SurfelSample> samples, int count)
    {
        count = Mathf.Clamp(count, 0, 64);
        if (count == 0 || samples.Count == 0)
        {
            return new List<SurfelSample>();
        }

        var center = EstimateSurfelCenter(samples);
        var candidates = samples
            .Select(sample => new { Sample = sample, Score = Colorfulness(sample.Color) + Luminance(sample.Color) * 0.15f })
            .Where(candidate => candidate.Score > 0.08f && IsInwardFacing(candidate.Sample, center))
            .OrderByDescending(candidate => candidate.Score)
            .ToList();

        if (candidates.Count < count)
        {
            candidates = samples
                .Select(sample => new { Sample = sample, Score = Colorfulness(sample.Color) + Luminance(sample.Color) * 0.15f })
                .Where(candidate => candidate.Score > 0.08f)
                .OrderByDescending(candidate => candidate.Score)
                .ToList();
        }
        var colored = new List<SurfelSample>(count);
        var minSpacing = EstimateSurfelLightRange(samples) * 0.45f;
        var minSpacingSquared = minSpacing * minSpacing;
        foreach (var candidate in candidates)
        {
            if (colored.All(sample => sample.Position.DistanceSquaredTo(candidate.Sample.Position) >= minSpacingSquared))
            {
                colored.Add(candidate.Sample);
                if (colored.Count >= count)
                {
                    return colored;
                }
            }
        }

        if (colored.Count >= count)
        {
            return colored;
        }

        var fallbackStep = Math.Max(1, samples.Count / Math.Max(1, count - colored.Count));
        for (var i = fallbackStep / 2; i < samples.Count && colored.Count < count; i += fallbackStep)
        {
            colored.Add(samples[i]);
        }

        return colored;
    }

    private static float EstimateSurfelLightRange(List<SurfelSample> samples)
    {
        var min = samples[0].Position;
        var max = samples[0].Position;
        for (var i = 1; i < samples.Count; i++)
        {
            min = min.Min(samples[i].Position);
            max = max.Max(samples[i].Position);
        }

        return Mathf.Clamp((max - min).Length() * 0.12f, 1.4f, 18.0f);
    }

    private static Vector3 EstimateSurfelCenter(List<SurfelSample> samples)
    {
        var min = samples[0].Position;
        var max = samples[0].Position;
        for (var i = 1; i < samples.Count; i++)
        {
            min = min.Min(samples[i].Position);
            max = max.Max(samples[i].Position);
        }

        return (min + max) * 0.5f;
    }

    private static bool IsInwardFacing(SurfelSample sample, Vector3 center)
    {
        if (sample.Normal.LengthSquared() <= 0.001f)
        {
            return true;
        }

        var toCenter = center - sample.Position;
        return toCenter.LengthSquared() <= 0.001f || sample.Normal.Normalized().Dot(toCenter.Normalized()) > 0.15f;
    }

    private static float Colorfulness(Color color)
    {
        var max = Mathf.Max(color.R, Mathf.Max(color.G, color.B));
        var min = Mathf.Min(color.R, Mathf.Min(color.G, color.B));
        return max - min;
    }

    private static float Luminance(Color color)
    {
        return color.R * 0.2126f + color.G * 0.7152f + color.B * 0.0722f;
    }

    private void CollectSurfelSamples(Node node, List<SurfelSample> samples, int budget, int meshCount)
    {
        if (samples.Count >= budget)
        {
            return;
        }

        if (node is MeshInstance3D meshInstance && meshInstance.Mesh != null && meshInstance.Visible)
        {
            CollectMeshSurfelSamples(meshInstance, samples, budget, meshCount);
        }

        foreach (var child in node.GetChildren())
        {
            CollectSurfelSamples(child, samples, budget, meshCount);
            if (samples.Count >= budget)
            {
                return;
            }
        }
    }

    private void CollectMeshSurfelSamples(MeshInstance3D meshInstance, List<SurfelSample> samples, int budget, int meshCount)
    {
        var mesh = meshInstance.Mesh;
        var perMeshTarget = Mathf.Max(32, budget / Math.Max(1, meshCount));
        if (mesh is BoxMesh box)
        {
            CollectBoxSurfelSamples(meshInstance, box, samples, budget, perMeshTarget);
            return;
        }

        var surfaceCount = mesh.GetSurfaceCount();
        if (surfaceCount <= 0)
        {
            return;
        }

        var perSurfaceTarget = Mathf.Max(16, perMeshTarget / Math.Max(1, surfaceCount));
        for (var surface = 0; surface < surfaceCount && samples.Count < budget; surface++)
        {
            var arrays = mesh.SurfaceGetArrays(surface);
            if (arrays.Count <= (int)Mesh.ArrayType.Vertex)
            {
                continue;
            }

            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (vertices.Length == 0)
            {
                continue;
            }

            var color = GetMeshSurfaceColor(meshInstance, surface);
            var indices = arrays.Count > (int)Mesh.ArrayType.Index
                ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array()
                : Array.Empty<int>();
            var transform = meshInstance.GlobalTransform;
            var triangles = BuildTriangleSamples(vertices, indices, transform);
            if (triangles.Count == 0)
            {
                continue;
            }

            var totalArea = triangles.Sum(triangle => triangle.Area);
            if (totalArea <= 0.0f)
            {
                continue;
            }

            AddTriangleAreaSamples(triangles, color, totalArea, perSurfaceTarget, samples, budget);
        }
    }

    private void CollectBoxSurfelSamples(MeshInstance3D meshInstance, BoxMesh box, List<SurfelSample> samples, int budget, int perMeshTarget)
    {
        var half = box.Size * 0.5f;
        var transform = meshInstance.GlobalTransform;
        var color = GetMeshSurfaceColor(meshInstance, 0);
        var faceMask = meshInstance.HasMeta(SurfelFaceMaskMeta)
            ? (int)meshInstance.GetMeta(SurfelFaceMaskMeta).AsInt32()
            : AllBoxFaces;
        var faces = BuildBoxFaces(box.Size, faceMask);
        var totalArea = faces.Sum(face => face.Area);
        if (totalArea <= 0.0f)
        {
            return;
        }

        foreach (var face in faces)
        {
            if (samples.Count >= budget)
            {
                return;
            }

            var faceTarget = Mathf.Max(1, Mathf.RoundToInt(perMeshTarget * face.Area / totalArea));
            AddBoxFaceSamples(face, half, transform, color, faceTarget, samples, budget);
        }
    }

    private readonly struct BoxFace
    {
        public BoxFace(int index, Vector3 normal, float width, float height, float area)
        {
            Index = index;
            Normal = normal;
            Width = width;
            Height = height;
            Area = area;
        }

        public int Index { get; }
        public Vector3 Normal { get; }
        public float Width { get; }
        public float Height { get; }
        public float Area { get; }
    }

    private static List<BoxFace> BuildBoxFaces(Vector3 size, int faceMask)
    {
        var faces = new List<BoxFace>(6);
        AddBoxFace(faces, faceMask, 0, FaceNegX, new Vector3(-1, 0, 0), size.Z, size.Y);
        AddBoxFace(faces, faceMask, 1, FacePosX, new Vector3(1, 0, 0), size.Z, size.Y);
        AddBoxFace(faces, faceMask, 2, FaceNegY, new Vector3(0, -1, 0), size.X, size.Z);
        AddBoxFace(faces, faceMask, 3, FacePosY, new Vector3(0, 1, 0), size.X, size.Z);
        AddBoxFace(faces, faceMask, 4, FaceNegZ, new Vector3(0, 0, -1), size.X, size.Y);
        AddBoxFace(faces, faceMask, 5, FacePosZ, new Vector3(0, 0, 1), size.X, size.Y);
        return faces;
    }

    private static void AddBoxFace(List<BoxFace> faces, int faceMask, int index, int bit, Vector3 normal, float width, float height)
    {
        if ((faceMask & bit) == 0)
        {
            return;
        }

        faces.Add(new BoxFace(index, normal, Mathf.Max(0.0001f, width), Mathf.Max(0.0001f, height), Mathf.Max(0.0001f, width * height)));
    }

    private static void AddBoxFaceSamples(BoxFace face, Vector3 half, Transform3D transform, Color color, int target, List<SurfelSample> samples, int budget)
    {
        var aspect = Mathf.Max(0.001f, face.Width / face.Height);
        var columns = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(target * aspect)));
        var rows = Mathf.Max(1, Mathf.CeilToInt(target / (float)columns));
        var normal = (transform.Basis * face.Normal).Normalized();

        for (var y = 0; y < rows && samples.Count < budget; y++)
        {
            var v = (y + 0.5f) / rows;
            for (var x = 0; x < columns && samples.Count < budget; x++)
            {
                var u = (x + 0.5f) / columns;
                var local = GetBoxFaceLocalPoint(face.Index, half, u, v);
                samples.Add(new SurfelSample(transform * local, normal, color));
            }
        }
    }

    private static Vector3 GetBoxFaceLocalPoint(int face, Vector3 half, float u, float v)
    {
        var px = Mathf.Lerp(-half.X, half.X, u);
        var py = Mathf.Lerp(-half.Y, half.Y, v);
        var pz = Mathf.Lerp(-half.Z, half.Z, u);
        var qz = Mathf.Lerp(-half.Z, half.Z, v);

        return face switch
        {
            0 => new Vector3(-half.X, py, qz),
            1 => new Vector3(half.X, py, qz),
            2 => new Vector3(px, -half.Y, qz),
            3 => new Vector3(px, half.Y, qz),
            4 => new Vector3(px, py, -half.Z),
            _ => new Vector3(px, py, half.Z),
        };
    }

    private static List<TriangleSurfaceSample> BuildTriangleSamples(Vector3[] vertices, int[] indices, Transform3D transform)
    {
        var triangleCount = indices.Length >= 3 ? indices.Length / 3 : vertices.Length / 3;
        var triangles = new List<TriangleSurfaceSample>(triangleCount);
        for (var triangle = 0; triangle < triangleCount; triangle++)
        {
            var i0 = indices.Length >= 3 ? indices[triangle * 3] : triangle * 3;
            var i1 = indices.Length >= 3 ? indices[triangle * 3 + 1] : triangle * 3 + 1;
            var i2 = indices.Length >= 3 ? indices[triangle * 3 + 2] : triangle * 3 + 2;
            if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
            {
                continue;
            }

            var a = transform * vertices[i0];
            var b = transform * vertices[i1];
            var c = transform * vertices[i2];
            var cross = (b - a).Cross(c - a);
            var area = cross.Length() * 0.5f;
            if (area <= 0.000001f)
            {
                continue;
            }

            triangles.Add(new TriangleSurfaceSample(a, b, c, cross.Normalized(), area));
        }

        return triangles;
    }

    private static void AddTriangleAreaSamples(List<TriangleSurfaceSample> triangles, Color color, float totalArea, int target, List<SurfelSample> samples, int budget)
    {
        target = Mathf.Max(1, target);
        var triangleIndex = 0;
        var cumulative = triangles[0].Area;
        for (var sampleIndex = 0; sampleIndex < target && samples.Count < budget; sampleIndex++)
        {
            var areaPoint = (sampleIndex + 0.5f) * totalArea / target;
            while (triangleIndex < triangles.Count - 1 && cumulative < areaPoint)
            {
                triangleIndex++;
                cumulative += triangles[triangleIndex].Area;
            }

            var triangle = triangles[triangleIndex];
            var u = RadicalInverse(sampleIndex + 1, 2);
            var v = RadicalInverse(sampleIndex + 1, 3);
            var sqrtU = Mathf.Sqrt(u);
            var b0 = 1.0f - sqrtU;
            var b1 = sqrtU * (1.0f - v);
            var b2 = sqrtU * v;
            var position = triangle.A * b0 + triangle.B * b1 + triangle.C * b2;
            samples.Add(new SurfelSample(position, triangle.Normal, color));
        }
    }

    private static float RadicalInverse(int index, int radix)
    {
        var factor = 1.0f / radix;
        var result = 0.0f;
        while (index > 0)
        {
            result += factor * (index % radix);
            index /= radix;
            factor /= radix;
        }

        return result;
    }

    private int CountMeshInstances(Node node)
    {
        var count = node is MeshInstance3D meshInstance && meshInstance.Mesh != null && meshInstance.Visible ? 1 : 0;
        foreach (var child in node.GetChildren())
        {
            count += CountMeshInstances(child);
        }

        return count;
    }

    private Color GetMeshSurfaceColor(MeshInstance3D meshInstance, int surface)
    {
        var material =
            meshInstance.MaterialOverride ??
            meshInstance.GetSurfaceOverrideMaterial(surface) ??
            meshInstance.Mesh?.SurfaceGetMaterial(surface);

        if (material is StandardMaterial3D standard)
        {
            var color = standard.AlbedoColor * GetTextureAverageColor(standard.AlbedoTexture);
            color.A = 1.0f;
            return color;
        }

        return new Color(0.55f, 0.78f, 1.0f, 1.0f);
    }

    private Color GetTextureAverageColor(Texture2D? texture)
    {
        if (texture == null)
        {
            return Colors.White;
        }

        var cacheKey = string.IsNullOrWhiteSpace(texture.ResourcePath)
            ? texture.GetInstanceId().ToString(CultureInfo.InvariantCulture)
            : texture.ResourcePath;
        if (_textureAverageColorCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var image = texture.GetImage();
        if (image == null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
        {
            _textureAverageColorCache[cacheKey] = Colors.White;
            return Colors.White;
        }

        image.Convert(Image.Format.Rgba8);
        var width = image.GetWidth();
        var height = image.GetHeight();
        var samples = 0;
        var color = Vector3.Zero;
        const int grid = 8;
        for (var y = 0; y < grid; y++)
        {
            var py = Mathf.Clamp(Mathf.RoundToInt((y + 0.5f) * height / grid), 0, height - 1);
            for (var x = 0; x < grid; x++)
            {
                var px = Mathf.Clamp(Mathf.RoundToInt((x + 0.5f) * width / grid), 0, width - 1);
                var pixel = image.GetPixel(px, py);
                color += new Vector3(pixel.R, pixel.G, pixel.B);
                samples++;
            }
        }

        var average = samples > 0
            ? new Color(color.X / samples, color.Y / samples, color.Z / samples, 1.0f)
            : Colors.White;
        _textureAverageColorCache[cacheKey] = average;
        return average;
    }

    private void SetOrbitFromCamera(Vector3 position, Vector3 target)
    {
        _target = target;
        var offset = position - target;
        _distance = Mathf.Max(0.25f, offset.Length());
        _yaw = Mathf.Atan2(offset.X, offset.Z);
        _pitch = Mathf.Asin(offset.Y / _distance);
        UpdateCameraTransform();
    }

    private void ResetCameraToCurrentPreset()
    {
        var preset = _presets[_currentPresetIndex];
        SetOrbitFromCamera(preset.CameraPosition, preset.CameraTarget);
    }

    private void UpdateCameraTransform()
    {
        var cp = Mathf.Cos(_pitch);
        var offset = new Vector3(
            _distance * cp * Mathf.Sin(_yaw),
            _distance * Mathf.Sin(_pitch),
            _distance * cp * Mathf.Cos(_yaw));
        _camera.Position = _target + offset;
        _camera.LookAt(_target, Vector3.Up);
    }

    private void UpdateOrbitFromCurrentCamera()
    {
        var offset = _camera.Position - _target;
        _distance = Mathf.Max(0.25f, offset.Length());
        _yaw = Mathf.Atan2(offset.X, offset.Z);
        _pitch = Mathf.Asin(Mathf.Clamp(offset.Y / _distance, -1.0f, 1.0f));
    }

    private void LookCamera(Vector2 pixels)
    {
        _yaw -= pixels.X * 0.0045f;
        _pitch = Mathf.Clamp(_pitch + pixels.Y * 0.0045f, -1.45f, 1.45f);

        var cp = Mathf.Cos(_pitch);
        var forward = new Vector3(
            -cp * Mathf.Sin(_yaw),
            -Mathf.Sin(_pitch),
            -cp * Mathf.Cos(_yaw)).Normalized();

        _target = _camera.Position + forward * _distance;
        _camera.LookAt(_target, Vector3.Up);
    }

    private void PanCamera(Vector2 pixels)
    {
        var right = _camera.GlobalTransform.Basis.X;
        var up = _camera.GlobalTransform.Basis.Y;
        var scale = _distance * 0.0015f;
        _target += (-right * pixels.X + up * pixels.Y) * scale;
        UpdateCameraTransform();
    }

    private void UpdateKeyboardMovement(float delta)
    {
        var movement = Vector3.Zero;
        var right = _camera.GlobalTransform.Basis.X;
        var forward = -_camera.GlobalTransform.Basis.Z;
        var up = Vector3.Up;

        if (Input.IsKeyPressed(Key.W)) movement += forward;
        if (Input.IsKeyPressed(Key.S)) movement -= forward;
        if (Input.IsKeyPressed(Key.D)) movement += right;
        if (Input.IsKeyPressed(Key.A)) movement -= right;
        if (Input.IsKeyPressed(Key.E)) movement += up;
        if (Input.IsKeyPressed(Key.Q)) movement -= up;

        if (movement.LengthSquared() > 0.0001f)
        {
            var speed = (_distance * 0.65f + 1.0f) * (Input.IsKeyPressed(Key.Shift) ? 4.0f : 1.0f);
            var deltaMove = movement.Normalized() * speed * delta;
            _camera.Position += deltaMove;
            _target += deltaMove;
            UpdateOrbitFromCurrentCamera();
            _camera.LookAt(_target, Vector3.Up);
        }
    }

    private async Task RunScreenshotSequence(Dictionary<string, string> args)
    {
        var dir = args.TryGetValue("screenshot-dir", out var dirArg) ? dirArg : "screenshots";
        var delay = ParseFloat(args.GetValueOrDefault("screenshot-delay"), 1.0f);
        delay = Mathf.Clamp(delay, 0.0f, MaxScreenshotDelaySeconds);
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
        delay = Mathf.Clamp(delay, 0.0f, MaxScreenshotDelaySeconds);
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

    private void RunImageComparison(Dictionary<string, string> args)
    {
        if (!args.TryGetValue("compare-reference", out var referenceArg) ||
            !args.TryGetValue("compare-candidate", out var candidateArg))
        {
            GD.PushError("Image comparison requires --compare-reference=<path> and --compare-candidate=<path>.");
            return;
        }

        var referencePath = ResolveProjectPath(referenceArg);
        var candidatePath = ResolveProjectPath(candidateArg);
        var diffPath = args.TryGetValue("compare-diff", out var diffArg) ? ResolveProjectPath(diffArg) : null;
        var reportPath = args.TryGetValue("compare-report", out var reportArg) ? ResolveProjectPath(reportArg) : null;

        var reference = new Image();
        var referenceError = reference.Load(referencePath);
        if (referenceError != Error.Ok)
        {
            GD.PushError($"Could not load reference image '{referencePath}': {referenceError}");
            return;
        }

        var candidate = new Image();
        var candidateError = candidate.Load(candidatePath);
        if (candidateError != Error.Ok)
        {
            GD.PushError($"Could not load candidate image '{candidatePath}': {candidateError}");
            return;
        }

        var result = CompareImages(reference, candidate, diffPath);
        var json = BuildCompareReportJson(referencePath, candidatePath, diffPath, result);
        GD.Print(json);

        if (!string.IsNullOrWhiteSpace(reportPath))
        {
            var directory = System.IO.Path.GetDirectoryName(reportPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(reportPath, json, Encoding.UTF8);
        }
    }

    private sealed class CompareResult
    {
        public required int Width { get; init; }
        public required int Height { get; init; }
        public required bool ResizedCandidate { get; init; }
        public required double MeanAbsoluteError { get; init; }
        public required double RootMeanSquareError { get; init; }
        public required double MaxError { get; init; }
        public required double PercentPixelsOver02 { get; init; }
        public required double PercentPixelsOver05 { get; init; }
        public required double PercentPixelsOver10 { get; init; }
    }

    private static CompareResult CompareImages(Image reference, Image candidate, string? diffPath)
    {
        reference.Convert(Image.Format.Rgba8);
        candidate.Convert(Image.Format.Rgba8);

        var width = reference.GetWidth();
        var height = reference.GetHeight();
        var resizedCandidate = candidate.GetWidth() != width || candidate.GetHeight() != height;
        if (resizedCandidate)
        {
            candidate.Resize(width, height, Image.Interpolation.Lanczos);
        }

        Image? diff = null;
        if (!string.IsNullOrWhiteSpace(diffPath))
        {
            diff = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        }

        var pixelCount = width * height;
        var channelCount = pixelCount * 3.0;
        double absoluteSum = 0.0;
        double squareSum = 0.0;
        double maxError = 0.0;
        var over02 = 0;
        var over05 = 0;
        var over10 = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var a = reference.GetPixel(x, y);
                var b = candidate.GetPixel(x, y);
                var dr = Math.Abs(a.R - b.R);
                var dg = Math.Abs(a.G - b.G);
                var db = Math.Abs(a.B - b.B);
                var pixelError = Math.Max(dr, Math.Max(dg, db));

                absoluteSum += dr + dg + db;
                squareSum += dr * dr + dg * dg + db * db;
                maxError = Math.Max(maxError, pixelError);

                if (pixelError > 0.02f) over02++;
                if (pixelError > 0.05f) over05++;
                if (pixelError > 0.10f) over10++;

                diff?.SetPixel(x, y, new Color(
                    Mathf.Clamp(dr * 6.0f, 0.0f, 1.0f),
                    Mathf.Clamp(dg * 6.0f, 0.0f, 1.0f),
                    Mathf.Clamp(db * 6.0f, 0.0f, 1.0f),
                    1.0f));
            }
        }

        if (diff != null && !string.IsNullOrWhiteSpace(diffPath))
        {
            var directory = System.IO.Path.GetDirectoryName(diffPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var error = diff.SavePng(diffPath);
            if (error != Error.Ok)
            {
                GD.PushError($"Could not save diff image '{diffPath}': {error}");
            }
        }

        return new CompareResult
        {
            Width = width,
            Height = height,
            ResizedCandidate = resizedCandidate,
            MeanAbsoluteError = absoluteSum / channelCount,
            RootMeanSquareError = Math.Sqrt(squareSum / channelCount),
            MaxError = maxError,
            PercentPixelsOver02 = over02 * 100.0 / pixelCount,
            PercentPixelsOver05 = over05 * 100.0 / pixelCount,
            PercentPixelsOver10 = over10 * 100.0 / pixelCount,
        };
    }

    private static string BuildCompareReportJson(string referencePath, string candidatePath, string? diffPath, CompareResult result)
    {
        return $$"""
        {
          "reference": "{{JsonEscape(referencePath)}}",
          "candidate": "{{JsonEscape(candidatePath)}}",
          "diff": {{(diffPath == null ? "null" : $"\"{JsonEscape(diffPath)}\"")}},
          "width": {{result.Width}},
          "height": {{result.Height}},
          "resizedCandidate": {{result.ResizedCandidate.ToString().ToLowerInvariant()}},
          "meanAbsoluteError": {{result.MeanAbsoluteError.ToString("0.000000", CultureInfo.InvariantCulture)}},
          "rootMeanSquareError": {{result.RootMeanSquareError.ToString("0.000000", CultureInfo.InvariantCulture)}},
          "maxError": {{result.MaxError.ToString("0.000000", CultureInfo.InvariantCulture)}},
          "percentPixelsOver02": {{result.PercentPixelsOver02.ToString("0.000000", CultureInfo.InvariantCulture)}},
          "percentPixelsOver05": {{result.PercentPixelsOver05.ToString("0.000000", CultureInfo.InvariantCulture)}},
          "percentPixelsOver10": {{result.PercentPixelsOver10.ToString("0.000000", CultureInfo.InvariantCulture)}}
        }
        """;
    }

    private static string JsonEscape(string value)
    {
        return value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }

    private static string ResolveProjectPath(string path)
    {
        if (System.IO.Path.IsPathRooted(path))
        {
            return path;
        }

        if (path.StartsWith("res://", StringComparison.Ordinal) || path.StartsWith("user://", StringComparison.Ordinal))
        {
            return ProjectSettings.GlobalizePath(path);
        }

        return System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), path);
    }

    private static float ParseFloat(string? value, float fallback)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static int ParseInt(string? value, int fallback)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
    }

    private static bool ParseBool(string? value, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            "0" or "false" or "no" or "off" => false,
            _ => fallback,
        };
    }

    private static GiOutputMode? ParseGiMode(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "direct" => GiOutputMode.Direct,
            "indirect" => GiOutputMode.Indirect,
            "combined" => GiOutputMode.Combined,
            _ => null,
        };
    }

    private static Dictionary<string, string> ParseArguments()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rawArg in OS.GetCmdlineArgs().Concat(OS.GetCmdlineUserArgs()))
        {
            if (!rawArg.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var arg = rawArg[2..];
            var equals = arg.IndexOf('=');
            if (equals < 0)
            {
                result[arg] = "true";
            }
            else
            {
                result[arg[..equals]] = arg[(equals + 1)..];
            }
        }

        return result;
    }

    private void SetStatus(string text)
    {
        _statusLabel.Text = text;
        GD.Print(text);
    }
}
