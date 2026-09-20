using System.Numerics;
using JassCardEye.Dataset.Cards;
using JassCardEye.Dataset.Viewer.Predict;
using JassCardEye.Dataset.Viewer.Sessions;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine("ok   " + description);
}
static IReadOnlyList<SessionFrame> Read(string rows) => SessionLog.Read(new StringReader(SessionLog.Header + "\n" + rows));
static void Reject(string rows, string description)
{
    try { Read(rows); } catch (FormatException) { Check(true, description); return; }
    throw new Exception(description);
}
// Holds the rules of the Dataset Tool that can be decided without a window to account: how it reads a
// recognition log and what it calls an anomaly, and what it makes of the two models that propose a
// label.
//
//   dotnet run -c Release --project src/tools/dataset/Checks

var parsed = Read("0,0.000,,,,,,,0\n1,33.333,spades_6,0.8500,0.1000,0.2000,0.3000,0.4000,1\n");
Check(parsed.Count == 2 && parsed[0].Card == null && parsed[1].Card == Card.Parse("spades_6")
    && parsed[1].Committed && parsed[1].Box.Y == .2, "a row without a detection, and a counted card, as the apps write them");
var quoted = Read("0,0.000,\"spades_6\",0.85,0.1,0.2,0.3,0.4,0\r\n");
Check(quoted[0].Card == Card.Parse("spades_6"), "a quoted label and Windows line ends");
Check(Read("0,0,,,,,,,1\n")[0].Committed, "a count on a frame without a detection is kept, not refused");
Reject("1,0,,,,,,,0\n", "reject missing frame zero");
Reject("0,0,,,,,,,0\n2,1,,,,,,,0\n", "reject gaps rather than shift video association");
Reject("0,0,,,,,,,0\n1,0,,,,,,,0\n", "reject duplicate timestamps");
Reject("0,NaN,,,,,,,0\n", "reject non-finite timestamps");
Check(Read("0,0,roses_9,1.1512,-0.0007,0.0005,0.7358,0.6875,1\n")[0].Confidence > 1,
    "a confidence above 1 and a box edge just outside the square, as an iPhone recorded them, are kept");
Reject("0,0,spades_6,0.9,0,0,-0.1,1,0\n", "reject a box of negative size");
Reject("0,0,not_a_card,1,0,0,1,1,0\n", "reject unknown card tokens");
Reject("0,0,,0.5,,,,,0\n", "reject a confidence without a label");
Reject("frame,t_ms,label\n", "reject a log with other columns");

var a = Card.Parse("spades_6");
var b = Card.Parse("spades_8");
SessionAnalysis Analyse(Card?[] cards, int commit = -1) => new(cards.Select((card, i) =>
    new SessionFrame(i, i * 33.333, card, card == null ? 0 : .9, default, i == commit)).ToArray());
var outlier = Analyse([a, a, a, b, a, a, a], 3);
Check(outlier.Anomalies.Count(x => x.Kind == AnomalyKind.Outlier && x.Frame == 3 && x.Expected == a) == 1,
    "isolated wrong card in a consistent run");
Check(outlier.Anomalies.Count(x => x.Kind == AnomalyKind.CommitMismatch && x.Frame == 3) == 1,
    "commit disagrees with surrounding majority");
var ghost = Analyse([null, null, null, b, null, null, null]);
Check(ghost.Anomalies.Count == 1 && ghost.Anomalies[0].Kind == AnomalyKind.Ghost, "ghost in blank stretch");
var flip = Analyse([a, b, a, b, a, b]);
Check(flip.Anomalies.Count == 1 && flip.Anomalies[0].Kind == AnomalyKind.FlipFlop
    && flip.Anomalies[0].EndFrame == 5, "maximal flip-flop counted once");
Check(Analyse([a, a, a, b, b, b]).Anomalies.Count == 0, "ordinary card change is not an anomaly");
Check(Analyse([null, b, null]).Anomalies.Count == 0, "insufficient blank context is not called a ghost");
var totals = SessionAnalysis.Aggregate([outlier, ghost, flip, ghost]);
Check(totals.Confusions.Count == 1 && totals.Confusions[0].Count == 2,
    "aggregate candidate pair without double-counting the same outlier/commit");
