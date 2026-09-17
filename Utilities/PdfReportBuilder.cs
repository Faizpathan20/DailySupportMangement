using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace Master.Utilities;

// Minimal, dependency-free PDF generator used by the Reports page.
// Produces a professional A4 document with:
//   - report title + filter meta + generated date/time
//   - summary lines
//   - a bordered table with a repeated header on every page
//   - "Page X of Y" and total-records footer on every page.
//
// No external NuGet package is used; objects are written by hand
// (PDF 1.4) using the built-in Helvetica / Helvetica-Bold fonts.
public static class PdfReportBuilder
{
    private const double PageWidth = 595.28;  // A4 points
    private const double PageHeight = 841.89;
    private const double Margin = 40;
    private const double RowHeight = 16;
    private const double HeaderHeight = 18;
    private const double FooterSpace = 28;

    private static readonly CultureInfo Inv =
        CultureInfo.InvariantCulture;


    public static byte[] Build(
        string title,
        IReadOnlyList<string> metaLines,
        IReadOnlyList<string> summaryLines,
        IReadOnlyList<string> headers,
        IReadOnlyList<string[]> rows,
        string totalNote,
        IReadOnlyList<double>? columnWidths = null)
    {
        int colCount = Math.Max(1, headers.Count);

        double usable = PageWidth - (2 * Margin);

        double[] widths =
            ComputeWidths(colCount, usable, columnWidths);

        if (rows.Count == 0)
        {
            rows = new List<string[]>
            {
                Enumerable
                    .Repeat("No records found.", colCount)
                    .ToArray()
            };
        }

        // Vertical space taken by the first-page header block.
        double firstHeader =
            24
            + (metaLines.Count * 13)
            + 6
            + (summaryLines.Count * 14)
            + 22
            + HeaderHeight
            + 8;

        double otherHeader =
            18 + HeaderHeight + 8;

        double firstAvail =
            PageHeight - Margin - firstHeader - FooterSpace;

        double otherAvail =
            PageHeight - Margin - otherHeader - FooterSpace;

        int firstRows =
            Math.Max(1, (int)Math.Floor(firstAvail / RowHeight));

        int otherRows =
            Math.Max(1, (int)Math.Floor(otherAvail / RowHeight));

        // Paginate rows.
        var pages = new List<List<string[]>>();

        int index = 0;
        int capacity = firstRows;

        while (index < rows.Count)
        {
            int take = Math.Min(
                capacity,
                rows.Count - index);

            var chunk = new List<string[]>();

            for (int i = 0; i < take; i++)
            {
                chunk.Add(rows[index + i]);
            }

            pages.Add(chunk);

            index += take;
            capacity = otherRows;
        }

        int totalPages = pages.Count;

        var pageContents = new List<string>();

        for (int p = 0; p < totalPages; p++)
        {
            var sb = new StringBuilder();

            double topY = PageHeight - Margin;

            double y;

            if (p == 0)
            {
                DrawText(
                    sb, Margin, topY - 16, title,
                    16, true, 0.09, 0.12, 0.23);

                y = topY - 24;

                foreach (var meta in metaLines)
                {
                    DrawText(
                        sb, Margin, y - 9, meta,
                        9, false, 0.35, 0.35, 0.35);

                    y -= 13;
                }

                y -= 6;

                foreach (var line in summaryLines)
                {
                    DrawText(
                        sb, Margin, y - 10, line,
                        10, false, 0.12, 0.16, 0.28);

                    y -= 14;
                }

                y -= 10;

                DrawLine(
                    sb, Margin, y, PageWidth - Margin, y,
                    0.80, 0.82, 0.86, 0.8);

                y -= 12;

                DrawHeaderRow(sb, headers, widths, y);

                y -= HeaderHeight;
            }
            else
            {
                DrawText(
                    sb, Margin, topY - 11,
                    title + "  (continued)",
                    11, true, 0.09, 0.12, 0.23);

                y = topY - 18;

                DrawHeaderRow(sb, headers, widths, y);

                y -= HeaderHeight;
            }

            foreach (var row in pages[p])
            {
                DrawDataRow(sb, row, widths, y);

                y -= RowHeight;
            }

            // ---- Footer ----
            DrawText(
                sb, Margin, Margin - 10, totalNote,
                8, false, 0.45, 0.45, 0.45);

            string pageText =
                "Page " + (p + 1) + " of " + totalPages;

            double pw = EstimateWidth(pageText, 8, false);

            DrawText(
                sb, PageWidth - Margin - pw, Margin - 10,
                pageText, 8, false, 0.45, 0.45, 0.45);

            pageContents.Add(sb.ToString());
        }

        return Serialize(pageContents);
    }


