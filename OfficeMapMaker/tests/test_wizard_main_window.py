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