Check(totals.Ghosts.Count == 1 && totals.Ghosts[0].Count == 2, "aggregate ghosts across sessions");
Check(totals.Switches.Count == 1 && totals.Switches[0].Count == 7,
    "count direct card switches without connecting separate sessions or crossing blank intervals");
// Replaying a log with other settings (SessionReplay), as the app's pile decides.
static SessionFrame[] Frames(params (Card? Card, double Confidence, bool Committed)[] rows) => rows
    .Select((r, i) => new SessionFrame(i, i * 33.333, r.Card, r.Card is null ? 0 : r.Confidence, default, r.Committed)).ToArray();

var run = Frames((a, .9, false), (a, .9, false), (a, .9, true), (b, .9, false), (b, .9, false), (b, .9, true));
Check(SessionReplay.Count(run, new(0.6, StabilityRule.Run, 3)).Select(c => c.Frame).SequenceEqual([2, 5]),
    "a run of 3 counts a card on its third frame");
Check(SessionReplay.Count(run, new(0.6, StabilityRule.Run, 1)).Select(c => c.Frame).SequenceEqual([0, 3]),
    "a run of 1 counts it at once");
Check(SessionReplay.RecordedWith(run) is { Rule: StabilityRule.Run, Frames: 3 },
    "the rule a log was counted with is found by replaying it");
var dip = Frames((a, .9, false), (a, .5, false), (a, .9, false), (a, .9, false));
Check(SessionReplay.Count(dip, new(0.6, StabilityRule.Run, 2)).Select(c => c.Frame).SequenceEqual([3]),
    "a frame below the threshold breaks a run");
var vote = Frames((a, .9, false), (b, .9, false), (a, .9, false), (a, .9, false), (b, .9, false));
Check(SessionReplay.Count(vote, new(0.6, StabilityRule.Majority, 2)).Select(c => c.Frame).SequenceEqual([2]),
    "a majority of 2 in 3 counts past a single rival");
var weak = SessionReplay.Cards(Frames((a, .95, false), (a, .95, true), (b, .81, false), (a, .7, false)), 0.9);
Check(weak[0].Card == b && weak[0].Highest == .81 && weak[0].FramesAtThreshold == 0
    && weak[1].LongestRunAtThreshold == 2 && weak[1].CountedAt == 1,
    "cards are listed weakest first, with their longest run at the threshold and where the app counted them");
Check(SessionReplay.Histogram(Frames((a, .65, false), (a, .97, false), (a, 1.15, false), (null, 0, false))).SequenceEqual([0, 1, 0, 0, 0, 1, 1]),
    "confidences fall into their bands, above 1 included");
Check(SessionReplay.ShortReadings(Frames((a, .9, false), (a, .9, false), (a, .9, false), (b, .97, false), (a, .9, false), (a, .9, false)), 0.6)
    is [{ Frame: 3, Frames: 1 } brief] && brief.Card == b,
    "a one-frame reading is found, and the card it interrupted is not counted as a second one");

// The session info both apps write beside a recording (SessionInfo).
var info = SessionInfo.Parse("{\n  \"app_build\" : \"1109\",\n  \"confidence_threshold\" : 0.6,\n  \"device\" : \"iPhone17,1\",\n" +
    "  \"platform\" : \"iOS\",\n  \"stability_frames\" : 3,\n  \"stability_rule\" : \"run\",\n  \"system\" : \"26.0.0\"\n}");
Check(info?.Settings is { Threshold: 0.6, Rule: StabilityRule.Run, Frames: 3 } && info.Describe().StartsWith("iPhone17,1 · iOS 26.0.0"),
    "the session info is read by its snake-case keys");

// The Settings tab as text (SessionReport).
var report = SessionReport.Settings([new ReportSession("s.mov", run, null, new ReplaySettings(0.6, StabilityRule.Run, 3), "worked out from the log")],
    new ReplaySettings(0.95, StabilityRule.Run, 3));
