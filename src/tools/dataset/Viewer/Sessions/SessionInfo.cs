using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace JassCardEye.Dataset.Viewer.Sessions;

/// <summary>
/// What a session was recorded with: the JSON both apps write next to the recognition log - device, app
/// and model, and the settings the pile was counting with (see "Session recordings" in
/// context/architecture/data-pipeline.md). It is what makes sessions from different devices comparable.
///
/// Every field may be missing, and so may the whole file: a recording can come without one.
/// </summary>
internal sealed record SessionInfo(
    string? Platform,
    string? AppVersion,
    string? AppBuild,
    string? Device,
    string? System,
    string? ModelVariant,
    string? ModelRun,
    string? Compute,
    double? ConfidenceThreshold,
    string? StabilityRule,
    int? StabilityFrames,
    string? Deck,
    string? CameraLens,
    string? Discipline,
    string? StartedAt)
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    /// <summary>The info next to a session video, null when there is none.</summary>
    public static SessionInfo? ReadBeside(string videoPath)
    {
        var path = Path.ChangeExtension(videoPath, ".json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    internal static SessionInfo? Parse(string json) => JsonSerializer.Deserialize<SessionInfo>(json, Options);

    /// <summary>The threshold and stability rule the app was counting with, when the info names them.</summary>
    public ReplaySettings? Settings =>
        ConfidenceThreshold is { } threshold && StabilityFrames is { } frames && StabilityRule is "run" or "majority"
            ? new ReplaySettings(threshold, StabilityRule == "run" ? Sessions.StabilityRule.Run : Sessions.StabilityRule.Majority, frames)
            : null;

    /// <summary>One line: device, system, app, model and compute - what is known of it.</summary>
    public string Describe()
    {
        var parts = new List<string>();
        if (Device is not null) parts.Add(Device);
        if (Platform is not null || System is not null) parts.Add($"{Platform} {System}".Trim());
        if (AppVersion is not null) parts.Add(AppBuild is null ? $"app {AppVersion}" : $"app {AppVersion} ({AppBuild})");
        if (ModelRun is not null) parts.Add($"model {ModelVariant ?? "?"} {ModelRun}");
        if (Compute is not null) parts.Add(Compute);
        if (CameraLens is not null) parts.Add($"{CameraLens} lens");
        return string.Join(" · ", parts);
    }
}
