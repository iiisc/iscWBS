using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;

namespace iscWBS.Core.Services;

/// <summary>
/// A project as an Excel workbook, with a sheet each for tasks, dependencies, milestones, risks and notes, and tasks
/// referred to by outline code. Import reads sheets and columns by position, so headings can be renamed but the
/// order has to stay as export wrote it. Every method takes the app's text for a resource key, for the headings
/// and the status and level names, which export writes in the app's language.
/// </summary>
public static partial class ProjectWorkbook
{
    private static readonly string[] _sheets = ["Tasks", "Dependencies", "Milestones", "Risks", "Notes"];

    private static readonly string[][] _columns =
    [
        ["Code", "Title", "Description", "AssignedTo", "Status", "EstimatedHours", "ActualHours", "Start", "Due", "Deliverable", "BaselineStart", "BaselineDue"],
        ["Predecessor", "PredecessorTitle", "Successor", "SuccessorTitle", "Type", "Delay"],
        ["Title", "Due", "Complete", "Tasks"],
        ["Title", "Description", "Likelihood", "Impact", "Severity", "Owner", "Response", "Closed"],
        ["Code", "TaskTitle", "Date", "Text"],
    ];

    /// <summary>Columns holding outline codes, per sheet; they're formatted as text so Excel doesn't turn 1.10 into a number.</summary>
    private static readonly int[][] _codeColumns = [[0], [0, 2], [3], [], [0]];

    private const uint BoldStyle = 1, DateStyle = 2, TextStyle = 3, DateTimeStyle = 4;

    public static void Write(ProjectContent content, string path, Func<string, string> label)
    {
        Dictionary<Guid, WbsNode> byId = content.Nodes.ToDictionary(n => n.Id);
        Dictionary<Guid, int> order = content.Nodes.Select((n, i) => (n.Id, i)).ToDictionary(p => p.Id, p => p.i);
        string Code(Guid id) => byId.TryGetValue(id, out WbsNode? n) ? n.Code : string.Empty;
        string Title(Guid id) => byId.TryGetValue(id, out WbsNode? n) ? n.Title : string.Empty;
        int Order(Guid id) => order.GetValueOrDefault(id, int.MaxValue);
        ILookup<Guid, Guid> links = content.MilestoneLinks.ToLookup(l => l.MilestoneId, l => l.NodeId);

        IEnumerable<object?[]>[] rows =
        [
            content.Nodes.Select(n => new object?[]
            {
                n.Code, n.Title, n.Description, n.AssignedTo, label($"Status_{n.Status}"), n.EstimatedHours, n.ActualHours,
                n.StartDate, n.DueDate, n.IsDeliverable, n.BaselineStart, n.BaselineDue
            }),
            content.Dependencies
                .OrderBy(d => Order(d.SuccessorId)).ThenBy(d => Order(d.PredecessorId))
                .Select(d => new object?[]
                {
                    Code(d.PredecessorId), Title(d.PredecessorId), Code(d.SuccessorId), Title(d.SuccessorId),
                    DependencyConstraints.GetAbbreviation(d.Type), d.LagDays
                }),
            content.Milestones.Select(m => new object?[]
            {
                m.Title, m.DueDate, m.IsComplete, string.Join(", ", links[m.Id].OrderBy(Order).Select(Code))
            }),
            content.Risks.Select(r => new object?[]
            {
                r.Title, r.Description, label($"RiskLevel_{r.Likelihood}"), label($"RiskLevel_{r.Impact}"),
                label($"RiskLevel_{r.Severity}"), r.Owner, r.Response, r.IsClosed
            }),
            content.Notes
                .OrderBy(n => Order(n.NodeId)).ThenBy(n => n.CreatedAt)
                .Select(n => new object?[] { Code(n.NodeId), Title(n.NodeId), n.CreatedAt, n.Text }),
        ];

        using SpreadsheetDocument document = SpreadsheetDocument.Create(path, SpreadsheetDocumentType.Workbook);
        WorkbookPart workbook = document.AddWorkbookPart();
        var sheets = new Sheets();
        workbook.Workbook = new Workbook(sheets);
        workbook.AddNewPart<WorkbookStylesPart>().Stylesheet = Styles();
        for (int i = 0; i < _sheets.Length; i++)
        {
            string[] headings = _columns[i].Select(c => label($"Workbook_{_sheets[i]}_{c}")).ToArray();
            AddSheet(workbook, sheets, (uint)i + 1, label($"Workbook_{_sheets[i]}"), headings, rows[i].ToList(), _codeColumns[i]);
        }
    }

