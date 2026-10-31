# CCF Deadline Island — Development

Development guide for the Windows desktop companion to CCF conference deadlines.
See the [project README](../README.md) for downloads and the [demo](../docs/demo.gif).

## Requirements

- Windows 10 or 11, x64.
- .NET 8 SDK.
- NSIS 3, required only when creating an installer.

Run the following commands from the repository root in PowerShell.

## Run

```powershell
dotnet run --project windows-island/CcfDeadlineIsland.csproj
```

## Build and package

Build the self-contained Windows x64 application and installer:

```powershell
./windows-island/Build.ps1
```

Publish the application without creating an installer:

```powershell
./windows-island/Build.ps1 -PublishOnly
```

Version 0.1.6 outputs:

```text
artifacts/windows-island-0.1.6/
artifacts/CCF-Deadline-Island_0.1.6_x64-setup.exe
```

Distribute the complete publish directory when distributing the application
without its installer. Keep the bundled assets and license notices with it.
The self-contained release does not require a separate .NET installation.

The build script uses a local `.tools/dotnet` SDK when available, then checks PATH.
It discovers NSIS from PATH or a standard local installation. Set `MAKENSIS` to
the full path of `makensis.exe` when automatic discovery is unsuitable.
NuGet packages default to `.tools/nuget`; `NUGET_PACKAGES` can override that cache.

```powershell
$env:MAKENSIS = 'C:\Program Files (x86)\NSIS\makensis.exe'
$env:NUGET_PACKAGES = 'D:\Caches\NuGet'
```

## Tests

Run the data, persistence and placement checks:

```powershell
dotnet run --project windows-island/Tests/Island.Tests.csproj -c Release
```

Add a live check against the conference data source:

```powershell
dotnet run --project windows-island/Tests/Island.Tests.csproj -c Release -- --live
```

Build and render the production UI offscreen:

```powershell
dotnet build windows-island/CcfDeadlineIsland.csproj -c Release
dotnet 'windows-island/bin/Release/net8.0-windows/CCF Deadline Island.dll' --render-preview artifacts/ui-checks
```

The renderer runs UI and Windows integration checks without opening a visible
desktop window. It writes page images, `geometry.json` and `checks.txt` to the
output directory. The published executable accepts the same `--render-preview`
argument. Release verification is recorded in [QA-0.1.6](docs/QA-0.1.6.md).

## Local storage

Application preferences and the deadline cache are stored in
`%LOCALAPPDATA%\CCFDeadlineIsland`. The current-user installation directory is
`%LOCALAPPDATA%\Programs\CCF Deadline Island`. Uninstalling retains preferences
and cached conference data.

## Credits and license

The UI is based on [DynamicWin](https://github.com/FlorianButz/DynamicWin) by
Florian Butz. Conference data is provided by
[CCF Deadlines](https://github.com/ccfddl/ccf-deadlines).
See [NOTICE](NOTICE.md) for attribution and third-party notices, and
[LICENSE](LICENSE) for the project license.
