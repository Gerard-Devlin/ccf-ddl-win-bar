# 0.1.2 verification

Verified on Windows on 2026-10-31 using the production WPF/Skia renderer.

- Removed the complete list footer and reduced the three-row panel to 320×218 DIP.
- Added small Website, Copy and Calendar icons. Copy replaces Copy times.
- Removed the detail footer's deadline-count text and its unused space; actual
  pagination buttons remain available when more than two dates exist.
- Added ten research-area toggles, All/Clear and immediate preference persistence;
  the filter panel is now 320×380 DIP.
- Compact title, countdown and width follow the selected conference. Selection is
  tracked by ID across data reordering; an unavailable selection falls back to the
  nearest visible conference.
- Removed foreground/fullscreen auto-hide, including the case where a fullscreen
  window on the secondary display hid the primary-screen island. Native topmost
  state is maintained on load, reveal, deactivation and the existing one-second
  timer, without activating the window. Modal/context-menu operations and explicit
  user Hide are respected. Preview uses the same topmost behavior.

92 production UI checks passed, including routed pointer and keyboard input,
research-area filtering/persistence and selected-conference compact display.
9 additional checks used an owned hidden native HWND to verify topmost promotion,
repair after external demotion, unchanged bounds, hidden-state preservation and no
foreground-focus theft. No desktop window was opened by these automated checks.

Screenshots use sample conferences: [upcoming](screenshots/upcoming.png),
[filters](screenshots/filters.png), [details](screenshots/details.png).
The earlier data, calendar and placement coverage is recorded in
[QA-0.1.1](QA-0.1.1.md).
