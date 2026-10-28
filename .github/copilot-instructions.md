# Copilot Instructions — iscWBS

WinUI 3 desktop app (.NET 10, unpackaged, self-contained x64) for planning projects around a Work Breakdown Structure. Each project is one SQLite file (`.iscwbs`).

## Layout

```
Views (XAML + thin code-behind)  →  ViewModels (CommunityToolkit.Mvvm)  →  Core/Services  →  SQLite
```

- `Core/Models/` — data rows plus pure, static domain logic: `DependencyConstraints`, `WbsHierarchy`, `DueDateRules`, `ProjectSummary.Compute`, `GanttLayoutBuilder`. Anything that doesn't touch the database or UI goes here, so it can be unit-tested.
- `Core/Services/` — concrete classes, no interfaces. `ProjectStateService` owns the open file; `WbsService` and `MilestoneService` query it through `ProjectStateService.Connection`, SQL included.
- `Core/Data/` — `WbsDatabase` (pragmas, schema, migrations).
- `Helpers/` — UI-side helpers: `Strings` (resource lookup), `DisplayText`, `StatusPalette`, `FilePickers`, `Logger`.
- Services are DI singletons, view models transient; pages resolve their view model with `App.Services.GetRequiredService<T>()` in the constructor.
- Pages call their view model's `OnNavigatedTo`/`OnNavigatedFrom` from the matching `Page` overrides.

## Rules that matter

- **Schema changes need explicit confirmation.** Anything that makes an existing `.iscwbs` file unreadable — bumping `_currentSchemaVersion`, dropping/renaming a table or column, changing a key type — must be surfaced and confirmed first. Then add a migration step in `WbsDatabase` and test it against a file from the previous version.
- **Dates are calendar dates.** Store `DateTimeOffset.Date` (local midnight) and compare with `DateTime.Today`, never UTC.
- **All user-visible text is localized.** XAML uses `x:Uid`; code uses `Strings.Get`/`Strings.Format`. Add every key to both `Strings/en-US/Resources.resw` and `Strings/sv-SE/Resources.resw`. Exceptions keep English messages for the log and carry a resource key via `WithMessageKey`.
- **Errors:** view models wrap service calls in `try/catch` and show `DialogService.ShowErrorAsync(..., DisplayText.ErrorMessage(ex))`. Expected failures are `WbsException`.
- **Colours:** WBS status colours come from `StatusPalette`; everything else from theme resources.
- `x:Bind` only, `Mode=TwoWay` only on editable inputs. Interactive controls get `AutomationProperties.Name`.
- `async Task`, never `async void` outside event handlers; no `.Result`/`.Wait()`.
- Only use packages already referenced in `iscWBS.csproj`.

## Build and test

```
dotnet build iscWBS.csproj -c Debug -p:Platform=x64
dotnet test tests/iscWBS.Tests
```

The test project links the Core sources it needs instead of referencing the WinUI app, so it runs on plain `net10.0`. Keep anything you want tested free of WinUI types.
