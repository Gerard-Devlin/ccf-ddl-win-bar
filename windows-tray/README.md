# CCF Deadlines Windows Tray

A minimal Windows tray companion for CCF Deadlines. Hover the tray icon for a
temporary preview, or left-click it to pin and focus the panel. Click the icon
again, press Esc, or switch to another window to close it. The panel's Pin / Unpin
button switches between pinned mode and hover preview. Starting the app again
opens the existing instance's panel.

## Development

Requirements:

- Rust with the `x86_64-pc-windows-msvc` target
- Visual Studio C++ build tools
- Microsoft Edge WebView2 (included with current Windows 10/11)

Run from this directory:

```powershell
cargo run --manifest-path .\src-tauri\Cargo.toml
```

Build an installer:

```powershell
cargo install tauri-cli --version "^2"
cargo tauri build
```

The app fetches `https://ccfddl.com/conference/allconf.yml` at startup and refreshes
it every ten minutes while running, falling back to the GitHub mirror if the
request or YAML parsing fails. The last successful response is cached locally and
stays visible when a refresh fails, including after restarting offline. Expired
abstract deadlines advance to the next submission deadline automatically.

Use Refresh / Retry in the panel or Refresh in the tray's right-click menu for an
immediate update. The footer shows the last successful update time and offline
status; hover it for the full failure reason and timestamp. The tray menu also
provides Show and Exit.

## Verification

```powershell
cargo test --manifest-path .\windows-tray\src-tauri\Cargo.toml
python -m pip install playwright
python -m unittest discover -s windows-tray/tests -p 'ui_*.py' -v
```

Run these checks from the repository root. UI tests use headless Microsoft Edge
with a mock Tauri bridge, including a 1.5-second stationary-hover mutation check,
deadline rollover, refresh failures, and offline cache recovery. Tray clicks,
focus loss, Esc, and reopening a running instance also need a Windows smoke test.
