# 0.1.10 verification

Verified on Windows on 2026-11-04. Design rationale and official references are
recorded in [Interface design](DESIGN.md).

## Visual review

Production-renderer screenshots were inspected for settings, details, long
conference names, compact/expanded lists, followed conferences, urgent deadlines,
copy feedback, loading and empty results. The settings page has no surrounding
range outline, no chip checkmarks, no footer Reset/Done buttons and no animation
switch. No extra bookmark or urgency symbols appear on cards or the compact island.
The Follow action remains in details. The README animation is unchanged.

- [Settings](screenshots/0.1.10-settings.png)
- [Conference details](screenshots/0.1.10-details.png)
- [Long conference title](screenshots/0.1.10-long-title-details.png)

## Automated verification

The release build and self-contained published executable run the same suite:

- 139 production UI checks through WPF routed pointer events and keyboard input.
- 80 temporal text checks at 100%, 125%, 150% and 200% rendering scales.
- 26 accessibility and boundary checks: native automation patterns, names,
  mutually exclusive date ranges, stable countdown digit advances and shell width,
  long titles, reduced motion, and urgent-text contrast.
- 25 native WPF layout checks at 100%, 125%, 150%, 200% and 225% text enlargement:
  no visible scrollbars, settled settings geometry, primary-screen centering within
  one physical pixel, coordinate round trips within one physical pixel, and the
  final switch reachable through focus scrolling.
- 9 native topmost checks, 8 native primary-placement checks, and 11 isolated
  startup-registration checks.
- 51 data, caching, settings, timezone, deadline-expiry, fallback and physical
  placement checks.

Native layout checks use owned hidden HWNDs and the production layout transform;
they do not change the user's system text-size or animation settings. Display-scale
cases in the data suite use physical placement fixtures, while the native tests
use the attached primary display.

## Desktop observation and limits

The isolated preview exposed real Windows UI Automation nodes for the navigation,
rank and research-area choices, radio date ranges, and the two switches. The
settings screen was opened through Windows input and inspected. Live diagnostics
on the 2880 × 1800 primary display at 192 DPI recorded zero physical-pixel center
deviation. Further desktop automation was stopped with Escape and was not resumed.
Final changes were checked with production rendering and routed-input tests.

Narrator speech output, every Windows high-contrast theme and a physical monitor
hot-plug sequence were not manually exercised. This release does not claim Apple
HIG certification or full WCAG conformance.
