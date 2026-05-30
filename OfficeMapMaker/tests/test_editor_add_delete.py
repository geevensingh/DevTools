"""Tests for the add-label / delete-label / orphan-room workflows (ed4).

Covers three feature surfaces in the calibration editor:

* :class:`~officemapmaker.editor.commands.AddLabelCommand` and
  :class:`~officemapmaker.editor.commands.DeleteLabelCommand` model
  mutations, including undo / redo round-trips and the ``structural=True``
  change-notification contract.
* :class:`~officemapmaker.editor.canvas.MapCanvas` interaction modes:
  ``set_add_label_mode``, mutual exclusion with pick-room mode, click
  handling that emits ``add_label_requested`` and disarms the mode,
  Esc cancellation, ``room_at_scene_pos`` hit-testing, ``rebuild_labels``,
  and ``select_label``.
* :class:`~officemapmaker.editor.controller.EditorController` glue:
  the canvas click → QInputDialog → ``AddLabelCommand`` flow, the
  inspector "Create label for this room" button flow, and
  ``delete_selected_label``.

Uses ``pytest-qt``'s ``qtbot`` fixture for widget lifecycle (matches
:mod:`tests.test_editor_room_picker`). ``QInputDialog`` is monkeypatched
in controller tests so we don't pop a modal dialog under headless Qt.
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
from officemapmaker.editor.commands import (  # noqa: E402
    AddLabelCommand,
    DeleteLabelCommand,
    LabelChange,
)
from officemapmaker.editor.controller import EditorController  # noqa: E402
from officemapmaker.editor.items import LabelItem  # noqa: E402
from officemapmaker.editor.sidebar import InspectorPanel  # noqa: E402
from officemapmaker.geometry import mask_to_rle  # noqa: E402


# ---------------------------------------------------------------- helpers


def _square_rle(*, x: int, y: int, side: int, canvas_size: int = 200) -> str:
    """Build an RLE mask with a solid square at ``(x, y)`` of size ``side``.

    The mask spans the full ``canvas_size × canvas_size`` because the
    real calibration pipeline encodes each room's polygon in full-image
    coordinates (not room-local). The ``RoomItem`` is placed at scene
    origin and the polygon's own vertices carry the position.
    """
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
    # Set a pixmap so the scene has a non-empty rect; needed for
    # ``ensureVisible`` and the room polygons to draw on top of something.
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


def test_add_label_command_appends_to_calibration():
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=1)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    new = _mklabel("1410", room_id=2)

    cmd = AddLabelCommand(cal, new)
    cmd.redo()

    assert len(cal.labels) == 2
    assert cal.labels[-1] is new
    assert cmd._new_index == 1


def test_add_label_undo_removes_label():
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=1)],
        rooms=[_mkroom(1)],
    )
    new = _mklabel("1410", room_id=None)

    cmd = AddLabelCommand(cal, new)
    cmd.redo()
    assert len(cal.labels) == 2

    cmd.undo()
    assert len(cal.labels) == 1
    assert cal.labels[0].id == "1480"


def test_add_label_redo_reinserts_at_same_index():
    """Redo after undo must restore at the same index so the index stays stable."""
    cal = _mkcal(
        labels=[_mklabel("a", room_id=None), _mklabel("b", room_id=None)],
        rooms=[],
    )
    new = _mklabel("c", room_id=None)
    cmd = AddLabelCommand(cal, new)

    cmd.redo()
    original_index = cmd._new_index
    cmd.undo()
    cmd.redo()

    assert cmd._new_index == original_index
    assert cal.labels[original_index].id == "c"


def test_add_label_emits_structural_change():
    cal = _mkcal(
        labels=[],
        rooms=[_mkroom(7)],
    )
    new = _mklabel("700", room_id=7)
    received: list[LabelChange] = []
    cmd = AddLabelCommand(cal, new, on_change=received.append)

    cmd.redo()

    assert len(received) == 1
    change = received[0]
    assert change.structural is True
    assert change.label_indices == [0]
    assert change.room_ids == [7]


def test_add_label_undo_emits_structural_change_with_no_selection():
    cal = _mkcal(labels=[], rooms=[_mkroom(7)])
    new = _mklabel("700", room_id=7)
    received: list[LabelChange] = []
    cmd = AddLabelCommand(cal, new, on_change=received.append)
    cmd.redo()
    received.clear()

    cmd.undo()

    assert len(received) == 1
    assert received[0].structural is True
    assert received[0].label_indices == []  # nothing to re-select on undo of add
    assert received[0].room_ids == [7]


def test_add_label_with_no_room_id_emits_empty_room_list():
    cal = _mkcal(labels=[], rooms=[])
    new = _mklabel("orphan", room_id=None)
    received: list[LabelChange] = []
    cmd = AddLabelCommand(cal, new, on_change=received.append)

    cmd.redo()

    assert received[0].room_ids == []


def test_delete_label_command_removes_from_calibration():
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1), _mklabel("b", room_id=2)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    cmd = DeleteLabelCommand(cal, 0)

    cmd.redo()

    assert len(cal.labels) == 1
    assert cal.labels[0].id == "b"


def test_delete_label_undo_restores_at_original_index():
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1), _mklabel("b", room_id=2), _mklabel("c", room_id=None)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    cmd = DeleteLabelCommand(cal, 1)  # delete "b"
    cmd.redo()
    assert [lab.id for lab in cal.labels] == ["a", "c"]

    cmd.undo()
    assert [lab.id for lab in cal.labels] == ["a", "b", "c"]


def test_delete_label_raises_on_out_of_range_index():
    cal = _mkcal(labels=[_mklabel("a", room_id=None)], rooms=[])
    with pytest.raises(IndexError):
        DeleteLabelCommand(cal, 5)
    with pytest.raises(IndexError):
        DeleteLabelCommand(cal, -1)


def test_delete_label_emits_structural_change_on_redo_and_undo():
    cal = _mkcal(
        labels=[_mklabel("a", room_id=3)],
        rooms=[_mkroom(3)],
    )
    received: list[LabelChange] = []
    cmd = DeleteLabelCommand(cal, 0, on_change=received.append)

    cmd.redo()
    assert received[-1].structural is True
    assert received[-1].label_indices == []
    assert received[-1].room_ids == [3]

    cmd.undo()
    assert received[-1].structural is True
    assert received[-1].label_indices == [0]
    assert received[-1].room_ids == [3]


# -------------------------------------------------------- canvas plumbing


def test_canvas_set_add_label_mode_is_idempotent(qtbot):
    cal = _mkcal(labels=[], rooms=[_mkroom(1)])
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_calibration(cal)

    assert canvas.add_label_mode() is False
    canvas.set_add_label_mode(True)
    assert canvas.add_label_mode() is True
    canvas.set_add_label_mode(True)  # no-op
    assert canvas.add_label_mode() is True
    canvas.set_add_label_mode(False)
    assert canvas.add_label_mode() is False


def test_canvas_add_label_mode_mutually_exclusive_with_pick_room(qtbot):
    cal = _mkcal(labels=[], rooms=[_mkroom(1)])
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_calibration(cal)

    canvas.set_room_pick_mode(True)
    canvas.set_add_label_mode(True)
    assert canvas.room_pick_mode() is False
    assert canvas.add_label_mode() is True

    canvas.set_room_pick_mode(True)
    assert canvas.add_label_mode() is False
    assert canvas.room_pick_mode() is True


def test_canvas_escape_cancels_add_label_mode(qtbot):
    cal = _mkcal(labels=[], rooms=[])
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_calibration(cal)
    canvas.set_add_label_mode(True)

    cancelled: list[None] = []
    canvas.add_label_cancelled.connect(lambda: cancelled.append(None))

    QtWidgets.QApplication.processEvents()
    event = QtGui.QKeyEvent(
        QtCore.QEvent.Type.KeyPress,
        QtCore.Qt.Key.Key_Escape,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.keyPressEvent(event)

    assert cancelled == [None]
    assert canvas.add_label_mode() is False


def test_canvas_room_at_scene_pos_hits_inside_room(qtbot):
    cal = _mkcal(
        labels=[],
        rooms=[_mkroom(42, x=10, y=10, side=40)],
    )
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_calibration(cal)

    hit = canvas.room_at_scene_pos(QtCore.QPointF(30, 30))
    assert hit is not None
    assert hit.room.id == 42


def test_canvas_room_at_scene_pos_misses_outside_room(qtbot):
    cal = _mkcal(
        labels=[],
        rooms=[_mkroom(42, x=10, y=10, side=40)],
    )
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_calibration(cal)

    # Far outside the room polygon.
    hit = canvas.room_at_scene_pos(QtCore.QPointF(500, 500))
    assert hit is None


def test_canvas_rebuild_labels_recreates_label_items(qtbot):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=None)],
        rooms=[],
    )
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_calibration(cal)
    assert len(canvas.label_items()) == 1

    # Mutate model directly and rebuild — simulates an Add/Delete redo.
    cal.labels.append(_mklabel("b", room_id=None))
    canvas.rebuild_labels()

    items = canvas.label_items()
    assert len(items) == 2
    assert set(items.keys()) == {0, 1}


def test_canvas_select_label_selects_the_right_item(qtbot):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=None), _mklabel("b", room_id=None)],
        rooms=[],
    )
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    canvas.set_calibration(cal)

    canvas.select_label(1)

    items = canvas.label_items()
    assert items[1].isSelected() is True
    assert items[0].isSelected() is False


# ------------------------------------------------------ controller wiring


def test_controller_add_label_request_creates_label(qtbot, tmp_path, monkeypatch):
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=1)],
        rooms=[_mkroom(1, x=0, y=0, side=40), _mkroom(2, x=60, y=60, side=40)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)

    monkeypatch.setattr(
        QtWidgets.QInputDialog,
        "getText",
        staticmethod(lambda *args, **kwargs: ("1410", True)),
    )

    canvas.set_add_label_mode(True)
    canvas.add_label_requested.emit(QtCore.QPointF(20, 20))

    assert len(cal.labels) == 2
    added = cal.labels[-1]
    assert added.id == "1410"
    # Click was inside room 1, so the new label should be auto-linked.
    assert added.room_id == 1
    assert added.fill_seed == (20, 20)
    assert added.ocr_confidence == 1.0


def test_controller_add_label_request_outside_room_creates_orphan(
    qtbot, tmp_path, monkeypatch
):
    cal = _mkcal(
        labels=[],
        rooms=[_mkroom(1, x=0, y=0, side=10)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)

    monkeypatch.setattr(
        QtWidgets.QInputDialog,
        "getText",
        staticmethod(lambda *args, **kwargs: ("orphan", True)),
    )

    canvas.add_label_requested.emit(QtCore.QPointF(150, 150))

    assert len(cal.labels) == 1
    assert cal.labels[0].room_id is None


def test_controller_add_label_request_cancelled_does_nothing(
    qtbot, tmp_path, monkeypatch
):
    cal = _mkcal(labels=[], rooms=[_mkroom(1)])
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)

    monkeypatch.setattr(
        QtWidgets.QInputDialog,
        "getText",
        staticmethod(lambda *args, **kwargs: ("", False)),
    )
    canvas.add_label_requested.emit(QtCore.QPointF(5, 5))

    assert len(cal.labels) == 0


def test_controller_add_label_request_with_empty_id_does_nothing(
    qtbot, tmp_path, monkeypatch
):
    cal = _mkcal(labels=[], rooms=[_mkroom(1)])
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)

    monkeypatch.setattr(
        QtWidgets.QInputDialog,
        "getText",
        staticmethod(lambda *args, **kwargs: ("   ", True)),
    )
    canvas.add_label_requested.emit(QtCore.QPointF(5, 5))

    assert len(cal.labels) == 0


def test_controller_create_label_for_room_centers_on_room(
    qtbot, tmp_path, monkeypatch
):
    cal = _mkcal(
        labels=[],
        rooms=[_mkroom(99, x=20, y=30, side=40)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)

    monkeypatch.setattr(
        QtWidgets.QInputDialog,
        "getText",
        staticmethod(lambda *args, **kwargs: ("9900", True)),
    )
    inspector.create_label_for_room.emit(99)

    assert len(cal.labels) == 1
    added = cal.labels[0]
    assert added.id == "9900"
    assert added.room_id == 99
    # Center of bbox (20,30,40,40) is (40, 50).
    assert added.fill_seed == (40, 50)


def test_controller_delete_selected_label_pushes_command(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1), _mklabel("b", room_id=2)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)

    items = canvas.label_items()
    items[0].setSelected(True)

    did_delete = controller.delete_selected_label()

    assert did_delete is True
    assert len(cal.labels) == 1
    assert cal.labels[0].id == "b"

    # Undoable.
    controller.undo_stack().undo()
    assert len(cal.labels) == 2
    assert cal.labels[0].id == "a"


def test_controller_delete_selected_label_noop_without_selection(qtbot, tmp_path):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=None)],
        rooms=[],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
    # No selection.

    did_delete = controller.delete_selected_label()
    assert did_delete is False
    assert len(cal.labels) == 1


def test_structural_change_rebuilds_canvas_label_items(qtbot, tmp_path):
    """End-to-end: an AddLabelCommand through the controller actually
    grows the canvas's ``_label_items`` dict and selects the new label.
    """
    cal = _mkcal(
        labels=[_mklabel("a", room_id=None)],
        rooms=[_mkroom(1, x=0, y=0, side=40)],
    )
    controller, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
    assert len(canvas.label_items()) == 1

    new = _mklabel("b", room_id=1)
    cmd = AddLabelCommand(
        cal, new, on_change=controller._on_label_change  # noqa: SLF001 — test-only
    )
    controller.undo_stack().push(cmd)

    items = canvas.label_items()
    assert len(items) == 2
    # The new label should be selected (controller's structural-change handler).
    assert items[1].isSelected() is True


def test_inspector_create_label_button_visible_only_when_room_has_no_labels(qtbot):
    cal = _mkcal(
        labels=[_mklabel("a", room_id=1)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    inspector = InspectorPanel()
    qtbot.addWidget(inspector)

    # Room 1 has a label → button hidden.
    inspector.show_room(room=cal.rooms[0], labels=[cal.labels[0]])
    assert inspector._room_widgets["create_button"].isVisible() is False  # noqa: SLF001

    # Room 2 has no labels → button visible.
    # The widget itself is not on-screen during tests, so we check the
    # internal show/hide call rather than ``isVisible`` (which requires a
    # visible parent). ``visibleRegion``-style checks aren't appropriate
    # here; the public contract is "show or hide based on labels list".
    inspector.show_room(room=cal.rooms[1], labels=[])
    # Even off-screen, ``isVisibleTo(parent)`` is true if setVisible(True)
    # was the last call.
    button = inspector._room_widgets["create_button"]  # noqa: SLF001
    assert button.isVisibleTo(inspector) is True
