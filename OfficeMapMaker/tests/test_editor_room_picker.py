"""Tests for the visual room-picker flow in the calibration editor.

Covers the wiring between :class:`InspectorPanel`, :class:`MapCanvas`, and
:class:`EditorController` for the "click a room to link this label" UX:

* Toggling the inspector's pick button puts the canvas into pick mode.
* A simulated room pick fires a :class:`ChangeRoomLinkCommand` so the link
  is undoable.
* Cancelling (Esc / clicked-empty-space) clears the mode and unchecks
  the button without touching the model.
* The button is reset whenever selection moves to a different label —
  pick mode belongs to whichever label was active when armed.

Uses ``pytest-qt``'s ``qtbot`` fixture for widget lifecycle: hand-rolled
``QApplication`` fixtures here caused process-exit crashes on Windows
because ``QGraphicsView`` instances were still alive at GC time.
"""

from __future__ import annotations

import numpy as np
import pytest

pytest.importorskip("PySide6")
pytest.importorskip("pytestqt")
from PySide6 import QtCore, QtGui, QtWidgets  # noqa: E402

from officemapmaker.calibration import (  # noqa: E402
    Calibration,
    Label,
    RenderDefaults,
    Room,
)
from officemapmaker.editor.canvas import MapCanvas  # noqa: E402
from officemapmaker.editor.controller import EditorController  # noqa: E402
from officemapmaker.editor.sidebar import InspectorPanel  # noqa: E402
from officemapmaker.geometry import mask_to_rle  # noqa: E402


# ---------------------------------------------------------------- helpers


def _tiny_rle(h: int = 4, w: int = 4) -> str:
    """A non-empty mask so RoomItem actually builds a polygon."""
    mask = np.ones((h, w), dtype=np.uint8)
    return mask_to_rle(mask)


def _mklabel(
    id_: str,
    *,
    room_id: int | None,
    bbox: tuple[int, int, int, int] = (0, 0, 10, 10),
) -> Label:
    return Label(
        id=id_,
        bbox=bbox,
        room_id=room_id,
        fill_seed=(5, 5),
        ocr_confidence=0.9,
        notes="",
    )


def _mkroom(id_: int) -> Room:
    return Room(
        id=id_,
        polygon_rle=_tiny_rle(),
        area_px=16,
        bbox=(0, 0, 4, 4),
    )


def _mkcal(labels: list[Label], rooms: list[Room]) -> Calibration:
    return Calibration(
        map_image="m.png",
        map_hash="sha256:deadbeef",
        labels=labels,
        rooms=rooms,
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )


def _build_controller(cal: Calibration, tmp_path, qtbot) -> tuple[
    EditorController, MapCanvas, InspectorPanel
]:
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_calibration(cal)
    inspector = InspectorPanel()
    qtbot.addWidget(inspector)
    controller = EditorController(
        calibration=cal,
        calibration_path=tmp_path / "calibration.json",
        canvas=canvas,
        inspector=inspector,
    )
    return controller, canvas, inspector


def _show_label(inspector: InspectorPanel, cal: Calibration, idx: int) -> None:
    """Drive the inspector into the "label X is selected" state."""
    inspector.show_label(
        label=cal.labels[idx],
        label_index=idx,
        available_room_ids=sorted(r.id for r in cal.rooms),
    )


# ---------------------------------------------- inspector signal contract


def test_inspector_pick_button_emits_room_pick_requested(qtbot):
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=None)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    inspector = InspectorPanel()
    qtbot.addWidget(inspector)
    _show_label(inspector, cal, 0)

    received: list[tuple[int, bool]] = []
    inspector.room_pick_requested.connect(
        lambda idx, active: received.append((idx, active))
    )

    button: QtWidgets.QToolButton = inspector._label_widgets["room_pick_button"]
    button.click()  # check
    button.click()  # uncheck

    assert received == [(0, True), (0, False)]


def test_inspector_set_room_pick_active_does_not_re_emit(qtbot):
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=None)],
        rooms=[_mkroom(1)],
    )
    inspector = InspectorPanel()
    qtbot.addWidget(inspector)
    _show_label(inspector, cal, 0)

    received: list[tuple[int, bool]] = []
    inspector.room_pick_requested.connect(
        lambda idx, active: received.append((idx, active))
    )

    # Programmatic state-sync from the controller must NOT bounce a phantom
    # request back into the controller — otherwise turning the button off in
    # response to a pick would re-arm the canvas.
    inspector.set_room_pick_active(True)
    inspector.set_room_pick_active(False)
    assert received == []


# --------------------------------------------------- controller wiring


def test_pick_button_puts_canvas_into_pick_mode(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=None)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
    _show_label(inspector, cal, 0)

    assert canvas.room_pick_mode() is False
    inspector._label_widgets["room_pick_button"].click()
    assert canvas.room_pick_mode() is True


