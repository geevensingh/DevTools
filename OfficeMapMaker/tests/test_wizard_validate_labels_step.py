"""Tests for the W5 Validate-labels step pane.

Like the W4 tests, these are wizard-level integration tests: we
monkeypatch the pure ``load_assignments`` + ``validate_labels`` calls
inside the step's adapter (or sometimes the adapter itself) and verify
that the step's three-pane state machine, table population, ignore
list, status mapping, and "Show in editor" jump-back all behave
correctly.
"""

from __future__ import annotations

import time
from pathlib import Path

import pytest
from PySide6 import QtCore, QtWidgets

from officemapmaker.calibration import Calibration, Label
from officemapmaker.io_assignments import Assignment
from officemapmaker.validate import ValidationIssue
from officemapmaker.wizard.main_window import MainWindow, StepStatus
from officemapmaker.wizard.steps.calibrate_step import CalibrateStep
from officemapmaker.wizard.steps.validate_labels_step import (
    ValidateLabelsStep,
    _classify_issues,
    _issue_key,
    _run_validate_labels,
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
    # The assignments file is loaded by the real ``load_assignments``
    # only when we test the adapter end-to-end; the wizard tests
    # monkeypatch the adapter so fake bytes are fine.
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
# Helper to install a calibration on the session + the calibrate step.
# Step 2 requires session.calibration to be non-None to show its run UI;
# the "Show in editor" test also needs Step 0's editor pane to be mounted.
# ---------------------------------------------------------------------------


def _install_calibration(w: MainWindow, *, labels: list[Label] | None = None) -> Calibration:
    """Inject a calibration into the wizard the way CalibrateStep would."""
    cal = Calibration(
        map_image="demo.png",
        map_hash="sha256:fake",
        labels=list(labels or []),
    )
    w.session.calibration = cal
    # Best-effort: also mount the editor pane on Step 0 so the
    # "Show in editor" navigation has a real canvas to talk to. Wrap
    # in try/except in case the fake PNG isn't loadable on this Qt
    # build -- the rest of the W5 tests still pass.
    cal_step = w._steps[0].widget
    if isinstance(cal_step, CalibrateStep):
        try:
            cal_step._build_editor_pane(cal)
            cal_step._stack.setCurrentWidget(cal_step._editor_pane)
        except Exception:
            pass
    return cal


def _go_to_validate_labels(w: MainWindow) -> ValidateLabelsStep:
    """Switch the sidebar to step 2 and return the step widget."""
    w._sidebar.setCurrentRow(1)
    step = w._steps[1].widget
    assert isinstance(step, ValidateLabelsStep)
    return step


# ---------------------------------------------------------------------------
# Step is the real ValidateLabelsStep, not a placeholder
# ---------------------------------------------------------------------------


def test_step_two_is_validate_labels_step_instance(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert isinstance(w._steps[1].widget, ValidateLabelsStep)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# No-calibration pane shown when the user navigates to step 2 first
# ---------------------------------------------------------------------------


def test_no_calibration_pane_shown_without_calibration(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert w.session.calibration is None
        step = _go_to_validate_labels(w)
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
        step = _go_to_validate_labels(w)
        assert step._stack.currentWidget() is step._landing_pane
        assert step._run_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Run -> results pane with the table populated
# ---------------------------------------------------------------------------


def test_run_button_populates_results_table(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    err = ValidationIssue(
        severity="error",
        code="office_not_on_map",
        message="office 1480 not on map",
        office_id="1480",
        person="Jane Doe",
        source_row=2,
    )
    warn = ValidationIssue(
        severity="warning",
        code="duplicate_row",
        message="duplicate row for John Smith",
        office_id="1481",
        person="John Smith",
        source_row=3,
    )

    def fake_adapter(_cal, _path, *, progress_cb, cancel_cb):
        progress_cb(0.5, "validating")
        # Return (assignments, issues) per the contract.
        return [], [err, warn]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step._run_validate_labels",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_labels(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        # Results pane is now active.
        assert step._stack.currentWidget() is step._results_pane
        assert step._table.rowCount() == 2

        # First row is the error.
        sev_item = step._table.item(0, 0)
        assert sev_item is not None
        assert "ERROR" in sev_item.text()

        # Status mapped to ERROR (any error wins).
        assert w._steps[1].status == StepStatus.ERROR
        # Issues panel shows the stringified ValidationIssue values.
        assert any("office_not_on_map" in s for s in w._steps[1].issues)
        assert any("duplicate_row" in s for s in w._steps[1].issues)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Warning-only result -> WARNING status (Next still enabled)
# ---------------------------------------------------------------------------


def test_warning_only_run_yields_warning_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    warn = ValidationIssue(
        severity="warning",
        code="duplicate_row",
        message="duplicate row",
        office_id="1481",
        person="X",
        source_row=4,
    )

    def fake_adapter(_cal, _path, *, progress_cb, cancel_cb):
        return [], [warn]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step._run_validate_labels",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_labels(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        assert w._steps[1].status == StepStatus.WARNING
        # Warnings do not block Next.
        assert w._can_advance()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Clean run -> OK + empty-state visible
# ---------------------------------------------------------------------------


def test_clean_run_shows_empty_state(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    def fake_adapter(_cal, _path, *, progress_cb, cancel_cb):
        return [Assignment(name="A", office_id="1480", team="X", source_row=2)], []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step._run_validate_labels",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_labels(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        assert not step._empty_label.isHidden()
        assert step._table.rowCount() == 0
        assert w._steps[1].status == StepStatus.OK
        # Assignments stashed on the session for the next steps.
        assert hasattr(w.session, "_cached_assignments")
        assert len(w.session._cached_assignments) == 1
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Ignore removes a row and (if all errors are ignored) flips the status
# ---------------------------------------------------------------------------


def test_ignore_removes_row_and_updates_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    err = ValidationIssue(
        severity="error",
        code="office_not_on_map",
        message="office 1480 not on map",
        office_id="1480",
        person="Jane Doe",
        source_row=2,
    )

    def fake_adapter(_cal, _path, *, progress_cb, cancel_cb):
        return [], [err]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step._run_validate_labels",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_labels(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert w._steps[1].status == StepStatus.ERROR
        assert step._table.rowCount() == 1

        # Simulate the user clicking Ignore on the sole row.
        step._on_ignore_clicked(err)

        # Table is empty; status flips to OK.
        assert step._table.rowCount() == 0
        assert not step._empty_label.isHidden()
        assert w._steps[1].status == StepStatus.OK
        # Footer reflects the ignored count.
        assert "1 issue" in step._footer_label.text()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Re-run resets the ignored set + re-fetches issues
# ---------------------------------------------------------------------------


def test_rerun_clears_ignored_and_refetches(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    err = ValidationIssue(
        severity="error",
        code="office_not_on_map",
        message="office 1480 not on map",
        office_id="1480",
        person="Jane Doe",
        source_row=2,
    )

    def fake_adapter(_cal, _path, *, progress_cb, cancel_cb):
        return [], [err]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step._run_validate_labels",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_labels(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        step._on_ignore_clicked(err)
        assert _issue_key(err) in step._ignored

        # Re-run should clear the ignored set.
        step._rerun_button.click()
        assert _drain_until(lambda: step._table.rowCount() == 1)
        assert step._ignored == set()
        assert w._steps[1].status == StepStatus.ERROR
    finally:
        w.close()


# ---------------------------------------------------------------------------
# "Show in editor" navigates to step 0 + (best-effort) centers a label
# ---------------------------------------------------------------------------


def test_show_in_editor_navigates_to_step_zero(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    err = ValidationIssue(
        severity="error",
        code="office_not_on_map",
        message="office 1480 not on map",
        office_id="1480",
        person="Jane Doe",
        source_row=2,
    )

    def fake_adapter(_cal, _path, *, progress_cb, cancel_cb):
        return [], [err]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step._run_validate_labels",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_labels(w)
        # Sanity: we're on step 1 (index of the validate_labels step).
        assert w._current_index == 1

        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        # Trigger the jump-back directly (the action button does the
        # same call internally).
        step._on_show_in_editor("1480")

        # We jumped to step 0 without the "invalidate downstream?" prompt
        # (navigate_to_step bypasses it).
        assert w._current_index == 0
        # Step 1's status was NOT reset by the jump.
        assert w._steps[1].status == StepStatus.ERROR
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Pipeline failure -> ERROR status, buttons re-enabled
# ---------------------------------------------------------------------------


def test_adapter_exception_sets_error_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    def boom(_cal, _path, *, progress_cb, cancel_cb):
        raise RuntimeError("xlsx is corrupted")

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step._run_validate_labels",
        boom,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_calibration(w)
        step = _go_to_validate_labels(w)
        step._run_button.click()
        # MainWindow's failure handler sets ERROR.
        assert _drain_until(lambda: w._steps[1].status == StepStatus.ERROR)
        assert step._run_button.isEnabled()
        assert step._rerun_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Unit tests on the pure helpers
# ---------------------------------------------------------------------------


def test_classify_issues_unit():
    # Empty -> OK.
    status, msgs, codes, sevs = _classify_issues([])
    assert status == StepStatus.OK
    assert msgs == []
    assert codes == []
    assert sevs == []

    # Warnings only -> WARNING.
    w = ValidationIssue(severity="warning", code="dup_row", message="x")
    status, _, w_codes, w_sevs = _classify_issues([w])
    assert status == StepStatus.WARNING
    assert w_codes == ["dup_row"]
    assert w_sevs == ["warning"]

    # Any error -> ERROR (regardless of warnings).
    e = ValidationIssue(severity="error", code="missing", message="y")
    status, _, e_codes, e_sevs = _classify_issues([w, e])
    assert status == StepStatus.ERROR
    assert e_codes == ["dup_row", "missing"]
    assert e_sevs == ["warning", "error"]


def test_issue_key_is_stable_and_distinct():
    a = ValidationIssue(
        severity="error",
        code="X",
        message="m",
        office_id="1480",
        person="Jane",
        source_row=2,
    )
    b = ValidationIssue(
        severity="error",
        code="X",
        message="m",
        office_id="1480",
        person="Jane",
        source_row=2,
    )
    c = ValidationIssue(
        severity="error",
        code="X",
        message="m",
        office_id="1481",
        person="Jane",
        source_row=2,
    )
    assert _issue_key(a) == _issue_key(b)
    assert _issue_key(a) != _issue_key(c)


def test_adapter_calls_load_then_validate(monkeypatch):
    """_run_validate_labels is the bridge between two pure functions."""
    fake_assn = [Assignment(name="A", office_id="1480", team="T", source_row=2)]
    fake_issues = [
        ValidationIssue(severity="warning", code="dup", message="x")
    ]
    calls: list[str] = []

    def fake_load(path):
        calls.append(f"load:{path}")
        return fake_assn

    def fake_validate(cal, assn):
        calls.append(f"validate:{len(list(assn))}")
        return list(fake_issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step.load_assignments",
        fake_load,
    )
    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step.validate_labels",
        fake_validate,
    )

    progress: list[tuple[float, str]] = []

    def cancel():
        return False

    result = _run_validate_labels(
        Calibration(map_image="m", map_hash="sha256:0"),
        Path("xyz.xlsx"),
        progress_cb=lambda f, m: progress.append((f, m)),
        cancel_cb=cancel,
    )

    assignments, issues = result
    assert assignments is fake_assn
    assert issues == fake_issues
    assert calls[0].startswith("load:")
    assert calls[1].startswith("validate:1")
    # Progress was called at start, middle, and end.
    assert progress[0][0] == 0.0
    assert progress[-1][0] == 1.0


def test_adapter_honors_cancel(monkeypatch):
    """If cancel_cb() returns True after the load, raise PipelineCanceled."""
    from officemapmaker.pipeline import PipelineCanceled

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.validate_labels_step.load_assignments",
        lambda _p: [],
    )

    with pytest.raises(PipelineCanceled):
        _run_validate_labels(
            Calibration(map_image="m", map_hash="sha256:0"),
            Path("xyz.xlsx"),
            progress_cb=lambda f, m: None,
            cancel_cb=lambda: True,
        )
