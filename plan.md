# iscWBS — Implementation Plan

## Project Overview

**iscWBS** is a Windows desktop application (WinUI 3, .NET 10, packaged MSIX)

---

## Target Folder Structure

```
iscWBS/
├── Assets/
├── Core/
│   ├── Models/             # Plain domain entities + enums
│   ├── Exceptions/         # WbsException hierarchy
│   ├── Services/           # Interfaces and implementations (same folder)
│   └── Repositories/       # SQLite data access
├── ViewModels/             # One ViewModel per page + ShellViewModel
├── Views/
│   └── Controls/           # Reusable UserControls
├── Converters/             # IValueConverter implementations
├── Helpers/                # Static utility classes
└── Themes/
    └── Generic.xaml        # Merged ResourceDictionary
```

---

## NuGet Packages to Add

| Package | Purpose |
|---|---|
| `CommunityToolkit.Mvvm` | `ObservableObject`, `[RelayCommand]`, `[ObservableProperty]` |
| `CommunityToolkit.WinUI.Controls.DataGrid` | Data grid for WBS outline view |
| `LiveChartsCore.SkiaSharpView.WinUI` | Dashboard and report charts |
| `sqlite-net-pcl` | Local SQLite persistence |
| `SQLitePCLRaw.bundle_green` | SQLite native bindings |
| `Microsoft.Extensions.DependencyInjection` | DI container in `App.xaml.cs` |

---

## Domain Models (`Core/Models/`)

### `WbsNode`
| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `ProjectId` | `Guid` | Foreign key |
| `ParentId` | `Guid?` | `null` = root node |
| `Code` | `string` | Auto-generated, e.g. `"1.2.3"` |
| `Title` | `string` | Display name |
| `Description` | `string` | Detail notes |
| `AssignedTo` | `string` | Responsible person |
| `Status` | `WbsStatus` | `NotStarted / InProgress / Complete / Blocked` |
| `EstimatedHours` | `double` | |
| `ActualHours` | `double` | |
| `EstimatedCost` | `decimal` | |
| `ActualCost` | `decimal` | |
| `StartDate` | `DateTimeOffset?` | |
| `DueDate` | `DateTimeOffset?` | |
| `SortOrder` | `int` | Sibling ordering |

### `Project`
| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | Primary key |
| `Name` | `string` | |
| `Description` | `string` | |
| `Owner` | `string` | |
| `CreatedAt` | `DateTimeOffset` | |
| `Currency` | `string` | Default `"USD"` |
| `StartDate` | `DateTimeOffset?` | If null, `GanttViewModel` derives as min `StartDate` across all nodes |

### `Milestone`
| Property | Type |
|---|---|
| `Id` | `Guid` |
| `ProjectId` | `Guid` |
| `Title` | `string` |
| `DueDate` | `DateTimeOffset` |
| `IsComplete` | `bool` |
| `LinkedNodeIds` | `string` | JSON-serialised `List<Guid>` |

### `NodeDependency`
| Property | Type | Notes |
|---|---|---|
| `PredecessorId` | `Guid` | |
| `SuccessorId` | `Guid` | |
| `Type` | `DependencyType` | `FinishToStart / StartToStart / FinishToFinish` |

---

## Database Schema (`WbsDatabase`)

One `.iscwbs` SQLite file per project. `WbsDatabase` calls `SQLiteAsyncConnection.CreateTableAsync<T>()` on first open. Migrations tracked via a `SchemaVersion` table; run automatically when the database is opened.

### Tables

| Table | Primary Key | Notes |
|---|---|---|
| `SchemaVersion` | `Version INTEGER` | One row per migration; insert on each schema change |
| `Projects` | `Id TEXT` | Includes `StartDate TEXT` (nullable ISO 8601) |
| `WbsNodes` | `Id TEXT` | `ParentId` null = root node; `SortOrder` is 0-based within sibling group |
| `Milestones` | `Id TEXT` | `LinkedNodeIds` stored as JSON array string |
| `NodeDependencies` | `(PredecessorId TEXT, SuccessorId TEXT)` | Composite primary key |

### C# → SQLite type mappings (sqlite-net-pcl)

| C# type | SQLite storage |
|---|---|
| `Guid` | `TEXT` (UUID string) |
| `DateTimeOffset?` | `TEXT` (ISO 8601, nullable) |
| `decimal` | `REAL` |
| `enum` | `INTEGER` |
| `bool` | `INTEGER` (`0` / `1`) |

### Indices
- `WbsNodes`: index on `(ProjectId, ParentId)` — used by `GetChildrenAsync` and `GetRootNodesAsync`
- `Milestones`: index on `ProjectId`

---

## Service Interfaces (`Core/Services/`)

