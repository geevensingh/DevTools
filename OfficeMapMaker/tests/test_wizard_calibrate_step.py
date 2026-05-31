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
    status, msgs, targets, codes, sevs = _classify_issues([])
    assert status == StepStatus.OK
    assert msgs == []
    assert targets == []
    assert codes == []
    assert sevs == []


def test_classify_issues_warning_only():
    issues = [
        CalibrationIssue(severity="warning", code="a", message="x"),
        CalibrationIssue(severity="warning", code="b", message="y"),
    ]
    status, msgs, targets, codes, sevs = _classify_issues(issues)
    assert status == StepStatus.WARNING
    assert len(msgs) == 2
    assert targets == [None, None]
    assert codes == ["a", "b"]
    assert sevs == ["warning", "warning"]


def test_classify_issues_error_dominates():
    """If any error is present, the step is ERROR even alongside warnings."""
    issues = [
        CalibrationIssue(severity="warning", code="a", message="x"),
        CalibrationIssue(severity="error", code="b", message="y"),
    ]
    status, _, _, codes, sevs = _classify_issues(issues)
    assert status == StepStatus.ERROR
    assert codes == ["a", "b"]
    assert sevs == ["warning", "error"]


def test_classify_issues_propagates_points():
    """Each issue's ``point`` flows into the returned ``targets`` list."""
    issues = [
        CalibrationIssue(
            severity="warning", code="orphan_label",
            message="x", point=(100, 200),
        ),
        CalibrationIssue(
            severity="warning", code="no_rooms_detected",
            message="y",  # point defaults to None
        ),
        CalibrationIssue(
            severity="warning", code="orphan_room",
            message="z", point=(300, 400),
        ),
    ]
    _, _, targets, _, _ = _classify_issues(issues)
    assert targets == [(100, 200), None, (300, 400)]


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
        (
            initial_status,
            initial_msgs,
            _initial_targets,
            _initial_codes,
            _initial_sevs,
        ) = _classify_issues(initial)
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


def test_undo_index_changed_uses_quick_revalidate(qapp, inputs, monkeypatch):
    """Live revalidation must call revalidate_calibration with quick=True.

    The mask-decode path takes multi-seconds on real calibrations, so
    every keystroke / drag would hang the UI. This test pins the
    contract that the live hook never asks for the slow check.
    """
    from officemapmaker.wizard.steps import calibrate_step as cs_mod

    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        step = w._steps[0].widget
        step.on_activated()

        calls: list[dict] = []
        real = cs_mod.revalidate_calibration

        def spy(cal_arg, **kwargs):
            calls.append(kwargs)
            return real(cal_arg, **kwargs)

        monkeypatch.setattr(cs_mod, "revalidate_calibration", spy)

        step._on_undo_index_changed(0)

        assert calls == [{"quick": True}], (
            f"live revalidation must pass quick=True; got {calls}"
        )
    finally:
        w.close()


def test_revalidate_button_runs_full_check(qapp, inputs, monkeypatch):
    """The Re-validate toolbar button should call revalidate_calibration
    with quick=False so the slow mask check actually runs."""
    from officemapmaker.wizard.steps import calibrate_step as cs_mod

    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        step = w._steps[0].widget
        step.on_activated()

        calls: list[dict] = []
        real = cs_mod.revalidate_calibration

        def spy(cal_arg, **kwargs):
            calls.append(kwargs)
            return real(cal_arg, **kwargs)

        monkeypatch.setattr(cs_mod, "revalidate_calibration", spy)

        step._on_revalidate_clicked()

        assert calls == [{"quick": False}], (
            f"Re-validate must run the full check; got {calls}"
        )
    finally:
        w.close()


