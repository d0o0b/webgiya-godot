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

    private static RenderQuality ParseRenderQuality(string? value, RenderQuality fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.ToLowerInvariant() switch
        {
            "balanced" or "preview" => RenderQuality.Balanced,
            "high" => RenderQuality.High,
            "ultra" => RenderQuality.Ultra,
            _ => fallback,
        };
    }

    private static SurfelSamplingMode ParseSurfelSamplingMode(string? value, SurfelSamplingMode fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        return value.ToLowerInvariant() switch
        {
            "reference" or "visible" or "screen" or "screen-space" => SurfelSamplingMode.ReferenceVisible,
            "geometry" or "area" or "mesh" => SurfelSamplingMode.GeometryArea,
            _ => fallback,
        };
    }

    private static string SurfelSamplingModeName(SurfelSamplingMode mode)
    {
        return mode switch
        {
            SurfelSamplingMode.GeometryArea => "geometry",
            _ => "reference",
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
