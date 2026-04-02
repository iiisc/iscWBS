# iscWBS — Testing & Bug-Fix Plan

## Current Plan Status

| Phase | Complete? | Notes |
|---|---|---|
| Phase 1 — Foundation | ✅ | |
| Phase 2 — WBS Editor | ✅ | Drag-and-drop intentionally deferred |
| Phase 3 — Dashboard & Visualisations | ✅ | |
| Phase 4 — Milestones | ✅ | |
| Phase 4 — IExportService (Excel + PDF) | ❌ | Not started |
| Phase 4 — WbsDiagramControl | ❌ | Not started |
| Phase 4 — SettingsPage | ❌ | Stub only |
| Phase 4 — Keyboard shortcuts | ❌ | Not started |
| Phase 4 — Accessibility pass | ❌ | Not started |

---

## Part 1 — Known Bugs (Fix Before Testing)

### B1 — NodeDependency records orphaned on WbsNode delete  🔴 High
**File:** `Core/Services/WbsService.cs` → `DeleteRecursiveAsync`  
**Problem:** When a node is deleted, any `NodeDependency` rows where it appears as `PredecessorId` OR `SuccessorId` are left in the database. Re-opening the same project will show phantom dependencies pointing to non-existent nodes.  
**Fix needed:** Add `NodeDependencyRepository` to `WbsService`; in `DeleteRecursiveAsync`, delete the node's dependency rows before deleting the node itself.

```csharp
// Before Connection.DeleteAsync call in DeleteRecursiveAsync:
await _dependencyRepository.DeleteByNodeAsync(nodeId);
// Add DeleteByNodeAsync to NodeDependencyRepository:
// DELETE FROM NodeDependencies WHERE PredecessorId = ? OR SuccessorId = ?
```

---

### B2 — `WbsNode.EstimatedCost` / `ActualCost` declared as `double` not `decimal`  🟡 Medium
**File:** `Core/Models/WbsNode.cs`  
**Problem:** The plan specifies `decimal` for cost fields; the model uses `double`. `NumberBox.Value` is also `double` so binding works, but floating-point arithmetic will cause rounding errors in financial totals on the Dashboard.  
**Fix needed:** Change the fields to `decimal` on the model; add a `NumberBox`→`decimal` conversion layer in the ViewModel (or keep `double` on the ViewModel and cast when persisting). Simplest fix: keep `double` throughout and accept ±0.01 rounding in totals — document the decision.

---

### B3 — `MilestonesPage` selection lost after Save  🟡 Medium
**File:** `ViewModels/MilestonesViewModel.cs` → `SaveEditAsync`  
**Problem:** `SaveEditAsync` calls `LoadMilestonesAsync()` which clears and rebuilds the `Milestones` collection. `MilestoneRowViewModel` uses reference equality, so the ListView deselects and the detail panel disappears even though the user just saved an edit.  
**Fix needed:** Store the edited milestone's `Id`, then re-select the matching row after reload:

```csharp
[RelayCommand]
private async Task SaveEditAsync()
{
    if (SelectedMilestone is null || string.IsNullOrWhiteSpace(EditTitle)) return;
    Guid savedId = SelectedMilestone.Milestone.Id;
    // ... update and save ...
    await LoadMilestonesAsync();
    SelectedMilestone = Milestones.FirstOrDefault(r => r.Milestone.Id == savedId);
}
```

---

