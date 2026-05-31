"""Tests for the W6 Validate-fill step pane.

Like the W4/W5 tests, these are wizard-level integration tests: we
monkeypatch the pure ``validate_fill`` call inside the step's adapter
(or sometimes the adapter itself) and verify that the step's three-pane
state machine, table population, ignore list, status mapping
(ADVISORY, not ERROR/WARNING -- leaks are informational), and "Show in
editor" jump-back all behave correctly.
"""

from __future__ import annotations

import time
from pathlib import Path

import pytest
from PySide6 import QtCore, QtWidgets

from officemapmaker.calibration import Calibration, Label
from officemapmaker.validate import FillLeak
from officemapmaker.wizard.main_window import MainWindow, StepStatus
from officemapmaker.wizard.steps.calibrate_step import CalibrateStep
from officemapmaker.wizard.steps.validate_fill_step import (
    ValidateFillStep,
    _classify_issues,
    _issue_key,
    _run_validate_fill,
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
    # Real (tiny) PNG so the calibrate step's editor mount succeeds
    # when we navigate to step 0 in the "Show in editor" test.
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
# Helpers to install calibration on the session + jump to step 3.
# ---------------------------------------------------------------------------


def _install_calibration(w: MainWindow, *, labels: list[Label] | None = None) -> Calibration:
    """Inject a calibration into the wizard the way CalibrateStep would."""
    cal = Calibration(
        map_image="demo.png",
        map_hash="sha256:fake",
        labels=list(labels or []),
    )
    w.session.calibration = cal
    # Best-effort: mount the editor pane on Step 0 so the "Show in
    # editor" navigation has a real canvas to talk to. Mounting may
    # fail on minimal CI builds; the rest of the tests tolerate it.
    cal_step = w._steps[0].widget
    if isinstance(cal_step, CalibrateStep):
        try:
            cal_step._build_editor_pane(cal)
            cal_step._stack.setCurrentWidget(cal_step._editor_pane)
        except Exception:
            pass
    return cal


def _go_to_validate_fill(w: MainWindow) -> ValidateFillStep:
    """Switch the sidebar to step 3 (validate_fill) and return the widget."""
    w._sidebar.setCurrentRow(2)
    step = w._steps[2].widget
    assert isinstance(step, ValidateFillStep)
    return step


# ---------------------------------------------------------------------------
# Step 3 is the real ValidateFillStep, not a placeholder
# ---------------------------------------------------------------------------


def test_step_three_is_validate_fill_step_instance(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert isinstance(w._steps[2].widget, ValidateFillStep)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# No-calibration pane shown when the user navigates to step 3 first
# ---------------------------------------------------------------------------


def test_no_calibration_pane_shown_without_calibration(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert w.session.calibration is None
        step = _go_to_validate_fill(w)
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
        step = _go_to_validate_fill(w)
        assert step._stack.currentWidget() is step._landing_pane
        assert step._run_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Run -> results pane with the table populated and ADVISORY status
# ---------------------------------------------------------------------------


def test_run_button_populates_results_table_with_advisory_status(
    qapp, inputs, monkeypatch
):
    map_path, assn_path, out = inputs

    leak1 = FillLeak(
        severity="warning",
        code="leak_oversized",
        office_id="1480",
        room_id=42,
        message="office 1480 leaked through wall gap",
        suggested_patch=(100, 100, 110, 100),
    )
    leak2 = FillLeak(
        severity="warning",
        code="leak_oversized_vs_median",
        office_id="1481",
        room_id=43,
        message="office 1481: polygon may itself be two rooms merged",
    )

    def fake_adapter(_map, _cal, *, progress_cb, cancel_cb):
        progress_cb(0.5, "checking")
        return None, [leak1, leak2]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step._run_validate_fill",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_fill(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_leaks is not None)

        # Results pane is active.
        assert step._stack.currentWidget() is step._results_pane
        assert step._table.rowCount() == 2

        # Severity column shows WARNING (the only severity in
        # FillLeak today).
        sev_item = step._table.item(0, 0)
        assert sev_item is not None
        assert "WARNING" in sev_item.text()

        # Status mapped to ADVISORY (warnings only -> advisory; never
        # WARNING for this step because leaks don't block).
        assert w._steps[2].status == StepStatus.ADVISORY
        # Issues panel shows the stringified FillLeak values.
        assert any("leak_oversized" in s for s in w._steps[2].issues)
        # Warnings/advisory do not block Next.
        assert w._can_advance()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Clean run -> OK + empty-state visible
# ---------------------------------------------------------------------------


def test_clean_run_shows_empty_state(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    def fake_adapter(_map, _cal, *, progress_cb, cancel_cb):
        return None, []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step._run_validate_fill",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_fill(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_leaks is not None)
        assert not step._empty_label.isHidden()
        assert step._table.rowCount() == 0
        assert w._steps[2].status == StepStatus.OK
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Ignore removes a row and (if all leaks are ignored) flips status to OK
# ---------------------------------------------------------------------------


def test_ignore_removes_row_and_updates_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    leak = FillLeak(
        severity="warning",
        code="leak_into_other_office",
        office_id="1480",
        room_id=42,
        message="office 1480 leaks into office 1481",
        leak_into_office_id="1481",
    )

    def fake_adapter(_map, _cal, *, progress_cb, cancel_cb):
        return None, [leak]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step._run_validate_fill",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_fill(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_leaks is not None)

        assert w._steps[2].status == StepStatus.ADVISORY
        assert step._table.rowCount() == 1

        step._on_ignore_clicked(leak)

        # Table is empty; status flips to OK.
        assert step._table.rowCount() == 0
        assert not step._empty_label.isHidden()
        assert w._steps[2].status == StepStatus.OK
        # Footer reflects the ignored count.
        assert "1 leak" in step._footer_label.text()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Re-run clears the ignored set + re-fetches leaks
# ---------------------------------------------------------------------------


def test_rerun_clears_ignored_and_refetches(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    leak = FillLeak(
        severity="warning",
        code="leak_oversized",
        office_id="1480",
        room_id=42,
        message="office 1480 leaked",
    )

    def fake_adapter(_map, _cal, *, progress_cb, cancel_cb):
        return None, [leak]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step._run_validate_fill",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_fill(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_leaks is not None)
        step._on_ignore_clicked(leak)
        assert _issue_key(leak) in step._ignored

        # Re-run clears the ignored set.
        step._rerun_button.click()
        assert _drain_until(lambda: step._table.rowCount() == 1)
        assert step._ignored == set()
        assert w._steps[2].status == StepStatus.ADVISORY
    finally:
        w.close()


# ---------------------------------------------------------------------------
# "Show in editor" for a label-centric code navigates to step 0
# ---------------------------------------------------------------------------


def test_show_label_navigates_to_step_zero(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    leak = FillLeak(
        severity="warning",
        code="leak_oversized",
        office_id="1480",
        room_id=42,
        message="office 1480 leaked",
    )

    def fake_adapter(_map, _cal, *, progress_cb, cancel_cb):
        return None, [leak]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step._run_validate_fill",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_fill(w)
        # Sanity: we're on step 2 (validate_fill).
        assert w._current_index == 2

        step._run_button.click()
        assert _drain_until(lambda: step._last_leaks is not None)

        step._on_show_label("1480")

        # navigate_to_step bypassed the "invalidate downstream?" prompt.
        assert w._current_index == 0
        # Step 2's status was NOT reset by the jump.
        assert w._steps[2].status == StepStatus.ADVISORY
    finally:
        w.close()


# ---------------------------------------------------------------------------
# "Show in editor" for a room-centric code navigates + selects the room
# ---------------------------------------------------------------------------


def test_show_room_navigates_to_step_zero_and_selects_room(
    qapp, inputs, monkeypatch
):
    map_path, assn_path, out = inputs

    leak = FillLeak(
        severity="warning",
        code="leak_oversized_vs_median",
        office_id="1480",
        room_id=42,
        message="office 1480 polygon may be two rooms merged",
    )

    def fake_adapter(_map, _cal, *, progress_cb, cancel_cb):
        return None, [leak]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step._run_validate_fill",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_fill(w)

        step._run_button.click()
        assert _drain_until(lambda: step._last_leaks is not None)

        # Capture how select_room was called by monkeypatching the
        # bound method on whatever canvas the calibrate step exposes.
        canvas = step._calibrate_canvas()
        calls: list[int] = []
        if canvas is not None:
            monkeypatch.setattr(
                canvas, "select_room", lambda rid: calls.append(rid)
            )

        step._on_show_room(42)

        assert w._current_index == 0
        if canvas is not None:
            assert calls == [42]
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Pipeline failure -> ERROR status, buttons re-enabled
# ---------------------------------------------------------------------------


def test_adapter_exception_sets_error_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    def boom(_map, _cal, *, progress_cb, cancel_cb):
        raise RuntimeError("map.png is corrupted")

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step._run_validate_fill",
        boom,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_fill(w)
        step._run_button.click()
        # MainWindow's failure handler sets ERROR.
        assert _drain_until(lambda: w._steps[2].status == StepStatus.ERROR)
        assert step._run_button.isEnabled()
        assert step._rerun_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Unit tests on the pure helpers
# ---------------------------------------------------------------------------


def test_classify_issues_unit():
    # Empty -> OK.
    status, msgs = _classify_issues([])
    assert status == StepStatus.OK
    assert msgs == []

    # Warnings only -> ADVISORY (not WARNING -- the renderer clips).
    w = FillLeak(
        severity="warning", code="leak_oversized", office_id="1", room_id=2, message="m"
    )
    status, _ = _classify_issues([w])
    assert status == StepStatus.ADVISORY

    # Defensive: any error -> ERROR (future-proofing in case a new
    # leak code is introduced as an error).
    e = FillLeak(
        severity="error", code="x", office_id="1", room_id=2, message="m"
    )
    status, _ = _classify_issues([w, e])
    assert status == StepStatus.ERROR


def test_issue_key_is_stable_and_distinct():
    a = FillLeak(
        severity="warning",
        code="leak_into_other_office",
        office_id="1480",
        room_id=2,
        message="m",
        leak_into_office_id="1481",
    )
    b = FillLeak(
        severity="warning",
        code="leak_into_other_office",
        office_id="1480",
        room_id=2,
        message="m",
        leak_into_office_id="1481",
    )
    # Same key for two FillLeaks with the same code/office/leak_into.
    assert _issue_key(a) == _issue_key(b)

    # Different leak_into_office_id -> different key (a leaks into 1481
    # vs leaks into 1482 are distinct issues even if from the same
    # source room).
    c = FillLeak(
        severity="warning",
        code="leak_into_other_office",
        office_id="1480",
        room_id=2,
        message="m",
        leak_into_office_id="1482",
    )
    assert _issue_key(a) != _issue_key(c)


def test_adapter_calls_validate_fill(monkeypatch):
    """_run_validate_fill is a thin wrapper around the pure function."""
    fake_leaks = [
        FillLeak(
            severity="warning",
            code="leak_oversized",
            office_id="1480",
            room_id=2,
            message="x",
        )
    ]
    calls: list[str] = []

    def fake_validate(map_path, cal):
        calls.append(f"validate:{map_path}")
        return list(fake_leaks)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step.validate_fill",
        fake_validate,
    )

    progress: list[tuple[float, str]] = []

    def cancel():
        return False

    result = _run_validate_fill(
        Path("demo.png"),
        Calibration(map_image="m", map_hash="sha256:0"),
        progress_cb=lambda f, m: progress.append((f, m)),
        cancel_cb=cancel,
    )

    artifact, leaks = result
    # No artifact -- validate_fill is diagnostic-only.
    assert artifact is None
    assert leaks == fake_leaks
    assert calls[0].startswith("validate:")
    # Progress called at start and end.
    assert progress[0][0] == 0.0
    assert progress[-1][0] == 1.0


def test_adapter_honors_cancel(monkeypatch):
    """If cancel_cb() returns True before the fill check, raise PipelineCanceled."""
    from officemapmaker.pipeline import PipelineCanceled

    # validate_fill should never be called; if it is, that's a bug.
    def boom(_map, _cal):
        raise AssertionError("validate_fill should not be called when canceled")

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_fill_step.validate_fill",
        boom,
    )

    with pytest.raises(PipelineCanceled):
        _run_validate_fill(
            Path("demo.png"),
            Calibration(map_image="m", map_hash="sha256:0"),
            progress_cb=lambda f, m: None,
            cancel_cb=lambda: True,
        )
