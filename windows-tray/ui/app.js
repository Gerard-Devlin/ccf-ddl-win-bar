const nextDeadline = document.querySelector("#next-deadline");
const list = document.querySelector("#deadline-list");
const filterOptions = document.querySelector("#filter-options");
const filterOptionsTitle = document.querySelector("#filter-options-title");
const filterGroups = document.querySelector("#filter-groups");
const filterSummary = document.querySelector("#filter-summary");
const withinDays = document.querySelector("#within-days");
const displayCount = document.querySelector("#display-count");
const autostartToggle = document.querySelector("#autostart-toggle");
const invoke = window.__TAURI__?.core?.invoke;

const ccfRanks = ["A", "B", "C", "N"];
const coreRanks = ["A*", "A", "B", "C", "N"];
const thcplRanks = ["A", "B", "N"];
const categories = [
  ["AI", "Artificial Intelligence"], ["DB", "Database"], ["SE", "Software Engineering"], ["SC", "Security"],
  ["CG", "Graphics & Multimedia"], ["DS", "Architecture & Storage"], ["NW", "Networks"],
  ["CT", "Theory"], ["HI", "Human-Computer Interaction"], ["MX", "Interdisciplinary"],
];
const defaults = {
  ranks: [...ccfRanks],
  coreRanks: [...coreRanks],
  thcplRanks: [...thcplRanks],
  categories: categories.map(([code]) => code),
  withinDays: 365,
  displayCount: 20,
  scrollableListVersion: 1,
};

const filterDefinitions = {
  ccf: { title: "CCF Rank", key: "ranks", values: ccfRanks },
  core: { title: "CORE Rank", key: "coreRanks", values: coreRanks },
  thcpl: { title: "THCPL Rank", key: "thcplRanks", values: thcplRanks },
  category: { title: "Research Area", key: "categories", values: categories.map(([code]) => code) },
};

let deadlines = [];
let refreshTimer;
let countdownTimer;
let preferences = loadPreferences();
let autostartEnabled = false;
let autostartBusy = false;
let currentFilterGroup = "ccf";
let flyoutLevel = 0;

const previewDeadlines = [
  { id: "icml26", rank: "A", core_rank: "A*", thcpl_rank: "A", sub: "AI", link: "https://icml.cc/", deadline_ms: Date.now() + 12 * 86400000 + 22 * 3600000 },
  { id: "acl26", rank: "A", core_rank: "A*", thcpl_rank: "A", sub: "AI", link: "https://www.aclweb.org/", deadline_ms: Date.now() + 17 * 86400000 + 22 * 3600000 },
  { id: "issta26", rank: "A", core_rank: "A", thcpl_rank: "A", sub: "SE", link: "https://conf.researchr.org/series/issta", deadline_ms: Date.now() + 18 * 86400000 + 22 * 3600000 },
  { id: "ase26", rank: "A", core_rank: "A*", thcpl_rank: "A", sub: "SE", link: "https://conf.researchr.org/series/ase", deadline_ms: Date.now() + 74 * 86400000 + 22 * 3600000 },
  { id: "sigmod27", rank: "A", core_rank: "A*", thcpl_rank: "A", sub: "DB", link: "https://sigmod.org/", deadline_ms: Date.now() + 91 * 86400000 + 4 * 3600000 },
  { id: "aaai27", rank: "A", core_rank: "A*", thcpl_rank: "A", sub: "AI", link: "https://aaai.org/", deadline_ms: Date.now() + 108 * 86400000 + 9 * 3600000 },
];

function loadPreferences() {
  try {
    const stored = JSON.parse(localStorage.getItem("ccf-tray-preferences") || "{}");
    if (stored.scrollableListVersion !== 1) {
      return { ...defaults, ...stored, displayCount: 20, scrollableListVersion: 1 };
    }
    return { ...defaults, ...stored };
  } catch {
    return { ...defaults };
  }
}

function savePreferences() {
  localStorage.setItem("ccf-tray-preferences", JSON.stringify(preferences));
  renderFilterGroups();
  renderFilterOptions();
  render();
}

function escapeHtml(value) {
  return String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#039;");
}

