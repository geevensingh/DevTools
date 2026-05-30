"""Tests for the delete-room workflow (ed8).

Covers three feature surfaces in the calibration editor:

* :class:`~officemapmaker.editor.commands.DeleteRoomCommand` model
  mutations: removes the room, clears ``room_id`` on every label that
  was linked to it, restores both on undo, and emits a structural
  ``RoomChange`` with the affected label indices on both redo and undo.
* :class:`~officemapmaker.editor.controller.EditorController` glue:
  ``delete_selected_room`` happy path (label > room priority means the
  caller is expected to try label deletion first), no-op when nothing
  is selected, and the inspector / label-style refresh path that fires
  when an affected label is currently being shown.
* ``RoomChange.label_indices`` payload field added in ed8.

Uses ``pytest-qt``'s ``qtbot`` fixture for widget lifecycle (matches
:mod:`tests.test_editor_add_delete`).
"""

from __future__ import annotations

import numpy as np
import pytest

pytest.importorskip("PySide6")
pytest.importorskip("pytestqt")
from PySide6 import QtCore, QtGui  # noqa: E402

from officemapmaker.calibration import (  # noqa: E402
    Calibration,
    Label,
    RenderDefaults,
    Room,
)
from officemapmaker.editor.canvas import MapCanvas  # noqa: E402
from officemapmaker.editor.commands import (  # noqa: E402
    DeleteRoomCommand,
    RoomChange,
)
from officemapmaker.editor.controller import EditorController  # noqa: E402
from officemapmaker.editor.items import LabelItem, RoomItem  # noqa: E402
from officemapmaker.editor.sidebar import InspectorPanel  # noqa: E402
from officemapmaker.geometry import mask_to_rle  # noqa: E402


# ---------------------------------------------------------------- helpers


def _square_rle(*, x: int, y: int, side: int, canvas_size: int = 200) -> str:
    """Build a full-image RLE mask with a solid square at (x, y) of size side."""
    mask = np.zeros((canvas_size, canvas_size), dtype=np.uint8)
    mask[y : y + side, x : x + side] = 1
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


def _mkroom(id_: int, *, x: int = 0, y: int = 0, side: int = 40) -> Room:
    return Room(
        id=id_,
        polygon_rle=_square_rle(x=x, y=y, side=side),
        area_px=side * side,
        bbox=(x, y, side, side),
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
    pixmap = QtGui.QPixmap(200, 200)
    pixmap.fill(QtCore.Qt.GlobalColor.white)
    canvas._scene.clear()  # noqa: SLF001 — test-only setup
    canvas._pixmap_item = canvas._scene.addPixmap(pixmap)  # noqa: SLF001
    canvas._pixmap_item.setZValue(-100)  # noqa: SLF001
    canvas._scene.setSceneRect(0, 0, 200, 200)  # noqa: SLF001
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


# ---------------------------------------------------------------- commands


def test_delete_room_command_removes_from_calibration():
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1), _mklabel("b", room_id=2)],
        rooms=[_mkroom(1), _mkroom(2, x=60)],
    )
    cmd = DeleteRoomCommand(cal, 0)

    cmd.redo()

    assert len(cal.rooms) == 1
    assert cal.rooms[0].id == 2


def test_delete_room_clears_linked_label_room_ids():
    cal = _mkcal(
        labels=[
            _mklabel("a", room_id=1),
            _mklabel("b", room_id=1),  # also linked to room 1
            _mklabel("c", room_id=2),
        ],
        rooms=[_mkroom(1), _mkroom(2, x=60)],
    )
    cmd = DeleteRoomCommand(cal, 0)  # delete room 1

    cmd.redo()

    assert cal.labels[0].room_id is None
    assert cal.labels[1].room_id is None
    # Label linked to room 2 is untouched.
    assert cal.labels[2].room_id == 2


def test_delete_room_undo_restores_room_and_label_links():
    cal = _mkcal(
        labels=[
            _mklabel("a", room_id=1),
            _mklabel("b", room_id=1),
            _mklabel("c", room_id=2),
        ],
        rooms=[_mkroom(1), _mkroom(2, x=60)],
    )
    cmd = DeleteRoomCommand(cal, 0)
    cmd.redo()
    cmd.undo()

    assert [r.id for r in cal.rooms] == [1, 2]
    assert cal.labels[0].room_id == 1
    assert cal.labels[1].room_id == 1
    assert cal.labels[2].room_id == 2


def test_delete_room_with_no_linked_labels():
    cal = _mkcal(
        labels=[_mklabel("a", room_id=2)],
        rooms=[_mkroom(1), _mkroom(2, x=60)],
    )
    cmd = DeleteRoomCommand(cal, 0)  # delete room 1, nothing links to it

    cmd.redo()

    assert len(cal.rooms) == 1
    assert cal.labels[0].room_id == 2  # unchanged

    cmd.undo()

    assert len(cal.rooms) == 2


def test_delete_room_raises_on_out_of_range_index():
    cal = _mkcal(labels=[], rooms=[_mkroom(1)])
    with pytest.raises(IndexError):
        DeleteRoomCommand(cal, 5)
    with pytest.raises(IndexError):
        DeleteRoomCommand(cal, -1)


