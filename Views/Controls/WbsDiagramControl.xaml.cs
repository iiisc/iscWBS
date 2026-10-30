using Windows.ApplicationModel.DataTransfer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using iscWBS.Core.Models;
using iscWBS.Helpers;

namespace iscWBS.Views.Controls;

public sealed partial class WbsDiagramControl : UserControl
{
    private const double _cardWidth    = 180;
    private const double _cardHeight   = 82;
    private const double _headerHeight = 22;
    private const double _footerHeight = 20;
    private const double _hGap         = 32;   // gap between sibling subtrees
    private const double _vGap         = 52;   // gap between parent row and child row
    private const double _pad          = 24;   // canvas edge padding

    public static readonly DependencyProperty AllNodesProperty =
        DependencyProperty.Register(
            nameof(AllNodes),
            typeof(IReadOnlyList<WbsNode>),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).ScheduleRebuild()));

    public static readonly DependencyProperty SelectedNodeIdProperty =
        DependencyProperty.Register(
            nameof(SelectedNodeId),
            typeof(Guid),
            typeof(WbsDiagramControl),
            new PropertyMetadata(Guid.Empty, static (d, _) => ((WbsDiagramControl)d).OnSelectedNodeIdChanged()));

    public static readonly DependencyProperty BlockedNodeIdsProperty =
        DependencyProperty.Register(
            nameof(BlockedNodeIds),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).ScheduleRebuild()));

    public static readonly DependencyProperty CompletionBlockedNodeIdsProperty =
        DependencyProperty.Register(
            nameof(CompletionBlockedNodeIds),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).ScheduleRebuild()));

    public static readonly DependencyProperty ViolatedCompleteNodeIdsProperty =
        DependencyProperty.Register(
            nameof(ViolatedCompleteNodeIds),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).ScheduleRebuild()));

    public static readonly DependencyProperty ViolatedInProgressNodeIdsProperty =
        DependencyProperty.Register(
            nameof(ViolatedInProgressNodeIds),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).ScheduleRebuild()));

    public static readonly DependencyProperty DependenciesProperty =
        DependencyProperty.Register(
            nameof(Dependencies),
            typeof(object),
            typeof(WbsDiagramControl),
            new PropertyMetadata(null, static (d, _) => ((WbsDiagramControl)d).ScheduleRebuild()));

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

    public IReadOnlySet<Guid>? BlockedNodeIds
    {
        get => (IReadOnlySet<Guid>?)GetValue(BlockedNodeIdsProperty);
        set => SetValue(BlockedNodeIdsProperty, value);
    }

    public IReadOnlySet<Guid>? CompletionBlockedNodeIds
    {
        get => (IReadOnlySet<Guid>?)GetValue(CompletionBlockedNodeIdsProperty);
        set => SetValue(CompletionBlockedNodeIdsProperty, value);
    }

    public IReadOnlySet<Guid>? ViolatedCompleteNodeIds
    {
        get => (IReadOnlySet<Guid>?)GetValue(ViolatedCompleteNodeIdsProperty);
        set => SetValue(ViolatedCompleteNodeIdsProperty, value);
    }

    public IReadOnlySet<Guid>? ViolatedInProgressNodeIds
    {
        get => (IReadOnlySet<Guid>?)GetValue(ViolatedInProgressNodeIdsProperty);
        set => SetValue(ViolatedInProgressNodeIdsProperty, value);
    }

    public IReadOnlyList<NodeDependency>? Dependencies
    {
        get => (IReadOnlyList<NodeDependency>?)GetValue(DependenciesProperty);
        set => SetValue(DependenciesProperty, value);
    }

    /// <summary>Fired when the user taps a node card in the diagram.</summary>
    public event EventHandler<WbsNode>? NodeSelected;

    /// <summary>Fired when the user right-taps a node card; position is relative to this control.</summary>
    public event EventHandler<(WbsNode Node, Point Position)>? NodeRightTapped;

    /// <summary>Fired when a card is dragged onto another card, to become its last child.</summary>
    public event EventHandler<(Guid NodeId, Guid NewParentId)>? NodeDropped;

    private Guid _draggedId;
    // The dragged node's branch and parent, where it can't be dropped.
    private HashSet<Guid> _noDropIds = [];

    private sealed class DiagramNode
    {
        public required WbsNode Data { get; init; }
        public List<DiagramNode> Children { get; } = new();
        public double   X            { get; set; }
        public double   Y            { get; set; }
        public double   SubtreeWidth { get; set; }
        /// <summary>Left edge of each child's subtree allocation, relative to this node's allocation left.</summary>
        public double[] ChildLeft    { get; set; } = [];
        /// <summary>Leftmost X at each depth level, relative to this node's allocation left.</summary>
        public double[] LeftContour  { get; set; } = [];
        /// <summary>Rightmost X at each depth level, relative to this node's allocation left.</summary>
        public double[] RightContour { get; set; } = [];
    }

    private readonly Dictionary<Guid, Border> _cards          = new();
    private readonly List<UIElement>          _arrowElements  = new();
    private readonly HashSet<Guid>            _collapsedNodes = new();
    private readonly FontFamily               _iconFont       = new("Segoe MDL2 Assets");

    // Proxy brushes from the XAML resources; their colours follow the theme.
    private readonly Dictionary<WbsStatus, Brush> _statusBrushes;
    private readonly Brush _secondaryBrush;
    private readonly Brush _selectionBrush;
    private readonly Brush _connectorBrush;
    private readonly Brush _cardBackground;
    private readonly Brush _onStatusBrush;
    private readonly Brush _accentBrush;
    private readonly Brush _onAccentBrush;
    private Guid _highlightedId;
    private WbsHierarchy? _hierarchy;
    private Dictionary<Guid, int> _levels = [];

    public WbsDiagramControl()
    {
        InitializeComponent();
        _statusBrushes  = Enum.GetValues<WbsStatus>().ToDictionary(s => s, s => (Brush)Resources[$"DiagramStatus{s}"]);
        _secondaryBrush = (Brush)Resources["DiagramSecondaryText"];
        _selectionBrush = (Brush)Resources["DiagramSelection"];
        _connectorBrush = (Brush)Resources["DiagramConnector"];
        _cardBackground = (Brush)Resources["DiagramCardBackground"];
        _onStatusBrush  = (Brush)Resources["DiagramOnStatus"];
        _accentBrush    = (Brush)Resources["DiagramAccent"];
        _onAccentBrush  = (Brush)Resources["DiagramOnAccent"];
        // Cards are built in code with brushes resolved for the current theme, so rebuild on a theme switch.
        ActualThemeChanged += (_, _) => RebuildDiagram();
    }

    /// <summary>Collapses all non-leaf nodes so only root nodes remain visible.</summary>
    public void CollapseAll()
    {
        _collapsedNodes.Clear();
        if (AllNodes is not null)
            _collapsedNodes.UnionWith(AllNodes.Where(n => n.ParentId.HasValue).Select(n => n.ParentId!.Value));
        RebuildDiagram();
    }

    /// <summary>Expands all nodes so the full tree is visible.</summary>
    public void ExpandAll()
    {
        _collapsedNodes.Clear();
        RebuildDiagram();
    }

    private void ToggleCollapse(Guid nodeId)
    {
        if (!_collapsedNodes.Remove(nodeId))
            _collapsedNodes.Add(nodeId);
        RebuildDiagram();
    }

    private static int CountDescendants(DiagramNode node)
        => node.Children.Sum(child => 1 + CountDescendants(child));

    private bool _rebuildQueued;

    // The view model updates several inputs back-to-back; rebuild once after the batch, not per property.
    private void ScheduleRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _rebuildQueued = false;
            RebuildDiagram();
        });
    }

    private void RebuildDiagram()
    {
        _cards.Clear();
        _arrowElements.Clear();
        DiagramCanvas.Children.Clear();
        DiagramCanvas.Width  = _pad * 2;
        DiagramCanvas.Height = _pad * 2;

        if (AllNodes is null || AllNodes.Count == 0) return;

        var hierarchy = _hierarchy = new WbsHierarchy(AllNodes);
        _levels = hierarchy.BranchLevels();
        DiagramNode Build(WbsNode n)
        {
            var node = new DiagramNode { Data = n };
            node.Children.AddRange(hierarchy.ChildrenOf(n.Id).Select(Build));
            return node;
        }
        List<DiagramNode> roots = hierarchy.Roots.Select(Build).ToList();

        foreach (DiagramNode root in roots)
            MeasureSubtree(root);

        double curX = _pad;
        foreach (DiagramNode root in roots)
        {
            PlaceSubtree(root, curX, _pad);
            curX += root.SubtreeWidth + _hGap;
        }

        double maxX = 0, maxY = 0;
        AccumulateBounds(roots, ref maxX, ref maxY);
        DiagramCanvas.Width  = maxX + _pad;
        DiagramCanvas.Height = maxY + _pad;

        // Connectors first so they sit under the cards.
        foreach (DiagramNode root in roots)
            DrawConnectors(root);

        foreach (DiagramNode root in roots)
            DrawCard(root);

        UpdateSelectionHighlight();
    }

    private void MeasureSubtree(DiagramNode node)
    {
        if (node.Children.Count == 0 || _collapsedNodes.Contains(node.Data.Id))
        {
            node.SubtreeWidth = _cardWidth;
            node.ChildLeft    = [];
            node.LeftContour  = [0.0];
            node.RightContour = [_cardWidth];
            return;
        }

        foreach (DiagramNode child in node.Children)
            MeasureSubtree(child);

        // Contour-based packing: for each sibling find the minimum X offset that
        // keeps it clear of ALL previously placed siblings at every depth level.
        double[] childLeft = new double[node.Children.Count];
        childLeft[0] = 0.0;

        for (int i = 1; i < node.Children.Count; i++)
        {
            double[] lc      = node.Children[i].LeftContour;
            double   minLeft = 0.0;

            for (int j = 0; j < i; j++)
            {
                double[] rc    = node.Children[j].RightContour;
                int      depth = Math.Min(rc.Length, lc.Length);
                for (int d = 0; d < depth; d++)
                {
                    double required = childLeft[j] + rc[d] - lc[d] + _hGap;
                    if (required > minLeft) minLeft = required;
                }
            }

            childLeft[i] = minLeft;
        }

        double totalSpan = childLeft[^1] + node.Children[^1].SubtreeWidth;
        node.SubtreeWidth = Math.Max(_cardWidth, totalSpan);

        // If the parent card is wider than the packed children span, centre the children.
        if (node.SubtreeWidth > totalSpan)
        {
            double offset = (node.SubtreeWidth - totalSpan) / 2.0;
            for (int i = 0; i < childLeft.Length; i++)
                childLeft[i] += offset;
            totalSpan = node.SubtreeWidth;
        }

        node.ChildLeft = childLeft;

        // Build the contour for this subtree so the parent can pack it correctly.
        double parentCardLeft  = totalSpan / 2.0 - _cardWidth / 2.0;
        int    maxChildDepth   = node.Children.Max(c => c.LeftContour.Length);
        double[] left  = new double[1 + maxChildDepth];
        double[] right = new double[1 + maxChildDepth];
        left[0]  = parentCardLeft;
        right[0] = parentCardLeft + _cardWidth;

        for (int d = 0; d < maxChildDepth; d++)
        {
            left[d + 1]  = double.MaxValue;
            right[d + 1] = double.MinValue;
            for (int i = 0; i < node.Children.Count; i++)
            {
                if (d < node.Children[i].LeftContour.Length)
                    left[d + 1] = Math.Min(left[d + 1], childLeft[i] + node.Children[i].LeftContour[d]);
                if (d < node.Children[i].RightContour.Length)
                    right[d + 1] = Math.Max(right[d + 1], childLeft[i] + node.Children[i].RightContour[d]);
            }
            // Levels with no children default to the parent card extents.
            if (left[d + 1]  == double.MaxValue)  left[d + 1]  = left[d];
            if (right[d + 1] == double.MinValue) right[d + 1] = right[d];
        }

        node.LeftContour  = left;
        node.RightContour = right;
    }

    private void PlaceSubtree(DiagramNode node, double left, double top)
    {
        node.X = left + (node.SubtreeWidth - _cardWidth) / 2.0;
        node.Y = top;

        if (_collapsedNodes.Contains(node.Data.Id)) return;

        double childTop = top + _cardHeight + _vGap;
        for (int i = 0; i < node.Children.Count; i++)
            PlaceSubtree(node.Children[i], left + node.ChildLeft[i], childTop);
    }

    private void AccumulateBounds(IEnumerable<DiagramNode> nodes, ref double maxX, ref double maxY)
    {
        foreach (DiagramNode n in nodes)
        {
            double r = n.X + _cardWidth;
            double b = n.Y + _cardHeight;
            if (r > maxX) maxX = r;
            if (b > maxY) maxY = b;
            if (!_collapsedNodes.Contains(n.Data.Id))
                AccumulateBounds(n.Children, ref maxX, ref maxY);
        }
    }

    private void DrawConnectors(DiagramNode node)
    {
        if (_collapsedNodes.Contains(node.Data.Id)) return;

        double parentCx     = node.X + _cardWidth / 2.0;
        double parentBottom = node.Y + _cardHeight;

        foreach (DiagramNode child in node.Children)
        {
            double childCx = child.X + _cardWidth / 2.0;
            double elbowY  = parentBottom + _vGap / 2.0;

            var wire = new Polyline
            {
                Stroke          = _connectorBrush,
                StrokeThickness = 1.5,
                StrokeLineJoin  = PenLineJoin.Round,
            };
            wire.Points.Add(new Point(parentCx, parentBottom));
            wire.Points.Add(new Point(parentCx, elbowY));
            wire.Points.Add(new Point(childCx,  elbowY));
            wire.Points.Add(new Point(childCx,  child.Y));
            DiagramCanvas.Children.Add(wire);

            DrawConnectors(child);
        }
    }

    private void DrawCard(DiagramNode node)
    {
        // Always the stored status colour; blocking state is shown by the badge instead.
        Brush statusBrush = _statusBrushes[node.Data.Status];

        var card = new Border
        {
            Width            = _cardWidth,
            Height           = _cardHeight,
            CornerRadius     = new CornerRadius(8),
            BorderThickness  = new Thickness(1.5),
            BorderBrush      = statusBrush,
            Background       = _cardBackground,
            Tag              = node.Data,
            Child            = BuildCardContent(node.Data, statusBrush),
        };
        AutomationProperties.SetName(card, $"{node.Data.Code} — {node.Data.Title}");
        card.PointerPressed += Card_PointerPressed;
        card.Tapped      += Card_Tapped;
        card.RightTapped += Card_RightTapped;
        card.CanDrag     = true;
        card.AllowDrop   = true;
        card.DragStarting += Card_DragStarting;
        card.DragOver    += Card_DragOver;
        card.Drop        += Card_Drop;
        card.DropCompleted += (_, _) => _draggedId = Guid.Empty;

        Canvas.SetLeft(card, node.X);
        Canvas.SetTop(card,  node.Y);
        DiagramCanvas.Children.Add(card);
        _cards[node.Data.Id] = card;

        Guid id = node.Data.Id;
        // Blocked-coloured when the node can't go on, accent-coloured when it only can't finish yet.
        Brush blockedBrush = _statusBrushes[WbsStatus.Blocked];
        (string Glyph, Brush Background, Brush Foreground, string TipKey)? blocking = id switch
        {
            _ when BlockedNodeIds?.Contains(id) == true            => ("\uE83D", blockedBrush, _onStatusBrush, "Diagram_StartBlocked"),
            _ when CompletionBlockedNodeIds?.Contains(id) == true  => ("\uE7BA", _accentBrush, _onAccentBrush, "Diagram_CompletionBlocked"),
            _ when ViolatedCompleteNodeIds?.Contains(id) == true   => ("\uE7BA", _accentBrush, _onAccentBrush, "Diagram_CompleteViolated"),
            _ when ViolatedInProgressNodeIds?.Contains(id) == true => ("\uE7BA", blockedBrush, _onStatusBrush, "Diagram_InProgressViolated"),
            _ => null,
        };
        if (blocking is { } b)
            AddBlockingBadge(node.X, node.Y, b.Glyph, b.Background, b.Foreground, Strings.Get(b.TipKey));

        bool isCollapsed = _collapsedNodes.Contains(node.Data.Id);
        bool hasChildren = node.Children.Count > 0;

        if (hasChildren)
        {
            const double btnSize = 16.0;
            var chevron = new Button
            {
                Width           = btnSize,
                Height          = btnSize,
                Padding         = new Thickness(0),
                Background      = new SolidColorBrush(Windows.UI.Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
                BorderThickness = new Thickness(0),
                Content         = new TextBlock
                {
                    Text                = isCollapsed ? "\uE76C" : "\uE70D",
                    FontFamily          = _iconFont,
                    FontSize            = 9,
                    Foreground          = _onStatusBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment   = VerticalAlignment.Center,
                },
            };
            AutomationProperties.SetName(chevron, Strings.Get(isCollapsed ? "Diagram_ExpandChildren" : "Diagram_CollapseChildren"));
            Guid capturedId = node.Data.Id;
            chevron.Click += (_, _) => ToggleCollapse(capturedId);
            Canvas.SetLeft(chevron, node.X + _cardWidth - btnSize - 4);
            Canvas.SetTop(chevron,  node.Y + (_headerHeight - btnSize) / 2.0);
            Canvas.SetZIndex(chevron, 10);
            DiagramCanvas.Children.Add(chevron);
        }

        if (isCollapsed)
        {
            int hidden = CountDescendants(node);
            var badge = new Border
            {
                Width        = _cardWidth,
                CornerRadius = new CornerRadius(10),
                Background   = new SolidColorBrush(Windows.UI.Color.FromArgb(0x35, 0x80, 0x80, 0x80)),
                Padding      = new Thickness(4, 2, 4, 2),
                Child        = new TextBlock
                {
                    Text                = $"+{hidden}",
                    FontSize            = 10,
                    Foreground          = _secondaryBrush,
                    HorizontalAlignment = HorizontalAlignment.Center,
                },
            };
            ToolTipService.SetToolTip(badge, hidden == 1 ? Strings.Get("Diagram_OneNodeHidden") : Strings.Format("Diagram_NodesHidden", hidden));
            Canvas.SetLeft(badge, node.X);
            Canvas.SetTop(badge,  node.Y + _cardHeight + 8);
            DiagramCanvas.Children.Add(badge);
        }
        else
        {
            foreach (DiagramNode child in node.Children)
                DrawCard(child);
        }
    }

    private void AddBlockingBadge(double cardX, double cardY, string icon, Brush background, Brush foreground, string tooltip)
    {
        const double size = 16.0;
        var badge = new Border
        {
            Width        = size,
            Height       = size,
            CornerRadius = new CornerRadius(size / 2),
            Background   = background,
            Child        = new TextBlock
            {
                Text                = icon,
                FontFamily          = _iconFont,
                FontSize            = 9,
                Foreground          = foreground,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(badge, cardX + _cardWidth  - size / 2);
        Canvas.SetTop(badge,  cardY               - size / 2);
        ToolTipService.SetToolTip(badge, tooltip);
        DiagramCanvas.Children.Add(badge);
    }

    private UIElement BuildCardContent(WbsNode node, Brush headerBrush)
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_headerHeight) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(_footerHeight) });

        var header = new Border
        {
            Background   = headerBrush,
            CornerRadius = new CornerRadius(6.5, 6.5, 0, 0),
            Padding      = new Thickness(8, 0, 8, 0),
        };
        header.Child = new TextBlock
        {
            Text              = node.Code,
            FontWeight        = Microsoft.UI.Text.FontWeights.SemiBold,
            FontSize          = 11,
            Foreground        = _onStatusBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming      = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(header, 0);
        grid.Children.Add(header);

        var body = new Grid { Padding = new Thickness(8, 3, 8, 2), ColumnSpacing = 6 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(new LevelMarker
        {
            Level             = _levels.GetValueOrDefault(node.Id, 1),
            Width             = 10,
            Height            = 10,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var title = new TextBlock
        {
            Text              = node.Title,
            FontSize          = 11,
            TextWrapping      = TextWrapping.Wrap,
            TextTrimming      = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            MaxLines          = 2,
        };
        Grid.SetColumn(title, 1);
        body.Children.Add(title);
        Grid.SetRow(body, 1);
        grid.Children.Add(body);

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
                Foreground        = _secondaryBrush,
                TextTrimming      = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetRow(footer, 2);
            grid.Children.Add(footer);
        }

        return grid;
    }

    private void ClearArrows()
    {
        foreach (UIElement el in _arrowElements)
            DiagramCanvas.Children.Remove(el);
        _arrowElements.Clear();
    }

    /// <summary>
    /// Draws the selected node's dependency arrows only; drawing every dependency at once
    /// across an org-chart layout turns into unreadable routing.
    /// </summary>
    private void DrawDependencyArrows()
    {
        ClearArrows();

        if (Dependencies is not { Count: > 0 } deps) return;
        if (SelectedNodeId == Guid.Empty) return;

        foreach (NodeDependency dep in deps)
        {
            if (dep.PredecessorId != SelectedNodeId && dep.SuccessorId != SelectedNodeId)
                continue;

            if (!_cards.TryGetValue(dep.PredecessorId, out Border? predCard) ||
                !_cards.TryGetValue(dep.SuccessorId,   out Border? succCard))
                continue;

            double predLeft = Canvas.GetLeft(predCard);
            double succLeft = Canvas.GetLeft(succCard);
            double fromY    = Canvas.GetTop(predCard) + _cardHeight / 2.0;
            double toY      = Canvas.GetTop(succCard) + _cardHeight / 2.0;
            double dx       = succLeft - predLeft;

            // Route through the left/right card edges only: top and bottom belong to the
            // parent-child connectors, and sharing them would visually merge the two relationships.
            double fromX, toX, cp1X, cp2X;
            if (Math.Abs(dx) < _cardWidth * 0.5)
            {
                // Same column: loop through the right margin so the curve never crosses the tree lines.
                fromX = predLeft + _cardWidth;
                toX   = succLeft + _cardWidth;
                cp1X  = cp2X = Math.Max(predLeft, succLeft) + _cardWidth + 80.0;
            }
            else
            {
                double dir = Math.Sign(dx);
                fromX = dir > 0 ? predLeft + _cardWidth : predLeft;
                toX   = dir > 0 ? succLeft : succLeft + _cardWidth;
                double off = Math.Max(Math.Abs(dx) * 0.4, 50.0);
                cp1X = fromX + dir * off;
                cp2X = toX   - dir * off;
            }

            bool isFinishConstraint = DependencyConstraints.IsFinishConstraint(dep.Type);

            var figure = new PathFigure { StartPoint = new Point(fromX, fromY), IsClosed = false };
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(cp1X, fromY),
                Point2 = new Point(cp2X, toY),
                Point3 = new Point(toX,  toY),
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);

            var curve = new Microsoft.UI.Xaml.Shapes.Path
            {
                Data            = geometry,
                Stroke          = _connectorBrush,
                StrokeThickness = 2.0,
                Fill            = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
                StrokeDashArray = isFinishConstraint ? new DoubleCollection { 4, 2 } : null,
            };
            ToolTipService.SetToolTip(curve, DisplayText.GetLabel(dep.Type));
            DiagramCanvas.Children.Add(curve);
            _arrowElements.Add(curve);

            // Filled circle at the destination marks the direction of the arrow.
            const double dotR = 4.0;
            var dot = new Ellipse { Width = dotR * 2, Height = dotR * 2, Fill = _connectorBrush };
            Canvas.SetLeft(dot, toX - dotR);
            Canvas.SetTop(dot,  toY - dotR);
            DiagramCanvas.Children.Add(dot);
            _arrowElements.Add(dot);
        }
    }

    private bool _bringSelectionIntoView;

    // The selection can come from outside the diagram (the Outline, a newly added node), so its card may be
    // scrolled away or inside a collapsed branch.
    private void OnSelectedNodeIdChanged()
    {
        _bringSelectionIntoView = SelectedNodeId != Guid.Empty;
        if (ExpandAncestors(SelectedNodeId))
            ScheduleRebuild();
        else
            UpdateSelectionHighlight();
    }

    private bool ExpandAncestors(Guid id)
    {
        Dictionary<Guid, WbsNode> byId = AllNodes?.ToDictionary(n => n.Id) ?? [];
        bool expanded = false;
        for (Guid? parent = byId.GetValueOrDefault(id)?.ParentId; parent is { } p; parent = byId.GetValueOrDefault(p)?.ParentId)
            expanded |= _collapsedNodes.Remove(p);
        return expanded;
    }

    private void UpdateSelectionHighlight()
    {
        SetHighlight(_highlightedId, false);
        SetHighlight(SelectedNodeId, true);
        _highlightedId = SelectedNodeId;
        // The card of a node added a moment ago only exists after the queued rebuild.
        if (_bringSelectionIntoView && _cards.TryGetValue(SelectedNodeId, out Border? card))
        {
            _bringSelectionIntoView = false;
            card.StartBringIntoView();
        }
        DrawDependencyArrows();
    }

    private void SetHighlight(Guid id, bool selected)
    {
        if (!_cards.TryGetValue(id, out Border? card)) return;
        card.BorderThickness = new Thickness(selected ? 2.5 : 1.5);
        card.BorderBrush     = selected ? _selectionBrush : _statusBrushes[((WbsNode)card.Tag).Status];
    }

    // Taking focus on press, before Tapped selects, makes a focused detail-panel text box commit its
    // edit (on LostFocus) to the node it belongs to.
    private void Card_PointerPressed(object sender, PointerRoutedEventArgs e)
        => DiagramScroller.Focus(FocusState.Pointer);

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

    private void Card_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        if (sender is not Border { Tag: WbsNode node } || _hierarchy is null) return;
        _draggedId = node.Id;
        _noDropIds = [.. _hierarchy.CollectSubtree(node).Select(n => n.Id)];
        if (node.ParentId is { } parent)
            _noDropIds.Add(parent);
        args.Data.SetText(node.ToString());
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }

    private bool CanDropOn(WbsNode target) => _draggedId != Guid.Empty && !_noDropIds.Contains(target.Id);

    private void Card_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: WbsNode target } || !CanDropOn(target)) return;
        e.AcceptedOperation = DataPackageOperation.Move;
        e.DragUIOverride.Caption = Strings.Format("Diagram_DropUnder", target);
    }

    private void Card_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Border { Tag: WbsNode target } || !CanDropOn(target)) return;
        NodeDropped?.Invoke(this, (_draggedId, target.Id));
        _draggedId = Guid.Empty;
    }

    /// <summary>Arrow keys move the selection: Up to the parent, Down to the first visible child, Left/Right between siblings.</summary>
    private void DiagramScroller_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Alt+arrows move the node itself; the toolbar's shortcuts handle those.
        if (_hierarchy is null || AllNodes is null || e.KeyStatus.IsMenuKeyDown) return;

        WbsNode? current = AllNodes.FirstOrDefault(n => n.Id == SelectedNodeId);
        WbsNode? target;
        if (current is null)
        {
            target = e.Key is VirtualKey.Up or VirtualKey.Down or VirtualKey.Left or VirtualKey.Right
                ? _hierarchy.DepthFirst().Select(entry => entry.Node).FirstOrDefault()
                : null;
        }
        else
        {
            IReadOnlyList<WbsNode> siblings = current.ParentId is { } parentId
                ? _hierarchy.ChildrenOf(parentId)
                : _hierarchy.Roots;
            int index = siblings.Select(n => n.Id).ToList().IndexOf(current.Id);
            target = e.Key switch
            {
                VirtualKey.Up    => AllNodes.FirstOrDefault(n => n.Id == current.ParentId),
                VirtualKey.Down  => _collapsedNodes.Contains(current.Id) ? null : _hierarchy.ChildrenOf(current.Id).FirstOrDefault(),
                VirtualKey.Left  => index > 0 ? siblings[index - 1] : null,
                VirtualKey.Right => index < siblings.Count - 1 ? siblings[index + 1] : null,
                _ => null
            };
        }

        if (target is null || !_cards.ContainsKey(target.Id)) return;
        e.Handled = true;
        NodeSelected?.Invoke(this, target);
    }
}