    // ---- Widths ----

    private static double[] ComputeWidths(
        int colCount,
        double usable,
        IReadOnlyList<double>? columnWidths)
    {
        var widths = new double[colCount];

        bool useCustom =
            columnWidths != null
            && columnWidths.Count == colCount
            && columnWidths.Sum() > 0;

        for (int i = 0; i < colCount; i++)
        {
            widths[i] = useCustom
                ? usable * (columnWidths![i] / columnWidths.Sum())
                : usable / colCount;
        }

        return widths;
    }


    // ---- Drawing primitives ----

    private static void DrawHeaderRow(
        StringBuilder sb,
        IReadOnlyList<string> headers,
        double[] widths,
        double topY)
    {
        FillRect(
            sb, Margin, topY - HeaderHeight,
            widths.Sum(), HeaderHeight,
            0.93, 0.95, 0.98);

        double x = Margin;

        for (int i = 0; i < headers.Count; i++)
        {
            DrawText(
                sb, x + 5, topY - 12,
                Fit(headers[i], widths[i] - 10, 9),
                9, true, 0.12, 0.16, 0.28);

            x += widths[i];
        }
    }


    private static void DrawDataRow(
        StringBuilder sb,
        string[] row,
        double[] widths,
        double topY)
    {
        double x = Margin;

        for (int i = 0; i < widths.Length; i++)
        {
            string value = i < row.Length ? row[i] : "";

            DrawText(
                sb, x + 5, topY - 11,
                Fit(value, widths[i] - 10, 8.5),
                8.5, false, 0.16, 0.19, 0.24);

            x += widths[i];
        }

        DrawLine(
            sb, Margin, topY - RowHeight,
            Margin + widths.Sum(), topY - RowHeight,
            0.90, 0.92, 0.95, 0.5);
    }


    private static void DrawText(
        StringBuilder sb,
        double x,
        double y,
        string text,
        double size,
        bool bold,
        double r,
        double g,
        double b)
    {
        sb.Append("BT\n");
        sb.Append(bold ? "/F2 " : "/F1 ");
        sb.Append(size.ToString("0.##", Inv)).Append(" Tf\n");
        sb.Append(r.ToString("0.###", Inv)).Append(' ')
          .Append(g.ToString("0.###", Inv)).Append(' ')
          .Append(b.ToString("0.###", Inv)).Append(" rg\n");
        sb.Append(x.ToString("0.##", Inv)).Append(' ')
          .Append(y.ToString("0.##", Inv)).Append(" Td\n");
        sb.Append('(').Append(Escape(text)).Append(") Tj\n");
        sb.Append("ET\n");
    }


    private static void FillRect(
        StringBuilder sb,
        double x,
        double y,
        double w,
        double h,
        double r,
        double g,
        double b)
    {
        sb.Append(r.ToString("0.###", Inv)).Append(' ')
          .Append(g.ToString("0.###", Inv)).Append(' ')
          .Append(b.ToString("0.###", Inv)).Append(" rg ");
        sb.Append(x.ToString("0.##", Inv)).Append(' ')
          .Append(y.ToString("0.##", Inv)).Append(' ')
          .Append(w.ToString("0.##", Inv)).Append(' ')
          .Append(h.ToString("0.##", Inv)).Append(" re f\n");
    }


    private static void DrawLine(
        StringBuilder sb,
        double x1,
        double y1,
        double x2,
        double y2,
        double r,
        double g,
        double b,
        double width)
    {
        sb.Append(r.ToString("0.###", Inv)).Append(' ')
          .Append(g.ToString("0.###", Inv)).Append(' ')
          .Append(b.ToString("0.###", Inv)).Append(" RG ");
        sb.Append(width.ToString("0.##", Inv)).Append(" w ");
        sb.Append(x1.ToString("0.##", Inv)).Append(' ')
          .Append(y1.ToString("0.##", Inv)).Append(" m ")
          .Append(x2.ToString("0.##", Inv)).Append(' ')
          .Append(y2.ToString("0.##", Inv)).Append(" l S\n");
    }


    // ---- Text helpers ----

