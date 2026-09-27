using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace JassCardEye.Dataset.Viewer.Simulate;

/// <summary>
/// Simulate table: renders a counting sequence in 3D and shows every frame with the label a dataset would
/// give it - the box of the top card, how much of it is visible, how it moves, and whether the frame is a
/// positive, a negative or in the ambiguous band - next to the 2D generator's picture of the same deck and
/// seed. Blender does the rendering in a process of its own (see src/tools/render3d/README.md); this page
/// writes what to render and shows what comes back.
/// </summary>
public partial class SimulatePage : UserControl
{
    private readonly string? _blender = TableSimulation.FindBlender();
    private readonly string? _repository = TableSimulation.FindRepository();
    private SimulatedSequence? _sequence;
    private CancellationTokenSource? _rendering;
    private bool _showing;

    public SimulatePage()
    {
        InitializeComponent();
        bool ready = _blender is not null && _repository is not null;
        BlenderStatus.Text = _blender is null
            ? "✗ Blender was not found, so nothing can be rendered. Install it (blender.org) or set $BLENDER."
            : _repository is null
                ? "✗ The repository was not found: run the tool from it (dotnet run), so the renderer and the card scans can be found."
                : $"✓ Blender found: {_blender}";
        BlenderStatus.Foreground = new SolidColorBrush(Color.Parse(ready ? "#8f8" : "#f88"));
        RenderButton.IsEnabled = ready;
    }

    private SimulationParams CurrentParams() => new(
        DeckBox.SelectedIndex == 1 ? "german" : "french",
        (int)(PlayedBox.Value ?? 0),
        Selected(TableBox),
        Selected(LightBox),
        HandBox.IsChecked == true,
        Selected(CameraBox),
        (int)(SeedBox.Value ?? 1),
        EngineBox.SelectedIndex == 1 ? "cycles" : "eevee");

    private static string Selected(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content as string ?? "random";

    private async void OnRender(object? sender, RoutedEventArgs e) => await RenderAsync();

    /// <summary>Renders the sequence the parameters describe and shows its first frame.</summary>
    public async Task RenderAsync(string? seed = null)
    {
        if (_blender is null || _repository is null) return;
        if (int.TryParse(seed, out int chosen)) SeedBox.Value = chosen;
        _rendering?.Cancel();
        _rendering = new CancellationTokenSource();
        var p = CurrentParams();
        RenderButton.IsEnabled = false;
        CancelButton.IsEnabled = true;
        ProgressText.Text = "Starting Blender …";
        var progress = new Progress<string>(line => ProgressText.Text = $"Rendering {line}");
        try
        {
            var sequence = await Task.Run(() => TableSimulation.RenderAsync(_blender, _repository, p, progress, _rendering.Token));
            ProgressText.Text = $"Rendered into {sequence.Folder}";
            await ShowSequenceAsync(sequence);
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Stopped.";
        }
        catch (Exception error)
        {
            ProgressText.Text = error.Message;
        }
        finally
        {
            RenderButton.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => _rendering?.Cancel();

    private async void OnOpen(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        var start = _repository is null ? null : await top.StorageProvider.TryGetFolderFromPathAsync(TableSimulation.RendersFolder(_repository));
        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "A rendered sequence (a folder with frames.json)",
            SuggestedStartLocation = start,
        });
        if (folders.Count == 1 && folders[0].TryGetLocalPath() is { } path) await OpenAsync(path);
    }

    /// <summary>Shows a render an earlier run left - also what JASSCARDEYE_OPEN opens in this section.</summary>
    public async Task OpenAsync(string folder)
    {
        if (!File.Exists(Path.Combine(folder, "frames.json")))
        {
            ProgressText.Text = $"No frames.json in {folder}.";
            return;
        }
        await ShowSequenceAsync(SimulatedSequence.Load(folder));
    }

    private async Task ShowSequenceAsync(SimulatedSequence sequence)
    {
        _sequence = sequence;
        _showing = true;
        FrameSlider.Maximum = Math.Max(0, sequence.Frames.Count - 1);
        FrameSlider.Value = 0;
        FrameSlider.IsEnabled = sequence.Frames.Count > 1;
        _showing = false;

        int positive = sequence.Frames.Count(f => f.Kind == "positive");
        int negative = sequence.Frames.Count(f => f.Kind == "negative");
        int difficult = sequence.Frames.Count - positive - negative;
        string rate = sequence.RenderRate is { } r ? string.Create(CultureInfo.InvariantCulture, $"{r:0.0} frames/s") : "–";
        SequenceText.Text = string.Create(CultureInfo.InvariantCulture,
            $"{sequence.Frames.Count} frames: {positive} positive, {negative} negative, {difficult} ambiguous (left out of a dataset). " +
            $"{sequence.Device}, {rate} rendering. Tilt {sequence.TiltDegrees:0}°, {sequence.Played} cards on the pile, seed {sequence.Seed}.");

        await ShowCounterpartAsync(sequence);
        await ShowFrameAsync(0);
    }

    private async Task ShowCounterpartAsync(SimulatedSequence sequence)
    {
        if (_repository is null) return;
        try
        {
            var (image, corners, card) = await Task.Run(() => Counterpart2D.Render(
                Path.Combine(_repository, "data", "cards"), sequence.Deck, sequence.Seed, sequence.Played, sequence.TiltDegrees, sequence.Size));
            CounterpartView.Show(image, corners, "positive", card, sequence.Size);
            CounterpartText.Text = $"Top card {card}. The 2D generator draws still piles only: no hand, no card in flight, a soft shadow under each card.";
        }
        catch (Exception error)
        {
            CounterpartView.Clear();
            CounterpartText.Text = $"The 2D picture could not be drawn: {error.Message}";
        }
    }

    private async void OnFrameChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_showing) await ShowFrameAsync((int)e.NewValue);
    }

    /// <summary>One frame back or on - what ← and → do in this section.</summary>
    public void Step(int delta)
    {
        if (_sequence is null) return;
        FrameSlider.Value = Math.Clamp(FrameSlider.Value + delta, 0, FrameSlider.Maximum);
    }

    private async Task ShowFrameAsync(int index)
    {
        if (_sequence is null || index < 0 || index >= _sequence.Frames.Count) return;
        var frame = _sequence.Frames[index];
        FrameNumber.Text = $"Frame {frame.Frame + 1} of {_sequence.Frames.Count}";
        if (frame.ImagePath is null || !File.Exists(frame.ImagePath))
        {
            FrameView.Clear();
            FrameText.Text = $"{frame.Kind}: {frame.Reason} (not rendered)";
            return;
        }
        var bitmap = await Task.Run(() => new Bitmap(frame.ImagePath));
        string caption = frame.Card is null ? frame.Kind : $"{frame.Kind} · {frame.Card}";
        FrameView.Show(bitmap, frame.Corners, frame.Kind, caption, _sequence.Size);
        var culture = CultureInfo.InvariantCulture;
        string visible = frame.Visibility is { } v ? string.Create(culture, $" Visible {v:P0}") : "";
        string motion = frame.Motion is { } m ? string.Create(culture, $", {m} (smear {frame.Travel:0.000} card heights)") : "";
        FrameText.Text = $"{char.ToUpperInvariant(frame.Kind[0])}{frame.Kind[1..]}: {frame.Reason}.{visible}{motion}" +
                         (frame.FlyingCard ? ", a card in the air" : "") + string.Create(culture, $". {frame.Time:0.00} s into the sequence.");
    }

    /// <summary>Stops a render still running, when the window closes.</summary>
    public void CancelWork() => _rendering?.Cancel();
}
