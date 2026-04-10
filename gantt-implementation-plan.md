# Gantt View — Implementation Plan

## Overview

Seven sequential steps to produce a fully functional Gantt chart with a timeline ruler, progress fills, milestone diamonds, dependency arrows, and a matching Linked Milestones section in the WBS detail panel.

---

## Step 1 — Data layer: project-wide dependency query

`NodeDependencyRepository` only exposes `GetBySuccessorAsync` (per-node). The Gantt needs all arrows for a project in a single call.

**Files changed**

| File | Change |
|---|---|
| `Core/Repositories/NodeDependencyRepository.cs` | Add `GetAllByProjectAsync(Guid projectId)` via a raw SQL join on `WbsNodes` filtered by `ProjectId` |
| `Core/Services/IWbsService.cs` | Add `GetAllDependenciesByProjectAsync(Guid projectId)` |
| `Core/Services/WbsService.cs` | Implement the new method by delegating to the repository |

---

## Step 2 — `GanttRowViewModel`: new fields

| Field | Type | Purpose |
|---|---|---|
| `NodeId` | `Guid` | Maps rows to dependency arrow endpoints |
| `Depth` | `int` | Label left-indent (`Depth × 12 px`) |
| `IsParent` | `bool` | Optional hollow/summary bar style |
| `AssignedTo` | `string` | Small text rendered beneath the bar |
| `PercentComplete` | `double` | `ActualHours / EstimatedHours` (0–1); drives progress fill |

**File changed:** `ViewModels/GanttRowViewModel.cs`

---

## Step 3 — Three new lightweight record types

All in `ViewModels/`.

| File | Record | Properties |
|---|---|---|
| `GanttHeaderTick.cs` | `GanttHeaderTick` | `double X`, `string Label` |
| `GanttMilestoneMarker.cs` | `GanttMilestoneMarker` | `string Title`, `double X`, `bool IsComplete` |
| `GanttDependencyArrow.cs` | `GanttDependencyArrow` | `double FromX`, `double FromY`, `double ToX`, `double ToY` |

These are pre-computed pixel coordinates handed to `GanttPage` for rendering — the ViewModel does all the geometry, the view only draws.

---

## Step 4 — `GanttViewModel` overhaul

**New dependency:** inject `IMilestoneService` (already registered as Singleton — no `App.xaml.cs` changes needed).

**Changes to `RebuildRows()`**

Replace the flat `for` loop with a depth-first tree walk:
1. Group all nodes into `Dictionary<Guid?, List<WbsNode>>` by `ParentId`
2. Recurse from roots, sorted by `SortOrder` at each level
3. Track `depth` through recursion — stamp `Depth` and `IsParent` on each `GanttRowViewModel`

**New constant:** `public const double HeaderHeight = 40` — every `rowTop` is offset by `HeaderHeight` so rows begin below the ruler.

**Three new builder methods**

| Method | Output property | Logic |
|---|---|---|
| `BuildHeaderTicks()` | `IReadOnlyList<GanttHeaderTick> HeaderTicks` | Day zoom → one tick per day; Week → every Monday; Month → 1st of each month. Label format matches zoom. |
| `BuildMilestoneMarkers()` | `IReadOnlyList<GanttMilestoneMarker> MilestoneMarkers` | Loads milestones via `IMilestoneService.GetByProjectAsync`, converts `DueDate → X` using `(date − _projectStart).TotalDays × ppd` |
| `BuildDependencyArrows()` | `IReadOnlyList<GanttDependencyArrow> DependencyArrows` | Calls `GetAllDependenciesByProjectAsync`, looks up predecessor row → `BarLeft + BarWidth` (right edge) and `RowTop + barMidY`; successor row → `BarLeft` (left edge) and `RowTop + barMidY` |

`LoadAsync` calls all three builders after `RebuildRows()`. `OnSelectedZoomIndexChanged` rebuilds everything.

**File changed:** `ViewModels/GanttViewModel.cs`

---

## Step 5 — `GanttPage` code-behind: split `DrawGantt()`

Replace the single method body with five focused sub-methods called in order.

| Sub-method | Draws |
|---|---|
| `DrawHeader()` | Iterates `HeaderTicks`: vertical `Line` dividers at `_labelWidth + tick.X`; `TextBlock` date labels in the `HeaderHeight` strip; horizontal baseline `Line` |
| `DrawRows()` | Existing row/label/bar logic **plus** `Depth × 12` px label indent; a second lighter `Rectangle` inside the bar at `PercentComplete × BarWidth`; `AssignedTo` `TextBlock` (11 px) below bar |
| `DrawMilestones()` | Iterates `MilestoneMarkers`: a `Rectangle` with `RenderTransform = RotateTransform(45°)` positioned in the header strip; complete → green tint, incomplete → blue tint; `ToolTipService.SetToolTip` with title |
| `DrawDependencies()` | Iterates `DependencyArrows`: a 3-segment elbow `Polyline` (right → vertical → left) in a muted stroke |
| `DrawTodayLine()` | Existing logic, no change |

**File changed:** `Views/GanttPage.xaml.cs`

---

## Step 6 — `WbsTreeViewModel`: Linked Milestones

**New dependency:** inject `IMilestoneService`.

**New members**

| Member | Type | Purpose |
|---|---|---|
| `LinkedMilestones` | `ObservableCollection<MilestoneRowViewModel>` | Milestones that link to the currently selected node |

**`LoadLinkedMilestonesAsync(Guid nodeId)`**

1. Call `IMilestoneService.GetByProjectAsync(projectId)`
2. Deserialize each `Milestone.LinkedNodeIds` (JSON array of `Guid`)
3. Filter to milestones that contain `nodeId`
4. Build `MilestoneRowViewModel` items and populate `LinkedMilestones`

Called from `OnSelectedNodeChanged` alongside the existing dependency load.

**File changed:** `ViewModels/WbsTreeViewModel.cs`

---

## Step 7 — `WbsDetailControl.xaml`: Linked Milestones section

After the existing "Predecessors" section, append:

- `Rectangle` divider (1 px, `DividerStrokeColorDefaultBrush`)
- `TextBlock` heading "Linked Milestones" (`SubtitleTextBlockStyle`)
- `ItemsControl` bound to `ViewModel.LinkedMilestones`
  - Each item: `FontIcon` (✔ complete / ⚠ overdue), milestone `Title`, `DueDateLabel`

**File changed:** `Views/Controls/WbsDetailControl.xaml`

---

## Change surface summary

| # | File | Nature of change |
|---|---|---|
| 1 | `Core/Repositories/NodeDependencyRepository.cs` | + `GetAllByProjectAsync` |
| 2 | `Core/Services/IWbsService.cs` | + `GetAllDependenciesByProjectAsync` |
| 3 | `Core/Services/WbsService.cs` | + implementation |
| 4 | `ViewModels/GanttRowViewModel.cs` | + 5 fields |
| 5 | `ViewModels/GanttHeaderTick.cs` | new record |
| 6 | `ViewModels/GanttMilestoneMarker.cs` | new record |
| 7 | `ViewModels/GanttDependencyArrow.cs` | new record |
| 8 | `ViewModels/GanttViewModel.cs` | inject service, tree walk, 3 builder methods, 3 new properties, `HeaderHeight` |
| 9 | `Views/GanttPage.xaml.cs` | split `DrawGantt()` into 5 sub-methods |
| 10 | `ViewModels/WbsTreeViewModel.cs` | inject service, `LinkedMilestones`, `LoadLinkedMilestonesAsync` |
| 11 | `Views/Controls/WbsDetailControl.xaml` | + Linked Milestones section |
