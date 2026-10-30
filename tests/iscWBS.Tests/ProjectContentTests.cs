using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using iscWBS.Core.Data;
using iscWBS.Core.Exceptions;
using iscWBS.Core.Models;
using iscWBS.Core.Services;

namespace iscWBS.Tests;

/// <summary>Copying branches, starting a project from another one, and the Excel workbook, against real files.</summary>
public sealed class ProjectContentTests : IAsyncLifetime
{
    private readonly TempDirectory _dir = new();
    private readonly ProjectStateService _state;
    private readonly WbsService _wbs;
    private readonly MilestoneService _milestones;
    private readonly RiskService _risks;
    private Guid _projectId;

    // The resource keys stand in for the app's text.
    private static string Label(string key) => key;

    public ProjectContentTests()
    {
        _state = new ProjectStateService(new SettingsService(_dir.File("settings.json")));
        _wbs = new WbsService(_state);
        _milestones = new MilestoneService(_state);
        _risks = new RiskService(_state);
    }

    public async ValueTask InitializeAsync()
    {
        await _state.CreateProjectAsync("Test", _dir.File("test.iscwbs"));
        _projectId = _state.ActiveProject!.Id;
    }

    public async ValueTask DisposeAsync()
    {
        await _state.CloseProjectAsync();
        _dir.Dispose();
    }

    private async Task<string[]> Outline() => (await _wbs.GetAllByProjectAsync(_state.ActiveProject!.Id)).Select(n => $"{n.Code} {n.Title}").ToArray();

    private async Task<WbsNode> Update(WbsNode node, Action<WbsNode> change)
    {
        change(node);
        await _wbs.UpdateNodeAsync(node);
        return node;
    }

    /// <summary>Root with A (A1 → A2 by a 2-day delay, A1 in progress) and B; a milestone on A1, a note, and one open and one closed risk.</summary>
    private async Task<(WbsNode A, WbsNode A1, WbsNode A2)> ArrangeProject()
    {
        WbsNode root = await _wbs.AddRootNodeAsync(_projectId, "Root");
        WbsNode a = await _wbs.AddChildNodeAsync(root.Id, "A");
        WbsNode a1 = await Update(await _wbs.AddChildNodeAsync(a.Id, "A1"), n =>
        {
            n.Status = WbsStatus.InProgress;
            n.AssignedTo = "Kim";
            n.EstimatedHours = 10;
            n.ActualHours = 4;
            n.StartDate = new DateTime(2026, 3, 2);
            n.DueDate = new DateTime(2026, 3, 6);
            n.IsDeliverable = true;
        });
        WbsNode a2 = await Update(await _wbs.AddChildNodeAsync(a.Id, "A2"), n =>
        {
            n.StartDate = new DateTime(2026, 3, 8);
            n.DueDate = new DateTime(2026, 3, 10);
        });
        await _wbs.AddChildNodeAsync(root.Id, "B");
        await _wbs.AddDependencyAsync(new NodeDependency { PredecessorId = a1.Id, SuccessorId = a2.Id, Type = DependencyType.FinishToStart, LagDays = 2 });
        await _wbs.SetBaselineAsync(_projectId);
        await _wbs.AddNoteAsync(a1.Id, "Started early");
        Milestone milestone = await _milestones.CreateAsync(_projectId, "Review", new DateTime(2026, 3, 12));
        await _milestones.LinkNodeAsync(milestone.Id, a1.Id);
        Risk open = await _risks.CreateAsync(_projectId, "Supplier late");
        open.Likelihood = RiskLevel.High;
        open.Owner = "Sam";
        await _risks.UpdateAsync(open);
        Risk closed = await _risks.CreateAsync(_projectId, "Budget cut");
        closed.IsClosed = true;
        await _risks.UpdateAsync(closed);
        return (a, a1, a2);
    }

    [Fact]
    public async Task CopyingABranch_PutsANotStartedCopyAfterIt_WithTheDependenciesInsideIt()
    {
        (WbsNode a, _, _) = await ArrangeProject();

        WbsNode copy = await _wbs.DuplicateNodeAsync(a.Id);

        Assert.Equal(["1 Root", "1.1 A", "1.1.1 A1", "1.1.2 A2", "1.2 A", "1.2.1 A1", "1.2.2 A2", "1.3 B"], await Outline());
        Assert.Equal("1.2", copy.Code);
        IReadOnlyList<WbsNode> nodes = await _wbs.GetAllByProjectAsync(_projectId);
        WbsNode copiedA1 = nodes.Single(n => n.Code == "1.2.1");
        Assert.Equal((WbsStatus.NotStarted, 0.0, 10.0, null, "Kim", true),
            (copiedA1.Status, copiedA1.ActualHours, copiedA1.EstimatedHours, copiedA1.BaselineDue, copiedA1.AssignedTo, copiedA1.IsDeliverable));
        NodeDependency copiedDependency = (await _wbs.GetAllDependenciesByProjectAsync(_projectId)).Single(d => d.PredecessorId == copiedA1.Id);
        Assert.Equal((nodes.Single(n => n.Code == "1.2.2").Id, 2), (copiedDependency.SuccessorId, copiedDependency.LagDays));
        Assert.Empty(await _wbs.GetNotesAsync(copiedA1.Id));
        Assert.Empty(await _milestones.GetByNodeAsync(copiedA1.Id));
    }

