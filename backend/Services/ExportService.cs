using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using PdfSharpCore.Drawing;
using PdfSharpCore.Fonts;
using PdfSharpCore.Pdf;

namespace TaxFlow.Api.Services;

public sealed record ExportTable(string Title, IReadOnlyList<string> Headers, IReadOnlyList<IReadOnlyList<string>> Rows);

public sealed class ExportService
{
    public byte[] ToCsv(ExportTable table)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', table.Headers.Select(EscapeCsv)));
        foreach (var row in table.Rows) sb.AppendLine(string.Join(',', row.Select(EscapeCsv)));
        var content = Encoding.UTF8.GetBytes(sb.ToString());
        return Encoding.UTF8.GetPreamble().Concat(content).ToArray();
    }

    public byte[] ToXlsx(ExportTable table)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            WriteEntry(archive, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            WriteEntry(archive, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/workbook.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="TaxFlow" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            WriteEntry(archive, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            WriteEntry(archive, "xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="2"><font><sz val="11"/><name val="Arial"/></font><font><b/><color rgb="FFFFFFFF"/><sz val="11"/><name val="Arial"/></font></fonts>
                  <fills count="3"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill><fill><patternFill patternType="solid"><fgColor rgb="FF194FC4"/><bgColor indexed="64"/></patternFill></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="2" borderId="0" xfId="0" applyFill="1" applyFont="1"/></cellXfs>
                </styleSheet>
                """);

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><sheetData>");
            AppendRow(xml, table.Headers, 1, true);
            for (var i = 0; i < table.Rows.Count; i++) AppendRow(xml, table.Rows[i], i + 2, false);
            xml.Append("</sheetData><autoFilter ref=\"A1:").Append(ColumnName(table.Headers.Count)).Append(Math.Max(1, table.Rows.Count + 1)).Append("\"/></worksheet>");
            WriteEntry(archive, "xl/worksheets/sheet1.xml", xml.ToString());
        }
        return output.ToArray();
    }

    public byte[] ToPdf(ExportTable table)
    {
        lock (FontLock) { GlobalFontSettings.FontResolver ??= new BundledFontResolver(); }
        using var doc = new PdfDocument();
        doc.Info.Title = table.Title;
        var font = new XFont("TaxFlowSans", 8);
        var heading = new XFont("TaxFlowSans", 15);
        XGraphics? graphics = null;
        double y = 0;
        int pageNumber = 0;
        var widths = Enumerable.Repeat(770.0 / Math.Max(1, table.Headers.Count), table.Headers.Count).ToArray();
        void NewPage()
        {
            graphics?.Dispose();
            var page = doc.AddPage(); page.Orientation = PdfSharpCore.PageOrientation.Landscape;
            graphics = XGraphics.FromPdfPage(page); pageNumber++; y = 36;
            graphics.DrawString(table.Title, heading, XBrushes.DarkBlue, new XPoint(30, y)); y += 18;
            graphics.DrawString($"TaxFlow AI · Trang {pageNumber} · {table.Rows.Count} bản ghi", font, XBrushes.Gray, new XPoint(30, y)); y += 22;
            for (int col = 0; col < table.Headers.Count; col++) graphics.DrawString(table.Headers[col], font, XBrushes.DarkBlue, new XPoint(30 + col * widths[col], y));
            y += 14; graphics.DrawLine(XPens.LightGray, 30, y - 6, 810, y - 6);
        }
        NewPage();
        foreach (var row in table.Rows)
        {
            var wrapped = Enumerable.Range(0, table.Headers.Count).Select(col => Wrap(row.ElementAtOrDefault(col) ?? "", widths[col] - 8, font, graphics!)).ToArray();
            var lines = wrapped.Max(x => x.Count);
            for (int line = 0; line < lines; line++)
            {
                if (y > 550) NewPage();
                for (int col = 0; col < wrapped.Length; col++)
                    if (line < wrapped[col].Count) graphics!.DrawString(wrapped[col][line], font, XBrushes.Black, new XPoint(30 + col * widths[col], y));
                y += 12;
            }
            graphics!.DrawLine(XPens.LightGray, 30, y - 4, 810, y - 4); y += 5;
        }
        graphics?.Dispose();
        using var output = new MemoryStream(); doc.Save(output, false); return output.ToArray();
    }
    private static readonly object FontLock = new();
    private static List<string> Wrap(string value, double width, XFont font, XGraphics graphics)
    {
        var result = new List<string>(); var line = "";
        foreach (var ch in value.Replace("\r", ""))
        {
            if (ch == '\n') { result.Add(line); line = ""; continue; }
            if (line.Length > 0 && graphics.MeasureString(line + ch, font).Width > width) { result.Add(line); line = ""; }
            line += ch;
        }
        result.Add(line); return result;
    }
    private sealed class BundledFontResolver : IFontResolver
    {
        public string DefaultFontName => "TaxFlowSans";
        public byte[] GetFont(string faceName) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf"));
        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) => new("TaxFlowSans");
    }

    private static void AppendRow(StringBuilder xml, IReadOnlyList<string> values, int rowNumber, bool header)
    {
        xml.Append("<row r=\"").Append(rowNumber).Append("\">");
        for (var i = 0; i < values.Count; i++)
        {
            xml.Append("<c r=\"").Append(ColumnName(i + 1)).Append(rowNumber).Append("\" t=\"inlineStr\"");
            if (header) xml.Append(" s=\"1\"");
            xml.Append("><is><t xml:space=\"preserve\">").Append(XmlEscape(values[i])).Append("</t></is></c>");
        }
        xml.Append("</row>");
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        while (index > 0) { index--; name = (char)('A' + index % 26) + name; index /= 26; }
        return name;
    }

    private static void WriteEntry(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }

    private static string EscapeCsv(string value)
    {
        var trimmed = value.TrimStart();
        if (trimmed.Length > 0 && (trimmed[0] is '=' or '+' or '@' || trimmed[0] == '-' && !decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.InvariantCulture, out _))) value = "'" + value;
        return value.ContainsAny(',', '"', '\n', '\r') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
    private static string XmlEscape(string value) => System.Security.SecurityElement.Escape(value) ?? string.Empty;
    private static string EscapePdf(string value) => value.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var chars = normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray();
        return new string(chars).Normalize(NormalizationForm.FormC).Replace('đ', 'd').Replace('Đ', 'D');
    }
}

file static class StringExtensions
{
    public static bool ContainsAny(this string value, params char[] chars) => chars.Any(value.Contains);
}
