using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ClosedXML.Excel;

namespace GolfStatsApp.Server.Services
{
    public static class ExportService
    {
        public const string CsvContentType = "text/csv";
        public const string XlsxContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        public static byte[] ToCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", headers.Select(Escape)));
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", row.Select(v => Escape(Format(v)))));
            return new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
        }

        public static byte[] ToXlsx(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add(sheetName);
            for (var c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
            }

            var r = 2;
            foreach (var row in rows)
            {
                for (var c = 0; c < row.Count; c++)
                {
                    var cell = ws.Cell(r, c + 1);
                    switch (row[c])
                    {
                        case null: break;
                        case double d: cell.Value = d; break;
                        case int i: cell.Value = i; break;
                        default: cell.Value = row[c]!.ToString(); break;
                    }
                }
                r++;
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents();
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static string Format(object? v) => v switch
        {
            null => string.Empty,
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => v.ToString() ?? string.Empty
        };

        // Prefixing formula-trigger characters prevents CSV/Excel formula injection.
        public static string Escape(string value)
        {
            if (value.Length > 0 && "=+-@\t\r".Contains(value[0]) && !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                value = "'" + value;
            if (value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0)
                value = "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }
    }
}

