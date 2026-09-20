using System;
using System.IO;
using System.Text.Json;

namespace JassCardEye.Dataset.Viewer;

/// <summary>
/// Settings that survive restarts, so a labelling session can be resumed without picking the same
/// folders again. Stored as JSON in the user's application data directory.
/// </summary>
internal sealed class ViewerSettings
{
    /// <summary>Target dataset the labels are written to.</summary>
    public string? DatasetPath { get; set; }

    /// <summary>Photo folder opened last. Stored, but not reopened on the next start.</summary>
    public string? PhotosPath { get; set; }

    /// <summary>
    /// The deck new labels are drawn from (French or German). Remembered because a labelling session is
    /// one deck from start to finish: a validation set of German photos should not start every launch on
    /// the French suits.
    /// </summary>
    public string? Deck { get; set; }

    /// <summary>Which frames a video extraction keeps: every n-th. Remembered, because one set of videos
    /// is usually cut the same way.</summary>
    public int? FrameStep { get; set; }

    /// <summary>Where everything the tool remembers between runs lives.</summary>
    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "JassCardEye");

    private static string FilePath => Path.Combine(Folder, "viewer-settings.json");

    public static ViewerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ViewerSettings>(File.ReadAllText(FilePath)) ?? new ViewerSettings();
        }
        catch (Exception)
        {
            // A corrupt or unreadable settings file must never block the app – start with defaults.
        }
        return new ViewerSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Losing the settings is not worth interrupting the user's work.
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
