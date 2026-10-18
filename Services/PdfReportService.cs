#if ANDROID

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Android.Graphics;
using Android.Graphics.Pdf;

using ContactHistoryItem = MoneySpend.ViewModels.ContactHistoryItem;

using AndroidColor = Android.Graphics.Color;
using AndroidPaint = Android.Graphics.Paint;
using AndroidRectF = Android.Graphics.RectF;
using AndroidCanvas = Android.Graphics.Canvas;
using AndroidPath = Android.Graphics.Path;
using AndroidLinearGradient = Android.Graphics.LinearGradient;
using AndroidShader = Android.Graphics.Shader;

using IOPath = System.IO.Path;

namespace MoneySpend.Services;

public static class PdfReportService
{
    // =====================================================================
    // PAGE METRICS (A4 @ 72dpi)
    // =====================================================================

    private const int PageW = 595;
    private const int PageH = 842;

    private const float M = 32f;                       // side margin
    private const float ContentW = PageW - (2 * M);
    private const float ContentBottom = 756f;          // content must end above footer

    private const float RowH = 30f;
    private const float TableHeadH = 22f;
    private const float SectionH = 24f;
    private const float SummaryCardH = 88f;
    private const float InfoBoxH = 46f;

    private const float FirstTableTop = 346f;          // page 1 table start
    private const float NextTableTop = 144f;           // continuation page table start

    // =====================================================================
    // COLORS
    // =====================================================================

    private static readonly AndroidColor Orange = AndroidColor.Rgb(255, 107, 0);
    private static readonly AndroidColor OrangeLight = AndroidColor.Rgb(255, 154, 60);
    private static readonly AndroidColor OrangeDeep = AndroidColor.Rgb(255, 85, 0);
    private static readonly AndroidColor OrangeLine = AndroidColor.Rgb(255, 163, 94);
    private static readonly AndroidColor OrangeTint = AndroidColor.Rgb(255, 244, 232);
    private static readonly AndroidColor OrangeStroke = AndroidColor.Rgb(253, 217, 180);
    private static readonly AndroidColor PeachWave = AndroidColor.Rgb(255, 222, 190);
    private static readonly AndroidColor PeachCircle = AndroidColor.Rgb(255, 214, 178);

    private static readonly AndroidColor Navy = AndroidColor.Rgb(20, 33, 61);
    private static readonly AndroidColor Slate = AndroidColor.Rgb(71, 85, 105);
    private static readonly AndroidColor Gray = AndroidColor.Rgb(110, 124, 145);

    private static readonly AndroidColor LineColor = AndroidColor.Rgb(226, 232, 240);
    private static readonly AndroidColor TableHeadBg = AndroidColor.Rgb(236, 242, 250);
    private static readonly AndroidColor RowAlt = AndroidColor.Rgb(246, 249, 253);

    private static readonly AndroidColor Green = AndroidColor.Rgb(22, 163, 74);
    private static readonly AndroidColor GreenTint = AndroidColor.Rgb(232, 247, 238);
    private static readonly AndroidColor GreenStroke = AndroidColor.Rgb(170, 224, 195);

    private static readonly AndroidColor Red = AndroidColor.Rgb(225, 29, 46);
    private static readonly AndroidColor RedTint = AndroidColor.Rgb(254, 236, 238);
    private static readonly AndroidColor RedStroke = AndroidColor.Rgb(250, 187, 192);

    private static readonly AndroidColor White = AndroidColor.White;

    // =====================================================================
    // PUBLIC API  (signature unchanged)
    // =====================================================================

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

            var filePath = IOPath.Combine(FileSystem.CacheDirectory, fileName);

            var rows = history
                .OrderByDescending(x => x.Date)
                .ToList();

