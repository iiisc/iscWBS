# Copilot Instructions — iscWBS

This is a **WinUI 3 (.NET 10, packaged MSIX)** desktop application for project management centred on a Work Breakdown Structure editor. Full code standards are in `instructions.md`. Full feature plan is in `plan.md`.

---

## Architecture

Strict MVVM layering — never skip layers:

```
View (XAML)  →  ViewModel (CommunityToolkit.Mvvm)  →  Service interface  →  Repository (SQLite)
```

- **Views** — layout and animation only; no business logic
- **ViewModels** — state, commands, orchestration; no WinUI type references (only `DispatcherQueue` is permitted for UI thread marshalling)
- **Services** — business rules; always work through interfaces
- **Repositories** — all SQLite access; one class per aggregate

---

## ViewModel Pattern

All ViewModels are `sealed partial` and inherit `ObservableObject`:

```csharp
namespace iscWBS.ViewModels;

public sealed partial class ExampleViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = string.Empty;

    [RelayCommand]
    private async Task LoadAsync() { }

    partial void OnTitleChanged(string value) { }
}
```

- Collections → `ObservableCollection<T>`
- Long-running commands → `[RelayCommand(IncludeCancelCommand = true)]`
- Never instantiate `ContentDialog`, `Frame`, or `Window` inside a ViewModel — use `INavigationService` / `IDialogService`

---

## Dependency Injection

Registered in `App.xaml.cs` via `Microsoft.Extensions.DependencyInjection`:

- **Singleton** — all services (`IProjectStateService`, `INavigationService`, `IDialogService`, `IWbsService`, `ISettingsService`, etc.)
- **Transient** — all ViewModels
- **Not in DI** — `WbsDatabase`; it is created and disposed by `IProjectStateService` when a project is opened or closed

Views resolve their ViewModel in the constructor:

```csharp
public ExamplePage()
{
    InitializeComponent();
    ViewModel = App.Services.GetRequiredService<ExampleViewModel>();
}
```

`App.Services` is a `static IServiceProvider` property set during `OnLaunched`. No service locator calls outside of View constructors.

---

## Service & Repository Patterns

### Key service interfaces

```csharp
// Owns the active SQLite connection; all repositories draw their connection from here
public interface IProjectStateService
{
    Project? ActiveProject { get; }
    bool HasActiveProject { get; }
    SQLiteAsyncConnection? Database { get; }
    event EventHandler<Project?> ActiveProjectChanged;
    Task OpenProjectAsync(string filePath);
    Task CreateProjectAsync(string name, string filePath);
    Task CloseProjectAsync();
}

// Wraps ApplicationData.LocalSettings
public interface ISettingsService
{
    T? Get<T>(string key);
    void Set<T>(string key, T value);
    IReadOnlyList<string> GetRecentProjects();
    void AddRecentProject(string filePath);
}
```

### General rules
- Live in `Core/Services/` and are the only types ViewModels reference
- All methods are `async Task<T>` or `async Task` — no synchronous data access
- XML doc comments (`///`) on every `public` member
- Throw domain-specific exceptions (e.g. `WbsNotFoundException`) — never expose raw SQLite errors

### Repositories
- One class per aggregate root: `ProjectRepository`, `WbsNodeRepository`
- Inject `IProjectStateService` to obtain the active `SQLiteAsyncConnection`
- Use `SQLiteAsyncConnection` exclusively — no sync connection
- Return `null` for not-found single lookups; throw for genuinely unexpected states:

```csharp
public async Task<WbsNode?> GetByIdAsync(Guid id) { ... }
```

### Breaking schema changes

Any change that would make an existing `.iscwbs` project file unreadable — including incrementing `_currentSchemaVersion` in `WbsDatabase`, dropping or renaming a column or table, changing a primary key type, or removing a `CreateTableAsync` call — **requires explicit manual confirmation before proceeding**.

Copilot must never silently apply such changes. Always surface them prominently and wait for the developer to confirm before writing the migration code. When confirmation is given:

1. Increment `_currentSchemaVersion` in `WbsDatabase`.
2. Add a complete migration case in `ApplyMigrationsAsync` covering every version gap.
3. Manually test the migration against at least one pre-migration `.iscwbs` file before merging.
4. Document the change in the PR description.

---

## Application Startup

- `OnLaunched` calls `QuestPDF.Settings.License = LicenseType.Community`, builds the DI container, then opens `ShellWindow`
- `ShellWindow.Loaded` calls `INavigationService.Initialize(contentFrame)`, `IDialogService.Initialize(XamlRoot)`, and checks `ISettingsService.GetRecentProjects()`
  - If a valid recent project exists → call `IProjectStateService.OpenProjectAsync`, navigate to `DashboardPage`
  - Otherwise → navigate to `WelcomePage`
- `WelcomePage` is **not** a `NavigationView` item; it is only the startup target when no project is open
- `NavigationView` items are disabled while `IProjectStateService.HasActiveProject` is `false`
- `ShellViewModel` subscribes to `IProjectStateService.ActiveProjectChanged` to toggle nav items and update the title bar

---

## Navigation

- `INavigationService` wraps the root `Frame` in `ShellWindow`
- ViewModels call `_navigationService.NavigateTo<TPage>()` — never `Frame.Navigate()` directly
- Page ViewModels that need lifecycle callbacks implement `INavigationAware`:

