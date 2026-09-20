using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using JassCardEye.Dataset.Cards;
using JassCardEye.Dataset.Generation;
using JassCardEye.Dataset.Viewer.Predict;
using SkiaSharp;
using IOPath = System.IO.Path;

namespace JassCardEye.Dataset.Viewer;

public partial class MainWindow : Window
{
    private double _zoom = 1.0;
    private Bitmap? _bitmap;
    private List<string> _imageFiles = [];
    private int _currentIndex = -1;
    private string[] _classNames = [];
    private List<YoloLabel> _currentLabels = [];
    private string? _caption;

    private List<DatasetVariant> _variants = [];
    private DatasetVariant? _variant;
    private bool _populating;

    // Mode
    private bool _ready;
    private ViewerSection _section = ViewerSection.Browse;
    private bool LabelMode => _section == ViewerSection.Label;
    private bool _switchingSection; // set while the sidebar selection is changed from code

    // Correcting the label of a sample that is being viewed. Not a third mode but an addition to the
    // view: the same corners and the same class as when labelling, on the image already on screen.
    private bool _editing;
    private string? _editRoot;
    private string? _editStem;
    private bool _busy; // a rebuild is running – saving again meanwhile would race it
    private bool _deleteArmed; // deleting a sample takes two clicks; see DeleteSampleAsync

    /// <summary>True while four corners and a class are being placed, in either mode.</summary>
    private bool Annotating => LabelMode || _editing;

    // Label mode
    private readonly List<Avalonia.Point> _points = []; // in image pixel coordinates
    private int _dragIndex = -1;
    private VariantDatasetWriter? _writer;
    private string? _datasetPath;
    private List<string> _photoFiles = [];
    private readonly HashSet<string> _labeledStems = [];
    private bool _storedNegative; // the photo on screen is in the dataset as "No card"
    private readonly ViewerSettings _settings = ViewerSettings.Load();

    // Proposing a label with variant B. The proposer is looked for at start, like ffmpeg,
    // but loads nothing until ⇧→ is pressed for the first time. What is on screen is kept so the
    // status line can say what was proposed and the log can say whether it was taken as it came.
    private readonly CardProposer? _proposer;
    private Proposal? _proposed;
    private string _proposalNote = "";
    // Which photo a proposal was asked for. A proposal that arrives after the person has moved on is
    // about the photo before - it is dropped rather than placed on the wrong picture.
    private int _photoToken;

    // Extracting frames from a video. Looked up once at start: installing ffmpeg while the viewer runs
    // is rare enough to ask for a restart.
    private readonly string? _ffmpeg = FrameExtractor.FindFfmpeg();
    private CancellationTokenSource? _extraction;

    // The class being assigned, picked as deck, suit and rank. Kept as three parts rather than one card:
    // a card only exists once suit and rank are both chosen, and either may be chosen first.
    private Deck _deck;
    private Suit? _suit;
    private Rank? _rank;
    private Card? SelectedCard => _suit is { } suit && _rank is { } rank ? new Card(suit, rank) : null;

