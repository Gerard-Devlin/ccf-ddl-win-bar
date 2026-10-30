# CCF Deadline Island — attribution and distribution notices

This adaptation of [DynamicWin](https://github.com/FlorianButz/DynamicWin) by
[Florian Butz](https://github.com/FlorianButz) is distributed under
[Creative Commons Attribution-ShareAlike 4.0 International](https://creativecommons.org/licenses/by-sa/4.0/).
The complete license is included in `LICENSE` and `Upstream/DynamicWin/LICENSE`.
Changes to this adaptation are by CCF Deadline Island contributors, 2026.

The UI source comes from DynamicWin's public `main` branch at commit
`9f24d176a16333b1cfda21dd3ed48087497194ed`. Its default `V2` branch is release-only;
no closed V2 source is included or claimed. The 63 copied source/resource files
retain their original paths under `Upstream/DynamicWin`; `VENDOR-MANIFEST.json`
records their original SHA-256 hashes and sizes.

The original Skia drawing primitives, `SecondOrder` motion, Inter/Cascadia font
resources, and icon resources are reused. Version 0.1.1 adapts the menu into a
three-conference list and compact domain filters. The archived upstream snapshot
remains byte-identical. Its `UI/UIObject.cs` is excluded from compilation in favor
of `DynamicWinHost/Input/UIObject.cs`, which replaces frame-polled clicks with
host mouse/keyboard events, input capture, explicit hit areas, and focus state.
Other integration adds deadline details, cached data, calendar export, and
physical-pixel primary-monitor placement.

Third-party texts are included in `licenses/`:

- YamlDotNet 16.3.0: MIT; `YamlDotNet-MIT.txt`.
- SkiaSharp and SkiaSharp.NativeAssets.Win32 2.88.8: MIT and accompanying third-party notices.
- Inter: SIL Open Font License 1.1; `Inter-OFL.txt`.
- Cascadia: SIL Open Font License 1.1; `Cascadia-OFL.txt`.
- Self-contained distributions include the resolved Microsoft .NET and Windows
  Desktop runtime license texts, plus the .NET runtime's third-party notices.
  `Build.ps1` copies these from the exact runtime packages used by publish.

Conference data is fetched from [CCF Deadlines](https://github.com/ccfddl/ccf-deadlines).
This is an unofficial companion, with no affiliation or endorsement by
DynamicWin's author or CCF.