```
IProjectStateService  — owns WbsDatabase; open/create/close active project; fires ActiveProjectChanged
ISettingsService      — wraps ApplicationData.LocalSettings; recent projects, theme, preferences
IWbsService           — tree operations: add, remove, move, reorder, auto-recode
IMilestoneService     — CRUD for Milestone
INavigationService    — Frame-based page navigation from ViewModels
IDialogService        — ContentDialog host; Initialize(XamlRoot) called once by ShellWindow
IExportService        — Excel and PDF export (Phase 4)
```

---

## Phase 1 — Foundation
**Goal:** Runnable shell with navigation, DI, and database initialised.

- [x] Add all NuGet packages to `iscWBS.csproj`
- [ ] Create folder structure under project root
- [ ] `Core/Exceptions/` — `WbsException` (base), `WbsNotFoundException`, `WbsValidationException`, `ProjectNotFoundException`
- [ ] `Core/Models/` — `WbsNode`, `Project`, `Milestone`, `NodeDependency`, `WbsStatus` enum, `DependencyType` enum
- [ ] `Core/Repositories/` — `WbsDatabase` (takes `filePath`; created by `IProjectStateService`), `ProjectRepository`, `WbsNodeRepository`
- [ ] `Core/Services/` — all interfaces and implementations
  - `IProjectStateService` / `ProjectStateService` — owns `WbsDatabase`, fires `ActiveProjectChanged`
  - `ISettingsService` / `SettingsService` — wraps `ApplicationData.LocalSettings`
  - `INavigationService` / `NavigationService` — `Initialize(Frame frame)` called from `ShellWindow.Loaded`
  - `IDialogService` / `DialogService` — `Initialize(XamlRoot)` called from `ShellWindow.Loaded`
  - `IWbsService` / `WbsService` — stub implementation (full logic in Phase 2)
- [ ] Configure DI in `App.xaml.cs`
  - Call `QuestPDF.Settings.License = LicenseType.Community` before building the container
  - Singleton: all services (`WbsDatabase` is **not** registered — owned by `IProjectStateService`)
  - Transient: all ViewModels
  - `App.Services` exposed as `static IServiceProvider`
- [ ] Replace `MainWindow` with `ShellWindow`
  - `NavigationView` (left-rail, compact) with items: Dashboard, WBS Tree, WBS Outline, Gantt, Reports + Settings at footer
  - Nav items disabled while `IProjectStateService.HasActiveProject` is `false`
  - Custom title bar via `AppWindow.TitleBar` showing `iscWBS — {ProjectName}`
  - `ShellWindow.Loaded` calls `INavigationService.Initialize(contentFrame)`, `IDialogService.Initialize(XamlRoot)`, then checks recent projects
  - Navigates to `WelcomePage` if no recent project; navigates to `DashboardPage` when project opens
- [ ] Stub all navigation-target pages — empty `Grid` with a centred `TextBlock` title:
  `DashboardPage`, `WbsTreePage`, `WbsOutlinePage`, `GanttPage`, `ReportsPage`, `SettingsPage`
- [ ] `WelcomePage` — startup target when no project is open
  - Not a NavigationView item
  - **New Project** → `ContentDialog` via `IDialogService` collecting: Name (required), Owner, Currency (default `"USD"`), save location via `FolderPicker`; creates `<Name>.iscwbs` via `IProjectStateService.CreateProjectAsync`
  - **Open Project** → `FileOpenPicker` filtered to `*.iscwbs`; calls `IProjectStateService.OpenProjectAsync`
  - **Recent Projects** → scrollable list from `ISettingsService.GetRecentProjects()`; click opens directly
- [ ] `Helpers/SettingsKeys.cs` — `static class` with `const string` keys used by `ISettingsService`: `RecentProjects`, `Theme`, `Currency`, `DateFormat`, `WindowBounds`
- [ ] `ShellViewModel` — subscribes to `ActiveProjectChanged`, toggles nav items, updates title

---

## Phase 2 — WBS Editor
**Goal:** Full CRUD and reorder of the WBS tree with lazy-loaded tree view.

### Key type: `WbsNodeViewModel`
A `WbsNodeViewModel` (in `ViewModels/`) wraps a `WbsNode` for the `TreeView`:
- `ObservableCollection<WbsNodeViewModel> Children` — lazily populated on first expand
- `bool IsExpanded`, `bool IsSelected`, `bool IsEditing` — observable state
- On construction, if the node has children a **placeholder child** (`WbsNodeViewModel.Placeholder`) is inserted so the `TreeView` renders an expand arrow before real children load

### `IWbsService` method surface

