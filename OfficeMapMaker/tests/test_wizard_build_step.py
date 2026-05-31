"""Tests for the W8 Build-composite step pane.

Same shape as the W6/W7 wizard-integration tests: we monkeypatch the
``render_composite`` adapter and verify the step's 3-pane state
machine, table population, ignore list, status mapping, re-run cascade,
and "Show on map" navigation.
"""

from __future__ import annotations

import time
from pathlib import Path

import pytest
from PySide6 import QtCore, QtWidgets

from officemapmaker.calibration import Calibration, Label, Room
from officemapmaker.io_assignments import Assignment
from officemapmaker.layout import Layout
from officemapmaker.render import RenderIssue, RenderResult
from officemapmaker.wizard.main_window import MainWindow, StepStatus
from officemapmaker.wizard.steps.build_step import (
    BuildStep,
    _classify_issues,
    _issue_key,
    _run_render_composite,
)
from officemapmaker.wizard.steps.calibrate_step import CalibrateStep


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


def _make_label(office_id: str, room_id: int) -> Label:
    return Label(
        id=office_id, bbox=(100, 100, 40, 12), room_id=room_id,
        fill_seed=(120, 110), ocr_confidence=0.99,
    )


def _make_room(room_id: int, *, x: int = 95, y: int = 95) -> Room:
    return Room(id=room_id, polygon_rle="", area_px=2500, bbox=(x, y, 50, 40))


