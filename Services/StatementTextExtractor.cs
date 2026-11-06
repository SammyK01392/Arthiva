using System.Text;
using Plugin.Maui.OCR;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Exceptions;
using PigPdf = UglyToad.PdfPig.PdfDocument;

namespace MoneySpend.Services;

/// <summary>PDF -> text lines (PdfPig).  Image/screenshot -> text lines (on-device OCR). No AI, no internet.</summary>
public static class StatementTextExtractor
{
    // A gap wider than this (x font size) between two letters = a space.
    // Needed because the Google Pay PDF has NO space characters at all ("PaidtoVaishali").
    // Measured on the real statements: letters inside a word ~0.0, between words ~0.3.
    private const double SpaceGap = 0.2;

    public static Task<List<string>> ExtractLinesAsync(string path, Func<Task<string?>> askPassword)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();

        return ext == ".pdf"
            ? ExtractPdfAsync(path, askPassword)
            : ExtractImageAsync(path);
    }

    // ───────────────────────── PDF ─────────────────────────
    private static async Task<List<string>> ExtractPdfAsync(string path, Func<Task<string?>> askPassword)
    {
        string? password = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                return await Task.Run(() => ReadPdf(path, password));
            }
            catch (PdfDocumentEncryptedException)
            {
                // locked PDF (bank statements usually are): ask the user
                password = await askPassword();

                if (string.IsNullOrEmpty(password))
                    throw new OperationCanceledException("Password not entered.");
            }
        }

        throw new InvalidOperationException("Wrong password.");
    }

    private static List<string> ReadPdf(string path, string? password)
    {
        var options = new ParsingOptions
        {
            UseLenientParsing = true,
            SkipMissingFonts = true
        };

        if (!string.IsNullOrEmpty(password))
            options.Passwords = new List<string> { password };

        var lines = new List<string>();

        using var document = PigPdf.Open(path, options);

        foreach (var page in document.GetPages())
            lines.AddRange(BuildLines(page.Letters));

        return lines;
    }

    /// <summary>Letters -> visual lines (same baseline = same line), spaces from gaps.</summary>
    private static List<string> BuildLines(IEnumerable<Letter> letters)
    {
        // PDF y grows upward, so the top line has the biggest y
        var sorted = letters.OrderByDescending(l => l.StartBaseLine.Y).ToList();

        var groups = new List<List<Letter>>();
        List<Letter>? current = null;
        double lineY = 0;

        foreach (var letter in sorted)
        {
            if (current is null || Math.Abs(letter.StartBaseLine.Y - lineY) > 3)
            {
                current = new List<Letter>();
                groups.Add(current);
                lineY = letter.StartBaseLine.Y;
            }

            current.Add(letter);
        }

        var result = new List<string>();

        foreach (var group in groups)
        {
            var sb = new StringBuilder();
            Letter? previous = null;

            foreach (var letter in group.OrderBy(l => l.StartBaseLine.X))
            {
                if (string.IsNullOrWhiteSpace(letter.Value))
                {
                    // a real space character in the PDF (PhonePe has them)
                    if (sb.Length > 0 && sb[^1] != ' ') sb.Append(' ');
                    previous = letter;
                    continue;
                }

                if (previous is not null && !string.IsNullOrWhiteSpace(previous.Value))
                {
                    var gap = letter.StartBaseLine.X - previous.EndBaseLine.X;

                    if (gap > SpaceGap * letter.PointSize && sb.Length > 0 && sb[^1] != ' ')
                        sb.Append(' ');
                }

                sb.Append(letter.Value);
                previous = letter;
            }

            var text = sb.ToString().Trim();
            if (text.Length > 0) result.Add(text);
        }

        return result;
    }

    // ───────────────────────── Screenshot ─────────────────────────
    private static async Task<List<string>> ExtractImageAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);

        await OcrPlugin.Default.InitAsync();

        var options = new OcrOptions.Builder().SetTryHard(true).Build();
        var result = await OcrPlugin.Default.RecognizeTextAsync(bytes, options);

        if (!result.Success || string.IsNullOrWhiteSpace(result.AllText))
            throw new InvalidOperationException("Could not read any text from the image.");

        return result.AllText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }
}
