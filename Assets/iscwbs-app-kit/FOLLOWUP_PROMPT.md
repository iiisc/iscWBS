# Follow-up prompt for Claude Code (new artwork)

Replace the kit folder with this version, then paste:

---

The brand kit in `brand/iscwbs-app-kit/` now includes the artwork you asked for. Read the updated `README.md` sections "Empty states" and "Reports header", then:

1. Copy `Assets/Illustrations/EmptyMilestones.png`, `NothingScheduled.png` and `ReportsHeader.png` into the project's `Assets/Illustrations/` (content, copied to output).
2. Use `IscEmptyState` with `EmptyMilestones` in the Milestones view when there are no milestones, and with `NothingScheduled` in the timeline when no work has dates. Take the copy from `source/illustrations/copy.json` (`.resw` if localized).
3. Add the Reports header per the README: banner at the top of the Reports page, title overlaid on the clear left side in white Outfit SemiBold, image hidden in High Contrast.

Keep the AOT publish warning-free, verify in a published build, and summarize what changed.