def _install_calibration(
    w: MainWindow,
    *,
    labels: list[Label] | None = None,
    rooms: list[Room] | None = None,
) -> Calibration:
    cal = Calibration(
        map_image="demo.png",
        map_hash="sha256:fake",
        labels=list(labels or []),
        rooms=list(rooms or []),
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


def _install_layout(w: MainWindow) -> Layout:
    """Empty Layout is enough to flip the 'no-layout' pane off."""
    lay = Layout(map_image="demo.png", map_hash="sha256:fake", entries=[])
    w.session.layout = lay
    return lay


def _go_to_build(w: MainWindow) -> BuildStep:
    w._sidebar.setCurrentRow(4)
    step = w._steps[4].widget
    assert isinstance(step, BuildStep)
    return step


def _make_render_result(
    composite_path: Path, issues: list[RenderIssue] | None = None,
) -> RenderResult:
    return RenderResult(
        composite_path=composite_path,
        review_path=composite_path.with_name(
            composite_path.stem + "_review.png"
        ),
        issues=list(issues or []),
        palette=None,
        changed_pixel_count=0,
        unexpected_pixel_count=0,
    )


# ---------------------------------------------------------------------------
# Classifier
# ---------------------------------------------------------------------------


def test_classify_error_wins():
    issues = [
        RenderIssue(severity="warning", code="fill_leak_clipped", message="x",
                    office_id="1480"),
        RenderIssue(severity="error", code="palette_team_missing", message="y",
                    office_id="1481"),
    ]
    status, msgs, codes, sevs = _classify_issues(issues)
    assert status == StepStatus.ERROR
    assert len(msgs) == 2
    assert codes == ["fill_leak_clipped", "palette_team_missing"]
    assert sevs == ["warning", "error"]


def test_classify_warning_when_no_errors():
    issues = [
        RenderIssue(severity="warning", code="fill_leak_clipped", message="x"),
    ]
    status, *_ = _classify_issues(issues)
    assert status == StepStatus.WARNING


def test_classify_empty_is_ok():
    status, *_ = _classify_issues([])
    assert status == StepStatus.OK


def test_issue_key_distinguishes_codes_and_offices():
    a = RenderIssue(severity="warning", code="fill_leak_clipped",
                    message="x", office_id="1480")
    b = RenderIssue(severity="warning", code="palette_low_contrast",
                    message="x", office_id="1480")
    c = RenderIssue(severity="warning", code="fill_leak_clipped",
                    message="x", office_id="1481")
    keys = {_issue_key(a), _issue_key(b), _issue_key(c)}
    assert len(keys) == 3


# ---------------------------------------------------------------------------
# Step 5 is the real BuildStep, not a placeholder
# ---------------------------------------------------------------------------


def test_step_five_is_build_step_instance(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert isinstance(w._steps[4].widget, BuildStep)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# No-layout pane shown when navigating without layout
# ---------------------------------------------------------------------------


def test_no_layout_pane_shown_without_layout(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        # No layout installed yet.
        step = _go_to_build(w)
        assert step._stack.currentWidget() is step._no_layout_pane
    finally:
        w.close()


def test_no_layout_pane_shown_without_calibration(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # Neither calibration nor layout.
        step = _go_to_build(w)
        assert step._stack.currentWidget() is step._no_layout_pane
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Landing pane shown when both calibration + layout exist but no run
# ---------------------------------------------------------------------------


def test_landing_pane_shown_when_prereqs_present(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        _install_layout(w)
        step = _go_to_build(w)
        assert step._stack.currentWidget() is step._landing_pane
        assert step._run_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Run -> results pane with table populated + WARNING status
# ---------------------------------------------------------------------------


def test_run_button_populates_results_with_warning_status(
    qapp, inputs, monkeypatch
):
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")  # exists so mount works
    render_result = _make_render_result(
        composite,
        issues=[
            RenderIssue(severity="warning", code="fill_leak_clipped",
                        message="office 1481 leaked", office_id="1481"),
        ],
    )
    assignments = [Assignment(name="Jane Doe", office_id="1480",
                              team="BITS", source_row=2)]

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        progress_cb(0.5, "rendering")
        return (render_result, assignments), list(render_result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert step._stack.currentWidget() is step._results_pane
        assert step._table.rowCount() == 1
        assert w._steps[4].status == StepStatus.WARNING
        # Composite path remembered.
        assert step._composite_path == composite
        # Cached assignments populated for the tile step.
        assert getattr(w.session, "_cached_assignments", None) is assignments
        # Open in Explorer is enabled once we have a composite.
        assert step._open_button.isEnabled()
        # Warnings allow Next.
        assert w._can_advance()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Errors block Next
# ---------------------------------------------------------------------------


def test_errors_block_next(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")
    render_result = _make_render_result(
        composite,
        issues=[
            RenderIssue(severity="error", code="palette_team_missing",
                        message="team missing", office_id="1480"),
        ],
    )

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        return (render_result, []), list(render_result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert w._steps[4].status == StepStatus.ERROR
        assert not w._can_advance()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Clean run -> OK + empty-state visible
# ---------------------------------------------------------------------------


def test_clean_run_shows_empty_state(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")
    render_result = _make_render_result(composite, issues=[])

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        return (render_result, []), []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        assert not step._empty_label.isHidden()
        assert step._table.rowCount() == 0
        assert w._steps[4].status == StepStatus.OK
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Ignore removes a row and (if all issues ignored) flips status to OK
# ---------------------------------------------------------------------------


def test_ignore_removes_row_and_updates_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")
    issue = RenderIssue(severity="warning", code="fill_leak_clipped",
                        message="office 1480 leaked", office_id="1480")
    render_result = _make_render_result(composite, issues=[issue])

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        return (render_result, []), list(render_result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert w._steps[4].status == StepStatus.WARNING
        assert step._table.rowCount() == 1

        step._on_ignore_clicked(issue)

        assert step._table.rowCount() == 0
        assert not step._empty_label.isHidden()
        assert w._steps[4].status == StepStatus.OK
        assert "1 issue" in step._footer_label.text()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Re-build invalidates downstream (tile)
# ---------------------------------------------------------------------------


def test_rerun_invalidates_downstream_tile(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")
    issue = RenderIssue(severity="warning", code="fill_leak_clipped",
                        message="office 1480 leaked", office_id="1480")
    render_result = _make_render_result(composite, issues=[issue])

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        return (render_result, []), list(render_result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        step._on_ignore_clicked(issue)
        assert _issue_key(issue) in step._ignored

        # Pretend tile was OK before; re-build should reset it.
        w.set_step_status("tile", StepStatus.OK)
        assert w._steps[5].status == StepStatus.OK

        step._rerun_button.click()
        assert _drain_until(lambda: step._table.rowCount() == 1)
        assert step._ignored == set()
        assert w._steps[4].status == StepStatus.WARNING
        # Tile was invalidated.
        assert w._steps[5].status == StepStatus.PENDING
    finally:
        w.close()


# ---------------------------------------------------------------------------
# "Show on map" with a known label centers the preview on its room bbox
# ---------------------------------------------------------------------------


def test_show_on_map_with_known_office_centers_preview(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")
    issue = RenderIssue(severity="warning", code="fill_leak_clipped",
                        message="x", office_id="1480")
    render_result = _make_render_result(composite, issues=[issue])

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        return (render_result, []), list(render_result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(
            w,
            labels=[_make_label("1480", room_id=7)],
            rooms=[_make_room(7, x=300, y=400)],
        )
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        # Spy on center_on_bbox.
        called = {}

        def spy(bbox):
            called["bbox"] = bbox

        step._preview_view.center_on_bbox = spy  # type: ignore[method-assign]

        step._on_show_on_map("1480")
        assert called.get("bbox") == (300, 400, 50, 40)
        assert w._current_index == 4
    finally:
        w.close()


def test_show_on_map_case_insensitive(qapp, inputs, monkeypatch):
    """`render_composite` uppercases office ids when keying. Make sure
    the calibration lookup is case-insensitive too."""
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")
    issue = RenderIssue(severity="warning", code="fill_leak_clipped",
                        message="x", office_id="1480A")
    render_result = _make_render_result(composite, issues=[issue])

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        return (render_result, []), list(render_result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(
            w,
            labels=[_make_label("1480a", room_id=7)],  # lowercase!
            rooms=[_make_room(7, x=200, y=300)],
        )
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        called = {}

        def spy(bbox):
            called["bbox"] = bbox

        step._preview_view.center_on_bbox = spy  # type: ignore[method-assign]
        step._on_show_on_map("1480A")  # uppercase issue, lowercase label
        assert called.get("bbox") == (200, 300, 50, 40)
    finally:
        w.close()


def test_show_on_map_unknown_office_falls_back_to_step_zero(
    qapp, inputs, monkeypatch
):
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")
    issue = RenderIssue(severity="error", code="layout_office_not_in_calibration",
                        message="unknown", office_id="9999")
    render_result = _make_render_result(composite, issues=[issue])

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        return (render_result, []), list(render_result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # Calibration has NO label/room for 9999.
        _install_calibration(w)
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        step._on_show_on_map("9999")
        # Fallback navigated us to step 0.
        assert w._current_index == 0
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Activation when previously run shows the results pane (cached state)
# ---------------------------------------------------------------------------


def test_activation_with_cached_state_shows_results(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    composite = out / "composite.png"
    composite.write_bytes(b"fake png bytes")
    render_result = _make_render_result(composite, issues=[])

    def fake_adapter(_map, _cal, _lay, _assn_path, _cached, _output_png,
                     *, progress_cb, cancel_cb):
        return (render_result, []), []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.build_step._run_render_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        _install_layout(w)
        step = _go_to_build(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        assert step._stack.currentWidget() is step._results_pane

        # Navigate away and back; cached state should bring us back to results.
        # Use navigate_to_step to bypass the "re-run downstream?" prompt that
        # _sidebar.setCurrentRow would surface when stepping backward from 4.
        w.navigate_to_step(0)
        w.navigate_to_step(4)
        assert step._stack.currentWidget() is step._results_pane
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Real adapter end-to-end smoke test: cancel before render
# ---------------------------------------------------------------------------


def test_real_adapter_cancel_before_render(tmp_path: Path):
    from PIL import Image

    map_path = tmp_path / "demo.png"
    Image.new("RGB", (32, 32), color=(255, 255, 255)).save(map_path)

    cal = Calibration(map_image="demo.png", map_hash="sha256:x")
    lay = Layout(map_image="demo.png", map_hash="sha256:x", entries=[])

    progress_calls: list[tuple[float, str]] = []
    cancel_flag = {"once": False}

    def progress_cb(frac, msg):
        progress_calls.append((frac, msg))

    def cancel_cb():
        if not cancel_flag["once"]:
            cancel_flag["once"] = True
            return True
        return False

    from officemapmaker.pipeline import PipelineCanceled

    cached = [Assignment(name="X", office_id="1480", team="T", source_row=2)]
    with pytest.raises(PipelineCanceled):
        _run_render_composite(
            map_path, cal, lay, tmp_path / "no.xlsx", cached,
            tmp_path / "composite.png",
            progress_cb=progress_cb, cancel_cb=cancel_cb,
        )
    # We shouldn't have hit the "Done" 1.0 message.
    assert not any(frac == 1.0 for frac, _ in progress_calls)


# ---------------------------------------------------------------------------
# Real adapter end-to-end smoke test: empty layout renders OK
# ---------------------------------------------------------------------------


def test_real_adapter_empty_layout_writes_composite(tmp_path: Path):
    from PIL import Image

    map_path = tmp_path / "demo.png"
    Image.new("RGB", (32, 32), color=(255, 255, 255)).save(map_path)

    cal = Calibration(map_image="demo.png", map_hash="sha256:x")
    lay = Layout(map_image="demo.png", map_hash="sha256:x", entries=[])

    def progress_cb(_frac, _msg):
        pass

    def cancel_cb():
        return False

    cached: list[Assignment] = []
    composite_path = tmp_path / "composite.png"
    result, issues = _run_render_composite(
        map_path, cal, lay, tmp_path / "no.xlsx", cached, composite_path,
        progress_cb=progress_cb, cancel_cb=cancel_cb,
    )
    render_result, assignments = result
    assert isinstance(render_result, RenderResult)
    assert render_result.composite_path == composite_path
    assert composite_path.exists()
    # An empty layout has no team-color issues; should be clean.
    assert issues == []
    assert assignments == []