```csharp
Task<IReadOnlyList<WbsNode>> GetRootNodesAsync(Guid projectId);
Task<IReadOnlyList<WbsNode>> GetChildrenAsync(Guid parentId);
Task<WbsNode> AddChildNodeAsync(Guid parentId, string title);
Task<WbsNode> AddSiblingNodeAsync(Guid siblingId, string title);
Task UpdateNodeAsync(WbsNode node);
Task DeleteNodeAsync(Guid id);           // recursive — deletes all descendants
Task MoveNodeAsync(Guid nodeId, Guid? newParentId, int newSortOrder);
```

Every mutating method recalculates WBS codes for affected siblings via a private `RecodeAsync` helper.

### WBS code generation rules
- Root nodes by `SortOrder`: `1`, `2`, `3`
- Children append dot-index: `1.1`, `1.2`; grandchildren: `1.1.1`, `1.1.2`
- Re-run after every add, delete, move, or reorder within a sibling group

### Lazy loading
- `WbsTreePage` code-behind handles `TreeView.Expanding`, passing the expanded `WbsNodeViewModel` to `WbsTreeViewModel.ExpandNodeCommand`
- Command calls `IWbsService.GetChildrenAsync`, removes the placeholder, and populates `Children`

### Checklist

- [ ] `WbsNodeViewModel` with placeholder-child lazy loading pattern
- [ ] `IWbsService` + `WbsService` — full implementation with `SortOrder` maintenance and WBS code generation
- [ ] `WbsTreePage` + `WbsTreeViewModel`
  - Page layout: two-column `Grid` — `TreeView` (left, `*` min 240px) + `GridSplitter` + `WbsDetailControl` (right, 320px fixed; collapses when no node is selected)
  - Root `TreeView` bound to `ObservableCollection<WbsNodeViewModel>` (root nodes only initially)
  - `TreeView.Expanding` code-behind handler delegates to `ExpandNodeCommand`
  - Inline title editing: `IsEditing` toggles `TextBox` / `TextBlock` via `x:Bind`
  - Context menu per node: **Add Child**, **Add Sibling**, **Edit**, **Delete**, **Move Up**, **Move Down**
- [ ] `WbsDetailControl` (`UserControl`) — side panel bound to `WbsTreeViewModel.SelectedNode`
  - Editable fields (`TwoWay`): Title, Description, AssignedTo, Status, EstimatedHours, ActualHours, EstimatedCost, ActualCost, StartDate, DueDate
  - **Save** calls `IWbsService.UpdateNodeAsync`; **Cancel** reverts changes
- [ ] Drag-and-drop reordering — `TreeView` drag events in code-behind delegate to `MoveNodeCommand`
- [ ] `WbsOutlinePage` + `WbsOutlineViewModel`
  - `DataGrid` flat list of all nodes sorted by `Code`
  - Columns: Code, Title, AssignedTo, Status, Est. Hours, Act. Hours, Est. Cost, Act. Cost, Due Date
  - Row click navigates to the node in `WbsTreePage`

---

## Phase 3 — Dashboard & Visualisations
**Goal:** Data-driven overview of any open project using LiveCharts2.

### LiveCharts2 control mapping

| Chart | XAML control | ViewModel series type |
|---|---|---|
| Status donut | `<lvc:PieChart>` | `ISeries[]` — 4 `PieSeries<ObservableValue>` slices |
| Cost comparison | `<lvc:CartesianChart>` | `ISeries[]` — 2 `ColumnSeries<double>` (estimated, actual) |
| Effort per assignee | `<lvc:CartesianChart>` | `ISeries[]` — one `ColumnSeries<double>` per assignee |
| Progress over time | `<lvc:CartesianChart>` | `ISeries[]` — `LineSeries<DateTimePoint>` |
| Burn-down | `<lvc:CartesianChart>` | `ISeries[]` — 2 `LineSeries<DateTimePoint>` (remaining, ideal) |

All series are `ISeries[]` observable properties on the ViewModel, rebuilt on `ActiveProjectChanged`.

### Gantt canvas layout
- Row height: `48px`; canvas height = `nodeCount × 48`
- `pixelsPerDay` by zoom level: **Day** = `40`, **Week** = `8`, **Month** = `2`
- Bar `Canvas.Left` = `(StartDate − projectStart).TotalDays × pixelsPerDay`
- Bar `Canvas.Width` = `(DueDate − StartDate).TotalDays × pixelsPerDay`
- Today line: vertical `Line` at `(Today − projectStart).TotalDays × pixelsPerDay`
- Dependency arrows: `Path` elements from bar right-edge to target bar left-edge
- Milestone: `Rectangle` rotated 45° centred at `DueDate` x-position

### Checklist

