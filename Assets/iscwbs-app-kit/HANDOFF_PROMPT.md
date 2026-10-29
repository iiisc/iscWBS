# Hand-off prompt for Claude Code

Unzip this kit next to the WinUI project (suggested: `brand/iscwbs-app-kit/`), then paste the prompt below into Claude Code from the repository root.

---

The iscWBS brand kit for this app is in `brand/iscwbs-app-kit/`. Read its `README.md` first; it defines the colors, shapes, type, icons and how assets are deployed. The app is WinUI 3, **unpackaged** (no MSIX, no installer), published **self-contained with Native AOT**. Every change must keep `dotnet publish` with AOT working with no new trim or AOT warnings.

Before changing anything, inspect the solution: project structure, Windows App SDK version, publish profile and AOT settings, MVVM setup, existing resource dictionaries, styles and asset names. Follow the existing conventions.

1. **App icon.** Copy `Assets/AppIcon.ico` into the project's `Assets/`. Set `<ApplicationIcon>Assets\AppIcon.ico</ApplicationIcon>` in the `.csproj` so the exe carries the icon, and call `AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"))` after the main window is created (and for any secondary windows).
2. **Content files.** Copy `Assets/Fonts/` (including `OFL.txt`) and `Assets/Illustrations/` into the project and make sure they're copied to the publish output (`Content`, `CopyToOutputDirectory="PreserveNewest"`). Don't ship `source/`, `marketing/` or `tokens/`.
3. **Resources.** Copy `Themes/IscBrand.xaml` and `Themes/IscIcons.xaml` into the project and merge them in `App.xaml` after `XamlControlsResources`. Replace hard-coded colors with the `Isc*Brush` resources via `{ThemeResource}`. Check Light, Dark and High Contrast, including switching theme while the app is running.
4. **Font.** Use `IscFontFamily` for the wordmark, page headings and empty states. Confirm Light, Normal and SemiBold render from the variable font in a published build; if one doesn't, switch to the static `IscFontFamilyLight/Regular/SemiBold` resources.
5. **Logo.** Create a reusable `IscLogo` control: the app icon plus the "iscWBS" wordmark ("isc" Light, "WBS" SemiBold, theme colors per README). Use it in the title bar or navigation pane header.
6. **Icons.** Use the `IscIcon*` `PathIcon` styles for WBS node types, tree actions (add child/sibling, indent/outdent, expand/collapse, drag), dependency, assignee and statuses, in buttons, menu items, `NavigationView` items and the tree. Give icon-only buttons an `AutomationProperties.Name` and a tooltip. Use Segoe Fluent Icons (`FontIcon`) for generic actions not in the set.
7. **WBS tree.** Style the tree's item template per the README: a squircle marker (`IscNodeCornerRadius`) filled with the node's level brush, root = level 1, each top-level branch taking the next of levels 2–6 (cycling) with descendants inheriting it. Compute the level index in the view model and map it to a brush with `{x:Bind}` and an AOT-safe converter or function (no reflection), making sure the marker updates when the theme changes.
8. **Empty states.** Create an `IscEmptyState` UserControl (illustration `Image` at 320×200, title, body, primary action button using `AccentButtonStyle`) and use it in the views listed in the README's Empty states table, with the suggested copy from `source/illustrations/copy.json`. Put the strings in `.resw` resources if the app is localized.

Build after each step. At the end, run the actual AOT publish, launch the published exe from a different working directory, and confirm the exe icon in Explorer, the taskbar and title bar icons, the fonts and the illustrations all appear. Don't modify the artwork files. Summarize what changed, where each asset is used, and anything that didn't fit, with the alternative you chose.
