using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

public partial class Main : Node3D
{
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
    private Node3D _sceneRoot = null!;
    private OptionButton _sceneSelect = null!;
    private Label _statusLabel = null!;
    private CheckButton _animateLight = null!;
    private int _currentPresetIndex;
    private Vector3 _target = Vector3.Zero;
    private float _yaw;
    private float _pitch;
    private float _distance = 8.0f;
    private bool _orbiting;
    private bool _panning;
    private float _lightAnimationTime;
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
            var x = Mathf.Sin(_lightAnimationTime * 0.5f) * 10.0f;
            var z = Mathf.Cos(_lightAnimationTime * 0.5f) * 10.0f;
            _sun.Position = new Vector3(x, 20.5f, z);
            _sun.LookAt(Vector3.Zero);
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

        _statusLabel = new Label
        {
            Text = "",
            Position = new Vector2(15, 58),
            CustomMinimumSize = new Vector2(720, 24),
            Modulate = new Color(0.92f, 0.92f, 0.92f, 0.88f),
        };
        root.AddChild(_statusLabel);
    }

    private void LoadPreset(int index)
    {
        index = Mathf.PosMod(index, _presets.Count);
        _currentPresetIndex = index;
        _sceneSelect.Select(index);

        var preset = _presets[index];
        SetStatus($"Loading {preset.Label}");
        ClearScene();
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
        SetStatus($"{preset.Label} loaded");
    }

    private void ConfigureEnvironment(ScenePreset preset)
    {
        var environment = new Godot.Environment();
        environment.Set("background_mode", 2);
        environment.Set("background_color", new Color(0.125f, 0.125f, 0.145f));
        environment.Set("ambient_light_color", Colors.White);
        environment.Set("ambient_light_source", 3);
        environment.Set("ambient_light_energy", preset.AmbientEnergy);
        environment.Set("ambient_light_sky_contribution", 1.0f);
        environment.Set("reflected_light_source", 2);
        environment.Set("tonemap_mode", 3);
        environment.Set("tonemap_exposure", 0.85f);
        environment.Set("tonemap_white", 1.0f);
        environment.Set("ssao_enabled", true);
        environment.Set("ssao_radius", 2.0f);
        environment.Set("ssao_intensity", 1.6f);
        environment.Set("ssil_enabled", true);
        environment.Set("ssil_radius", 5.0f);
        environment.Set("ssil_intensity", 1.0f);
        environment.Set("sdfgi_enabled", true);
        environment.Set("sdfgi_energy", preset.IndirectEnergy);
        environment.Set("sdfgi_bounce_feedback", 0.65f);
        environment.Set("sdfgi_read_sky_light", true);
        environment.Set("glow_enabled", true);
        environment.Set("glow_intensity", 0.08f);

        var texture = ResourceLoader.Load<Texture2D>(preset.HdrPath);
        if (texture != null)
        {
            var skyMaterial = new PanoramaSkyMaterial { Panorama = texture };
            var sky = new Sky { SkyMaterial = skyMaterial };
            environment.Set("sky", sky);
        }

        _worldEnvironment.Environment = environment;
    }

    private void ConfigureLight(ScenePreset preset)
    {
        _sun.LightEnergy = preset.LightIntensity;
        var az = Mathf.DegToRad(preset.LightAzimuthDeg);
        var el = Mathf.DegToRad(preset.LightElevationDeg);
        const float radius = 40.0f;
        _sun.Position = new Vector3(
            radius * Mathf.Cos(el) * Mathf.Cos(az),
            radius * Mathf.Sin(el),
            radius * Mathf.Cos(el) * Mathf.Sin(az));
        _sun.LookAt(Vector3.Zero);
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

        foreach (var sceneId in sceneIds)
        {
            var index = _presets.FindIndex(preset => preset.Id == sceneId);
            if (index < 0)
            {
                GD.PushWarning($"Unknown screenshot scene '{sceneId}'");
                continue;
            }

            LoadPreset(index);
            await CaptureScreenshot(delay, dir);
        }

        if (args.TryGetValue("screenshot-quit", out var quitValue) && quitValue == "false")
        {
            return;
        }

        GetTree().Quit();
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
        var path = System.IO.Path.Combine(absoluteDirectory, $"{preset.Id}-{timestamp}.png");
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