    [Fact]
    public async Task ANewProjectFromAnotherOne_GetsItsPlan_NotStarted_AndMovedToStartOnTheGivenDay()
    {
        await ArrangeProject();
        ProjectContent template = (await ProjectStateService.ReadContentAsync(_dir.File("test.iscwbs"))).AsTemplate(new DateTime(2026, 9, 7));

        await _state.CreateProjectAsync("Copy", _dir.File("copy.iscwbs"), content: template);

        Guid copyId = _state.ActiveProject!.Id;
        Assert.NotEqual(_projectId, copyId);
        Assert.Equal(["1 Root", "1.1 A", "1.1.1 A1", "1.1.2 A2", "1.2 B"], await Outline());
        WbsNode a1 = (await _wbs.GetAllByProjectAsync(copyId)).Single(n => n.Title == "A1");
        Assert.Equal((WbsStatus.NotStarted, 0.0, new DateTime(2026, 9, 7), new DateTime(2026, 9, 11), null),
            (a1.Status, a1.ActualHours, a1.StartDate, a1.DueDate, a1.BaselineStart));
        Assert.Equal(2, (await _wbs.GetAllDependenciesByProjectAsync(copyId)).Single().LagDays);
        Milestone milestone = (await _milestones.GetByProjectAsync(copyId)).Single();
        Assert.Equal(new DateTime(2026, 9, 17), milestone.DueDate);
        Assert.Equal([a1.Id], await _milestones.GetLinkedNodeIdsAsync(milestone.Id));
        Assert.Empty(await _wbs.GetNotesAsync(a1.Id));
        Assert.Equal(["Supplier late"], (await _risks.GetByProjectAsync(copyId)).Select(r => r.Title));
    }

    [Fact]
    public async Task AnExportedWorkbook_ImportsAsTheSameProject()
    {
        await ArrangeProject();
        ProjectContent original = await ProjectContent.ReadAsync(_state.Connection, _projectId);
        string path = _dir.File("export.xlsx");
        ProjectWorkbook.Write(original, path, Label);

        await _state.CreateProjectAsync("Imported", _dir.File("imported.iscwbs"), content: ProjectWorkbook.Read(path, Label));

        ProjectContent imported = await ProjectContent.ReadAsync(_state.Connection, _state.ActiveProject!.Id);
        static string Describe(WbsNode n) => $"{n.Code} {n.Title} {n.Status} {n.AssignedTo} {n.EstimatedHours} {n.ActualHours} {n.StartDate:d} {n.DueDate:d} {n.IsDeliverable} {n.BaselineDue:d}";
        Assert.Equal(original.Nodes.Select(Describe), imported.Nodes.Select(Describe));
        string Code(ProjectContent c, Guid id) => c.Nodes.Single(n => n.Id == id).Code;
        Assert.Equal(original.Dependencies.Select(d => (Code(original, d.PredecessorId), Code(original, d.SuccessorId), d.Type, d.LagDays)),
            imported.Dependencies.Select(d => (Code(imported, d.PredecessorId), Code(imported, d.SuccessorId), d.Type, d.LagDays)));
        Assert.Equal(original.Milestones.Select(m => (m.Title, m.DueDate, m.IsComplete)), imported.Milestones.Select(m => (m.Title, m.DueDate, m.IsComplete)));
        Assert.Equal(original.MilestoneLinks.Select(l => Code(original, l.NodeId)), imported.MilestoneLinks.Select(l => Code(imported, l.NodeId)));
        Assert.Equal(original.Risks.Select(r => (r.Title, r.Likelihood, r.Impact, r.Owner, r.IsClosed)),
            imported.Risks.Select(r => (r.Title, r.Likelihood, r.Impact, r.Owner, r.IsClosed)));
        Assert.Equal(original.Notes.Select(n => (Code(original, n.NodeId), n.Text, n.CreatedAt.ToString("g"))),
            imported.Notes.Select(n => (Code(imported, n.NodeId), n.Text, n.CreatedAt.ToString("g"))));
    }