def test_revalidate_button_is_noop_when_no_calibration(qapp, inputs):
    """Clicking Re-validate before any calibration runs should not crash."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert w.session.calibration is None
        step = w._steps[0].widget
        # Don't activate / build editor -- just hit the handler.
        step._on_revalidate_clicked()  # must not raise
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Add-room toolbar wiring (Shift+N / Shift+R / Shift+P)
# ---------------------------------------------------------------------------


def _toolbar_action(step, label_text: str):
    """Find a QAction on the calibrate-step toolbar by its visible text."""
    assert step._toolbar is not None, "toolbar should be built after on_activated"
    for action in step._toolbar.actions():
        if action.text() == label_text:
            return action
    raise AssertionError(
        f"toolbar action {label_text!r} not found; "
        f"have {[a.text() for a in step._toolbar.actions()]}"
    )


@pytest.mark.parametrize(
    "label,controller_attr",
    [
        ("Add room: flood-fill", "set_add_room_flood_mode"),
        ("Add room: rectangle", "set_add_room_rect_mode"),
        ("Add room: polygon", "set_add_room_polygon_mode"),
    ],
)
def test_add_room_toolbar_actions_exist_and_route_to_controller(
    qapp, inputs, monkeypatch, label, controller_attr
):
    """Each Add-room toolbar button should toggle the corresponding
    controller mode. These were dropped from the wizard at some
    point; this test pins them so they don't regress again."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        step = w._steps[0].widget
        step.on_activated()  # builds editor + toolbar

        action = _toolbar_action(step, label)
        assert action.isCheckable(), f"{label!r} must be checkable"

        calls: list[bool] = []
        monkeypatch.setattr(
            step._controller, controller_attr, lambda checked: calls.append(checked)
        )

        action.setChecked(True)
        assert calls == [True], f"{label!r} on -> {controller_attr}(True); got {calls}"

        action.setChecked(False)
        assert calls == [True, False], (
            f"{label!r} off -> {controller_attr}(False); got {calls}"
        )
    finally:
        w.close()


@pytest.mark.parametrize(
    "label,cancel_signal",
    [
        ("Add room: flood-fill", "add_room_flood_cancelled"),
        ("Add room: rectangle", "add_room_rect_cancelled"),
        ("Add room: polygon", "add_room_polygon_cancelled"),
    ],
)
def test_add_room_toolbar_resyncs_on_canvas_cancel(
    qapp, inputs, label, cancel_signal
):
    """If the canvas drops out of add-room mode (Esc, etc.), the
    toolbar toggle should pop back up so it doesn't stay 'down'."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        step = w._steps[0].widget
        step.on_activated()

        action = _toolbar_action(step, label)
        # Force-check the toggle (bypassing the controller call) so
        # we can verify the canvas-cancel handler clears it.
        action.blockSignals(True)
        action.setChecked(True)
        action.blockSignals(False)
        assert action.isChecked()

        getattr(step._canvas, cancel_signal).emit()
        assert not action.isChecked(), (
            f"{label!r} should pop up when {cancel_signal} fires"
        )
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Click-to-navigate from the issues panel
# ---------------------------------------------------------------------------


def test_navigate_to_issue_target_centers_canvas(qapp, inputs):
    """``navigate_to_issue_target((x, y))`` switches to the editor pane and
    asks the canvas to centre on the requested point."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        step = w._steps[0].widget
        step.on_activated()

        captured: dict = {}
        orig = step._canvas.center_on_point

        def recorder(x, y, *, min_zoom=1.0):
            captured["xy"] = (x, y)
            captured["min_zoom"] = min_zoom
            return orig(x, y, min_zoom=min_zoom)

        step._canvas.center_on_point = recorder  # type: ignore[assignment]

        step.navigate_to_issue_target((123, 456))

        assert captured["xy"] == (123, 456)
        assert captured["min_zoom"] == 1.0
        assert step._stack.currentWidget() is step._editor_pane
    finally:
        w.close()


def test_issue_panel_click_calls_navigate_to_issue_target(qapp, inputs):
    """Clicking an issue row in the panel dispatches to the current
    step's ``navigate_to_issue_target`` with the row's stored target."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        step = w._steps[0].widget
        step.on_activated()

        captured: dict = {}

        def stub(target):
            captured["target"] = target

        step.navigate_to_issue_target = stub  # type: ignore[assignment]

        w.set_step_status(
            "calibrate", StepStatus.WARNING,
            issues=["one with target", "one without"],
            issue_targets=[(11, 22), None],
        )

        # Simulate the user clicking the first row.
        first = w._issues_list.item(0)
        assert first is not None
        assert first.data(QtCore.Qt.ItemDataRole.UserRole) == (11, 22)
        w._on_issue_item_activated(first)
        assert captured["target"] == (11, 22)

        # Second row has no target; clicking it must not call the stub
        # (so we'd still see the first call's value).
        captured.clear()
        second = w._issues_list.item(1)
        assert second is not None
        assert second.data(QtCore.Qt.ItemDataRole.UserRole) is None
        w._on_issue_item_activated(second)
        assert "target" not in captured
    finally:
        w.close()


def test_set_step_status_stores_issue_targets(qapp, inputs):
    """``set_step_status(... issue_targets=[...])`` is stored on the entry
    and padded/truncated to issues length."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # Exact match.
        w.set_step_status(
            "calibrate", StepStatus.WARNING,
            issues=["a", "b"], issue_targets=[(1, 2), None],
        )
        assert w._steps[0].issue_targets == [(1, 2), None]

        # No targets supplied -> all None, parallel to issues.
        w.set_step_status(
            "calibrate", StepStatus.WARNING, issues=["only"]
        )
        assert w._steps[0].issue_targets == [None]

        # Too few targets -> right-padded with None.
        w.set_step_status(
            "calibrate", StepStatus.WARNING,
            issues=["a", "b", "c"], issue_targets=[(9, 9)],
        )
        assert w._steps[0].issue_targets == [(9, 9), None, None]

        # Too many targets -> truncated.
        w.set_step_status(
            "calibrate", StepStatus.WARNING,
            issues=["only"], issue_targets=[(1, 1), (2, 2), (3, 3)],
        )
        assert w._steps[0].issue_targets == [(1, 1)]
    finally:
        w.close()