def test_delete_room_emits_structural_change_on_redo_and_undo():
    cal = _mkcal(
        labels=[_mklabel("a", room_id=3), _mklabel("b", room_id=3)],
        rooms=[_mkroom(3)],
    )
    received: list[RoomChange] = []
    cmd = DeleteRoomCommand(cal, 0, on_change=received.append)

    cmd.redo()
    assert received[-1].structural is True
    assert received[-1].room_ids == []
    assert sorted(received[-1].label_indices) == [0, 1]

    cmd.undo()
    assert received[-1].structural is True
    assert received[-1].room_ids == [3]
    assert sorted(received[-1].label_indices) == [0, 1]


def test_delete_room_with_no_linked_labels_emits_empty_label_indices():
    cal = _mkcal(labels=[_mklabel("a", room_id=None)], rooms=[_mkroom(1)])
    received: list[RoomChange] = []
    cmd = DeleteRoomCommand(cal, 0, on_change=received.append)

    cmd.redo()
    assert received[-1].label_indices == []

    cmd.undo()
    assert received[-1].label_indices == []


def test_room_change_label_indices_default_is_empty():
    """``RoomChange`` without an explicit ``label_indices`` defaults to [].

    Backward compatibility check: ``AddRoomCommand`` constructs
    ``RoomChange`` without the new keyword and the controller relies on
    iterating ``.label_indices`` either way.
    """
    rc = RoomChange(room_ids=[1], structural=True)
    assert rc.label_indices == []


# -------------------------------------------------------- controller


def test_controller_delete_selected_room_pushes_command(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1), _mklabel("b", room_id=2)],
        rooms=[_mkroom(1), _mkroom(2, x=60)],
    )
    controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

    # Select the room with id=1.
    room_items = canvas.room_items()
    room_items[1].setSelected(True)

    did_delete = controller.delete_selected_room()

    assert did_delete is True
    assert len(cal.rooms) == 1
    assert cal.rooms[0].id == 2
    # Linked label's room_id was cleared.
    assert cal.labels[0].room_id is None
    # Unrelated label untouched.
    assert cal.labels[1].room_id == 2


def test_controller_delete_selected_room_undo_restores_everything(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1), _mklabel("b", room_id=1)],
        rooms=[_mkroom(1), _mkroom(2, x=60)],
    )
    controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

    canvas.room_items()[1].setSelected(True)
    controller.delete_selected_room()

    controller.undo_stack().undo()
    assert len(cal.rooms) == 2
    assert cal.labels[0].room_id == 1
    assert cal.labels[1].room_id == 1


def test_controller_delete_selected_room_noop_without_selection(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=None)],
        rooms=[_mkroom(1)],
    )
    controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

    did_delete = controller.delete_selected_room()
    assert did_delete is False
    assert len(cal.rooms) == 1


def test_controller_delete_selected_room_noop_when_only_label_selected(
    qtbot, tmp_path
):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1)],
        rooms=[_mkroom(1)],
    )
    controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

    # Only a label is selected — delete_selected_room should not fire.
    canvas.label_items()[0].setSelected(True)

    did_delete = controller.delete_selected_room()
    assert did_delete is False
    assert len(cal.rooms) == 1


def test_controller_delete_room_rebuilds_canvas_room_items(qtbot, tmp_path):
    """End-to-end: deleting a room actually shrinks the canvas's room dict."""
    cal = _mkcal(
        labels=[],
        rooms=[_mkroom(1), _mkroom(2, x=60)],
    )
    controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
    assert len(canvas.room_items()) == 2

    canvas.room_items()[1].setSelected(True)
    controller.delete_selected_room()

    items = canvas.room_items()
    assert set(items.keys()) == {2}


def test_controller_delete_room_restyles_affected_labels(qtbot, tmp_path):
    """Deleting the linked room flips the label's item to orphan styling."""
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1)],
        rooms=[_mkroom(1)],
    )
    controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

    label_item = canvas.label_items()[0]
    assert label_item.status == LabelItem.STATUS_LINKED

    canvas.room_items()[1].setSelected(True)
    controller.delete_selected_room()

    # Style was re-applied because label_indices=[0] was in the RoomChange.
    label_item = canvas.label_items()[0]
    assert label_item.status == LabelItem.STATUS_ORPHAN


def test_controller_delete_room_refreshes_inspector_for_affected_label(
    qtbot, tmp_path
):
    """If the inspector is showing a label whose room just got deleted,
    the inspector re-renders so the room combo reflects the cleared link.
    """
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1)],
        rooms=[_mkroom(1)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)

    # Show the label in the inspector first (controller will pick it up
    # via selection change, but easier to call directly).
    canvas.label_items()[0].setSelected(True)
    # The selection-changed handler will have shown the label.
    assert inspector.current_label_index() == 0

    # Now also select the room and delete it. ``delete_selected_room``
    # works on whatever room is selected regardless of what the
    # inspector is showing.
    canvas.scene().clearSelection()
    canvas.room_items()[1].setSelected(True)
    # Re-show the label in the inspector (clearSelection above blew it
    # away) so we can verify the post-delete refresh path.
    canvas.scene().clearSelection()
    canvas.label_items()[0].setSelected(True)
    canvas.room_items()[1].setSelected(True)
    assert inspector.current_label_index() == 0

    controller.delete_selected_room()

    # Inspector should still show label 0, with room_id now None.
    assert inspector.current_label_index() == 0
    assert cal.labels[0].room_id is None
