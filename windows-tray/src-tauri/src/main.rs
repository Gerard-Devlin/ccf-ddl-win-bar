#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::{
    collections::HashSet,
    sync::{
        Arc,
        atomic::{AtomicBool, AtomicU64, Ordering},
    },
    time::Duration,
};

use chrono::{DateTime, FixedOffset, NaiveDateTime, Offset, TimeZone, Utc};
use chrono_tz::Tz;
use serde::{Deserialize, Serialize};
use tauri::{
    AppHandle, Manager, PhysicalPosition, PhysicalSize, Rect,
    menu::{Menu, MenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
};
use tauri_plugin_autostart::{MacosLauncher, ManagerExt};
use tauri_plugin_opener::OpenerExt;

const DATA_URLS: [&str; 2] = [
    "https://ccfddl.com/conference/allconf.yml",
    "https://ccfddl.github.io/conference/allconf.yml",
];
const POPOVER_LABEL: &str = "popover";
const TRAY_ID: &str = "ccf-deadlines";
const HIDE_DELAY: Duration = Duration::from_millis(430);
const BASE_POPOVER_WIDTH: u32 = 240;
const GROUP_FLYOUT_WIDTH: u32 = 440;
const OPTIONS_FLYOUT_WIDTH: u32 = 640;

#[derive(Default)]
struct HoverState {
    pinned: AtomicBool,
    tray_hovered: AtomicBool,
    window_hovered: AtomicBool,
    hide_generation: AtomicU64,
}

#[derive(Debug, Deserialize)]
struct Conference {
    title: String,
    sub: String,
    #[serde(default)]
    rank: ConferenceRank,
    #[serde(default)]
    confs: Vec<ConferenceYear>,
}

#[derive(Debug, Default, Deserialize)]
struct ConferenceRank {
    #[serde(default = "default_rank")]
    ccf: String,
    #[serde(default = "default_rank")]
    core: String,
    #[serde(default = "default_rank")]
    thcpl: String,
}

#[derive(Debug, Deserialize)]
struct ConferenceYear {
    year: i32,
    id: String,
    #[serde(default)]
    link: String,
    #[serde(default)]
    timeline: Vec<Timeline>,
    #[serde(default = "default_timezone")]
    timezone: String,
}

#[derive(Debug, Deserialize)]
struct Timeline {
    #[serde(default)]
    abstract_deadline: Option<String>,
    deadline: String,
    #[serde(default)]
    timezone: Option<String>,
    #[serde(default)]
    comment: Option<String>,
}

#[derive(Clone, Debug, Serialize, PartialEq)]
struct DeadlineDetail {
    deadline_type: &'static str,
    comment: Option<String>,
    deadline: String,
    timezone: String,
    deadline_ms: i64,
}

#[derive(Debug, Serialize, PartialEq)]
struct DeadlineItem {
    id: String,
    title: String,
    sub: String,
    rank: String,
    core_rank: String,
    thcpl_rank: String,
    year: i32,
    deadline_type: &'static str,
    deadline_ms: i64,
    link: String,
    deadlines: Vec<DeadlineDetail>,
}

fn default_timezone() -> String {
    "UTC".into()
}

fn default_rank() -> String {
    "N".into()
}

fn parse_naive(value: &str) -> Option<NaiveDateTime> {
    ["%Y-%m-%d %H:%M:%S", "%Y-%m-%d %H:%M"]
        .iter()
        .find_map(|format| NaiveDateTime::parse_from_str(value, format).ok())
}

fn parse_utc_offset(value: &str) -> Option<FixedOffset> {
    if value == "UTC" {
        return Some(Utc.fix());
    }

    let raw = value.strip_prefix("UTC")?;
    let sign = match raw.as_bytes().first().copied()? {
        b'+' => 1,
        b'-' => -1,
        _ => return None,
    };
    let parts: Vec<&str> = raw[1..].split(':').collect();
    let hours: i32 = parts.first()?.parse().ok()?;
    let minutes: i32 = parts.get(1).and_then(|part| part.parse().ok()).unwrap_or(0);
    FixedOffset::east_opt(sign * (hours * 3600 + minutes * 60))
}

fn deadline_to_utc(value: &str, timezone: &str) -> Option<DateTime<Utc>> {
    let naive = parse_naive(value)?;

    if timezone == "AoE" {
        return FixedOffset::west_opt(12 * 3600)?
            .from_local_datetime(&naive)
            .single()
            .map(|date| date.with_timezone(&Utc));
    }

    if let Some(offset) = parse_utc_offset(timezone) {
        return offset
            .from_local_datetime(&naive)
            .single()
            .map(|date| date.with_timezone(&Utc));
    }

    let named_timezone = if timezone == "PT" {
        chrono_tz::America::Los_Angeles
    } else {
        timezone.parse::<Tz>().ok()?
    };

    named_timezone
        .from_local_datetime(&naive)
        .earliest()
        .map(|date| date.with_timezone(&Utc))
}

fn collect_deadlines(conferences: Vec<Conference>, now: DateTime<Utc>) -> Vec<DeadlineItem> {
    let mut deadlines = Vec::new();
    let now_ms = now.timestamp_millis();

    for conference in conferences {
        for edition in conference.confs {
            let mut edition_deadlines = Vec::new();

            for timeline in edition.timeline {
                let timezone = timeline
                    .timezone
                    .unwrap_or_else(|| edition.timezone.clone());
                let candidates = [
                    (timeline.abstract_deadline, "Abstract Deadline"),
                    (Some(timeline.deadline), "Submission Deadline"),
                ];

                for (value, deadline_type) in candidates {
                    let Some(value) = value else {
                        continue;
                    };
                    let Some(deadline) = deadline_to_utc(&value, &timezone) else {
                        continue;
                    };

                    edition_deadlines.push(DeadlineDetail {
                        deadline_type,
                        comment: timeline.comment.clone(),
                        deadline: value,
                        timezone: timezone.clone(),
                        deadline_ms: deadline.timestamp_millis(),
                    });
                }
            }

            edition_deadlines.sort_by_key(|deadline| deadline.deadline_ms);
            let Some(next_deadline) = edition_deadlines
                .iter()
                .find(|deadline| deadline.deadline_ms > now_ms)
                .cloned()
            else {
                continue;
            };

            deadlines.push(DeadlineItem {
                id: edition.id,
                title: conference.title.clone(),
                sub: conference.sub.clone(),
                rank: conference.rank.ccf.clone(),
                core_rank: conference.rank.core.clone(),
                thcpl_rank: conference.rank.thcpl.clone(),
                year: edition.year,
                deadline_type: next_deadline.deadline_type,
                deadline_ms: next_deadline.deadline_ms,
                link: edition.link,
                deadlines: edition_deadlines,
            });
        }
    }

    deadlines.sort_by_key(|deadline| deadline.deadline_ms);
    let mut seen_conferences = HashSet::new();
    deadlines.retain(|deadline| seen_conferences.insert(deadline.id.to_ascii_lowercase()));
    deadlines
}

#[tauri::command]
async fn fetch_deadlines() -> Result<Vec<DeadlineItem>, String> {
    let client = reqwest::Client::builder()
        .timeout(Duration::from_secs(12))
        .user_agent("CCF-Deadlines-Windows-Tray/0.1")
        .build()
        .map_err(|error| format!("Unable to create the network client: {error}"))?;

    fetch_deadlines_from_urls(&client, &DATA_URLS).await
}

async fn fetch_deadlines_from_urls(
    client: &reqwest::Client,
    urls: &[&str],
) -> Result<Vec<DeadlineItem>, String> {
    let mut last_error = String::new();
    for &url in urls {
        let response = match client.get(url).send().await {
            Ok(response) => response,
            Err(error) => {
                last_error = error.to_string();
                continue;
            }
        };

        let response = match response.error_for_status() {
            Ok(response) => response,
            Err(error) => {
                last_error = error.to_string();
                continue;
            }
        };

        let yaml = match response.text().await {
            Ok(yaml) => yaml,
            Err(error) => {
                last_error = error.to_string();
                continue;
            }
        };

        let conferences: Vec<Conference> = match serde_yaml::from_str(&yaml) {
            Ok(conferences) => conferences,
            Err(error) => {
                last_error = format!("Invalid conference data from {url}: {error}");
                continue;
            }
        };
        return Ok(collect_deadlines(conferences, Utc::now()));
    }

    Err(format!(
        "Unable to load the CCF Deadlines data sources: {last_error}"
    ))
}

#[tauri::command]
fn open_url(app: AppHandle, url: String) -> Result<(), String> {
    let parsed =
        tauri::Url::parse(&url).map_err(|_| "Invalid conference website URL".to_string())?;
    if !matches!(parsed.scheme(), "http" | "https") {
        return Err("Only HTTP or HTTPS conference website URLs are allowed".into());
    }

    app.opener()
        .open_url(parsed.as_str(), None::<&str>)
        .map_err(|error| format!("Unable to open the conference website: {error}"))
}

#[tauri::command]
fn get_autostart_enabled(app: AppHandle) -> Result<bool, String> {
    app.autolaunch()
        .is_enabled()
        .map_err(|error| format!("Unable to read startup setting: {error}"))
}

#[tauri::command]
fn set_autostart_enabled(app: AppHandle, enabled: bool) -> Result<(), String> {
    let autostart = app.autolaunch();
    if enabled {
        autostart.enable()
    } else {
        autostart.disable()
    }
    .map_err(|error| format!("Unable to update startup setting: {error}"))
}

fn flyout_width(level: u8) -> u32 {
    match level {
        1 => GROUP_FLYOUT_WIDTH,
        2.. => OPTIONS_FLYOUT_WIDTH,
        _ => BASE_POPOVER_WIDTH,
    }
}

fn resize_popover(app: &AppHandle, logical_width: u32) -> Result<(), String> {
    let window = app
        .get_webview_window(POPOVER_LABEL)
        .ok_or_else(|| "Popover window is unavailable".to_string())?;
    let old_size = window.outer_size().map_err(|error| error.to_string())?;
    let old_position = window.outer_position().map_err(|error| error.to_string())?;
    let scale_factor = window.scale_factor().unwrap_or(1.0);
    let new_width = (f64::from(logical_width) * scale_factor).round() as u32;
    if new_width == old_size.width {
        return Ok(());
    }

    let right_edge = old_position.x + old_size.width as i32;
    window
        .set_size(PhysicalSize::new(new_width, old_size.height))
        .map_err(|error| error.to_string())?;
    window
        .set_position(PhysicalPosition::new(
            right_edge - new_width as i32,
            old_position.y,
        ))
        .map_err(|error| error.to_string())
}

#[tauri::command]
fn set_flyout_level(app: AppHandle, level: u8) -> Result<(), String> {
    resize_popover(&app, flyout_width(level))
}

fn schedule_hide(app: AppHandle, state: Arc<HoverState>) {
    let generation = state.hide_generation.fetch_add(1, Ordering::SeqCst) + 1;
    std::thread::spawn(move || {
        std::thread::sleep(HIDE_DELAY);
        let main_app = app.clone();
        let _ = app.run_on_main_thread(move || {
            if state.hide_generation.load(Ordering::SeqCst) != generation
                || state.pinned.load(Ordering::SeqCst)
                || state.tray_hovered.load(Ordering::SeqCst)
                || state.window_hovered.load(Ordering::SeqCst)
            {
                return;
            }
            hide_popover(&main_app, &state);
        });
    });
}

fn update_pinned(app: &AppHandle, state: &HoverState, pinned: bool) {
    state.pinned.store(pinned, Ordering::SeqCst);
    state.hide_generation.fetch_add(1, Ordering::SeqCst);
    if let Some(window) = app.get_webview_window(POPOVER_LABEL) {
        let _ = window.eval(format!("window.setPopoverPinned?.({pinned})"));
    }
}

fn hide_popover(app: &AppHandle, state: &HoverState) {
    update_pinned(app, state, false);
    state.window_hovered.store(false, Ordering::SeqCst);
    if let Some(window) = app.get_webview_window(POPOVER_LABEL) {
        let _ = window.hide();
        let _ = window.set_focusable(false);
        let _ = window.eval("window.resetFilterFlyouts?.()");
    }
    let _ = resize_popover(app, BASE_POPOVER_WIDTH);
}

fn show_pinned_popover(app: &AppHandle, state: &HoverState) {
    update_pinned(app, state, true);
    if let Some(window) = app.get_webview_window(POPOVER_LABEL) {
        let _ = window.set_focusable(true);
        if !window.is_visible().unwrap_or(false) {
            if let Some(rect) = app
                .tray_by_id(TRAY_ID)
                .and_then(|tray| tray.rect().ok().flatten())
            {
                let scale = window.scale_factor().unwrap_or(1.0);
                let position = rect.position.to_physical::<f64>(scale);
                let size = rect.size.to_physical::<f64>(scale);
                position_popover(
                    app,
                    PhysicalPosition::new(
                        position.x + size.width / 2.0,
                        position.y + size.height / 2.0,
                    ),
                    rect,
                );
            } else {
                let _ = window.center();
            }
        }
        let _ = window.show();
        let _ = window.set_focus();
    }
}

#[tauri::command]
fn get_popover_pinned(state: tauri::State<'_, Arc<HoverState>>) -> bool {
    state.pinned.load(Ordering::SeqCst)
}

#[tauri::command]
fn set_popover_pinned(app: AppHandle, state: tauri::State<'_, Arc<HoverState>>, pinned: bool) {
    if pinned {
        show_pinned_popover(&app, &state);
    } else {
        update_pinned(&app, &state, false);
        if let Some(window) = app.get_webview_window(POPOVER_LABEL) {
            let _ = window.set_focusable(false);
        }
        schedule_hide(app, Arc::clone(state.inner()));
    }
}

#[tauri::command]
fn dismiss_popover(app: AppHandle, state: tauri::State<'_, Arc<HoverState>>) {
    hide_popover(&app, &state);
}

fn dismiss_if_unfocused(app: &AppHandle, state: &HoverState) {
    if state.pinned.load(Ordering::SeqCst)
        && !state.tray_hovered.load(Ordering::SeqCst)
        && app
            .get_webview_window(POPOVER_LABEL)
            .is_some_and(|window| !window.is_focused().unwrap_or(true))
    {
        hide_popover(app, state);
    }
}

fn position_popover(app: &AppHandle, cursor: PhysicalPosition<f64>, tray_rect: Rect) {
    let Some(window) = app.get_webview_window(POPOVER_LABEL) else {
        return;
    };
    let Ok(window_size) = window.outer_size() else {
        return;
    };
    let scale_factor = window.scale_factor().unwrap_or(1.0);
    let tray_position = tray_rect.position.to_physical::<i32>(scale_factor);
    let tray_size = tray_rect.size.to_physical::<u32>(scale_factor);
    let Ok(monitors) = window.available_monitors() else {
        return;
    };

    let cursor_x = cursor.x.round() as i32;
    let cursor_y = cursor.y.round() as i32;
    let monitor = monitors.iter().find(|monitor| {
        let position = monitor.position();
        let size = monitor.size();
        cursor_x >= position.x
            && cursor_x < position.x + size.width as i32
            && cursor_y >= position.y
            && cursor_y < position.y + size.height as i32
    });

    let Some(monitor) = monitor.or_else(|| monitors.first()) else {
        return;
    };
    let monitor_position = monitor.position();
    let monitor_size = monitor.size();
    let min_x = monitor_position.x + 8;
    let min_y = monitor_position.y + 8;
    let max_x = monitor_position.x + monitor_size.width as i32 - window_size.width as i32 - 8;
    let max_y = monitor_position.y + monitor_size.height as i32 - window_size.height as i32 - 8;

    let distances = [
        (cursor_y - monitor_position.y, 0_u8),
        (
            monitor_position.x + monitor_size.width as i32 - cursor_x,
            1_u8,
        ),
        (
            monitor_position.y + monitor_size.height as i32 - cursor_y,
            2_u8,
        ),
        (cursor_x - monitor_position.x, 3_u8),
    ];
    let nearest_edge = distances
        .iter()
        .min_by_key(|(distance, _)| *distance)
        .map(|(_, edge)| *edge)
        .unwrap_or(2);

    let (x, y) = match nearest_edge {
        0 => (
            tray_position.x,
            tray_position.y + tray_size.height as i32 + 8,
        ),
        1 => (
            tray_position.x - window_size.width as i32 - 8,
            tray_position.y + tray_size.height as i32 - window_size.height as i32,
        ),
        3 => (
            tray_position.x + tray_size.width as i32 + 8,
            tray_position.y,
        ),
        _ => (
            tray_position.x + tray_size.width as i32 - window_size.width as i32,
            tray_position.y - window_size.height as i32 - 8,
        ),
    };

    let _ = window.set_position(PhysicalPosition::new(
        x.clamp(min_x, max_x.max(min_x)),
        y.clamp(min_y, max_y.max(min_y)),
    ));
}

fn show_popover(app: &AppHandle, state: &HoverState, position: PhysicalPosition<f64>, rect: Rect) {
    state.tray_hovered.store(true, Ordering::SeqCst);
    state.hide_generation.fetch_add(1, Ordering::SeqCst);
    if state.pinned.load(Ordering::SeqCst) {
        return;
    }
    position_popover(app, position, rect);
    if let Some(window) = app.get_webview_window(POPOVER_LABEL) {
        let _ = window.set_focusable(false);
        let _ = window.show();
    }
}

#[tauri::command]
fn set_window_hovered(app: AppHandle, state: tauri::State<'_, Arc<HoverState>>, hovered: bool) {
    state.window_hovered.store(hovered, Ordering::SeqCst);
    if hovered {
        state.hide_generation.fetch_add(1, Ordering::SeqCst);
    } else {
        schedule_hide(app, Arc::clone(state.inner()));
    }
}

fn main() {
    let hover_state = Arc::new(HoverState::default());

    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(
            |app, _args, _working_directory| {
                let state = app.state::<Arc<HoverState>>();
                show_pinned_popover(app, &state);
            },
        ))
        .manage(Arc::clone(&hover_state))
        .invoke_handler(tauri::generate_handler![
            fetch_deadlines,
            open_url,
            get_autostart_enabled,
            set_autostart_enabled,
            set_flyout_level,
            set_window_hovered,
            get_popover_pinned,
            set_popover_pinned,
            dismiss_popover
        ])
        .plugin(tauri_plugin_autostart::init(
            MacosLauncher::LaunchAgent,
            None,
        ))
        .plugin(tauri_plugin_opener::init())
        .setup(move |app| {
            let show = MenuItem::with_id(app, "show", "Show CCF Deadlines", true, None::<&str>)?;
            let refresh = MenuItem::with_id(app, "refresh", "Refresh", true, None::<&str>)?;
            let quit = MenuItem::with_id(app, "quit", "Exit CCF Deadlines", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show, &refresh, &quit])?;
            let tray_state = Arc::clone(&hover_state);

            let mut tray_builder = TrayIconBuilder::with_id(TRAY_ID)
                .tooltip("CCF Deadlines — hover to preview, click to pin")
                .menu(&menu)
                .show_menu_on_left_click(false)
                .on_menu_event(|app, event| match event.id.as_ref() {
                    "show" | "refresh" => {
                        let state = app.state::<Arc<HoverState>>();
                        show_pinned_popover(app, &state);
                        if event.id.as_ref() == "refresh" {
                            if let Some(window) = app.get_webview_window(POPOVER_LABEL) {
                                let _ = window.eval("window.refreshDeadlines?.()");
                            }
                        }
                    }
                    "quit" => app.exit(0),
                    _ => {}
                })
                .on_tray_icon_event(move |tray, event| {
                    let app = tray.app_handle();
                    match event {
                        TrayIconEvent::Click {
                            button: MouseButton::Left,
                            button_state: MouseButtonState::Up,
                            ..
                        } => {
                            if tray_state.pinned.load(Ordering::SeqCst) {
                                hide_popover(app, &tray_state);
                            } else {
                                show_pinned_popover(app, &tray_state);
                            }
                        }
                        TrayIconEvent::Enter { position, rect, .. } => {
                            show_popover(app, &tray_state, position, rect);
                        }
                        TrayIconEvent::Move { .. } => {
                            tray_state.tray_hovered.store(true, Ordering::SeqCst);
                            tray_state.hide_generation.fetch_add(1, Ordering::SeqCst);
                        }
                        TrayIconEvent::Leave { .. } => {
                            tray_state.tray_hovered.store(false, Ordering::SeqCst);
                            dismiss_if_unfocused(app, &tray_state);
                            schedule_hide(app.clone(), Arc::clone(&tray_state));
                        }
                        _ => {}
                    }
                });

            if let Some(icon) = app.default_window_icon() {
                tray_builder = tray_builder.icon(icon.clone());
            }
            tray_builder.build(app)?;

            if let Some(window) = app.get_webview_window(POPOVER_LABEL) {
                let window_app = app.handle().clone();
                let window_state = Arc::clone(&hover_state);
                window.on_window_event(move |event| {
                    match event {
                        tauri::WindowEvent::CloseRequested { api, .. } => {
                            api.prevent_close();
                            hide_popover(&window_app, &window_state);
                        }
                        // A tray click also takes focus; let its click handler toggle the pin.
                        tauri::WindowEvent::Focused(false) => {
                            dismiss_if_unfocused(&window_app, &window_state)
                        }
                        _ => {}
                    }
                });
            }

            Ok(())
        })
        .run(tauri::generate_context!())
        .expect("failed to run CCF Deadlines tray application");
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn falls_back_to_mirror_when_primary_yaml_is_invalid() {
        use std::{
            io::{Read, Write},
            net::TcpListener,
        };

        let server = TcpListener::bind("127.0.0.1:0").unwrap();
        let address = server.local_addr().unwrap();
        let worker = std::thread::spawn(move || {
            for body in [
                "not: [valid",
                "- title: MIRROR\n  sub: AI\n  confs:\n    - year: 2099\n      id: mirror99\n      timeline:\n        - deadline: '2099-01-01 00:00:00'\n",
            ] {
                let (mut stream, _) = server.accept().unwrap();
                stream
                    .set_read_timeout(Some(Duration::from_secs(5)))
                    .unwrap();
                let mut request = [0; 4096];
                stream.read(&mut request).unwrap();
                write!(
                    stream,
                    "HTTP/1.1 200 OK\r\nContent-Length: {}\r\nConnection: close\r\n\r\n{body}",
                    body.len()
                )
                .unwrap();
            }
        });
        let primary = format!("http://{address}/primary");
        let mirror = format!("http://{address}/mirror");
        let client = reqwest::Client::builder()
            .no_proxy()
            .timeout(Duration::from_secs(5))
            .build()
            .unwrap();
        let deadlines = tauri::async_runtime::block_on(fetch_deadlines_from_urls(
            &client,
            &[&primary, &mirror],
        ))
        .unwrap();
        assert_eq!(deadlines.len(), 1);
        assert_eq!(deadlines[0].id, "mirror99");
        worker.join().unwrap();
    }

    #[test]
    fn parses_aoe_and_utc_offsets() {
        let aoe = deadline_to_utc("2026-07-22 23:59:00", "AoE").unwrap();
        assert_eq!(aoe.to_rfc3339(), "2026-07-23T11:59:00+00:00");

        let utc_eight = deadline_to_utc("2026-07-22 20:00:00", "UTC+8").unwrap();
        assert_eq!(utc_eight.to_rfc3339(), "2026-07-22T12:00:00+00:00");
    }

    #[test]
    fn collects_all_details_and_uses_the_nearest_future_deadline() {
        let yaml = r#"
    - title: FIRST
      sub: AI
      rank:
        ccf: A
      confs:
        - year: 2027
          id: first27
          link: https://example.com/first
          timezone: UTC
          timeline:
            - deadline: '2027-01-02 00:00:00'
    - title: SECOND
      sub: SE
      rank:
        ccf: B
        core: A*
        thcpl: A
      confs:
        - year: 2027
          id: second27
          link: https://example.com/second
          timezone: UTC
          timeline:
            - deadline: '2026-11-15 00:00:00'
              comment: Previous cycle
            - abstract_deadline: '2027-01-01 00:00:00'
              deadline: '2027-01-03 00:00:00'
              timezone: UTC+8
              comment: Main track
            - deadline: TBD
              comment: Unparseable date
    - title: PAST
      sub: AI
      confs:
        - year: 2026
          id: past26
          timezone: UTC
          timeline:
            - deadline: '2026-10-01 00:00:00'
"#;
        let data: Vec<Conference> = serde_yaml::from_str(yaml).unwrap();
        let now = DateTime::parse_from_rfc3339("2026-12-01T00:00:00Z")
            .unwrap()
            .with_timezone(&Utc);
        let deadlines = collect_deadlines(data, now);

        assert_eq!(deadlines.len(), 2);
        assert_eq!(deadlines[0].id, "second27");
        assert_eq!(deadlines[0].deadline_type, "Abstract Deadline");
        assert_eq!(
            deadlines[0].deadline_ms,
            deadline_to_utc("2027-01-01 00:00:00", "UTC+8")
                .unwrap()
                .timestamp_millis()
        );
        assert_eq!(deadlines[0].core_rank, "A*");
        assert_eq!(deadlines[0].thcpl_rank, "A");
        assert_eq!(deadlines[0].deadlines.len(), 3);

        let details = &deadlines[0].deadlines;
        assert_eq!(details[0].deadline_type, "Submission Deadline");
        assert_eq!(details[0].comment.as_deref(), Some("Previous cycle"));
        assert_eq!(details[0].deadline, "2026-11-15 00:00:00");
        assert_eq!(details[0].timezone, "UTC");
        assert!(details[0].deadline_ms < now.timestamp_millis());

        assert_eq!(details[1].deadline_type, "Abstract Deadline");
        assert_eq!(details[1].comment.as_deref(), Some("Main track"));
        assert_eq!(details[1].deadline, "2027-01-01 00:00:00");
        assert_eq!(details[1].timezone, "UTC+8");
        assert_eq!(details[1].deadline_ms, deadlines[0].deadline_ms);

        assert_eq!(details[2].deadline_type, "Submission Deadline");
        assert_eq!(details[2].comment.as_deref(), Some("Main track"));
        assert_eq!(details[2].deadline, "2027-01-03 00:00:00");
        assert_eq!(details[2].timezone, "UTC+8");
        assert!(details[2].deadline_ms > details[1].deadline_ms);

        assert_eq!(deadlines[1].id, "first27");
        assert_eq!(deadlines[1].deadline_type, "Submission Deadline");
        assert_eq!(deadlines[1].core_rank, "N");
        assert_eq!(deadlines[1].thcpl_rank, "N");
    }

    #[test]
    fn maps_filter_flyout_levels_to_window_widths() {
        assert_eq!(flyout_width(0), 240);
        assert_eq!(flyout_width(1), 440);
        assert_eq!(flyout_width(2), 640);
        assert_eq!(flyout_width(9), 640);
    }
}
