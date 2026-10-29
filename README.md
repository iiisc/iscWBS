# iscWBS

A Windows desktop application for project management centred on a Work Breakdown Structure editor.

Built with **WinUI 3** and **.NET 10**, distributed as a self-contained ZIP — no installation required. Project data lives in a portable `.iscwbs` file (SQLite) that you place wherever you like. The UI is available in English and Swedish.

---

## Features

- **WBS Tree** — hierarchical diagram editor with auto-generated codes, status tracking, hour estimates and dependencies (FS/SS/FF/SF)
- **WBS Outline** — flat list of all nodes; select a row to open it in the tree
- **Gantt Chart** (beta) — timeline view with dependency arrows and milestone markers
- **Milestones** — milestone tracker linked to WBS nodes
- **Dashboard** — progress KPIs, status chart and deliverables, filterable by assignee, status and date
- **Reports** — export a project status report to PDF

---

## Requirements

| | |
|---|---|
| OS | Windows 10 version 1809 (build 17763) or later |
| Architecture | x64 |
| Runtime | Self-contained — no separate .NET install needed |

---

## Getting Started

1. Download the latest `iscWBS-<version>-x64.zip` from [Releases](../../releases).
2. Extract it to a folder you can write to (the in-app updater replaces files in that folder, so avoid `Program Files`).
3. Run `iscWBS.exe` — no installer, no admin rights required.
4. Choose **New Project** to create a `.iscwbs` project file, or **Open Project** to load an existing one.

Settings and logs are stored under `%LOCALAPPDATA%\ISC\iscWBS\`.

> Windows SmartScreen may warn on first launch while the executable is unsigned. Click **More info → Run anyway** to proceed.

---

## Building from Source

```powershell
git clone https://github.com/iiisc/iscWBS.git
cd iscWBS
dotnet build iscWBS.csproj -c Debug -p:Platform=x64
dotnet test tests/iscWBS.Tests
dotnet publish iscWBS.csproj -c Release -r win-x64 -p:Platform=x64
```

**Prerequisites:** the .NET 10 SDK. The Windows App SDK comes from NuGet and is bundled into the app. Publishing compiles to native code (Native AOT), which also needs the MSVC build tools (Visual Studio's "Desktop development with C++" workload).

---

## Tech Stack

| | |
|---|---|
| UI framework | WinUI 3 (Windows App SDK) |
| Language | C# 13 / .NET 10 |
| Architecture | MVVM — CommunityToolkit.Mvvm |
| Database | SQLite via Microsoft.Data.Sqlite |
| PDF export | QuestPDF |

---

## License

MIT