function formatRemaining(deadlineMs) {
  const totalSeconds = Math.max(0, Math.floor((deadlineMs - Date.now()) / 1000));
  const days = Math.floor(totalSeconds / 86400);
  const hours = Math.floor((totalSeconds % 86400) / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  if (days > 0) return `${days}d ${hours}h ${minutes}m ${seconds}s`;
  if (hours > 0) return `${hours}h ${minutes}m ${seconds}s`;
  if (minutes > 0) return `${minutes}m ${seconds}s`;
  return `${seconds}s`;
}

function row(deadline) {
  const displayId = deadline.id.toUpperCase();
  return `<button class="deadline-row" type="button" data-url="${escapeHtml(deadline.link)}" aria-label="Open ${escapeHtml(displayId)} website"><span class="deadline-name">${escapeHtml(displayId)}</span><span class="deadline-remaining">${formatRemaining(deadline.deadline_ms)}</span></button>`;
}

function visibleDeadlines() {
  const latest = Date.now() + preferences.withinDays * 86400000;
  const seen = new Set();
  return deadlines.filter((deadline) => {
    const conferenceId = deadline.id.toLowerCase();
    if (seen.has(conferenceId)
      || !preferences.ranks.includes(deadline.rank)
      || !preferences.coreRanks.includes(deadline.core_rank || "N")
      || !preferences.thcplRanks.includes(deadline.thcpl_rank || "N")
      || !preferences.categories.includes(deadline.sub)
      || deadline.deadline_ms > latest) return false;
    seen.add(conferenceId);
    return true;
  });
}

function render() {
  const visible = visibleDeadlines().slice(0, preferences.displayCount);
  if (!visible.length) {
    nextDeadline.innerHTML = '<div class="message">No matching conferences</div>';
    list.innerHTML = "";
  } else {
    nextDeadline.innerHTML = row(visible[0]);
    list.innerHTML = visible.slice(1).map(row).join("");
  }

  const allSelected = preferences.ranks.length === ccfRanks.length
    && preferences.coreRanks.length === coreRanks.length
    && preferences.thcplRanks.length === thcplRanks.length
    && preferences.categories.length === categories.length;
  filterSummary.textContent = allSelected ? "All" : "Filtered";
}

function renderFilterGroups() {
  filterGroups.innerHTML = Object.entries(filterDefinitions).map(([type, definition]) => {
    const selected = preferences[definition.key].length;
    const summary = selected === definition.values.length ? "All" : selected === 0 ? "None" : `${selected}/${definition.values.length}`;
    const active = flyoutLevel === 2 && type === currentFilterGroup ? " filter-group-row--active" : "";
    return `<button class="filter-group-row${active}" type="button" data-filter-group="${type}"><span>${definition.title}</span><span class="filter-group-summary">${summary}</span><span class="chevron">›</span></button>`;
  }).join("");
}

function renderFilterOptions() {
  const definition = filterDefinitions[currentFilterGroup];
  filterOptionsTitle.textContent = definition.title;
  filterOptions.innerHTML = definition.values.map((value) => {
    if (currentFilterGroup === "category") {
      const [, name] = categories.find(([code]) => code === value);
      return filterRow(currentFilterGroup, value, name, value, preferences[definition.key].includes(value));
    }
    const system = currentFilterGroup.toUpperCase();
    return filterRow(currentFilterGroup, value, rankLabel(system, value), "", preferences[definition.key].includes(value));
  }).join("");
}

function rankLabel(system, rank) {
  return rank === "N" ? `Non-${system}` : `${system} ${rank}`;
}

function filterRow(type, value, label, code, checked) {
  const codeLabel = code ? `<span class="filter-code">${code}</span>` : "";
  return `<button class="filter-row" type="button" data-filter-type="${type}" data-filter-value="${value}"><span>${label}${codeLabel}</span><span class="filter-check">${checked ? "✓" : ""}</span></button>`;
}

function showView(id) {
  document.querySelectorAll(".menu-view").forEach((view) => view.classList.toggle("menu-view--active", view.id === id));
}

function applyFlyoutLevel(level) {
  flyoutLevel = level;
  document.body.dataset.flyoutLevel = String(level);
  renderFilterGroups();
}

async function setFlyoutLevel(level) {
  applyFlyoutLevel(level);
  if (invoke) {
    try { await invoke("set_flyout_level", { level }); } catch {}
  }
}

function openFilterGroup(group) {
  if (!filterDefinitions[group]) return;
  currentFilterGroup = group;
  renderFilterOptions();
  setFlyoutLevel(2);
}

window.resetFilterFlyouts = () => applyFlyoutLevel(0);

function renderAutostart() {
  autostartToggle.setAttribute("aria-checked", String(autostartEnabled));
  autostartToggle.disabled = autostartBusy;
}

async function loadAutostart() {
  if (!invoke) {
    renderAutostart();
    return;
  }

  try {
    autostartEnabled = await invoke("get_autostart_enabled");
  } catch {
    autostartToggle.disabled = true;
    autostartToggle.title = "Startup setting unavailable";
    return;
  }
  renderAutostart();
}

async function loadDeadlines() {
  if (!invoke) {
    deadlines = previewDeadlines;
    render();
    return;
  }
  try {
    deadlines = await invoke("fetch_deadlines");
    render();
  } catch {
    nextDeadline.innerHTML = '<div class="message">Unable to load conference data</div>';
    list.innerHTML = "";
  }
}

document.querySelector("#open-filters").addEventListener("pointerenter", () => setFlyoutLevel(1));
document.querySelector("#open-filters").addEventListener("click", () => setFlyoutLevel(1));
document.querySelector("#open-settings").addEventListener("pointerenter", () => setFlyoutLevel(0));
document.querySelector("#open-settings").addEventListener("click", () => {
  setFlyoutLevel(0);
  showView("settings-view");
});
document.querySelectorAll("[data-back]").forEach((button) => button.addEventListener("click", () => {
  setFlyoutLevel(0);
  showView("main-view");
}));

filterGroups.addEventListener("pointerover", (event) => {
  const button = event.target.closest("[data-filter-group]");
  if (button) openFilterGroup(button.dataset.filterGroup);
});

filterGroups.addEventListener("click", (event) => {
  const button = event.target.closest("[data-filter-group]");
  if (button) openFilterGroup(button.dataset.filterGroup);
});

filterOptions.addEventListener("click", (event) => {
  const button = event.target.closest("[data-filter-type]");
  if (!button) return;
  const definition = filterDefinitions[button.dataset.filterType];
  if (!definition) return;
  const key = definition.key;
  const value = button.dataset.filterValue;
  preferences[key] = preferences[key].includes(value) ? preferences[key].filter((item) => item !== value) : [...preferences[key], value];
  savePreferences();
});

withinDays.addEventListener("change", () => { preferences.withinDays = Number(withinDays.value); savePreferences(); });
displayCount.addEventListener("change", () => { preferences.displayCount = Number(displayCount.value); savePreferences(); });
autostartToggle.addEventListener("click", async () => {
  if (autostartBusy) return;
  const nextValue = !autostartEnabled;
  if (!invoke) {
    autostartEnabled = nextValue;
    renderAutostart();
    return;
  }

  autostartBusy = true;
  renderAutostart();
  try {
    await invoke("set_autostart_enabled", { enabled: nextValue });
    autostartEnabled = nextValue;
    autostartToggle.title = "";
  } catch {
    autostartToggle.title = "Unable to update startup setting";
  } finally {
    autostartBusy = false;
    renderAutostart();
  }
});
document.querySelector("#reset-settings").addEventListener("click", () => {
  preferences = {
    ...defaults,
    ranks: [...defaults.ranks],
    coreRanks: [...defaults.coreRanks],
    thcplRanks: [...defaults.thcplRanks],
    categories: [...defaults.categories],
  };
  withinDays.value = String(preferences.withinDays);
  displayCount.value = String(preferences.displayCount);
  savePreferences();
});

document.addEventListener("click", async (event) => {
  const deadline = event.target.closest("[data-url]");
  if (!deadline?.dataset.url) return;
  if (invoke) {
    await invoke("open_url", { url: deadline.dataset.url });
    await invoke("set_window_hovered", { hovered: false });
  } else {
    window.open(deadline.dataset.url, "_blank", "noopener,noreferrer");
  }
});

document.addEventListener("pointerenter", () => invoke?.("set_window_hovered", { hovered: true }));
document.addEventListener("pointerleave", () => invoke?.("set_window_hovered", { hovered: false }));

window.addEventListener("DOMContentLoaded", () => {
  withinDays.value = String(preferences.withinDays);
  displayCount.value = String(preferences.displayCount);
  renderFilterGroups();
  renderFilterOptions();
  loadDeadlines();
  loadAutostart();
  refreshTimer = window.setInterval(loadDeadlines, 10 * 60 * 1000);
  countdownTimer = window.setInterval(render, 1000);
});

window.addEventListener("beforeunload", () => { window.clearInterval(refreshTimer); window.clearInterval(countdownTimer); });
