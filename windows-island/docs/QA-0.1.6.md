# 0.1.6 verification

Verified on Windows on 2026-10-31.

Verified behavior:

- The island is fixed to the top center of the primary display. Compact mode uses
  the horizontal capsule; side docking, rotated side content and dragging are removed.
- Saved left/right dock preferences from older versions are ignored, restoring
  the top layout without changing filters, following or other retained preferences.
- Primary-screen center and taskbar clearance are recalculated after DPI, display
  layout and work-area changes. The physical center error must remain within 1 px.
- Blank-area presses preserve normal expand/pin behavior and cannot move the
  window. Existing control clicks, capture cancellation, Esc and keyboard navigation
  continue to work.
- The settings gear retains filters and current-user startup registration. The
  submission/startup rows use right-aligned animated blue/gray switches; rank,
  research-area and range chips use background selection without checkmarks.
- Settings retain the 320×416 DIP layout and compact footer. The detail return
  action remains an arrow; Copy success shows a check and Copied for approximately
  1.5 seconds, while a failed copy never reports success.
- Selected conference, followed green, urgent red and existing UI animations remain
  unchanged by the removal of docking.

Checks cover loading old left/right preferences, primary top centering, multiple
DPI scales, short/empty lists, settings switch pointer/keyboard activation and
copy success/failure feedback. Startup verification uses an isolated temporary
registry key rather than the actual Windows Run key.

- 51 data, preference compatibility and primary-placement checks passed.
- 127 production UI checks passed via routed WPF input and keyboard handling.
- 9 native topmost, 8 native primary-placement and 11 isolated startup checks passed.

Preview images use the production renderer with sample conference data and a
composited solid background; they must not be described as desktop drag captures.
