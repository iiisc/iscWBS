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

    // ─── Palette ─────────────────────────────────────────────────────
    private const string _blue       = "#0078D4";
    private const string _green      = "#107C10";
    private const string _red        = "#C50F1F";
    private const string _amber      = "#C47D0E";
    private const string _grey       = "#6B7280";
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
        Title    = $"{_data.ProjectName} \u2014 Status Report",
        Author   = "iscWBS",
        Subject  = "Project Status Report",
        CreationDate = _data.GeneratedAt
    };

    public DocumentSettings GetSettings() => DocumentSettings.Default;

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

    // ─── Header ───────────────────────────────────────────────────────────────

    private (string Label, string Color) GetHealthStatus()
    {
        if (_data.BlockedCount > 0 ||
            (_data.TotalNodes > 0 && _data.OverdueCount > (int)Math.Max(1, _data.TotalNodes * 0.15)))
            return ("\u25cf AT RISK", _red);

        if (_data.OverdueCount > 0 || _data.AtRiskCount > 0)
            return ("\u25cf CAUTION", _amber);

        return ("\u25cf ON TRACK", _green);
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
                        .Text("Project Status Report")
                        .FontSize(11).FontColor(_textMuted);
                });
                row.AutoItem().AlignRight().Column(right =>
                {
                    right.Item().AlignRight()
                        .Text($"Generated  {_data.GeneratedAt:d MMM yyyy, HH:mm}")
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

    // ─── Body ─────────────────────────────────────────────────────────────────

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

    // ─── Attention Required ─────────────────────────────────────────────────

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

        if (items.Count == 0) return;

        c.Column(col =>
        {
            col.Item().Text("ATTENTION REQUIRED")
                .FontSize(9).Bold().FontColor(_red).LetterSpacing(0.05f);

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(52);  // Code
                    cd.RelativeColumn();    // Title
                    cd.ConstantColumn(70);  // Assignee
                    cd.ConstantColumn(66);  // Due Date
                    cd.ConstantColumn(64);  // Urgency
                });

                t.Header(h =>
                {
                    foreach (string label in new[] { "Code", "Title", "Assignee", "Due Date", "Status" })
                        h.Cell().Background("#FEE2E2").Padding(5)
                            .Text(label).FontSize(8).Bold().FontColor(_red);
                });

                foreach (WbsTreeEntry entry in items)
                {
                    string urgency      = entry.IsOverdue
                        ? $"{entry.DaysOverdue}d overdue"
                        : $"due in {entry.DaysUntilDue}d";
                    string urgencyColor = entry.IsOverdue ? _red : _amber;

                    t.Cell().Background("#FFF7F7").BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4).Text(entry.Code).FontSize(8).FontColor(_textMuted);
                    t.Cell().Background("#FFF7F7").BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4).Text(entry.Title).FontSize(8);
                    t.Cell().Background("#FFF7F7").BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4).Text(entry.AssignedToShort).FontSize(8).FontColor(_textMuted);
                    t.Cell().Background("#FFF7F7").BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4).Text(entry.DueDateShort).FontSize(8).FontColor(urgencyColor);
                    t.Cell().Background("#FFF7F7").BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4).Text(urgency).FontSize(8).Bold().FontColor(urgencyColor);
                }
            });
        });
    }

    // ─── Effort Summary ────────────────────────────────────────────────────

    private void ComposeEffortSummary(IContainer c)
    {
        // Use only leaf nodes to avoid double-counting hours from parent rollups.
        List<WbsTreeEntry> leaves = _data.WbsTree.Where(e => !e.IsParent).ToList();
        double totalEst  = leaves.Sum(e => e.EstHours);
        double totalAct  = leaves.Sum(e => e.ActHours);
        double remaining = Math.Max(0, totalEst - totalAct);
        string burnRate  = totalEst > 0 ? $"{totalAct / totalEst * 100:F0}%" : "—";

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
            col.Item().Text("EFFORT SUMMARY")
                .FontSize(9).Bold().FontColor(_sectionFg).LetterSpacing(0.05f);

            // Overall totals row
            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    for (int i = 0; i < 4; i++) cd.RelativeColumn();
                });
                t.Header(h =>
                {
                    foreach (string label in new[] { "Estimated", "Actual", "Remaining", "Burn Rate" })
                        h.Cell().Background(_headerBg).Padding(6)
                            .Text(label).FontSize(8).Bold().FontColor(_sectionFg);
                });

                string overBurnColor = totalEst > 0 && totalAct > totalEst ? _red : _textDark;
                void TotCell(string v, string color) =>
                    t.Cell().BorderBottom(1.5f).BorderColor(_border)
                        .PaddingVertical(8).PaddingHorizontal(6).AlignCenter()
                        .Text(v).FontSize(14).Bold().FontColor(color);

                TotCell($"{totalEst:F0}h", _textDark);
                TotCell($"{totalAct:F0}h", overBurnColor);
                TotCell($"{remaining:F0}h", _textDark);
                TotCell(burnRate, overBurnColor);
            });

            // Per-assignee breakdown
            if (byAssignee.Count > 0)
            {
                col.Item().PaddingTop(10).Table(t =>
                {
                    t.ColumnsDefinition(cd =>
                    {
                        cd.RelativeColumn(2);  // Assignee
                        cd.RelativeColumn();   // Tasks
                        cd.RelativeColumn();   // Est
                        cd.RelativeColumn();   // Act
                        cd.RelativeColumn();   // Burn %
                    });
                    t.Header(h =>
                    {
                        foreach (string label in new[] { "Assignee", "Tasks", "Est. Hours", "Act. Hours", "Burn Rate" })
                            h.Cell().Background(_headerBg).Padding(5)
                                .Text(label).FontSize(8).Bold().FontColor(_sectionFg);
                    });

                    int rowIdx = 0;
                    foreach ((string assignee, int nodes, double estH, double actH) in byAssignee)
                    {
                        string rowBg  = rowIdx++ % 2 == 0 ? "#FFFFFF" : "#F9FAFB";
                        string burn   = estH > 0 ? $"{actH / estH * 100:F0}%" : "—";
                        string burnFg = estH > 0 && actH > estH ? _red : _textDark;

                        t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border).Padding(4).Text(assignee).FontSize(8);
                        t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border).Padding(4).Text(nodes.ToString()).FontSize(8).FontColor(_textMuted);
                        t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border).Padding(4).Text($"{estH:F0}h").FontSize(8).FontColor(_textMuted);
                        t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border).Padding(4).Text($"{actH:F0}h").FontSize(8).FontColor(_textDark);
                        t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border).Padding(4).Text(burn).FontSize(8).FontColor(burnFg);
                    }
                });
            }
        });
    }

    // ─── KPI summary ────────────────────────────────────────────────────────

    private void ComposeSummaryRow(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().Text("SUMMARY")
                .FontSize(9).Bold().FontColor(_sectionFg).LetterSpacing(0.05f);

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    for (int i = 0; i < 6; i++) cd.RelativeColumn();
                });

                t.Header(h =>
                {
                    foreach (string label in new[] { "% Complete", "Complete", "In Progress", "At Risk", "Overdue", "Blocked" })
                        h.Cell().Background(_headerBg).Padding(6)
                            .Text(label).FontSize(8).Bold().FontColor(_sectionFg);
                });

                void KpiCell(string value, string color) =>
                    t.Cell()
                        .BorderBottom(1.5f).BorderColor(_border)
                        .PaddingVertical(10).PaddingHorizontal(6)
                        .AlignCenter()
                        .Text(value).FontSize(16).Bold().FontColor(color);

                KpiCell($"{_data.OverallPercent:F1}%", _textDark);
                KpiCell(_data.CompleteCount.ToString(),   _green);
                KpiCell(_data.InProgressCount.ToString(), _blue);
                KpiCell(_data.AtRiskCount.ToString(),     _amber);
                KpiCell(_data.OverdueCount.ToString(),    _red);
                KpiCell(_data.BlockedCount.ToString(),    _red);
            });
        });
    }

    // ─── Deliverables ─────────────────────────────────────────────────────────

    private void ComposeDeliverables(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().Text("DELIVERABLES")
                .FontSize(9).Bold().FontColor(_sectionFg).LetterSpacing(0.05f);

            if (_data.Deliverables.Count == 0)
            {
                col.Item().PaddingTop(6)
                    .Text("No deliverables found. Open a node in the WBS editor and enable \u201cInclude in report\u201d to add it here.")
                    .FontColor(_textMuted);
                return;
            }

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(46);  // Code
                    cd.RelativeColumn(3);   // Title
                    cd.ConstantColumn(62);  // Progress
                    cd.ConstantColumn(78);  // Status
                    cd.ConstantColumn(68);  // Due Date
                });

                t.Header(h =>
                {
                    foreach (string label in new[] { "Code", "Title", "Progress", "Status", "Due Date" })
                        h.Cell().Background(_headerBg).Padding(5)
                            .Text(label).FontSize(8).Bold().FontColor(_sectionFg);
                });

                foreach (DeliverableRow row in _data.Deliverables)
                {
                    string statusText = row.Status switch
                    {
                        WbsStatus.InProgress => "In Progress",
                        WbsStatus.Complete   => "Complete",
                        WbsStatus.Blocked    => "Blocked",
                        _                    => "Not Started"
                    };
                    string statusColor = row.Status switch
                    {
                        WbsStatus.InProgress => _blue,
                        WbsStatus.Complete   => _green,
                        WbsStatus.Blocked    => _red,
                        _                    => _grey
                    };

                    t.Cell().BorderBottom(1).BorderColor(_border).Padding(5)
                        .Text(row.Code).FontSize(9).SemiBold().FontColor(_textMuted);

                    t.Cell().BorderBottom(1).BorderColor(_border).Padding(5)
                        .Text(row.Title).FontSize(9).SemiBold();

                    t.Cell().BorderBottom(1).BorderColor(_border).Padding(5).Column(pc =>
                    {
                        pc.Item().Text(row.ProgressPercentText).FontSize(9).FontColor(_textDark);
                        pc.Item().PaddingTop(3).Height(4).Row(pr =>
                        {
                            float done = (float)row.ProgressFraction;
                            float rem  = 1f - done;
                            if (done > 0) pr.RelativeItem(done).Background(_green);
                            if (rem  > 0) pr.RelativeItem(rem).Background(_border);
                        });
                    });

                    t.Cell().BorderBottom(1).BorderColor(_border).Padding(5)
                        .Text(statusText).FontSize(9).FontColor(statusColor);

                    t.Cell().BorderBottom(1).BorderColor(_border).Padding(5)
                        .Text(row.DueDateText).FontSize(9).FontColor(_textMuted);
                }
            });
        });
    }

    // ─── Milestones ───────────────────────────────────────────────────────────

    private void ComposeMilestones(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().Text("UPCOMING MILESTONES")
                .FontSize(9).Bold().FontColor(_sectionFg).LetterSpacing(0.05f);

            col.Item().PaddingTop(6).Table(t =>
            {
                t.ColumnsDefinition(cd =>
                {
                    cd.RelativeColumn();
                    cd.ConstantColumn(78);
                });

                t.Header(h =>
                {
                    h.Cell().Background(_headerBg).Padding(5).Text("Milestone").FontSize(8).Bold().FontColor(_sectionFg);
                    h.Cell().Background(_headerBg).Padding(5).Text("Due Date").FontSize(8).Bold().FontColor(_sectionFg);
                });

                foreach ((string title, DateTime due) in _data.Milestones)
                {
                    t.Cell().BorderBottom(1).BorderColor(_border).Padding(5).Text(title).FontSize(9);
                    t.Cell().BorderBottom(1).BorderColor(_border).Padding(5)
                        .Text(due.ToString("d MMM yyyy")).FontSize(9).FontColor(_textMuted);
                }
            });
        });
    }

    // ─── WBS Tree outline ─────────────────────────────────────────────────────

    private void ComposeWbsTree(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().Text("WORK BREAKDOWN STRUCTURE")
                .FontSize(9).Bold().FontColor(_sectionFg).LetterSpacing(0.05f);

            if (_data.WbsTree.Count == 0)
            {
                col.Item().PaddingTop(6).Text("No WBS nodes found.").FontColor(_textMuted);
                return;
            }

            col.Item().PaddingTop(6).Table(t =>
            {
                // A4 usable width ≈ 510pt with 1.5cm margins
                t.ColumnsDefinition(cd =>
                {
                    cd.ConstantColumn(52);   // Code
                    cd.RelativeColumn();     // Title (indented)
                    cd.ConstantColumn(70);   // Status
                    cd.ConstantColumn(54);   // Progress
                    cd.ConstantColumn(68);   // Assignee
                    cd.ConstantColumn(62);   // Due Date
                });

                t.Header(h =>
                {
                    foreach (string label in new[] { "Code", "Title", "Status", "Progress", "Assignee", "Due Date" })
                        h.Cell().Background(_headerBg).Padding(5)
                            .Text(label).FontSize(8).Bold().FontColor(_sectionFg);
                });

                int rowIndex = 0;
                foreach (WbsTreeEntry entry in _data.WbsTree)
                {
                    string rowBg = rowIndex++ % 2 == 0 ? "#FFFFFF" : "#F9FAFB";
                    float indent = entry.Depth * 10f;

                    string statusText = entry.Status switch
                    {
                        WbsStatus.InProgress => "In Progress",
                        WbsStatus.Complete   => "Complete",
                        WbsStatus.Blocked    => "Blocked",
                        _                    => "Not Started"
                    };
                    string statusColor = entry.Status switch
                    {
                        WbsStatus.InProgress => _blue,
                        WbsStatus.Complete   => _green,
                        WbsStatus.Blocked    => _red,
                        _                    => _grey
                    };

                    // Code
                    t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4)
                        .Text(entry.Code).FontSize(8).FontColor(_textMuted);

                    // Title — indented per depth level
                    var titleCell = t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4).PaddingLeft(4 + indent);
                    if (entry.IsParent)
                        titleCell.Text(entry.Title).FontSize(8).SemiBold();
                    else
                        titleCell.Text(entry.Title).FontSize(8);

                    // Status
                    t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4)
                        .Text(statusText).FontSize(8).FontColor(statusColor);

                    // Progress — percentage + micro bar
                    t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4).Column(pc =>
                        {
                            pc.Item()
                                .Text($"{(int)(entry.ProgressFraction * 100)}%")
                                .FontSize(8).FontColor(_textDark);
                            pc.Item().PaddingTop(2).Height(3).Row(pr =>
                            {
                                float done = (float)entry.ProgressFraction;
                                float rem  = 1f - done;
                                if (done > 0) pr.RelativeItem(done).Background(_green);
                                if (rem  > 0) pr.RelativeItem(rem).Background(_border);
                            });
                        });

                    // Assignee
                    t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4)
                        .Text(entry.AssignedToShort).FontSize(8).FontColor(_textMuted);

                    // Due Date
                    t.Cell().Background(rowBg).BorderBottom(0.5f).BorderColor(_border)
                        .Padding(4)
                        .Text(entry.DueDateShort).FontSize(8).FontColor(_textMuted);
                }
            });
        });
    }

    // ─── Footer ───────────────────────────────────────────────────────────────

    private void ComposeFooter(IContainer c)
    {
        c.Column(col =>
        {
            col.Item().PaddingBottom(4).LineHorizontal(0.5f).LineColor(_border);
            col.Item().Row(row =>
            {
                row.RelativeItem()
                    .Text("Generated by iscWBS")
                    .FontSize(8).FontColor(_textMuted);
                row.AutoItem().Text(x =>
                {
                    x.Span("Page ").FontSize(8).FontColor(_textMuted);
                    x.CurrentPageNumber().FontSize(8).FontColor(_textMuted);
                    x.Span(" of ").FontSize(8).FontColor(_textMuted);
                    x.TotalPages().FontSize(8).FontColor(_textMuted);
                });
            });
        });
    }
}
