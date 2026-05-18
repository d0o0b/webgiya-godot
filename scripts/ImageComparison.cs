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
}
