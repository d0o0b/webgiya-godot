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
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
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

    private void ConfigureViewportQuality()
    {
        var viewport = GetViewport();
        var msaa = _renderQuality switch
        {
            RenderQuality.Ultra => 3,
            RenderQuality.High => 2,
            _ => 1,
        };

        viewport.Set("msaa_3d", msaa);
        viewport.Set("screen_space_aa", 1);
        viewport.Set("use_taa", _renderQuality != RenderQuality.Balanced);
        viewport.Set("use_debanding", true);
        viewport.Set("texture_mipmap_bias", _renderQuality == RenderQuality.Ultra ? -0.65f : -0.35f);
    }
}
