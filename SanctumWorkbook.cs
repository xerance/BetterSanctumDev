using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace BetterSanctumDev;

// A spreadsheet built from the wide run file, for reading rather than for recording.
//
// The CSV stays the record. It is appended a line at a time, so a crash costs the line
// being written; a workbook has to be rewritten whole every time, so the same crash would
// cost every run in it. This is generated from the CSV on demand instead, which means it
// can be thrown away and rebuilt and nothing depends on it.
//
// No library. An xlsx is a zip of XML documents and System.IO.Compression is in the
// framework, so the alternative to writing them out by hand was a package the HUD cannot
// restore. Strings are written inline rather than through a shared string table, which is
// larger on disk and very much smaller in code.
//
// Two sheets. Runs holds a row per run and per deal; Prices holds what each currency was
// worth when the workbook was built. The totals are formulas against Prices rather than
// numbers baked in, so correcting a price re-totals every run in the file - which is the
// whole reason the prices are a sheet and not a constant.
public static class SanctumWorkbook
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string PackageRels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRels = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    // A spreadsheet counts days from the last day of 1899, and is one out on purpose - it
    // carries a leap day in 1900 that never happened, for compatibility with a spreadsheet
    // older than most of the people using this one.
    private static readonly DateTime ExcelEpoch = new DateTime(1899, 12, 30);

    // The columns of the wide file that are not currencies. Everything else in it is, and
    // that is what the total is a total of.
    private static readonly HashSet<string> FixedColumns =
        new(new[] { "date", "run", "source", "duration" }, StringComparer.OrdinalIgnoreCase);

    private const int StyleDefault = 0;
    private const int StyleHeader = 1;
    private const int StyleDate = 2;
    private const int StyleBand = 3;
    private const int StyleDateBand = 4;
    private const int StyleTotalRun = 5;
    private const int StyleTotalDeal = 6;
    private const int StylePrice = 7;
    private const int StyleTotalRunDivine = 8;
    private const int StyleTotalDealDivine = 9;

    // The heading Divine Orbs is written under, which is what the divine total divides by.
    private static readonly string DivineColumn = CurrencyNames.ToShort("Divine Orbs");

    // Everything is one column right of where it would naturally start, leaving A for the
    // league. A run's data reading from B is a small cost; a workbook that does not say
    // which economy its prices came from is a bigger one.
    private const int FirstDataColumn = 2;

    // Returns how many rows were written, or -1 with the reason in error. A count with an
    // error alongside it is a warning: the workbook was written, and something in it is not
    // what you would want.
    public static int Write(
        string csvPath,
        string xlsxPath,
        ISet<string> skipColumns,
        string league,
        IReadOnlyList<(string Currency, double Chaos)> prices,
        out string error)
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

            return Emit(rows, keep, xlsxPath, league, prices, ref error);
        }
        catch (Exception e)
        {
            error = e.Message;
            return -1;
        }
    }

    // The same workbook with nothing in it: headings, the price sheet, and a pair of rows
    // per run with only the run number and whether it is the run or its deals filled in.
    //
    // For recording runs by hand. The totals are the same formulas, so a quantity typed
    // into a currency column prices itself - which is most of what the workbook is for, and
    // none of it needs the HUD to have written the row.
    public static int WriteTemplate(
        string xlsxPath,
        IReadOnlyList<string> currencies,
        int runs,
        string league,
        IReadOnlyList<(string Currency, double Chaos)> prices,
        out string error)
    {
        error = null;
        try
        {
            var headers = new List<string> { "date", "run", "source", "duration" };
            headers.AddRange(currencies.Select(CurrencyNames.ToShort));

            var rows = new List<List<string>> { headers };
            for (var run = 1; run <= Math.Max(runs, 1); run++)
            {
                foreach (var source in new[] { "run", "deal" })
                {
                    var row = new List<string> { "", run.ToString(CultureInfo.InvariantCulture), source, "" };
                    row.AddRange(currencies.Select(_ => ""));
                    rows.Add(row);
                }
            }

            var keep = Enumerable.Range(0, headers.Count).ToList();
            return Emit(rows, keep, xlsxPath, league, prices, ref error);
        }
        catch (Exception e)
        {
            error = e.Message;
            return -1;
        }
    }

    private static int Emit(
        List<List<string>> rows,
        List<int> keep,
        string xlsxPath,
        string league,
        IReadOnlyList<(string Currency, double Chaos)> prices,
        ref string error)
    {
        {
            prices ??= new List<(string, double)>();
            var runs = BuildRuns(rows, keep, league);
            var priceSheet = BuildPrices(prices);

            // A heading with no row on Prices totals as nothing, and does it quietly - the
            // SUMIF simply matches no row. That happens when a file was written under a
            // heading that has since been renamed, and the totals then understate by
            // however much that currency was worth. Worth saying out loud.
            var priced = new HashSet<string>(
                prices.Select(x => CurrencyNames.ToShort(x.Currency)), StringComparer.OrdinalIgnoreCase);

            var unmatched = keep
                .Select(x => rows[0][x])
                .Where(x => !FixedColumns.Contains(x) && !priced.Contains(x))
                .ToList();

            if (unmatched.Count > 0)
            {
                error = "no price for " + string.Join(", ", unmatched) +
                        " - those columns count as nothing in the totals. Delete the CSV to start it again under the current headings.";
            }
            else if (!prices.Any(x => x.Chaos > 0 &&
                         string.Equals(CurrencyNames.ToShort(x.Currency), DivineColumn, StringComparison.OrdinalIgnoreCase)))
            {
                // The divine column divides by the price sheet's divine row. Without one
                // it reads empty rather than wrong, but empty wants explaining.
                error = "no divine price, so total value d is blank. The chaos totals are unaffected.";
            }

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
                AddEntry(zip, "xl/worksheets/sheet1.xml", runs);
                AddEntry(zip, "xl/worksheets/sheet2.xml", priceSheet);
            }

            File.Move(temp, xlsxPath, overwrite: true);
            return rows.Count - 1;
        }
    }

    private static string BuildRuns(List<List<string>> rows, List<int> keep, string league)
    {
        var headers = keep.Select(x => rows[0][x]).ToList();

        // Where the currencies sit, which is what the total sums and what the price lookup
        // matches on. Contiguous in practice, and taken as the span between the first and
        // last so a column added between them is included rather than silently dropped.
        var currencyIndexes = headers
            .Select((name, index) => (name, index))
            .Where(x => !FixedColumns.Contains(x.name))
            .Select(x => x.index)
            .ToList();

        var totalColumn = headers.Count + FirstDataColumn;
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append($"<worksheet xmlns=\"{Main}\">");

        // The header stays put while the runs scroll under it
        xml.Append("<sheetViews><sheetView tabSelected=\"1\" workbookViewId=\"0\">");
        xml.Append("<pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/>");
        xml.Append("</sheetView></sheetViews>");

        // One width for every currency, so the block reads as a block rather than as
        // columns of assorted widths. Sized to the longest heading among them, since the
        // quantities under it are shorter than any of the names.
        var currencyWidth = currencyIndexes.Count > 0
            ? Math.Clamp(currencyIndexes.Max(x => headers[x].Length) + 2, 7, 20)
            : 7;

        xml.Append("<cols>");
        xml.Append($"<col min=\"1\" max=\"1\" width=\"20\" customWidth=\"1\"/>");
        for (var column = 0; column < headers.Count; column++)
        {
            var width = currencyIndexes.Contains(column)
                ? currencyWidth
                : Math.Clamp(rows.Max(row => keep[column] < row.Count ? row[keep[column]].Length : 0) + 2, 6, 24);
            xml.Append($"<col min=\"{column + FirstDataColumn}\" max=\"{column + FirstDataColumn}\" width=\"{width}\" customWidth=\"1\"/>");
        }

        xml.Append($"<col min=\"{totalColumn}\" max=\"{totalColumn + 1}\" width=\"14\" customWidth=\"1\"/>");
        xml.Append("</cols><sheetData>");

        // Banded by run rather than by row, so a run's rows share a colour however many it
        // wrote - and a run that ever writes one row instead of two cannot shift the
        // banding of every run after it, which counting rows would.
        var runColumn = headers.FindIndex(x => string.Equals(x, "run", StringComparison.OrdinalIgnoreCase));
        var sourceColumn = headers.FindIndex(x => string.Equals(x, "source", StringComparison.OrdinalIgnoreCase));

        for (var row = 0; row < rows.Count; row++)
        {
            var cells = keep.Select(x => x < rows[row].Count ? rows[row][x] : "").ToList();
            var tinted = row > 0 &&
                         runColumn >= 0 &&
                         int.TryParse(cells[runColumn], out var runNumber) &&
                         runNumber % 2 == 1;

            xml.Append($"<row r=\"{row + 1}\">");

            // A1 says which economy the prices below came from. Nothing else lives in A.
            if (row == 0)
            {
                xml.Append(TextCell("A1", string.IsNullOrEmpty(league) ? "League: unknown" : $"League: {league}", StyleHeader));
            }

            for (var column = 0; column < headers.Count; column++)
            {
                var reference = ColumnName(column + FirstDataColumn) + (row + 1);
                var value = cells[column];

                if (row == 0)
                {
                    xml.Append(TextCell(reference, value, StyleHeader));
                    continue;
                }

                // An empty cell in a tinted row is still written, or the band breaks into
                // stripes wherever a run happened not to pay something.
                if (value.Length == 0)
                {
                    if (tinted)
                    {
                        xml.Append($"<c r=\"{reference}\" s=\"{StyleBand}\"/>");
                    }

                    continue;
                }

                // A date written as a date rather than as the text of one, so a chart can
                // put runs on a time axis instead of treating each day as its own category.
                if (string.Equals(headers[column], "date", StringComparison.OrdinalIgnoreCase) &&
                    DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    var serial = (date.Date - ExcelEpoch).TotalDays;
                    xml.Append($"<c r=\"{reference}\" s=\"{(tinted ? StyleDateBand : StyleDate)}\"><v>{Number(serial)}</v></c>");
                }
                else if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    var style = tinted ? $" s=\"{StyleBand}\"" : "";
                    xml.Append($"<c r=\"{reference}\"{style}><v>{Number(number)}</v></c>");
                }
                else
                {
                    xml.Append(TextCell(reference, value, tinted ? StyleBand : StyleDefault));
                }
            }

            // Two totals: chaos, and the same figure in divine. Coloured by run or deal,
            // since the two are not summed together and should not look as though they
            // could be, and shaded apart by which total they are.
            var chaosReference = ColumnName(totalColumn) + (row + 1);
            var divineReference = ColumnName(totalColumn + 1) + (row + 1);

            if (row == 0)
            {
                xml.Append(TextCell(chaosReference, "total value c", StyleHeader));
                xml.Append(TextCell(divineReference, "total value d", StyleHeader));
            }
            else if (currencyIndexes.Count > 0)
            {
                var isDeal = sourceColumn >= 0 &&
                             string.Equals(cells[sourceColumn], "deal", StringComparison.OrdinalIgnoreCase);

                xml.Append($"<c r=\"{chaosReference}\" s=\"{(isDeal ? StyleTotalDeal : StyleTotalRun)}\">" +
                           $"<f>{TotalFormula(currencyIndexes, row)}</f></c>");

                // Divided by whatever the price sheet says a divine is, rather than by a
                // rate fixed at export: correct the divine price and both totals follow.
                // IFERROR covers a price sheet with no divine on it, where the division
                // would otherwise fill the column with #DIV/0!.
                var divine = $"IFERROR({chaosReference}/SUMIF(Prices!$B:$B,&quot;{DivineColumn}&quot;,Prices!$C:$C),&quot;&quot;)";
                xml.Append($"<c r=\"{divineReference}\" s=\"{(isDeal ? StyleTotalDealDivine : StyleTotalRunDivine)}\">" +
                           $"<f>{divine}</f></c>");
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    // Each quantity multiplied by the price of the currency its heading names, summed.
    //
    // SUMIF against the heading row is what makes it survive the columns changing: it
    // looks each price up by name rather than by position, so a workbook built from a file
    // with different columns still totals correctly, and a currency missing from Prices
    // contributes nothing instead of shifting everything after it.
    private static string TotalFormula(List<int> currencyIndexes, int row)
    {
        var first = ColumnName(currencyIndexes.Min() + FirstDataColumn);
        var last = ColumnName(currencyIndexes.Max() + FirstDataColumn);
        var line = row + 1;

        return $"SUMPRODUCT(${first}{line}:${last}{line},SUMIF(Prices!$B:$B,${first}$1:${last}$1,Prices!$C:$C))";
    }

    private static string BuildPrices(IReadOnlyList<(string Currency, double Chaos)> prices)
    {
        var xml = new StringBuilder();
        xml.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        xml.Append($"<worksheet xmlns=\"{Main}\">");
        xml.Append("<cols>");
        xml.Append("<col min=\"1\" max=\"1\" width=\"20\" customWidth=\"1\"/>");
        xml.Append("<col min=\"2\" max=\"2\" width=\"16\" customWidth=\"1\"/>");
        xml.Append("<col min=\"3\" max=\"3\" width=\"13\" customWidth=\"1\"/>");
        xml.Append("</cols><sheetData>");

        xml.Append("<row r=\"3\">");
        xml.Append(TextCell("A3", "Currency pull date", StyleHeader));
        xml.Append("</row>");

        xml.Append("<row r=\"4\">");
        xml.Append($"<c r=\"A4\" s=\"{StyleDate}\"><v>{Number((DateTime.Now.Date - ExcelEpoch).TotalDays)}</v></c>");
        xml.Append("</row>");

        xml.Append("<row r=\"7\">");
        xml.Append(TextCell("B7", "currency", StyleHeader));
        xml.Append(TextCell("C7", "chaos value", StyleHeader));
        xml.Append("</row>");

        // The names here are the headings used over on Runs, because that is what the
        // total looks them up by.
        var line = 8;
        foreach (var (currency, chaos) in prices)
        {
            xml.Append($"<row r=\"{line}\">");
            xml.Append(TextCell($"B{line}", CurrencyNames.ToShort(currency), StyleDefault));
            xml.Append($"<c r=\"C{line}\" s=\"{StylePrice}\"><v>{Number(chaos)}</v></c>");
            xml.Append("</row>");
            line++;
        }

        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

    private static string TextCell(string reference, string text, int style) =>
        $"<c r=\"{reference}\" s=\"{style}\" t=\"inlineStr\"><is><t>{Escape(text)}</t></is></c>";

    private static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);

    // A, B, ... Z, AA, AB. One-based, so column 1 is A.
    private static string ColumnName(int index)
    {
        var name = "";
        for (var value = index; value > 0; value = (value - 1) / 26)
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
        "<Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
        "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
        "</Types>";

    private static string RootRels() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<Relationships xmlns=\"{PackageRels}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{OfficeRels}/officeDocument\" Target=\"xl/workbook.xml\"/>" +
        "</Relationships>";

    // calcPr forces a recalculation on open, so the totals are computed rather than blank:
    // the formulas are written without a cached result, having never been evaluated here.
    private static string Workbook() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<workbook xmlns=\"{Main}\" xmlns:r=\"{OfficeRels}\">" +
        "<sheets>" +
        "<sheet name=\"Runs\" sheetId=\"1\" r:id=\"rId1\"/>" +
        "<sheet name=\"Prices\" sheetId=\"2\" r:id=\"rId2\"/>" +
        "</sheets>" +
        "<calcPr calcId=\"0\" fullCalcOnLoad=\"1\"/>" +
        "</workbook>";

    private static string WorkbookRels() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<Relationships xmlns=\"{PackageRels}\">" +
        $"<Relationship Id=\"rId1\" Type=\"{OfficeRels}/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
        $"<Relationship Id=\"rId2\" Type=\"{OfficeRels}/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
        $"<Relationship Id=\"rId3\" Type=\"{OfficeRels}/styles\" Target=\"styles.xml\"/>" +
        "</Relationships>";

    // Two of everything Excel insists on counting. The second fill is not used and is not
    // optional: Excel rejects a styles part without gray125 sitting at index 1.
    private static string Styles() =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        $"<styleSheet xmlns=\"{Main}\">" +
        "<numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/></numFmts>" +
        "<fonts count=\"2\">" +
        "<font><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "<font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font>" +
        "</fonts>" +
        "<fills count=\"7\">" +
        "<fill><patternFill patternType=\"none\"/></fill>" +
        "<fill><patternFill patternType=\"gray125\"/></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFDCE6F1\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE2EFDA\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFFCE4D6\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFC6E0B4\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF8CBAD\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
        "</fills>" +
        "<borders count=\"1\"><border/></borders>" +
        "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
        "<cellXfs count=\"10\">" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
        "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"3\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"4\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/>" +
        "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"5\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\"/>" +
        "<xf numFmtId=\"2\" fontId=\"0\" fillId=\"6\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\" applyFill=\"1\"/>" +
        "</cellXfs>" +
        "</styleSheet>";
}
