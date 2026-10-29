# Copilot Instructions — iscWBS

WinUI 3 desktop app (.NET 10, unpackaged, self-contained x64) for planning projects around a Work Breakdown Structure. Each project is one SQLite file (`.iscwbs`).

## Layout

```
Views (XAML + thin code-behind)  →  ViewModels (CommunityToolkit.Mvvm)  →  Core/Services  →  SQLite
```

- `Core/Models/` — data rows plus pure, static domain logic: `DependencyConstraints`, `WbsHierarchy`, `DueDateRules`, `ProjectSummary.Compute`, `GanttLayoutBuilder`. Anything that doesn't touch the database or UI goes here, so it can be unit-tested.
- `Core/Services/` — concrete classes, no interfaces. `ProjectStateService` owns the open file; `WbsService` and `MilestoneService` query it through `ProjectStateService.Connection`, SQL included.
- `Core/Data/` — `WbsDatabase` (pragmas, schema, migrations) and `SqliteExtensions`, the query helpers over Microsoft.Data.Sqlite. Map rows by hand and bind values through those helpers: they keep the GUID and date text formats existing files use.
- `Helpers/` — UI-side helpers: `Strings` (resource lookup), `DisplayText`, `StatusPalette`, `FilePickers`, `Logger`.
- `AppServices` holds one instance of each service and builds a fresh view model per page; pages call `App.Services.Create…ViewModel()` in their constructor. No DI container.
- Pages call their view model's `OnNavigatedTo`/`OnNavigatedFrom` from the matching `Page` overrides.

## Rules that matter

- **Schema changes need explicit confirmation.** Anything that makes an existing `.iscwbs` file unreadable — bumping `_currentSchemaVersion`, dropping/renaming a table or column, changing a key type — must be surfaced and confirmed first. Then add a migration step in `WbsDatabase` and test it against a file from the previous version.
- **Dates are calendar dates.** Store `DateTimeOffset.Date` (local midnight) and compare with `DateTime.Today`, never UTC.
- **All user-visible text is localized.** XAML uses `x:Uid`; code uses `Strings.Get`/`Strings.Format`. Add every key to both `Strings/en-US/Resources.resw` and `Strings/sv-SE/Resources.resw`. Exceptions keep English messages for the log and carry a resource key via `WithMessageKey`.
- **Errors:** view models wrap service calls in `try/catch` and show `DialogService.ShowErrorAsync(..., DisplayText.ErrorMessage(ex))`. Expected failures are `WbsException`.
- **Colours:** WBS status colours come from `StatusPalette`; everything else from theme resources.
- Release builds are Native AOT. Anything bound to `ItemsSource` must be typed `List<T>` or `ObservableCollection<T>` (CsWinRT cannot marshal `IReadOnlyList<T>` or collection-expression arrays), and JSON goes through `AppJsonContext`.
- `x:Bind` only, `Mode=TwoWay` only on editable inputs. Interactive controls get `AutomationProperties.Name`.
- `async Task`, never `async void` outside event handlers; no `.Result`/`.Wait()`.
- Only use packages already referenced in `iscWBS.csproj`.

## Build and test

```
dotnet build iscWBS.csproj -c Debug -p:Platform=x64
dotnet test tests/iscWBS.Tests
dotnet publish iscWBS.csproj -c Release -r win-x64 -p:Platform=x64   # Native AOT; needs the MSVC build tools
```

Release-only AOT failures don't show in Debug: run the published exe before shipping a change to bindings or JSON.

The test project links the Core sources it needs instead of referencing the WinUI app, so it runs on plain `net10.0`. Keep anything you want tested free of WinUI types.
