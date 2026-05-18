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
    private const float ReferenceSurfelSpacing = 0.08f;
    private const int ReferenceSurfelTileSize = 8;

    private enum GiOutputMode
    {
        Direct,
        Indirect,
        Combined,
    }

    private enum RenderQuality
    {
        Balanced,
        High,
        Ultra,
    }

    private enum SurfelSamplingMode
    {
        ReferenceVisible,
        GeometryArea,
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
        public float OcclusionShadowStrength { get; init; } = 0.8f;
        public float BleedReduction { get; init; } = 0.1f;
        public float AlbedoBoost { get; init; } = 1.0f;
        public float ShadowMaxDistance { get; init; } = 60.0f;
        public float ShadowBias { get; init; } = 0.02f;
        public float ShadowNormalBias { get; init; } = 1.0f;
        public float SdfgiBounceFeedback { get; init; } = 0.65f;
        public bool SdfgiReadSkyLight { get; init; } = true;
        public float SsaoRadius { get; init; } = 2.0f;
        public float SsilRadius { get; init; } = 5.0f;
        public float TonemapExposure { get; init; } = 0.85f;
        public float AmbientSkyContribution { get; init; } = 1.0f;
        public float AdjustmentContrast { get; init; } = 1.02f;
        public float AdjustmentSaturation { get; init; } = 1.0f;
    }

    private readonly struct SurfelSample
    {
        public SurfelSample(Vector3 position, Vector3 normal, Color color, float displayScale = 1.0f)
        {
            Position = position;
            Normal = normal;
            Color = color;
            DisplayScale = displayScale;
        }

        public Vector3 Position { get; }
        public Vector3 Normal { get; }
        public Color Color { get; }
        public float DisplayScale { get; }
    }

    private readonly struct TriangleSurfaceSample
    {
        public TriangleSurfaceSample(Vector3 a, Vector3 b, Vector3 c, Vector3 normal, Vector2 uvA, Vector2 uvB, Vector2 uvC, bool hasUv, float area)
        {
            A = a;
            B = b;
            C = c;
            Normal = normal;
            UvA = uvA;
            UvB = uvB;
            UvC = uvC;
            HasUv = hasUv;
            Area = area;
        }

        public Vector3 A { get; }
        public Vector3 B { get; }
        public Vector3 C { get; }
        public Vector3 Normal { get; }
        public Vector2 UvA { get; }
        public Vector2 UvB { get; }
        public Vector2 UvC { get; }
        public bool HasUv { get; }
        public float Area { get; }
    }

    private readonly struct TileSurfelCandidate
    {
        public TileSurfelCandidate(SurfelSample sample, float depthSquared, int tileIndex, uint orderKey)
        {
            Sample = sample;
            DepthSquared = depthSquared;
            TileIndex = tileIndex;
            OrderKey = orderKey;
        }

        public SurfelSample Sample { get; }
        public float DepthSquared { get; }
        public int TileIndex { get; }
        public uint OrderKey { get; }
    }

    private sealed class SurfaceMaterialSampler
    {
        public SurfaceMaterialSampler(Color baseColor, Image? albedoImage, float albedoBoost)
        {
            BaseColor = baseColor;
            AlbedoImage = albedoImage;
            AlbedoBoost = albedoBoost;
        }

        private Color BaseColor { get; }
        private Image? AlbedoImage { get; }
        private float AlbedoBoost { get; }

        public Color Sample(Vector2 uv)
        {
            var color = BaseColor;
            if (AlbedoImage == null || AlbedoImage.GetWidth() <= 0 || AlbedoImage.GetHeight() <= 0)
            {
                color.A = 1.0f;
                return color;
            }

            var x = Mathf.Clamp(Mathf.FloorToInt(Repeat(uv.X) * AlbedoImage.GetWidth()), 0, AlbedoImage.GetWidth() - 1);
            var y = Mathf.Clamp(Mathf.FloorToInt(Repeat(uv.Y) * AlbedoImage.GetHeight()), 0, AlbedoImage.GetHeight() - 1);
            var texel = AlbedoImage.GetPixel(x, y);
            color *= texel;
            color = ApplyAlbedoBoost(color, AlbedoBoost);
            color.A = 1.0f;
            return color;
        }

        private static Color ApplyAlbedoBoost(Color color, float boost)
        {
            if (Mathf.IsEqualApprox(boost, 1.0f))
            {
                return color;
            }

            boost = Mathf.Max(0.001f, boost);
            color.R = 1.0f - Mathf.Pow(Mathf.Max(0.0f, 1.0f - color.R), boost);
            color.G = 1.0f - Mathf.Pow(Mathf.Max(0.0f, 1.0f - color.G), boost);
            color.B = 1.0f - Mathf.Pow(Mathf.Max(0.0f, 1.0f - color.B), boost);
            return color;
        }

        private static float Repeat(float value)
        {
            return value - Mathf.Floor(value);
        }
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
            AmbientEnergy = 0.55f,
            OcclusionShadowStrength = 0.5f,
            ShadowMaxDistance = 24.0f,
            SdfgiBounceFeedback = 0.62f,
            SdfgiReadSkyLight = true,
            SsaoRadius = 1.3f,
            SsilRadius = 3.0f,
            TonemapExposure = 0.78f,
            AmbientSkyContribution = 0.75f,
            AdjustmentContrast = 1.0f,
        },
        new ScenePreset
        {
            Id = "leonardo",
            Label = "Leonardo",
            Kind = "leonardo",
            HdrPath = "res://assets/exr/hay_bales_1k.exr",
            CameraPosition = new Vector3(0, 3, 13),
            CameraTarget = new Vector3(0, 2, 0),
            OcclusionShadowStrength = 0.8f,
            ShadowMaxDistance = 40.0f,
        },
        new ScenePreset
        {
            Id = "occlusion",
            Label = "Occlusion test",
            Kind = "occlusion",
            HdrPath = "res://assets/exr/hay_bales_1k.exr",
            CameraPosition = new Vector3(13, 3, 0),
            CameraTarget = new Vector3(0, 2, 0),
            OcclusionShadowStrength = 1.2f,
            BleedReduction = 0.1f,
            ShadowMaxDistance = 42.0f,
        },
        new ScenePreset
        {
            Id = "marble-bust",
            Label = "Marble Bust",
            Kind = "marble-bust",
            HdrPath = "res://assets/exr/kloppenheim_05_puresky_2k.hdr",
            CameraPosition = new Vector3(0, -0.3f, 3),
            CameraTarget = Vector3.Zero,
            OcclusionShadowStrength = 0.5f,
            BleedReduction = 0.1f,
            ShadowMaxDistance = 18.0f,
            SsaoRadius = 1.2f,
            SsilRadius = 2.5f,
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
            OcclusionShadowStrength = 0.6f,
            BleedReduction = 0.25f,
            AlbedoBoost = 1.2f,
            ShadowMaxDistance = 110.0f,
            ShadowBias = 0.012f,
            ShadowNormalBias = 0.65f,
            SdfgiBounceFeedback = 0.82f,
            SsaoRadius = 2.4f,
            SsilRadius = 7.5f,
            TonemapExposure = 0.95f,
            AdjustmentContrast = 0.9f,
            AdjustmentSaturation = 0.95f,
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
            OcclusionShadowStrength = 0.6f,
            BleedReduction = 0.25f,
            AlbedoBoost = 1.2f,
            ShadowMaxDistance = 120.0f,
            ShadowBias = 0.012f,
            ShadowNormalBias = 0.65f,
            SdfgiBounceFeedback = 0.82f,
            SsaoRadius = 2.4f,
            SsilRadius = 7.5f,
            TonemapExposure = 0.95f,
            AdjustmentContrast = 0.9f,
            AdjustmentSaturation = 0.95f,
        },
    };

    private readonly Dictionary<string, Color> _textureAverageColorCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Image> _textureImageCache = new(StringComparer.Ordinal);
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
    private float _presetOcclusionShadowStrength = 0.8f;
    private float _presetBleedReduction = 0.1f;
    private float _presetAlbedoBoost = 1.0f;
    private float _presetShadowMaxDistance = 60.0f;
    private float _presetShadowBias = 0.02f;
    private float _presetShadowNormalBias = 1.0f;
    private float _presetSdfgiBounceFeedback = 0.65f;
    private bool _presetSdfgiReadSkyLight = true;
    private float _presetSsaoRadius = 2.0f;
    private float _presetSsilRadius = 5.0f;
    private float _presetTonemapExposure = 0.85f;
    private float _presetAmbientSkyContribution = 1.0f;
    private float _presetAdjustmentContrast = 1.02f;
    private float _presetAdjustmentSaturation = 1.0f;
    private RenderQuality _renderQuality = RenderQuality.High;
    private SurfelSamplingMode _surfelSamplingMode = SurfelSamplingMode.ReferenceVisible;
    private GiOutputMode _giMode = GiOutputMode.Combined;
    private bool _syncingControls;
    private string _screenshotViewName = "default";
    private bool _surfelPreviewEnabled;
    private bool _surfelLightsEnabled;
    private bool _exportSurfelDataDuringScreenshots;
    private bool _exportRenderMetadataDuringScreenshots;
    private bool _hideUiDuringScreenshots;
    private float _surfelPreviewSize = 0.035f;
    private int _surfelPreviewBudget = 8192;
    private int _surfelLightCount = 24;
    private int _surfelExportLimit = 8192;
    private float _surfelLightEnergy = 0.16f;
    private bool _cameraDrivenSurfelsDirty;
    private float _cameraDrivenSurfelsDirtyElapsed;
    private const float CameraDrivenSurfelRebuildDelaySeconds = 0.12f;
    private const float MinAutomatedScreenshotDelaySeconds = 2.0f;
    private const float MaxScreenshotDelaySeconds = 5.0f;
}
