using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace BetterSanctumDev;

// A spreadsheet built from the wide run file, for reading rather than for recording.
//
// The CSV stays the record. It is appended a line at a time, so a crash costs the line
// being written; a workbook has to be rewritten whole every time, so the same crash costs
// every run in it. This is generated from the CSV on demand instead, which means it can be
// thrown away and rebuilt and nothing depends on it.
//
// No library. An xlsx is a zip of XML documents and System.IO.Compression is in the
// framework, so the alternative to writing them out by hand was a package the HUD cannot
// restore. Strings are written inline rather than through a shared string table, which is
// larger on disk and very much smaller in code.
public static class SanctumWorkbook
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string PackageRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // A spreadsheet counts days from the last day of 1899, and is one out on purpose - it
    // carries a leap day in 1900 that never happened, for compatibility with a spreadsheet
    // older than most of the people using this one.
    private static readonly DateTime ExcelEpoch = new DateTime(1899, 12, 30);

    // Returns how many rows were written, or -1 with the reason in error.
    public static int Write(string csvPath, string xlsxPath, ISet<string> skipColumns, out string error)
    {
        error = null;
        try
        {
            if (!File.Exists(csvPath))
            {
                error = $"{Path.GetFileName(csvPath)} does not exist yet";
                return -1;
            }

            var rows = File.ReadAllLines(csvPath)
                .Where(x => x.Length > 0)
                .Select(ParseCsvLine)
                .ToList();

            if (rows.Count == 0)
            {
                error = $"{Path.GetFileName(csvPath)} is empty";
                return -1;
            }

            // Which columns to carry over, decided from the header and applied to every
            // row by index, so a row with a stray extra field cannot shift the rest.
            var keep = rows[0]
                .Select((name, index) => (name, index))
                .Where(x => !skipColumns.Contains(x.name))
                .Select(x => x.index)
                .ToList();

            var sheet = BuildSheet(rows, keep);

            // Written to a temporary file and moved into place, so an export interrupted
            // half way through does not leave a corrupt workbook where a good one was.
            var temp = xlsxPath + ".tmp";
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                AddEntry(zip, "[Content_Types].xml", ContentTypes());
                AddEntry(zip, "_rels/.rels", RootRels());
                AddEntry(zip, "xl/workbook.xml", Workbook());
                AddEntry(zip, "xl/_rels/workbook.xml.rels", WorkbookRels());
                AddEntry(zip, "xl/styles.xml", Styles());
                AddEntry(zip, "xl/worksheets/sheet1.xml", sheet);
            }

            File.Move(temp, xlsxPath, overwrite: true);
            return rows.Count - 1;
        }
        catch (Exception e)
        {
            error = e.Message;
            return -1;
        }
    }

    private static string BuildSheet(List<List<string>> rows, List<int> keep)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append($"<worksheet xmlns=\"{Main}\">");

        // The header stays put while the runs scroll under it
        xml.Append("<sheetViews><sheetView workbookViewId=\"0\">");
        xml.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        xml.Append("</sheetView></sheetViews>");

        // Wide enough for the heading, since the headings are the short names and the
        // values under them are quantities
        xml.Append("<cols>");
        for (var column = 0; column < keep.Count; column++)
        {
            var width = Math.Max(rows[0][keep[column]].Length + 3, 7);
            xml.Append($"<col min=\"{column + 1}\" max=\"{column + 1}\" width=\"{width}\" customWidth=\"1\"/>");
        }

        xml.Append("</cols><sheetData>");

        // Banded by run rather than by row, so a run's rows share a colour however many it
        // wrote - and a run that ever writes one row instead of two cannot shift the
        // banding of every run after it, which counting rows would.
        var runColumn = rows[0].IndexOf("run");

        for (var row = 0; row < rows.Count; row++)
        {
            var tinted = row > 0 &&
                         runColumn >= 0 &&
                         runColumn < rows[row].Count &&
                         int.TryParse(rows[row][runColumn], out var runNumber) &&
                         runNumber % 2 == 0;

            xml.Append($"<row r=\"{row + 1}\">");
            for (var column = 0; column < keep.Count; column++)
            {
                var index = keep[column];
                var value = index < rows[row].Count ? rows[row][index] : "";
                var reference = ColumnName(column) + (row + 1);

                // An empty cell in a tinted row is still written, or the band breaks into
                // stripes wherever a run happened not to pay something.
                if (value.Length == 0)
                {
                    if (tinted)
                    {
                        xml.Append($"<c r=\"{reference}\" s=\"3\"/>");
                    }

                    continue;
                }

                // A date written as a date rather than as the text of one, so a chart can
                // put runs on a time axis instead of treating each day as its own category.
                if (row > 0 && rows[0][index] == "date" &&
                    DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var date))
                {
                    var serial = (date.Date - ExcelEpoch).TotalDays;
                    xml.Append($"<c r=\"{reference}\" s=\"{(tinted ? 4 : 2)}\"><v>{serial.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                }

                // A number written as a number, so the sheet can sum and chart it without
                // being told to convert the column first. The header row never is.
                else if (row > 0 && double.TryParse(value, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out var number))
                {
                    var style = tinted ? " s=\"3\"" : "";
                    xml.Append($"<c r=\"{reference}\"{style}><v>{number.ToString(System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                }
                else
                {
                    var style = row == 0 ? " s=\"1\"" : tinted ? " s=\"3\"" : "";
                    xml.Append($"<c r=\"{reference}\"{style} t=\"inlineStr\"><is><t>{Escape(value)}</t></is></c>");
                }
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    // A, B, ... Z, AA, AB. Thirty-six currencies plus the fixed columns runs past Z.
    private static string ColumnName(int index)
    {
        var name = "";
        for (var value = index + 1; value > 0; value = (value - 1) / 26)
        {
            name = (char)('A' + (value - 1) % 26) + name;
        }

        return name;
    }

    // Quoted fields with doubled quotes inside them, the way Field writes them
    private static List<string> ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        return fields;
    }

    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;");

    private static void AddEntry(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string ContentTypes() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
        "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private static string RootRels() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<Relationships xmlns=\"{PackageRels}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{OfficeRels}/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    private static string Workbook() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<workbook xmlns=\"{Main}\" xmlns:r=\"{OfficeRels}\">" +
        "<sheets><sheet name=\"Runs\" sheetId=\"1\" r:id=\"rId1\"/></sheets>" +
        "</workbook>";

    private static string WorkbookRels() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<Relationships xmlns=\"{PackageRels}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{OfficeRels}/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        $"<Relationship Id=\"rId2\" Type=\"{OfficeRels}/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    // Two of everything Excel insists on counting, and a bold font for the header. The
    // second fill is not used and is not optional: Excel rejects a styles part without it.
    private static string Styles() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<styleSheet xmlns=\"{Main}\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/></numFmts>" +
        "<fonts count=\"2\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "</fonts>" +
        "<fills count=\"3\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDCE6F1\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "</fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"5\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\"/>" +
        "</cellXfs>" +
        "</styleSheet>";
}
