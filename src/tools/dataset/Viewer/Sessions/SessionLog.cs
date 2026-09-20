using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.VisualBasic.FileIO;
using JassCardEye.Dataset.Cards;

namespace JassCardEye.Dataset.Viewer.Sessions;

/// <summary>A box normalised to the analysed square, origin top-left.</summary>
internal readonly record struct SessionBox(double X, double Y, double Width, double Height);

/// <summary>One row of a recognition log: what the model made of frame <see cref="Index"/> (from 0) of the video.</summary>
internal sealed record SessionFrame(int Index, double Milliseconds, Card? Card,
    double Confidence, SessionBox Box, bool Committed);

/// <summary>
/// Reads the recognition log both apps write next to a session recording. The columns are described
/// under "Session recordings" in context/architecture/data-pipeline.md.
///
/// Strict on purpose: row n belongs to frame n of the video, and a log that skips or repeats a row
/// would pin every later detection on the wrong picture. Such a log is refused, never repaired.
/// </summary>
internal static class SessionLog
{
    public const string Header = "frame,t_ms,label,confidence,x,y,w,h,committed";

    public static IReadOnlyList<SessionFrame> Read(string path)
    {
        using var reader = new StreamReader(path);
        return Read(reader);
    }

    internal static IReadOnlyList<SessionFrame> Read(TextReader reader)
    {
        // The apps write labels as plain tokens; quoted fields are accepted all the same.
        using var csv = new TextFieldParser(reader) { HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false };
        csv.SetDelimiters(",");
        var header = csv.ReadFields();
        if (header == null || !header.SequenceEqual(Header.Split(',')))
            throw new FormatException($"The log does not start with the header {Header}.");

        var frames = new List<SessionFrame>();
        while (!csv.EndOfData)
        {
            var fields = csv.ReadFields();
            var line = frames.Count + 2;
            if (fields == null || fields.Length != 9) throw Invalid(line, "9 fields expected");
            if (!int.TryParse(fields[0], NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index != frames.Count)
                throw Invalid(line, $"frame {frames.Count} expected - the rows count up from 0 without a gap");

            double Number(int column)
            {
                if (!double.TryParse(fields[column], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                    throw Invalid(line, $"no number in column {column + 1}");
                return value;
            }

            var time = Number(1);
            if (time < 0 || (frames.Count > 0 && time <= frames[^1].Milliseconds))
                throw Invalid(line, "the times have to count up");
            if (fields[8] is not ("0" or "1")) throw Invalid(line, "committed is 0 or 1");

            Card? card = null;
            double confidence = 0;
            SessionBox box = default;
            if (fields[2].Length == 0)
            {
                if (fields.Skip(3).Take(5).Any(f => f.Length != 0))
                    throw Invalid(line, "a row without a label has no confidence and no box");
            }
            else
            {
                if (!Card.TryParse(fields[2], out var parsed)) throw Invalid(line, $"unknown label {fields[2]}");
                card = parsed;
                // What the model said, taken as it is: the strictness above is about which frame a row
                // belongs to, not about the model's numbers. A recording from an iPhone can hold a
                // confidence above 1 - Vision's own confidence is the sum over all classes - and a
                // detector can place a box partly outside the square; drawing clips it.
                confidence = Number(3);
                box = new(Number(4), Number(5), Number(6), Number(7));
                if (box.Width < 0 || box.Height < 0)
                    throw Invalid(line, "a box of negative size");
            }
            frames.Add(new(index, time, card, confidence, box, fields[8] == "1"));
        }
        if (frames.Count == 0) throw new FormatException("The log has no rows.");
        return frames;
    }

    private static FormatException Invalid(int line, string reason) => new($"line {line} of the log: {reason}.");
}
