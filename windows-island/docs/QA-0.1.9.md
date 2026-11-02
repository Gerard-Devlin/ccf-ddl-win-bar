# 0.1.9 verification

Verified on Windows on 2026-11-02.

The visual pass applies the relationships documented in [design notes](DESIGN.md):

- Card corners follow the shell radius and content inset. Filter chips, switch
  rows, capsules, icon buttons and badges have consistent roles and radii.
- Header icons share size and alignment. The detail title sits 4 DIP after the
  back button, and detail actions sit 8 DIP below the last card.
- Settings labels have 6 DIP before their controls and 12 DIP between groups.
  Card and switch-row contents share 14-DIP horizontal padding.
- Conference titles align with rank badges. Local and source time values share
  a column. Details retain the originating navigation highlight.
- Loading text is centered when the recovery button is absent. Empty states,
  long titles and single/two-deadline details have been rendered for inspection.
- Pagination uses the same stroked arrow icons as detail navigation.
- All-list keyboard scrolling reaches later conferences. Enter and Space use
  the same layout-readiness guard as pointer activation.

139 production UI checks, 80 temporal text checks, 9 native topmost checks,
8 native primary-placement checks and 11 isolated startup checks passed.
Routed corner clicks check visible and transparent parts of rounded cards.
Keyboard checks cover scrolling, page boundaries and activation during expansion.

Geometry assertions allow the existing half-DIP spring settling tolerance used
by production input readiness. Text checks use offscreen 100%, 125%, 150% and 200%
canvas scales. Page images use sample data and the production renderer; they
are not desktop captures or physical DPI-transition tests.
