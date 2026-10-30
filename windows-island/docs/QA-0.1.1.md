# 0.1.1 verification

Verified on Windows on 2026-10-31. All screenshots use sample conferences and the
production DynamicWin renderer; no alternate HTML mockup is involved.

- 45 deterministic data, persistence, calendar and physical-placement checks passed.
- 85 production UI checks passed using routed WPF pointer down/up events and the
  shared keyboard controller, including clicks between render frames, repeated
  rank changes, checkbox-label clicks, drag-out release, duplicate release,
  blur/page/visibility/rebinding cancellation, Tab/Shift+Tab/Space/Enter,
  persisted filter/display preferences, empty and zero-to-three-row layouts,
  long names, expiry promotion, full-list scrolling and deadline pagination.
- Native WPF input was separately exercised through Windows mouse/keyboard
  injection: checkbox text, Space, Enter, Tab with visible focus, repeated rank
  clicks, dragging outside a control, Esc contraction, and clicking the compact
  island. The opening click cannot activate navigation while it is moving.
- On the current 2560×1440 primary display at 150% (144 DPI), the compact,
  three-row and filter shells measured at physical center X=1280: **0 px error**.
- A separate native hidden-HWND check crossed to the existing left display at
  200% (192 DPI), returned to primary automatically, and stayed centered after
  window-width changes. No OS display settings were changed.
- Pure placement checks cover 100%, 125%, 150% and 200%, odd/negative monitor
  coordinates, top/side taskbars and non-client border offsets. These additional
  scale cases are calculations, not a claim of visible desktop testing at every scale.
- All 64 files in the upstream hash manifest remain byte-identical.

The native Esc check caught and fixed premature dismissal release during
contraction. Pixel measurement also caught independent integer truncation of
shell position/width; the host now keeps the animated shell center in floating
point before Windows scales it to physical pixels.

Main and filter content sizes are 320×248 and 320×288 DIP. Short lists shrink
vertically. Long names use ellipses to preserve the rank badge and countdown
columns. The full list includes every filtered meeting rather than the old
short-list count limit. Preference restart coverage uses the production
Save/Load functions and temporary storage, leaving installed-app settings intact.

Screenshots: [upcoming](screenshots/upcoming.png), [filters](screenshots/filters.png),
[details](screenshots/details.png), [compact](screenshots/compact.png).
