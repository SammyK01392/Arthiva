#if ANDROID

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Android.Graphics;
using Android.Graphics.Pdf;

using ContactHistoryItem = Arthiva.ViewModels.ContactHistoryItem;

using AndroidColor = Android.Graphics.Color;
using AndroidPaint = Android.Graphics.Paint;
using AndroidRectF = Android.Graphics.RectF;
using AndroidCanvas = Android.Graphics.Canvas;

using IOPath = System.IO.Path;

namespace Arthiva.Services;

public static class PdfReportService
{
    public static async Task<string> GenerateContactReportAsync(
        string contactName,
        string? mobile,
        string? email,
        decimal totalReceive,
        decimal totalOwe,
        string netBalance,
        IEnumerable<ContactHistoryItem> history)
    {
        return await Task.Run(() =>
        {
            var fileName =
                $"{SanitizeFileName(contactName)}_Report_{DateTime.Now:yyyyMMdd_HHmmss}.pdf";

            var filePath = IOPath.Combine(
                FileSystem.CacheDirectory,
                fileName);

            const int pageWidth = 595;
            const int pageHeight = 842;

            const float margin = 40;
            const float contentWidth = pageWidth - (margin * 2);

            var historyList = history
                .OrderByDescending(x => x.Date)
                .ToList();

            using var document = new PdfDocument();

            PdfDocument.Page? currentPage = null;
            AndroidCanvas? canvas = null;

            var pageNumber = 1;
            var y = margin;

            // =====================================================
            // PAINTS
            // =====================================================

            using var titlePaint = CreatePaint(
                AndroidColor.Rgb(15, 23, 42),
                24,
                TypefaceStyle.Bold);

            using var headingPaint = CreatePaint(
                AndroidColor.Rgb(15, 23, 42),
                15,
                TypefaceStyle.Bold);

            using var bodyPaint = CreatePaint(
                AndroidColor.Rgb(51, 65, 85),
                10.5f);

            using var captionPaint = CreatePaint(
                AndroidColor.Rgb(100, 116, 139),
                8.5f);

            using var incomePaint = CreatePaint(
                AndroidColor.Rgb(22, 163, 74),
                13,
                TypefaceStyle.Bold);

            using var expensePaint = CreatePaint(
                AndroidColor.Rgb(220, 38, 38),
                13,
                TypefaceStyle.Bold);

            using var tableHeaderPaint = CreatePaint(
                AndroidColor.Rgb(71, 85, 105),
                8,
                TypefaceStyle.Bold);

            using var cardPaint =
                new AndroidPaint(PaintFlags.AntiAlias);

            cardPaint.Color =
                AndroidColor.Rgb(248, 250, 252);

            using var linePaint =
                new AndroidPaint(PaintFlags.AntiAlias);

            linePaint.Color =
                AndroidColor.Rgb(226, 232, 240);

            linePaint.StrokeWidth = 1;

            // =====================================================
            // PAGE FUNCTIONS
            // =====================================================

            void StartPage()
            {
                var pageInfo =
                    new PdfDocument.PageInfo.Builder(
                        pageWidth,
                        pageHeight,
                        pageNumber)
                    .Create();

                currentPage =
                    document.StartPage(pageInfo);

                canvas =
                    currentPage.Canvas;

                canvas.DrawColor(
                    AndroidColor.White);

                y = margin;
            }

            void FinishPage()
            {
                if (currentPage != null)
                {
                    document.FinishPage(
                        currentPage);

                    currentPage = null;
                    canvas = null;
                }

                pageNumber++;
            }

            void EnsureSpace(float requiredHeight)
            {
                if (canvas == null)
                {
                    StartPage();
                    return;
                }

                if (y + requiredHeight >
                    pageHeight - 65)
                {
                    FinishPage();
                    StartPage();
                }
            }

            void DrawText(
                string? text,
                float x,
                float baseline,
                AndroidPaint paint)
            {
                canvas?.DrawText(
                    text ?? string.Empty,
                    x,
                    baseline,
                    paint);
            }

            void DrawRightText(
                string? text,
                float right,
                float baseline,
                AndroidPaint paint)
            {
                text ??= string.Empty;

                var width =
                    paint.MeasureText(text);

                DrawText(
                    text,
                    right - width,
                    baseline,
                    paint);
            }

            void DrawHorizontalLine()
            {
                canvas?.DrawLine(
                    margin,
                    y,
                    pageWidth - margin,
                    y,
                    linePaint);
            }

            // =====================================================
            // START PAGE
            // =====================================================

            StartPage();

            // =====================================================
            // HEADER
            // =====================================================

            DrawText(
                "Arthiva",
                margin,
                y,
                titlePaint);

            y += 27;

            DrawText(
                "Contact Financial Statement",
                margin,
                y,
                captionPaint);

            y += 17;

            DrawText(
                $"Generated on {DateTime.Now:dd MMM yyyy, hh:mm tt}",
                margin,
                y,
                captionPaint);

            y += 18;

            DrawHorizontalLine();

            y += 28;

            // =====================================================
            // CONTACT
            // =====================================================

            DrawText(
                contactName,
                margin,
                y,
                headingPaint);

            y += 20;

            if (!string.IsNullOrWhiteSpace(mobile))
            {
                DrawText(
                    mobile,
                    margin,
                    y,
                    bodyPaint);

                y += 16;
            }

            if (!string.IsNullOrWhiteSpace(email))
            {
                DrawText(
                    email,
                    margin,
                    y,
                    bodyPaint);

                y += 16;
            }

            y += 18;

            // =====================================================
            // SUMMARY
            // =====================================================

            EnsureSpace(105);

            DrawText(
                "SUMMARY",
                margin,
                y,
                headingPaint);

            y += 23;

            const float gap = 10;

            var cardWidth =
                (contentWidth - (gap * 2)) / 3;

            const float cardHeight = 72;

            var receiveRect =
                new AndroidRectF(
                    margin,
                    y,
                    margin + cardWidth,
                    y + cardHeight);

            var oweRect =
                new AndroidRectF(
                    margin + cardWidth + gap,
                    y,
                    margin + (cardWidth * 2) + gap,
                    y + cardHeight);

            var netRect =
                new AndroidRectF(
                    margin + (cardWidth * 2) + (gap * 2),
                    y,
                    pageWidth - margin,
                    y + cardHeight);

            canvas!.DrawRoundRect(
                receiveRect,
                10,
                10,
                cardPaint);

            canvas.DrawRoundRect(
                oweRect,
                10,
                10,
                cardPaint);

            canvas.DrawRoundRect(
                netRect,
                10,
                10,
                cardPaint);

            // Receive
            DrawText(
                "YOU'LL RECEIVE",
                receiveRect.Left + 10,
                receiveRect.Top + 21,
                captionPaint);

            DrawText(
                $"₹{totalReceive:N2}",
                receiveRect.Left + 10,
                receiveRect.Top + 48,
                incomePaint);

            // Owe
            DrawText(
                "YOU OWE",
                oweRect.Left + 10,
                oweRect.Top + 21,
                captionPaint);

            DrawText(
                $"₹{totalOwe:N2}",
                oweRect.Left + 10,
                oweRect.Top + 48,
                expensePaint);

            // Net
            DrawText(
                "NET BALANCE",
                netRect.Left + 10,
                netRect.Top + 21,
                captionPaint);

            AndroidPaint netPaint;

            if (netBalance.Equals(
                "Settled",
                StringComparison.OrdinalIgnoreCase))
            {
                netPaint = captionPaint;
            }
            else if (netBalance.Contains(
                "owe",
                StringComparison.OrdinalIgnoreCase))
            {
                netPaint = expensePaint;
            }
            else
            {
                netPaint = incomePaint;
            }

            DrawText(
                netBalance,
                netRect.Left + 10,
                netRect.Top + 48,
                netPaint);

            y += 102;

            // =====================================================
            // HISTORY TITLE
            // =====================================================

            EnsureSpace(70);

            DrawText(
                "TRANSACTION HISTORY",
                margin,
                y,
                headingPaint);

            y += 25;

            // =====================================================
            // TABLE HEADER
            // =====================================================

            EnsureSpace(40);

            DrawHorizontalLine();

            y += 17;

            DrawText(
                "DATE",
                margin,
                y,
                tableHeaderPaint);

            DrawText(
                "TYPE",
                margin + 90,
                y,
                tableHeaderPaint);

            DrawText(
                "MOVEMENT",
                margin + 160,
                y,
                tableHeaderPaint);

            DrawRightText(
                "AMOUNT",
                pageWidth - margin,
                y,
                tableHeaderPaint);

            y += 10;

            DrawHorizontalLine();

            y += 20;

            // =====================================================
            // HISTORY
            // =====================================================

            if (historyList.Count == 0)
            {
                DrawText(
                    "No transaction history available.",
                    margin,
                    y,
                    captionPaint);

                y += 30;
            }
            else
            {
                foreach (var item in historyList)
                {
                    EnsureSpace(55);

                    var typePaint =
                        item.RecordType.Equals(
                            "Lend",
                            StringComparison.OrdinalIgnoreCase)
                            ? incomePaint
                            : expensePaint;

                    DrawText(
                        item.Date.ToString("dd MMM yyyy"),
                        margin,
                        y,
                        bodyPaint);

                    DrawText(
                        item.RecordType,
                        margin + 90,
                        y,
                        typePaint);

                    DrawText(
                        item.MovementLabel,
                        margin + 160,
                        y,
                        bodyPaint);

                    DrawRightText(
                        $"₹{item.Amount:N2}",
                        pageWidth - margin,
                        y,
                        bodyPaint);

                    y += 17;

                    if (!string.IsNullOrWhiteSpace(item.Notes))
                    {
                        DrawText(
                            $"Note: {item.Notes}",
                            margin + 160,
                            y,
                            captionPaint);

                        y += 14;
                    }

                    DrawHorizontalLine();

                    y += 18;
                }
            }

            // =====================================================
            // FOOTER
            // =====================================================

            if (canvas != null)
            {
                canvas.DrawLine(
                    margin,
                    pageHeight - 45,
                    pageWidth - margin,
                    pageHeight - 45,
                    linePaint);

                var footer =
                    $"Generated by Arthiva  •  Page {pageNumber}";

                DrawRightText(
                    footer,
                    pageWidth - margin,
                    pageHeight - 25,
                    captionPaint);
            }

            // =====================================================
            // FINISH PAGE
            // =====================================================

            if (currentPage != null)
            {
                FinishPage();
            }

            // =====================================================
            // SAVE PDF
            // =====================================================

            using var stream =
                File.Open(
                    filePath,
                    FileMode.Create,
                    FileAccess.Write);

            document.WriteTo(stream);

            stream.Flush();

            return filePath;
        });
    }

    // =============================================================
    // CREATE PAINT
    // =============================================================

    private static AndroidPaint CreatePaint(
        AndroidColor color,
        float textSize,
        TypefaceStyle style = TypefaceStyle.Normal)
    {
        var paint =
            new AndroidPaint(
                PaintFlags.AntiAlias);

        paint.Color = color;
        paint.TextSize = textSize;

        if (style != TypefaceStyle.Normal)
        {
            paint.SetTypeface(
                Typeface.Create(
                    Typeface.Default,
                    style));
        }

        return paint;
    }

    // =============================================================
    // SAFE FILE NAME
    // =============================================================

    private static string SanitizeFileName(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Contact";

        foreach (var character in
                 IOPath.GetInvalidFileNameChars())
        {
            value = value.Replace(
                character.ToString(),
                string.Empty);
        }

        value = value.Trim();

        return string.IsNullOrWhiteSpace(value)
            ? "Contact"
            : value;
    }
}

#endif