    private static void AddSheet(WorkbookPart workbook, Sheets sheets, uint sheetId, string name, string[] headings, List<object?[]> rows, int[] codeColumns)
    {
        WorksheetPart part = workbook.AddNewPart<WorksheetPart>();
        var data = new SheetData();
        data.Append(new Row(headings.Select((h, c) => TextCell(Reference(c, 1), h, BoldStyle))) { RowIndex = 1 });
        for (int r = 0; r < rows.Count; r++)
        {
            uint rowIndex = (uint)r + 2;
            data.Append(new Row(rows[r].Select((value, c) => Cell(Reference(c, rowIndex), value, codeColumns.Contains(c)))) { RowIndex = rowIndex });
        }

        var columns = new Columns(headings.Select((h, c) =>
        {
            int longest = rows.Select(row => row[c] switch { string s => s.Length, DateTime => 16, null => 0, _ => 8 }).DefaultIfEmpty().Max();
            var column = new Column { Min = (uint)c + 1, Max = (uint)c + 1, Width = Math.Clamp(Math.Max(longest, h.Length) + 2, 8, 60), CustomWidth = true };
            if (codeColumns.Contains(c))
                column.Style = TextStyle;
            return column;
        }));
        // The headings stay in view while scrolling.
        var views = new SheetViews(new SheetView(new Pane
        {
            VerticalSplit = 1,
            TopLeftCell = "A2",
            ActivePane = PaneValues.BottomLeft,
            State = PaneStateValues.Frozen
        }) { WorkbookViewId = 0 });
        part.Worksheet = new Worksheet(views, columns, data);
        sheets.Append(new Sheet { Id = workbook.GetIdOfPart(part), SheetId = sheetId, Name = SheetName(name) });
    }

    private static Cell Cell(string reference, object? value, bool isCode) => value switch
    {
        null or "" => new Cell { CellReference = reference },
        string text => TextCell(reference, text, isCode ? TextStyle : null),
        DateTime date => new Cell { CellReference = reference, CellValue = new CellValue(date.ToOADate()), StyleIndex = date.TimeOfDay == TimeSpan.Zero ? DateStyle : DateTimeStyle },
        bool flag => new Cell { CellReference = reference, CellValue = new CellValue(flag ? "1" : "0"), DataType = CellValues.Boolean },
        int number => new Cell { CellReference = reference, CellValue = new CellValue(number) },
        double number => new Cell { CellReference = reference, CellValue = new CellValue(number) },
        _ => TextCell(reference, Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, null),
    };