Check(report.Contains("counted        2 in the app, 0 replayed") && report.Contains("no longer      spades_6, spades_8"),
    "the settings report names the cards a stricter threshold loses");

// A session as the apps pack it (SessionArchive): a folder of the session's name inside the ZIP.
var scratch = Directory.CreateTempSubdirectory("session-checks-");
try
{
    string Pack(string name, bool withFolder, bool withMacMetadata = false)
    {
        var source = Directory.CreateDirectory(Path.Combine(scratch.FullName, "source-" + name, name));
        File.WriteAllText(Path.Combine(source.FullName, name + ".mov"), "video");
        File.WriteAllText(Path.Combine(source.FullName, name + ".csv"), SessionLog.Header);
        var zip = Path.Combine(scratch.FullName, name + ".zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(source.FullName, zip, System.IO.Compression.CompressionLevel.NoCompression, includeBaseDirectory: withFolder);
        if (withMacMetadata)
        {
            using var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Update);
            archive.CreateEntry($"__MACOSX/{name}/._{name}.mov");
        }
        return zip;
    }

    var packed = SessionArchive.Unpack(Pack("session_a", withFolder: true, withMacMetadata: true));
    Check(packed == Path.Combine(scratch.FullName, "session_a", "session_a.mov") && File.Exists(Path.ChangeExtension(packed, ".csv")),
        "a session ZIP is unpacked beside itself, its folder taken as the session, macOS metadata ignored");
    Check(SessionArchive.Unpack(Path.Combine(scratch.FullName, "session_a.zip")) == packed,
        "opening the same ZIP again uses the folder already there");
    Check(SessionArchive.Unpack(Pack("session_b", withFolder: false)) == Path.Combine(scratch.FullName, "session_b", "session_b.mov"),
        "a ZIP without the folder is unpacked into one named after it");
}
finally
{
    scratch.Delete(recursive: true);
}

// --- What a model's answer turns into when the Dataset Tool proposes a label ---
//
// The models themselves cannot run here - there are no weights in CI and no GPU - but everything the
// tool decides around them can, and that is where a proposal goes wrong: corners in the wrong order
// rectify the card mirrored or on its head, and a threshold that slipped puts a guess into the
// validation set.

// A card of 64 × 100 at the origin, turned by 30°, as an oriented box would give it.
static Vector2[] Box(float angle, float width = 64, float height = 100, float cx = 300, float cy = 300)
{
    var (sin, cos) = ((float)Math.Sin(angle * Math.PI / 180), (float)Math.Cos(angle * Math.PI / 180));
    Vector2 At(float x, float y) => new(cx + x * cos - y * sin, cy + x * sin + y * cos);
    return [At(-width / 2, -height / 2), At(width / 2, -height / 2), At(width / 2, height / 2), At(-width / 2, height / 2)];
}

static bool Same(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b) =>
    a.Count == b.Count && a.Zip(b).All(p => Vector2.Distance(p.First, p.Second) < 0.01f);

var upright = ObbCorners.Upright(Box(0));
Check(Same(upright, Box(0)), "an upright box comes back as it went in: top left first, the short side first");
Check(Same(ObbCorners.Upright(Box(30)), Box(30)), "a tilted card keeps its corner order");
for (int start = 1; start < 4; start++)
{
    var rotated = Box(20).Skip(start).Concat(Box(20).Take(start)).ToArray();
    Check(Same(ObbCorners.Upright(rotated), Box(20)), $"the same box starting at corner {start} lands in the same order");
}
Check(Same(ObbCorners.Upright(Box(20).Reverse().ToArray()), Box(20)),
    "a box wound the other way is turned round, so the crop is never mirrored");
Check(Same(ObbCorners.Upright(Box(180)), Box(0)),
    "of the two half-turns, the one standing on its feet on the photo is the one proposed");
// A card lying on its side: its own short edge stays the top edge, or the crop comes out squashed -
// which is the mistake nobody can miss, and the reason the rule is about the sides and not the screen.
Check(Same(ObbCorners.Upright(Box(90)), Box(90)), "a card lying on its side keeps its own corner order");

