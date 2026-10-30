"""Run with: python -m unittest discover -s windows-tray/tests -p 'ui_*.py'.

Requires Python Playwright and Microsoft Edge.
The Tauri bridge is mocked; native tray/focus behavior needs a Windows smoke test.
"""

import json
from datetime import datetime, timezone
from pathlib import Path
import unittest

from playwright.sync_api import expect, sync_playwright


APP_URL = (Path(__file__).resolve().parents[1] / "ui" / "index.html").as_uri()
CACHE_KEY = "ccf-tray-deadlines-v1"
BRIDGE = """
window.testFetchMode = MODE;
window.testCalls = [];
window.__TAURI__ = { core: { invoke: async (command, args) => {
  window.testCalls.push({ command, args });
  if (command === 'fetch_deadlines') {
    if (window.testFetchMode === 'offline') throw 'Network timeout: test data source';
    if (window.testFetchMode === 'pending') await new Promise(() => {});
    if (window.testFetchMode === 'empty') return [];
    const now = Date.now();
    const abstractOffset = window.testQuickDeadlines ? 1500 : 3600000;
    const submissionOffset = window.testQuickDeadlines ? 60000 : 7200000;
    const otherOffset = window.testQuickDeadlines ? 30000 : 5400000;
    return [
      { id: 'abstract30', title: 'ABSTRACT', year: 2030, rank: 'A', sub: 'AI', link: '',
        deadline_ms: now + abstractOffset, deadline_type: 'Abstract Deadline', deadlines: [
          { deadline_ms: now + abstractOffset, deadline_type: 'Abstract Deadline' },
          { deadline_ms: now + submissionOffset, deadline_type: 'Submission Deadline' }
        ] },
      { id: 'other30', title: 'OTHER', year: 2030, rank: 'B', sub: 'SE', link: '',
        deadline_ms: now + otherOffset, deadline_type: 'Submission Deadline' },
      { id: 'expired30', title: 'EXPIRED', year: 2030, rank: 'C', sub: 'AI', link: '',
        deadline_ms: now - 1000, deadline_type: 'Submission Deadline' }
    ];
  }
  if (command === 'get_autostart_enabled' || command === 'get_popover_pinned') return false;
  if (command === 'set_popover_pinned') window.setPopoverPinned(args.pinned);
} } };
"""


