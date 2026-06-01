"""Integration tests for ``MainWindow`` <-> ``Session`` wiring (W2).

These tests need a headless QApplication. They mark the suite as
PySide6-dependent at module load.
"""

from __future__ import annotations

import json
from pathlib import Path

import pytest

pytest.importorskip("PySide6")

from PySide6 import QtWidgets  # noqa: E402

from officemapmaker.session import STEP_IDS, Session  # noqa: E402
from officemapmaker.wizard.main_window import MainWindow, StepStatus  # noqa: E402


@pytest.fixture(scope="module")
def qapp():
    """Headless QApplication shared across tests in this file."""
    import os

    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication([])
    yield app


@pytest.fixture
def inputs(tmp_path: Path):
    map_path = tmp_path / "demo.png"
    assn_path = tmp_path / "demo.xlsx"
    map_path.write_bytes(b"\x89PNG\r\n\x1a\n" + b"a" * 64)
    assn_path.write_bytes(b"PK\x03\x04" + b"b" * 64)
    return map_path, assn_path, tmp_path


# ---------------------------------------------------------------------------
# Basic round-trip via the convenience constructor
# ---------------------------------------------------------------------------


def test_convenience_constructor_creates_session_file_on_disk(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        expected = out / "demo.session.json"
        assert expected.is_file()
        # File must be valid JSON with all six steps populated.
        data = json.loads(expected.read_text(encoding="utf-8"))
        assert set(data["step_state"].keys()) == set(STEP_IDS)
    finally:
        w.close()


def test_set_step_status_writes_to_session_file(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        w.set_step_status("calibrate", StepStatus.OK)
        w.set_step_status(
            "validate_labels",
            StepStatus.WARNING,
            issues=["Office 1480 unassigned", "Office 1067 unassigned"],
        )
        data = json.loads((out / "demo.session.json").read_text(encoding="utf-8"))
        assert data["step_state"]["calibrate"]["status"] == "ok"
        assert data["step_state"]["validate_labels"]["status"] == "warning"
        assert len(data["step_state"]["validate_labels"]["issues"]) == 2
        assert (
            data["step_state"]["validate_labels"]["issues"][0]["message"]
            == "Office 1480 unassigned"
        )
    finally:
        w.close()


def test_reload_with_existing_session_restores_status_and_current_step(qapp, inputs):
    map_path, assn_path, out = inputs

    # First window: do some work + jump step.
    w1 = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    w1.set_step_status("calibrate", StepStatus.OK)
    w1.set_step_status(
        "validate_labels", StepStatus.WARNING, issues=["only warning"]
    )
    w1._activate_step(2)  # validate_fill
    w1.close()

    # Second window: same paths -> session loads via load_or_create
    # (mode="restored") -> MainWindow sees the saved state.
    load = Session.load_or_create(
        map_path=map_path, assignments_path=assn_path, output_dir=out
    )
    assert load.mode == "restored"
    w2 = MainWindow(session=load.session)
    try:
        assert w2.current_step_id == "validate_fill"
        assert w2._steps[0].status == StepStatus.OK
        assert w2._steps[1].status == StepStatus.WARNING
        assert w2._steps[1].issues == ["only warning"]
        # The content stack must actually point at the restored step's
        # widget, not at index 0 (the QStackedWidget default). Regression
        # test for the bug where reopening a session at step N showed the
        # step-N badge highlighted in the sidebar but rendered step 0's
        # content in the main pane -- a user clicking "Run calibration"
        # would silently wipe N steps of work.
        assert w2._content_stack.currentIndex() == 2
        assert w2._content_stack.currentWidget() is w2._steps[2].widget
    finally:
        w2.close()


def test_advisory_status_maps_to_info_in_session(qapp, inputs):
    """The wizard's ADVISORY badge round-trips through the session as ``info``."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        w.set_step_status("validate_fill", StepStatus.ADVISORY, issues=["leak"])
        data = json.loads((out / "demo.session.json").read_text(encoding="utf-8"))
        assert data["step_state"]["validate_fill"]["status"] == "info"
        # And reloading gets back to ADVISORY (not stuck on a raw "info" string).
        w.close()
        load = Session.load_or_create(
            map_path=map_path, assignments_path=assn_path, output_dir=out
        )
        w2 = MainWindow(session=load.session)
        try:
            assert w2._steps[2].status == StepStatus.ADVISORY
        finally:
            w2.close()
    finally:
        pass


# ---------------------------------------------------------------------------
# Constructor validation
# ---------------------------------------------------------------------------


def test_constructor_requires_either_session_or_paths(qapp):
    with pytest.raises(TypeError):
        MainWindow()  # no session, no paths


# ---------------------------------------------------------------------------
# Pipeline runner integration (W3)
# ---------------------------------------------------------------------------


import time  # noqa: E402  (helpers below)


def _drain_until(predicate, *, timeout_s: float = 5.0, tick_ms: int = 10) -> bool:
    """Spin Qt's event loop until ``predicate()`` is true or timeout."""
    from PySide6 import QtCore as _Qt

    deadline = time.monotonic() + timeout_s
    app = QtWidgets.QApplication.instance()
    assert app is not None
    while time.monotonic() < deadline:
        app.processEvents(_Qt.QEventLoop.ProcessEventsFlag.AllEvents, tick_ms)
        if predicate():
            return True
    return False


def test_run_pipeline_step_marks_running_then_ok(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        captured = {}

        def func(*, progress_cb, cancel_cb):
            progress_cb(0.5, "halfway")
            return "the-result", []

        def on_finished(result, issues):
            captured["result"] = result
            captured["issues"] = issues

        runner = w.run_pipeline_step("calibrate", func, on_finished=on_finished)
        assert runner is not None
        # Footer should be in "running" mode immediately.
        # (isVisible() is false on hidden parents; isHidden() reports
        # the widget's explicit visibility state independently.)
        assert not w._progress_bar.isHidden()
        assert not w._cancel_button.isHidden()
        assert w._steps[0].status == StepStatus.RUNNING

        assert _drain_until(lambda: "result" in captured)
        runner.wait(2000)
        _drain_until(lambda: not runner.is_running())

        assert captured["result"] == "the-result"
        assert w._steps[0].status == StepStatus.OK
        assert w._progress_bar.isHidden()
        assert w._cancel_button.isHidden()
    finally:
        w.close()


def test_run_pipeline_step_failed_sets_error_with_issue(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        captured = {}

        def func(*, progress_cb, cancel_cb):
            raise RuntimeError("boom")

        def on_failed(exc):
            captured["exc"] = exc

        runner = w.run_pipeline_step("calibrate", func, on_failed=on_failed)
        assert runner is not None

        assert _drain_until(lambda: "exc" in captured)
        runner.wait(2000)
        _drain_until(lambda: not runner.is_running())

        assert isinstance(captured["exc"], RuntimeError)
        assert w._steps[0].status == StepStatus.ERROR
        assert any("boom" in iss for iss in w._steps[0].issues)
    finally:
        w.close()


def test_run_pipeline_step_canceled_reverts_to_prior_status(qapp, inputs):
    """Cancel mid-run -> status reverts (not stuck on RUNNING, not ERROR)."""
    import threading

    from officemapmaker.pipeline import PipelineCanceled

    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # Give the step a prior status so we can assert the revert.
        w.set_step_status("calibrate", StepStatus.WARNING, issues=["prior"])

        started = threading.Event()
        released = threading.Event()

        def func(*, progress_cb, cancel_cb):
            started.set()
            released.wait(timeout=5)
            if cancel_cb():
                raise PipelineCanceled()
            return None, []

        canceled_seen = {}

        def on_canceled():
            canceled_seen["yes"] = True

        runner = w.run_pipeline_step(
            "calibrate", func, on_canceled=on_canceled
        )
        assert runner is not None
        assert started.wait(timeout=2)
        # Click Cancel.
        w._on_cancel_pipeline()
        released.set()

        assert _drain_until(lambda: canceled_seen.get("yes"))
        runner.wait(2000)
        _drain_until(lambda: not runner.is_running())

        assert w._steps[0].status == StepStatus.WARNING
        assert w._steps[0].issues == ["prior"]
        assert w._progress_bar.isHidden()
        assert w._cancel_button.isHidden()
    finally:
        w.close()


def test_run_pipeline_step_refuses_when_another_is_active(qapp, inputs):
    import threading

    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        release = threading.Event()

        def slow(*, progress_cb, cancel_cb):
            release.wait(timeout=5)
            return None, []

        r1 = w.run_pipeline_step("calibrate", slow)
        assert r1 is not None
        r2 = w.run_pipeline_step(
            "calibrate", lambda *, progress_cb, cancel_cb: (None, [])
        )
        assert r2 is None  # refused while r1 is active

        release.set()
        assert _drain_until(lambda: not r1.is_running())
        r1.wait(2000)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Window geometry persistence
# ---------------------------------------------------------------------------


def test_window_geometry_round_trips_across_relaunch(qapp, inputs, monkeypatch):
    """Saving on close + restoring on next launch reuses size + position."""
    from PySide6 import QtCore

    from officemapmaker.wizard import main_window as mw

    # Isolated settings file so this test doesn't depend on (or
    # pollute) whatever's in the session-wide tmp QSettings dir.
    tmp_ini = inputs[2] / "geom.ini"

    def _local_settings() -> QtCore.QSettings:
        return QtCore.QSettings(str(tmp_ini), QtCore.QSettings.Format.IniFormat)

    monkeypatch.setattr(mw, "_make_settings", _local_settings)

    map_path, assn_path, out = inputs

    # Pick a target size that fits inside the offscreen test
    # platform's reported screen (typically 800x800) -- otherwise
    # Qt's restoreGeometry will clamp the saved width/height down to
    # the available screen, and the round-trip won't be observable.
    saved_w, saved_h = 600, 500

    # First launch: resize to a distinctive size, then close (which
    # triggers _save_window_geometry).
    w1 = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    w1.resize(saved_w, saved_h)
    w1.move(20, 20)
    # Process the move/resize so frameGeometry reflects them before
    # we ask Qt to serialize the geometry.
    QtWidgets.QApplication.processEvents()
    w1.close()

    # The blob should have landed in QSettings.
    settings = _local_settings()
    blob = settings.value(mw._SETTINGS_GEOMETRY_KEY)
    assert blob is not None and bytes(blob), (
        "closeEvent should have persisted a non-empty geometry blob"
    )

    # Second launch: should restore (or at least come up near) the
    # size we set, not the default 1366x800.
    w2 = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # restoreGeometry is approximate (window frame, DPI, etc. all
        # play in), so we test the size came back close, not exact.
        assert abs(w2.width() - saved_w) < 50, (
            f"width should restore near {saved_w}, got {w2.width()}"
        )
        assert abs(w2.height() - saved_h) < 50, (
            f"height should restore near {saved_h}, got {w2.height()}"
        )
    finally:
        w2.close()


def test_window_geometry_falls_back_when_saved_off_screen(qapp, inputs, monkeypatch):
    """An off-screen restored geometry should reset to the default size."""
    from PySide6 import QtCore

    from officemapmaker.wizard import main_window as mw

    tmp_ini = inputs[2] / "offscreen.ini"

    def _local_settings() -> QtCore.QSettings:
        return QtCore.QSettings(str(tmp_ini), QtCore.QSettings.Format.IniFormat)

    monkeypatch.setattr(mw, "_make_settings", _local_settings)

    map_path, assn_path, out = inputs

    # Prime the settings with a distinctive size we can later check
    # was *not* used.
    saved_w, saved_h = 600, 500
    w1 = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    w1.resize(saved_w, saved_h)
    QtWidgets.QApplication.processEvents()
    w1.close()

    # Force the on-screen check to always say "no" so we exercise the
    # fallback path deterministically (the real "monitor unplugged"
    # case is hard to simulate in headless tests). Patch AFTER the
    # save so the close() above still persists normally.
    monkeypatch.setattr(mw, "_geometry_is_on_screen", lambda _rect: False)

    w2 = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # The saved 600x500 must NOT have been applied. The fallback
        # resize asks for _DEFAULT_WINDOW_SIZE (1366x800); on a
        # smaller test "screen" Qt may clamp it, but the result will
        # still differ from the saved 600x500 by more than the
        # tolerance we'd allow for a successful round-trip.
        close_to_saved = (
            abs(w2.width() - saved_w) < 50
            and abs(w2.height() - saved_h) < 50
        )
        assert not close_to_saved, (
            f"fallback path should not produce the saved size "
            f"{saved_w}x{saved_h}; got {w2.width()}x{w2.height()}"
        )
    finally:
        w2.close()


def test_window_geometry_uses_default_when_no_prior_settings(qapp, inputs, monkeypatch):
    """First-ever launch (no saved geometry) uses the default size."""
    from PySide6 import QtCore

    from officemapmaker.wizard import main_window as mw

    tmp_ini = inputs[2] / "empty.ini"
    # Don't pre-write to the INI -- it should be missing entirely.
    assert not tmp_ini.exists()

    def _local_settings() -> QtCore.QSettings:
        return QtCore.QSettings(str(tmp_ini), QtCore.QSettings.Format.IniFormat)

    monkeypatch.setattr(mw, "_make_settings", _local_settings)

    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # No saved geometry => the constructor asked for
        # _DEFAULT_WINDOW_SIZE (1366x800). On a small offscreen
        # "screen" Qt may clamp it, but the window must at least be
        # large enough to be usable.
        assert w.width() >= 500
        assert w.height() >= 400
    finally:
        w.close()


def test_geometry_is_on_screen_accepts_window_on_primary(qapp):
    """The helper should return True for a rect inside the primary screen."""
    from PySide6 import QtCore

    from officemapmaker.wizard.main_window import _geometry_is_on_screen

    app = QtWidgets.QApplication.instance()
    primary = app.primaryScreen().availableGeometry()
    inside = QtCore.QRect(primary.x() + 10, primary.y() + 10, 400, 300)
    assert _geometry_is_on_screen(inside)


def test_geometry_is_on_screen_rejects_off_screen_rect(qapp):
    """The helper should return False for a rect far from any screen."""
    from PySide6 import QtCore

    from officemapmaker.wizard.main_window import _geometry_is_on_screen

    far_away = QtCore.QRect(50_000, 50_000, 800, 600)
    assert not _geometry_is_on_screen(far_away)


# ---------------------------------------------------------------------------
# Issues-panel chip-row + summary title (kind-grouping UX)
# ---------------------------------------------------------------------------


def _set_calibrate_issues(
    w: MainWindow,
    issues,
    codes=None,
    severities=None,
    status: StepStatus = StepStatus.ERROR,
):
    """Helper: drop a synthetic issue set onto the calibrate step.

    Defaults severity to error so any code-shape-only test still
    yields a deterministic status.
    """
    w.set_step_status(
        "calibrate",
        status,
        issues=issues,
        issue_codes=codes,
        issue_severities=severities,
    )


def test_set_step_status_stores_parallel_codes_and_severities(qapp, inputs):
    """Codes + severities passed to set_step_status land on _StepEntry."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _set_calibrate_issues(
            w,
            ["[error] orphan_room: a", "[warning] orphan_label: b"],
            codes=["orphan_room", "orphan_label"],
            severities=["error", "warning"],
        )
        entry = w._steps[0]
        assert entry.issue_codes == ["orphan_room", "orphan_label"]
        assert entry.issue_severities == ["error", "warning"]
    finally:
        w.close()


def test_set_step_status_defaults_severity_from_status(qapp, inputs):
    """Omitting issue_severities derives severity from the step status."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        w.set_step_status(
            "calibrate", StepStatus.WARNING, issues=["a", "b"]
        )
        entry = w._steps[0]
        # Derived from StepStatus.WARNING.
        assert entry.issue_severities == ["warning", "warning"]
    finally:
        w.close()


def test_session_round_trip_preserves_codes(qapp, inputs):
    """Real issue codes (not "placeholder") survive a save/load cycle."""
    map_path, assn_path, out = inputs
    w1 = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    _set_calibrate_issues(
        w1,
        ["[error] orphan_room: a", "[warning] orphan_label: b"],
        codes=["orphan_room", "orphan_label"],
        severities=["error", "warning"],
    )
    w1.close()

    data = json.loads((out / "demo.session.json").read_text(encoding="utf-8"))
    issue_codes = [iss["code"] for iss in data["step_state"]["calibrate"]["issues"]]
    assert issue_codes == ["orphan_room", "orphan_label"]

    load = Session.load_or_create(
        map_path=map_path, assignments_path=assn_path, output_dir=out
    )
    w2 = MainWindow(session=load.session)
    try:
        assert w2._steps[0].issue_codes == ["orphan_room", "orphan_label"]
        assert w2._steps[0].issue_severities == ["error", "warning"]
    finally:
        w2.close()


def test_chip_row_built_from_codes(qapp, inputs):
    """Three issues across two codes -> All, a, b chips with counts."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _set_calibrate_issues(
            w,
            ["x", "y", "z"],
            codes=["a", "a", "b"],
            severities=["error", "error", "warning"],
        )
        # All + a + b -> 3 buttons total.
        assert set(w._chip_buttons.keys()) == {None, "a", "b"}
        assert "All (3)" in w._chip_buttons[None].text()
        assert "a (2)" in w._chip_buttons["a"].text()
        assert "b (1)" in w._chip_buttons["b"].text()
    finally:
        w.close()


def test_chip_row_hidden_when_no_real_codes(qapp, inputs):
    """Issues without any non-empty code -> chip scroll is hidden."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        w.set_step_status(
            "calibrate",
            StepStatus.WARNING,
            issues=["legacy row 1", "legacy row 2"],
            # No codes / severities -> codes pad to "".
        )
        assert not w._chip_scroll.isVisibleTo(w._issues_group)
        # And no chips were built.
        assert w._chip_buttons == {}
    finally:
        w.close()


def test_chip_click_filters_list_and_strips_prefix(qapp, inputs):
    """Clicking a chip filters the list to that code + strips the prefix."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _set_calibrate_issues(
            w,
            [
                "[error] orphan_room: room 1 has no label",
                "[warning] orphan_label: label 9 has no room",
                "[error] orphan_room: room 2 has no label",
            ],
            codes=["orphan_room", "orphan_label", "orphan_room"],
            severities=["error", "warning", "error"],
        )
        # Unfiltered: 3 rows.
        assert w._issues_list.count() == 3

        # Filter on "orphan_room": 2 rows, prefix stripped.
        w._on_chip_clicked("orphan_room")
        assert w._issues_list.count() == 2
        texts = [w._issues_list.item(i).text() for i in range(2)]
        assert texts == [
            "room 1 has no label",
            "room 2 has no label",
        ]
    finally:
        w.close()


def test_reclicking_active_chip_unfilters(qapp, inputs):
    """Clicking the active chip again clears the filter (back to All)."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _set_calibrate_issues(
            w,
            ["a-row", "b-row"],
            codes=["a", "b"],
            severities=["error", "warning"],
        )
        w._on_chip_clicked("a")
        assert w._active_issue_code == "a"
        w._on_chip_clicked("a")
        assert w._active_issue_code is None
        # And the list is back to showing every row.
        assert w._issues_list.count() == 2
    finally:
        w.close()


def test_step_change_resets_active_chip(qapp, inputs):
    """Navigating to a different step clears the chip filter."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # Step 0 has issues + active chip.
        _set_calibrate_issues(
            w,
            ["a-row"],
            codes=["a"],
            severities=["error"],
        )
        w._on_chip_clicked("a")
        assert w._active_issue_code == "a"

        # Activate step 1 (validate_labels). That refreshes the
        # issues panel for the new step, which resets the filter.
        w._activate_step(1)
        assert w._active_issue_code is None
    finally:
        w.close()


def test_title_formats_singular_and_plural(qapp, inputs):
    """Issues title shows total + per-severity breakdown with plurals."""
    from officemapmaker.wizard.main_window import _format_issues_title

    # No issues -> bare title.
    assert _format_issues_title(0, []) == "Issues"

    # 1 error (singular).
    assert _format_issues_title(1, ["error"]) == "Issues (1) - 1 error"

    # 2 errors + 1 warning (plural / singular mix).
    assert (
        _format_issues_title(3, ["error", "error", "warning"])
        == "Issues (3) - 2 errors, 1 warning"
    )

    # Advisory only -> uses "advisory" (no plural form).
    assert (
        _format_issues_title(2, ["advisory", "advisory"])
        == "Issues (2) - 2 advisory"
    )

    # Zero counts are omitted.
    assert _format_issues_title(1, ["warning"]) == "Issues (1) - 1 warning"


def test_strip_severity_code_prefix_handles_known_severities(qapp):
    """The prefix regex strips bracketed-severity + code: from a message."""
    from officemapmaker.wizard.main_window import _strip_severity_code_prefix

    assert (
        _strip_severity_code_prefix("[error] orphan_room: foo bar")
        == "foo bar"
    )
    assert (
        _strip_severity_code_prefix("[warning] dup_row: y")
        == "y"
    )
    # No prefix -> no-op.
    assert _strip_severity_code_prefix("just a message") == "just a message"


def test_chip_visibility_follows_group_collapse(qapp, inputs):
    """Collapsing the issues group also hides the chip row."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _set_calibrate_issues(
            w,
            ["a-row"],
            codes=["a"],
            severities=["error"],
        )
        # Group expanded after set_step_status -> chips eligible to show.
        assert w._issues_group.isChecked()

        # Collapse via the group's toggled handler.
        w._issues_group.setChecked(False)
        # _on_issues_group_toggled hides chips along with the list.
        assert not w._chip_scroll.isVisibleTo(w._issues_group)
        assert not w._issues_list.isVisibleTo(w._issues_group)

        # Re-expand restores both.
        w._issues_group.setChecked(True)
        assert w._chip_scroll.isVisibleTo(w._issues_group)
        assert w._issues_list.isVisibleTo(w._issues_group)
    finally:
        w.close()