    // The current photo as an SKImage, needed to rectify the live crop preview.
    private SKImage? _sourceImage;
    private Bitmap? _cropBitmap;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".bmp"];

    private static readonly Color[] BoxColors =
    [
        Colors.Red, Colors.Lime, Colors.DeepSkyBlue, Colors.Yellow,
        Colors.Magenta, Colors.Orange, Colors.HotPink, Colors.Aquamarine,
        Colors.Coral, Colors.MediumPurple
    ];

    public MainWindow()
    {
        InitializeComponent();

        // The iOS app's icon. macOS shows a Dock icon exactly as given and does not round it the way iOS
        // does, so it gets the rounded, inset version a Mac app is expected to have; Windows and Linux
        // taskbars expect the full square.
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri(OperatingSystem.IsMacOS()
            ? "avares://JassCardEye.Dataset.Viewer/Assets/AppIcon-macOS.png"
            : "avares://JassCardEye.Dataset.Viewer/Assets/AppIcon.png")));

        // Restore the previous session so labelling can continue without picking folders again.
        _deck = Enum.TryParse<Deck>(_settings.Deck, out var deck) ? deck : Deck.French;
        FrameStepBox.Value = Math.Clamp(_settings.FrameStep ?? 4, 1, 600);
        UpdateFrameStepHint();
        FfmpegStatusText.Text = _ffmpeg is not null
            ? $"✓ ffmpeg found: {_ffmpeg}"
            : "✗ ffmpeg was not found, so no video can be chosen. Install it (brew install ffmpeg) and start the viewer again.";
        FfmpegStatusText.Foreground = new SolidColorBrush(Color.Parse(_ffmpeg is not null ? "#8f8" : "#f88"));
        if (_settings.DatasetPath is { } stored && Directory.Exists(stored))
            ApplyDatasetPath(stored);

        // Browsing and marking keys are caught on the way down, before a focused control sees them: the
        // sidebar list and the scrollable image area both use the arrow keys themselves, and a focused
        // button would take Space as a click.
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnReviewKeyUp, RoutingStrategies.Tunnel);

        // Variant B places a label to check instead of one to click: B₁ the corners, B₂ the card.
        (_proposer, var proposeStatus) = CardProposer.Discover();
        ProposeStatusText.Text = proposeStatus;
        ProposeStatusText.Foreground = new SolidColorBrush(Color.Parse(_proposer is not null ? "#8f8" : "#f88"));
        if (_proposer is not null) ToolTip.SetTip(ProposeStatusText, _proposer.Details);

        // A session frame worth labelling goes to Label photos, the one labeller there is.
        SessionsPage.Ffmpeg = _ffmpeg;
        SessionsPage.LabelFrameRequested += LabelSessionFrameAsync;
        Closed += (_, _) =>
        {
            SessionsPage.CancelWork();
            _proposer?.Dispose();
        };
        _ready = true;
        UpdateControlStates();
    }

    // --- Scripted session, for documentation and screenshots ---
    //
    // The viewer is driven by hand, but a screenshot for the thesis has to be repeatable and must
    // not depend on the machine granting screen recording. So the window can open a folder, jump
    // to an image, switch the edit mode on and render itself to a PNG, all from the environment:
    //
    //   JASSCARDEYE_MODE=label|extract|review|sessions
    //                                                start in that section instead of browsing
    //                                                (review: the folder of frames to go through)
    //   JASSCARDEYE_MARK=1                           mark the frame on screen for deletion (review)
    //   JASSCARDEYE_OPEN=<dataset or image folder>   what "Open folder…" would open (in Label photos:
    //                                                the folder of photos to label; in Analyse
    //                                                sessions: a session video)
    //   JASSCARDEYE_TAB=anomalies|cards|settings     the tab of Analyse sessions to show
    //   JASSCARDEYE_FILE=<part of an image name>     the image to show (first match)
    //   JASSCARDEYE_EDIT=1                           edit mode on: corners and rectified preview
    //   JASSCARDEYE_PROPOSE=1                        propose a label for the photo shown (Label photos)
    //   JASSCARDEYE_SNAPSHOT=<file.png>              render the window content there, then quit
    //
    // Rendered at 2x, which is what a Retina screen shows and what print needs.
    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Once the application is up: macOS takes the Dock icon from the running app, not the window.
        MacDockIcon.Apply(new Uri("avares://JassCardEye.Dataset.Viewer/Assets/AppIcon-macOS.png"));

        switch (Environment.GetEnvironmentVariable("JASSCARDEYE_MODE"))
        {
            case "sessions": await SelectSectionAsync(ViewerSection.Sessions); break;
            case "label":   await SelectSectionAsync(ViewerSection.Label);   break;
            case "extract": await SelectSectionAsync(ViewerSection.Extract); break;
            case "review":  await SelectSectionAsync(ViewerSection.Extract); break;
        }

        if (Environment.GetEnvironmentVariable("JASSCARDEYE_OPEN") is { } open)
        {
            if (_section == ViewerSection.Sessions)
            {
                await SessionsPage.OpenAsync([open]);
                if (Environment.GetEnvironmentVariable("JASSCARDEYE_TAB") is { } tab) SessionsPage.ShowTab(tab);
            }
            else if (LabelMode)
            {
                await OpenPhotosFolderAsync(open);
            }
            else if (_section == ViewerSection.Extract)
            {
                await StartReviewAsync(open);
            }
            else
            {
                _variants = DiscoverVariants(open);
                if (_variants.Count == 0)
                {
                    StatusText.Text = "No images or variants found.";
                    return;
                }
                await PopulateVariantsAsync();
            }

            if (Environment.GetEnvironmentVariable("JASSCARDEYE_FILE") is { } part)
            {
                int index = _imageFiles.FindIndex(f => f.Contains(part, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    _currentIndex = index;
                    await LoadCurrentImageAsync();
                }
            }
            UpdateControlStates();

            if (Environment.GetEnvironmentVariable("JASSCARDEYE_MARK") == "1" && _reviewing && _currentIndex >= 0)
            {
                _marked.Add(_imageFiles[_currentIndex]);
                UpdateControlStates();
            }

            if (Environment.GetEnvironmentVariable("JASSCARDEYE_EDIT") == "1" && EditToggle.IsEnabled)
                EditToggle.IsChecked = true;   // OnEditToggled loads the corners and the preview

            if (Environment.GetEnvironmentVariable("JASSCARDEYE_PROPOSE") == "1" && LabelMode)
                await ProposeAsync();          // what ⇧→ would place on this photo
        }

        if (Environment.GetEnvironmentVariable("JASSCARDEYE_SNAPSHOT") is not { } snapshot) return;
        await Task.Delay(1500);            // let the edit mode and the layout settle
        if (Content is not Control root) return;
        var size = new PixelSize((int)(root.Bounds.Width * 2), (int)(root.Bounds.Height * 2));
        using var bitmap = new RenderTargetBitmap(size, new Avalonia.Vector(192, 192));
        bitmap.Render(root);
        bitmap.Save(snapshot);
        Close();
    }

    // Enables only the controls that can actually do something in the current state.
    private void UpdateControlStates()
    {
        bool hasList = _imageFiles.Count > 0;
        bool hasImage = _bitmap is not null && _currentIndex >= 0 && _currentIndex < _imageFiles.Count;

        // Which parts belong to the current section – kept here so there is one place that decides.
        bool sessions = _section == ViewerSection.Sessions;
        SessionsPage.IsVisible = sessions;
        bool extract = _section == ViewerSection.Extract;
        bool review = extract && _reviewing;
        BrowseHeader.IsVisible = _section == ViewerSection.Browse;
        LabelHeader.IsVisible = LabelMode;
        ReviewHeader.IsVisible = review;
        ExtractPage.IsVisible = extract && !review;
        ImageArea.IsVisible = !sessions && (!extract || review);
        StatusBar.IsVisible = !sessions && (!extract || review);
        AnnotateBar.IsVisible = Annotating;

        // Shared navigation + zoom.
        PrevButton.IsEnabled = hasList;
        NextButton.IsEnabled = hasList;
        ZoomInButton.IsEnabled = hasImage;
        ZoomOutButton.IsEnabled = hasImage;
        ResetButton.IsEnabled = hasImage;

        // View mode.
        VariantSelector.IsEnabled = _variants.Count > 0;
        EditToggle.IsEnabled = _editing || CurrentSample() is not null;

        // Extract: without ffmpeg no video can be chosen. While an extraction runs, its button cancels
        // it, and neither the video nor the step it was started with can change.
        bool extracting = _extraction is not null;
        ChooseVideoButton.IsEnabled = _ffmpeg is not null && !extracting;
        FrameStepBox.IsEnabled = !extracting;
        ExtractButton.IsEnabled = extracting || (_ffmpeg is not null && _videoPath is not null && !_targetBlocked);
        ExtractButton.Content = extracting ? "Cancel" : "Extract frames";
        ExtractProgress.IsVisible = extracting;
        LabelExtractedButton.IsVisible = !extracting && _extractedFolder is not null;
        ReviewExtractedButton.IsVisible = LabelExtractedButton.IsVisible;
        ExtractResultRow.IsVisible = !string.IsNullOrEmpty(ExtractResultText.Text);

        // Review: what is marked, and the two clicks it takes to delete it.
        var frame = review && hasImage ? _imageFiles[_currentIndex] : null;
        bool frameMarked = frame is not null && _marked.Contains(frame);
        MarkedOverlay.IsVisible = frameMarked;
        MarkButton.IsChecked = frameMarked;
        MarkButton.IsEnabled = frame is not null && !_busy;
        DeleteMarkedButton.IsEnabled = _marked.Count > 0 && !_busy;
        DeleteMarkedButton.Content = _marked.Count == 0 ? "Delete marked"
            : _deleteMarkedArmed ? $"Confirm: delete {_marked.Count}"
            : $"Delete {_marked.Count} marked";
        UnmarkAllButton.IsEnabled = _marked.Count > 0 && !_busy;
        if (review && !_busy)
            ReviewStatusText.Text = $"{_marked.Count} of {_reviewFiles.Count} marked";

        // Both annotation paths save the same thing; only the target differs – a new sample in the
        // label mode's dataset, an existing one in edit mode.
        bool canSave = hasImage && !_busy &&
            (_editing ? _editStem is not null : LabelMode && _datasetPath is not null);
        SetPickerEnabled(Annotating && hasImage);
        SaveButton.IsEnabled = canSave && _points.Count == 4 && SelectedCard is not null;
        RotateButton.IsEnabled = Annotating && _points.Count == 4;
        ClearButton.IsEnabled = Annotating && hasImage && _points.Count > 0;
        NegativeButton.IsEnabled = canSave;
        // Only ever in edit mode: in the label mode the list holds the original photos, and nothing in
        // this window has any business deleting those.
        DeleteButton.IsVisible = _editing;
        DeleteButton.IsEnabled = _editing && !_busy && _editStem is not null;
        DeleteButton.Content = _deleteArmed ? "Confirm delete" : "Delete sample";
        NextUnlabelledButton.IsVisible = LabelMode;
        NextUnlabelledButton.IsEnabled = LabelMode && hasList;
        // Proposing belongs to labelling new photos: a sample being corrected already has a label,
        // and correcting it is the one job a proposal must not take over.
        ProposeButton.IsVisible = LabelMode && _proposer is not null;
        ProposeButton.IsEnabled = LabelMode && hasImage && !_busy;
        NextFlaggedButton.IsVisible = _editing;
        NextFlaggedButton.IsEnabled = _editing && hasList;
    }

    // --- Sections ---
    //
    // Four sections behind the sidebar: browsing a dataset (and correcting its labels), labelling new
    // photos into it, extracting frames from a video, and analysing a session recorded in the app. Browse
    // and Label share the image area but each has its own list of images; Extract and Analyse sessions have
    // pages of their own and leave both lists alone.

    // Whose images the canvas currently holds, so coming back from Extract does not start over.
    private ViewerSection _imagesOf = ViewerSection.Browse;

    private async void OnSectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _switchingSection || NavList.SelectedIndex < 0) return;
        await ApplySectionAsync((ViewerSection)NavList.SelectedIndex);
    }

    // Switches the section from code, with the sidebar following along.
    private async Task SelectSectionAsync(ViewerSection section)
    {
        _switchingSection = true;
        NavList.SelectedIndex = (int)section;
        _switchingSection = false;
        await ApplySectionAsync(section);
    }

    private async Task ApplySectionAsync(ViewerSection section)
    {
        if (section == _section) return;
        _section = section;
        _points.Clear();
        _dragIndex = -1;

        // Editing belongs to browsing – leaving it also leaves the correction.
        if (section != ViewerSection.Browse && _editing)
        {
            _editing = false;
            _editRoot = _editStem = null;
            EditToggle.IsChecked = false;
        }

        // The extraction and sessions pages have no images in the shared area; a review of extracted frames does.
        if (section == ViewerSection.Sessions || (section == ViewerSection.Extract && !_reviewing))
        {
            UpdateCropPreview();
            UpdateControlStates();
            return;
        }

        bool label = section == ViewerSection.Label;
        if (!label)
        {
            _sourceImage?.Dispose();
            _sourceImage = null;
        }
        UpdateCropPreview();

        if (label) EnsureClassPicker();

        // Each section has its own image set: browse = current variant, label = chosen photos. Coming
        // back to the set already on the canvas keeps the position.
        if (section != _imagesOf)
        {
            _imageFiles = section switch
            {
                ViewerSection.Label   => _photoFiles,
                ViewerSection.Extract => _reviewFiles,
                _                     => _variant?.Images ?? [],
            };
            _currentIndex = _imageFiles.Count > 0 ? 0 : -1;
            _imagesOf = section;
        }

        if (_currentIndex < 0)
        {
            _bitmap = null;
            _currentLabels = [];
            _caption = null;
            ImageCanvas.Children.Clear();
            IndexText.Text = "";
            if (label) LabelStatus.Text = "";
            StatusText.Text = section switch
            {
                ViewerSection.Label   => "Choose a folder of photos to start labelling.",
                ViewerSection.Extract => "No frames left in this folder.",
                _                     => "Open a dataset folder or an image.",
            };
            UpdateControlStates();
            return;
        }

        await LoadCurrentImageAsync();
    }

    // --- Class picker: deck, suit, rank ---

    // The names printed on the cards and used at the table. Only the screen uses them; the stored label
    // keeps its token (acorns_queen), which is what the app and the training read.
    private static string SuitName(Suit suit) => suit switch
    {
        Suit.Clubs    => "Kreuz",
        Suit.Diamonds => "Ecken",
        Suit.Hearts   => "Herz",
        Suit.Spades   => "Schaufel",
        Suit.Acorns   => "Eichel",
        Suit.Roses    => "Rosen",
        Suit.Bells    => "Schellen",
        Suit.Shields  => "Schilten",
        _ => suit.ToToken(),
    };

    // The face cards are named by deck: the German deck prints Under and Ober, the French one a Bauer
    // and a Dame. The class behind them is the same rank either way (jack, queen).
    private static string RankName(Rank rank, Deck deck) => (rank, deck) switch
    {
        (Rank.Jack, Deck.French)  => "Bauer",
        (Rank.Queen, Deck.French) => "Dame",
        (Rank.Jack, _)            => "Under",
        (Rank.Queen, _)           => "Ober",
        (Rank.King, _)            => "König",
        (Rank.Ace, _)             => "Ass",
        _ => rank.ToToken(),
    };

    // The key that picks each rank, shown in its tooltip. 0 is the ten, the letters are the first
    // letters of the names on the card.
    private static string RankKey(Rank rank, Deck deck) => (rank, deck) switch
    {
        (Rank.Ten, _)             => "0",
        (Rank.Jack, Deck.French)  => "U or B",
        (Rank.Jack, _)            => "U",
        (Rank.Queen, _)           => "O",   // D would be the Dame, but D switches the deck
        (Rank.King, _)            => "K",
        (Rank.Ace, _)             => "A",
        _ => rank.ToToken(),
    };

    private List<Suit> DeckSuits => [.. JassDeck.Suits.Where(s => s.Deck() == _deck)];

    /// <summary>Builds the rank and suit buttons on first use; later calls only refresh them.</summary>
    private void EnsureClassPicker()
    {
        if (RankButtons.Children.Count == 0)
        {
            foreach (var rank in JassDeck.Ranks)
            {
                var button = new ToggleButton
                {
                    Content = RankName(rank, _deck),
                    MinWidth = 38,
                    HorizontalContentAlignment = HorizontalAlignment.Center,
                    Tag = rank,
                };
                button.Click += (_, _) => PickRank(rank);
                RankButtons.Children.Add(button);
            }
        }
        if (SuitButtons.Children.Count == 0) BuildSuitButtons();
        RefreshPicker();
    }

    // One button per suit of the current deck: the suit's drawing from the app, then its name. The
    // drawing sits on a light tile, because Kreuz and Schaufel are printed black and would vanish on the
    // dark button.
    private void BuildSuitButtons()
    {
        SuitButtons.Children.Clear();
        int key = 1;
        foreach (var suit in DeckSuits)
        {
            var mark = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xEC, 0xEC)),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(2),
                Child = new Image { Source = SuitMarks.For(suit), Width = 18, Height = 18 },
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            content.Children.Add(mark);
            content.Children.Add(new TextBlock { Text = SuitName(suit), VerticalAlignment = VerticalAlignment.Center });

            var button = new ToggleButton { Content = content, Tag = suit };
            ToolTip.SetTip(button, $"Key {key++}");
            button.Click += (_, _) => PickSuit(suit);
            SuitButtons.Children.Add(button);
        }
    }

    private void PickSuit(Suit suit)
    {
        _suit = suit;
        RefreshPicker();
    }

    // Keys 1-4 in the order the suit buttons stand in.
    private void PickSuitAt(int slot)
    {
        var suits = DeckSuits;
        if (slot < suits.Count) PickSuit(suits[slot]);
    }

    private void PickRank(Rank rank)
    {
        _rank = rank;
        RefreshPicker();
    }

    private void OnDeckClick(object? sender, RoutedEventArgs e) =>
        SetDeck(sender == GermanDeckButton ? Deck.German : Deck.French, remember: true);

    /// <summary>
    /// Switches the deck the suit buttons offer. The suit does not carry over: Ecken and Schellen play
    /// the same role at the table, but a suit chosen in one deck and silently read in the other is exactly
    /// the wrong label this picker exists to prevent. The rank does carry over - it is the same on both.
    /// </summary>
    private void SetDeck(Deck deck, bool remember)
    {
        if (deck != _deck)
        {
            _deck = deck;
            _suit = null;
            BuildSuitButtons();
        }
        if (remember)
        {
            _settings.Deck = deck.ToString();
            _settings.Save();
        }
        RefreshPicker();
    }

    // Shows a stored label in the picker. Not remembered as the session's deck: opening one French label
    // while correcting a German set should not turn the next launch French.
    private void SelectCard(Card card)
    {
        SetDeck(card.Suit.Deck(), remember: false);
        _suit = card.Suit;
        _rank = card.Rank;
        RefreshPicker();
    }

    private void RefreshPicker()
    {
        FrenchDeckButton.IsChecked = _deck == Deck.French;
        GermanDeckButton.IsChecked = _deck == Deck.German;
        foreach (var button in SuitButtons.Children.OfType<ToggleButton>())
            button.IsChecked = button.Tag is Suit suit && suit == _suit;
        foreach (var button in RankButtons.Children.OfType<ToggleButton>())
        {
            if (button.Tag is not Rank rank) continue;
            button.IsChecked = rank == _rank;
            // The rank stays selected across a deck switch, but its name follows the deck: a queen is
            // an Ober in the German deck and a Dame in the French one.
            button.Content = RankName(rank, _deck);
            ToolTip.SetTip(button, $"Key {RankKey(rank, _deck)}");
        }

        ClassText.Text = SelectedCard is { } card
            ? $"{SuitName(card.Suit)} {RankName(card.Rank, card.Suit.Deck())}  ·  {JassClasses.Names[card.ClassId]}"
            : _suit is null && _rank is null ? "choose suit and rank"
            : _suit is null ? "choose the suit" : "choose the rank";
        UpdateControlStates();
    }

    private void SetPickerEnabled(bool enabled)
    {
        FrenchDeckButton.IsEnabled = enabled;
        GermanDeckButton.IsEnabled = enabled;
        foreach (var button in SuitButtons.Children) button.IsEnabled = enabled;
        foreach (var button in RankButtons.Children) button.IsEnabled = enabled;
    }

    // --- Correcting a label from the view ---

    /// <summary>
    /// The dataset sample the image on screen belongs to, or null when there is none to correct: a
    /// plain image folder has no labels, and a folder of crops may hold images the dataset does not.
    /// </summary>
    private (string Root, string Stem)? CurrentSample()
    {
        if (LabelMode || _variant?.Root is not { } root) return null;
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return null;
        if (!SampleLabels.IsAnnotated(root)) return null;

        var stem = IOPath.GetFileNameWithoutExtension(_imageFiles[_currentIndex]);
        return SampleLabels.FindImage(root, stem) is not null ? (root, stem) : null;
    }

    private async void OnEditToggled(object? sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        bool wanted = EditToggle.IsChecked == true;
        if (wanted == _editing) return;

        _editing = wanted;
        _deleteArmed = false;
        _points.Clear();
        _dragIndex = -1;

        if (_editing)
        {
            EnsureClassPicker();
        }
        else
        {
            _editRoot = _editStem = null;
            _sourceImage?.Dispose();
            _sourceImage = null;
        }

        await LoadCurrentImageAsync();
        UpdateControlStates();
    }

    /// <summary>
    /// Writes a correction and brings the dataset back in step with it. The derived variants are
    /// rebuilt rather than patched: that is the same code a training run performs, so what appears on
    /// screen afterwards is what the training would see. Rebuilding the whole validation set takes
    /// a few seconds, which is cheaper than a second way of updating the same files.
    /// </summary>
    private async Task ApplyCorrectionAsync(string root, string stem, Action write, string? done = null)
    {
        _busy = true;
        LabelStatus.Text = $"Updating {stem} and rebuilding the variants…";
        UpdateControlStates();

        try
        {
            await Task.Run(() =>
            {
                write();
                new DatasetRebuilder().Rebuild(root);
            });
        }
        catch (Exception ex)
        {
            LabelStatus.Text = "Error while saving: " + ex.Message;
            return;
        }
        finally
        {
            _busy = false;
        }

        // The correction can have moved the sample into a different class folder, so the variant's
        // file list is stale. It is re-read and the position restored – a correction is usually
        // checked on the spot rather than followed by the next image.
        await RefreshVariantsAsync(stem);
        LabelStatus.Text = done ?? $"Saved {stem} · all variants rebuilt";
    }

    // Re-reads the variants of the open dataset, keeping the selected one and returning to the given
    // sample, so saving does not throw the browsing position away.
    private async Task RefreshVariantsAsync(string stem)
    {
        if (_variant?.Root is not { } root) return;

        var name = _variant.Name;
        var rediscovered = DiscoverVariants(root);
        if (rediscovered.Count == 0) return;

        _variants = rediscovered;
        int index = Math.Max(0, _variants.FindIndex(v => v.Name == name));

        _populating = true;
        VariantSelector.ItemsSource = _variants.Select(v => v.Name).ToList();
        VariantSelector.SelectedIndex = index;
        _populating = false;

        _variant = _variants[index];
        _imageFiles = _variant.Images;
        _imagesOf = ViewerSection.Browse;
        _classNames = _variant.ClassNames;

        // A sample turned into a negative disappears from the crop variant, so fall back to where the
        // browsing was rather than to the start of the list.
        int found = _imageFiles.FindIndex(f => IOPath.GetFileNameWithoutExtension(f) == stem);
        _currentIndex = found >= 0
            ? found
            : Math.Min(Math.Max(_currentIndex, 0), _imageFiles.Count - 1);

        await LoadCurrentImageAsync();
    }

    private async void OnDeleteSample(object? sender, RoutedEventArgs e) => await DeleteSampleAsync();

    /// <summary>
    /// Removes the sample on screen from the dataset – image and labels – for a frame that cannot be
    /// annotated correctly at all.
    ///
    /// It takes two clicks, because it deletes files rather than rewriting them. It stays recoverable:
    /// the images and the source labels are version-controlled, so `git checkout` brings a sample back.
    /// Nothing in this window does, though, which is why it asks.
    /// </summary>
    private async Task DeleteSampleAsync()
    {
        if (!_editing || _busy) return;
        if (_editRoot is not { } root || _editStem is not { } stem) return;

        if (!_deleteArmed)
        {
            _deleteArmed = true;
            UpdateControlStates();
            LabelStatus.Text = $"Click again to delete {stem} – or move to another image to keep it.";
            return;
        }

        _deleteArmed = false;
        await ApplyCorrectionAsync(root, stem, () => SampleLabels.DeleteSample(root, stem),
            $"Deleted {stem} · all variants rebuilt");
    }

    private async void OnNextFlagged(object? sender, RoutedEventArgs e) => await NextFlaggedAsync();

    /// <summary>
    /// Jumps to the next sample whose label touches the image border. That is the fault which stops a
    /// training run before it starts – a card cut off by the frame cannot be annotated correctly – and
    /// finding it by eye among hundreds of frames is exactly the tedious part.
    /// </summary>
    private async Task NextFlaggedAsync()
    {
        if (_variant?.Root is not { } root || _imageFiles.Count == 0) return;

        for (int step = 1; step <= _imageFiles.Count; step++)
        {
            int index = (_currentIndex + step) % _imageFiles.Count;
            if (!LabelTouchesBorder(root, IOPath.GetFileNameWithoutExtension(_imageFiles[index]))) continue;

            _currentIndex = index;
            await LoadCurrentImageAsync();
            return;
        }
        LabelStatus.Text = "No label in this variant touches the border.";
    }

    // The threshold of src/tools/check_dataset.py, so this finds exactly what would fail there - unlike
    // the live warning while placing corners, which is deliberately more cautious.
    // Reading the corners against a 1×1 image yields them normalised, so testing every sample of a
    // dataset costs a few hundred small file reads and no image decoding at all.
    private static bool LabelTouchesBorder(string root, string stem)
    {
        var corners = SampleLabels.ReadCorners(root, stem, 1, 1);
        return corners.Length == 4 && corners.Any(c =>
            c.X <= 0.0005f || c.Y <= 0.0005f || c.X >= 0.9995f || c.Y >= 0.9995f);
    }

    // --- View mode: opening ---

    private async void OnOpenFolderClick(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a dataset or image folder",
            AllowMultiple = false
        });
        if (folders.Count == 0) return;

        var folderPath = folders[0].TryGetLocalPath();
        if (folderPath is null) return;

        _variants = DiscoverVariants(folderPath);
        if (_variants.Count == 0)
        {
            StatusText.Text = "No images or variants found.";
            return;
        }
        await PopulateVariantsAsync();
    }

    private async void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose an image",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Images") { Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"] }]
        });
        if (files.Count == 0) return;

        var path = files[0].TryGetLocalPath();
        if (path is null) return;

        _variants =
        [
            new DatasetVariant
            {
                Name = "Image",
                Kind = VariantKind.Plain,
                Images = [path],
                ClassNames = LoadClassNames(IOPath.GetDirectoryName(path) ?? ""),
            }
        ];
        await PopulateVariantsAsync();
    }

    private async Task PopulateVariantsAsync()
    {
        _populating = true;
        VariantSelector.ItemsSource = _variants.Select(v => v.Name).ToList();
        VariantSelector.SelectedIndex = 0;
        _populating = false;
        await ApplyVariantAsync(0);
    }

    private async void OnVariantChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_populating || LabelMode) return;
        await ApplyVariantAsync(VariantSelector.SelectedIndex);
    }

    private async Task ApplyVariantAsync(int index)
    {
        if (index < 0 || index >= _variants.Count) return;
        _variant = _variants[index];
        _imageFiles = _variant.Images;
        _imagesOf = ViewerSection.Browse;
        _classNames = _variant.ClassNames;
        _currentIndex = _imageFiles.Count > 0 ? 0 : -1;
        await LoadCurrentImageAsync();
    }

    // --- Label mode: choosing photos & saving ---

    private async void OnChooseDataset(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the target dataset for the labels (created/extended)", AllowMultiple = false
        });
        if (folders.Count == 0) return;
        if (folders[0].TryGetLocalPath() is not { } dir) return;

        ApplyDatasetPath(dir);
        UpdateLabelStatus();
        UpdateControlStates();
    }

    // Remembers the dataset, reads back which photos are already labelled and persists the choice.
    private void ApplyDatasetPath(string path)
    {
        _datasetPath = path;
        _writer = null; // created on first save, so merely choosing a path writes nothing
        DatasetPathText.Text = path;

        _settings.DatasetPath = path;
        _settings.Save();

        RescanLabelled();
    }

    private void RescanLabelled()
    {
        _labeledStems.Clear();
        if (_datasetPath is null) return;

        var imagesDir = IOPath.Combine(_datasetPath, "images");
        if (!Directory.Exists(imagesDir)) return;
        foreach (var f in Directory.EnumerateFiles(imagesDir).Where(IsImageFile))
            _labeledStems.Add(IOPath.GetFileNameWithoutExtension(f));
    }

    private async void OnChoosePhotos(object? sender, RoutedEventArgs e)
    {
        var photoFolders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder with the photos to label", AllowMultiple = false
        });
        if (photoFolders.Count == 0) return;
        if (photoFolders[0].TryGetLocalPath() is not { } photosDir) return;

        await OpenPhotosFolderAsync(photosDir);
    }

    // Opens a folder of photos for labelling - chosen by hand, or just extracted from a video.
    private async Task OpenPhotosFolderAsync(string photosDir)
    {
        var photos = EnumerateImages(photosDir);

        // Skia cannot decode HEIC/HEIF, so such files are skipped. Say so instead of silently
        // leaving photos out of the reference set.
        int skipped = Directory.EnumerateFiles(photosDir)
            .Count(f => IOPath.GetExtension(f).ToLowerInvariant() is ".heic" or ".heif");
        var note = skipped > 0
            ? $"  ⚠ {skipped} HEIC/HEIF file(s) skipped – convert them to JPEG first."
            : "";

        if (photos.Count == 0)
        {
            StatusText.Text = "No usable photos found in the folder." + note;
            return;
        }
        if (skipped > 0) StatusText.Text = note.TrimStart();

        _settings.PhotosPath = photosDir;
        _settings.Save();

        PhotosPathText.Text = photosDir;
        _photoFiles = photos;
        _imageFiles = _photoFiles;
        _imagesOf = ViewerSection.Label;
        _currentIndex = 0;
        await LoadCurrentImageAsync();
    }

    // --- Extract: frames from a video ---

    private string? _videoPath;
    private bool _targetBlocked;      // the target folder exists and holds files
    private string? _extractedFolder; // the folder the last extraction wrote, offered for labelling

    private void OnFrameStepChanged(object? sender, NumericUpDownValueChangedEventArgs e)
    {
        UpdateFrameStepHint();
        if (!_ready || e.NewValue is not { } value) return;
        _settings.FrameStep = (int)value;
        _settings.Save();
    }

    private void UpdateFrameStepHint()
    {
        int step = (int)(FrameStepBox.Value ?? 4);
        FrameStepHint.Text = step == 1
            ? "Keeps every frame of the video."
            : $"Keeps frames 1, {1 + step}, {1 + 2 * step}, … – one frame in {step}.";
    }

    private async void OnChooseVideo(object? sender, RoutedEventArgs e)
    {
        if (_ffmpeg is null || _extraction is not null) return;

        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose the video to extract frames from",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Videos") { Patterns = ["*.mp4", "*.mov", "*.m4v", "*.avi", "*.mkv"] },
            ],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } video) return;

        _videoPath = video;
        _extractedFolder = null;
        ExtractResultText.Text = "";
        RefreshVideoTarget();
    }

    // Shows the chosen video and where its frames would go, and whether that folder is free.
    private void RefreshVideoTarget()
    {
        if (_videoPath is not { } video) return;

        var folder = FrameExtractor.TargetFolder(video);
        _targetBlocked = Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any();
        VideoPathText.Text = video;
        TargetFolderText.Text = folder;
        // Right after an extraction the folder is full because of it - that is the result, not a warning.
        TargetWarning.IsVisible = _targetBlocked && _extractedFolder != folder;
        UpdateControlStates();
    }

    private async void OnExtractFrames(object? sender, RoutedEventArgs e)
    {
        // The button cancels an extraction that is already running.
        if (_extraction is not null)
        {
            _extraction.Cancel();
            return;
        }
        if (_ffmpeg is not { } ffmpeg || _videoPath is not { } video || _targetBlocked) return;

        int step = (int)(FrameStepBox.Value ?? 4);
        var videoName = IOPath.GetFileName(video);

        _extraction = new CancellationTokenSource();
        _extractedFolder = null;
        ExtractResultText.Text = "";
        ExtractProgressText.Text = "Starting ffmpeg …";
        UpdateControlStates();

        // Progress reports arrive through the dispatcher and can trail the end of the run; once the
        // result is on screen, a late one must not overwrite it.
        bool running = true;
        var progress = new Progress<int>(frames =>
        {
            if (running) ExtractProgressText.Text = $"{frames} frame(s) written …";
        });

        try
        {
            var result = await FrameExtractor.ExtractAsync(ffmpeg, video, step, progress, _extraction.Token);
            running = false;
            _extractedFolder = result.Folder;
            ShowExtractResult($"✓ {result.Frames} frame(s) of {videoName} extracted.", "#8f8");
        }
        catch (OperationCanceledException)
        {
            running = false;
            ShowExtractResult($"Extraction of {videoName} cancelled – nothing was kept.", "#aaa");
        }
        catch (Exception ex)
        {
            running = false;
            ShowExtractResult($"✗ Extraction of {videoName} failed: {ex.Message}", "#f88");
        }
        finally
        {
            _extraction.Dispose();
            _extraction = null;
            ExtractProgressText.Text = "";
            RefreshVideoTarget(); // the folder exists now
        }
    }

    private void ShowExtractResult(string text, string color)
    {
        ExtractResultText.Text = text;
        ExtractResultText.Foreground = new SolidColorBrush(Color.Parse(color));
    }

    // Straight on to labelling what was just extracted.
    private async void OnLabelExtracted(object? sender, RoutedEventArgs e)
    {
        if (_extractedFolder is not { } folder) return;
        await SelectSectionAsync(ViewerSection.Label);
        await OpenPhotosFolderAsync(folder);
    }

    // A frame from Analyse sessions, already written next to its video: labelled like an extracted frame,
    // with the photo on screen being that frame. Going back to Analyse sessions finds the session as it was.
    private async Task LabelSessionFrameAsync(string folder, string file)
    {
        await SelectSectionAsync(ViewerSection.Label);
        await OpenPhotosFolderAsync(folder);
        int index = _imageFiles.FindIndex(f => IOPath.GetFullPath(f) == IOPath.GetFullPath(file));
        if (index >= 0 && index != _currentIndex)
        {
            _currentIndex = index;
            await LoadCurrentImageAsync();
        }
        UpdateControlStates();
    }

    // --- Extract: reviewing frames ---
    //
    // Extracting every n-th frame still keeps runs of near-identical frames, frames with a hand in the
    // way and frames with no card at all. Going through them before labelling is quicker than skipping
    // them one by one while labelling: flick through, mark, delete the marked ones together.

    private bool _reviewing;
    private string? _reviewFolder;
    private List<string> _reviewFiles = [];
    private readonly HashSet<string> _marked = new(StringComparer.Ordinal);
    private bool _deleteMarkedArmed; // deleting takes two clicks, like deleting a sample

    private async void OnReviewExtracted(object? sender, RoutedEventArgs e)
    {
        if (_extractedFolder is { } folder) await StartReviewAsync(folder);
    }

    private async void OnReviewFolder(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose the folder of frames to review", AllowMultiple = false
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder) return;
        await StartReviewAsync(folder);
    }

    private async Task StartReviewAsync(string folder)
    {
        _reviewFolder = folder;
        _reviewFiles = EnumerateImages(folder);
        _marked.Clear();
        _deleteMarkedArmed = false;
        _reviewing = true;
        ReviewFolderText.Text = folder;
        ToolTip.SetTip(DeleteMarkedButton, Trash.IsAvailable
            ? "Moves the marked frames to the Trash, from where they can be put back"
            : "Deletes the marked frames for good - there is no Trash to move them to");

        _imageFiles = _reviewFiles;
        _imagesOf = ViewerSection.Extract;
        await ShowReviewFrameAsync(0);
    }

    // Puts a frame of the review on screen, or says the folder is empty.
    private async Task ShowReviewFrameAsync(int index)
    {
        _imageFiles = _reviewFiles;
        _currentIndex = _reviewFiles.Count == 0 ? -1 : Math.Clamp(index, 0, _reviewFiles.Count - 1);
        if (_currentIndex >= 0)
        {
            await LoadCurrentImageAsync();
            return;
        }

        _bitmap = null;
        _currentLabels = [];
        _caption = null;
        ImageCanvas.Children.Clear();
        IndexText.Text = "";
        StatusText.Text = "No frames in this folder.";
        UpdateControlStates();
    }

    // ← and → browse wherever there are images; Space and X mark a frame while reviewing. Typing into
    // a field keeps its keys.
    private async void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is TextBox) return;
        if (_section == ViewerSection.Sessions)
        {
            if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None)
            {
                e.Handled = true;
                await SessionsPage.StepAsync(e.Key == Key.Left ? -1 : 1);
            }
            return;
        }
        if (_section == ViewerSection.Extract && !_reviewing) return;

        switch (e.Key)
        {
            case Key.Left or Key.Right when e.KeyModifiers == KeyModifiers.None:
                e.Handled = true;
                await NavigateAsync(e.Key == Key.Left ? -1 : 1);
                break;
            // ⇧→ is → with a proposal waiting on the next photo: the corners and the card from
            // variant B, to check and correct - never saved by anything but Enter.
            case Key.Left or Key.Right when LabelMode && e.KeyModifiers == KeyModifiers.Shift:
                e.Handled = true;
                await NavigateAsync(e.Key == Key.Left ? -1 : 1);
                await ProposeAsync();
                break;
            case Key.Space or Key.X when _section == ViewerSection.Extract:
                e.Handled = true;
                await ToggleMarkAsync();
                break;
        }
    }

    // The release of the same key is swallowed too. A focused control acts on a released Space - a
    // Button clicks - which would move the review on a second time.
    private void OnReviewKeyUp(object? sender, KeyEventArgs e)
    {
        if (_section == ViewerSection.Extract && _reviewing && e.Key is Key.Space or Key.X)
            e.Handled = true;
    }

    private async void OnToggleMark(object? sender, RoutedEventArgs e) => await ToggleMarkAsync();

    // Marks or unmarks the frame on screen. Marking moves on to the next frame, because deciding a frame
    // is done once it is marked; unmarking stays, because it is a correction of the frame on screen.
    private async Task ToggleMarkAsync()
    {
        if (!_reviewing || _busy || _currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        var frame = _imageFiles[_currentIndex];
        bool marked = _marked.Add(frame);
        if (!marked) _marked.Remove(frame);
        _deleteMarkedArmed = false;

        if (marked && _currentIndex < _imageFiles.Count - 1)
            await NavigateAsync(1);
        else
            UpdateControlStates();
    }

    private void OnUnmarkAll(object? sender, RoutedEventArgs e)
    {
        _marked.Clear();
        _deleteMarkedArmed = false;
        UpdateControlStates();
    }

    private async void OnDeleteMarked(object? sender, RoutedEventArgs e)
    {
        if (!_reviewing || _busy || _marked.Count == 0 || _reviewFolder is not { } folder) return;
        if (!_deleteMarkedArmed)
        {
            _deleteMarkedArmed = true;
            UpdateControlStates();
            return;
        }
        _deleteMarkedArmed = false;

        // Where to stand afterwards: the first frame from here on that stays, or else the last one before.
        var current = _currentIndex >= 0 ? _reviewFiles[_currentIndex] : null;
        var stay = _reviewFiles.Skip(Math.Max(0, _currentIndex)).FirstOrDefault(f => !_marked.Contains(f))
                   ?? _reviewFiles.LastOrDefault(f => !_marked.Contains(f));
        var victims = _reviewFiles.Where(_marked.Contains).ToList();

        _busy = true;
        ReviewStatusText.Text = $"Deleting {victims.Count} frame(s) …";
        UpdateControlStates();
        string result;
        try
        {
            await Task.Run(() => Trash.Move(victims));
            result = Trash.IsAvailable
                ? $"{victims.Count} frame(s) moved to the Trash."
                : $"{victims.Count} frame(s) deleted.";
        }
        catch (Exception ex)
        {
            result = "Deleting failed: " + ex.Message;
        }
        finally
        {
            _busy = false;
        }

        // Read the folder again rather than trusting the list: a batch may have failed half-way.
        _reviewFiles = EnumerateImages(folder);
        _marked.RemoveWhere(f => !File.Exists(f));
        int index = stay is not null ? _reviewFiles.IndexOf(stay) : -1;
        await ShowReviewFrameAsync(index >= 0 ? index : 0);
        ReviewStatusText.Text = $"{result}  {_marked.Count} of {_reviewFiles.Count} marked";
    }

    private async void OnLabelReviewed(object? sender, RoutedEventArgs e)
    {
        if (_reviewFolder is not { } folder) return;
        await SelectSectionAsync(ViewerSection.Label);
        await OpenPhotosFolderAsync(folder);
    }

    private void OnCloseReview(object? sender, RoutedEventArgs e)
    {
        _reviewing = false;
        _marked.Clear();
        _deleteMarkedArmed = false;
        UpdateControlStates();
    }

    private async void OnNextUnlabelled(object? sender, RoutedEventArgs e)
    {
        if (_imageFiles.Count == 0) return;

        for (int step = 1; step <= _imageFiles.Count; step++)
        {
            int index = (_currentIndex + step) % _imageFiles.Count;
            if (!_labeledStems.Contains(StemFor(_imageFiles[index])))
            {
                _currentIndex = index;
                await LoadCurrentImageAsync();
                return;
            }
        }
        LabelStatus.Text = "All photos in this folder are labelled.";
    }

    /// <summary>
    /// Output name for a photo: its file name. It keeps the link back to the original file, so a mistake
    /// can simply be re-labelled, overwriting the old entry. Telling sources apart is up to the names:
    /// frames extracted here carry their video's name.
    /// </summary>
    private static string StemFor(string imagePath) => IOPath.GetFileNameWithoutExtension(imagePath);

    // Creates the writer on first use, so picking a dataset path alone does not create directories.
    private VariantDatasetWriter? EnsureWriter()
    {
        if (_writer is not null) return _writer;
        if (_datasetPath is null) return null;

        // Real photos are normalised to 640×640 (aspect-preserving centre-crop) to match the generator.
        _writer = new VariantDatasetWriter(_datasetPath, DatasetTasks.All, ImageFormat.Jpeg, 90, 640);
        return _writer;
    }

    private async void OnSaveLabel(object? sender, RoutedEventArgs e) => await SaveLabelAsync();

    private async Task SaveLabelAsync()
    {
        if (!Annotating || _bitmap is null || _currentIndex < 0 || _busy) return;
        if (_points.Count != 4) { LabelStatus.Text = "Please set exactly 4 corners."; return; }
        if (SelectedCard is not { } card) { LabelStatus.Text = "Choose suit and rank first."; return; }

        var corners = _points.Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray();

        // A correction replaces the labels of an existing sample; the image stays as it is, since it
        // is version-controlled and re-encoding it would change the file without changing the picture.
        if (_editing)
        {
            if (_editRoot is not { } editRoot || _editStem is not { } editStem) return;
            int width = _bitmap.PixelSize.Width, height = _bitmap.PixelSize.Height;
            await ApplyCorrectionAsync(editRoot, editStem,
                () => SampleLabels.Write(editRoot, editStem, card, corners, width, height));
            return;
        }

        if (EnsureWriter() is not { } writer) { LabelStatus.Text = "Choose a dataset first."; return; }

        var path = _imageFiles[_currentIndex];
        var stem = StemFor(path);

        try
        {
            writer.Write(path, corners, card, stem);
            writer.WriteMetadata();
        }
        catch (Exception ex)
        {
            LabelStatus.Text = "Error while saving: " + ex.Message;
            return;
        }

        _labeledStems.Add(stem);
        LogProposal(stem, card);
        await NavigateAsync(1);
    }

    private async void OnNoCard(object? sender, RoutedEventArgs e) => await SaveNegativeAsync();

    private async Task SaveNegativeAsync()
    {
        if (!Annotating || _bitmap is null || _currentIndex < 0 || _busy) return;

        // Correcting a sample to "no card" removes its labels; the frame stays in the dataset, where
        // Ultralytics reads an image without a label as background - which is what it now is.
        if (_editing)
        {
            if (_editRoot is not { } editRoot || _editStem is not { } editStem) return;
            await ApplyCorrectionAsync(editRoot, editStem, () => SampleLabels.WriteNegative(editRoot, editStem));
            return;
        }

        if (EnsureWriter() is not { } writer) { LabelStatus.Text = "Choose a dataset first."; return; }

        var path = _imageFiles[_currentIndex];
        var stem = StemFor(path);

        try
        {
            writer.WriteNegative(path, stem);
            writer.WriteMetadata();
        }
        catch (Exception ex)
        {
            LabelStatus.Text = "Error while saving: " + ex.Message;
            return;
        }

        _labeledStems.Add(stem);
        // Worth its own line: a proposal on a photo that turns out to hold no card is the mistake
        // that would cost the most if nobody were looking.
        LogProposal(stem, null);
        await NavigateAsync(1);
    }

    // --- Proposing a label: B₁ places the corners, B₂ names the card ---

    private async void OnPropose(object? sender, RoutedEventArgs e) => await ProposeAsync();

    /// <summary>
    /// Puts what variant B makes of the photo on screen: four corners, and the card when B₂ is sure
    /// enough of one. Nothing is saved - afterwards the state is the same as if the corners had been
    /// clicked and the class picked, and it is corrected in exactly the same way.
    ///
    /// Two photos are left alone. One that is already in the dataset keeps its stored label - a
    /// proposal has no business overwriting the work of whoever labelled it. And one where corners or
    /// a class have already been placed by hand keeps those: the person was faster than the models.
    /// </summary>
    private async Task ProposeAsync()
    {
        if (!LabelMode || _proposer is null || _busy || _bitmap is null) return;
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        var path = _imageFiles[_currentIndex];
        if (_labeledStems.Contains(StemFor(path)))
        {
            _proposalNote = "already in the dataset – the stored label stands";
            UpdateLabelStatus();
            return;
        }
        if (_points.Count > 0 || SelectedCard is not null) return;

        int token = _photoToken;
        var deck = _deck;
        _proposalNote = "asking B₁ and B₂ …";
        UpdateLabelStatus();

        Proposal? proposal;
        try
        {
            // On a thread of its own: decoding the photo, cutting its square, one rectification and
            // two model calls are not much, but they are enough to make the window stutter under
            // someone's hands.
            proposal = await Task.Run(() => _proposer.ProposeAsync(path, deck, CancellationToken.None));
        }
        catch (Exception ex)
        {
            if (token == _photoToken)
            {
                _proposalNote = "proposing failed: " + ex.Message;
                UpdateLabelStatus();
            }
            return;
        }

        // While the models were thinking, the photo may have moved on or someone may have started
        // placing corners. Either way the answer is about a situation that no longer exists.
        if (token != _photoToken || _points.Count > 0 || SelectedCard is not null) return;

        if (proposal is null)
        {
            _proposalNote = $"no proposal – B₁ sees no card above {ProposalRules.MinBoxConfidence:0.00}";
            UpdateLabelStatus();
            return;
        }

        _proposed = proposal;
        foreach (var corner in proposal.Corners)
            _points.Add(new Avalonia.Point(corner.X, corner.Y));
        if (proposal.Card is { } card) SelectCard(card);

        _proposalNote = proposal.Describe();
        UpdateLabelStatus();
        RenderCanvas();
        UpdateControlStates();
        UpdateCropPreview();
    }

    /// <summary>
    /// What became of the proposed corners - unchanged, turned, or moved. It decides the colour they
    /// are drawn in and what the log records about them.
    /// </summary>
    private CornerChange CornerChangeNow() =>
        _proposed is { } proposal
            ? ProposalRules.Compare(proposal.Corners,
                [.. _points.Select(p => new Vector2((float)p.X, (float)p.Y))])
            : CornerChange.Moved;

    // Everything a proposal left behind, dropped as soon as another photo is on screen.
    private void ForgetProposal()
    {
        _photoToken++;
        _proposed = null;
        _proposalNote = "";
    }

    // One line per sample that was saved after a proposal: what was proposed, what was saved, and
    // whether the corners were taken as they came. See ProposalLog for why this is worth writing.
    private void LogProposal(string stem, Card? saved)
    {
        if (_proposed is not { } proposal || _proposer is null) return;
        ProposalLog.Write(stem, _proposer.WeightsId, proposal.Card, proposal.BoxConfidence,
            proposal.ClassConfidence, saved, CornerChangeNow());
    }

    private void OnRotateCorners(object? sender, RoutedEventArgs e) => RotateCorners();

    /// <summary>
    /// Moves the corner order round by one, turning the card in the crop preview by 90°. Two presses
    /// are the fix for a card annotated starting at the opposite corner - which is the mistake that
    /// actually happens, because the pips of a low Jass card look the same either way up. Dragging
    /// four corners into place again would say the same thing far more laboriously.
    /// </summary>
    private void RotateCorners()
    {
        if (_points.Count != 4) return;

        var first = _points[0];
        _points.RemoveAt(0);
        _points.Add(first);

        UpdateLabelStatus();
        RenderCanvas();
        UpdateCropPreview();
    }

    private void OnClearPoints(object? sender, RoutedEventArgs e) => ClearPoints();

    private void ClearPoints()
    {
        _points.Clear();
        _dragIndex = -1;
        UpdateLabelStatus();
        RenderCanvas();
        UpdateControlStates();
        UpdateCropPreview();
    }

    private void RemoveLastPoint()
    {
        if (_points.Count == 0) return;
        _points.RemoveAt(_points.Count - 1);
        UpdateLabelStatus();
        RenderCanvas();
        UpdateControlStates();
        UpdateCropPreview();
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!Annotating || _bitmap is null) return;
        var img = ToImagePoint(e.GetPosition(ImageCanvas));

        int hit = FindHandle(img);
        if (hit >= 0) { _dragIndex = hit; return; }
        if (_points.Count < 4)
        {
            _points.Add(img);
            UpdateLabelStatus();
            RenderCanvas();
            UpdateControlStates();
            UpdateCropPreview();
        }
    }

    private void OnCanvasPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!Annotating || _dragIndex < 0 || _bitmap is null) return;
        var img = ToImagePoint(e.GetPosition(ImageCanvas));
        _points[_dragIndex] = new Avalonia.Point(
            Math.Clamp(img.X, 0, _bitmap.PixelSize.Width),
            Math.Clamp(img.Y, 0, _bitmap.PixelSize.Height));
        RenderCanvas();
    }

    private void OnCanvasPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        bool wasDragging = _dragIndex >= 0;
        _dragIndex = -1;
        if (wasDragging) UpdateCropPreview(); // refresh once the corner has been placed
    }

    /// <summary>
    /// Renders the card as it would end up in the dataset – rectified to an upright rectangle. If the
    /// corners were clicked in the wrong order the card appears rotated or mirrored, which makes the
    /// mistake obvious before saving.
    /// </summary>
    private void UpdateCropPreview()
    {
        bool show = Annotating && _points.Count == 4 && _sourceImage is not null;
        CropPreviewPanel.IsVisible = show;
        if (!show) return;

        try
        {
            var corners = _points.Select(p => new Vector2((float)p.X, (float)p.Y)).ToArray();
            using var crop = CardCrop.Rectify(_sourceImage!, corners, 256);
            using var data = crop.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = new MemoryStream(data.ToArray());

            var previous = _cropBitmap;
            _cropBitmap = new Bitmap(stream);
            CropPreview.Source = _cropBitmap;
            previous?.Dispose();
        }
        catch (Exception)
        {
            // Degenerate corner placements can make the homography unsolvable – just hide the preview.
            CropPreviewPanel.IsVisible = false;
        }
    }

    private Avalonia.Point ToImagePoint(Avalonia.Point canvasPoint) =>
        new(canvasPoint.X / _zoom, canvasPoint.Y / _zoom);

    private int FindHandle(Avalonia.Point img)
    {
        double threshold = 10 / _zoom; // ~10 screen pixels
        for (int i = 0; i < _points.Count; i++)
        {
            var dx = _points[i].X - img.X;
            var dy = _points[i].Y - img.Y;
            if (dx * dx + dy * dy <= threshold * threshold) return i;
        }
        return -1;
    }

    private void UpdateLabelStatus()
    {
        if (!Annotating || _currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        // Only a fully visible card can be annotated correctly – a clipped one would need corners
        // outside the image. Flag it: a card clearly cut off is saved as a negative, and a nearly
        // complete one that only touches the border is not saved at all (or deleted when correcting).
        var warning = _points.Count == 4 && TouchesBorder()
            ? "  ⚠ card touches the border – clearly cut off: 'No card (N)'; only touching: skip it (or 'Delete sample')"
            : "";

        if (_editing)
        {
            LabelStatus.Text = _editStem is null
                ? "This image is not a sample of an annotated dataset."
                : $"{_points.Count}/4 corners (TL→TR→BR→BL) · correcting {_editStem}{warning}";
            return;
        }

        var stem = StemFor(_imageFiles[_currentIndex]);
        var done = !_labeledStems.Contains(stem) ? ""
            : _storedNegative ? " · stored as No card – saving a card replaces it"
            : " · already labelled – loaded, saving overwrites it";
        var proposal = _proposalNote.Length > 0 ? " · " + _proposalNote : "";
        LabelStatus.Text = $"{_points.Count}/4 corners (TL→TR→BR→BL) · saves as {stem}{done}{proposal}{warning}";
    }

    /// <summary>
    /// Shows what the dataset already holds for a photo being labelled, so a mistake is corrected by
    /// moving a corner or picking another rank and saving - not by placing everything again.
    ///
    /// The stored corners belong to the dataset's image, not to the photo: the writer cut the photo's
    /// central square and scaled it (see <c>VariantDatasetWriter.CenterCropSquare</c>), and the
    /// oriented label is normalised to that square. Reading them back is the same journey a proposal
    /// makes in the other direction, so both take it through <see cref="PhotoSquare"/>.
    /// </summary>
    private void LoadStoredLabel(string photoPath)
    {
        if (_bitmap is null || _datasetPath is not { } dataset) return;
        var stem = StemFor(photoPath);
        if (!_labeledStems.Contains(stem)) return;

        int width = _bitmap.PixelSize.Width, height = _bitmap.PixelSize.Height;
        int side = Math.Min(width, height);
        var square = PhotoSquare.Of(width, height, side);

        foreach (var c in SampleLabels.ReadCorners(dataset, stem, side, side))
        {
            var photo = square.ToPhoto(c);
            _points.Add(new Avalonia.Point(photo.X, photo.Y));
        }

        if (SampleLabels.ReadCard(dataset, stem) is { } card)
            SelectCard(card);
        else
            _storedNegative = _points.Count == 0; // in images/, but without a label: a negative
    }

    private bool TouchesBorder()
    {
        if (_bitmap is null) return false;
        double w = _bitmap.PixelSize.Width, h = _bitmap.PixelSize.Height;
        double margin = Math.Max(3, Math.Min(w, h) * 0.004);
        return _points.Any(p => p.X <= margin || p.Y <= margin || p.X >= w - margin || p.Y >= h - margin);
    }

    // --- Navigation / loading ---

    private async void OnPrevClick(object? sender, RoutedEventArgs e) => await NavigateAsync(-1);
    private async void OnNextClick(object? sender, RoutedEventArgs e) => await NavigateAsync(1);

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // The extraction page and the review have no shortcuts beyond those in OnTunnelKeyDown, which
        // also handles ← and → everywhere.
        if (_section is ViewerSection.Extract or ViewerSection.Sessions) return;

        // Letters and digits pick the class, but not while something is being typed into a text field.
        // Enter, Escape and the arrows stay live.
        bool typing = FocusManager?.GetFocusedElement() is TextBox;

        if (Annotating)
        {
            switch (e.Key)
            {
                case Key.D when !typing:        SetDeck(_deck == Deck.French ? Deck.German : Deck.French, remember: true); break;
                case Key.D1 or Key.NumPad1 when !typing: PickSuitAt(0);         break;
                case Key.D2 or Key.NumPad2 when !typing: PickSuitAt(1);         break;
                case Key.D3 or Key.NumPad3 when !typing: PickSuitAt(2);         break;
                case Key.D4 or Key.NumPad4 when !typing: PickSuitAt(3);         break;
                case Key.D6 or Key.NumPad6 when !typing: PickRank(Rank.Six);    break;
                case Key.D7 or Key.NumPad7 when !typing: PickRank(Rank.Seven);  break;
                case Key.D8 or Key.NumPad8 when !typing: PickRank(Rank.Eight);  break;
                case Key.D9 or Key.NumPad9 when !typing: PickRank(Rank.Nine);   break;
                case Key.D0 or Key.NumPad0 when !typing: PickRank(Rank.Ten);    break;
                case Key.U or Key.B when !typing: PickRank(Rank.Jack); break;
                case Key.O when !typing:        PickRank(Rank.Queen);  break;
                case Key.K when !typing:        PickRank(Rank.King);   break;
                case Key.A when !typing:        PickRank(Rank.Ace);    break;
                case Key.Enter:  await SaveLabelAsync();     break;
                case Key.Escape: ClearPoints();              break;
                case Key.Back:   RemoveLastPoint();          break;
                case Key.N:      await SaveNegativeAsync();  break;
                case Key.R:      RotateCorners();            break;
                case Key.F when _editing:   await NextFlaggedAsync(); break;
                case Key.Delete when _editing: await DeleteSampleAsync(); break;
                case Key.E when _editing:   EditToggle.IsChecked = false; break;
            }
            return;
        }

        switch (e.Key)
        {
            case Key.E when EditToggle.IsEnabled: EditToggle.IsChecked = true; break;
        }
    }

    private async Task NavigateAsync(int delta)
    {
        // A rebuild is recreating the very folders this list points into.
        if (_imageFiles.Count == 0 || _busy) return;
        _currentIndex = (_currentIndex + delta + _imageFiles.Count) % _imageFiles.Count;
        await LoadCurrentImageAsync();
    }

    private async Task LoadCurrentImageAsync()
    {
        if (_currentIndex < 0 || _currentIndex >= _imageFiles.Count) return;

        _deleteArmed = false; // an armed deletion belongs to the image it was armed on
        ForgetProposal();     // and a proposal belongs to the photo it was made for

        var imagePath = _imageFiles[_currentIndex];

        // A correction always belongs to the dataset's own frame, whatever the variant shows: a
        // rectified crop has no corners left to move, and its class lives in a folder name. So the
        // frame is put on screen instead, and the crop appears in the preview as it would be written.
        _editRoot = _editStem = null;
        if (_editing && CurrentSample() is { } sample &&
            SampleLabels.FindImage(sample.Root, sample.Stem) is { } framePath)
        {
            (_editRoot, _editStem) = sample;
            imagePath = framePath;
        }

        // The list can outlive the files it names - a rebuild refiles samples into other class
        // folders, and a dataset may be edited from elsewhere. Say so instead of dying on it.
        try
        {
            await using var stream = File.OpenRead(imagePath);
            _bitmap = new Bitmap(stream);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusText.Text = $"Could not open {IOPath.GetFileName(imagePath)} – reopen the folder.";
            return;
        }

        _currentLabels = [];
        _caption = null;

        if (Annotating)
        {
            _points.Clear();
            _dragIndex = -1;

            // Keep the photo around as an SKImage so the crop preview can be rectified from it.
            _sourceImage?.Dispose();
            _sourceImage = SKImage.FromEncodedData(SKData.Create(imagePath));

            // Every photo starts without a class. Carried over from the photo before, suit and rank
            // would be one Enter away from a wrong label that looks right - so after a save, which
            // moves on to the next photo, both have to be chosen again. The deck stays: a labelling
            // session is one deck.
            _suit = null;
            _rank = null;

            // A correction starts from what is stored, not from scratch – which is the whole point
            // when all that is wrong is the corner the annotation started at.
            _storedNegative = false;
            if (_editRoot is { } root && _editStem is { } stem)
            {
                foreach (var c in SampleLabels.ReadCorners(root, stem, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height))
                    _points.Add(new Avalonia.Point(c.X, c.Y));
                if (SampleLabels.ReadCard(root, stem) is { } card)
                    SelectCard(card);
            }
            else if (LabelMode)
            {
                LoadStoredLabel(imagePath);
            }
            if (RankButtons.Children.Count > 0) RefreshPicker();
        }
        else if (_section == ViewerSection.Extract)
        {
            // Frames under review have no labels to draw.
        }
        else
        {
            switch (_variant?.Kind)
            {
                case VariantKind.Classify:
                    _caption = ClassOf(imagePath);
                    break;
                case VariantKind.Detect when _variant.LabelsDir is { } dir:
                    _currentLabels = ParseLabelFile(IOPath.Combine(dir, IOPath.GetFileNameWithoutExtension(imagePath) + ".txt"));
                    break;
                default:
                    _currentLabels = LoadYoloLabels(imagePath);
                    break;
            }
        }

        _zoom = ComputeFitZoom();
        IndexText.Text = $"{_currentIndex + 1} / {_imageFiles.Count}";

        if (Annotating)
        {
            UpdateLabelStatus();
            StatusText.Text = $"{(_editing ? "Correcting" : "Labelling")}: {IOPath.GetFileName(imagePath)}"
                + $"  ({_bitmap.PixelSize.Width}×{_bitmap.PixelSize.Height})";
        }
        else if (_section == ViewerSection.Extract)
        {
            StatusText.Text = $"{IOPath.GetFileName(imagePath)}  ({_bitmap.PixelSize.Width}×{_bitmap.PixelSize.Height})";
        }
        else
        {
            StatusText.Text = _caption is not null
                ? $"{IOPath.GetFileName(imagePath)}  ({_bitmap.PixelSize.Width}×{_bitmap.PixelSize.Height})  Class: {_caption}"
                : $"{IOPath.GetFileName(imagePath)}  ({_bitmap.PixelSize.Width}×{_bitmap.PixelSize.Height})  {_currentLabels.Count} box(es)";
        }

        RenderCanvas();
        UpdateControlStates();
        UpdateCropPreview();
    }

    // --- Variant discovery ---

    private static List<DatasetVariant> DiscoverVariants(string opened)
    {
        string root = ResolveRoot(opened);
        var variants = new List<DatasetVariant>();

        TryAddDetect(variants, root, "detect", "C – Detector (72 classes)");
        TryAddDetect(variants, root, "locate", "B₁ – Localise (OBB, 1 class)");
        TryAddClassify(variants, root, "classify_full", "A – Classification (whole image)");
        TryAddClassify(variants, root, "classify_crop", "B₂ – Classification (crop)");

        if (variants.Count == 0)
        {
            var images = EnumerateImages(opened);
            if (images.Count > 0)
                variants.Add(new DatasetVariant
                {
                    Name = "Folder",
                    Kind = VariantKind.Plain,
                    Images = images,
                    ClassNames = LoadClassNames(opened),
                });
        }
        return variants;
    }

    private static string ResolveRoot(string opened)
    {
        if (HasAnyVariant(opened)) return opened;
        var parent = IOPath.GetDirectoryName(opened.TrimEnd(IOPath.DirectorySeparatorChar));
        return parent is not null && HasAnyVariant(parent) ? parent : opened;
    }

    private static bool HasAnyVariant(string root) =>
        Directory.Exists(IOPath.Combine(root, "detect", "labels")) ||
        Directory.Exists(IOPath.Combine(root, "locate", "labels")) ||
        Directory.Exists(IOPath.Combine(root, "classify_full", "train")) ||
        Directory.Exists(IOPath.Combine(root, "classify_crop", "train"));

    private static void TryAddDetect(List<DatasetVariant> variants, string root, string sub, string name)
    {
        var variantDir = IOPath.Combine(root, sub);
        var labelsDir = IOPath.Combine(variantDir, "labels");
        if (!Directory.Exists(labelsDir)) return;

        var imagesDir = Directory.Exists(IOPath.Combine(variantDir, "images"))
            ? IOPath.Combine(variantDir, "images")
            : IOPath.Combine(root, "images");
        var images = EnumerateImages(imagesDir);
        if (images.Count == 0) return;

        variants.Add(new DatasetVariant
        {
            Name = name,
            Kind = VariantKind.Detect,
            Images = images,
            ClassNames = LoadClassNames(variantDir),
            LabelsDir = labelsDir,
            Root = root,
        });
    }

    private static void TryAddClassify(List<DatasetVariant> variants, string root, string sub, string name)
    {
        var trainDir = IOPath.Combine(root, sub, "train");
        if (!Directory.Exists(trainDir)) return;

        var images = Directory.EnumerateFiles(trainDir, "*", SearchOption.AllDirectories)
            .Where(IsImageFile)
            .OrderBy(IOPath.GetFileName, StringComparer.Ordinal)
            .ThenBy(f => f, StringComparer.Ordinal)
            .ToList();
        if (images.Count == 0) return;

        var classes = Directory.GetDirectories(trainDir)
            .Select(d => IOPath.GetFileName(d))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        variants.Add(new DatasetVariant
        {
            Name = name,
            Kind = VariantKind.Classify,
            Images = images,
            ClassNames = classes,
            Root = root,
        });
    }

    private static string ClassOf(string imagePath) =>
        IOPath.GetFileName(IOPath.GetDirectoryName(imagePath) ?? "") is { Length: > 0 } c ? c : "?";

    private static List<string> EnumerateImages(string dir) =>
        Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Where(IsImageFile).OrderBy(f => f, StringComparer.Ordinal).ToList()
            : [];

    private static bool IsImageFile(string path) =>
        ImageExtensions.Contains(IOPath.GetExtension(path).ToLowerInvariant());

    // --- Classes & labels ---

    private static string[] LoadClassNames(string folderPath)
    {
        foreach (var dir in new[] { folderPath, IOPath.GetDirectoryName(folderPath) ?? "" })
        {
            if (string.IsNullOrEmpty(dir)) continue;

            var classesFile = IOPath.Combine(dir, "classes.txt");
            if (File.Exists(classesFile))
                return File.ReadAllLines(classesFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

            var yamlFile = IOPath.Combine(dir, "data.yaml");
            if (File.Exists(yamlFile))
            {
                var names = ParseYamlNames(yamlFile);
                if (names.Length > 0) return names;
            }
        }
        return JassClasses.Names.ToArray();
    }

    private static string[] ParseYamlNames(string yamlPath)
    {
        foreach (var line in File.ReadAllLines(yamlPath))
        {
            var m = Regex.Match(line, @"^\s*names\s*:\s*\[(.+)\]");
            if (m.Success)
                return m.Groups[1].Value
                        .Split(',')
                        .Select(s => s.Trim().Trim('\'').Trim('"'))
                        .Where(s => s.Length > 0)
                        .ToArray();
        }
        return [];
    }

    private static List<YoloLabel> LoadYoloLabels(string imagePath)
    {
        var labelPath = IOPath.ChangeExtension(imagePath, ".txt");
        if (!File.Exists(labelPath))
        {
            var dir    = IOPath.GetDirectoryName(imagePath)!;
            var parent = IOPath.GetDirectoryName(dir) ?? dir;
            var stem   = IOPath.GetFileNameWithoutExtension(imagePath);
            labelPath  = IOPath.Combine(parent, "labels", stem + ".txt");
        }
        return ParseLabelFile(labelPath);
    }

    private static List<YoloLabel> ParseLabelFile(string labelPath)
    {
        if (!File.Exists(labelPath)) return [];

        var result = new List<YoloLabel>();
        foreach (var line in File.ReadAllLines(labelPath))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) continue;
            if (!int.TryParse(parts[0], out var classId)) continue;

            var coords = new double[parts.Length - 1];
            bool ok = true;
            for (int i = 0; i < coords.Length; i++)
                if (!TryParseDouble(parts[i + 1], out coords[i])) { ok = false; break; }
            if (!ok) continue;

            result.Add(new YoloLabel(classId, coords.Length == 8, coords));
        }
        return result;
    }

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // --- Rendering ---

    private void RenderCanvas()
    {
        if (_bitmap is null) return;

        ImageCanvas.Children.Clear();

        double canvasW = _bitmap.PixelSize.Width  * _zoom;
        double canvasH = _bitmap.PixelSize.Height * _zoom;
        ImageCanvas.Width  = canvasW;
        ImageCanvas.Height = canvasH;

        var img = new Image { Source = _bitmap, Width = canvasW, Height = canvasH };
        Canvas.SetLeft(img, 0);
        Canvas.SetTop(img, 0);
        ImageCanvas.Children.Add(img);

        if (Annotating)
        {
            RenderLabelOverlay();
            return;
        }

        RenderBoxes(canvasW, canvasH);

        if (_caption is not null)
        {
            double fontSize = Math.Max(14, 16 * _zoom);
            var badge = new Border
            {
                Background      = new SolidColorBrush(Color.FromArgb(210, 0, 0, 0)),
                BorderBrush     = new SolidColorBrush(Colors.Lime),
                BorderThickness = new Avalonia.Thickness(1),
                CornerRadius    = new Avalonia.CornerRadius(3),
                Child = new TextBlock { Text = _caption, Foreground = Brushes.Lime, FontSize = fontSize, Margin = new Avalonia.Thickness(6, 3) }
            };
            Canvas.SetLeft(badge, 8);
            Canvas.SetTop(badge, 8);
            ImageCanvas.Children.Add(badge);
        }
    }

    private void RenderBoxes(double canvasW, double canvasH)
    {
        double strokeThickness = Math.Max(1.5, 2 * _zoom);

        foreach (var label in _currentLabels)
        {
            var color  = BoxColors[label.ClassId % BoxColors.Length];
            var stroke = new SolidColorBrush(color);
            var fill   = new SolidColorBrush(new Color(40, color.R, color.G, color.B));

            double labelX, labelY;

            if (label.IsObb)
            {
                var pts = new Avalonia.Point[]
                {
                    new(label.Coords[0] * canvasW, label.Coords[1] * canvasH),
                    new(label.Coords[2] * canvasW, label.Coords[3] * canvasH),
                    new(label.Coords[4] * canvasW, label.Coords[5] * canvasH),
                    new(label.Coords[6] * canvasW, label.Coords[7] * canvasH),
                };

                var poly = new Polygon { Points = pts, Stroke = stroke, StrokeThickness = strokeThickness, Fill = fill };
                Canvas.SetLeft(poly, 0);
                Canvas.SetTop(poly, 0);
                ImageCanvas.Children.Add(poly);

                labelX = pts.MinBy(p => p.Y).X;
                labelY = pts.Min(p => p.Y);
            }
            else
            {
                double bx = (label.Coords[0] - label.Coords[2] / 2) * canvasW;
                double by = (label.Coords[1] - label.Coords[3] / 2) * canvasH;
                double bw = label.Coords[2] * canvasW;
                double bh = label.Coords[3] * canvasH;

                var rect = new Rectangle { Width = bw, Height = bh, Stroke = stroke, StrokeThickness = strokeThickness, Fill = fill };
                Canvas.SetLeft(rect, bx);
                Canvas.SetTop(rect, by);
                ImageCanvas.Children.Add(rect);

                labelX = bx;
                labelY = by;
            }

            var className = label.ClassId < _classNames.Length ? _classNames[label.ClassId] : $"class {label.ClassId}";
            double fontSize = Math.Max(10, 11 * _zoom);
            var badge = new Border
            {
                Background   = stroke,
                CornerRadius = new Avalonia.CornerRadius(2),
                Child = new TextBlock { Text = className, Foreground = Brushes.White, FontSize = fontSize, Margin = new Avalonia.Thickness(3, 1) }
            };
            Canvas.SetLeft(badge, labelX);
            Canvas.SetTop(badge, Math.Max(0, labelY - fontSize - 6));
            ImageCanvas.Children.Add(badge);
        }
    }

    private void RenderLabelOverlay()
    {
        // Corners a model placed are drawn in the orange the session timeline uses for anomalies, and
        // turn green the moment one of them is moved: from then on they are somebody's own work and
        // should not keep looking like a machine's suggestion.
        var colour = CornerChangeNow() == CornerChange.AsProposed ? Color.Parse("#ff9f0a") : Colors.Lime;
        var stroke = new SolidColorBrush(colour);

        if (_points.Count >= 2)
        {
            var pts = _points.Select(p => new Avalonia.Point(p.X * _zoom, p.Y * _zoom)).ToList();
            if (_points.Count == 4)
            {
                var poly = new Polygon
                {
                    Points = pts,
                    Stroke = stroke,
                    StrokeThickness = 2,
                    Fill = new SolidColorBrush(new Color(50, colour.R, colour.G, colour.B)),
                };
                ImageCanvas.Children.Add(poly);
            }
            else
            {
                ImageCanvas.Children.Add(new Polyline { Points = pts, Stroke = stroke, StrokeThickness = 2 });
            }
        }

        for (int i = 0; i < _points.Count; i++)
        {
            double cx = _points[i].X * _zoom, cy = _points[i].Y * _zoom;
            var dot = new Ellipse { Width = 11, Height = 11, Fill = Brushes.White, Stroke = stroke, StrokeThickness = 2 };
            Canvas.SetLeft(dot, cx - 5.5);
            Canvas.SetTop(dot, cy - 5.5);
            ImageCanvas.Children.Add(dot);

            var num = new TextBlock { Text = (i + 1).ToString(), Foreground = stroke, FontSize = 14, FontWeight = FontWeight.Bold };
            Canvas.SetLeft(num, cx + 7);
            Canvas.SetTop(num, cy - 20);
            ImageCanvas.Children.Add(num);
        }
    }

    private double ComputeFitZoom()
    {
        if (_bitmap is null) return 1.0;
        var bounds = ScrollArea.Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return 1.0;
        return Math.Min(bounds.Width  / _bitmap.PixelSize.Width,
                        bounds.Height / _bitmap.PixelSize.Height);
    }

    private void OnZoomIn(object? s, RoutedEventArgs e)  { _zoom *= 1.25;            RenderCanvas(); }
    private void OnZoomOut(object? s, RoutedEventArgs e) { _zoom /= 1.25;            RenderCanvas(); }
    private void OnReset(object? s, RoutedEventArgs e)   { _zoom = ComputeFitZoom(); RenderCanvas(); }
}

internal enum ViewerSection { Browse, Label, Extract, Sessions }

internal enum VariantKind { Detect, Classify, Plain }

internal sealed class DatasetVariant
{
    public required string Name { get; init; }
    public required VariantKind Kind { get; init; }
    public required List<string> Images { get; init; }
    public string[] ClassNames { get; init; } = [];
    public string? LabelsDir { get; init; }

    /// <summary>
    /// The dataset this variant was discovered in, or null for a plain image folder. It is what makes
    /// a label correctable: every variant is derived from the labels under this root, so a correction
    /// is written there once instead of into each variant separately.
    /// </summary>
    public string? Root { get; init; }
}

record YoloLabel(int ClassId, bool IsObb, double[] Coords);