def test_canvas_room_picked_pushes_change_room_link_command(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=None)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
    _show_label(inspector, cal, 0)

    # Arm pick mode for label 0.
    inspector._label_widgets["room_pick_button"].click()
    assert canvas.room_pick_mode() is True

    # Simulate the canvas emitting "user clicked room 2".
    canvas.room_picked.emit(2)

    # The link is now 2 …
    assert cal.labels[0].room_id == 2
    # … the canvas left pick mode …
    assert canvas.room_pick_mode() is False
    # … the inspector button is back to unchecked …
    assert (
        inspector._label_widgets["room_pick_button"].isChecked() is False
    )
    # … and the action is undoable.
    assert controller.is_dirty() is True
    controller.undo_stack().undo()
    assert cal.labels[0].room_id is None


def test_canvas_pick_cancelled_does_not_touch_model(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=1)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
    _show_label(inspector, cal, 0)

    inspector._label_widgets["room_pick_button"].click()
    canvas.room_pick_cancelled.emit()

    assert cal.labels[0].room_id == 1
    assert canvas.room_pick_mode() is False
    assert (
        inspector._label_widgets["room_pick_button"].isChecked() is False
    )
    assert controller.is_dirty() is False


def test_picking_same_room_is_a_noop(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=1)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
    _show_label(inspector, cal, 0)

    inspector._label_widgets["room_pick_button"].click()
    # Picking the room the label is already linked to should not push a
    # no-op onto the undo stack — that would let the user "undo" something
    # they never did.
    canvas.room_picked.emit(1)

    assert cal.labels[0].room_id == 1
    assert controller.is_dirty() is False


def test_unchecking_pick_button_leaves_canvas_mode(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=None)],
        rooms=[_mkroom(1)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
    _show_label(inspector, cal, 0)

    button = inspector._label_widgets["room_pick_button"]
    button.click()  # arm
    assert canvas.room_pick_mode() is True
    button.click()  # disarm
    assert canvas.room_pick_mode() is False


# ----------------------------------------------------- canvas behavior


def test_canvas_set_room_pick_mode_changes_cursor(qtbot):
    canvas = MapCanvas()
    qtbot.addWidget(canvas)

    assert canvas.room_pick_mode() is False
    canvas.set_room_pick_mode(True)
    assert canvas.room_pick_mode() is True
    assert canvas.viewport().cursor().shape() == QtCore.Qt.CursorShape.CrossCursor

    canvas.set_room_pick_mode(False)
    assert canvas.room_pick_mode() is False
    # After unsetCursor() the viewport falls back to its inherited cursor —
    # don't pin the exact shape, just confirm it's no longer the crosshair.
    assert canvas.viewport().cursor().shape() != QtCore.Qt.CursorShape.CrossCursor


def test_canvas_escape_cancels_pick_mode(qtbot):
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_room_pick_mode(True)

    cancelled: list[bool] = []
    canvas.room_pick_cancelled.connect(lambda: cancelled.append(True))

    ev = QtGui.QKeyEvent(
        QtCore.QEvent.Type.KeyPress,
        QtCore.Qt.Key.Key_Escape,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.keyPressEvent(ev)

    assert canvas.room_pick_mode() is False
    assert cancelled == [True]


def test_canvas_escape_outside_pick_mode_is_ignored(qtbot):
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    cancelled: list[bool] = []
    canvas.room_pick_cancelled.connect(lambda: cancelled.append(True))

    ev = QtGui.QKeyEvent(
        QtCore.QEvent.Type.KeyPress,
        QtCore.Qt.Key.Key_Escape,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.keyPressEvent(ev)

    # Esc when not in pick mode falls through to super().keyPressEvent
    # (which does nothing useful for us). Just make sure we didn't emit.
    assert cancelled == []


# ------------------------------------------- selection-change side-effect


def test_switching_labels_disarms_pick_button(qtbot):
    cal = _mkcal(
        labels=[
            _mklabel("1480", room_id=None),
            _mklabel("1481", room_id=None),
        ],
        rooms=[_mkroom(1)],
    )
    inspector = InspectorPanel()
    qtbot.addWidget(inspector)
    _show_label(inspector, cal, 0)
    inspector._label_widgets["room_pick_button"].click()
    assert inspector._label_widgets["room_pick_button"].isChecked() is True

    # Selection moves to the other label. The inspector must drop the pick
    # button — picking belongs to whichever label was selected at the time
    # of arming, and re-arming on the new label is one extra click anyway.
    _show_label(inspector, cal, 1)
    assert inspector._label_widgets["room_pick_button"].isChecked() is False