def test_on_activated_pushes_targets_after_session_restore(qapp, inputs):
    """A restored session has issue messages but no per-issue targets.
    ``on_activated`` must run a quick revalidation so the panel's
    click-to-show works on the first launch after restart."""
    map_path, assn_path, out = inputs

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        cal = _two_room_cal_with_orphan(map_path)
        w.session.calibration = cal
        # Pre-seed entry.issues with bare strings (no targets) to mimic
        # what the __init__ restore path does.
        w._steps[0].issues = ["stale message"]
        w._steps[0].issue_targets = []

        step = w._steps[0].widget
        step.on_activated()

        # The orphan label "1480" has bbox (40,40,30,20), so its
        # bbox_center is (55, 50). _classify_issues should have
        # populated at least one target with a real point.
        assert any(t is not None for t in w._steps[0].issue_targets)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Inline find-by-id search box (toolbar)
# ---------------------------------------------------------------------------


def _activated_step_with_two_labels(inputs):
    """Build a MainWindow + activated CalibrateStep on a 2-label fixture.

    The fixture has labels ``1480`` (orphan) and ``1481``; both are
    accessible to ``find_label_indices`` regardless of orphan state.
    """
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    cal = _two_room_cal_with_orphan(map_path)
    w.session.calibration = cal
    step = w._steps[0].widget
    step.on_activated()
    return w, step


def test_search_box_is_mounted_in_toolbar(qapp, inputs):
    """The Find: label, line edit, and results counter live in the
    toolbar between the Delete-selected action and the right-aligned
    spacer."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        assert step._search_box is not None
        assert step._search_results_label is not None
        # The QLineEdit must actually be added to the toolbar; check
        # via widgetForAction is awkward for raw QWidget inserts, so
        # walk the toolbar's children.
        children = step._toolbar.findChildren(QtWidgets.QLineEdit)
        assert step._search_box in children
    finally:
        w.close()


def test_search_text_change_updates_match_count(qapp, inputs):
    """Typing in the search box should refresh the cached matches and
    update the "N matches" hint."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        step._search_box.setText("148")
        # Both 1480 and 1481 contain "148".
        assert len(step._search_matches) == 2
        assert "2 matches" in step._search_results_label.text()
        assert step._search_cursor == -1  # not jumped yet
    finally:
        w.close()


def test_search_no_match_shows_no_matches(qapp, inputs):
    w, step = _activated_step_with_two_labels(inputs)
    try:
        step._search_box.setText("9999")
        assert step._search_matches == []
        assert step._search_results_label.text() == "no matches"
    finally:
        w.close()


def test_search_empty_query_clears_hint(qapp, inputs):
    """Clearing the search box should clear the hint -- not show 'no matches'."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        step._search_box.setText("148")
        assert step._search_results_label.text() != ""
        step._search_box.setText("")
        assert step._search_results_label.text() == ""
    finally:
        w.close()


def test_search_enter_jumps_to_first_match(qapp, inputs):
    """Pressing Enter (or calling _on_find_next) jumps to the first
    match and calls center_on_label."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        captured: list = []
        step._canvas.center_on_label = (  # type: ignore[assignment]
            lambda idx: captured.append(idx)
        )

        step._search_box.setText("148")
        step._on_find_next()

        assert len(captured) == 1
        assert step._search_cursor == 0
        # Hint switches to "1 of N" once cycling begins.
        assert "1 of 2" in step._search_results_label.text()
    finally:
        w.close()


