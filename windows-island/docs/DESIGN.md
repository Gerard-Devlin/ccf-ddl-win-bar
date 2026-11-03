# Interface design

The island applies Apple's guidance on [concentric container corners](https://developer.apple.com/documentation/swiftui/concentricrectangle)
and [button shapes](https://developer.apple.com/design/human-interface-guidelines/buttons).
Adjacent inner and outer corners should share a visual center: an inner corner
radius plus its inset gives the outer radius. Apple also discusses this
relationship in [Design for spatial user interfaces](https://developer.apple.com/videos/play/wwdc2023/10076/).

The following DIP values are choices for this compact Windows interface:

| Element | Radius | Relationship |
| --- | --- | --- |
| Island shell | 35 | Existing outer shape |
| Conference and deadline cards | 19 | Shell radius minus the 16-DIP content inset |
| Full-width switch rows | 19 | Shared card radius within a 40-DIP row |
| Filter chips and small controls | 10 | Repeated compact control shape |
| Header and detail actions | 14 | Capsules or circles at a 28-DIP height |
| Rank badges | 6 | Smaller nested label shape |

Cards use 14-DIP horizontal content padding. Local and source dates share a
fixed value column. Header controls share a vertical center and align to the
content edges. The detail title sits 4 DIP after the back button's hit area.
Settings use 6 DIP from section labels to controls and 12 DIP between groups;
detail actions sit 8 DIP below their deadline cards. Existing Inter typography,
black surfaces and semantic countdown colors are retained.

Drawing and routed input use the same fractional rounded bounds. Keyboard focus
expands that outline; hover and press feedback animate backgrounds while text
and icons retain their size and position. The original DynamicWin snapshot is
preserved under `Upstream/`.


## Typography and layout

Inter remains the interface font. Conference names use 14 DIP, secondary dates
and source times use 11.5 DIP, and section labels use 12 DIP. Compact countdowns
use equal-width digit advances. The compact shell has a fixed 224 × 35 DIP size,
so changing countdown units cannot move the title or resize the shell.
Long conference names wrap in details and move the cards and actions down together.
Research-area abbreviations have expanded tooltips and accessible names.

Settings separate the deadline filters from the General startup option. Filter
choices are independent rounded buttons with an 8-DIP gap. Date ranges remain
mutually exclusive, but have no surrounding outline. Selected chips use a deeper
background without a leading checkmark. Full-row options use right-aligned
switches. No Reset, Done or animation toggle is present.

## Accessibility and motion

The Windows host exposes native UI Automation names, button invocation, switch
and multiselect states, and mutually exclusive range selection. Focused controls
have a visible outline. Routine countdown updates do not raise live notifications.
Followed and urgent state is described to assistive tools and in the conference
hint; no extra corner symbols are drawn on the compact island or list cards.

The app follows Windows text enlargement and rasterizes at the combined display
DPI and layout scale. Content that exceeds the available height remains reachable
by wheel and keyboard focus with no visible scrollbars. The All list also uses
subtle edge fades. Windows high-contrast colors override the ordinary palette,
and the system animation preference disables spring, fade and press motion.
There is no in-app animation switch.

The urgent red meets 4.5:1 text contrast against normal, hover and pressed card
surfaces. Followed countdowns keep their green priority in the normal theme.
Errors and refresh results use brief feedback, while Copy confirms success in
place. The bottom of each page stays free of permanent status rows.

These choices interpret Apple's [accessibility](https://developer.apple.com/design/human-interface-guidelines/accessibility),
[typography](https://developer.apple.com/design/human-interface-guidelines/typography),
[motion](https://developer.apple.com/design/human-interface-guidelines/motion),
[toggles](https://developer.apple.com/design/human-interface-guidelines/toggles)
and [feedback](https://developer.apple.com/design/human-interface-guidelines/feedback)
guidance for a Windows desktop companion. The DIP values are project choices,
not an Apple certification or a universal set of required measurements.