    private static string Fit(
        string? text,
        double width,
        double size)
    {
        text ??= "";

        if (EstimateWidth(text, size, false) <= width)
        {
            return text;
        }

        double limit =
            width - EstimateWidth("...", size, false);

        var sb = new StringBuilder();
        double used = 0;

        foreach (char ch in text)
        {
            double cw = CharWidth(ch) * size;

            if (used + cw > limit)
            {
                break;
            }

            sb.Append(ch);
            used += cw;
        }

        return sb.ToString() + "...";
    }


    private static double EstimateWidth(
        string? text,
        double size,
        bool bold)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        double total = 0;

        foreach (char ch in text)
        {
            total += CharWidth(ch);
        }

        return total * size * (bold ? 1.06 : 1.0);
    }


    // Rough Helvetica width factors (fraction of em).
    private static double CharWidth(char ch)
    {
        if (ch == ' ') return 0.278;

        if (char.IsDigit(ch)) return 0.556;

        if (ch == '.' || ch == ',' || ch == ':' || ch == '/')
        {
            return 0.278;
        }

        if (char.IsUpper(ch)) return 0.667;

        return 0.5;
    }


    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var sb = new StringBuilder();

        foreach (char ch in text)
        {
            // Keep WinAnsi-safe characters; drop anything above 255.
            if (ch > 255 || ch == '\r' || ch == '\n')
            {
                sb.Append(' ');
                continue;
            }

            if (ch == '(' || ch == ')' || ch == '\\')
            {
                sb.Append('\\');
            }

            sb.Append(ch);
        }

        return sb.ToString();
    }


    // ---- PDF object serialization ----

    private static byte[] Serialize(
        List<string> pageContents)
    {
        int pageCount = pageContents.Count;

        // Object numbering:
        //   1 Catalog, 2 Pages, 3 Helvetica, 4 Helvetica-Bold,
        //   then per page: page object + content object.
        int firstPageObj = 5;

        var bodies = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>"
        };

        var kids = new StringBuilder();

        for (int i = 0; i < pageCount; i++)
        {
            int pageObj = firstPageObj + (i * 2);

            kids.Append(pageObj).Append(" 0 R ");
        }

        bodies.Add(
            "<< /Type /Pages /Kids ["
            + kids.ToString().Trim()
            + "] /Count " + pageCount + " >>");

        bodies.Add(
            "<< /Type /Font /Subtype /Type1 "
            + "/BaseFont /Helvetica "
            + "/Encoding /WinAnsiEncoding >>");

        bodies.Add(
            "<< /Type /Font /Subtype /Type1 "
            + "/BaseFont /Helvetica-Bold "
            + "/Encoding /WinAnsiEncoding >>");

        for (int i = 0; i < pageCount; i++)
        {
            int pageObj = firstPageObj + (i * 2);
            int contentObj = pageObj + 1;

            bodies.Add(
                "<< /Type /Page /Parent 2 0 R "
                + "/MediaBox [0 0 "
                + PageWidth.ToString("0.##", Inv) + " "
                + PageHeight.ToString("0.##", Inv) + "] "
                + "/Resources << /Font << /F1 3 0 R "
                + "/F2 4 0 R >> >> "
                + "/Contents " + contentObj + " 0 R >>");

            byte[] contentBytes =
                Encoding.Latin1.GetBytes(pageContents[i]);

            bodies.Add(
                "<< /Length " + contentBytes.Length + " >>\n"
                + "stream\n"
                + pageContents[i]
                + "\nendstream");
        }

        using var ms = new MemoryStream();

        void Write(string s) =>
            ms.Write(Encoding.Latin1.GetBytes(s));

        Write("%PDF-1.4\n");

        var offsets = new long[bodies.Count];

        for (int i = 0; i < bodies.Count; i++)
        {
            offsets[i] = ms.Position;

            Write((i + 1) + " 0 obj\n");
            Write(bodies[i]);
            Write("\nendobj\n");
        }

        long xrefPos = ms.Position;

        int total = bodies.Count + 1;

        Write("xref\n");
        Write("0 " + total + "\n");

        Write("0000000000 65535 f \n");

        for (int i = 0; i < bodies.Count; i++)
        {
            Write(
                offsets[i].ToString("D10", Inv)
                + " 00000 n \n");
        }

        Write("trailer\n");
        Write("<< /Size " + total + " /Root 1 0 R >>\n");
        Write("startxref\n");
        Write(xrefPos.ToString(Inv) + "\n");
        Write("%%EOF");

        return ms.ToArray();
    }
}