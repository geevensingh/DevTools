"""Tests for the W7 Plan-layout step pane.

Same shape as the W5/W6 wizard-integration tests: we monkeypatch the
pure ``plan_layout`` call (or the whole adapter) and verify that the
step's 4-pane state machine, table population, ignore list, status
mapping, and "Show on map" / re-run navigation all behave correctly.
"""

from __future__ import annotations

import time
from pathlib import Path

import pytest
from PySide6 import QtCore, QtWidgets

from officemapmaker.calibration import Calibration, Label
from officemapmaker.io_assignments import Assignment
from officemapmaker.layout import (
    FitStrategy,
    Layout,
    LayoutEntry,
    LayoutIssue,
    NameEntry,
    OfficeNumberPlacement,
)
from officemapmaker.wizard.main_window import MainWindow, StepStatus
from officemapmaker.wizard.steps.calibrate_step import CalibrateStep
from officemapmaker.wizard.steps.layout_step import (
    LayoutStep,
    _classify_issues,
    _issue_key,
    _run_plan_layout,
)


@pytest.fixture(scope="module")
def qapp():
    import os

    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication([])
    yield app


@pytest.fixture
def inputs(tmp_path: Path):
    map_path = tmp_path / "demo.png"
    assn_path = tmp_path / "demo.xlsx"
    try:
        from PIL import Image

        Image.new("RGB", (64, 64), color=(255, 255, 255)).save(map_path)
    except ImportError:  # pragma: no cover - PIL is a project dep
        map_path.write_bytes(b"\x89PNG\r\n\x1a\n" + b"a" * 64)
    assn_path.write_bytes(b"PK\x03\x04" + b"b" * 64)
    return map_path, assn_path, tmp_path


def _drain_until(predicate, *, timeout_s: float = 5.0, tick_ms: int = 10) -> bool:
    deadline = time.monotonic() + timeout_s
    app = QtWidgets.QApplication.instance()
    assert app is not None
    while time.monotonic() < deadline:
        app.processEvents(
            QtCore.QEventLoop.ProcessEventsFlag.AllEvents, tick_ms
        )
        if predicate():
            return True
    return False


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _install_calibration(w: MainWindow, *, labels: list[Label] | None = None) -> Calibration:
    cal = Calibration(
        map_image="demo.png",
        map_hash="sha256:fake",
        labels=list(labels or []),
    )
    w.session.calibration = cal
    cal_step = w._steps[0].widget
    if isinstance(cal_step, CalibrateStep):
        try:
            cal_step._build_editor_pane(cal)
            cal_step._stack.setCurrentWidget(cal_step._editor_pane)
        except Exception:
            pass
    return cal


def _go_to_layout(w: MainWindow) -> LayoutStep:
    w._sidebar.setCurrentRow(3)
    step = w._steps[3].widget
    assert isinstance(step, LayoutStep)
    return step


def _make_entry(office_id: str, *, x: int = 10, y: int = 20) -> LayoutEntry:
    name = NameEntry(
        full_name="Jane Doe",
        rendered_text="Jane Doe",
        bbox=(x + 2, y + 2, 40, 12),
        font_px=12,
    )
    number = OfficeNumberPlacement(
        text=office_id, bbox=(x + 40, y + 30, 20, 10), font_px=10
    )
    return LayoutEntry(
        office_id=office_id,
        room_id=1,
        team="BITS",
        fit_strategy=FitStrategy.SHRINK,
        names=[name],
        office_number=number,
        inscribed_rect=(x, y, 50, 40),
        leader_lines=[],
    )


def _make_layout(*entries: LayoutEntry) -> Layout:
    return Layout(
        map_image="demo.png", map_hash="sha256:fake", entries=list(entries)
    )


# ---------------------------------------------------------------------------
# Classifier
# ---------------------------------------------------------------------------


def test_classify_error_wins():
    issues = [
        LayoutIssue(
            severity="warning", code="leader_line_fallback",
            message="x", office_id="1480",
        ),
        LayoutIssue(
            severity="error", code="person_not_placed",
            message="y", office_id="1481", person="Jane Doe",
        ),
    ]
    status, msgs, codes, sevs = _classify_issues(issues)
    assert status == StepStatus.ERROR
    assert len(msgs) == 2
    assert codes == ["leader_line_fallback", "person_not_placed"]
    assert sevs == ["warning", "error"]


def test_classify_warning_when_no_errors():
    issues = [
        LayoutIssue(
            severity="warning", code="abbreviation_fallback",
            message="x", office_id="1480",
        ),
    ]
    status, *_ = _classify_issues(issues)
    assert status == StepStatus.WARNING


