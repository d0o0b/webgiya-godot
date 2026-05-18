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
            Mesh = new QuadMesh { Size = Vector2.One },
            InstanceCount = samples.Count,
            VisibleInstanceCount = samples.Count,
        };

        var sceneScale = EstimateSurfelPreviewSceneScale(samples);
        for (var i = 0; i < samples.Count; i++)
        {
            var size = _surfelPreviewSize * sceneScale * samples[i].DisplayScale;
            var basis = MakeSurfelPreviewBasis(samples[i].Normal, size);
            var cameraOffset = _camera.GlobalPosition - samples[i].Position;
            var previewPosition = samples[i].Position;
            if (cameraOffset.LengthSquared() > 0.001f)
            {
                previewPosition += cameraOffset.Normalized() * size * 0.35f;
            }

            multimesh.SetInstanceTransform(i, new Transform3D(basis, previewPosition));
            multimesh.SetInstanceColor(i, samples[i].Color);
        }

        _surfelPreview.Multimesh = multimesh;
        _surfelPreview.Visible = true;
        GD.Print($"Built surfel preview: {samples.Count} surfels ({SurfelSamplingModeName(_surfelSamplingMode)} sampling)");
    }

    private void MarkCameraDrivenSurfelsDirty()
    {
        if (_syncingControls || _camera == null)
        {
            return;
        }

        if (!_surfelPreviewEnabled && (!_surfelLightsEnabled || _surfelSamplingMode != SurfelSamplingMode.ReferenceVisible))
        {
            return;
        }

        _cameraDrivenSurfelsDirty = true;
        _cameraDrivenSurfelsDirtyElapsed = 0.0f;
    }

    private void UpdateCameraDrivenSurfels(float delta)
    {
        if (!_cameraDrivenSurfelsDirty)
        {
            return;
        }

        _cameraDrivenSurfelsDirtyElapsed += delta;
        if (_cameraDrivenSurfelsDirtyElapsed < CameraDrivenSurfelRebuildDelaySeconds)
        {
            return;
        }

        _cameraDrivenSurfelsDirty = false;
        _cameraDrivenSurfelsDirtyElapsed = 0.0f;

        if (_surfelPreviewEnabled)
        {
            RebuildSurfelPreview();
        }

        if (_surfelLightsEnabled && _surfelSamplingMode == SurfelSamplingMode.ReferenceVisible)
        {
            RebuildSurfelLights();
        }
    }

    private static Basis MakeSurfelPreviewBasis(Vector3 normal, float size)
    {
        var z = normal.LengthSquared() > 0.001f ? normal.Normalized() : Vector3.Up;
        var helper = Mathf.Abs(z.Dot(Vector3.Up)) > 0.94f ? Vector3.Right : Vector3.Up;
        var x = helper.Cross(z).Normalized();
        var y = z.Cross(x).Normalized();
        return new Basis(x * size, y * size, z);
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

        var samples = CreateSurfelSamples(Mathf.Clamp(Mathf.Max(_surfelPreviewBudget, _surfelLightCount * 64), 256, 65536));
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
        budget = Mathf.Clamp(budget, 1, 65536);
        if (_surfelSamplingMode == SurfelSamplingMode.ReferenceVisible)
        {
            var visibleSamples = CreateReferenceVisibleSurfelSamples(budget);
            if (visibleSamples.Count > 0)
            {
                return visibleSamples;
            }
        }

        return CreateGeometrySurfelSamples(budget);
    }

    private List<SurfelSample> CreateGeometrySurfelSamples(int budget)
    {
        budget = Mathf.Clamp(budget, 1, 65536);
        var samples = new List<SurfelSample>(budget);
        var meshCount = Mathf.Max(1, CountMeshInstances(_sceneRoot));
        CollectSurfelSamples(_sceneRoot, samples, budget, meshCount);
        return samples;
    }

    private List<SurfelSample> CreateReferenceVisibleSurfelSamples(int budget)
    {
        var viewportSize = GetViewport().GetVisibleRect().Size;
        var width = Mathf.RoundToInt(viewportSize.X);
        var height = Mathf.RoundToInt(viewportSize.Y);
        if (width <= 0 || height <= 0 || _camera == null)
        {
            return new List<SurfelSample>();
        }

        var candidateBudget = Mathf.Clamp(budget * 10, budget, 65536);
        var candidates = CreateGeometrySurfelSamples(candidateBudget);
        if (candidates.Count == 0)
        {
            return candidates;
        }

        var tileWidth = Mathf.Max(1, Mathf.CeilToInt(width / (float)ReferenceSurfelTileSize));
        var tileHeight = Mathf.Max(1, Mathf.CeilToInt(height / (float)ReferenceSurfelTileSize));
        var tileCount = tileWidth * tileHeight;
        var tileHits = new TileSurfelCandidate[tileCount];
        var tileHasHit = new bool[tileCount];
        var cameraPosition = _camera.GlobalPosition;
        var cameraForward = -_camera.GlobalTransform.Basis.Z.Normalized();

        foreach (var candidate in candidates)
        {
            var toSample = candidate.Position - cameraPosition;
            var forwardDepth = toSample.Dot(cameraForward);
            if (forwardDepth <= 0.01f)
            {
                continue;
            }

            if (candidate.Normal.LengthSquared() > 0.001f)
            {
                var towardCamera = -toSample.Normalized();
                if (candidate.Normal.Normalized().Dot(towardCamera) < -0.2f)
                {
                    continue;
                }
            }

            var screen = _camera.UnprojectPosition(candidate.Position);
            if (screen.X < 0.0f || screen.Y < 0.0f || screen.X >= width || screen.Y >= height)
            {
                continue;
            }

            var tileX = Mathf.Clamp((int)(screen.X / ReferenceSurfelTileSize), 0, tileWidth - 1);
            var tileY = Mathf.Clamp((int)(screen.Y / ReferenceSurfelTileSize), 0, tileHeight - 1);
            var tileIndex = tileY * tileWidth + tileX;
            var depthSquared = toSample.LengthSquared();
            if (tileHasHit[tileIndex] && depthSquared >= tileHits[tileIndex].DepthSquared)
            {
                continue;
            }

            tileHasHit[tileIndex] = true;
            tileHits[tileIndex] = new TileSurfelCandidate(candidate, depthSquared, tileIndex, HashTile(tileX, tileY));
        }

        var visible = new List<TileSurfelCandidate>(Math.Min(tileCount, budget));
        for (var tileIndex = 0; tileIndex < tileCount; tileIndex++)
        {
            if (tileHasHit[tileIndex])
            {
                visible.Add(tileHits[tileIndex]);
            }
        }

        if (visible.Count <= budget)
        {
            visible.Sort((a, b) => a.TileIndex.CompareTo(b.TileIndex));
            return visible.Select(hit => hit.Sample).ToList();
        }

        visible.Sort((a, b) => a.OrderKey.CompareTo(b.OrderKey));
        return visible.Take(budget).Select(hit => hit.Sample).ToList();
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

            var materialSampler = GetSurfaceMaterialSampler(meshInstance, surface);
            var indices = arrays.Count > (int)Mesh.ArrayType.Index
                ? arrays[(int)Mesh.ArrayType.Index].AsInt32Array()
                : Array.Empty<int>();
            var uvs = arrays.Count > (int)Mesh.ArrayType.TexUV
                ? arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array()
                : Array.Empty<Vector2>();
            var transform = meshInstance.GlobalTransform;
            var triangles = BuildTriangleSamples(vertices, uvs, indices, transform);
            if (triangles.Count == 0)
            {
                continue;
            }

            var totalArea = triangles.Sum(triangle => triangle.Area);
            if (totalArea <= 0.0f)
            {
                continue;
            }

            AddTriangleAreaSamples(triangles, materialSampler, totalArea, perSurfaceTarget, samples, budget);
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

    private static float EstimateSurfelPreviewSceneScale(List<SurfelSample> samples)
    {
        if (samples.Count == 0)
        {
            return 1.0f;
        }

        var min = samples[0].Position;
        var max = samples[0].Position;
        for (var i = 1; i < samples.Count; i++)
        {
            min = min.Min(samples[i].Position);
            max = max.Max(samples[i].Position);
        }

        var diagonal = Mathf.Max(0.001f, (max - min).Length());
        return Mathf.Clamp(9.0f / diagonal, 0.28f, 1.1f);
    }

    private static float EstimateSurfelDisplayScale(float representedArea, int representedCount)
    {
        var spacing = Mathf.Sqrt(Mathf.Max(0.000001f, representedArea / Mathf.Max(1, representedCount)));
        return Mathf.Clamp(spacing / ReferenceSurfelSpacing, 0.4f, 1.15f);
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
        var displayScale = EstimateSurfelDisplayScale(face.Area, columns * rows);

        for (var y = 0; y < rows && samples.Count < budget; y++)
        {
            var v = (y + 0.5f) / rows;
            for (var x = 0; x < columns && samples.Count < budget; x++)
            {
                var u = (x + 0.5f) / columns;
                var local = GetBoxFaceLocalPoint(face.Index, half, u, v);
                samples.Add(new SurfelSample(transform * local, normal, color, displayScale));
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
            0 => new Vector3(-half.X, py, pz),
            1 => new Vector3(half.X, py, pz),
            2 => new Vector3(px, -half.Y, qz),
            3 => new Vector3(px, half.Y, qz),
            4 => new Vector3(px, py, -half.Z),
            _ => new Vector3(px, py, half.Z),
        };
    }

    private static List<TriangleSurfaceSample> BuildTriangleSamples(Vector3[] vertices, Vector2[] uvs, int[] indices, Transform3D transform)
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
            var hasUv = uvs.Length > i0 && uvs.Length > i1 && uvs.Length > i2;
            var uvA = hasUv ? uvs[i0] : Vector2.Zero;
            var uvB = hasUv ? uvs[i1] : Vector2.Zero;
            var uvC = hasUv ? uvs[i2] : Vector2.Zero;
            var cross = (b - a).Cross(c - a);
            var area = cross.Length() * 0.5f;
            if (area <= 0.000001f)
            {
                continue;
            }

            triangles.Add(new TriangleSurfaceSample(a, b, c, cross.Normalized(), uvA, uvB, uvC, hasUv, area));
        }

        return triangles;
    }

    private static void AddTriangleAreaSamples(List<TriangleSurfaceSample> triangles, SurfaceMaterialSampler materialSampler, float totalArea, int target, List<SurfelSample> samples, int budget)
    {
        target = Mathf.Max(1, target);
        var displayScale = EstimateSurfelDisplayScale(totalArea, target);
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
            var uv = triangle.HasUv
                ? triangle.UvA * b0 + triangle.UvB * b1 + triangle.UvC * b2
                : Vector2.Zero;
            var color = materialSampler.Sample(uv);
            samples.Add(new SurfelSample(position, triangle.Normal, color, displayScale));
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

    private static uint HashTile(int x, int y)
    {
        unchecked
        {
            var hash = 2166136261u;
            hash = (hash ^ (uint)x) * 16777619u;
            hash = (hash ^ (uint)y) * 16777619u;
            hash ^= hash >> 16;
            hash *= 2246822519u;
            hash ^= hash >> 13;
            hash *= 3266489917u;
            hash ^= hash >> 16;
            return hash;
        }
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
        var sampler = GetSurfaceMaterialSampler(meshInstance, surface);
        return sampler.Sample(Vector2.Zero);
    }

    private SurfaceMaterialSampler GetSurfaceMaterialSampler(MeshInstance3D meshInstance, int surface)
    {
        var material =
            meshInstance.MaterialOverride ??
            meshInstance.GetSurfaceOverrideMaterial(surface) ??
            meshInstance.Mesh?.SurfaceGetMaterial(surface);

        if (material is StandardMaterial3D standard)
        {
            var color = standard.AlbedoColor;
            color.A = 1.0f;
            return new SurfaceMaterialSampler(color, GetTextureImage(standard.AlbedoTexture), _presetAlbedoBoost);
        }

        return new SurfaceMaterialSampler(new Color(0.55f, 0.78f, 1.0f, 1.0f), null, _presetAlbedoBoost);
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

        var image = GetTextureImage(texture);
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

    private Image? GetTextureImage(Texture2D? texture)
    {
        if (texture == null)
        {
            return null;
        }

        var cacheKey = string.IsNullOrWhiteSpace(texture.ResourcePath)
            ? texture.GetInstanceId().ToString(CultureInfo.InvariantCulture)
            : texture.ResourcePath;
        if (_textureImageCache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var image = texture.GetImage();
        if (image == null || image.GetWidth() <= 0 || image.GetHeight() <= 0)
        {
            return null;
        }

        image.Convert(Image.Format.Rgba8);
        _textureImageCache[cacheKey] = image;
        return image;
    }
}
