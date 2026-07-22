# CCF Deadlines Windows Tray

A minimal Windows tray companion for CCF Deadlines. It has no main window and
does not keep a local conference database. Hover the tray icon to see upcoming
deadlines fetched from the live CCF Deadlines website; move the pointer away to
hide the panel.

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

The app fetches `https://ccfddl.com/conference/allconf.yml` whenever the panel
is first shown and refreshes it every ten minutes while running. Right-click the
tray icon to exit.
