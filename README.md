# CCF Deadlines Tray for Windows

An unofficial, lightweight Windows system-tray companion for [CCF Deadlines](https://ccfddl.com/). Hover over the tray icon to check the next conference deadlines, then move away to dismiss it. There is no conventional main window and no local conference database to maintain.

![CCF Deadlines Tray preview](docs/preview.png)

## Features

- Opens on tray hover and closes automatically when the pointer leaves.
- Shows the next deadline plus a scrollable list of upcoming conferences.
- Shows all upcoming abstract and submission deadlines for a conference when you hover over its row, with both source and local times.
- Updates countdowns every second.
- Refreshes conference data from CCF Deadlines every 10 minutes.
- Filters by CCF rank, CORE rank, THCPL rank, and research area.
- Lets you choose the deadline range and number of visible items.
- Opens the selected conference's official website in your default browser.
- Supports system, dark, and light color modes.
- Supports launch at Windows startup.
- Works with different taskbar positions and multi-monitor layouts.

## Requirements

- Windows 10 or Windows 11
- An internet connection for conference data
- Microsoft Edge WebView2 Runtime (included with current Windows installations; the installer can download it when needed)

To build from source, you also need:

- [Rust](https://www.rust-lang.org/tools/install) with the `x86_64-pc-windows-msvc` target
- [Microsoft Visual Studio Build Tools](https://visualstudio.microsoft.com/visual-cpp-build-tools/) with the **Desktop development with C++** workload, MSVC toolchain, and Windows SDK

## Run from source

Download or clone this repository, open PowerShell in its root directory, and run:

```powershell
cargo run --manifest-path .\windows-tray\src-tauri\Cargo.toml
```

The application starts directly in the Windows notification area. If the icon is hidden, open the tray overflow menu or pin it through Windows taskbar settings.

## Build the Windows installer

Install the Tauri CLI once:

```powershell
cargo install tauri-cli --version "^2" --locked
```

Then build the NSIS installer:

```powershell
cd windows-tray
cargo tauri build
```

The installer is written to:

```text
windows-tray\src-tauri\target\release\bundle\nsis\
```

## Usage

1. Start the application and locate **CCF Deadlines** in the notification area.
2. Hover over the icon to show the deadline panel.
3. Move the pointer into the panel to keep it open.
4. Hover over a conference row to inspect its abstract, submission, and cycle deadlines in the source timezone and your local timezone.
5. Select a conference row to open its official website.
6. Use **Filter** to narrow the list by ranking system or research area.
7. Use **Display** to change the deadline range, item count, or startup behavior.
8. Right-click the tray icon and choose **Exit CCF Deadlines** to quit.

## Filters and display settings

| Setting | Options |
| --- | --- |
| CCF Rank | A, B, C, Non-CCF |
| CORE Rank | A\*, A, B, C, Non-CORE |
| THCPL Rank | A, B, Non-THCPL |
| Research Area | AI, databases, software engineering, security, graphics, systems, networks, theory, HCI, and interdisciplinary topics |
| Deadline range | 30, 90, 180, or 365 days |
| Items shown | 3, 4, 5, 10, 20, or 50 |
| Color mode | System, Dark, or Light |
| Launch at startup | On or off |

Filter and display preferences are stored locally on the current Windows account.

## Data and refresh behavior

The application downloads the combined conference YAML from:

1. `https://ccfddl.com/conference/allconf.yml`
2. `https://ccfddl.github.io/conference/allconf.yml`, attempted if the first endpoint fails

Data is fetched when the application starts and every 10 minutes afterward. Countdown values are calculated locally and refreshed once per second. Expired deadlines are removed, and only the nearest future deadline for each conference edition is shown.

The application does not upload personal data. Opening a conference launches its official URL in the system browser.

## Project structure

```text
windows-tray/
|-- src-tauri/       Rust/Tauri backend, tray integration, networking, and autostart
`-- ui/              HTML, CSS, and JavaScript for the hover panel
```

This repository intentionally contains only the Windows tray application. It consumes the dataset published by the upstream CCF Deadlines project instead of bundling the website, conference YAML, CLI, calendar, or browser-extension sources.

## Tests

Run the Rust tests from the repository root:

```powershell
cargo test --manifest-path .\windows-tray\src-tauri\Cargo.toml
```

## Contributing

- For Windows tray bugs or interface changes, open an issue or pull request in this repository.
- For incorrect conference dates, ranks, or links, contribute to the [upstream CCF Deadlines repository](https://github.com/ccfddl/ccf-deadlines).

## Credits

- Conference data: [CCF Deadlines](https://github.com/ccfddl/ccf-deadlines)
- Desktop framework: [Tauri](https://tauri.app/)

## License

Distributed under the [MIT License](LICENSE).
