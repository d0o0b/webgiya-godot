using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public partial class Main : Node3D
{
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

    private Camera3D _camera = null!;
    private DirectionalLight3D _sun = null!;
    private WorldEnvironment _worldEnvironment = null!;
    private Godot.Environment _environment = null!;
    private Node3D _sceneRoot = null!;
    private OptionButton _sceneSelect = null!;
    private OptionButton _giModeSelect = null!;
    private Label _statusLabel = null!;
    private CheckButton _animateLight = null!;
    private HSlider _indirectSlider = null!;
    private HSlider _azimuthSlider = null!;
    private HSlider _elevationSlider = null!;
    private HSlider _lightIntensitySlider = null!;
    private HSlider _lightSpeedSlider = null!;
    private int _currentPresetIndex;
    private Vector3 _target = Vector3.Zero;
    private float _yaw;
    private float _pitch;
    private float _distance = 8.0f;
    private bool _orbiting;
    private bool _panning;
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
    private const float MaxScreenshotDelaySeconds = 5.0f;

    public override void _Ready()
    {
        BuildWorld();
        BuildUi();

        LoadPreset(0);
        var args = ParseArguments();
        if (args.ContainsKey("screenshots"))
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
                _orbiting = button.Pressed;
            }
            else if (button.ButtonIndex is MouseButton.Right or MouseButton.Middle)
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
                _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.006f, -1.45f, 1.45f);
                UpdateCameraTransform();
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
    }

    private void BuildUi()
    {
        var root = new CanvasLayer { Name = "Ui" };
        AddChild(root);

        var controls = new HBoxContainer
        {
            Name = "SceneSwitcher",
            Position = new Vector2(15, 15),
            CustomMinimumSize = new Vector2(420, 36),
        };
        root.AddChild(controls);

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
        root.AddChild(renderControls);

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

        _statusLabel = new Label
        {
            Text = "",
            Position = new Vector2(15, 100),
            CustomMinimumSize = new Vector2(720, 24),
            Modulate = new Color(0.92f, 0.92f, 0.92f, 0.88f),
        };
        root.AddChild(_statusLabel);
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
        _syncingControls = false;
    }

    private void ClearScene()
    {
        foreach (var child in _sceneRoot.GetChildren())
        {
            child.QueueFree();
        }
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

        AddBox(group, "Floor", new Vector3(boxWidth + wall * 2, wall, boxDepth + wall), new Vector3(0, -wall * 0.5f, -wall * 0.5f), Vector3.Zero, white);
        AddBox(group, "LeftWall", new Vector3(wall, boxHeight, boxDepth), new Vector3(-boxWidth * 0.5f - wall * 0.5f, boxHeight * 0.5f, 0), Vector3.Zero, red);
        AddBox(group, "RightWall", new Vector3(wall, boxHeight, boxDepth), new Vector3(boxWidth * 0.5f + wall * 0.5f, boxHeight * 0.5f, 0), Vector3.Zero, green);
        AddBox(group, "BackWall", new Vector3(boxWidth + wall * 2, boxHeight, wall), new Vector3(0, boxHeight * 0.5f, -boxDepth * 0.5f - wall * 0.5f), Vector3.Zero, white);

        var panelWidth = 0.75f + wall;
        var panelDepth = boxDepth + wall;
        var ceilingY = boxHeight + wall * 0.5f;
        var ceilingX = boxWidth * 0.5f - panelWidth * 0.5f + wall;
        AddBox(group, "CeilingLeft", new Vector3(panelWidth, wall, panelDepth), new Vector3(-ceilingX, ceilingY, -0.5f * wall), Vector3.Zero, white);
        AddBox(group, "CeilingRight", new Vector3(panelWidth, wall, panelDepth), new Vector3(ceilingX, ceilingY, -0.5f * wall), Vector3.Zero, white);
        AddBox(group, "TallBox", new Vector3(0.5f, 0.7f, 0.5f), new Vector3(-0.3f, 0.35f, -0.2f), new Vector3(0, Mathf.Pi * 0.25f, 0), white);
        AddBox(group, "ShortBox", new Vector3(0.4f, 0.4f, 0.4f), new Vector3(0.4f, 0.2f, 0.4f), new Vector3(0, Mathf.Pi * -0.1f, 0), white);
    }

    private void BuildMarbleBustScene()
    {
        var group = new Node3D { Name = "MarbleBustScene", Position = new Vector3(0, -0.9f, 0), Scale = new Vector3(3, 3, 3) };
        _sceneRoot.AddChild(group);

        var white = MakeMaterial(new Color(0.67f, 0.67f, 0.67f), 0.85f);
        var red = MakeMaterial(new Color(1, 0, 0), 0.9f);
        var blue = MakeMaterial(new Color(0, 0, 1), 0.9f);
        const float wall = 0.02f;

        AddBox(group, "Floor", new Vector3(1.0f + wall * 2, wall, 1.0f + wall), new Vector3(0, -wall * 0.5f, 0), Vector3.Zero, white);
        AddBox(group, "Back", new Vector3(1.0f + wall * 2, wall, 1.0f / 1.5f), new Vector3(0, 0.33f, -0.5f), new Vector3(Mathf.Pi / 2.0f, 0, 0), white);
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

    private MeshInstance3D AddBox(Node3D parent, string name, Vector3 size, Vector3 position, Vector3 rotation, Material material)
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
            _target += movement.Normalized() * (_distance * 0.65f + 1.0f) * delta;
            UpdateCameraTransform();
        }
    }

    private async Task RunScreenshotSequence(Dictionary<string, string> args)
    {
        var dir = args.TryGetValue("screenshot-dir", out var dirArg) ? dirArg : "screenshots";
        var delay = ParseFloat(args.GetValueOrDefault("screenshot-delay"), 1.0f);
        delay = Mathf.Clamp(delay, 0.0f, MaxScreenshotDelaySeconds);
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
        if (delay > 0.0f)
        {
            await ToSignal(GetTree().CreateTimer(delay), SceneTreeTimer.SignalName.Timeout);
        }

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        var image = GetViewport().GetTexture().GetImage();
        var absoluteDirectory = MakeScreenshotDirectory(directory);
        var preset = _presets[_currentPresetIndex];
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var path = System.IO.Path.Combine(absoluteDirectory, $"{preset.Id}-{_giMode.ToString().ToLowerInvariant()}-{_screenshotViewName}-{timestamp}.png");
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

    private static float ParseFloat(string? value, float fallback)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : fallback;
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
