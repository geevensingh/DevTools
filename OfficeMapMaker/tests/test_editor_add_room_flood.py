"""Tests for the add-room (flood-fill) workflow — ed7a (Phase 1 of 3).

Covers three feature surfaces, mirroring the layering of
:mod:`tests.test_editor_add_delete`:

* :class:`~officemapmaker.editor.commands.AddRoomCommand` model mutation
  + the :class:`~officemapmaker.editor.commands.RoomChange` change-
  notification contract (structural=True; carries the new id on redo
  and an empty list on undo).
* :class:`~officemapmaker.editor.canvas.MapCanvas` add-room-flood mode:
  the new signals (``add_room_flood_requested`` /
  ``add_room_flood_cancelled``), mutual exclusion with pick-room and
  add-label, click handling, Esc cancellation, ``rebuild_rooms`` and
  ``select_room``.
* :class:`~officemapmaker.editor.controller.EditorController` glue:
  the canvas-click → flood-fill → ``AddRoomCommand`` flow, the
  no-map-path guard, the seed-on-wall / out-of-bounds / area-cap
  rejections, and the new ``_next_room_id`` helper.

``QMessageBox`` calls are monkeypatched to silently record their
invocations so we can assert on rejection paths without a modal dialog
under headless Qt.
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
    AddRoomCommand,
    RoomChange,
)
from officemapmaker.editor.controller import EditorController  # noqa: E402
from officemapmaker.editor.items import RoomItem  # noqa: E402
from officemapmaker.editor.sidebar import InspectorPanel  # noqa: E402
from officemapmaker.geometry import mask_to_rle, rle_to_mask  # noqa: E402


# ---------------------------------------------------------------- helpers


_CANVAS_SIZE = 200


def _square_rle(*, x: int, y: int, side: int, canvas_size: int = _CANVAS_SIZE) -> str:
    """Same idiom as test_editor_add_delete: full-image-coord RLE mask."""
    mask = np.zeros((canvas_size, canvas_size), dtype=np.uint8)
    mask[y : y + side, x : x + side] = 1
    return mask_to_rle(mask)


def _mklabel(id_: str, *, room_id: int | None) -> Label:
    return Label(
        id=id_,
        bbox=(0, 0, 10, 10),
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


def _build_controller(
    cal: Calibration, tmp_path, qtbot, *, map_path=None
) -> tuple[EditorController, MapCanvas, InspectorPanel]:
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    pixmap = QtGui.QPixmap(_CANVAS_SIZE, _CANVAS_SIZE)
    pixmap.fill(QtCore.Qt.GlobalColor.white)
    canvas._scene.clear()  # noqa: SLF001 — test-only setup
    canvas._pixmap_item = canvas._scene.addPixmap(pixmap)  # noqa: SLF001
    canvas._pixmap_item.setZValue(-100)  # noqa: SLF001
    canvas._scene.setSceneRect(0, 0, _CANVAS_SIZE, _CANVAS_SIZE)  # noqa: SLF001
    canvas.set_calibration(cal)
    inspector = InspectorPanel()
    qtbot.addWidget(inspector)
    controller = EditorController(
        calibration=cal,
        calibration_path=tmp_path / "calibration.json",
        canvas=canvas,
        inspector=inspector,
        map_path=map_path,
    )
    return controller, canvas, inspector


# ============================================================ commands


class TestRoomChange:
    def test_defaults(self):
        change = RoomChange()
        assert change.room_ids == []
        assert change.structural is False

    def test_populated(self):
        change = RoomChange(room_ids=[7, 8], structural=True)
        assert change.room_ids == [7, 8]
        assert change.structural is True

    def test_room_ids_is_copied(self):
        # Same defensive copy the LabelChange path uses — mutating the
        # caller's list should never alter the notification.
        src = [1, 2]
        change = RoomChange(room_ids=src, structural=True)
        src.append(3)
        assert change.room_ids == [1, 2]


class TestAddRoomCommand:
    def test_redo_appends_and_fires_callback(self):
        cal = _mkcal([], [_mkroom(1)])
        new_room = _mkroom(2, x=60, y=60)
        seen: list[RoomChange] = []
        cmd = AddRoomCommand(cal, new_room, on_change=seen.append)

        cmd.redo()

        assert len(cal.rooms) == 2
        assert cal.rooms[-1] is new_room
        assert len(seen) == 1
        assert seen[0].structural is True
        assert seen[0].room_ids == [2]

    def test_undo_removes_and_fires_callback(self):
        cal = _mkcal([], [_mkroom(1)])
        new_room = _mkroom(2, x=60, y=60)
        seen: list[RoomChange] = []
        cmd = AddRoomCommand(cal, new_room, on_change=seen.append)

        cmd.redo()
        seen.clear()
        cmd.undo()

        assert len(cal.rooms) == 1
        assert cal.rooms[0].id == 1
        assert len(seen) == 1
        assert seen[0].structural is True
        # Undo carries an empty list so the controller can drop selection
        # on the (now-gone) room.
        assert seen[0].room_ids == []

    def test_redo_undo_redo_cycle_is_idempotent(self):
        cal = _mkcal([], [_mkroom(1)])
        new_room = _mkroom(2, x=60, y=60)
        cmd = AddRoomCommand(cal, new_room)

        cmd.redo()
        cmd.undo()
        cmd.redo()

        assert len(cal.rooms) == 2
        assert cal.rooms[-1] is new_room

    def test_undo_without_redo_is_safe(self):
        cal = _mkcal([], [_mkroom(1)])
        new_room = _mkroom(2)
        cmd = AddRoomCommand(cal, new_room)

        # Pure-undo before redo shouldn't blow up. Mirrors the defensive
        # _new_index None-guard in AddLabelCommand.undo.
        cmd.undo()

        assert len(cal.rooms) == 1

    def test_works_without_callback(self):
        # on_change is optional — the command must still mutate the model.
        cal = _mkcal([], [])
        new_room = _mkroom(5)
        cmd = AddRoomCommand(cal, new_room)
        cmd.redo()
        assert cal.rooms == [new_room]


# ============================================================ canvas


class TestCanvasAddRoomFloodMode:
    def test_signals_exist(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        # PySide6 ``Signal`` objects don't have a useful runtime type check;
        # the simplest assert is that ``connect`` works without raising.
        canvas.add_room_flood_requested.connect(lambda _pt: None)
        canvas.add_room_flood_cancelled.connect(lambda: None)

    def test_toggle_state(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.add_room_flood_mode() is False
        canvas.set_add_room_flood_mode(True)
        assert canvas.add_room_flood_mode() is True
        canvas.set_add_room_flood_mode(False)
        assert canvas.add_room_flood_mode() is False

    def test_toggle_is_idempotent(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_flood_mode(True)
        canvas.set_add_room_flood_mode(True)
        assert canvas.add_room_flood_mode() is True

    def test_mutual_exclusion_with_add_label(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_label_mode(True)
        canvas.set_add_room_flood_mode(True)
        assert canvas.add_label_mode() is False
        assert canvas.add_room_flood_mode() is True

    def test_mutual_exclusion_with_room_pick(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_room_pick_mode(True)
        canvas.set_add_room_flood_mode(True)
        assert canvas.room_pick_mode() is False
        assert canvas.add_room_flood_mode() is True

    def test_add_label_disarms_add_room(self, qtbot):
        # Verify the other direction too: turning add-label on must clear
        # add-room-flood (no two modes hot at once).
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_flood_mode(True)
        canvas.set_add_label_mode(True)
        assert canvas.add_room_flood_mode() is False
        assert canvas.add_label_mode() is True

    def test_pick_room_disarms_add_room(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_flood_mode(True)
        canvas.set_room_pick_mode(True)
        assert canvas.add_room_flood_mode() is False
        assert canvas.room_pick_mode() is True


class TestCanvasRebuildAndSelect:
    def test_rebuild_rooms_picks_up_new_room(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        pixmap = QtGui.QPixmap(_CANVAS_SIZE, _CANVAS_SIZE)
        pixmap.fill(QtCore.Qt.GlobalColor.white)
        canvas._pixmap_item = canvas._scene.addPixmap(pixmap)  # noqa: SLF001
        canvas._scene.setSceneRect(0, 0, _CANVAS_SIZE, _CANVAS_SIZE)  # noqa: SLF001
        canvas.set_calibration(cal)
        assert set(canvas.room_items().keys()) == {1}

        # Mutate the model the way AddRoomCommand.redo would, then rebuild.
        cal.rooms.append(_mkroom(2, x=70, y=70))
        canvas.rebuild_rooms()

        assert set(canvas.room_items().keys()) == {1, 2}

    def test_rebuild_rooms_preserves_label_items(self, qtbot):
        cal = _mkcal([_mklabel("100", room_id=1)], [_mkroom(1)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        pixmap = QtGui.QPixmap(_CANVAS_SIZE, _CANVAS_SIZE)
        pixmap.fill(QtCore.Qt.GlobalColor.white)
        canvas._pixmap_item = canvas._scene.addPixmap(pixmap)  # noqa: SLF001
        canvas._scene.setSceneRect(0, 0, _CANVAS_SIZE, _CANVAS_SIZE)  # noqa: SLF001
        canvas.set_calibration(cal)
        labels_before = set(canvas.label_items().keys())

        cal.rooms.append(_mkroom(2, x=70, y=70))
        canvas.rebuild_rooms()

        assert set(canvas.label_items().keys()) == labels_before

    def test_select_room(self, qtbot):
        cal = _mkcal([], [_mkroom(1), _mkroom(2, x=70, y=70)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        pixmap = QtGui.QPixmap(_CANVAS_SIZE, _CANVAS_SIZE)
        pixmap.fill(QtCore.Qt.GlobalColor.white)
        canvas._pixmap_item = canvas._scene.addPixmap(pixmap)  # noqa: SLF001
        canvas._scene.setSceneRect(0, 0, _CANVAS_SIZE, _CANVAS_SIZE)  # noqa: SLF001
        canvas.set_calibration(cal)

        canvas.select_room(2)

        selected = [
            it for it in canvas.scene().selectedItems() if isinstance(it, RoomItem)
        ]
        assert len(selected) == 1
        assert selected[0].room.id == 2

    def test_select_room_missing_id_is_noop(self, qtbot):
        cal = _mkcal([], [_mkroom(1)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        pixmap = QtGui.QPixmap(_CANVAS_SIZE, _CANVAS_SIZE)
        pixmap.fill(QtCore.Qt.GlobalColor.white)
        canvas._pixmap_item = canvas._scene.addPixmap(pixmap)  # noqa: SLF001
        canvas._scene.setSceneRect(0, 0, _CANVAS_SIZE, _CANVAS_SIZE)  # noqa: SLF001
        canvas.set_calibration(cal)
        canvas.select_room(999)  # no-op, no crash
        assert canvas.scene().selectedItems() == []


class TestCanvasEscape:
    def test_esc_cancels_add_room_flood(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_flood_mode(True)

        signals: list[bool] = []
        canvas.add_room_flood_cancelled.connect(lambda: signals.append(True))

        event = QtGui.QKeyEvent(
            QtCore.QEvent.Type.KeyPress,
            QtCore.Qt.Key.Key_Escape,
            QtCore.Qt.KeyboardModifier.NoModifier,
        )
        canvas.keyPressEvent(event)

        assert canvas.add_room_flood_mode() is False
        assert signals == [True]


# ============================================================ controller


class _MessageBoxRecorder:
    """Drop-in for ``QMessageBox.warning`` / ``.information``.

    Lets a test assert that the controller surfaced an error to the user
    without actually popping a modal dialog under headless Qt.
    """

    def __init__(self) -> None:
        self.calls: list[tuple[str, str]] = []

    def __call__(self, _parent, title: str, text: str, *_args, **_kwargs):
        self.calls.append((title, text))
        return QtWidgets.QMessageBox.StandardButton.Ok


class TestControllerNextRoomId:
    def test_empty(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        assert controller._next_room_id() == 1  # noqa: SLF001

    def test_max_plus_one(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1), _mkroom(7, x=70, y=70), _mkroom(3, x=120, y=120)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        assert controller._next_room_id() == 8  # noqa: SLF001

    def test_after_add_increments_again(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(5)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        assert controller._next_room_id() == 6  # noqa: SLF001
        cal.rooms.append(_mkroom(6, x=70, y=70))
        assert controller._next_room_id() == 7  # noqa: SLF001


class TestControllerArmGuard:
    def test_no_map_path_arm_warns_and_does_not_arm(
        self, qtbot, tmp_path, monkeypatch
    ):
        cal = _mkcal([], [])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=None
        )
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller.set_add_room_flood_mode(True)

        assert canvas.add_room_flood_mode() is False
        assert len(recorder.calls) == 1
        assert "Add room" in recorder.calls[0][0]

    def test_with_map_path_arms(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        # File doesn't have to exist for the *arm* path — only for the
        # actual flood-fill click. So a path that points nowhere is fine
        # here and lets us verify the guard's pure path-presence check.
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )

        controller.set_add_room_flood_mode(True)

        assert canvas.add_room_flood_mode() is True


class TestControllerFloodFill:
    """Controller's flood-fill click handler — full happy path + rejections.

    The flood-fill itself is exercised against a hand-crafted ``wall_mask``
    (injected via ``controller._wall_mask_cache`` to skip the cv2 +
    adaptive-threshold step). That keeps the test deterministic and fast
    without compromising the assertion: every other code path in
    ``_handle_canvas_add_room_flood`` runs unchanged.
    """

    def _inject_wall_mask(self, controller: EditorController, mask: np.ndarray) -> None:
        # Direct cache write: the controller checks `_wall_mask_cache is
        # not None` first, so this short-circuits the cv2 + map-load path.
        controller._wall_mask_cache = mask  # noqa: SLF001

    def _make_wall_mask_with_box(
        self, *, box=(40, 40, 100, 100)
    ) -> np.ndarray:
        """Make a wall mask: 1px-thick box border around (x0,y0)-(x1,y1)."""
        mask = np.zeros((_CANVAS_SIZE, _CANVAS_SIZE), dtype=np.uint8)
        x0, y0, x1, y1 = box
        mask[y0, x0:x1 + 1] = 255
        mask[y1, x0:x1 + 1] = 255
        mask[y0:y1 + 1, x0] = 255
        mask[y0:y1 + 1, x1] = 255
        return mask

    def test_happy_path_appends_room(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        self._inject_wall_mask(controller, self._make_wall_mask_with_box())

        # Click inside the box.
        controller._handle_canvas_add_room_flood(  # noqa: SLF001
            QtCore.QPointF(70, 70)
        )

        assert len(cal.rooms) == 2
        new_room = cal.rooms[-1]
        # _next_room_id was max(1) + 1.
        assert new_room.id == 2
        # Filled area = (100 - 40 - 1) ** 2 = 59 ** 2 = 3481.
        assert new_room.area_px == 59 * 59
        # Bbox covers (41, 41) inclusive through (99, 99) inclusive.
        assert new_room.bbox == (41, 41, 59, 59)
        # The mask round-trips correctly.
        decoded = rle_to_mask(new_room.polygon_rle)
        assert decoded.shape == (_CANVAS_SIZE, _CANVAS_SIZE)
        assert int((decoded > 0).sum()) == 59 * 59
        # A new RoomItem was created in the canvas.
        assert 2 in canvas.room_items()

    def test_happy_path_pushes_undoable_command(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        self._inject_wall_mask(controller, self._make_wall_mask_with_box())

        controller._handle_canvas_add_room_flood(  # noqa: SLF001
            QtCore.QPointF(70, 70)
        )

        # Undo should remove the room; redo should bring it back.
        assert controller.undo_stack().canUndo()
        controller.undo_stack().undo()
        assert len(cal.rooms) == 1
        controller.undo_stack().redo()
        assert len(cal.rooms) == 2

    def test_click_on_wall_rejects(self, qtbot, tmp_path, monkeypatch):
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        self._inject_wall_mask(controller, self._make_wall_mask_with_box())
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        # Click on the top border of the box (a wall pixel).
        controller._handle_canvas_add_room_flood(  # noqa: SLF001
            QtCore.QPointF(50, 40)
        )

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
        # Message should mention "wall" so the user knows what went wrong.
        assert "wall" in recorder.calls[0][1].lower()

    def test_click_outside_image_rejects(self, qtbot, tmp_path, monkeypatch):
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        self._inject_wall_mask(controller, self._make_wall_mask_with_box())
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller._handle_canvas_add_room_flood(  # noqa: SLF001
            QtCore.QPointF(-5, 50)
        )

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
        assert "outside" in recorder.calls[0][1].lower()

    def test_click_outside_image_max_x_rejects(self, qtbot, tmp_path, monkeypatch):
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        self._inject_wall_mask(controller, self._make_wall_mask_with_box())
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller._handle_canvas_add_room_flood(  # noqa: SLF001
            QtCore.QPointF(_CANVAS_SIZE + 5, 50)
        )

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1

    def test_leak_too_big_rejects(self, qtbot, tmp_path, monkeypatch):
        # Wall mask is all-zeros except for a tiny island wall — a flood
        # from any interior point will fill almost the entire canvas,
        # well above the 30% cap.
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        empty_mask = np.zeros((_CANVAS_SIZE, _CANVAS_SIZE), dtype=np.uint8)
        # Put a tiny 4×4 dark patch so the flood seed itself isn't on a
        # wall but the rest of the canvas is wide open — flood will cover
        # virtually everything.
        empty_mask[0:4, 0:4] = 255
        self._inject_wall_mask(controller, empty_mask)
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller._handle_canvas_add_room_flood(  # noqa: SLF001
            QtCore.QPointF(100, 100)
        )

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
        # Message should call out the % and mention wall_patches.
        body = recorder.calls[0][1]
        assert "%" in body
        assert "wall_patches" in body

    def test_new_room_is_selected(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        self._inject_wall_mask(controller, self._make_wall_mask_with_box())

        controller._handle_canvas_add_room_flood(  # noqa: SLF001
            QtCore.QPointF(70, 70)
        )

        selected = [
            it
            for it in canvas.scene().selectedItems()
            if isinstance(it, RoomItem)
        ]
        assert len(selected) == 1
        assert selected[0].room.id == 2

    def test_undo_drops_selection(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        self._inject_wall_mask(controller, self._make_wall_mask_with_box())

        controller._handle_canvas_add_room_flood(  # noqa: SLF001
            QtCore.QPointF(70, 70)
        )
        controller.undo_stack().undo()

        # No RoomItem with id=2 should remain in the scene.
        assert 2 not in canvas.room_items()


class TestControllerWallMaskCache:
    def test_invalidate_drops_cache(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "map.png"
        )
        controller._wall_mask_cache = np.zeros((4, 4), dtype=np.uint8)  # noqa: SLF001
        controller.invalidate_wall_mask()
        assert controller._wall_mask_cache is None  # noqa: SLF001

    def test_get_wall_mask_returns_none_when_no_map_path(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=None
        )
        assert controller._get_wall_mask() is None  # noqa: SLF001

    def test_get_wall_mask_returns_none_when_file_missing(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, map_path=tmp_path / "does-not-exist.png"
        )
        assert controller._get_wall_mask() is None  # noqa: SLF001
