# 0.1.7 verification

Verified on Windows on 2026-11-01.

- Text measurement and drawing share Inter font metrics, fractional glyph
  positioning and light hinting to avoid uneven small-text spacing.
- Button and conference-row backgrounds retain spring feedback. Text and icons
  keep their size and position during hover, press and release.
- Compact labels measure advance width and font height before drawing. Drawing
  does not mutate their layout, and idle labels remain stable across frames.
- The archived DynamicWin source snapshot remains unchanged.

Checks:

- 51 data, cache, preference compatibility and primary-placement checks.
- 127 production UI checks using routed WPF pointer and keyboard input.
- 80 temporal text checks at 100%, 125%, 150% and 200% offscreen canvas scales.
  Foreground pixels are compared through hover, press and cancelled release;
  compact labels are compared over 180 idle frames. Background feedback is also
  checked so stable text does not remove the button animation.
- 9 native topmost, 8 native primary-placement and 11 isolated startup checks.
- Negative control: the pre-fix renderer fails the same foreground stability
  check for Reset filters during hover at 100% scale.

The temporal checks exercise the production renderer offscreen. They do not
simulate a physical monitor's DPI-change event or prove text stability during
every expansion transition. Preview images use sample conference data.

Run the published executable with `--render-preview <output-directory>` to
generate the page images, `checks.txt`, `text-rendering-checks.txt` and
`geometry.json`. Startup checks use an isolated temporary registry key.
