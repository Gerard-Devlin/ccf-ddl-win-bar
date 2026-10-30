const nextDeadline = document.querySelector("#next-deadline");
const list = document.querySelector("#deadline-list");
const filterOptions = document.querySelector("#filter-options");
const filterOptionsTitle = document.querySelector("#filter-options-title");
const filterGroups = document.querySelector("#filter-groups");
const filterSummary = document.querySelector("#filter-summary");
const deadlineDetails = document.querySelector("#deadline-details");
const deadlineDetailsTitle = document.querySelector("#deadline-details-title");
const deadlineDetailsList = document.querySelector("#deadline-details-list");
const popoverShell = document.querySelector(".popover-shell");
const openFiltersButton = document.querySelector("#open-filters");
const withinDays = document.querySelector("#within-days");
const displayCount = document.querySelector("#display-count");
const colorMode = document.querySelector("#color-mode");
const autostartToggle = document.querySelector("#autostart-toggle");
const dataStatus = document.querySelector("#data-status");
const refreshButton = document.querySelector("#refresh-deadlines");
const pinButton = document.querySelector("#pin-panel");
const invoke = window.__TAURI__?.core?.invoke;
const systemColorScheme = window.matchMedia("(prefers-color-scheme: dark)");
const cacheKey = "ccf-tray-deadlines-v1";

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
  colorMode: "system",
  scrollableListVersion: 1,
};

const filterDefinitions = {
  ccf: { title: "CCF Rank", key: "ranks", values: ccfRanks },
  core: { title: "CORE Rank", key: "coreRanks", values: coreRanks },
  thcpl: { title: "THCPL Rank", key: "thcplRanks", values: thcplRanks },
  category: { title: "Research Area", key: "categories", values: categories.map(([code]) => code) },
};

let deadlines = [];
let hasLoadedData = false;
let lastUpdated = null;
let loadError = "";
let refreshing = false;
let popoverPinned = false;
let nextDeadlineChange = Infinity;
let refreshTimer;
let countdownTimer;
let preferences = loadPreferences();
let autostartEnabled = false;
let autostartBusy = false;
let currentFilterGroup = "ccf";
let flyoutLevel = 0;
let flyoutKind = "none";
let activeDeadlineId = null;
let detailsCloseTimer;

const previewNow = Date.now();
const previewDeadlines = [
  makePreviewDeadline({ id: "icml26", title: "ICML", year: 2026, rank: "A", coreRank: "A*", thcplRank: "A", sub: "AI", link: "https://icml.cc/", offset: 12 * 86400000 + 22 * 3600000, comment: "Main Track" }),
  makePreviewDeadline({ id: "acl26", title: "ACL", year: 2026, rank: "A", coreRank: "A*", thcplRank: "A", sub: "AI", link: "https://www.aclweb.org/", offset: 17 * 86400000 + 22 * 3600000, comment: "Main Conference" }),
  makePreviewDeadline({ id: "issta26", title: "ISSTA", year: 2026, rank: "A", coreRank: "A", thcplRank: "A", sub: "SE", link: "https://conf.researchr.org/series/issta", offset: 18 * 86400000 + 22 * 3600000, comment: "Research Papers" }),
  makePreviewDeadline({ id: "ase26", title: "ASE", year: 2026, rank: "A", coreRank: "A*", thcplRank: "A", sub: "SE", link: "https://conf.researchr.org/series/ase", offset: 74 * 86400000 + 22 * 3600000, comment: "Technical Track" }),
  makePreviewDeadline({ id: "sigmod27", title: "SIGMOD", year: 2027, rank: "A", coreRank: "A*", thcplRank: "A", sub: "DB", link: "https://sigmod.org/", offset: 91 * 86400000 + 4 * 3600000, comment: "Second Submission Cycle" }),
  makePreviewDeadline({ id: "aaai27", title: "AAAI", year: 2027, rank: "A", coreRank: "A*", thcplRank: "A", sub: "AI", link: "https://aaai.org/", offset: 108 * 86400000 + 9 * 3600000, comment: "Main Technical Track" }),
];