- [ ] `DashboardPage` + `DashboardViewModel` — replaces Phase 1 stub
  - All data reloaded in `INavigationAware.OnNavigatedTo` — always reflects the latest saved state
  - 4 KPI `Border` cards in a `Grid` row (total nodes, % complete, budget variance, overdue count)
  - `PieChart` status donut
  - `CartesianChart` cost comparison bar (estimated vs actual per top-level branch)
  - `CartesianChart` effort-per-assignee bar
  - Upcoming milestones `ListView` (next 30 days, sorted by `DueDate`)
- [ ] `GanttPage` + `GanttViewModel`
  - `Canvas` inside `ScrollViewer` (both axes scrollable)
  - `projectStart` = `Project.StartDate` if set; otherwise derived as `WbsNodes.Min(n => n.StartDate)`
  - Nodes without both `StartDate` and `DueDate`: render label row greyed out with "Unscheduled" text, no bar
  - Zoom toolbar (`RadioButtons`): Day / Week / Month — recalculates `pixelsPerDay` and redraws all elements
  - Today line, milestone diamonds, dependency path arrows
- [ ] `ReportsPage` + `ReportsViewModel`
  - Progress line chart and burn-down line chart via `CartesianChart`
  - Filter controls: date range `CalendarDatePicker`, assignee `ComboBox`, status `ComboBox`
- [ ] `WbsDetailControl` — add **Dependencies** tab (Phase 3 addition to existing control)
  - Lists `NodeDependency` records for the selected node
  - Add dependency: searchable node picker + `DependencyType` selector
  - Remove dependency: delete button per row with confirm dialog

---

## Phase 4 — Milestones, Export & Polish
**Goal:** Complete feature set, export capability, and production-ready UX.

- [ ] `MilestonesPage` + `MilestonesViewModel`
  - List of milestones with status badges
  - Link milestones to one or more `WbsNode` records
  - Mark complete with confirmation dialog
- [ ] `IExportService` implementations
  - **Excel** (`ClosedXML`):
    - Sheet 1 — **Summary**: project name, owner, currency, total estimated/actual cost and hours
    - Sheet 2 — **WBS**: all nodes; columns: Code, Title, AssignedTo, Status, Est. Hours, Act. Hours, Est. Cost, Act. Cost, StartDate, DueDate; parent rows bold, rows colour-coded by status
  - **PDF** (`QuestPDF`):
    - Page 1 — Cover: project name, owner, export date
    - Page 2 — WBS table (same columns as Excel sheet 2)
    - Page 3 — Summary charts rendered via `SkiaSharp` as bitmaps
- [ ] WBS Diagram view (`WbsDiagramControl`)
  - `Canvas`-drawn top-down tree using recursive post-order layout:
    - Leaf node width = `160px`; parent width = sum of children widths + `16px` gaps
    - Each node centred over its children span; level y-position = `depth × 120px`
  - Nodes as `Border` elements (rounded, `60px` height) with `Code` + `Title` labels
  - Connector lines between parents and children
  - Pan via pointer drag on `Canvas`; zoom via `ScrollViewer` + `Ctrl+Scroll`
  - Clicking a node navigates to it in `WbsTreePage`
- [ ] `SettingsPage` + `SettingsViewModel`
  - Theme selector (Light / Dark / System) — sets `RequestedTheme` on `ShellWindow`'s root `Grid` element; `Application.RequestedTheme` is read-only after launch
  - Default currency and date format (persisted via `ISettingsService`)
  - Recent projects list with **Open** and **Remove** actions
- [ ] Keyboard shortcuts via `KeyboardAccelerator` on `WbsTreePage`:
  - `Ins` → Add child node
  - `Del` → Delete selected node (triggers confirm dialog)
  - `F2` → Begin inline edit on selected node
  - Global shortcuts handled in `ShellWindow.ProcessKeyboardAccelerators`
- [ ] `Helpers/Logger.cs` — static `Write(Exception ex)` helper; appends to `%LOCALAPPDATA%\ISC\iscWBS\Logs\log-{date}.txt`; called from `App.xaml.cs UnhandledException` handler
- [ ] Accessibility: ensure all interactive elements have `AutomationProperties.Name`

---

## Data Storage

- One **SQLite database file per project**: `<ProjectName>.iscwbs`
- Stored in `%LOCALAPPDATA%\ISC\iscWBS\Projects\` by default (user-configurable)
- Recent project paths persisted to Windows `ApplicationData.LocalSettings`
- Database schema versioned via a `SchemaVersion` table; migrations run on open

---

## Testing Milestones

| Milestone | Criteria |
|---|---|
| Phase 1 complete | App launches, `ShellWindow` shows, navigation works |
| Phase 2 complete | Full WBS tree CRUD round-trips to SQLite |
| Phase 3 complete | Dashboard and Gantt render live data |
| Phase 4 complete | Excel/PDF export produces valid files |
