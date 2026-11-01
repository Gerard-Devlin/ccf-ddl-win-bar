# 0.1.8 verification

Verified on Windows on 2026-11-02.

- Removed Reset filters and Done from the settings footer.
- Reduced settings height from 416 to 378 DIP, leaving 20 DIP below the startup
  row. Filters continue to apply and save immediately.
- Upcoming and All remain available in the header for leaving settings.
- Empty filter results retain their recovery action.

128 production UI checks, 80 temporal text checks, 9 native topmost checks,
8 native primary-placement checks and 11 isolated startup checks passed.
Text checks use offscreen 100%, 125%, 150% and 200% canvas scales.

The existing checks exercise routed pointer and keyboard events. The removed
footer references were replaced with the empty-results recovery action and the
Copy button for icon-label animation coverage. Preview images use sample data
and the production renderer without opening a visible desktop window.
