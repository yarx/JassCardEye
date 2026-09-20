using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace JassCardEye.Dataset.Viewer.Sessions;

/// <summary>
/// Analyse sessions: a session recorded in the app, its video and its recognition log on one
/// timeline. The log says what the model made of every frame; the video shows what it was looking at. Only
/// the two together make a wrong count explainable.
///
/// The Settings tab replays the open sessions with another threshold and stability rule, so the values the
/// app counts with can be worked out from real sessions of several devices rather than guessed.
///
/// Nothing is labelled here. A frame worth keeping is written next to its video, named like an extracted
/// frame, and handed to Label photos - one labeller, with one set of rules for what goes into a dataset.
/// </summary>
public partial class SessionsPage : UserControl
{
    /// <summary>The threshold both apps count with; a log without session info is taken to have used it.</summary>
    private const double AppThreshold = 0.6;

    private sealed record OpenSession(string VideoPath, SessionVideo Video, SessionAnalysis Analysis, SessionInfo? Info,
        ReplaySettings? Recorded, string RecordedSource)
    {
        public override string ToString() => Path.GetFileName(VideoPath);

        public ReportSession Report => new(Path.GetFileName(VideoPath), Analysis.Frames, Info, Recorded, RecordedSource);
    }

    private sealed record AnomalyItem(SessionAnomaly Anomaly)
    {
        public override string ToString()
        {
            var a = Anomaly;
            string what = a.Kind switch
            {
                AnomalyKind.Outlier => $"{a.Observed.Display} once, in a run of {a.Expected?.Display}",
                AnomalyKind.Ghost => $"{a.Observed.Display} out of nothing",
                AnomalyKind.FlipFlop => $"{a.Observed.Display} and {a.Expected?.Display} in turn, to frame {a.EndFrame + 1}",
                AnomalyKind.CommitMismatch => $"counted {a.Observed.Display}, around it {a.Expected?.Display}",
                _ => a.Kind.ToString(),
            };
            return $"Frame {a.Frame + 1}: {what}";
        }
    }

    private readonly List<OpenSession> _sessions = [];
    private readonly bool _ready;
    private string? _ffmpeg;
    private CancellationTokenSource? _opening;
    private CancellationTokenSource? _loading;
    private int _frame;
    private byte[]? _jpeg;   // the frame on screen, exactly as ffmpeg delivered it; null while it loads
    private ReplaySettings _settings = new(AppThreshold, StabilityRule.Run, 3);
    private bool _applying;
    private bool _settingsChosen;   // once settings were picked by hand, opening a session no longer replaces them

    public SessionsPage()
    {
        InitializeComponent();
        Timeline.FrameChosen += async frame => await ShowFrameAsync(frame);
        _ready = true;
        ApplySettings(_settings);
        UpdateControls();
    }

    /// <summary>The ffmpeg the window found; without it no session can be opened.</summary>
    public string? Ffmpeg
    {
        get => _ffmpeg;
        set
        {
            _ffmpeg = value;
            if (value is null)
                OpenStatus.Text = "✗ ffmpeg was not found, so no session can be opened. Install it (brew install ffmpeg) and start the tool again.";
            UpdateControls();
        }
    }

    /// <summary>A frame has been written for labelling: the folder of photos, and the photo.</summary>
    public event Func<string, string, Task>? LabelFrameRequested;

    private OpenSession? Selected => SessionList.SelectedItem as OpenSession;