    [Fact]
    public async Task ImportingAnEditedWorkbook_PlacesNewRowsByCode_AndReadsTypedValues()
    {
        await ArrangeProject();
        string path = _dir.File("edited.xlsx");
        ProjectWorkbook.Write(await ProjectContent.ReadAsync(_state.Connection, _projectId), path, Label);
        // Typed into Excel at the bottom of the sheet, the way people add rows: a number for the code, an English status.
        Edit(path, 0, sheet =>
        {
            SetCell(sheet, "A7", 1.3);
            SetCell(sheet, "B7", "C");
            SetCell(sheet, "E7", "InProgress");
            SetCell(sheet, "A8", "1.1.0");
            SetCell(sheet, "B8", "A0");
        });

        ProjectContent content = ProjectWorkbook.Read(path, Label);

        await _state.CreateProjectAsync("Edited", _dir.File("edited.iscwbs"), content: content);
        Assert.Equal(["1 Root", "1.1 A", "1.1.1 A0", "1.1.2 A1", "1.1.3 A2", "1.2 B", "1.3 C"], await Outline());
        Assert.Equal(WbsStatus.InProgress, content.Nodes.Single(n => n.Title == "C").Status);
    }

    [Theory]
    [InlineData("A7", "2.1", "Error_ImportNoParent")]
    [InlineData("A7", "1.x", "Error_ImportCode")]
    [InlineData("A7", "1.1", "Error_ImportDuplicateCode")]
    [InlineData("I3", "next week", "Error_ImportValue")]
    [InlineData("E3", "Sort of done", "Error_ImportValue")]
    public async Task ImportingAWorkbookWithAMistake_SaysWhereItIs(string cell, string value, string messageKey)
    {
        await ArrangeProject();
        string path = _dir.File("mistake.xlsx");
        ProjectWorkbook.Write(await ProjectContent.ReadAsync(_state.Connection, _projectId), path, Label);
        Edit(path, 0, sheet =>
        {
            SetCell(sheet, cell, value);
            if (cell == "A7")
                SetCell(sheet, "B7", "Extra");
        });

        Exception ex = Assert.ThrowsAny<Exception>(() => ProjectWorkbook.Read(path, Label));

        Assert.True(ex.TryGetMessageKey(out string key, out object[] args));
        Assert.Equal((messageKey, "Workbook_Tasks"), (key, args[0]));
        Assert.Equal(cell[1..], args[1].ToString());
    }

    [Fact]
    public void AFileThatIsNotAWorkbook_IsRefused()
    {
        string path = _dir.File("not.xlsx");
        File.WriteAllText(path, "hello");

        Exception ex = Assert.ThrowsAny<Exception>(() => ProjectWorkbook.Read(path, Label));

        Assert.True(ex.TryGetMessageKey(out string key, out _));
        Assert.Equal("Error_ImportNotWorkbook", key);
    }

    [Fact]
    public async Task RisksAreListed_OpenOnesFirst_TheMostSeriousAtTheTop()
    {
        foreach ((string title, RiskLevel likelihood, RiskLevel impact, bool closed) in new[]
        {
            ("Low", RiskLevel.Low, RiskLevel.Low, false),
            ("Closed high", RiskLevel.High, RiskLevel.High, true),
            ("High", RiskLevel.High, RiskLevel.Medium, false),
            ("Medium", RiskLevel.Medium, RiskLevel.Medium, false),
        })
        {
            Risk risk = await _risks.CreateAsync(_projectId, title);
            (risk.Likelihood, risk.Impact, risk.IsClosed) = (likelihood, impact, closed);
            await _risks.UpdateAsync(risk);
        }

        IReadOnlyList<Risk> risks = await _risks.GetByProjectAsync(_projectId);

        Assert.Equal(["High", "Medium", "Low", "Closed high"], risks.Select(r => r.Title));
        Assert.Equal([RiskLevel.High, RiskLevel.Medium, RiskLevel.Low, RiskLevel.High], risks.Select(r => r.Severity));
    }

    private static void Edit(string path, int sheetIndex, Action<SheetData> change)
    {
        using SpreadsheetDocument document = SpreadsheetDocument.Open(path, isEditable: true);
        WorkbookPart workbook = document.WorkbookPart!;
        Sheet sheet = workbook.Workbook!.Sheets!.Elements<Sheet>().ElementAt(sheetIndex);
        change(((WorksheetPart)workbook.GetPartById(sheet.Id!)).Worksheet!.GetFirstChild<SheetData>()!);
    }

    /// <summary>Sets a cell the way Excel stores typing: numbers as numbers, anything else as text.</summary>
    private static void SetCell(SheetData sheet, string reference, object value)
    {
        uint rowIndex = uint.Parse(new string(reference.Where(char.IsDigit).ToArray()));
        Row row = sheet.Elements<Row>().FirstOrDefault(r => r.RowIndex?.Value == rowIndex) ?? sheet.AppendChild(new Row { RowIndex = rowIndex });
        Cell? cell = row.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == reference);
        // Only ever adds cells to the right of the row's last one, which keeps them in column order.
        cell ??= row.AppendChild(new Cell { CellReference = reference });
        cell.RemoveAllChildren();
        cell.StyleIndex = null;
        if (value is double number)
        {
            cell.DataType = null;
            cell.CellValue = new CellValue(number);
        }
        else
        {
            cell.DataType = CellValues.InlineString;
            cell.InlineString = new InlineString(new Text((string)value));
        }
    }
}