class TrayUIRegressions(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.playwright = sync_playwright().start()
        cls.browser = cls.playwright.chromium.launch(channel="msedge", headless=True)

    @classmethod
    def tearDownClass(cls):
        cls.browser.close()
        cls.playwright.stop()

    def setUp(self):
        self.context = self.browser.new_context(viewport={"width": 640, "height": 276})
        self.page = self.context.new_page()
        self.errors = []
        self.page.on("pageerror", lambda error: self.errors.append(str(error)))

    def tearDown(self):
        self.context.close()
        self.assertEqual(self.errors, [])

    def open_app(self, mode="success"):
        self.page.add_init_script(BRIDGE.replace("MODE", json.dumps(mode)))
        self.page.goto(APP_URL)
        self.page.wait_for_load_state("networkidle")
        expect(self.page.locator("#refresh-deadlines")).to_be_enabled()

    def row_ids(self):
        return self.page.locator(".deadline-row[data-deadline-id]").evaluate_all(
            "rows => rows.map(row => row.dataset.deadlineId)"
        )

    def test_filter_hover_keeps_nodes_and_stops_mutating(self):
        self.open_app()
        self.page.locator("#open-filters").hover()
        self.page.locator('[data-filter-group="ccf"]').hover()
        self.page.wait_for_function("document.body.dataset.flyoutLevel === '2'")
        self.page.evaluate("""() => {
          window.originalGroup = document.querySelector('[data-filter-group="ccf"]');
          window.groupMutations = 0;
          new MutationObserver(changes => window.groupMutations += changes.length)
            .observe(document.querySelector('#filter-groups'), {
              subtree: true, childList: true, attributes: true, characterData: true
            });
        }""")
        self.page.wait_for_timeout(1500)
        self.assertEqual(self.page.evaluate("window.groupMutations"), 0)
        self.assertTrue(self.page.evaluate("window.originalGroup.isConnected"))
        self.page.locator('[data-filter-type="ccf"][data-filter-value="A"]').click()
        expect(self.page.locator('[data-filter-group="ccf"] .filter-group-summary')).to_have_text("3/4")
        self.assertTrue(self.page.evaluate("window.originalGroup.isConnected"))
        self.assertNotIn("abstract30", self.row_ids())

    def test_expiry_advances_to_submission_and_resorts_without_fetching(self):
        frozen = datetime(2030, 1, 1, tzinfo=timezone.utc)
        self.page.clock.install(time=frozen)
        self.page.clock.pause_at(frozen)
        self.page.add_init_script("window.testQuickDeadlines = true")
        self.open_app()
        self.assertEqual(self.row_ids(), ["abstract30", "other30"])
        self.assertNotIn("0s", self.page.locator("#next-deadline").inner_text())
        self.page.clock.fast_forward(1000)
        expect(self.page.locator("#next-deadline .deadline-remaining")).to_have_text("1s")
        self.page.clock.fast_forward(1000)
        expect(self.page.locator("#next-deadline .deadline-row")).to_have_attribute("data-deadline-id", "other30")
        self.assertEqual(self.row_ids(), ["other30", "abstract30"])
        self.page.locator('[data-deadline-id="abstract30"]').focus()
        expect(self.page.locator("#deadline-details-list")).to_contain_text("Submission deadline")
        expect(self.page.locator("#deadline-details-list")).not_to_contain_text("Abstract deadline")
        self.page.clock.fast_forward(61000)
        expect(self.page.locator("#next-deadline")).to_have_text("No matching conferences")
        self.assertEqual(self.page.evaluate("testCalls.filter(call => call.command === 'fetch_deadlines').length"), 1)

    def test_failed_refresh_keeps_data_and_retry_recovers(self):
        self.open_app()
        rows = self.row_ids()
        cache = self.page.evaluate("key => localStorage.getItem(key)", CACHE_KEY)
        self.page.evaluate("window.testFetchMode = 'offline'")
        self.page.locator("#refresh-deadlines").click()
        expect(self.page.locator("#data-status")).to_contain_text("Offline")
        self.assertEqual(self.row_ids(), rows)
        self.assertIn("Network timeout: test data source", self.page.locator("#data-status").get_attribute("title"))
        self.assertEqual(self.page.evaluate("key => localStorage.getItem(key)", CACHE_KEY), cache)
        self.page.evaluate("window.testFetchMode = 'success'")
        self.page.get_by_role("button", name="Retry").click()
        expect(self.page.locator("#data-status")).not_to_contain_text("Offline")
        expect(self.page.get_by_role("button", name="Refresh", exact=True)).to_be_enabled()

    def test_offline_restart_restores_cache(self):
        self.open_app()
        rows = self.row_ids()
        self.page.add_init_script("window.testFetchMode = 'offline'")
        self.page.reload()
        expect(self.page.locator("#data-status")).to_contain_text("Offline")
        self.assertEqual(self.row_ids(), rows)
        expect(self.page.locator("#next-deadline")).not_to_contain_text("Unable to load")

    def test_cache_renders_while_refresh_is_pending_and_deduplicates_requests(self):
        self.open_app()
        rows = self.row_ids()
        self.page.add_init_script("window.testFetchMode = 'pending'")
        self.page.reload()
        expect(self.page.locator("#data-status")).to_contain_text("Refreshing")
        expect(self.page.locator("#refresh-deadlines")).to_be_disabled()
        self.assertEqual(self.row_ids(), rows)
        self.page.evaluate("window.refreshDeadlines()")
        self.assertEqual(self.page.evaluate("testCalls.filter(call => call.command === 'fetch_deadlines').length"), 1)

    def test_offline_without_cache_can_retry(self):
        self.open_app("offline")
        expect(self.page.locator("#next-deadline")).to_have_text("Unable to load conference data")
        self.page.evaluate("window.testFetchMode = 'success'")
        self.page.get_by_role("button", name="Retry").click()
        expect(self.page.locator(".deadline-row[data-deadline-id]")).to_have_count(2)

    def test_successful_empty_response_replaces_old_cache(self):
        self.open_app()
        self.page.evaluate("window.testFetchMode = 'empty'")
        self.page.locator("#refresh-deadlines").click()
        expect(self.page.locator("#next-deadline")).to_have_text("No matching conferences")
        self.assertEqual(self.page.evaluate("key => JSON.parse(localStorage.getItem(key)).deadlines", CACHE_KEY), [])

    def test_storage_failures_and_corrupt_cache_do_not_break_network_data(self):
        self.page.add_init_script("""localStorage.setItem('ccf-tray-deadlines-v1', '{broken');
          Storage.prototype.setItem = () => { throw new Error('Storage full'); };""")
        self.open_app()
        expect(self.page.locator(".deadline-row[data-deadline-id]")).to_have_count(2)
        expect(self.page.locator("#data-status")).not_to_contain_text("Offline")

    def test_pin_and_escape_use_native_commands(self):
        self.open_app()
        self.page.get_by_role("button", name="Pin", exact=True).click()
        expect(self.page.locator("#pin-panel")).to_have_attribute("aria-pressed", "true")
        self.page.mouse.move(0, 0)
        self.page.keyboard.press("Escape")
        self.assertTrue(self.page.evaluate("testCalls.some(call => call.command === 'dismiss_popover')"))
        self.assertIsNone(self.page.locator(".menu--primary").get_attribute("aria-live"))

    def test_footer_does_not_overlap_settings_at_native_window_size(self):
        self.page.set_viewport_size({"width": 240, "height": 276})
        self.open_app()
        self.page.locator("#open-settings").click()
        reset = self.page.locator("#reset-settings").bounding_box()
        footer = self.page.locator(".panel-status").bounding_box()
        self.assertLessEqual(reset["y"] + reset["height"], footer["y"])
        self.assertLessEqual(footer["y"] + footer["height"], 276)


if __name__ == "__main__":
    unittest.main()