### B4 — `WbsNode.StartDate` / `DueDate` stored as `DateTime` but treated inconsistently  🟡 Medium
**File:** `Core/Models/WbsNode.cs`, `ViewModels/WbsTreeViewModel.cs`  
**Problem:** The model stores `DateTime?` (SQLite TEXT via sqlite-net-pcl). The ViewModel round-trips via `DateTimeOffset` → `UtcDateTime`. If the user's machine is not UTC, `DateTime.SpecifyKind(value, DateTimeKind.Utc)` will produce the correct UTC offset, but only if the database actually stored UTC. First save is safe; a value loaded back from the database has `DateTimeKind.Unspecified` (sqlite-net-pcl does not preserve Kind), so a second load→edit→save cycle may drift.  
**Fix needed:** Always call `DateTime.SpecifyKind(date, DateTimeKind.Utc)` when reading from the database (already done in `LoadEditFieldsFromNode`) — ensure the same is applied in `GanttViewModel` and `DashboardViewModel` wherever dates are read directly from `WbsNode`.

---

### B5 — Dependencies tab shows only "successor" side; no cleanup on project reload  🟡 Medium
**File:** `NodeDependencyRepository.GetBySuccessorAsync`  
**Problem:** The Dependencies tab is designed to show predecessors (nodes that must come before the selected node). If a predecessor node is later deleted (**B1** above), the tab will show `"? (unknown)"` entries. This is a cosmetic bug that disappears once B1 is fixed.

---

### B6 — `WbsOutlinePage` row-click navigation to `WbsTreePage`  🟡 Medium — needs verification
**File:** `ViewModels/WbsOutlineViewModel.cs`  
**Risk:** The plan says row click navigates to the node in `WbsTreePage`. Verify that the navigation service call and node-selection handoff via the `parameter` argument actually works — `WbsTreeViewModel.OnNavigatedTo` must receive the node ID and scroll/select it. Check whether this was implemented or whether the row click only navigates to the page without selecting the node.

---

### B7 — `Milestone.DueDate` persisted as `DateTime` but `IsOverdue` compares UTC  🟢 Low
**File:** `ViewModels/MilestoneRowViewModel.cs`  
**Problem:** `IsOverdue` computes `Milestone.DueDate < DateTime.UtcNow`. Since `WbsDatabase` is created with `storeDateTimeAsTicks: false`, dates are stored as ISO text. sqlite-net-pcl reads them back as `DateTimeKind.Unspecified`. The comparison against `UtcNow` may be off by the local UTC offset (e.g. a milestone due at midnight UTC shows as overdue at 10 pm the day before in UTC+2).  
**Fix needed:** `return !Milestone.IsComplete && DateTime.SpecifyKind(Milestone.DueDate, DateTimeKind.Utc) < DateTime.UtcNow;`

---

## Part 2 — Feature Test Checklist

Run these manually in the order listed. Each test assumes the previous group passed.

---

### T1 — App Startup

| # | Action | Expected |
|---|---|---|
| T1.1 | Launch the app cold (no recent projects) | `WelcomePage` shown; all nav items disabled |
| T1.2 | Launch after a project was previously opened | Project opens automatically; `DashboardPage` shown; all nav items enabled |
| T1.3 | Kill and relaunch while a project is open | Same as T1.2 — recent project restored |
| T1.4 | Delete the `.iscwbs` file, then launch | Error dialog shown; recent project entry removed; `WelcomePage` shown |

---

### T2 — Project Lifecycle

| # | Action | Expected |
|---|---|---|
| T2.1 | Click **New Project**, fill Name + Owner + Currency, choose a folder | File `<Name>.iscwbs` created; `DashboardPage` shown; title bar shows `iscWBS — <Name>` |
| T2.2 | Click **New Project** without filling the Name field | Add button disabled or validation error shown; no file created |
| T2.3 | Click **Open Project**, browse to an existing `.iscwbs` file | Project loads; nav items enabled |
| T2.4 | Click a recent project in the list | Same as T2.3 |
| T2.5 | Open project A, then open project B | Project A is closed cleanly (no SQLite connection leak); project B loads correctly |

---

### T3 — WBS Tree CRUD

