# iscWBS — Code Standards & Architecture

## 1. Architecture Pattern

The application follows **MVVM (Model-View-ViewModel)** strictly.

```
View (XAML + minimal code-behind)
  └── x:Bind → ViewModel (CommunityToolkit.Mvvm)
                  └── calls → Service interface
                                └── calls → Repository (SQLite)
```

- **Views** own layout and animation only. No business logic.
- **ViewModels** own state, commands, and orchestration. No WinUI type references (except `DispatcherQueue` for marshalling).
- **Services** own business rules and cross-cutting concerns.
- **Repositories** own all data access. No SQL outside the repository layer.

---

## 2. Folder & Namespace Conventions

| Folder | Namespace |
|---|---|
| `Core/Models/` | `iscWBS.Core.Models` |
| `Core/Exceptions/` | `iscWBS.Core.Exceptions` |
| `Core/Services/Interfaces/` | `iscWBS.Core.Services` |
| `Core/Services/` | `iscWBS.Core.Services` |
| `Core/Repositories/` | `iscWBS.Core.Repositories` |
| `ViewModels/` | `iscWBS.ViewModels` |
| `Views/` | `iscWBS.Views` |
| `Views/Controls/` | `iscWBS.Views.Controls` |
| `Converters/` | `iscWBS.Converters` |
| `Helpers/` | `iscWBS.Helpers` |

Use **file-scoped namespaces** in every `.cs` file:

```csharp
namespace iscWBS.ViewModels;

public sealed partial class WbsTreeViewModel : ObservableObject { }
```

---

## 3. Dependency Injection

All dependencies are registered in `App.xaml.cs` using `Microsoft.Extensions.DependencyInjection`.

```csharp
// Lifetimes
Singleton   — database, services
Transient   — view models

// Resolution
App.Services.GetRequiredService<T>()
```

`App.Services` is a `static IServiceProvider` property set during `OnLaunched`. No service locator calls outside of View constructors.

```csharp
// View constructor pattern
public WbsTreePage()
{
    InitializeComponent();
    ViewModel = App.Services.GetRequiredService<WbsTreeViewModel>();
}
```

---

## 4. ViewModel Standards

Use `CommunityToolkit.Mvvm` throughout.

```csharp
public sealed partial class ExampleViewModel : ObservableObject
{
    // Backing field is camelCase; generated property is PascalCase
    [ObservableProperty]
    private string _title = string.Empty;

    // Commands are generated from methods
    [RelayCommand]
    private async Task LoadAsync()
    {
        // ...
    }

    // Validation on property change
    partial void OnTitleChanged(string value)
    {
        // side-effect logic here
    }
}
```

- All ViewModels are `sealed partial` classes.
- Collections exposed to the View are `ObservableCollection<T>`.
- Long-running operations use `[RelayCommand(IncludeCancelCommand = true)]`.
- ViewModels must never reference WinUI types (`UIElement`, `Page`, `Window`, etc.). Use `INavigationService` and `IDialogService` abstractions instead.

---

## 5. Service & Repository Standards

### Interfaces
- Prefixed with `I`: `IWbsService`, `IProjectService`
- All methods are `async Task<T>` or `async Task` — no synchronous data access
- Interfaces live in `Core/Services/` and are the only type referenced by ViewModels

### Implementations
- Concrete classes live in `Core/Services/` (same folder, no sub-folder)
- Throw domain-specific exceptions (e.g. `WbsNotFoundException`) rather than exposing raw SQLite exceptions

### Repositories
- One class per aggregate: `ProjectRepository`, `WbsNodeRepository`
- Use `SQLiteAsyncConnection` exclusively
- Return `null` (not exceptions) for single-entity lookups that may not exist:
  ```csharp
  public async Task<WbsNode?> GetByIdAsync(Guid id) { ... }
  ```

---

## 6. C# Coding Standards