function makePreviewDeadline({ id, title, year, rank, coreRank, thcplRank, sub, link, offset, comment }) {
  const abstractMs = previewNow + offset;
  const submissionMs = abstractMs + 7 * 86400000;
  return {
    id,
    title,
    year,
    rank,
    core_rank: coreRank,
    thcpl_rank: thcplRank,
    sub,
    link,
    deadline_type: "Abstract deadline",
    deadline_ms: abstractMs,
    deadline_details: [
      previewDetail(abstractMs, comment, "Abstract deadline"),
      previewDetail(submissionMs, comment, "Submission deadline"),
    ],
  };
}

function previewDetail(deadlineMs, comment, deadlineType) {
  return {
    deadline_type: deadlineType,
    comment,
    deadline: new Date(deadlineMs).toISOString().slice(0, 19).replace("T", " "),
    timezone: "UTC",
    deadline_ms: deadlineMs,
  };
}

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
  applyColorMode();
  renderFilterGroups();
  renderFilterOptions();
  render();
}

function applyColorMode() {
  const supportedModes = ["system", "dark", "light"];
  const selectedMode = supportedModes.includes(preferences.colorMode) ? preferences.colorMode : defaults.colorMode;
  const resolvedMode = selectedMode === "system" ? (systemColorScheme.matches ? "dark" : "light") : selectedMode;
  preferences.colorMode = selectedMode;
  document.documentElement.dataset.theme = resolvedMode;
  colorMode.value = selectedMode;
}

applyColorMode();

function escapeHtml(value) {
  return String(value).replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;").replaceAll("'", "&#039;");
}

function formatLocalTime(deadlineMs) {
  const date = new Date(Number(deadlineMs));
  if (Number.isNaN(date.getTime())) return "Time unavailable";
  const parts = new Intl.DateTimeFormat("en-CA", {
    year: "numeric",
    month: "2-digit",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit",
    hourCycle: "h23",
  }).formatToParts(date).reduce((values, part) => ({ ...values, [part.type]: part.value }), {});
  const offsetMinutes = -date.getTimezoneOffset();
  const sign = offsetMinutes >= 0 ? "+" : "-";
  const absoluteOffset = Math.abs(offsetMinutes);
  const offsetHours = String(Math.floor(absoluteOffset / 60)).padStart(2, "0");
  const offsetRemainder = absoluteOffset % 60;
  const zone = offsetMinutes === 0
    ? "UTC"
    : `UTC${sign}${offsetHours}${offsetRemainder ? `:${String(offsetRemainder).padStart(2, "0")}` : ""}`;
  return `${parts.year}-${parts.month}-${parts.day} ${parts.hour}:${parts.minute}:${parts.second} ${zone}`;
}

function formatOriginalTime(detail) {
  const raw = String(detail.deadline || "").trim().replace("T", " ");
  const zone = String(detail.timezone || "").trim();
  return [raw, zone].filter(Boolean).join(" ");
}

function formatDeadlineType(value) {
  const raw = String(value || "Deadline").trim();
  const normalized = raw.toLowerCase().replaceAll("_", " ");
  const labels = {
    abstract: "Abstract deadline",
    "abstract deadline": "Abstract deadline",
    paper: "Submission deadline",
    submission: "Submission deadline",
    "submission deadline": "Submission deadline",
    "摘要截止": "Abstract deadline",
    "投稿截止": "Submission deadline",
  };
  return labels[normalized] || raw;
}

function conferenceHeading(deadline) {
  const fallbackTitle = String(deadline.id || "Conference").replace(/\d+$/, "");
  return [deadline.title || fallbackTitle, deadline.year].filter(Boolean).join(" ");
}

function detailsFor(deadline) {
  const details = deadline.deadline_details || deadline.deadlines;
  if (Array.isArray(details) && details.length) {
    const sorted = [...details].sort((left, right) => Number(left.deadline_ms) - Number(right.deadline_ms));
    const upcoming = sorted.filter((detail) => Number(detail.deadline_ms) > Date.now());
    return upcoming.length ? upcoming : sorted;
  }
  return [{ deadline_type: deadline.deadline_type, deadline_ms: deadline.deadline_ms }];
}