            var data = new ReportData
            {
                Name = contactName,
                Mobile = mobile,
                Email = email,
                TotalReceive = totalReceive,
                TotalOwe = totalOwe,
                LendCount = rows.Count(x => IsType(x.RecordType, "Lend")),
                BorrowCount = rows.Count(x => IsType(x.RecordType, "Borrow")),
                SplitCount = rows.Count(x => IsType(x.RecordType, "Split")),
                GeneratedAt = DateTime.Now.ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture)
            };

            var plans = BuildPlans(rows.Count, data.HasSplit);

            using var document = new PdfDocument();
            using var r = new Renderer();

            for (var i = 0; i < plans.Count; i++)
            {
                var plan = plans[i];

                var pageInfo = new PdfDocument.PageInfo.Builder(PageW, PageH, i + 1).Create();
                var page = document.StartPage(pageInfo);

                r.Canvas = page.Canvas!;

                r.DrawBackground();
                r.DrawHeader(data.GeneratedAt);

                float y;

                if (plan.IsFirst)
                {
                    r.DrawContactCard(110f, data);

                    y = 180f;
                    y = r.DrawSectionHeader(y, SectionIcon.Chart, "Financial Summary");
                    y += 8f;

                    r.DrawSummaryCards(y, data);
                    y += SummaryCardH + 14f;

                    y = r.DrawSectionHeader(y, SectionIcon.List, "Transaction History");
                    y += 8f;
                }
                else
                {
                    y = 112f;

                    if (plan.ShowTable)
                    {
                        y = r.DrawSectionHeader(y, SectionIcon.List, "Transaction History", "(continued)");
                        y += 8f;
                    }
                }

                if (plan.ShowTable)
                {
                    var pageRows = rows.Skip(plan.Start).Take(plan.Count).ToList();
                    y = r.DrawTable(y, pageRows);
                    y += 16f;
                }

                if (plan.ShowClosing)
                    r.DrawClosing(y, plans.Count > 1, data);

                r.DrawFooter(i + 1, plans.Count);

                document.FinishPage(page);
            }

            using var stream = File.Open(filePath, FileMode.Create, FileAccess.Write);
            document.WriteTo(stream);
            stream.Flush();

            return filePath;
        });
    }

    // =====================================================================
    // PAGINATION (deterministic, so "Page X of Y" is known up front)
    // =====================================================================

    private sealed class PagePlan
    {
        public int Start;
        public int Count;
        public bool IsFirst;
        public bool ShowTable = true;
        public bool ShowClosing;
    }

    private sealed class ReportData
    {
        public string Name { get; init; } = string.Empty;
        public string? Mobile { get; init; }
        public string? Email { get; init; }
        public decimal TotalReceive { get; init; }
        public decimal TotalOwe { get; init; }
        public int LendCount { get; init; }
        public int BorrowCount { get; init; }
        public int SplitCount { get; init; }
        public string GeneratedAt { get; init; } = string.Empty;
        public bool HasSplit => SplitCount > 0;
    }

    private enum SectionIcon { Chart, List, Doc }

    private enum CardIcon { Up, Down, Rupee }

    private static float TableBlockHeight(int rowCount)
        => TableHeadH + (rowCount == 0 ? 38f : rowCount * RowH);

    private static float NotesBoxHeight(bool hasSplit)
        => 16f + ((hasSplit ? 5 : 4) * 14f);

    private static float ClosingHeight(bool multiPage, bool hasSplit)
    {
        var h = InfoBoxH + 16f;

        if (multiPage)
            h += SectionH + 8f + SummaryCardH + 16f;

        h += SectionH + 8f + NotesBoxHeight(hasSplit);

        return h;
    }

    private static List<PagePlan> BuildPlans(int total, bool hasSplit)
    {
        var cap1 = (int)Math.Floor((ContentBottom - FirstTableTop - TableHeadH - 2f) / RowH);
        var capN = (int)Math.Floor((ContentBottom - NextTableTop - TableHeadH - 2f) / RowH);

        var plans = new List<PagePlan>();

        var take = Math.Min(total, cap1);
        plans.Add(new PagePlan { Start = 0, Count = take, IsFirst = true });

        var idx = take;

        while (idx < total)
        {
            take = Math.Min(total - idx, capN);
            plans.Add(new PagePlan { Start = idx, Count = take });
            idx += take;
        }

        var last = plans[^1];
        var top = last.IsFirst ? FirstTableTop : NextTableTop;
        var bottom = top + TableBlockHeight(last.Count);
        var need = ClosingHeight(plans.Count > 1, hasSplit);

        if (bottom + 16f + need <= ContentBottom)
        {
            last.ShowClosing = true;
        }
        else
        {
            plans.Add(new PagePlan
            {
                Start = total,
                Count = 0,
                ShowTable = false,
                ShowClosing = true
            });
        }

        return plans;
    }

    // =====================================================================
    // HELPERS
    // =====================================================================

    private static bool IsType(string? value, string type)
        => string.Equals(value, type, StringComparison.OrdinalIgnoreCase);

    private static AndroidColor TypeColor(string? type)
    {
        if (IsType(type, "Lend")) return Green;
        if (IsType(type, "Borrow")) return Red;
        if (IsType(type, "Split")) return Orange;
        return Gray;
    }

    private static string Money(decimal value) => $"₹ {value:N2}";

    private static string CountText(int n) => $"{n} transaction{(n == 1 ? "" : "s")}";

    /// <summary>"PartialReturn" -> "Partial Return"</summary>
    private static string Pretty(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        var sb = new StringBuilder();

        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i]) && char.IsLower(s[i - 1]))
                sb.Append(' ');

            sb.Append(s[i]);
        }

        return sb.ToString();
    }

    private static string SanitizeFileName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Contact";

        foreach (var character in IOPath.GetInvalidFileNameChars())
            value = value.Replace(character.ToString(), string.Empty);

        value = value.Trim();

        return string.IsNullOrWhiteSpace(value) ? "Contact" : value;
    }

    // =====================================================================
    // RENDERER  (all drawing lives here)
    // =====================================================================

    private sealed class Renderer : IDisposable
    {
        private readonly AndroidPaint _fill = new(PaintFlags.AntiAlias);
        private readonly AndroidPaint _stroke = new(PaintFlags.AntiAlias);
        private readonly AndroidPaint _text = new(PaintFlags.AntiAlias);
        private readonly AndroidPaint _grad = new(PaintFlags.AntiAlias);

        private readonly Typeface? _regular;
        private readonly Typeface? _bold;
        private readonly Typeface? _script;

        private AndroidCanvas _c = null!;

        public AndroidCanvas Canvas
        {
            set => _c = value;
        }

        public Renderer()
        {
            _fill.SetStyle(AndroidPaint.Style.Fill);
            _text.SetStyle(AndroidPaint.Style.Fill);
            _grad.SetStyle(AndroidPaint.Style.Fill);

            _stroke.SetStyle(AndroidPaint.Style.Stroke);
            _stroke.StrokeCap = AndroidPaint.Cap.Round;
            _stroke.StrokeJoin = AndroidPaint.Join.Round;

            _regular = Typeface.Default;
            _bold = Typeface.Create(Typeface.Default, TypefaceStyle.Bold);
            _script = Typeface.Create("cursive", TypefaceStyle.Italic);
        }

        public void Dispose()
        {
            _fill.Dispose();
            _stroke.Dispose();
            _text.Dispose();
            _grad.Dispose();
            _bold?.Dispose();
            _script?.Dispose();
        }

        // -----------------------------------------------------------------
        // PRIMITIVES
        // -----------------------------------------------------------------

        private void FillRect(float l, float t, float r, float b, AndroidColor c)
        {
            _fill.Color = c;
            _c.DrawRect(l, t, r, b, _fill);
        }

        private void RRect(AndroidRectF rect, float radius, AndroidColor c)
        {
            _fill.Color = c;
            _c.DrawRoundRect(rect, radius, radius, _fill);
        }

        private void RRectStroke(AndroidRectF rect, float radius, AndroidColor c, float w)
        {
            _stroke.Color = c;
            _stroke.StrokeWidth = w;
            _c.DrawRoundRect(rect, radius, radius, _stroke);
        }

        private void Circle(float cx, float cy, float r, AndroidColor c)
        {
            _fill.Color = c;
            _c.DrawCircle(cx, cy, r, _fill);
        }

        private void CircleStroke(float cx, float cy, float r, AndroidColor c, float w)
        {
            _stroke.Color = c;
            _stroke.StrokeWidth = w;
            _c.DrawCircle(cx, cy, r, _stroke);
        }

        private void Line(float x1, float y1, float x2, float y2, AndroidColor c, float w)
        {
            _stroke.Color = c;
            _stroke.StrokeWidth = w;
            _c.DrawLine(x1, y1, x2, y2, _stroke);
        }

        private void Poly(AndroidColor c, float w, params (float X, float Y)[] pts)
        {
            using var p = new AndroidPath();
            p.MoveTo(pts[0].X, pts[0].Y);

            for (var i = 1; i < pts.Length; i++)
                p.LineTo(pts[i].X, pts[i].Y);

            _stroke.Color = c;
            _stroke.StrokeWidth = w;
            _c.DrawPath(p, _stroke);
        }

        private void FillPath(AndroidColor c, Action<AndroidPath> build)
        {
            using var p = new AndroidPath();
            build(p);
            p.Close();

            _fill.Color = c;
            _c.DrawPath(p, _fill);
        }

        // -----------------------------------------------------------------
        // TEXT
        // -----------------------------------------------------------------

        private void Prep(float size, AndroidColor c, bool bold)
        {
            _text.TextSize = size;
            _text.Color = c;
            _text.SetTypeface(bold ? _bold : _regular);
        }

        private float Measure(string s, float size, bool bold)
        {
            Prep(size, Navy, bold);
            return _text.MeasureText(s);
        }

        private float Text(string? s, float x, float baseline, float size, AndroidColor c, bool bold = false)
        {
            s ??= string.Empty;
            Prep(size, c, bold);
            _c.DrawText(s, x, baseline, _text);
            return _text.MeasureText(s);
        }

        private void TextRight(string? s, float right, float baseline, float size, AndroidColor c, bool bold = false)
        {
            s ??= string.Empty;
            var w = Measure(s, size, bold);
            Text(s, right - w, baseline, size, c, bold);
        }

        private void TextCenter(string? s, float cx, float baseline, float size, AndroidColor c, bool bold = false)
        {
            s ??= string.Empty;
            var w = Measure(s, size, bold);
            Text(s, cx - (w / 2f), baseline, size, c, bold);
        }

        private string Fit(string? s, float size, bool bold, float maxWidth)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            if (Measure(s, size, bold) <= maxWidth) return s;

            const string ellipsis = "…";
            var len = s.Length;

            while (len > 1 && Measure(s[..len] + ellipsis, size, bold) > maxWidth)
                len--;

            return s[..len].TrimEnd() + ellipsis;
        }

        // -----------------------------------------------------------------
        // ICONS
        // -----------------------------------------------------------------

        private void WalletLogo(float x, float y, float size)
        {
            var rect = new AndroidRectF(x, y, x + size, y + size);
            var radius = size * 0.26f;

            using (var shader = new AndroidLinearGradient(
                       x, y, x + size, y + size,
                       OrangeLight.ToArgb(), OrangeDeep.ToArgb(),
                       AndroidShader.TileMode.Clamp!))
            {
                _grad.SetShader(shader);
                _c.DrawRoundRect(rect, radius, radius, _grad);
                _grad.SetShader(null);
            }

            // flap
            RRect(new AndroidRectF(x + size * 0.26f, y + size * 0.22f, x + size * 0.62f, y + size * 0.34f),
                size * 0.05f, White);

            // body
            RRect(new AndroidRectF(x + size * 0.18f, y + size * 0.30f, x + size * 0.82f, y + size * 0.74f),
                size * 0.09f, White);

            // clasp
            RRect(new AndroidRectF(x + size * 0.54f, y + size * 0.43f, x + size * 0.86f, y + size * 0.61f),
                size * 0.07f, OrangeLight);

            Circle(x + size * 0.68f, y + size * 0.52f, size * 0.035f, White);
        }

        private void CalendarIcon(float cx, float cy, float s, AndroidColor c)
        {
            var r = new AndroidRectF(cx - s / 2f, cy - s / 2f + 1.5f, cx + s / 2f, cy + s / 2f);
            RRectStroke(r, 2f, c, 1.1f);
            Line(r.Left, r.Top + 4.5f, r.Right, r.Top + 4.5f, c, 1.1f);
            Line(cx - s * 0.2f, r.Top - 1.5f, cx - s * 0.2f, r.Top + 1.5f, c, 1.3f);
            Line(cx + s * 0.2f, r.Top - 1.5f, cx + s * 0.2f, r.Top + 1.5f, c, 1.3f);
        }

        private void PersonIcon(float cx, float cy, float s, AndroidColor c)
        {
            Circle(cx, cy - s * 0.18f, s * 0.2f, c);

            var body = new AndroidRectF(cx - s * 0.34f, cy + s * 0.06f, cx + s * 0.34f, cy + s * 0.06f + s * 0.64f);
            _fill.Color = c;
            _c.DrawArc(body, 180f, 180f, true, _fill);
        }

        private void PhoneIcon(float cx, float cy, float s, AndroidColor c)
        {
            var r = new AndroidRectF(cx - s * 0.27f, cy - s * 0.5f, cx + s * 0.27f, cy + s * 0.5f);
            RRectStroke(r, 1.8f, c, 1.2f);
            Line(cx - s * 0.08f, cy + s * 0.32f, cx + s * 0.08f, cy + s * 0.32f, c, 1.1f);
        }

        private void MailIcon(float cx, float cy, float s, AndroidColor c)
        {
            var r = new AndroidRectF(cx - s * 0.55f, cy - s * 0.4f, cx + s * 0.55f, cy + s * 0.4f);
            RRect(r, 1.8f, c);
            Poly(White, 1f,
                (r.Left + 1.2f, r.Top + 1.8f),
                (cx, cy + s * 0.08f),
                (r.Right - 1.2f, r.Top + 1.8f));
        }

        private void InfoIcon(float cx, float cy, float r, AndroidColor c)
        {
            CircleStroke(cx, cy, r, c, 1.3f);
            Circle(cx, cy - r * 0.42f, 0.9f, c);
            Line(cx, cy - r * 0.08f, cx, cy + r * 0.5f, c, 1.4f);
        }

        private void ArrowIcon(float cx, float cy, float s, bool up, AndroidColor c)
        {
            var tipY = up ? cy - s * 0.5f : cy + s * 0.5f;
            var tailY = up ? cy + s * 0.5f : cy - s * 0.5f;
            var headY = up ? tipY + s * 0.32f : tipY - s * 0.32f;

            Poly(c, 1.9f, (cx, tailY), (cx, tipY));
            Poly(c, 1.9f, (cx - s * 0.32f, headY), (cx, tipY), (cx + s * 0.32f, headY));
        }

        private void BarChartIcon(float cx, float cy, float s)
        {
            var bw = s * 0.22f;
            var baseY = cy + s * 0.4f;

            FillRect(cx - s * 0.38f, baseY - s * 0.35f, cx - s * 0.38f + bw, baseY, White);
            FillRect(cx - s * 0.11f, baseY - s * 0.6f, cx - s * 0.11f + bw, baseY, White);
            FillRect(cx + s * 0.16f, baseY - s * 0.8f, cx + s * 0.16f + bw, baseY, White);
        }

        private void ListIcon(float cx, float cy, float s)
        {
            for (var k = -1; k <= 1; k++)
            {
                var yy = cy + k * s * 0.3f;
                Circle(cx - s * 0.32f, yy, s * 0.07f, White);
                Line(cx - s * 0.14f, yy, cx + s * 0.38f, yy, White, 1.3f);
            }
        }

        private void DocIcon(float cx, float cy, float s)
        {
            RRect(new AndroidRectF(cx - s * 0.28f, cy - s * 0.38f, cx + s * 0.28f, cy + s * 0.38f), 1.8f, White);

            for (var k = -1; k <= 1; k++)
                Line(cx - s * 0.14f, cy + k * s * 0.18f, cx + s * 0.14f, cy + k * s * 0.18f, Orange, 1.1f);
        }

        // -----------------------------------------------------------------
        // BACKGROUND / WAVES
        // -----------------------------------------------------------------

        public void DrawBackground()
        {
            _c.DrawColor(White);

            const float w = PageW;
            const float h = PageH;

            // top-right swoosh
            FillPath(PeachWave, p =>
            {
                p.MoveTo(w - 170, 0);
                p.LineTo(w, 0);
                p.LineTo(w, 52);
                p.CubicTo(w - 35, 50, w - 80, 34, w - 115, 22);
                p.CubicTo(w - 140, 13, w - 155, 6, w - 170, 0);
            });

            FillPath(OrangeLight, p =>
            {
                p.MoveTo(w - 125, 0);
                p.LineTo(w, 0);
                p.LineTo(w, 38);
                p.CubicTo(w - 30, 36, w - 60, 22, w - 82, 11);
                p.CubicTo(w - 98, 5, w - 112, 2, w - 125, 0);
            });

            FillPath(Orange, p =>
            {
                p.MoveTo(w - 75, 0);
                p.LineTo(w, 0);
                p.LineTo(w, 24);
                p.CubicTo(w - 25, 22, w - 48, 10, w - 75, 0);
            });

            // bottom wave
            FillPath(PeachWave, p =>
            {
                p.MoveTo(0, h - 36);
                p.CubicTo(100, h - 50, 200, h - 18, 320, h - 32);
                p.CubicTo(430, h - 44, 520, h - 22, w, h - 36);
                p.LineTo(w, h);
                p.LineTo(0, h);
            });

            FillPath(OrangeLight, p =>
            {
                p.MoveTo(0, h - 24);
                p.CubicTo(110, h - 38, 210, h - 8, 330, h - 22);
                p.CubicTo(440, h - 34, 520, h - 12, w, h - 24);
                p.LineTo(w, h);
                p.LineTo(0, h);
            });

            FillPath(Orange, p =>
            {
                p.MoveTo(0, h - 12);
                p.CubicTo(120, h - 24, 230, h - 2, 340, h - 11);
                p.CubicTo(450, h - 22, 530, h - 4, w, h - 12);
                p.LineTo(w, h);
                p.LineTo(0, h);
            });
        }

        // -----------------------------------------------------------------
        // HEADER
        // -----------------------------------------------------------------

        public void DrawHeader(string generatedAt)
        {
            WalletLogo(M, 26f, 50f);

            var x = M + 62f;

            var w = Text("Money", x, 50f, 25f, Navy, true);
            Text("Spend", x + w, 50f, 25f, Orange, true);

            Text("Contact Financial Statement", x, 67f, 11f, Navy, true);
            Text("Track  •  Share  •  Settle", x, 81f, 8f, Gray);

            var right = PageW - M;
            var dateW = Measure(generatedAt, 8.5f, true);

            CalendarIcon(right - dateW - 16f, 66f, 13f, Navy);
            Text("Generated on", right - dateW, 61f, 7.5f, Gray);
            Text(generatedAt, right - dateW, 73f, 8.5f, Navy, true);

            Line(M, 98f, PageW - M, 98f, Orange, 1.2f);
        }

        // -----------------------------------------------------------------
        // CONTACT CARD
        // -----------------------------------------------------------------

        public void DrawContactCard(float top, ReportData d)
        {
            var rect = new AndroidRectF(M, top, PageW - M, top + 56f);

            RRect(rect, 10f, OrangeTint);
            RRectStroke(rect, 10f, OrangeStroke, 1f);

            var cx = M + 32f;
            var cy = top + 28f;

            Circle(cx, cy, 20f, PeachCircle);
            PersonIcon(cx, cy + 1f, 24f, Orange);

            var x = M + 64f;

            Text(Fit(d.Name, 14f, true, ContentW - 80f), x, top + 20f, 14f, Navy, true);

            var lineY = top + 35f;

            if (!string.IsNullOrWhiteSpace(d.Mobile))
            {
                PhoneIcon(x + 5f, lineY - 3f, 11f, Navy);
                Text(d.Mobile, x + 17f, lineY, 9.5f, Slate);
                lineY += 14f;
            }

            if (!string.IsNullOrWhiteSpace(d.Email))
            {
                MailIcon(x + 5.5f, lineY - 3f, 11f, Navy);
                Text(Fit(d.Email, 9.5f, false, ContentW - 110f), x + 17f, lineY, 9.5f, Slate);
            }
        }

        // -----------------------------------------------------------------
        // SECTION HEADER
        // -----------------------------------------------------------------

        public float DrawSectionHeader(float y, SectionIcon icon, string title, string? suffix = null)
        {
            var cy = y + 12f;
            var cx = M + 12f;

            Circle(cx, cy, 12f, Orange);

            switch (icon)
            {
                case SectionIcon.Chart: BarChartIcon(cx, cy, 13f); break;
                case SectionIcon.List: ListIcon(cx, cy, 13f); break;
                default: DocIcon(cx, cy, 14f); break;
            }

            var x = M + 32f;
            var end = x + Text(title, x, y + 17f, 14f, Navy, true);

            if (!string.IsNullOrEmpty(suffix))
                end += 6f + Text(suffix, end + 6f, y + 17f, 11f, Gray);

            Line(end + 12f, cy, PageW - M, cy, OrangeLine, 0.9f);

            return y + SectionH;
        }

        // -----------------------------------------------------------------
        // SUMMARY CARDS
        // -----------------------------------------------------------------

        public void DrawSummaryCards(float top, ReportData d)
        {
            const float gap = 12f;
            var cw = (ContentW - (gap * 2f)) / 3f;

            var diff = d.TotalReceive - d.TotalOwe;

            string netSub;
            if (diff > 0.004m) netSub = "You should receive";
            else if (diff < -0.004m) netSub = "You owe";
            else netSub = "Settled";

            SummaryCard(M, top, cw, GreenTint, GreenStroke, Green, CardIcon.Up,
                "Total Lend", Money(d.TotalReceive), CountText(d.LendCount));

            SummaryCard(M + cw + gap, top, cw, RedTint, RedStroke, Red, CardIcon.Down,
                "Total Borrow", Money(d.TotalOwe), CountText(d.BorrowCount));

            SummaryCard(M + ((cw + gap) * 2f), top, cw, OrangeTint, OrangeStroke, Orange, CardIcon.Rupee,
                "Net Balance", Money(Math.Abs(diff)), netSub);
        }

        private void SummaryCard(
            float x, float top, float w,
            AndroidColor tint, AndroidColor stroke, AndroidColor accent,
            CardIcon icon, string label, string amount, string sub)
        {
            var rect = new AndroidRectF(x, top, x + w, top + SummaryCardH);

            RRect(rect, 10f, tint);
            RRectStroke(rect, 10f, stroke, 1f);

            var cx = x + 25f;
            var cy = top + 25f;

            Circle(cx, cy, 13f, accent);

            switch (icon)
            {
                case CardIcon.Up: ArrowIcon(cx, cy, 12f, true, White); break;
                case CardIcon.Down: ArrowIcon(cx, cy, 12f, false, White); break;
                default: TextCenter("₹", cx, cy + 4.8f, 14f, White, true); break;
            }

            Text(label, x + 12f, top + 56f, 9.5f, Navy, true);

            var size = 15f;
            while (size > 9f && Measure(amount, size, true) > w - 24f)
                size -= 0.5f;

            Text(amount, x + 12f, top + 72f, size, accent, true);
            Text(sub, x + 12f, top + 83f, 8f, Gray);
        }

        // -----------------------------------------------------------------
        // TABLE
        // -----------------------------------------------------------------

        public float DrawTable(float top, IReadOnlyList<ContactHistoryItem> rows)
        {
            const float colDate = M + 10f;
            const float colType = M + 84f;
            const float colMove = M + 146f;
            const float amountRight = M + 360f;
            const float colNote = M + 378f;
            const float noteMax = ContentW - 378f - 8f;

            var h = TableBlockHeight(rows.Count);
            var rect = new AndroidRectF(M, top, PageW - M, top + h);

            _c.Save();

            using (var clip = new AndroidPath())
            {
                clip.AddRoundRect(rect, 10f, 10f, AndroidPath.Direction.Cw!);
                _c.ClipPath(clip);
            }

            FillRect(M, top, PageW - M, top + h, White);

            // header
            FillRect(M, top, PageW - M, top + TableHeadH, TableHeadBg);

            var hb = top + 14.5f;

            Text("DATE", colDate, hb, 7f, Slate, true);
            Text("TYPE", colType, hb, 7f, Slate, true);
            Text("MOVEMENT", colMove, hb, 7f, Slate, true);
            TextRight("AMOUNT", amountRight, hb, 7f, Slate, true);
            Text("NOTE", colNote, hb, 7f, Slate, true);

            if (rows.Count == 0)
            {
                TextCenter("No transaction history available.",
                    PageW / 2f, top + TableHeadH + 23f, 9f, Gray);
            }

            for (var i = 0; i < rows.Count; i++)
            {
                var item = rows[i];
                var rt = top + TableHeadH + (i * RowH);
                var cy = rt + (RowH / 2f);
                var accent = TypeColor(item.RecordType);

                if (i % 2 == 1)
                    FillRect(M, rt, PageW - M, rt + RowH, RowAlt);

                Text(item.Date.ToString("dd MMM yyyy", CultureInfo.InvariantCulture),
                    colDate, cy + 3f, 8.5f, Navy, true);

                // type pill
                var pillLabel = string.IsNullOrWhiteSpace(item.RecordType) ? "-" : item.RecordType;
                RRect(new AndroidRectF(colType, cy - 7.5f, colType + 46f, cy + 7.5f), 7.5f, accent);
                TextCenter(Fit(pillLabel, 7.5f, true, 38f), colType + 23f, cy + 2.7f, 7.5f, White, true);

                Text(Fit(Pretty(item.MovementLabel), 8.5f, false, 104f), colMove, cy + 3f, 8.5f, Slate);

                TextRight(Money(item.Amount), amountRight, cy + 3.2f, 9f, accent, true);

                if (!string.IsNullOrWhiteSpace(item.Notes))
                    Text(Fit(item.Notes, 7.5f, false, noteMax), colNote, cy + 2.8f, 7.5f, Gray);
            }

            _c.Restore();

            RRectStroke(rect, 10f, LineColor, 1f);

            return top + h;
        }

        // -----------------------------------------------------------------
        // CLOSING BLOCKS (info note, quick summary, notes)
        // -----------------------------------------------------------------

        public void DrawClosing(float y, bool multiPage, ReportData d)
        {
            // info note
            var box = new AndroidRectF(M, y, PageW - M, y + InfoBoxH);

            RRect(box, 8f, OrangeTint);
            RRectStroke(box, 8f, OrangeStroke, 1f);

            InfoIcon(M + 18f, y + (InfoBoxH / 2f), 8.5f, Orange);

            var kinds = d.HasSplit ? "Borrow, Lend and Split" : "Borrow and Lend";

            Text($"This statement includes your {kinds} transactions only.",
                M + 38f, y + 19f, 8f, Slate);
            Text("Keep track of your shared expenses and settle easily!",
                M + 38f, y + 32f, 8f, Slate);

            // handwritten tagline
            const string script = "Better Together";

            _text.TextSize = 14f;
            _text.Color = Orange;
            _text.SetTypeface(_script);

            var sw = _text.MeasureText(script);
            var sx = PageW - M - 18f - sw;

            _c.DrawText(script, sx, y + 25f, _text);

            using (var swoosh = new AndroidPath())
            {
                swoosh.MoveTo(sx, y + 33f);
                swoosh.CubicTo(sx + (sw * 0.35f), y + 28f, sx + (sw * 0.7f), y + 36f, sx + sw, y + 29f);

                _stroke.Color = Orange;
                _stroke.StrokeWidth = 1.2f;
                _c.DrawPath(swoosh, _stroke);
            }

            y += InfoBoxH + 16f;

            // quick summary (recap for multi-page statements)
            if (multiPage)
            {
                y = DrawSectionHeader(y, SectionIcon.Chart, "Quick Summary");
                y += 8f;

                DrawSummaryCards(y, d);
                y += SummaryCardH + 16f;
            }

            // notes
            y = DrawSectionHeader(y, SectionIcon.Doc, "Notes");
            y += 8f;

            var notes = new List<string>
            {
                "Initial Lend / Initial Borrow is the original amount given or taken.",
                "Return and Receive entries reduce the pending balance of that record.",
                "Lend means you gave money; Borrow means you took money from someone."
            };

            if (d.HasSplit)
                notes.Add("Split means you shared the expense with others.");

            notes.Add("All amounts are in Indian Rupees (₹).");

            var notesRect = new AndroidRectF(M, y, PageW - M, y + NotesBoxHeight(d.HasSplit));

            RRect(notesRect, 8f, RowAlt);
            RRectStroke(notesRect, 8f, LineColor, 1f);

            var by = y + 17f;

            foreach (var note in notes)
            {
                Circle(M + 14f, by - 2.6f, 1.3f, Gray);
                Text(note, M + 24f, by, 8f, Slate);
                by += 14f;
            }
        }

        // -----------------------------------------------------------------
        // FOOTER
        // -----------------------------------------------------------------

        public void DrawFooter(int page, int total)
        {
            WalletLogo(M, PageH - 80f, 22f);

            var x = M + 28f;

            var w = Text("Money", x, PageH - 64f, 10.5f, Navy, true);
            Text("Spend", x + w, PageH - 64f, 10.5f, Orange, true);

            TextRight($"Page {page} of {total}", PageW - M, PageH - 64f, 9f, Navy, true);
        }
    }
}

#endif