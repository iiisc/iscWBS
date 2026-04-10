using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using iscWBS.Core.Models;

namespace iscWBS.Views.Controls;

public sealed partial class WbsDiagramControl : UserControl
{
    // ── Layout constants ────────────────────────────────────────────────────
    private const double _cardWidth    = 180;
    private const double _cardHeight   = 82;
    private const double _headerHeight = 22;
    private const double _footerHeight = 20;
    private const double _hGap         = 32;   // gap between sibling subtrees
    private const double _vGap         = 52;   // gap between parent row and child row
    private const double _pad          = 24;   // canvas edge padding

    // ── Dependency properties ────────────────────────────────────────────────

    public static readonly DependencyProperty AllNodesProperty =
        DependencyProperty.Register(
            nameof(AllNodes),
            typeof(IReadOnlyList<WbsNode>),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).RebuildDiagram()));

    public static readonly DependencyProperty SelectedNodeIdProperty =
        DependencyProperty.Register(
            nameof(SelectedNodeId),
            typeof(Guid),
            typeof(WbsDiagramControl),
            new PropertyMetadata(Guid.Empty, static (d, _) => ((WbsDiagramControl)d).UpdateSelectionHighlight()));

    public static readonly DependencyProperty BlockedNodeIdsProperty =
        DependencyProperty.Register(
            nameof(BlockedNodeIds),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).RebuildDiagram()));

    public static readonly DependencyProperty CompletionBlockedNodeIdsProperty =
        DependencyProperty.Register(
            nameof(CompletionBlockedNodeIds),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).RebuildDiagram()));

    public static readonly DependencyProperty ViolatedCompleteNodeIdsProperty =
        DependencyProperty.Register(
            nameof(ViolatedCompleteNodeIds),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).RebuildDiagram()));

    public static readonly DependencyProperty ViolatedInProgressNodeIdsProperty =
        DependencyProperty.Register(
            nameof(ViolatedInProgressNodeIds),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).RebuildDiagram()));

    public static readonly DependencyProperty DependenciesProperty =
        DependencyProperty.Register(
            nameof(Dependencies),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).RebuildDiagram()));

    public IReadOnlyList<WbsNode>? AllNodes
    {
        get => (IReadOnlyList<WbsNode>?)GetValue(AllNodesProperty);
        set => SetValue(AllNodesProperty, value);
    }

    public Guid SelectedNodeId
    {
        get => (Guid)GetValue(SelectedNodeIdProperty);
        set => SetValue(SelectedNodeIdProperty, value);
    }

    public object? BlockedNodeIds
    {
        get => GetValue(BlockedNodeIdsProperty);
        set => SetValue(BlockedNodeIdsProperty, value);
    }

    public object? CompletionBlockedNodeIds
    {
        get => GetValue(CompletionBlockedNodeIdsProperty);
        set => SetValue(CompletionBlockedNodeIdsProperty, value);
    }

    public object? ViolatedCompleteNodeIds
    {
        get => GetValue(ViolatedCompleteNodeIdsProperty);
        set => SetValue(ViolatedCompleteNodeIdsProperty, value);
    }

    public object? ViolatedInProgressNodeIds
    {
        get => GetValue(ViolatedInProgressNodeIdsProperty);
        set => SetValue(ViolatedInProgressNodeIdsProperty, value);
    }

    public object? Dependencies
    {
        get => GetValue(DependenciesProperty);
        set => SetValue(DependenciesProperty, value);
    }

    /// <summary>Fired when the user taps a node card in the diagram.</summary>
    public event EventHandler<WbsNode>? NodeSelected;

    /// <summary>Fired when the user right-taps a node card; position is relative to this control.</summary>
    public event EventHandler<(WbsNode Node, Point Position)>? NodeRightTapped;

    // ── Internal layout model ────────────────────────────────────────────────

    private sealed class DiagramNode
    {
        public required WbsNode Data { get; init; }
        public List<DiagramNode> Children { get; } = new();
        public double X            { get; set; }
        public double Y            { get; set; }
        public double SubtreeWidth { get; set; }
    }

    private readonly List<DiagramNode>        _roots         = new();
    private readonly Dictionary<Guid, Border> _cards         = new();
    private readonly List<UIElement>          _arrowElements = new();

    public WbsDiagramControl()
    {
        InitializeComponent();
    }

    // ── Rebuild ──────────────────────────────────────────────────────────────

    private void RebuildDiagram()
    {
        _roots.Clear();
        _cards.Clear();
        _arrowElements.Clear();
        DiagramCanvas.Children.Clear();
        DiagramCanvas.Width  = _pad * 2;
        DiagramCanvas.Height = _pad * 2;

        if (AllNodes is null || AllNodes.Count == 0) return;

        // Build the hierarchy from the flat list using ParentId links
        var diagramById = new Dictionary<Guid, DiagramNode>(AllNodes.Count);
        foreach (WbsNode n in AllNodes)
            diagramById[n.Id] = new DiagramNode { Data = n };

        foreach (WbsNode n in AllNodes)
        {
            DiagramNode dn = diagramById[n.Id];
            if (n.ParentId.HasValue && diagramById.TryGetValue(n.ParentId.Value, out DiagramNode? parent))
                parent.Children.Add(dn);
            else
                _roots.Add(dn);
        }

        if (_roots.Count == 0) return;

        // Preserve tree ordering
        SortChildren(_roots);

        // Phase 1 (bottom-up): compute the width each subtree needs
        foreach (DiagramNode root in _roots)
            MeasureSubtree(root);

        // Phase 2 (top-down): assign absolute (X, Y) positions
        double curX = _pad;
        foreach (DiagramNode root in _roots)
        {
            PlaceSubtree(root, curX, _pad);
            curX += root.SubtreeWidth + _hGap;
        }

        // Size the canvas to fit all cards
        double maxX = 0, maxY = 0;
        AccumulateBounds(_roots, ref maxX, ref maxY);
        DiagramCanvas.Width  = maxX + _pad;
        DiagramCanvas.Height = maxY + _pad;

        // Draw connectors first so they appear under the cards
        Brush connectorBrush = ResolveBrush("DiagramConnector", Colors.Gray);
        foreach (DiagramNode root in _roots)
            DrawConnectors(root, connectorBrush);

        // Draw node cards on top
        foreach (DiagramNode root in _roots)
            DrawCard(root);

        UpdateSelectionHighlight();
    }

    // ── Layout helpers ───────────────────────────────────────────────────────

    private static void SortChildren(IEnumerable<DiagramNode> nodes)
    {
        foreach (DiagramNode n in nodes)
        {
            n.Children.Sort(static (a, b) => a.Data.SortOrder.CompareTo(b.Data.SortOrder));
            SortChildren(n.Children);
        }
    }

    private static void MeasureSubtree(DiagramNode node)
    {
        if (node.Children.Count == 0)
        {
            node.SubtreeWidth = _cardWidth;
            return;
        }

        foreach (DiagramNode child in node.Children)
            MeasureSubtree(child);

        double span = node.Children.Sum(c => c.SubtreeWidth) + (node.Children.Count - 1) * _hGap;
        node.SubtreeWidth = Math.Max(_cardWidth, span);
    }

    private static void PlaceSubtree(DiagramNode node, double left, double top)
    {
        node.X = left + (node.SubtreeWidth - _cardWidth) / 2.0;
        node.Y = top;

        double childLeft = left;
        double childTop  = top + _cardHeight + _vGap;
        foreach (DiagramNode child in node.Children)
        {
            PlaceSubtree(child, childLeft, childTop);
            childLeft += child.SubtreeWidth + _hGap;
        }
    }

    private static void AccumulateBounds(IEnumerable<DiagramNode> nodes, ref double maxX, ref double maxY)
    {
        foreach (DiagramNode n in nodes)
        {
            double r = n.X + _cardWidth;
            double b = n.Y + _cardHeight;
            if (r > maxX) maxX = r;
            if (b > maxY) maxY = b;
            AccumulateBounds(n.Children, ref maxX, ref maxY);
        }
    }

    // ── Rendering ────────────────────────────────────────────────────────────

    private void DrawConnectors(DiagramNode node, Brush stroke)
    {
        double parentCx     = node.X + _cardWidth / 2.0;
        double parentBottom = node.Y + _cardHeight;

        foreach (DiagramNode child in node.Children)
        {
            double childCx = child.X + _cardWidth / 2.0;
            double elbowY  = parentBottom + _vGap / 2.0;

            // Elbow connector: parent-bottom → elbow → child-top
            var wire = new Polyline
            {
                Stroke          = stroke,
                StrokeThickness = 1.5,
                StrokeLineJoin  = PenLineJoin.Round,
            };
            wire.Points.Add(new Point(parentCx, parentBottom));
            wire.Points.Add(new Point(parentCx, elbowY));
            wire.Points.Add(new Point(childCx,  elbowY));
            wire.Points.Add(new Point(childCx,  child.Y));
            DiagramCanvas.Children.Add(wire);

            DrawConnectors(child, stroke);
        }
    }

    private void DrawCard(DiagramNode node)
    {
        // Always use the stored status for colour — blocking state is shown via a badge overlay.
        Brush statusBrush    = GetStatusBrush(node.Data.Status);
        Brush secondaryBrush = ResolveBrush("TextFillColorSecondaryBrush", Colors.Gray);

        var card = new Border
        {
            Width            = _cardWidth,
            Height           = _cardHeight,
            CornerRadius     = new CornerRadius(6),
            BorderThickness  = new Thickness(1.5),
            BorderBrush      = statusBrush,
            Background       = ResolveBrush("DiagramCardBackground", Colors.White),
            Tag              = node.Data,
            Child            = BuildCardContent(node.Data, statusBrush, secondaryBrush),
        };
        AutomationProperties.SetName(card, $"{node.Data.Code} — {node.Data.Title}");
        card.Tapped      += Card_Tapped;
        card.RightTapped += Card_RightTapped;

        Canvas.SetLeft(card, node.X);
        Canvas.SetTop(card,  node.Y);
        DiagramCanvas.Children.Add(card);
        _cards[node.Data.Id] = card;

        // Badge in the top-right corner signals blocking state without replacing the status colour.
        bool isStartBlocked      = BlockedNodeIds           is IReadOnlySet<Guid> bids  && bids.Contains(node.Data.Id);
        bool isCompletionBlocked = CompletionBlockedNodeIds is IReadOnlySet<Guid> cbids && cbids.Contains(node.Data.Id);

        if (isStartBlocked)
            AddBlockingBadge(node.X, node.Y, "\uE83D",
                Windows.UI.Color.FromArgb(0xFF, 0xC5, 0x0F, 0x1F),
                "Start blocked — predecessor constraints not met");
        else if (isCompletionBlocked)
            AddBlockingBadge(node.X, node.Y, "\uE7BA",
                Windows.UI.Color.FromArgb(0xFF, 0xCA, 0x50, 0x10),
                "Completion blocked — predecessor constraints not met");
        else if (ViolatedCompleteNodeIds is IReadOnlySet<Guid> vids && vids.Contains(node.Data.Id))
            AddBlockingBadge(node.X, node.Y, "\uE7BA",
                Windows.UI.Color.FromArgb(0xFF, 0xCA, 0x50, 0x10),
                "Constraint violated — this node is Complete but a predecessor no longer satisfies its dependency");
        else if (ViolatedInProgressNodeIds is IReadOnlySet<Guid> vipids && vipids.Contains(node.Data.Id))
            AddBlockingBadge(node.X, node.Y, "\uE7BA",
                Windows.UI.Color.FromArgb(0xFF, 0xC5, 0x0F, 0x1F),
                "Start constraint violated — this node is In Progress but a predecessor no longer satisfies its start dependency");

        foreach (DiagramNode child in node.Children)
            DrawCard(child);
    }

    private void AddBlockingBadge(double cardX, double cardY, string icon, Windows.UI.Color color, string tooltip)
    {
        const double size = 16.0;
        var badge = new Border
        {
            Width        = size,
            Height       = size,
            CornerRadius = new CornerRadius(size / 2),
            Background   = new SolidColorBrush(color),
            Child        = new TextBlock
            {
                Text                = icon,
                FontFamily          = new FontFamily("Segoe MDL2 Assets"),
                FontSize            = 9,
                Foreground          = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(badge, cardX + _cardWidth  - size / 2);
        Canvas.SetTop(badge,  cardY               - size / 2);
        ToolTipService.SetToolTip(badge, tooltip);
        DiagramCanvas.Children.Add(badge);
    }

    private static UIElement BuildCardContent(WbsNode node, Brush headerBrush, Brush secondaryForeground)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_headerHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_footerHeight) });

        // ── Header: coloured strip with the WBS code ──────────────────────
        var header = new Border
        {
            Background   = headerBrush,
            CornerRadius = new CornerRadius(4.5, 4.5, 0, 0),
            Padding      = new Thickness(8, 0, 8, 0),
        };
        header.Child = new TextBlock
        {
            Text              = node.Code,
            FontWeight        = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize          = 11,
            Foreground        = new SolidColorBrush(Colors.White),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        // ── Body: node title ──────────────────────────────────────────────
        var body = new Border { Padding = new Thickness(8, 3, 8, 2) };
        body.Child = new TextBlock
        {
            Text              = node.Title,
            FontSize          = 11,
            TextWrapping      = TextWrapping.Wrap,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLines          = 2,
        };
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

        // ── Footer: assignee and/or due date ─────────────────────────────
        bool hasAssignee = !string.IsNullOrWhiteSpace(node.AssignedTo);
        bool hasDue      = node.DueDate.HasValue;
        if (hasAssignee || hasDue)
        {
            string metaText = (hasAssignee, hasDue) switch
            {
                (true, true)  => $"{node.AssignedTo}  ·  {node.DueDate!.Value:d MMM}",
                (true, false) => node.AssignedTo,
                _             => node.DueDate!.Value.ToString("d MMM"),
            };
            var footer = new Border { Padding = new Thickness(8, 0, 8, 3) };
            footer.Child = new TextBlock
            {
                Text              = metaText,
                FontSize          = 10,
                Foreground        = secondaryForeground,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);
        }

        return grid;
    }

    // ── Selection ────────────────────────────────────────────────────────────

    private void ClearArrows()
    {
        foreach (UIElement el in _arrowElements)
            DiagramCanvas.Children.Remove(el);
        _arrowElements.Clear();
    }

    /// <summary>
    /// Draws dependency arrows for the currently selected node only.
    /// Only one node’s relationships are shown at a time to avoid the routing
    /// chaos that results from drawing all project dependencies simultaneously
    /// across an org-chart layout.
    /// </summary>
    private void DrawDependencyArrows()
    {
        ClearArrows();

        if (Dependencies is not IReadOnlyList<NodeDependency> deps || deps.Count == 0) return;
        if (SelectedNodeId == Guid.Empty) return;

        Brush startConstraintBrush  = ResolveBrush("DiagramConnector", Colors.Gray);
        var   finishConstraintBrush = new SolidColorBrush(Windows.UI.Color.FromArgb(0xA0, 0xCA, 0x50, 0x10));

        foreach (NodeDependency dep in deps)
        {
            if (dep.PredecessorId != SelectedNodeId && dep.SuccessorId != SelectedNodeId)
                continue;

            if (!_cards.TryGetValue(dep.PredecessorId, out Border? predCard) ||
                !_cards.TryGetValue(dep.SuccessorId,   out Border? succCard))
                continue;

            double predLeft    = Canvas.GetLeft(predCard);
            double predTop     = Canvas.GetTop(predCard);
            double predCenterX = predLeft + _cardWidth  / 2.0;
            double predCenterY = predTop  + _cardHeight / 2.0;

            double succLeft    = Canvas.GetLeft(succCard);
            double succTop     = Canvas.GetTop(succCard);
            double succCenterX = succLeft + _cardWidth  / 2.0;
            double succCenterY = succTop  + _cardHeight / 2.0;

            // Vector between the two card centres in the 2-D canvas space.
            double dx = succCenterX - predCenterX;
            double dy = succCenterY - predCenterY;

            // Always route dependency arrows through left / right card edges.
            // Top / bottom edges are reserved exclusively for the parent-child
            // structural connectors drawn by DrawConnectors, so using them here
            // would cause the two kinds of relationship to visually merge.
            double fromX, fromY, toX, toY;
            double cp1X, cp1Y, cp2X, cp2Y;

            bool sameColumn = Math.Abs(dx) < _cardWidth * 0.5;

            if (sameColumn)
            {
                // Nodes share the same horizontal band.  Loop through the right
                // margin so the curve never crosses the parent-child tree lines.
                fromX = predLeft + _cardWidth;
                fromY = predCenterY;
                toX   = succLeft  + _cardWidth;
                toY   = succCenterY;

                double loopX = Math.Max(predLeft, succLeft) + _cardWidth + 80.0;
                cp1X = loopX;  cp1Y = fromY;
                cp2X = loopX;  cp2Y = toY;
            }
            else if (dx > 0)
            {
                // Successor is to the right → exit right, enter left.
                fromX = predLeft + _cardWidth;
                fromY = predCenterY;
                toX   = succLeft;
                toY   = succCenterY;

                double off = Math.Max(dx * 0.4, 50.0);
                cp1X = fromX + off;  cp1Y = fromY;
                cp2X = toX   - off;  cp2Y = toY;
            }
            else
            {
                // Successor is to the left → exit left, enter right.
                fromX = predLeft;
                fromY = predCenterY;
                toX   = succLeft  + _cardWidth;
                toY   = succCenterY;

                double off = Math.Max(-dx * 0.4, 50.0);
                cp1X = fromX - off;  cp1Y = fromY;
                cp2X = toX   + off;  cp2Y = toY;
            }

            bool isFinishConstraint = DependencyConstraints.IsFinishConstraint(dep.Type);
            string typeLabel = dep.Type switch
            {
                DependencyType.FinishToStart  => "Finish → Start",
                DependencyType.StartToStart   => "Start → Start",
                DependencyType.FinishToFinish => "Finish → Finish",
                DependencyType.StartToFinish  => "Start → Finish",
                _                             => dep.Type.ToString(),
            };

            Brush arrowBrush = isFinishConstraint ? finishConstraintBrush : startConstraintBrush;

            var figure = new PathFigure { StartPoint = new Point(fromX, fromY), IsClosed = false };
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(cp1X, cp1Y),
                Point2 = new Point(cp2X, cp2Y),
                Point3 = new Point(toX,  toY),
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            var curve = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data            = geometry,
                Stroke          = arrowBrush,
                StrokeThickness = 2.0,
                Fill            = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                StrokeDashArray = isFinishConstraint ? new DoubleCollection { 4, 2 } : null,
            };
            ToolTipService.SetToolTip(curve, typeLabel);
            DiagramCanvas.Children.Add(curve);
            _arrowElements.Add(curve);

            // Filled circle at the destination marks the direction of the arrow.
            const double dotR = 4.0;
            var dot = new Ellipse { Width = dotR * 2, Height = dotR * 2, Fill = arrowBrush };
            Canvas.SetLeft(dot, toX - dotR);
            Canvas.SetTop(dot,  toY - dotR);
            DiagramCanvas.Children.Add(dot);
            _arrowElements.Add(dot);
        }
    }

    private void UpdateSelectionHighlight()
    {
        foreach ((Guid id, Border card) in _cards)
        {
            WbsNode node        = (WbsNode)card.Tag!;
            bool    isSelected  = SelectedNodeId != Guid.Empty && id == SelectedNodeId;
            card.BorderThickness = new Thickness(isSelected ? 2.5 : 1.5);
            card.BorderBrush     = isSelected
                ? ResolveBrush("DiagramSelection", Colors.Blue)
                : GetStatusBrush(node.Status);
        }

        DrawDependencyArrows();
    }

    private void Card_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: WbsNode node })
            NodeSelected?.Invoke(this, node);
    }

    private void Card_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is Border { Tag: WbsNode node })
            NodeRightTapped?.Invoke(this, (node, e.GetPosition(this)));
    }

    // ── Resource helpers ─────────────────────────────────────────────────────

    private Brush GetStatusBrush(WbsStatus status)
    {
        string key = status switch
        {
            WbsStatus.InProgress => "WbsStatusInProgressBrush",
            WbsStatus.Complete   => "WbsStatusCompleteBrush",
            WbsStatus.Blocked    => "WbsStatusBlockedBrush",
            _                    => "WbsStatusNotStartedBrush",
        };
        return ResolveBrush(key, Colors.Gray);
    }

    private Brush ResolveBrush(string key, Windows.UI.Color fallback)
    {
        if (Resources.ContainsKey(key))
            return (Brush)Resources[key];
        if (Application.Current.Resources.ContainsKey(key))
            return (Brush)Application.Current.Resources[key];
        // ContainsKey does not search ThemeDictionaries in WinUI 3 — look in the active theme directly
        string themeKey = ActualTheme == ElementTheme.Dark ? "Dark" : "Light";
        if (Application.Current.Resources.ThemeDictionaries.TryGetValue(themeKey, out object? themeObj)
            && themeObj is ResourceDictionary themeDict
            && themeDict.ContainsKey(key))
            return (Brush)themeDict[key];
        return new SolidColorBrush(fallback);
    }
}