function renderDeadlineDetails(deadline) {
  deadlineDetailsTitle.textContent = conferenceHeading(deadline);
  const details = detailsFor(deadline);
  if (!details.length) {
    deadlineDetailsList.innerHTML = '<div class="deadline-detail__empty">No deadline details available</div>';
    return;
  }

  deadlineDetailsList.innerHTML = details.map((detail) => {
    const comment = String(detail.comment || "").trim();
    const type = formatDeadlineType(detail.deadline_type);
    const originalTime = formatOriginalTime(detail);
    const localTime = formatLocalTime(detail.deadline_ms);
    const typeIsRepeated = comment && comment.toLowerCase().includes(type.toLowerCase().replace(" deadline", ""));
    const heading = comment || type;
    const kind = comment && !typeIsRepeated ? `<p class="deadline-detail__kind">${escapeHtml(type)}</p>` : "";
    const original = originalTime
      ? `<div class="deadline-detail__time"><span>Source</span><time>${escapeHtml(originalTime)}</time></div>`
      : "";
    return `<article class="deadline-detail"><h3 class="deadline-detail__name">${escapeHtml(heading)}</h3>${kind}<div class="deadline-detail__times">${original}<div class="deadline-detail__time"><span>Local</span><time>${escapeHtml(localTime)}</time></div></div></article>`;
  }).join("");
  deadlineDetailsList.scrollTop = 0;
  scheduleFlyoutPosition();
}

function formatRemaining(deadlineMs) {
  const totalSeconds = Math.max(0, Math.ceil((deadlineMs - Date.now()) / 1000));
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
  const active = deadline.id === activeDeadlineId ? " deadline-row--active" : "";
  return `<button class="deadline-row${active}" type="button" data-deadline-id="${escapeHtml(deadline.id)}" data-deadline-ms="${Number(deadline.deadline_ms)}" data-url="${escapeHtml(deadline.link)}" aria-label="Open ${escapeHtml(displayId)} website"><span class="deadline-name">${escapeHtml(displayId)}</span><span class="deadline-remaining">${formatRemaining(deadline.deadline_ms)}</span></button>`;
}

function updateCountdowns() {
  if (Date.now() >= nextDeadlineChange) {
    render();
    return;
  }
  document.querySelectorAll(".deadline-row[data-deadline-ms]").forEach((deadlineRow) => {
    const remaining = deadlineRow.querySelector(".deadline-remaining");
    if (remaining) remaining.textContent = formatRemaining(Number(deadlineRow.dataset.deadlineMs));
  });
}

function visibleDeadlines() {
  const now = Date.now();
  const latest = now + preferences.withinDays * 86400000;
  const seen = new Set();
  nextDeadlineChange = Infinity;
  const upcoming = deadlines.flatMap((deadline) => {
    const next = detailsFor(deadline).find((detail) => Number(detail.deadline_ms) > now);
    if (!next) return [];
    const deadlineMs = Number(next.deadline_ms);
    nextDeadlineChange = Math.min(nextDeadlineChange, deadlineMs);
    // A cached abstract deadline can expire while its submission is still ahead.
    return [{ ...deadline, deadline_ms: deadlineMs, deadline_type: next.deadline_type }];
  }).sort((left, right) => left.deadline_ms - right.deadline_ms);
  return upcoming.filter((deadline) => {
    const conferenceId = deadline.id.toLowerCase();
    if (seen.has(conferenceId)
      || !preferences.ranks.includes(deadline.rank)
      || !preferences.coreRanks.includes(deadline.core_rank || "N")
      || !preferences.thcplRanks.includes(deadline.thcpl_rank || "N")
      || !preferences.categories.includes(deadline.sub)
      || deadline.deadline_ms <= now
      || deadline.deadline_ms > latest) return false;
    seen.add(conferenceId);
    return true;
  });
}