    /// <summary>Opens sessions by their videos; each log is the file of the same name ending in .csv, its info the one ending in .json.</summary>
    public async Task OpenAsync(IReadOnlyList<string> videos)
    {
        if (_ffmpeg is not { } ffmpeg || _opening is not null || videos.Count == 0) return;

        _opening = new CancellationTokenSource();
        var cancellation = _opening.Token;
        var problems = new List<string>();
        bool first = _sessions.Count == 0;
        OpenSession? opened = null;
        UpdateControls();
        try
        {
            foreach (var video in videos)
            {
                var path = Path.GetFullPath(video);
                OpenStatus.Text = $"Opening {Path.GetFileName(path)} …";
                try
                {
                    // A session as the apps pack it: unpacked beside the archive, then opened by its video.
                    if (string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
                        path = await Task.Run(() => SessionArchive.Unpack(path), cancellation);
                    if (_sessions.Any(s => s.VideoPath == path)) continue;

                    var log = Path.ChangeExtension(path, ".csv");
                    if (!File.Exists(log))
                        throw new FileNotFoundException($"there is no {Path.GetFileName(log)} next to it.");
                    var frames = await Task.Run(() => SessionLog.Read(log), cancellation);
                    var sessionVideo = await SessionVideo.OpenAsync(ffmpeg, path, cancellation);
                    sessionVideo.CheckAgainst(frames);

                    SessionInfo? info = null;
                    try
                    {
                        info = SessionInfo.ReadBeside(path);
                    }
                    catch (Exception error) when (error is IOException or JsonException)
                    {
                        problems.Add($"{Path.GetFileName(path)}: opened without its session info, which cannot be read ({error.Message}).");
                    }
                    var (recorded, source) = await Task.Run(() => RecordedSettings(frames, info), cancellation);
                    opened = new OpenSession(path, sessionVideo, new SessionAnalysis(frames), info, recorded, source);
                    _sessions.Add(opened);
                }
                catch (Exception error) when (error is not OperationCanceledException)
                {
                    problems.Add($"{Path.GetFileName(path)}: {error.Message}");
                }
            }
            OpenStatus.Text = problems.Count == 0 ? "" : "✗ " + string.Join("   ", problems);
        }
        catch (OperationCanceledException)
        {
            OpenStatus.Text = "Opening cancelled.";
        }
        finally
        {
            _opening.Dispose();
            _opening = null;
            // The first session sets the settings to what it was counted with, so the replay starts from the app.
            if (first && !_settingsChosen && opened?.Recorded is { } recordedSettings) ApplySettings(recordedSettings);
            RefreshSessions(opened);
        }
    }

    /// <summary>Shows the tab of that header (Anomalies, Cards, Settings) - for scripted screenshots.</summary>
    public void ShowTab(string header)
    {
        var tab = SideTabs.Items.OfType<TabItem>()
            .FirstOrDefault(t => string.Equals(t.Header as string, header, StringComparison.OrdinalIgnoreCase));
        if (tab is not null) SideTabs.SelectedItem = tab;
    }

    /// <summary>One frame back or forward - ← and → in the window.</summary>
    public Task StepAsync(int delta) => Selected is null ? Task.CompletedTask : ShowFrameAsync(_frame + delta);

    /// <summary>Stops whatever ffmpeg is still doing, when the window closes.</summary>
    public void CancelWork()
    {
        _opening?.Cancel();
        _loading?.Cancel();
    }

    /// <summary>What a session was counted with: from its info, or else the rule that explains its counts.</summary>
    private static (ReplaySettings?, string) RecordedSettings(IReadOnlyList<SessionFrame> frames, SessionInfo? info)
    {
        if (info?.Settings is { } fromInfo) return (fromInfo, "session info");
        if (SessionReplay.RecordedWith(frames) is { } fromLog)
        {
            double threshold = info?.ConfidenceThreshold ?? AppThreshold;
            return (fromLog with { Threshold = threshold }, "rule worked out from the log, threshold assumed");
        }
        return (null, "");
    }

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        // While sessions are being opened the button cancels.
        if (_opening is not null)
        {
            _opening.Cancel();
            return;
        }
        var files = await TopLevel.GetTopLevel(this)!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose sessions - the ZIP from the app, or a video with its log beside it",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Sessions") { Patterns = ["*.zip", "*.mov", "*.mp4"] }],
        });
        await OpenAsync(files.Select(f => f.TryGetLocalPath()).OfType<string>().ToArray());
    }

    private void OnCloseSession(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } session) return;
        _loading?.Cancel();
        _sessions.Remove(session);
        RefreshSessions(null);
    }

    private void RefreshSessions(OpenSession? select)
    {
        var keep = select ?? (Selected is { } current && _sessions.Contains(current) ? current : _sessions.LastOrDefault());
        SessionList.ItemsSource = _sessions.ToArray();
        SessionList.SelectedItem = keep;
        TotalsTitle.Text = _sessions.Count == 1 ? "Over the open session" : $"Over all {_sessions.Count} open sessions";
        TotalsText.Text = Totals();
        UpdateAnalysis();
        UpdateControls();
    }

    private async void OnSessionChanged(object? sender, SelectionChangedEventArgs e)
    {
        var session = Selected;
        Timeline.Analysis = session?.Analysis;
        AnomalyList.ItemsSource = session?.Analysis.Anomalies.Select(a => new AnomalyItem(a)).ToArray();
        _jpeg = null;
        FrameView.Clear();
        FrameText.Text = "";
        UpdateAnalysis();
        UpdateControls();
        if (session is not null) await ShowFrameAsync(0);
    }

    private async void OnAnomalyChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (AnomalyList.SelectedItem is AnomalyItem item) await ShowFrameAsync(item.Anomaly.Frame);
    }

    private async void OnPrevious(object? sender, RoutedEventArgs e) => await StepAsync(-1);
    private async void OnNext(object? sender, RoutedEventArgs e) => await StepAsync(1);

    // --- Settings: replaying the open sessions ---

    private void OnThresholdOrFramesChanged(object? sender, NumericUpDownValueChangedEventArgs e) => ReadSettings();
    private void OnRuleChanged(object? sender, SelectionChangedEventArgs e) => ReadSettings();

    private void OnRecordedSettings(object? sender, RoutedEventArgs e)
    {
        if (Selected?.Recorded is { } recorded) ApplySettings(recorded);
    }

    private void ReadSettings()
    {
        if (!_ready || _applying) return;
        _settingsChosen = true;
        _settings = new ReplaySettings(
            (double)(ThresholdBox.Value ?? (decimal)AppThreshold),
            RuleBox.SelectedIndex == 1 ? StabilityRule.Majority : StabilityRule.Run,
            (int)(FramesBox.Value ?? 3));
        UpdateAnalysis();
    }

    private void ApplySettings(ReplaySettings settings)
    {
        _settings = settings with { Threshold = Math.Round(settings.Threshold, 2) };
        _applying = true;
        ThresholdBox.Value = (decimal)_settings.Threshold;
        RuleBox.SelectedIndex = _settings.Rule == StabilityRule.Majority ? 1 : 0;
        FramesBox.Value = _settings.Frames;
        _applying = false;
        UpdateAnalysis();
    }

    private void UpdateAnalysis()
    {
        var session = Selected;
        Timeline.Threshold = _settings.Threshold;
        Timeline.Replayed = session is null ? null : SessionReplay.Count(session.Analysis.Frames, _settings);
        CardsText.Text = session is null ? "Open a session to see how each card fared." : SessionReport.Cards(session.Report, _settings);
        SettingsText.Text = SessionReport.Settings(_sessions.Select(s => s.Report).ToArray(), _settings);
        RecordedButton.IsEnabled = session?.Recorded is not null;
    }

    // --- The frame on screen ---

    private async Task ShowFrameAsync(int frame)
    {
        if (Selected is not { } session) return;
        frame = Math.Clamp(frame, 0, session.Video.FrameCount - 1);
        _frame = frame;
        _jpeg = null;
        Timeline.Selected = frame;

        var row = session.Analysis.Frames[frame];
        FrameText.Text = string.Create(CultureInfo.InvariantCulture,
            $"Frame {frame + 1} of {session.Video.FrameCount} · {row.Milliseconds / 1000:0.000} s · ") +
            (row.Card is { } card ? string.Create(CultureInfo.InvariantCulture, $"{card.Display} {row.Confidence:0.000}") : "nothing") +
            (row.Committed ? " · counted" : "");
        UpdateControls();

        // Stepping quickly asks for frames faster than ffmpeg delivers them: only the last one asked for is shown.
        _loading?.Cancel();
        var loading = new CancellationTokenSource();
        _loading = loading;
        try
        {
            var jpeg = await session.Video.ReadFrameAsync(frame, loading.Token);
            if (loading.IsCancellationRequested) return;
            _jpeg = jpeg;
            FrameView.Show(new Bitmap(new MemoryStream(jpeg)), row);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!loading.IsCancellationRequested) FrameText.Text = $"✗ Frame {frame + 1}: {error.Message}";
        }
        finally
        {
            if (_loading == loading) _loading = null;
            loading.Dispose();
            UpdateControls();
        }
    }

    // Named and placed like an extracted frame - <video>_<frame from 1>.jpg in the folder named after the
    // video - so a sample in the dataset says which session and which frame it came from, and a frame
    // extracted from the same video later carries the same name.
    private async void OnLabelFrame(object? sender, RoutedEventArgs e)
    {
        if (Selected is not { } session || _jpeg is not { } jpeg || LabelFrameRequested is null) return;

        var folder = FrameExtractor.TargetFolder(session.VideoPath);
        var digits = Math.Max(5, session.Video.FrameCount.ToString(CultureInfo.InvariantCulture).Length);
        var number = (_frame + 1).ToString("D" + digits, CultureInfo.InvariantCulture);
        var file = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(session.VideoPath)}_{number}.jpg");
        try
        {
            Directory.CreateDirectory(folder);
            if (!File.Exists(file)) await File.WriteAllBytesAsync(file, jpeg);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            FrameText.Text = $"✗ The frame could not be written: {error.Message}";
            return;
        }
        await LabelFrameRequested(folder, file);
    }

    private string Totals()
    {
        if (_sessions.Count == 0) return "Open a session to count what looks wrong.";

        var totals = SessionAnalysis.Aggregate(_sessions.Select(s => s.Analysis));
        var text = new StringBuilder();
        void Section(string title, IEnumerable<string> lines)
        {
            text.AppendLine(title);
            int count = 0;
            foreach (var line in lines)
            {
                text.AppendLine("  " + line);
                count++;
            }
            if (count == 0) text.AppendLine("  none");
            text.AppendLine();
        }
        Section("Confused pairs", totals.Confusions.Select(p => $"{p.First.Display} ↔ {p.Second.Display}   {p.Count}×"));
        Section("Ghosts", totals.Ghosts.Select(g => $"{g.Card.Display}   {g.Count}×"));
        Section("Direct changes between cards", totals.Switches.Select(p => $"{p.First.Display} ↔ {p.Second.Display}   {p.Count}×"));
        return text.ToString().TrimEnd();
    }

    private void UpdateControls()
    {
        bool opening = _opening is not null;
        OpenButton.IsEnabled = _ffmpeg is not null;
        OpenButton.Content = opening ? "Cancel" : "Open sessions…";
        SessionList.IsEnabled = !opening && _sessions.Count > 0;
        CloseButton.IsEnabled = !opening && Selected is not null;
        PrevButton.IsEnabled = Selected is not null;
        NextButton.IsEnabled = Selected is not null;
        LabelButton.IsEnabled = _jpeg is not null && LabelFrameRequested is not null;
    }
}