```csharp
public interface INavigationService
{
    bool CanGoBack { get; }
    void Initialize(Frame frame);        // called by ShellWindow.Loaded, never by ViewModels
    void NavigateTo<TPage>(object? parameter = null);
    void GoBack();
}

public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom();
}
```

- `INavigationAware.OnNavigatedTo` is the primary change notification mechanism — ViewModels reload their data here so the view is always fresh when navigated to

---

## Dialogs

- `ContentDialog` is never instantiated inside a ViewModel
- Dialog results are returned as typed result objects, not raw `ContentDialogResult`
- `Initialize` is called **once** by `ShellWindow.Loaded` — never by ViewModels

```csharp
public interface IDialogService
{
    void Initialize(XamlRoot xamlRoot);  // called by ShellWindow, never by ViewModels
    Task ShowErrorAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
    Task ShowContentAsync<TControl>(string title) where TControl : new();
}
```

---

## Error Handling

- All service and repository calls in ViewModels are wrapped in `try/catch`
- Errors surface via `IDialogService.ShowErrorAsync` — never swallowed silently
- A top-level `UnhandledException` handler in `App.xaml.cs` calls `Logger.Write(ex)` (static helper in `Helpers/Logger.cs`; appends to `%LOCALAPPDATA%\ISC\iscWBS\Logs\log-{date}.txt`) and shows a fatal error dialog

---

## XAML Rules

- `x:Bind` always — never `{Binding}`
- `x:Bind` default mode is `OneWay` — use `Mode=TwoWay` only for editable input controls
- All colours and fonts via `ThemeResource` from `Themes/Generic.xaml` — no hardcoded values
- WBS status colours — always use these `ThemeResource` keys: `WbsStatusNotStartedBrush`, `WbsStatusInProgressBrush`, `WbsStatusCompleteBrush`, `WbsStatusBlockedBrush`
- Styles with `TargetType` but no `x:Key` are implicit (global) — use sparingly
- Page-level layout root is always `Grid`; avoid deep nesting — prefer `StackPanel` or `RelativePanel` for simple flows
- Every interactive control must have `AutomationProperties.Name`
- Code-behind contains only `InitializeComponent()`, the ViewModel assignment, and unavoidable pointer/drag event handlers that immediately delegate to the ViewModel

```xml
<TextBlock Text="{x:Bind ViewModel.Title, Mode=OneWay}" />

<Button Command="{x:Bind ViewModel.DeleteNodeCommand}"
        CommandParameter="{x:Bind ViewModel.SelectedNode, Mode=OneWay}"
        AutomationProperties.Name="Delete selected node" />
```

---

## C# Rules

- File-scoped namespaces in every `.cs` file
- Nullable reference types enabled — no `null!` unless provably safe
- `async Task` only — never `async void` (exception: direct WinUI event handlers), never `.Result` or `.Wait()`
- Async methods always suffixed `Async`
- Private fields `_camelCase`; all other members `PascalCase`
- `var` only when type is obvious from the right-hand side
- `is` pattern matching and `switch` expressions over `as`/cast chains
- `record` for immutable value objects (e.g. chart data points)
- `///` XML doc comments on every `public` member of a service interface
- No `#region` blocks

---

## Naming Reference

| Artefact | Pattern | Example |
|---|---|---|
| Page | `<Name>Page` | `WbsTreePage` |
| ViewModel | `<Name>ViewModel` | `WbsTreeViewModel` |
| UserControl | `<Name>Control` | `WbsDetailControl` |
| Service interface | `I<Name>Service` | `IWbsService` |
| Service impl | `<Name>Service` | `WbsService` |
| Repository | `<Name>Repository` | `WbsNodeRepository` |
| Converter | `<Name>Converter` | `StatusToBrushConverter` |
| Async method | `<Verb><Noun>Async` | `LoadNodesAsync` |
| Observable field | `_camelCase` | `_selectedNode` |

---

## Folder → Namespace Map

| Folder | Namespace |
|---|---|
| `Core/Models/` | `iscWBS.Core.Models` |
| `Core/Exceptions/` | `iscWBS.Core.Exceptions` |
| `Core/Services/` | `iscWBS.Core.Services` |
| `Core/Repositories/` | `iscWBS.Core.Repositories` |
| `ViewModels/` | `iscWBS.ViewModels` |
| `Views/` | `iscWBS.Views` |
| `Views/Controls/` | `iscWBS.Views.Controls` |
| `Converters/` | `iscWBS.Converters` |
| `Helpers/` | `iscWBS.Helpers` |

---

## Available Packages

Only use libraries already installed. Do not suggest adding new packages.

| Package | Purpose |
|---|---|
| `CommunityToolkit.Mvvm` | `ObservableObject`, `[ObservableProperty]`, `[RelayCommand]` |
| `CommunityToolkit.WinUI.Controls.DataGrid` | Data grid for outline/table views |
| `LiveChartsCore.SkiaSharpView.WinUI` | All charts and data visualisations |
| `sqlite-net-pcl` + `SQLitePCLRaw.bundle_green` | SQLite persistence via `SQLiteAsyncConnection` |
| `Microsoft.Extensions.DependencyInjection` | DI container in `App.xaml.cs` |
| `ClosedXML` | Excel export (Phase 4) |
| `QuestPDF` | PDF export (Phase 4) |