function render() {
  const visible = visibleDeadlines().slice(0, preferences.displayCount);
  if (!visible.length) {
    nextDeadline.innerHTML = `<div class="message">${!hasLoadedData && loadError ? "Unable to load conference data" : "No matching conferences"}</div>`;
    list.innerHTML = "";
  } else {
    nextDeadline.innerHTML = row(visible[0]);
    list.innerHTML = visible.slice(1).map(row).join("");
  }

  if (activeDeadlineId) {
    const activeDeadline = visible.find((deadline) => deadline.id === activeDeadlineId);
    if (activeDeadline) renderDeadlineDetails(activeDeadline);
    else closeDeadlineDetails();
  }

  const allSelected = preferences.ranks.length === ccfRanks.length
    && preferences.coreRanks.length === coreRanks.length
    && preferences.thcplRanks.length === thcplRanks.length
    && preferences.categories.length === categories.length;
  filterSummary.textContent = allSelected ? "All" : "Filtered";
}

function renderFilterGroups() {
  if (!filterGroups.childElementCount) {
    filterGroups.innerHTML = Object.entries(filterDefinitions).map(([type, definition]) =>
      `<button class="filter-group-row" type="button" data-filter-group="${type}"><span>${definition.title}</span><span class="filter-group-summary"></span><span class="chevron">›</span></button>`
    ).join("");
  }
  Object.entries(filterDefinitions).forEach(([type, definition]) => {
    const selected = preferences[definition.key].length;
    const summary = selected === definition.values.length ? "All" : selected === 0 ? "None" : `${selected}/${definition.values.length}`;
    const button = filterGroups.querySelector(`[data-filter-group="${type}"]`);
    const summaryElement = button.querySelector(".filter-group-summary");
    if (summaryElement.textContent !== summary) summaryElement.textContent = summary;
    const active = flyoutKind === "filters" && flyoutLevel === 2 && type === currentFilterGroup;
    if (button.classList.contains("filter-group-row--active") !== active) {
      button.classList.toggle("filter-group-row--active", active);
    }
  });
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

function positionFlyout(panel, anchor) {
  if (!panel || !anchor || getComputedStyle(panel).display === "none") return;

  const shellRect = popoverShell.getBoundingClientRect();
  const anchorRect = anchor.getBoundingClientRect();
  const headerHeight = panel.querySelector("header")?.offsetHeight || anchorRect.height;
  const preferredTop = anchorRect.top + anchorRect.height / 2 - shellRect.top - headerHeight / 2;
  const maxTop = Math.max(0, shellRect.height - panel.offsetHeight);
  const top = Math.max(0, Math.min(preferredTop, maxTop));
  panel.style.transform = `translateY(${Math.round(top)}px)`;
}

function positionVisibleFlyouts() {
  if (flyoutKind === "details") {
    const anchor = [...document.querySelectorAll(".deadline-row[data-deadline-id]")]
      .find((rowElement) => rowElement.dataset.deadlineId === activeDeadlineId);
    positionFlyout(deadlineDetails, anchor);
    return;
  }

  if (flyoutKind === "filters") {
    positionFlyout(document.querySelector(".menu--groups"), openFiltersButton);
    if (flyoutLevel === 2) {
      positionFlyout(
        document.querySelector(".menu--options"),
        filterGroups.querySelector(`[data-filter-group="${currentFilterGroup}"]`),
      );
    }
  }
}

function scheduleFlyoutPosition() {
  window.requestAnimationFrame(positionVisibleFlyouts);
}

function applyFlyoutState(kind, level) {
  flyoutKind = kind;
  flyoutLevel = level;
  document.body.dataset.flyoutKind = kind;
  document.body.dataset.flyoutLevel = String(level);
  renderFilterGroups();
  scheduleFlyoutPosition();
}

async function setFlyoutState(kind, level) {
  if (kind === flyoutKind && level === flyoutLevel) return;
  const needsResize = level !== flyoutLevel;
  applyFlyoutState(kind, level);
  if (invoke && needsResize) {
    try { await invoke("set_flyout_level", { level }); } catch {}
  }
}

function clearDetailsCloseTimer() {
  window.clearTimeout(detailsCloseTimer);
  detailsCloseTimer = undefined;
}

function syncActiveDeadlineRows() {
  document.querySelectorAll(".deadline-row[data-deadline-id]").forEach((deadlineRow) => {
    deadlineRow.classList.toggle("deadline-row--active", deadlineRow.dataset.deadlineId === activeDeadlineId);
  });
}

function deactivateDeadlineDetails() {
  clearDetailsCloseTimer();
  activeDeadlineId = null;
  syncActiveDeadlineRows();
}

function openDeadlineDetails(deadline) {
  clearDetailsCloseTimer();
  activeDeadlineId = deadline.id;
  renderDeadlineDetails(deadline);
  syncActiveDeadlineRows();
  setFlyoutState("details", 1);
}

function closeDeadlineDetails() {
  deactivateDeadlineDetails();
  if (flyoutKind === "details") setFlyoutState("none", 0);
}

function scheduleDeadlineDetailsClose() {
  clearDetailsCloseTimer();
  detailsCloseTimer = window.setTimeout(closeDeadlineDetails, 120);
}

function openFilterFlyout(level) {
  deactivateDeadlineDetails();
  setFlyoutState("filters", level);
}

function openFilterGroup(group) {
  if (!filterDefinitions[group]
    || (group === currentFilterGroup && flyoutKind === "filters" && flyoutLevel === 2)) return;
  currentFilterGroup = group;
  renderFilterOptions();
  openFilterFlyout(2);
}

window.resetFilterFlyouts = () => {
  deactivateDeadlineDetails();
  applyFlyoutState("none", 0);
};

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

function renderDataStatus() {
  const updated = lastUpdated
    ? `Updated ${new Date(lastUpdated).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false })}`
    : "No saved data";
  dataStatus.textContent = !invoke ? "Preview data"
    : refreshing ? `Refreshing · ${updated}`
    : loadError ? `Offline · ${updated}` : updated;
  dataStatus.title = [loadError, lastUpdated ? `Last successful update: ${new Date(lastUpdated).toLocaleString()}` : ""].filter(Boolean).join("\n");
  dataStatus.classList.toggle("data-status--offline", Boolean(loadError));
  refreshButton.textContent = loadError ? "Retry" : "Refresh";
  refreshButton.disabled = refreshing;
}

function isDeadlineData(value) {
  return Array.isArray(value) && value.every((item) => item && typeof item.id === "string"
    && typeof item.rank === "string" && typeof item.sub === "string"
    && Number.isFinite(item.deadline_ms)
    && [item.deadlines, item.deadline_details].every((details) => details === undefined
      || (Array.isArray(details) && details.every((detail) => detail && Number.isFinite(detail.deadline_ms)))));
}

function loadCachedDeadlines() {
  try {
    const cached = JSON.parse(localStorage.getItem(cacheKey));
    if (!cached || !Number.isFinite(cached.updatedAt) || !isDeadlineData(cached.deadlines)) return;
    deadlines = cached.deadlines;
    lastUpdated = cached.updatedAt;
    hasLoadedData = true;
    render();
  } catch {
    // Storage can be unavailable or contain data from an interrupted write.
  }
}

function saveCachedDeadlines() {
  try {
    localStorage.setItem(cacheKey, JSON.stringify({ updatedAt: lastUpdated, deadlines }));
  } catch {
    // A storage failure must not discard a successful network response.
  }
}

async function loadDeadlines() {
  if (refreshing) return;
  if (!invoke) {
    deadlines = previewDeadlines;
    hasLoadedData = true;
    render();
    renderDataStatus();
    return;
  }
  refreshing = true;
  renderDataStatus();
  try {
    const fetched = await invoke("fetch_deadlines");
    if (!isDeadlineData(fetched)) throw new Error("Invalid conference data received");
    deadlines = fetched;
    hasLoadedData = true;
    lastUpdated = Date.now();
    loadError = "";
    saveCachedDeadlines();
  } catch (error) {
    loadError = error instanceof Error ? error.message : String(error);
  } finally {
    refreshing = false;
    render();
    renderDataStatus();
  }
}

window.refreshDeadlines = loadDeadlines;
window.setPopoverPinned = (pinned) => {
  popoverPinned = pinned;
  pinButton.setAttribute("aria-pressed", String(pinned));
  pinButton.textContent = pinned ? "Unpin" : "Pin";
  pinButton.title = pinned ? "Pinned · Esc or focus loss closes the panel" : "Keep the panel open while using it";
};
pinButton.addEventListener("click", async () => {
  if (invoke) await invoke("set_popover_pinned", { pinned: !popoverPinned });
  else window.setPopoverPinned(!popoverPinned);
});
refreshButton.addEventListener("click", loadDeadlines);
document.addEventListener("keydown", (event) => {
  if (event.key !== "Escape" || event.defaultPrevented) return;
  event.preventDefault();
  if (invoke) invoke("dismiss_popover");
  else {
    window.setPopoverPinned(false);
    window.resetFilterFlyouts();
  }
});

openFiltersButton.addEventListener("pointerenter", () => openFilterFlyout(1));
openFiltersButton.addEventListener("focus", () => openFilterFlyout(1));
openFiltersButton.addEventListener("click", () => openFilterFlyout(1));
document.querySelector("#open-settings").addEventListener("pointerenter", () => {
  deactivateDeadlineDetails();
  setFlyoutState("none", 0);
});
document.querySelector("#open-settings").addEventListener("click", () => {
  deactivateDeadlineDetails();
  setFlyoutState("none", 0);
  showView("settings-view");
});
document.querySelectorAll("[data-back]").forEach((button) => button.addEventListener("click", () => {
  deactivateDeadlineDetails();
  setFlyoutState("none", 0);
  showView("main-view");
}));

document.addEventListener("pointerover", (event) => {
  const deadlineRow = event.target.closest(".deadline-row[data-deadline-id]");
  if (!deadlineRow || deadlineRow.contains(event.relatedTarget)) return;
  const deadline = deadlines.find((item) => item.id === deadlineRow.dataset.deadlineId);
  if (deadline) openDeadlineDetails(deadline);
});

document.addEventListener("pointerout", (event) => {
  const deadlineRow = event.target.closest(".deadline-row[data-deadline-id]");
  if (!deadlineRow || deadlineRow.contains(event.relatedTarget)) return;
  scheduleDeadlineDetailsClose();
});

document.addEventListener("focusin", (event) => {
  const deadlineRow = event.target.closest(".deadline-row[data-deadline-id]");
  if (!deadlineRow) return;
  const deadline = deadlines.find((item) => item.id === deadlineRow.dataset.deadlineId);
  if (deadline) openDeadlineDetails(deadline);
});

document.addEventListener("focusout", (event) => {
  if (event.target.closest(".deadline-row[data-deadline-id]")) scheduleDeadlineDetailsClose();
});

deadlineDetails.addEventListener("pointerenter", clearDetailsCloseTimer);
deadlineDetails.addEventListener("pointerleave", scheduleDeadlineDetailsClose);

filterGroups.addEventListener("pointerenter", () => openFilterFlyout(Math.max(1, flyoutLevel)));
filterOptions.addEventListener("pointerenter", () => openFilterFlyout(2));

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
colorMode.addEventListener("change", () => { preferences.colorMode = colorMode.value; savePreferences(); });
systemColorScheme.addEventListener("change", () => {
  if (preferences.colorMode === "system") applyColorMode();
});
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

document.addEventListener("contextmenu", (event) => event.preventDefault());

document.addEventListener("pointerenter", () => invoke?.("set_window_hovered", { hovered: true }));
document.addEventListener("pointerleave", () => invoke?.("set_window_hovered", { hovered: false }));

window.addEventListener("DOMContentLoaded", () => {
  withinDays.value = String(preferences.withinDays);
  displayCount.value = String(preferences.displayCount);
  colorMode.value = preferences.colorMode;
  renderFilterGroups();
  renderFilterOptions();
  if (invoke) {
    loadCachedDeadlines();
    invoke("get_popover_pinned").then(window.setPopoverPinned).catch(() => {});
  }
  loadDeadlines();
  loadAutostart();
  refreshTimer = window.setInterval(loadDeadlines, 10 * 60 * 1000);
  countdownTimer = window.setInterval(updateCountdowns, 1000);
});

window.addEventListener("beforeunload", () => { window.clearInterval(refreshTimer); window.clearInterval(countdownTimer); });
window.addEventListener("resize", scheduleFlyoutPosition);
