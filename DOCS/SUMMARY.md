<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# Project Summary: Task-Split

**Status:** 🏗️ Development (Alpha — MVP + Add App UI + Groups panel)
**Objective:** A Windows taskbar enhancement tool for grouping and spacing icons.

## Quick Links
- [SCRATCHPAD](SCRATCHPAD.md)
- [FEATURES](FEATURES.md)
- [ARCHITECTURE](ARCHITECTURE.md)
- [STYLE_GUIDE](STYLE_GUIDE.md)
- [SBOM](SBOM.md)
- [README](../README.md) · [launch.bat](../launch.bat) · [build.bat](../build.bat)

## Current Focus
- Overlay groups panel UX (tooltips, click-to-focus shipped); taskbar physical spacing (Tier 1 in [FEATURES.md](FEATURES.md)).
- Win11 taskbar button discovery via UI Automation is in place; physical icon gaps remain future work.

## [AMENDED 2026-06-25]: Config defaults
First-run groups are **hardcoded** in `ConfigService.CreateDefault()` (Work / Browser / Chat + common process names). See [ARCHITECTURE.md](ARCHITECTURE.md#amended-2026-06-25-default-config-seed-first-run). Add App supports **delete from system** for junk discovered exes — see [ARCHITECTURE.md](ARCHITECTURE.md#amended-2026-06-25-add-app--delete-from-system).