def test_search_find_next_cycles_and_wraps(qapp, inputs):
    """F3 / Find next steps forward and wraps from last → first."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        captured: list = []
        step._canvas.center_on_label = (  # type: ignore[assignment]
            lambda idx: captured.append(idx)
        )

        step._search_box.setText("148")
        step._on_find_next()  # cursor -> 0
        step._on_find_next()  # cursor -> 1
        step._on_find_next()  # cursor wraps -> 0

        assert len(captured) == 3
        assert step._search_cursor == 0
        assert "1 of 2" in step._search_results_label.text()
    finally:
        w.close()


def test_search_find_previous_wraps(qapp, inputs):
    """Shift+F3 / Find previous from the first match wraps to the last."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        captured: list = []
        step._canvas.center_on_label = (  # type: ignore[assignment]
            lambda idx: captured.append(idx)
        )

        step._search_box.setText("148")
        step._on_find_previous()  # from -1, step -1 -> -2 % 2 -> 0
        # Then step previous again -> wraps to last (index 1).
        step._on_find_previous()

        assert len(captured) == 2
        assert step._search_cursor == 1
        assert "2 of 2" in step._search_results_label.text()
    finally:
        w.close()


def test_search_find_next_with_empty_match_is_noop(qapp, inputs):
    """F3 with no matches must not crash and must not jump anywhere."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        captured: list = []
        step._canvas.center_on_label = (  # type: ignore[assignment]
            lambda idx: captured.append(idx)
        )

        step._search_box.setText("9999")
        step._on_find_next()

        assert captured == []
        assert step._search_cursor == -1
    finally:
        w.close()


def test_focus_search_focuses_box_and_selects_all(qapp, inputs):
    """Ctrl+F (``_focus_search``) puts focus in the search box and
    selects existing text so retyping replaces."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        step._search_box.setText("1480")

        # On offscreen Qt, hasFocus() doesn't reliably reflect setFocus()
        # without a visible top-level window AND active window state, so
        # instead record the call directly.
        focus_calls: list = []
        orig_focus = step._search_box.setFocus
        step._search_box.setFocus = (  # type: ignore[assignment]
            lambda reason=QtCore.Qt.FocusReason.OtherFocusReason: (
                focus_calls.append(reason), orig_focus(reason)
            )
        )

        step._focus_search()

        assert focus_calls == [QtCore.Qt.FocusReason.ShortcutFocusReason]
        # selectAll() works regardless of focus state.
        assert step._search_box.selectedText() == "1480"
    finally:
        w.close()


def test_search_rerun_clears_state(qapp, inputs):
    """When ``_refresh_editor_from_session`` swaps in a new
    calibration, the cached match indices (which reference the old
    label list) must be cleared."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        step._search_box.setText("148")
        assert step._search_matches  # populated

        # Simulate a Re-run that replaced session.calibration with a
        # new instance. The same fixture again is fine -- it's just a
        # different Python object.
        from dataclasses import replace as dc_replace

        new_cal = _two_room_cal_with_orphan(inputs[0])
        # Mutate a label id so the old cache would be stale.
        for i, lab in enumerate(new_cal.labels):
            new_cal.labels[i] = dc_replace(lab, id=lab.id + "X")
        w.session.calibration = new_cal

        step._refresh_editor_from_session()

        assert step._search_matches == []
        assert step._search_cursor == -1
        assert step._search_last_query == ""
        assert step._search_box.text() == ""
        assert step._search_results_label.text() == ""
    finally:
        w.close()


def test_search_recomputes_when_box_text_diverges(qapp, inputs):
    """If the search box's current text differs from the cached query
    (e.g. paste path that doesn't fire textChanged), F3 should rebuild
    the cache before cycling."""
    w, step = _activated_step_with_two_labels(inputs)
    try:
        captured: list = []
        step._canvas.center_on_label = (  # type: ignore[assignment]
            lambda idx: captured.append(idx)
        )

        # Seed the cache with a stale query.
        step._search_box.setText("9999")
        assert step._search_matches == []
        # Now simulate text appearing without firing textChanged.
        step._search_box.blockSignals(True)
        step._search_box.setText("148")
        step._search_box.blockSignals(False)

        step._on_find_next()

        # The cache must have been rebuilt for "148".
        assert step._search_matches and len(step._search_matches) == 2
        assert captured  # we actually jumped
    finally:
        w.close()
