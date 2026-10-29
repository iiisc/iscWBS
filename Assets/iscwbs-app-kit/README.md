# iscWBS brand kit · WinUI 3 (unpackaged, self-contained, Native AOT)

The "Night sky · Bold crop" brand, packaged for an unpackaged WinUI 3 app that ships as a self-contained, Native AOT build with no installer. `HANDOFF_PROMPT.md` contains a prompt for Claude Code to wire it in.

## Folder map

| Folder | What it is | Where it goes |
|---|---|---|
| `Assets/AppIcon.ico` | App icon, 15 sizes from 16 to 256 px (covers every DPI step Windows uses) | Project `Assets/`; embedded in the exe and loaded for the title bar |
| `Assets/Illustrations/` | 5 empty-state illustrations: `.png` (1280×800, displayed at 320×200, so sharp up to 400% DPI) and `.svg` | Project `Assets/Illustrations/` |
| `Assets/Fonts/` | Outfit variable font, static Light/Regular/SemiBold instances, SIL Open Font License | Project `Assets/Fonts/` (the license must ship with the font) |
| `Themes/IscBrand.xaml` | Theme-aware colors and brushes (Light, Dark, HighContrast), font families, corner radii | Merge into `App.xaml` |
| `Themes/IscIcons.xaml` | 18 UI icons as `PathIcon` styles (filled geometry, follows `Foreground`) | Merge into `App.xaml` |
| `source/` | SVG masters, `icons.json`, `copy.json` (empty-state text) | Reference only; don't ship |
| `marketing/` | Share image (1200×630) and a 512 px icon PNG for a website, GitHub releases or a README | Not part of the app |
| `tokens/tokens.json` | The same design values in neutral JSON | Reference |

## Icon setup (no installer)

Without a package manifest, Windows takes the app's icon from the exe itself, and the window needs its icon set at runtime.

```xml
<!-- .csproj -->
<PropertyGroup>
  <ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>
</PropertyGroup>
<ItemGroup>
  <Content Include="Assets\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

```csharp
// after the main window is created
AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));
```

The embedded icon is what Explorer, the taskbar, pinned shortcuts and Alt+Tab show; `SetIcon` covers the title bar and keeps the taskbar button consistent. Use an absolute path built from `AppContext.BaseDirectory`, since the working directory isn't guaranteed when users launch the exe from elsewhere.

**Asset URIs.** `ms-appx:///Assets/...` resolves to the app folder in unpackaged WinUI 3 apps as long as the files are copied to the output, which the `Content` item above does. Verify fonts and illustrations in a published AOT build, not only in Debug. The illustrations are single high-resolution PNGs on purpose, so they don't depend on MRT scale-qualifier lookup.

## Visual rules

**Color.** Use the `Isc*Brush` resources with `{ThemeResource}` so they switch between Light, Dark and High Contrast, including when the user changes theme while the app is running. Night `#14112B` is the brand ground (`IscNightBrush`). Mica is a good window backdrop; use `IscSurfaceBrush` for cards and panels on top of it.

**Tree colors.** The root node uses `IscLevel1Brush` (yellow). Each top-level branch takes the next of `IscLevel2…6Brush` in turn (cycling), and its descendants inherit their branch's color. Level colors are accents (a squircle marker, a chip, an icon tint); node text stays `IscTextBrush` on `IscSurfaceBrush`. In High Contrast all levels map to system colors.

**Shape.** Nodes are squircles: corner radius ~31% of height (`IscNodeCornerRadius` = 11 for a 36 px node). Connectors are orthogonal lines, `IscConnectorThickness` 2, with softly rounded elbows. The four-point sparkle is the brand accent; use it sparingly.

**Icons.** `<PathIcon Style="{StaticResource IscIconWorkPackage}" Width="20" Height="20" />`. 24 px grid, outlined from a 2 px round stroke; use them at 16, 20 or 24 px. For generic actions not in the set (search, settings, delete…), use Segoe Fluent Icons via `FontIcon`.

**Type.** Outfit: `IscFontFamily` (variable) with `FontWeight` Light / Normal / SemiBold, or the static families if a weight doesn't render. Wordmark: "isc" Light + "WBS" SemiBold. On light: "WBS" `#1E1838`, "isc" `#5A4F96`. On dark: "WBS" `#FFFFFF`, "isc" `#C4B8FF`. Body text can stay Segoe UI Variable for a native feel; use Outfit for the wordmark, headings and empty states.

## AOT notes

Everything in the kit is plain XAML resources and content files: no reflection, no runtime `XamlReader`, no extra NuGet packages. When wiring it up, prefer `{x:Bind}` (compiled) over `{Binding}`, and keep converters and helpers free of reflection so trimming can't remove anything they need.

## Icon keys

`IscIconProject` · `IscIconDeliverable` · `IscIconWorkPackage` · `IscIconMilestone` · `IscIconAddChild` · `IscIconAddSibling` · `IscIconIndent` · `IscIconOutdent` · `IscIconExpand` · `IscIconCollapse` · `IscIconDragHandle` · `IscIconDependency` · `IscIconAssignee` · `IscIconStatusTodo` · `IscIconStatusInProgress` · `IscIconStatusDone` · `IscIconStatusBlocked` · `IscIconSparkle`

## Empty states

| Image | Where | Suggested copy |
|---|---|---|
| `EmptyProjects` | No projects yet | **Plant your first project** — Start with a root, then break it down into deliverables and work packages. [New project] |
| `EmptyBranch` | Selected node has no children | **This branch is still bare** — Add work packages to break this deliverable into doable pieces. [Add work package] |
| `NoResults` | Search/filter with no matches | **Nothing matches that search** — Try a different keyword, or clear your filters to see the whole tree. [Clear filters] |
| `AllDone` | Every work package complete | **Every branch is done** — All work packages in this project are complete. Nicely done. [View summary] |
| `NotFound` | Missing item (deleted node, broken link, file that can't open) | **This branch snapped off** — We couldn’t find that item. It may have been moved or deleted. [Back to projects] |

Show them with `<Image Source="ms-appx:///Assets/Illustrations/EmptyProjects.png" Width="320" Height="200" />`. They're transparent and work on Light and Dark. All copy is a suggestion.
