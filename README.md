# iscWBS

A Windows desktop application for project management centred on a Work Breakdown Structure editor.

Built with **WinUI 3** and **.NET 10**, distributed as a self-contained ZIP — no installation required. Project data lives in a portable `.iscwbs` file (SQLite) that you place wherever you like. The UI is available in English and Swedish, in light and dark mode.

![The WBS Tree with a node's details open](docs/screenshot.png)

---

## Features

- **WBS Tree** — hierarchical diagram editor with auto-generated codes, status tracking, hour estimates and dependencies (FS/SS/FF/SF)
- **WBS Outline** — flat list of all nodes; select a row to open it in the tree
- **Gantt Chart** (beta) — timeline view with dependency arrows and milestone markers
- **Milestones** — milestone tracker linked to WBS nodes
- **Dashboard** (beta) — progress KPIs, status chart and deliverables, filterable by assignee, status and date
- **Reports** (beta) — export a project status report to PDF

### Keyboard shortcuts

| Keys | Where | Action |
|---|---|---|
| Ctrl+Shift+N | WBS Tree | Add a root node |
| Ctrl+N | WBS Tree | Add a child to the selected node |
| Delete | WBS Tree, diagram focused | Delete the selected node |
| Arrow keys | WBS Tree, diagram focused | Move to the parent, first child or a sibling |
| Esc | WBS Tree | Close the detail panel |
| Ctrl+E | Reports | Generate the PDF report |

---

## Requirements

| | |
|---|---|
| OS | Windows 10 version 1809 (build 17763) or later |
| Architecture | x64 |
| Runtime | Self-contained — no separate .NET install needed |

---

## Getting Started

1. Download the latest `iscWBS-v<version>-win-x64.zip` from [Releases](../../releases).
2. Extract it somewhere you can write to; it unpacks into an `iscWBS` folder. The in-app updater replaces files in that folder, so avoid `Program Files`.
3. Run `iscWBS\iscWBS.exe` — no installer, no admin rights required. Later versions install from **Settings → Software Updates**, after checking the download against the SHA-256 GitHub publishes for it.
4. Choose **New Project** to create a `.iscwbs` project file, or **Open Project** to load an existing one.

Settings and logs are stored under `%LOCALAPPDATA%\ISC\iscWBS\`.

> Windows SmartScreen may warn on first launch while the executable is unsigned. Click **More info → Run anyway** to proceed.

---

## Building from Source

```powershell
git clone https://github.com/iiisc/iscWBS.git
cd iscWBS
dotnet build iscWBS.csproj
dotnet test tests/iscWBS.Tests
dotnet publish iscWBS.csproj -c Release -o publish/iscWBS
```

**Prerequisites:** the .NET 10 SDK. The Windows App SDK comes from NuGet and is bundled into the app. Publishing compiles to native code (Native AOT), which also needs the MSVC build tools (Visual Studio's "Desktop development with C++" workload).

## Releasing

Run **Actions → Release → Run workflow**. Leave the version empty for the next patch, or enter one such as `1.2.0`. The workflow runs the CI checks, publishes, tags the commit and creates a **draft** release whose notes list the commits since the last release. Rewrite the notes for users and publish the draft; the app only offers published releases.

---

## Tech Stack

| | |
|---|---|
| UI framework | WinUI 3 (Windows App SDK) |
| Language | C# 14 / .NET 10, compiled with Native AOT |
| Architecture | MVVM — CommunityToolkit.Mvvm |
| Database | SQLite via Microsoft.Data.Sqlite |
| PDF export | QuestPDF |

---

## License

[MIT](LICENSE)
