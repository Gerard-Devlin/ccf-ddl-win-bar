# 0.1.4 verification

Verified on Windows on 2026-10-31.

- The header has Upcoming, All and the settings gear. Filters and Launch at startup
  share the 320×432 DIP settings page; there is no animation preference.
- Animations remain enabled, including a draw-only press/release spring, selected
  background transition and checkmark fade. Input bounds stay fixed. Checkmarks
  retain at least 6 DIP separation from labels throughout the transition.
- Positive countdowns at most 24 hours away use the existing red error color in
  compact mode, conference rows and deadline details.
- Current-user startup registration uses a quoted executable path. DLL/runtime
  hosts are rejected, and disabling removes only this application's value.
- Blank-area dragging uses WPF capture and a movement threshold. The supported
  docks are primary-screen top, left and right. Top remains horizontally centered;
  sides preserve a normalized vertical position. There is no bottom dock. Presses
  on controls retain their normal click path; Esc, blur and capture loss cancel a drag.
- Dock placement follows the visible animated island rather than the transparent
  host rectangle. Native DPI/client bounds are re-read when a move changes DPI.

Checks passed:

- 215 data, persistence, calendar and placement checks, including 100/125/150/200%
  calculations, edge clamps, dock normalization, no-bottom selection and saved position.
- 113 production UI checks through routed WPF pointer events and shared keyboard
  input, including settings startup callbacks and intermediate animation states.
- 9 native topmost and 15 native docking checks using owned hidden HWNDs. Dock
  checks cover actual physical edge alignment, expansion, preview/cancel and commit.
- 11 startup checks in an isolated temporary registry key. Verification never
  modifies the actual Windows Run key or opens a visible desktop window.

Screenshots use the production renderer and sample conferences:
[settings](screenshots/settings.png), [urgent countdown](screenshots/urgent.png),
[compact urgent countdown](screenshots/urgent-compact.png).
