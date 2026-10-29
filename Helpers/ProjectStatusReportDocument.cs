using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using iscWBS.Core.Models;

namespace iscWBS.Helpers;

/// <summary>QuestPDF <see cref="IDocument"/> for the executive project status report.</summary>
internal sealed class ProjectStatusReportDocument : IDocument
{
    private readonly ProjectStatusReportData _data;
    private readonly ReportOptions _options;

    private static readonly string _blue  = StatusPalette.Hex(WbsStatus.InProgress);
    private static readonly string _green = StatusPalette.Hex(WbsStatus.Complete);
    private static readonly string _red   = StatusPalette.Hex(WbsStatus.Blocked);
    private const string _amber      = "#C47D0E";
    private const string _headerBg   = "#F3F4F6";
    private const string _border     = "#E5E7EB";
    private const string _textDark   = "#111827";
    private const string _textMuted  = "#6B7280";
    private const string _sectionFg  = "#374151";

    public ProjectStatusReportDocument(ProjectStatusReportData data, ReportOptions options)
    {
        _data    = data;
        _options = options;
    }

    public DocumentMetadata GetMetadata() => new()
    {
        Title    = Strings.Format("Report_MetadataTitle", _data.ProjectName),
        Author   = "iscWBS",
        Subject  = Strings.Get("Report_Title"),
        CreationDate = _data.GeneratedAt
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

    private static void SectionTitle(ColumnDescriptor col, string key, string? color = null)
        => col.Item().Text(Strings.Get(key)).FontSize(9).Bold().FontColor(color ?? _sectionFg).LetterSpacing(0.05f);

    private static void HeaderRow(TableDescriptor t, float padding, params string[] keys)
        => HeaderRow(t, _headerBg, _sectionFg, padding, keys);

    private static void HeaderRow(TableDescriptor t, string background, string color, float padding, params string[] keys)
        => t.Header(h =>
        {
            foreach (string key in keys)
                h.Cell().Background(background).Padding(padding).Text(Strings.Get(key)).FontSize(8).Bold().FontColor(color);
        });

    private static IContainer Cell(TableDescriptor t, float border = 0.5f, float padding = 4, string? background = null)
    {
        IContainer cell = t.Cell();
        if (background is not null) cell = cell.Background(background);
        return cell.BorderBottom(border).BorderColor(_border).Padding(padding);
    }

    private static void ProgressBar(IContainer c, double fraction, float height)
        => c.Height(height).Row(row =>
        {
            float done = (float)fraction;
            if (done > 0) row.RelativeItem(done).Background(_green);
            if (done < 1) row.RelativeItem(1f - done).Background(_border);
        });

    private static string BurnRate(double estimated, double actual)
        => estimated > 0 ? $"{actual / estimated * 100:F0}%" : "—";

    private static string BurnColor(double estimated, double actual)
        => estimated > 0 && actual > estimated ? _red : _textDark;

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(s => s.FontFamily("Segoe UI").FontSize(9).FontColor(_textDark));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(14).Element(ComposeBody);
            page.Footer().Element(ComposeFooter);
        });
    }

    private (string Label, string Color) GetHealthStatus()
    {
        ProjectSummary s = _data.Summary;
        if (s.BlockedCount > 0 ||
            (s.TotalNodes > 0 && s.OverdueCount > (int)Math.Max(1, s.TotalNodes * 0.15)))
            return (Strings.Get("Report_HealthAtRisk"), _red);

        if (s.OverdueCount > 0 || s.AtRiskCount > 0)
            return (Strings.Get("Report_HealthCaution"), _amber);

        return (Strings.Get("Report_HealthOnTrack"), _green);
    }

    private void ComposeHeader(IContainer c)
    {
        (string healthLabel, string healthColor) = GetHealthStatus();

        c.Column(col =>
        {
            col.Item().Row(row =>
            {
                row.RelativeItem().Column(inner =>
                {
                    inner.Item()
                        .Text(_data.ProjectName)
                        .FontSize(20).Bold().FontColor(_textDark);
                    inner.Item()
                        .Text(Strings.Get("Report_Title"))
                        .FontSize(11).FontColor(_textMuted);
                });
                row.AutoItem().AlignRight().Column(right =>
                {
                    right.Item().AlignRight()
                        .Text(Strings.Format("Report_GeneratedAt", _data.GeneratedAt.ToString("d MMM yyyy, HH:mm")))
                        .FontSize(8).FontColor(_textMuted);
                    right.Item().PaddingTop(6).AlignRight().Text(txt =>
                    {
                        txt.Span(healthLabel).FontSize(10).Bold().FontColor(healthColor);
                    });
                });
            });
            col.Item().PaddingTop(8).LineHorizontal(2).LineColor(_blue);
        });
    }

    private void ComposeBody(IContainer c)
    {
        bool hasAttention = _options.IncludeAttentionRequired &&
            _data.WbsTree.Any(e => !e.IsParent && (e.IsOverdue || e.IsAtRisk));
        bool hasHours = _options.IncludeEffortSummary &&
            _data.WbsTree.Where(e => !e.IsParent).Any(e => e.EstHours > 0 || e.ActHours > 0);

        c.Column(col =>
        {
            if (_options.IncludeSummary)
                col.Item().Element(ComposeSummaryRow);

            if (hasAttention)
                col.Item().PaddingTop(18).Element(ComposeAttentionRequired);

            if (_options.IncludeDeliverables)
                col.Item().PaddingTop(18).Element(ComposeDeliverables);

            if (hasHours)
                col.Item().PaddingTop(18).Element(ComposeEffortSummary);

            if (_options.IncludeMilestones && _data.Milestones.Count > 0)
                col.Item().PaddingTop(18).Element(ComposeMilestones);

            if (_options.IncludeWbsTree && _data.WbsTree.Count > 0)
            {
                col.Item().PageBreak();
                col.Item().Element(ComposeWbsTree);
            }
        });
    }

    private void ComposeAttentionRequired(IContainer c)
    {
        // Overdue items sorted by most overdue first, then at-risk sorted by soonest due.
        // Capped at 10 rows to keep the first page clean.
        List<WbsTreeEntry> items = _data.WbsTree
            .Where(e => !e.IsParent && e.IsOverdue)
            .OrderByDescending(e => e.DaysOverdue)
            .Concat(_data.WbsTree
                .Where(e => !e.IsParent && e.IsAtRisk)
                .OrderBy(e => e.DaysUntilDue))
            .Take(10)
            .ToList();

        c.Column(col =>
        {
            SectionTitle(col, "Report_AttentionRequired", _red);

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(52);
                    cd.RelativeColumn();
                    cd.ConstantColumn(70);
                    cd.ConstantColumn(66);
                    cd.ConstantColumn(64);
                });

                HeaderRow(t, "#FEE2E2", _red, 5, "Report_ColCode", "Report_ColTitle", "Report_ColAssignee", "Report_ColDueDate", "Report_ColStatus");

                const string bg = "#FFF7F7";
                foreach (WbsTreeEntry entry in items)
                {
                    string urgency      = entry.IsOverdue
                        ? Strings.Format("Report_DaysOverdue", entry.DaysOverdue)
                        : Strings.Format("Report_DueInDays", entry.DaysUntilDue);
                    string urgencyColor = entry.IsOverdue ? _red : _amber;

                    Cell(t, background: bg).Text(entry.Code).FontSize(8).FontColor(_textMuted);
                    Cell(t, background: bg).Text(entry.Title).FontSize(8);
                    Cell(t, background: bg).Text(entry.AssignedToShort).FontSize(8).FontColor(_textMuted);
                    Cell(t, background: bg).Text(entry.DueDateShort).FontSize(8).FontColor(urgencyColor);
                    Cell(t, background: bg).Text(urgency).FontSize(8).Bold().FontColor(urgencyColor);
                }
            });
        });
    }

    private void ComposeEffortSummary(IContainer c)
    {
        // Use only leaf nodes to avoid double-counting hours from parent rollups.
        List<WbsTreeEntry> leaves = _data.WbsTree.Where(e => !e.IsParent).ToList();
        double totalEst  = leaves.Sum(e => e.EstHours);
        double totalAct  = leaves.Sum(e => e.ActHours);
        double remaining = Math.Max(0, totalEst - totalAct);

        var byAssignee = leaves
            .Where(e => !string.IsNullOrWhiteSpace(e.AssignedTo))
            .GroupBy(e => e.AssignedTo)
            .Select(g => (
                Assignee: g.Key,
                Nodes:    g.Count(),
                EstH:     g.Sum(e => e.EstHours),
                ActH:     g.Sum(e => e.ActHours)))
            .OrderBy(x => x.Assignee)
            .ToList();

        c.Column(col =>
        {
            SectionTitle(col, "Report_EffortSummary");

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    for (int i = 0; i < 4; i++) cd.RelativeColumn();
                });
                HeaderRow(t, 6, "Report_ColEstimated", "Report_ColActual", "Report_ColRemaining", "Report_ColBurnRate");

                string overBurnColor = BurnColor(totalEst, totalAct);
                void TotCell(string v, string color) =>
                    t.Cell().BorderBottom(1.5f).BorderColor(_border)
                        .PaddingVertical(8).PaddingHorizontal(6).AlignCenter()
                        .Text(v).FontSize(14).Bold().FontColor(color);

                TotCell($"{totalEst:F0}h", _textDark);
                TotCell($"{totalAct:F0}h", overBurnColor);
                TotCell($"{remaining:F0}h", _textDark);
                TotCell(BurnRate(totalEst, totalAct), overBurnColor);
            });

            if (byAssignee.Count > 0)
            {
                col.Item().PaddingTop(10).Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(2);
                        for (int i = 0; i < 4; i++) cd.RelativeColumn();
                    });
                    HeaderRow(t, 5, "Report_ColAssignee", "Report_ColTasks", "Report_ColEstHours", "Report_ColActHours", "Report_ColBurnRate");

                    int rowIdx = 0;
                    foreach ((string assignee, int nodes, double estH, double actH) in byAssignee)
                    {
                        string rowBg = rowIdx++ % 2 == 0 ? "#FFFFFF" : "#F9FAFB";

                        Cell(t, background: rowBg).Text(assignee).FontSize(8);
                        Cell(t, background: rowBg).Text(nodes.ToString()).FontSize(8).FontColor(_textMuted);
                        Cell(t, background: rowBg).Text($"{estH:F0}h").FontSize(8).FontColor(_textMuted);
                        Cell(t, background: rowBg).Text($"{actH:F0}h").FontSize(8).FontColor(_textDark);
                        Cell(t, background: rowBg).Text(BurnRate(estH, actH)).FontSize(8).FontColor(BurnColor(estH, actH));
                    }
                });
            }
        });
    }

    private void ComposeSummaryRow(IContainer c)
    {
        c.Column(col =>
        {
            SectionTitle(col, "Report_Summary");

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    for (int i = 0; i < 6; i++) cd.RelativeColumn();
                });

                HeaderRow(t, 6, "Report_ColPercentComplete", "Report_ColComplete", "Report_ColInProgress", "Report_ColAtRisk", "Report_ColOverdue", "Report_ColBlocked");

                void KpiCell(string value, string color) =>
                    t.Cell()
                        .BorderBottom(1.5f).BorderColor(_border)
                        .PaddingVertical(10).PaddingHorizontal(6)
                        .AlignCenter()
                        .Text(value).FontSize(16).Bold().FontColor(color);

                ProjectSummary s = _data.Summary;
                KpiCell($"{s.PercentComplete:F1}%",   _textDark);
                KpiCell(s.CompleteCount.ToString(),   _green);
                KpiCell(s.InProgressCount.ToString(), _blue);
                KpiCell(s.AtRiskCount.ToString(),     _amber);
                KpiCell(s.OverdueCount.ToString(),    _red);
                KpiCell(s.BlockedCount.ToString(),    _red);
            });
        });
    }

    private void ComposeDeliverables(IContainer c)
    {
        c.Column(col =>
        {
            SectionTitle(col, "Report_Deliverables");

            if (_data.Deliverables.Count == 0)
            {
                col.Item().PaddingTop(6)
                    .Text(Strings.Get("Report_NoDeliverables"))
                    .FontColor(_textMuted);
                return;
            }

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(46);
                    cd.RelativeColumn(3);
                    cd.ConstantColumn(62);
                    cd.ConstantColumn(78);
                    cd.ConstantColumn(68);
                });

                HeaderRow(t, 5, "Report_ColCode", "Report_ColTitle", "Report_ColProgress", "Report_ColStatus", "Report_ColDueDate");

                foreach (DeliverableRow row in _data.Deliverables)
                {
                    Cell(t, 1, 5).Text(row.Code).FontSize(9).SemiBold().FontColor(_textMuted);
                    Cell(t, 1, 5).Text(row.Title).FontSize(9).SemiBold();
                    Cell(t, 1, 5).Column(pc =>
                    {
                        pc.Item().Text(row.ProgressPercentText).FontSize(9).FontColor(_textDark);
                        ProgressBar(pc.Item().PaddingTop(3), row.ProgressFraction, 4);
                    });
                    Cell(t, 1, 5).Text(row.Status.ToDisplayString()).FontSize(9).FontColor(StatusPalette.Hex(row.Status));
                    Cell(t, 1, 5).Text(row.DueDateText).FontSize(9).FontColor(_textMuted);
                }
            });
        });
    }

    private void ComposeMilestones(IContainer c)
    {
        c.Column(col =>
        {
            SectionTitle(col, "Report_UpcomingMilestones");

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn();
                    cd.ConstantColumn(78);
                });

                HeaderRow(t, 5, "Report_ColMilestone", "Report_ColDueDate");

                foreach ((string title, DateTime due) in _data.Milestones)
                {
                    Cell(t, 1, 5).Text(title).FontSize(9);
                    Cell(t, 1, 5).Text(due.ToString("d MMM yyyy")).FontSize(9).FontColor(_textMuted);
                }
            });
        });
    }

    private void ComposeWbsTree(IContainer c)
    {
        c.Column(col =>
        {
            SectionTitle(col, "Report_WbsTree");

            col.Item().PaddingTop(6).Table(t =>
            {
                // A4 usable width is about 510pt with 1.5cm margins.
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(52);
                    cd.RelativeColumn();
                    cd.ConstantColumn(70);
                    cd.ConstantColumn(54);
                    cd.ConstantColumn(68);
                    cd.ConstantColumn(62);
                });

                HeaderRow(t, 5, "Report_ColCode", "Report_ColTitle", "Report_ColStatus", "Report_ColProgress", "Report_ColAssignee", "Report_ColDueDate");

                int rowIndex = 0;
                foreach (WbsTreeEntry entry in _data.WbsTree)
                {
                    string rowBg = rowIndex++ % 2 == 0 ? "#FFFFFF" : "#F9FAFB";

                    Cell(t, background: rowBg).Text(entry.Code).FontSize(8).FontColor(_textMuted);
                    TextBlockDescriptor title = Cell(t, background: rowBg).PaddingLeft(4 + entry.Depth * 10f)
                        .Text(entry.Title).FontSize(8);
                    if (entry.IsParent) title.SemiBold();
                    Cell(t, background: rowBg).Text(entry.Status.ToDisplayString()).FontSize(8).FontColor(StatusPalette.Hex(entry.Status));
                    Cell(t, background: rowBg).Column(pc =>
                    {
                        pc.Item().Text($"{(int)(entry.ProgressFraction * 100)}%").FontSize(8).FontColor(_textDark);
                        ProgressBar(pc.Item().PaddingTop(2), entry.ProgressFraction, 3);
                    });
                    Cell(t, background: rowBg).Text(entry.AssignedToShort).FontSize(8).FontColor(_textMuted);
                    Cell(t, background: rowBg).Text(entry.DueDateShort).FontSize(8).FontColor(_textMuted);
                }
            });
        });
    }

    private void ComposeFooter(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().PaddingBottom(4).LineHorizontal(0.5f).LineColor(_border);
            col.Item().Row(row =>
            {
                row.RelativeItem()
                    .Text(Strings.Get("Report_GeneratedBy"))
                    .FontSize(8).FontColor(_textMuted);
                row.AutoItem().Text(x =>
                {
                    x.Span(Strings.Get("Report_Page") + " ").FontSize(8).FontColor(_textMuted);
                    x.CurrentPageNumber().FontSize(8).FontColor(_textMuted);
                    x.Span(" " + Strings.Get("Report_PageOf") + " ").FontSize(8).FontColor(_textMuted);
                    x.TotalPages().FontSize(8).FontColor(_textMuted);
                });
            });
        });
    }
}
