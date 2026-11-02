# Shape and spacing

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
| Full-width switch rows | 18 | Card radius capped by the 36-DIP row height |
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