- **Nullable reference types** are enabled project-wide. No `null!` suppressions unless genuinely safe.
- `async Task` only — never `async void` (exception: direct WinUI event handlers), never `.Result` or `.Wait()` calls.
- Async method names are always suffixed `Async`.
- `private` fields are `_camelCase`. All other members are `PascalCase`.
- No `var` where the type is not immediately obvious from the right-hand side.
- Use `is` pattern matching and `switch` expressions over `as`/cast chains.
- `record` types for immutable value objects (e.g. chart data points).
- No `#region` blocks.
- XML doc comments (`///`) on all `public` service interface members.

---

## 7. XAML Standards

- **`x:Bind`** over `{Binding}` everywhere. `Mode=OneWay` is the default; use `Mode=TwoWay` only for editable inputs.
- No hardcoded colour or font values inline — use `ThemeResource` or `StaticResource` from `Themes/Generic.xaml`.
- Styles with a `TargetType` but no `x:Key` are implicit (global) — use sparingly.
- Page-level layout uses `Grid`. Avoid deep nesting; prefer `RelativePanel` or `StackPanel` for simple flows.
- Every interactive control must have `AutomationProperties.Name` set.
- Code-behind files contain **only**:
  - `InitializeComponent()`
  - The ViewModel property assignment
  - Event handlers that are impossible to express in a command (e.g. drag-and-drop pointer events) — these must delegate immediately to the ViewModel.

```xml
<!-- Preferred binding pattern -->
<TextBlock Text="{x:Bind ViewModel.Title, Mode=OneWay}" />

<!-- Preferred command pattern -->
<Button Command="{x:Bind ViewModel.DeleteNodeCommand}"
        CommandParameter="{x:Bind ViewModel.SelectedNode, Mode=OneWay}"
        AutomationProperties.Name="Delete selected node" />
```

---

## 8. Navigation

- `INavigationService` wraps the root `Frame` in `ShellWindow`.
- ViewModels call `_navigationService.NavigateTo<TPage>()` — never `Frame.Navigate()` directly.
- Page ViewModels implement `INavigationAware` if they need `OnNavigatedTo` / `OnNavigatedFrom` callbacks.

```csharp
public interface INavigationService
{
    bool CanGoBack { get; }
    void NavigateTo<TPage>(object? parameter = null);
    void GoBack();
}

public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);
    void OnNavigatedFrom();
}
```

---

## 9. Dialogs

- `IDialogService` provides `ShowErrorAsync`, `ShowConfirmAsync`, and `ShowContentAsync<TControl>`.
- Content dialogs are never instantiated inside ViewModels directly.
- Dialog result is returned as a typed result object, not a raw `ContentDialogResult`.

```csharp
public interface IDialogService
{
    Task ShowErrorAsync(string title, string message);
    Task<bool> ShowConfirmAsync(string title, string message);
    Task ShowContentAsync<TControl>(string title) where TControl : new();
}
```

---

## 10. Error Handling

- All service and repository calls in ViewModels are wrapped in `try/catch`.
- Errors are surfaced to the user via `IDialogService.ShowErrorAsync` — never swallowed silently.
- A top-level `UnhandledException` handler in `App.xaml.cs` logs and shows a fatal error dialog before exit.

---

## 11. Naming Cheat Sheet

| Artefact | Convention | Example |
|---|---|---|
| Page (View) | `<Name>Page` | `WbsTreePage` |
| ViewModel | `<Name>ViewModel` | `WbsTreeViewModel` |
| UserControl | `<Name>Control` | `WbsDetailControl` |
| Service interface | `I<Name>Service` | `IWbsService` |
| Service implementation | `<Name>Service` | `WbsService` |
| Repository | `<Name>Repository` | `WbsNodeRepository` |
| Converter | `<Name>Converter` | `StatusToBrushConverter` |
| Async method | `<Verb><Noun>Async` | `LoadNodesAsync` |
| Observable property field | `_camelCase` | `_selectedNode` |

---

## 12. Available Packages

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
