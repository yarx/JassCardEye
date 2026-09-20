using System;
using System.Globalization;
using System.IO;
using JassCardEye.Dataset.Cards;

namespace JassCardEye.Dataset.Viewer.Predict;

/// <summary>
/// Writes down what the models proposed and what the person then saved - one line per sample that
/// was saved after a proposal.
///
/// This exists because of what the proposals are used on. <c>data/real/val</c> is the yardstick these
/// very models are measured against, and a label the model proposed and a person waved through makes
/// the yardstick a little more like the model. That cannot be prevented by being careful; it can only
/// be counted afterwards, which needs somebody to have written down which labels started as a
/// proposal and which of them were accepted unchanged.
///
/// It lives beside viewer-settings.json rather than in the dataset: it says something about how a
/// label came about, not what it is, and a dataset check should not have to know about it.
/// </summary>
internal static class ProposalLog
{
    private const string Header = "at,stem,weights,proposed,box_confidence,class_confidence,saved,corners";

    private static string FilePath => Path.Combine(ViewerSettings.Folder, "proposals.csv");

    /// <summary>
    /// Records one saved sample. <paramref name="saved"/> is null when it was saved as "No card" -
    /// the case that counts a proposal on a photo without a card, which is the expensive one.
    /// </summary>
    public static void Write(string stem, string weights, Card? proposed, float boxConfidence,
        float classConfidence, Card? saved, CornerChange corners)
    {
        try
        {
            Directory.CreateDirectory(ViewerSettings.Folder);
            bool fresh = !File.Exists(FilePath);
            using var file = new StreamWriter(FilePath, append: true);
            if (fresh) file.WriteLine(Header);
            file.WriteLine(string.Join(',', new[]
            {
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Escape(stem),
                Escape(weights),
                proposed?.Label ?? "",
                boxConfidence.ToString("0.0000", CultureInfo.InvariantCulture),
                classConfidence.ToString("0.0000", CultureInfo.InvariantCulture),
                saved?.Label ?? "none",
                corners switch
                {
                    CornerChange.AsProposed => "as proposed",
                    CornerChange.Turned => "turned",
                    CornerChange.Reordered => "reordered",
                    _ => "moved",
                },
            }));
        }
        catch (Exception)
        {
            // A line that could not be written must never cost the label it was about.
        }
    }

    // The stems come from file names, which may hold a comma.
    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"')
            ? '"' + value.Replace("\"", "\"\"") + '"'
            : value;
}
