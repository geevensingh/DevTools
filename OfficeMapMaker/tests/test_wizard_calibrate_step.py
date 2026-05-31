"""Tests for the W4 Calibrate step pane.

These tests cover the wizard-level integration: that the step is
swapped in for the placeholder, that the lifecycle hook fires, that
the landing pane is shown when no calibration is cached, that the
Run button kicks off the pipeline, and that a successful result
mounts the editor pane + persists into the session.

The pipeline call itself (``calibrate_map``) is monkeypatched out --
real OCR + connected-component analysis on a real map is covered by
``tests/test_calibrate.py``; here we just want to know the wiring is
right.
"""

from __future__ import annotations

import time
from pathlib import Path

import pytest
from PySide6 import QtCore, QtWidgets

from officemapmaker.calibrate import CalibrationIssue
from officemapmaker.calibration import Calibration
from officemapmaker.wizard.main_window import MainWindow, StepStatus
from officemapmaker.wizard.steps.calibrate_step import (
    CalibrateStep,
    _classify_issues,
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
    # Write a real (tiny) PNG so the editor's set_map_image() call
    # succeeds. Using PIL (already a project dep) -- the canvas works
    # fine with a 64x64 white image; we never actually run OCR or
    # render against it.
    try:
        from PIL import Image

        Image.new("RGB", (64, 64), color=(255, 255, 255)).save(map_path)
    except ImportError:  # pragma: no cover - PIL is a project dep
        map_path.write_bytes(b"\x89PNG\r\n\x1a\n" + b"a" * 64)
    # Assignments file is never actually loaded in these tests (W5 will
    # exercise that path); fake bytes are fine here.
    assn_path.write_bytes(b"PK\x03\x04" + b"b" * 64)
    return map_path, assn_path, tmp_path


def _drain_until(predicate, *, timeout_s: float = 5.0, tick_ms: int = 10) -> bool:
    """Spin Qt's event loop until ``predicate()`` is true or timeout."""
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
# Step is the CalibrateStep, not a placeholder
# ---------------------------------------------------------------------------


def test_step_one_is_calibrate_step_instance(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert isinstance(w._steps[0].widget, CalibrateStep)
        # The other five steps are still placeholders.
        for i in range(1, 6):
            assert not isinstance(w._steps[i].widget, CalibrateStep)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Landing pane shown when no calibration cached
# ---------------------------------------------------------------------------


def test_landing_pane_shown_on_fresh_session(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        step = w._steps[0].widget
        assert isinstance(step, CalibrateStep)
        # Fresh session => no calibration => landing pane visible.
        assert w.session.calibration is None
        assert step._stack.currentWidget() is step._landing
        # The Run button is enabled and waits for the user.
        assert step._run_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Run button kicks off the pipeline and updates state on success
# ---------------------------------------------------------------------------


def test_run_button_kicks_off_pipeline_and_records_result(
    qapp, inputs, monkeypatch
):
    """Clicking Run -> pipeline call -> session.calibration set -> badge OK."""
    map_path, assn_path, out = inputs

    fake_cal = Calibration(map_image="demo.png", map_hash="sha256:fake")

    def fake_calibrate_map(_map_path, *, progress_cb, cancel_cb, **_kw):
        progress_cb(0.5, "halfway")
        return fake_cal, []

    # Patch the module-level reference the step imports.
    monkeypatch.setattr(
        "officemapmaker.wizard.steps.calibrate_step.calibrate_map",
        fake_calibrate_map,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        step = w._steps[0].widget
        assert isinstance(step, CalibrateStep)
        # Simulate the user clicking the Run button.
        step._run_button.click()

        # Wait until the worker thread has come back through the main
        # event loop and on_calibration_finished has fired.
        assert _drain_until(lambda: w.session.calibration is fake_cal)

        # Step status flipped to OK with no issues. _classify_issues
        # returns OK for an empty list.
        assert w._steps[0].status == StepStatus.OK
        assert w._steps[0].issues == []
        # Run button is re-enabled (so a Re-run path stays available).
        assert step._run_button.isEnabled()
        # Editor pane was mounted and is the current widget.
        assert step._editor_built is True
        assert step._stack.currentWidget() is step._editor_pane
    finally:
        w.close()


def test_run_with_warning_issues_sets_warning_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    fake_cal = Calibration(map_image="demo.png", map_hash="sha256:fake")
    warn = CalibrationIssue(
        severity="warning", code="orphan_label", message="label 1480 has no room"
    )

    def fake_calibrate_map(_map_path, *, progress_cb, cancel_cb, **_kw):
        return fake_cal, [warn]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.calibrate_step.calibrate_map",
        fake_calibrate_map,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        step = w._steps[0].widget
        step._run_button.click()

        assert _drain_until(lambda: w.session.calibration is fake_cal)
        assert w._steps[0].status == StepStatus.WARNING
        # The issue's stringified form ends up in the issues list.
        assert any("orphan_label" in iss for iss in w._steps[0].issues)
    finally:
        w.close()


def test_run_with_error_issues_sets_error_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    fake_cal = Calibration(map_image="demo.png", map_hash="sha256:fake")
    err = CalibrationIssue(
        severity="error", code="duplicate_id", message="office 1003 appears twice"
    )

    def fake_calibrate_map(_map_path, *, progress_cb, cancel_cb, **_kw):
        return fake_cal, [err]

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.calibrate_step.calibrate_map",
        fake_calibrate_map,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        step = w._steps[0].widget
        step._run_button.click()

        assert _drain_until(lambda: w.session.calibration is fake_cal)
        assert w._steps[0].status == StepStatus.ERROR
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Failure path: calibrate_map raises -> step goes ERROR, session unchanged
# ---------------------------------------------------------------------------


def test_run_with_exception_sets_error_and_leaves_session_calibration_none(
    qapp, inputs, monkeypatch
):
    map_path, assn_path, out = inputs

    def fake_calibrate_map(_map_path, *, progress_cb, cancel_cb, **_kw):
        raise RuntimeError("boom")

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.calibrate_step.calibrate_map",
        fake_calibrate_map,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        step = w._steps[0].widget
        step._run_button.click()

        assert _drain_until(lambda: w._steps[0].status == StepStatus.ERROR)
        assert w.session.calibration is None
        assert any("boom" in iss for iss in w._steps[0].issues)
        assert step._run_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Lifecycle hooks: MainWindow calls on_activated/on_deactivated
# ---------------------------------------------------------------------------


def test_lifecycle_hooks_fire_on_navigation(qapp, inputs):
    """on_activated fires on initial step + after navigation; on_deactivated fires on leaving."""
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        step = w._steps[0].widget
        assert isinstance(step, CalibrateStep)

        # Spy on the hooks by wrapping them.
        activated_count = {"n": 0}
        deactivated_count = {"n": 0}
        original_activated = step.on_activated
        original_deactivated = step.on_deactivated

        def spy_activated():
            activated_count["n"] += 1
            original_activated()

        def spy_deactivated():
            deactivated_count["n"] += 1
            original_deactivated()

        step.on_activated = spy_activated  # type: ignore[method-assign]
        step.on_deactivated = spy_deactivated  # type: ignore[method-assign]

        # Navigate to step 2 -> on_deactivated should fire once.
        w._sidebar.setCurrentRow(1)
        assert deactivated_count["n"] == 1
        # And step 1 should NOT have been activated again.
        assert activated_count["n"] == 0

        # Navigate back to step 1 -> on_activated should fire.
        w._sidebar.setCurrentRow(0)
        assert activated_count["n"] == 1
    finally:
        w.close()


def test_initial_activation_fires_on_construction(qapp, inputs):
    """Confirm the __init__ end-of-flow calls on_activated for step 0."""
    map_path, assn_path, out = inputs
    # We can't observe the activation directly post-hoc, but we can
    # observe its effect: the landing pane should be the current
    # widget (on_activated is what sets that).
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        step = w._steps[0].widget
        assert isinstance(step, CalibrateStep)
        assert step._stack.currentWidget() is step._landing
    finally:
        w.close()


# ---------------------------------------------------------------------------
# _classify_issues helper
# ---------------------------------------------------------------------------


def test_classify_issues_empty_is_ok():
    status, msgs = _classify_issues([])
    assert status == StepStatus.OK
    assert msgs == []


def test_classify_issues_warning_only():
    issues = [
        CalibrationIssue(severity="warning", code="a", message="x"),
        CalibrationIssue(severity="warning", code="b", message="y"),
    ]
    status, msgs = _classify_issues(issues)
    assert status == StepStatus.WARNING
    assert len(msgs) == 2


def test_classify_issues_error_dominates():
    """If any error is present, the step is ERROR even alongside warnings."""
    issues = [
        CalibrationIssue(severity="warning", code="a", message="x"),
        CalibrationIssue(severity="error", code="b", message="y"),
    ]
    status, _ = _classify_issues(issues)
    assert status == StepStatus.ERROR


# ---------------------------------------------------------------------------
# Restored session with cached calibration -> editor pane on activation
# ---------------------------------------------------------------------------


def test_session_with_cached_calibration_mounts_editor(qapp, inputs):
    """If session.calibration exists at activation, the step mounts the
    editor pane (not the landing pane)."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        fake_cal = Calibration(map_image="demo.png", map_hash="sha256:fake")
        w.session.calibration = fake_cal

        step = w._steps[0].widget
        assert isinstance(step, CalibrateStep)

        # Re-fire the lifecycle hook now that the session has a cal.
        step.on_activated()

        assert step._editor_built is True
        assert step._editor_pane is not None
        assert step._stack.currentWidget() is step._editor_pane
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Live re-validation on every edit
# ---------------------------------------------------------------------------


def _two_room_cal_with_orphan(map_path: Path):
    """Build a real 2-room calibration where label 1480 is unassigned.

    Re-uses the synthetic CC + OCR helpers from test_calibrate to land
    on the same code path the wizard hits in production.
    """
    import numpy as np

    from officemapmaker.calibrate import _build_calibration, _OCRLabel
    from officemapmaker.geometry import ConnectedComponent

    def _square_cc(cc_id, x, y, side):
        mask = np.zeros((y + side + 10, x + side + 10), dtype=np.uint8)
        mask[y : y + side, x : x + side] = 1
        return ConnectedComponent(
            cc_id=cc_id,
            area_px=side * side,
            bbox=(x, y, side, side),
            centroid=(x + side // 2, y + side // 2),
            mask=mask.astype(bool),
        )

    components = [_square_cc(1, 10, 10, 100), _square_cc(2, 200, 10, 100)]
    ocr_labels = [
        _OCRLabel(text="1480", bbox=(40, 40, 30, 20), confidence=0.9),
        _OCRLabel(text="1481", bbox=(230, 40, 30, 20), confidence=0.9),
    ]
    cal, _ = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    # Force label 1480 into orphan state -- the wizard test fixture's
    # 64x64 white PNG bbox-checks would otherwise place both labels
    # cleanly; we want a known issue baseline to measure.
    from dataclasses import replace as dc_replace

    for i, lab in enumerate(cal.labels):
        if lab.id == "1480":
            cal.labels[i] = dc_replace(lab, room_id=None)
            break
    return cal


def test_undo_index_changed_recomputes_issue_count(qapp, inputs):
    """Editing the calibration should refresh the step's issue list live."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        step = w._steps[0].widget
        step.on_activated()
        assert step._editor_built is True

        # Prime the step with the initial issue list (mimicking what
        # _on_calibration_finished would do after a real run).
        from officemapmaker.calibrate import revalidate_calibration

        initial = revalidate_calibration(cal)
        initial_status, initial_msgs = _classify_issues(initial)
        w.set_step_status("calibrate", initial_status, issues=initial_msgs)
        n_initial = len(w._steps[0].issues)
        assert n_initial >= 1, "fixture should produce at least one issue"

        # Simulate the user fixing the orphan label by assigning room_id.
        from dataclasses import replace as dc_replace

        for i, lab in enumerate(cal.labels):
            if lab.id == "1480":
                cal.labels[i] = dc_replace(lab, room_id=1)
                break

        # Fire the undo signal directly -- this is the slot wired to
        # the EditorController's undo_stack.indexChanged in production.
        step._on_undo_index_changed(0)

        n_after = len(w._steps[0].issues)
        assert n_after < n_initial, (
            f"issue count should drop after fix: {n_initial} -> {n_after}"
        )
    finally:
        w.close()


def test_undo_index_changed_is_noop_when_session_calibration_is_none(qapp, inputs):
    """Calling the slot before a calibration exists shouldn't crash."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        step = w._steps[0].widget
        # No calibration set, no editor built. The slot should still
        # save the session (no-op effectively) and return cleanly.
        assert w.session.calibration is None
        step._on_undo_index_changed(0)  # must not raise
    finally:
        w.close()


def test_undo_index_changed_clears_status_to_ok_when_no_issues(qapp, inputs):
    """If every issue is fixed, the badge should drop to OK."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        step = w._steps[0].widget
        step.on_activated()

        # Seed with WARNING (orphan).
        w.set_step_status(
            "calibrate", StepStatus.WARNING, issues=["seeded warning"]
        )

        # Fix the orphan.
        from dataclasses import replace as dc_replace

        for i, lab in enumerate(cal.labels):
            if lab.id == "1480":
                cal.labels[i] = dc_replace(lab, room_id=1)
                break

        step._on_undo_index_changed(0)

        assert w._steps[0].status == StepStatus.OK
        assert w._steps[0].issues == []
    finally:
        w.close()