| # | Action | Expected |
|---|---|---|
| T3.1 | Click **Add Root Node** | Node `1` appears in the tree with title "New Node" |
| T3.2 | Add two more root nodes | Codes update to `1`, `2`, `3` |
| T3.3 | Right-click node `1` → **Add Child** | Node `1.1` appears as child of `1` |
| T3.4 | Right-click node `1.1` → **Add Sibling** | Node `1.2` appears after `1.1` |
| T3.5 | Right-click → **Edit Title**; type a new name; press Enter / click away | Title updated in tree; WBS code unchanged |
| T3.6 | Right-click → **Move Down** on node `1` | Node previously at `1` is now `2`; sibling codes update |
| T3.7 | Right-click → **Delete** on a leaf node | Confirm dialog; node removed; sibling codes recalculate |
| T3.8 | Delete a parent node that has children | All descendants deleted; codes recalculate |
| T3.9 | Expand a node with children (click arrow) | Children load lazily; no duplicates if expanded twice |
| T3.10 | Reload the page (navigate away and back) | All nodes still present with correct codes |
| T3.11 | Close and reopen the project | All nodes and codes persist correctly in SQLite |

---

### T4 — WBS Detail Panel

| # | Action | Expected |
|---|---|---|
| T4.1 | Click a node | Detail panel appears (right column) with node data populated |
| T4.2 | Edit Title, Description, AssignedTo; click **Save** | Data saved; node title updates in tree |
| T4.3 | Change Status to **In Progress**; click **Save** | Status updated; Dashboard KPIs reflect change on next visit |
| T4.4 | Enter `EstimatedHours = 10`, `ActualHours = 5`; click **Save** | Values round-trip correctly (no NaN / Infinity) |
| T4.5 | Set Start Date and Due Date; click **Save** | Dates persist; Gantt bars appear for this node |
| T4.6 | Click **Cancel** after editing | Fields revert to last saved values |
| T4.7 | Switch to the **Dependencies** tab | Tab shows "No predecessors" (or existing list) |
| T4.8 | Select a predecessor node in the picker; click **Add Predecessor** | Dependency row appears with correct Code, Title, Type label |
| T4.9 | Click the delete (✕) button on a dependency row | Confirm dialog; row removed |
| T4.10 | Add the same predecessor twice | Second add is silently ignored (duplicate guard) |
| T4.11 | Delete the predecessor node from the WBS tree (after B1 is fixed) | Dependency row disappears from the tab |

---

### T5 — WBS Outline

| # | Action | Expected |
|---|---|---|
| T5.1 | Navigate to **WBS Outline** | All nodes listed flat, sorted by Code |
| T5.2 | Navigate away then back | List reloads fresh |
| T5.3 | Click a row | Navigates to **WBS Tree** page (verify node is selected — see B6) |
| T5.4 | Add a node in WBS Tree, then visit Outline | New node appears |

---

### T6 — Dashboard

| # | Action | Expected |
|---|---|---|
| T6.1 | Navigate to **Dashboard** with no nodes | KPI cards show `0`; charts empty but not crashing |
| T6.2 | Add nodes with mixed statuses; revisit Dashboard | Status donut reflects counts; KPI cards update |
| T6.3 | Set estimated/actual costs on nodes; revisit | Cost comparison chart shows bars |
| T6.4 | Assign different nodes to two different people; revisit | Effort-per-assignee chart shows two columns |
| T6.5 | Add a milestone due within 30 days; revisit | Milestone appears in upcoming list |
| T6.6 | Mark the milestone complete; revisit | Milestone removed from upcoming list |

---

### T7 — Gantt

| # | Action | Expected |
|---|---|---|
| T7.1 | Navigate to **Gantt** with undated nodes | Rows show "Unscheduled" label; no bars; today line visible |
| T7.2 | Set Start + Due dates on several nodes; revisit Gantt | Bars appear at correct horizontal positions |
| T7.3 | Switch zoom to **Day / Week / Month** | Bars rescale proportionally; today line moves correctly |
| T7.4 | Gantt with a node whose `DueDate < StartDate` | Bar has zero or negative width — should either not render or show a minimum width |
| T7.5 | Project with 50+ nodes | Canvas scrolls; no crash or layout overflow |

