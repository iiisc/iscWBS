# iscWBS

A Windows desktop application for project management centred on a Work Breakdown Structure editor.

Built with **WinUI 3** and **.NET 10**, distributed as a single self-contained executable — no installation required. Project data lives in a portable `.iscwbs` file (SQLite) that you place wherever you like.

---

## Features

- **WBS Tree** — hierarchical node editor with auto-generated codes, status tracking, hour and cost estimates
- **WBS Outline** — spreadsheet-style flat view of all nodes with inline editing
- **Gantt Chart** — timeline view with dependency arrows and milestone markers
- **Milestones** — dedicated milestone tracker linked to WBS nodes
- **Dashboard** — at-a-glance progress charts and project summary
- **Reports** — export project status reports to PDF and Excel

---

## Requirements

| | |
|---|---|
| OS | Windows 10 version 1809 (build 17763) or later |
| Architecture | x64 |
| Runtime | Self-contained — no separate .NET install needed |

---

## Getting Started

1. Download the latest `iscWBS.exe` from [Releases](../../releases).
2. Run the executable — no installer, no admin rights required.
3. Choose **New Project** to create a `.iscwbs` project file, or **Open Project** to load an existing one.

> Windows SmartScreen may warn on first launch because the executable is unsigned. Click **More info → Run anyway** to proceed.

---

## Building from Source

```powershell
git clone https://github.com/iiisc/iscWBS.git
cd iscWBS
dotnet build -c Debug
```

**Prerequisites:** Visual Studio 2022 or later with the **Windows App SDK** workload, or the [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) standalone installer.

---

## Tech Stack

| | |
|---|---|
| UI framework | WinUI 3 (Windows App SDK) |
| Language | C# 13 / .NET 10 |
| Architecture | MVVM — CommunityToolkit.Mvvm |
| Database | SQLite via sqlite-net-pcl |
| Charts | LiveChartsCore (SkiaSharp) |
| PDF export | QuestPDF |
| Excel export | ClosedXML |

---

## License

MIT
