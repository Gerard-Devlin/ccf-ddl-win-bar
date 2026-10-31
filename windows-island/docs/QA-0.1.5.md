# 0.1.5 verification

Windows verification record for 2026-10-31.

- Top docking retains the horizontal compact capsule and physical primary-screen
  center. Left and right compact docks use a thin edge strip: 35 DIP wide and as
  long as the horizontal compact capsule, normally 200 DIP. The screen-edge side
  is flat and the inward-facing side retains rounded corners.
- The conference title and countdown remain one row, rotated along the strip's
  long edge: −90° on the left (title below, countdown above), and +90° on the right
  (title above, countdown below).
- Left docks expand rightward and right docks expand leftward into the existing
  readable 320 DIP panel. Expanded-panel text uses its normal reading direction.
  Shape springs and content fades remain enabled, including during orientation
  changes; the compact row is rotated without stretching its text.
- Effective dock direction includes a drag preview. Side positions continue to
  use the existing saved preferences, with no bottom dock or preference migration.
- Drag freezing no longer infers expansion from height alone: a side strip
  can be taller than an expanded short list. Content readiness accounts for both
  dimensions, while native placement follows the visible animated bounds.
- Copy success shows a checkmark and Copied for approximately 1.5 seconds before
  restoring Copy. A failed copy never shows a success state.
- The detail back action is an arrow. Settings use a 320×416 DIP panel with its
  footer at Y=372; the reset icon and label have a 4 DIP gap.
- Submission deadlines only and Launch at startup use right-aligned blue/gray
  switches. Whole-row pointer activation, Tab/Space/Enter and smooth knob/color
  transitions are retained.
- Rank, area and range chips use only a selected background with centered text;
  no checkmarks or reserved indicator space remain in these chips.
- Selected conference, followed green, urgent red, long-name ellipses, empty
  states and current-user startup behavior remain part of the regression scope.

Verification on the final edge-strip and mark-free filter layout:

- 293 data, persistence and pure placement checks passed, including compact and
  expanded dimensions at multiple DPI scales and saved dock restoration.
- 151 production UI checks passed through routed WPF pointer events and the shared
  keyboard input path.
- 9 native topmost and 18 native docking checks passed using owned hidden HWNDs;
  they check physical edge/center alignment and expansion/contraction placement.
- 11 startup-registration checks passed in an isolated temporary registry key.

The routed checks activate switch labels and the right-hand track, check keyboard
activation, and verify intermediate animation states without shifting hit areas.

Preview images use the production renderer with sample conference data and a
composited solid background. Side examples are rendered at 150% scale. They are not
screenshots of a real desktop drag.
Native automated checks keep test windows hidden and never change the actual
Windows Run key.

Production-rendered examples:
[left compact](screenshots/left-docked-preview.png),
[right compact](screenshots/right-docked-preview.png),
[right expanded](screenshots/right-expanded-preview.png),
[settings switches](screenshots/settings-switches-on.png), and
[copy feedback](screenshots/copy-feedback.png).