def test_classify_empty_is_ok():
    status, *_ = _classify_issues([])
    assert status == StepStatus.OK


def test_issue_key_distinguishes_codes_and_offices():
    a = LayoutIssue(severity="warning", code="leader_line_fallback",
                    message="x", office_id="1480")
    b = LayoutIssue(severity="warning", code="abbreviation_fallback",
                    message="x", office_id="1480")
    c = LayoutIssue(severity="warning", code="leader_line_fallback",
                    message="x", office_id="1481")
    keys = {_issue_key(a), _issue_key(b), _issue_key(c)}
    assert len(keys) == 3


# ---------------------------------------------------------------------------
# Step 4 is the real LayoutStep, not a placeholder
# ---------------------------------------------------------------------------


def test_step_four_is_layout_step_instance(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert isinstance(w._steps[3].widget, LayoutStep)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# No-calibration pane shown when the user navigates to step 4 first
# ---------------------------------------------------------------------------


def test_no_calibration_pane_shown_without_calibration(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert w.session.calibration is None
        step = _go_to_layout(w)
        assert step._stack.currentWidget() is step._no_cal_pane
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Landing pane shown when calibration exists but no run yet
# ---------------------------------------------------------------------------


def test_landing_pane_shown_when_calibration_present(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_layout(w)
        assert step._stack.currentWidget() is step._landing_pane
        assert step._run_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Run -> results pane with table populated + WARNING status, session.layout set
# ---------------------------------------------------------------------------


def test_run_button_populates_results_with_warning_status(
    qapp, inputs, monkeypatch
):
    map_path, assn_path, out = inputs

    layout = _make_layout(_make_entry("1480"))
    issues = [
        LayoutIssue(
            severity="warning", code="leader_line_fallback",
            message="office 1481 didn't fit", office_id="1481",
        ),
    ]
    assignments = [Assignment(name="Jane Doe", office_id="1480",
                              team="BITS", source_row=2)]

    def fake_adapter(_map, _cal, _assn_path, _cached, _preview_path,
                     *, progress_cb, cancel_cb):
        progress_cb(0.5, "planning")
        return (layout, assignments, None), issues

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.layout_step._run_plan_layout",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_layout(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert step._stack.currentWidget() is step._results_pane
        assert step._table.rowCount() == 1
        assert w._steps[3].status == StepStatus.WARNING
        # session.layout should be set so downstream steps see it.
        assert w.session.layout is layout
        # And the cached assignments stash from W5 is filled even if
        # W5 wasn't visited.
        assert getattr(w.session, "_cached_assignments", None) is assignments
        # WARNINGS allow Next.
        assert w._can_advance()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Errors block Next
# ---------------------------------------------------------------------------


def test_errors_block_next(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    layout = _make_layout()
    issues = [
        LayoutIssue(
            severity="error", code="person_not_placed",
            message="Jane Doe not placed", office_id="9999", person="Jane Doe",
        ),
    ]

    def fake_adapter(_map, _cal, _assn_path, _cached, _preview_path,
                     *, progress_cb, cancel_cb):
        return (layout, [], None), issues

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.layout_step._run_plan_layout",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_layout(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert w._steps[3].status == StepStatus.ERROR
        assert not w._can_advance()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Clean run -> OK + empty-state visible
# ---------------------------------------------------------------------------


def test_clean_run_shows_empty_state(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    layout = _make_layout(_make_entry("1480"))

    def fake_adapter(_map, _cal, _assn_path, _cached, _preview_path,
                     *, progress_cb, cancel_cb):
        return (layout, [], None), []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.layout_step._run_plan_layout",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_layout(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        assert not step._empty_label.isHidden()
        assert step._table.rowCount() == 0
        assert w._steps[3].status == StepStatus.OK
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Ignore removes a row and (if all issues ignored) flips status to OK
# ---------------------------------------------------------------------------


def test_ignore_removes_row_and_updates_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    layout = _make_layout(_make_entry("1480"))
    issue = LayoutIssue(
        severity="warning", code="abbreviation_fallback",
        message="office 1480 required initials", office_id="1480",
    )

    def fake_adapter(_map, _cal, _assn_path, _cached, _preview_path,
                     *, progress_cb, cancel_cb):
        return (layout, [], None), [issue]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.layout_step._run_plan_layout",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_layout(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert w._steps[3].status == StepStatus.WARNING
        assert step._table.rowCount() == 1

        step._on_ignore_clicked(issue)

        assert step._table.rowCount() == 0
        assert not step._empty_label.isHidden()
        assert w._steps[3].status == StepStatus.OK
        assert "1 issue" in step._footer_label.text()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Re-run clears ignored, invalidates downstream, and re-fetches
# ---------------------------------------------------------------------------


def test_rerun_clears_ignored_and_invalidates_downstream(
    qapp, inputs, monkeypatch
):
    map_path, assn_path, out = inputs

    layout = _make_layout(_make_entry("1480"))
    issue = LayoutIssue(
        severity="warning", code="leader_line_fallback",
        message="office 1480 didn't fit", office_id="1480",
    )

    def fake_adapter(_map, _cal, _assn_path, _cached, _preview_path,
                     *, progress_cb, cancel_cb):
        return (layout, [], None), [issue]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.layout_step._run_plan_layout",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_layout(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        step._on_ignore_clicked(issue)
        assert _issue_key(issue) in step._ignored
        # Pretend build was OK before; re-run should reset it.
        w.set_step_status("build", StepStatus.OK)
        assert w._steps[4].status == StepStatus.OK

        step._rerun_button.click()
        assert _drain_until(lambda: step._table.rowCount() == 1)
        assert step._ignored == set()
        assert w._steps[3].status == StepStatus.WARNING
        # Build was invalidated.
        assert w._steps[4].status == StepStatus.PENDING
    finally:
        w.close()


# ---------------------------------------------------------------------------
# "Show on map" with an entry centers preview; without entry, falls back
# to navigating to step 0.
# ---------------------------------------------------------------------------


def test_show_on_map_with_entry_centers_preview(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    entry = _make_entry("1480", x=100, y=200)
    layout = _make_layout(entry)
    issue = LayoutIssue(
        severity="warning", code="abbreviation_fallback",
        message="initials", office_id="1480",
    )

    def fake_adapter(_map, _cal, _assn_path, _cached, _preview_path,
                     *, progress_cb, cancel_cb):
        return (layout, [], None), [issue]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.layout_step._run_plan_layout",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_layout(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        # Spy on center_on_bbox.
        called = {}

        def spy(bbox):
            called["bbox"] = bbox

        step._preview_view.center_on_bbox = spy  # type: ignore[method-assign]

        step._on_show_on_map("1480")
        assert "bbox" in called
        # We expect the bbox to enclose the inscribed rect (100,200,50,40).
        x, y, w_, h_ = called["bbox"]
        assert x <= 100 and y <= 200
        assert x + w_ >= 150 and y + h_ >= 240
        # No navigation triggered.
        assert w._current_index == 3
    finally:
        w.close()


def test_show_on_map_without_entry_falls_back_to_step_zero(
    qapp, inputs, monkeypatch
):
    map_path, assn_path, out = inputs

    # No entry for 9999 -- only the issue references it.
    layout = _make_layout()
    issue = LayoutIssue(
        severity="error", code="person_not_placed",
        message="not placed", office_id="9999", person="Jane Doe",
    )

    def fake_adapter(_map, _cal, _assn_path, _cached, _preview_path,
                     *, progress_cb, cancel_cb):
        return (layout, [], None), [issue]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.layout_step._run_plan_layout",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_layout(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        step._on_show_on_map("9999")
        # Fallback navigated us to step 0.
        assert w._current_index == 0
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Activating step 4 with an existing session.layout shows results immediately
# ---------------------------------------------------------------------------


def test_activation_with_existing_session_layout_shows_results(qapp, inputs):
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        w.session.layout = _make_layout(_make_entry("1480"))
        # Visit step 4 directly. Without a cached _last_issues, but with
        # session.layout set, we still go to the results pane.
        step = _go_to_layout(w)
        assert step._stack.currentWidget() is step._results_pane
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Real adapter end-to-end: cancel before plan runs
# ---------------------------------------------------------------------------


def test_real_adapter_cancel_before_plan(tmp_path: Path):
    """Cancellation between assignments-load and plan_layout raises."""
    from PIL import Image

    map_path = tmp_path / "demo.png"
    Image.new("RGB", (32, 32), color=(255, 255, 255)).save(map_path)

    cal = Calibration(map_image="demo.png", map_hash="sha256:x", labels=[])

    progress_calls: list[tuple[float, str]] = []
    cancel_flag = {"once": False}

    def progress_cb(frac, msg):
        progress_calls.append((frac, msg))

    def cancel_cb():
        # Trigger cancel right after the assignments load.
        if not cancel_flag["once"]:
            cancel_flag["once"] = True
            return True
        return False

    from officemapmaker.pipeline import PipelineCanceled

    cached = [Assignment(name="X", office_id="1480", team="T", source_row=2)]
    with pytest.raises(PipelineCanceled):
        _run_plan_layout(
            map_path, cal, tmp_path / "missing.xlsx", cached, None,
            progress_cb=progress_cb, cancel_cb=cancel_cb,
        )
    # We at least made it to the "Planning layout" progress callback or
    # earlier; we shouldn't have hit the "Done" 1.0 message.
    assert not any(frac == 1.0 for frac, _ in progress_calls)


# ---------------------------------------------------------------------------
# Real adapter end-to-end: clean plan with cached assignments, no preview
# ---------------------------------------------------------------------------


def test_real_adapter_clean_plan_with_empty_calibration(tmp_path: Path):
    """A calibration with no labels yields an empty Layout, no issues."""
    from PIL import Image

    map_path = tmp_path / "demo.png"
    Image.new("RGB", (32, 32), color=(255, 255, 255)).save(map_path)

    cal = Calibration(map_image="demo.png", map_hash="sha256:x", labels=[])

    def progress_cb(_frac, _msg):
        pass

    def cancel_cb():
        return False

    cached: list[Assignment] = []
    result, issues = _run_plan_layout(
        map_path, cal, tmp_path / "no.xlsx", cached, None,
        progress_cb=progress_cb, cancel_cb=cancel_cb,
    )
    layout, assignments, preview_path = result
    assert isinstance(layout, Layout)
    assert layout.entries == []
    assert issues == []
    assert assignments == []
    assert preview_path is None


def test_real_adapter_maps_per_office_progress_into_planner_window(tmp_path: Path):
    """The wizard adapter should map plan_layout's 0..1 per-office
    progress into the [0.2, 0.85] window of the overall bar so the UI
    actually ticks during planning (the previously-frozen phase)."""
    from PIL import Image

    import numpy as np
    from officemapmaker.calibration import Label, Room
    from officemapmaker.geometry import mask_to_rle

    map_path = tmp_path / "demo.png"
    Image.new("RGB", (400, 400), color=(255, 255, 255)).save(map_path)

    def _room(rid: int, x: int, y: int, side: int = 60) -> Room:
        mask = np.zeros((400, 400), dtype=bool)
        mask[y : y + side, x : x + side] = True
        return Room(
            id=rid, polygon_rle=mask_to_rle(mask), area_px=int(mask.sum()),
            bbox=(x, y, side, side),
        )

    def _label(oid: str, rid: int, x: int, y: int) -> Label:
        return Label(
            id=oid, bbox=(x - 5, y - 5, 10, 10), room_id=rid,
            fill_seed=(x, y), ocr_confidence=0.9,
        )

    cal = Calibration(
        map_image="demo.png", map_hash="sha256:x",
        labels=[_label("1480", 1, 80, 80),
                _label("1481", 2, 200, 80),
                _label("1482", 3, 320, 80)],
        rooms=[_room(1, 50, 50), _room(2, 170, 50), _room(3, 290, 50)],
    )

    calls: list[tuple[float, str]] = []

    def progress_cb(f, m):
        calls.append((f, m))

    def cancel_cb():
        return False

    cached = [Assignment(name="A B", office_id="1480", team="T", source_row=2),
              Assignment(name="C D", office_id="1481", team="T", source_row=3),
              Assignment(name="E F", office_id="1482", team="T", source_row=4)]
    _run_plan_layout(
        map_path, cal, tmp_path / "no.xlsx", cached, None,
        progress_cb=progress_cb, cancel_cb=cancel_cb,
    )

    fractions = [f for f, _ in calls]
    # Outer-adapter boundaries are still emitted.
    assert 0.0 in fractions  # "Loading assignments..."
    assert pytest.approx(0.2) in fractions  # "Planning layout for N..."
    assert 1.0 in fractions  # "Done"
    # Every per-office tick lands inside [0.2, 0.85].
    inner = [f for f in fractions if 0.2 < f < 1.0]
    assert inner, "expected per-office progress ticks between 0.2 and 1.0"
    for f in inner:
        assert 0.2 <= f <= 0.85 + 1e-6, (
            f"expected planner tick in [0.2, 0.85], got {f}"
        )
    # The ticks should be monotonically non-decreasing.
    assert inner == sorted(inner)