// The way back from the square the dataset stores to the photo it was cut from.
var square = PhotoSquare.Of(1920, 1080, 640);
Check(Math.Abs(square.ToPhoto(new Vector2(0, 0)).X - 420) < 0.01f && square.ToPhoto(new Vector2(0, 0)).Y == 0,
    "the square of a landscape photo starts where its side bars end");
Check(Vector2.Distance(square.ToPhoto(new Vector2(640, 640)), new Vector2(1500, 1080)) < 0.01f,
    "and ends at its far corner, whatever size the square is stored at");
Check(Vector2.Distance(PhotoSquare.Of(640, 640, 640).ToPhoto(new Vector2(17, 29)), new Vector2(17, 29)) < 0.01f,
    "a photo that is already the square is left alone");

// Which box a proposal is built on.
static BoxScore Found(float confidence) => new(Box(0), confidence);
Check(ProposalRules.PickBox([]) is null, "no box, no proposal");
Check(ProposalRules.PickBox([Found(0.49f)]) is null, "a box below the gate is not proposed at all");
Check(ProposalRules.PickBox([Found(0.51f)]) is { Box.Confidence: 0.51f, Note: "" }, "a box above the gate is, without a remark");
Check(ProposalRules.PickBox([Found(0.6f), Found(0.9f)]) is { Box.Confidence: 0.9f, Note: "" }, "the strongest box wins");
Check(ProposalRules.PickBox([Found(0.85f), Found(0.9f)]) is { Note.Length: > 0 },
    "a second card nearly as strong is worth saying: which one is on top is then a matter of opinion");

// Which card is preselected, and when none is.
static ClassScore[] Scores(params (string Name, float Confidence)[] rows) =>
    rows.Select(r => new ClassScore(r.Name, r.Confidence)).ToArray();
var french = Scores(("spades_6", 0.91f), ("bells_8", 0.80f), ("clubs_7", 0.05f));
Check(ProposalRules.PickCard(french, Deck.French) is ({ } chosen, 0.91f, "") && chosen.Label == "spades_6",
    "the strongest card of the deck being labelled is preselected");
Check(ProposalRules.PickCard(Scores(("bells_8", 0.95f), ("spades_6", 0.70f)), Deck.French)
    is ({ Suit: Suit.Spades }, 0.70f, { Length: > 0 }),
    "a stronger answer in the other deck never switches the deck - it is mentioned");
Check(ProposalRules.PickCard(Scores(("spades_6", 0.59f)), Deck.French) is (null, 0.59f, { Length: > 0 }),
    "below the class gate the corners stand but the class stays open, rather than a guess to confirm");
Check(ProposalRules.PickCard(Scores(("bells_8", 0.99f)), Deck.French) is (null, 0f, { Length: > 0 }),
    "nothing of this deck at all leaves the class open too");
Check(ProposalRules.BestOfDeck(Scores(("spades_6", 0.91f), ("clubs_7", 0.05f)), Deck.German) is null,
    "no card of a deck means no best card of it");
Check(ProposalRules.Join("", "second", "", "third") == "second · third", "only the notes that have something to say");

// What became of the proposed corners, which is what the proposal log counts.
var placed = Box(25);
Check(ProposalRules.Compare(placed, Box(25)) == CornerChange.AsProposed, "the same corners in the same order: taken as they came");
Check(ProposalRules.Compare(placed, [.. placed.Skip(2), .. placed.Take(2)]) == CornerChange.Turned,
    "the same corners half a turn on: not a corrected box but a corrected guess about which way up");
Check(ProposalRules.Compare(placed, [.. placed.Skip(1), .. placed.Take(1)]) == CornerChange.Reordered,
    "a quarter turn is neither of those");
Check(ProposalRules.Compare(placed, [placed[0] + new Vector2(2, 0), placed[1], placed[2], placed[3]]) == CornerChange.Moved,
    "one corner dragged two pixels is a moved box, which is the one that says B₁ was wrong");

Console.WriteLine("Session analysis and label proposal checks passed.");
