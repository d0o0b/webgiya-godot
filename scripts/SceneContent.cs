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
            NormalizeMeshMaterials(mesh);
        }

        foreach (var child in node.GetChildren())
        {
            ConfigureGeometry(child);
        }
    }

    private void NormalizeMeshMaterials(MeshInstance3D mesh)
    {
        NormalizeMaterial(mesh.MaterialOverride);
        if (mesh.Mesh == null)
        {
            return;
        }

        for (var surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            NormalizeMaterial(mesh.GetSurfaceOverrideMaterial(surface));
            NormalizeMaterial(mesh.Mesh.SurfaceGetMaterial(surface));
        }
    }

    private static void NormalizeMaterial(Material? material)
    {
        if (material is not StandardMaterial3D standard)
        {
            return;
        }

        standard.Set("texture_filter", 5);
        standard.Roughness = Mathf.Clamp(standard.Roughness, 0.18f, 1.0f);
        standard.Metallic = Mathf.Clamp(standard.Metallic, 0.0f, 1.0f);
    }
}