    private static Cell TextCell(string reference, string text, uint? style) => new()
    {
        CellReference = reference,
        DataType = CellValues.InlineString,
        InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }),
        StyleIndex = style
    };

    private static string Reference(int column, uint row) => $"{(char)('A' + column)}{row}";

    /// <summary>Excel's rules: at most 31 characters, none of []:*?/\.</summary>
    private static string SheetName(string name) => new string(name.Where(c => "[]:*?/\\".IndexOf(c) < 0).Take(31).ToArray());

    private static Stylesheet Styles()
    {
        static Font Font(bool bold) => bold
            ? new Font(new Bold(), new FontSize { Val = 11 }, new FontName { Val = "Calibri" })
            : new Font(new FontSize { Val = 11 }, new FontName { Val = "Calibri" });
        // The number formats are Excel's built-in ones, which follow the reader's regional settings:
        // 14 a short date, 49 text, 22 a date and time.
        return new Stylesheet(
            new Fonts(Font(bold: false), Font(bold: true)),
            new Fills(new Fill(new PatternFill { PatternType = PatternValues.None }), new Fill(new PatternFill { PatternType = PatternValues.Gray125 })),
            new Borders(new Border()),
            new CellStyleFormats(new CellFormat()),
            new CellFormats(
                new CellFormat(),
                new CellFormat { FontId = 1, ApplyFont = true },
                new CellFormat { NumberFormatId = 14, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 49, ApplyNumberFormat = true },
                new CellFormat { NumberFormatId = 22, ApplyNumberFormat = true }));
    }

    /// <summary>The workbook's content, checked: every task code needs its parent, and every reference to a task needs the task.</summary>
    public static ProjectContent Read(string path, Func<string, string> label)
    {
        List<SheetRow>[] sheets;
        try
        {
            using SpreadsheetDocument document = SpreadsheetDocument.Open(path, isEditable: false);
            WorkbookPart workbook = document.WorkbookPart ?? throw new InvalidDataException("The file has no workbook.");
            List<string> sharedStrings = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(s => s.InnerText).ToList() ?? [];
            List<Sheet> sheetList = workbook.Workbook?.Sheets?.Elements<Sheet>().ToList() ?? [];
            sheets = _sheets.Select((_, i) => i < sheetList.Count ? ReadRows(workbook, sheetList[i], i, sharedStrings) : []).ToArray();
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or InvalidDataException or FileFormatException or System.Xml.XmlException)
        {
            throw new InvalidDataException("The file could not be read as an Excel workbook.", ex).WithMessageKey("Error_ImportNotWorkbook");
        }

        var reader = new Reader(label);
        var content = new ProjectContent();
        Dictionary<string, WbsNode> byCode = reader.ReadTasks(sheets[0], content.Nodes);
        reader.ReadDependencies(sheets[1], byCode, content.Dependencies);
        reader.ReadMilestones(sheets[2], byCode, content);
        reader.ReadRisks(sheets[3], content.Risks);
        reader.ReadNotes(sheets[4], byCode, content.Notes);
        return content;
    }

    private sealed record SheetRow(int Sheet, uint Number, object?[] Cells)
    {
        public object? this[int column] => column < Cells.Length ? Cells[column] : null;
    }

    /// <summary>Each row below the headings as text, numbers and booleans by column; blank rows are left out.</summary>
    private static List<SheetRow> ReadRows(WorkbookPart workbook, Sheet sheet, int sheetIndex, List<string> sharedStrings)
    {
        if (sheet.Id?.Value is not { } id || workbook.GetPartById(id) is not WorksheetPart part)
            return [];
        var rows = new List<SheetRow>();
        uint number = 0;
        foreach (Row row in part.Worksheet?.Descendants<Row>() ?? [])
        {
            number = row.RowIndex?.Value ?? number + 1;
            if (number == 1)
                continue;
            var cells = new List<object?>();
            foreach (Cell cell in row.Elements<Cell>())
            {
                int column = cell.CellReference?.Value is { } reference ? ColumnIndex(reference) : cells.Count;
                while (cells.Count <= column)
                    cells.Add(null);
                cells[column] = Value(cell, sharedStrings);
            }
            if (cells.Any(c => c is not null))
                rows.Add(new SheetRow(sheetIndex, number, [.. cells]));
        }
        return rows;
    }

    private static int ColumnIndex(string reference)
    {
        int index = 0;
        foreach (char c in reference.TakeWhile(char.IsLetter))
            index = index * 26 + (char.ToUpperInvariant(c) - 'A' + 1);
        return index - 1;
    }

    private static object? Value(Cell cell, List<string> sharedStrings)
    {
        string? raw = cell.CellValue?.Text;
        CellValues? type = cell.DataType?.Value;
        object? value;
        if (type == CellValues.SharedString)
            value = int.TryParse(raw, CultureInfo.InvariantCulture, out int i) && i < sharedStrings.Count ? sharedStrings[i] : null;
        else if (type == CellValues.InlineString)
            value = cell.InlineString?.InnerText;
        else if (type == CellValues.Boolean)
            value = raw is "1" || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        else if (type == CellValues.Error)
            value = null;
        else if (type is null || type == CellValues.Number)
            value = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double n) ? n : raw;
        else
            value = raw;
        return value is string text ? (string.IsNullOrWhiteSpace(text) ? null : text.Trim()) : value;
    }

    [GeneratedRegex(@"^\d+(\.\d+)*$")]
    private static partial Regex OutlineCode();

    private sealed class Reader(Func<string, string> label)
    {
        private readonly Dictionary<string, WbsStatus> _statuses = Names<WbsStatus>(s => label($"Status_{s}"));
        private readonly Dictionary<string, RiskLevel> _levels = Names<RiskLevel>(l => label($"RiskLevel_{l}"));
        private readonly Dictionary<string, DependencyType> _types = Names<DependencyType>(DependencyConstraints.GetAbbreviation);

        public Dictionary<string, WbsNode> ReadTasks(List<SheetRow> rows, List<WbsNode> nodes)
        {
            var byCode = new Dictionary<string, WbsNode>();
            var rowOf = new Dictionary<WbsNode, SheetRow>();
            foreach (SheetRow row in rows)
            {
                string code = Code(row, 0);
                if (!OutlineCode().IsMatch(code))
                    throw Error("Error_ImportCode", row, code);
                var node = new WbsNode
                {
                    Code = code,
                    Title = Text(row, 1),
                    Description = Text(row, 2),
                    AssignedTo = Text(row, 3),
                    Status = Choice(row, 4, _statuses) ?? WbsStatus.NotStarted,
                    EstimatedHours = Number(row, 5) ?? 0,
                    ActualHours = Number(row, 6) ?? 0,
                    StartDate = Date(row, 7)?.Date,
                    DueDate = Date(row, 8)?.Date,
                    IsDeliverable = Flag(row, 9),
                    BaselineStart = Date(row, 10)?.Date,
                    BaselineDue = Date(row, 11)?.Date,
                    // A code's last part places it among its siblings, so a row added at the bottom still lands where its code says.
                    SortOrder = int.TryParse(code[(code.LastIndexOf('.') + 1)..], CultureInfo.InvariantCulture, out int position) ? position : int.MaxValue
                };
                if (!byCode.TryAdd(code, node))
                    throw Error("Error_ImportDuplicateCode", row, code);
                rowOf[node] = row;
                nodes.Add(node);
            }
            foreach (WbsNode node in nodes)
            {
                int dot = node.Code.LastIndexOf('.');
                if (dot < 0)
                    continue;
                string parentCode = node.Code[..dot];
                node.ParentId = byCode.TryGetValue(parentCode, out WbsNode? parent)
                    ? parent.Id
                    : throw Error("Error_ImportNoParent", rowOf[node], node.Code, parentCode);
            }
            return byCode;
        }

        public void ReadDependencies(List<SheetRow> rows, Dictionary<string, WbsNode> byCode, List<NodeDependency> dependencies)
        {
            var successorsOf = new Dictionary<Guid, List<Guid>>();
            foreach (SheetRow row in rows)
            {
                var dependency = new NodeDependency
                {
                    PredecessorId = Task(row, 0, byCode).Id,
                    SuccessorId = Task(row, 2, byCode).Id,
                    Type = Choice(row, 4, _types) ?? DependencyType.FinishToStart,
                    LagDays = (int)Math.Round(Number(row, 5) ?? 0)
                };
                if (!successorsOf.TryGetValue(dependency.PredecessorId, out List<Guid>? successors))
                    successorsOf[dependency.PredecessorId] = successors = [];
                if (successors.Contains(dependency.SuccessorId))
                    continue;
                if (WbsService.WouldCreateCycle(dependency.PredecessorId, dependency.SuccessorId, id => successorsOf.GetValueOrDefault(id) ?? []))
                    throw Error("Error_ImportCircular", row);
                successors.Add(dependency.SuccessorId);
                dependencies.Add(dependency);
            }
        }

        public void ReadMilestones(List<SheetRow> rows, Dictionary<string, WbsNode> byCode, ProjectContent content)
        {
            foreach (SheetRow row in rows)
            {
                var milestone = new Milestone
                {
                    Title = Text(row, 0) is { Length: > 0 } title ? title : throw Error("Error_ImportMissing", row, Heading(row, 0)),
                    DueDate = Date(row, 1)?.Date ?? throw Error("Error_ImportMissing", row, Heading(row, 1)),
                    IsComplete = Flag(row, 2)
                };
                content.Milestones.Add(milestone);
                foreach (string code in Text(row, 3).Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Distinct())
                {
                    WbsNode node = byCode.TryGetValue(code, out WbsNode? found) ? found : throw Error("Error_ImportUnknownTask", row, code);
                    content.MilestoneLinks.Add((milestone.Id, node.Id));
                }
            }
        }

        public void ReadRisks(List<SheetRow> rows, List<Risk> risks)
        {
            DateTime now = DateTime.Now;
            foreach (SheetRow row in rows)
            {
                risks.Add(new Risk
                {
                    Title = Text(row, 0) is { Length: > 0 } title ? title : throw Error("Error_ImportMissing", row, Heading(row, 0)),
                    Description = Text(row, 1),
                    Likelihood = Choice(row, 2, _levels) ?? RiskLevel.Medium,
                    Impact = Choice(row, 3, _levels) ?? RiskLevel.Medium,
                    Owner = Text(row, 5),
                    Response = Text(row, 6),
                    IsClosed = Flag(row, 7),
                    // Keeps the workbook's order among risks rated the same.
                    CreatedAt = now.AddSeconds(risks.Count)
                });
            }
        }

        public void ReadNotes(List<SheetRow> rows, Dictionary<string, WbsNode> byCode, List<NodeNote> notes)
        {
            foreach (SheetRow row in rows)
            {
                if (Text(row, 3) is not { Length: > 0 } text)
                    continue;
                notes.Add(new NodeNote { NodeId = Task(row, 0, byCode).Id, CreatedAt = Date(row, 2) ?? DateTime.Now, Text = text });
            }
        }

        private static string Text(SheetRow row, int column) => row[column] switch
        {
            null => string.Empty,
            string text => text,
            double number => number.ToString(CultureInfo.CurrentCulture),
            var value => Convert.ToString(value, CultureInfo.CurrentCulture) ?? string.Empty
        };

        /// <summary>An outline code; Excel stores one typed into a cell not formatted as text as a number.</summary>
        private static string Code(SheetRow row, int column) => row[column] switch
        {
            double number => number.ToString("0.##########", CultureInfo.InvariantCulture),
            _ => Text(row, column)
        };

        private WbsNode Task(SheetRow row, int column, Dictionary<string, WbsNode> byCode)
        {
            string code = Code(row, column);
            return byCode.TryGetValue(code, out WbsNode? node) ? node : throw Error("Error_ImportUnknownTask", row, code);
        }

        private double? Number(SheetRow row, int column) => row[column] switch
        {
            null => null,
            double number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out double n)
                          || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out n) => n,
            _ => throw Error("Error_ImportValue", row, Heading(row, column), Text(row, column))
        };

        private DateTime? Date(SheetRow row, int column) => row[column] switch
        {
            null => null,
            double number when number is > -657435 and < 2958466 => DateTime.FromOADate(number),
            string text when DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out DateTime d)
                          || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out d) => d,
            _ => throw Error("Error_ImportValue", row, Heading(row, column), Text(row, column))
        };

        private bool Flag(SheetRow row, int column) => row[column] switch
        {
            null => false,
            bool flag => flag,
            double number => number != 0,
            string text when text.ToLowerInvariant() is "x" or "1" or "true" => true,
            string text when text.ToLowerInvariant() is "0" or "false" => false,
            _ => throw Error("Error_ImportValue", row, Heading(row, column), Text(row, column))
        };

        /// <summary>The value named in the cell; null when it's empty.</summary>
        private T? Choice<T>(SheetRow row, int column, Dictionary<string, T> names) where T : struct, Enum
        {
            if (row[column] is null)
                return null;
            string text = Text(row, column);
            return names.TryGetValue(text, out T value) ? value : throw Error("Error_ImportValue", row, Heading(row, column), text);
        }

        /// <summary>Each value by its name, by <paramref name="name"/> (its name in the app's language, or FS for a dependency type) and by its number.</summary>
        private static Dictionary<string, T> Names<T>(Func<T, string> name) where T : struct, Enum
        {
            var names = new Dictionary<string, T>(StringComparer.CurrentCultureIgnoreCase);
            foreach (T value in Enum.GetValues<T>())
            {
                names.TryAdd(value.ToString(), value);
                names.TryAdd(name(value), value);
                names.TryAdd(Convert.ToInt32(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture), value);
            }
            return names;
        }

        private string Heading(SheetRow row, int column) => label($"Workbook_{_sheets[row.Sheet]}_{_columns[row.Sheet][column]}");

        private WbsException Error(string key, SheetRow row, params object[] details)
            => new WbsException($"{_sheets[row.Sheet]}, row {row.Number}: {key}")
                .WithMessageKey(key, [label($"Workbook_{_sheets[row.Sheet]}"), row.Number, .. details]);
    }
}