---

### T8 — Reports

| # | Action | Expected |
|---|---|---|
| T8.1 | Navigate to **Reports** with no nodes | Charts render empty; no crash |
| T8.2 | Apply date-range filter that excludes all nodes | Charts clear; no crash |
| T8.3 | Apply assignee filter | Only that assignee's nodes appear in effort chart |
| T8.4 | Apply status filter | Progress chart reflects filtered set |
| T8.5 | Clear all filters | Charts return to full-project view |
| T8.6 | Burn-down chart with no dates set on nodes | Handles gracefully (no NaN lines) |

---

### T9 — Milestones

| # | Action | Expected |
|---|---|---|
| T9.1 | Navigate to **Milestones** | Empty list; toolbar visible |
| T9.2 | Enter a title + due date; click **Add Milestone** | Milestone appears in list with "Upcoming" badge |
| T9.3 | Add a milestone with a past due date | Badge shows "Overdue" in red |
| T9.4 | Click a milestone | Detail panel opens with correct title/date |
| T9.5 | Edit title; click **Save** | Title updates in list (selection preserved — see B3) |
| T9.6 | Click **Mark Complete** | Confirm dialog; badge changes to "✓ Complete"; Mark Complete button hidden |
| T9.7 | Select a node in the picker; click **Link Node** | Node appears in Linked Nodes list |
| T9.8 | Link the same node twice | Second link silently ignored |
| T9.9 | Click ✕ on a linked node | Node removed from list |
| T9.10 | Click ✕ delete on a milestone row | Confirm dialog; milestone removed; detail panel closes |
| T9.11 | Close and reopen project | All milestones and links persisted correctly |

---

### T10 — Error Handling & Edge Cases

| # | Action | Expected |
|---|---|---|
| T10.1 | Open a non-`.iscwbs` file via Open dialog | Error dialog; app remains stable |
| T10.2 | Delete the `.iscwbs` file while the project is open | Next operation shows an error dialog via `IDialogService` |
| T10.3 | Navigate between pages rapidly | No duplicate data loads; no `ObjectDisposedException` |
| T10.4 | Open two instances of the app pointing at the same file | SQLite locking error handled gracefully |
| T10.5 | Crash the app (kill from Task Manager); relaunch | Recent project still listed; opens normally |
| T10.6 | Check `%LOCALAPPDATA%\ISC\iscWBS\Logs\` | Log file created; exception details written after T10.1–T10.4 |

---

## Part 3 — Regression Checklist After Each Phase 4 Feature

Once the remaining Phase 4 items are implemented, run these regressions:

| Feature added | Re-run tests |
|---|---|
| `SettingsPage` | T1, T2 (theme; currency display) |
| Keyboard shortcuts | T3 (`Ins`, `Del`, `F2` on WBS Tree) |
| `IExportService` | T3, T5 (data is correct before export); verify generated files open in Excel / Adobe Reader |
| `WbsDiagramControl` | T3 (click node in diagram → navigates to WBS Tree) |
| Accessibility pass | Open Narrator; tab through every page; verify all controls announce correctly |

---

## Part 4 — Bug Priority Queue

Fix in this order before starting full test runs:

1. **B1** — `NodeDependencyRepository.DeleteByNodeAsync` + call from `WbsService.DeleteRecursiveAsync`
2. **B3** — Re-select milestone by ID after `SaveEditAsync` reload
3. **B7** — `DateTime.SpecifyKind` in `MilestoneRowViewModel.IsOverdue`
4. **B4** — Audit `GanttViewModel` and `DashboardViewModel` for `DateTimeKind.Unspecified` reads
5. **B5** — Document "predecessors only" scope of Dependencies tab (or add successor section)
6. **B6** — Verify `WbsOutlinePage` row-click actually selects the node in WbsTreePage
