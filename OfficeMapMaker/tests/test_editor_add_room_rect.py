"""Tests for the add-room (rectangle) workflow — ed7b (Phase 2 of 3).

The rectangle mode reuses ``AddRoomCommand`` + ``RoomChange`` (already
exercised by :mod:`tests.test_editor_add_room_flood`) so this file
focuses on the surfaces that are new in ed7b:

* :class:`~officemapmaker.editor.canvas.MapCanvas` add-room-rect mode:
  the new signals (``add_room_rect_requested`` /
  ``add_room_rect_cancelled``), 4-way mutual exclusion across the new
  mode and the three existing arm-modes (room-pick / add-label /
  add-room-flood), the press → move → release drag with a live preview
  rectangle, Esc cancellation (including mid-drag), and the
  ``image_size`` helper.

* :class:`~officemapmaker.editor.controller.EditorController` glue:
  the no-image-loaded guard on ``set_add_room_rect_mode``, the click →
  Room build → ``AddRoomCommand`` flow, the entirely-out-of-bounds
  rejection, the too-small rejection, and bbox clamping when the drag
  partially extends past the image edge.

``QMessageBox.warning`` is monkeypatched to silently record calls so
rejection-path assertions don't pop a modal dialog under headless Qt.
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
from officemapmaker.editor.items import RoomItem  # noqa: E402
from officemapmaker.editor.sidebar import InspectorPanel  # noqa: E402
from officemapmaker.geometry import mask_to_rle, rle_to_mask  # noqa: E402


# ---------------------------------------------------------------- helpers


_CANVAS_SIZE = 200


def _square_rle(*, x: int, y: int, side: int, canvas_size: int = _CANVAS_SIZE) -> str:
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


def _attach_pixmap(canvas: MapCanvas, *, size: int = _CANVAS_SIZE) -> None:
    """Give the canvas a white pixmap so ``image_size`` returns a value."""
    pixmap = QtGui.QPixmap(size, size)
    pixmap.fill(QtCore.Qt.GlobalColor.white)
    canvas._scene.clear()  # noqa: SLF001 — test-only setup
    canvas._pixmap_item = canvas._scene.addPixmap(pixmap)  # noqa: SLF001
    canvas._pixmap_item.setZValue(-100)  # noqa: SLF001
    canvas._scene.setSceneRect(0, 0, size, size)  # noqa: SLF001


def _build_controller(
    cal: Calibration, tmp_path, qtbot, *, attach_pixmap: bool = True
) -> tuple[EditorController, MapCanvas, InspectorPanel]:
    canvas = MapCanvas()
    qtbot.addWidget(canvas)
    if attach_pixmap:
        _attach_pixmap(canvas)
    canvas.set_calibration(cal)
    inspector = InspectorPanel()
    qtbot.addWidget(inspector)
    controller = EditorController(
        calibration=cal,
        calibration_path=tmp_path / "calibration.json",
        canvas=canvas,
        inspector=inspector,
        # Rect mode doesn't need map_path — only image dimensions, which
        # come from the canvas's loaded pixmap.
        map_path=None,
    )
    return controller, canvas, inspector


class _MessageBoxRecorder:
    """Drop-in for ``QMessageBox.warning`` that silently records calls."""

    def __init__(self) -> None:
        self.calls: list[tuple[str, str]] = []

    def __call__(self, _parent, title: str, text: str, *_args, **_kwargs):
        self.calls.append((title, text))
        return QtWidgets.QMessageBox.StandardButton.Ok


# ============================================================ canvas


class TestCanvasImageSize:
    def test_returns_none_without_pixmap(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.image_size() is None

    def test_returns_pixmap_dims(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas, size=150)
        assert canvas.image_size() == (150, 150)


class TestCanvasAddRoomRectMode:
    def test_signals_exist(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.add_room_rect_requested.connect(lambda _r: None)
        canvas.add_room_rect_cancelled.connect(lambda: None)

    def test_toggle_state(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.add_room_rect_mode() is False
        canvas.set_add_room_rect_mode(True)
        assert canvas.add_room_rect_mode() is True
        canvas.set_add_room_rect_mode(False)
        assert canvas.add_room_rect_mode() is False

    def test_toggle_is_idempotent(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_rect_mode(True)
        canvas.set_add_room_rect_mode(True)
        assert canvas.add_room_rect_mode() is True

    def test_mutual_exclusion_with_add_label(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_label_mode(True)
        canvas.set_add_room_rect_mode(True)
        assert canvas.add_label_mode() is False
        assert canvas.add_room_rect_mode() is True

    def test_mutual_exclusion_with_room_pick(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_room_pick_mode(True)
        canvas.set_add_room_rect_mode(True)
        assert canvas.room_pick_mode() is False
        assert canvas.add_room_rect_mode() is True

    def test_mutual_exclusion_with_add_room_flood(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_flood_mode(True)
        canvas.set_add_room_rect_mode(True)
        assert canvas.add_room_flood_mode() is False
        assert canvas.add_room_rect_mode() is True

    def test_add_label_disarms_rect(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_rect_mode(True)
        canvas.set_add_label_mode(True)
        assert canvas.add_room_rect_mode() is False
        assert canvas.add_label_mode() is True

    def test_pick_room_disarms_rect(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_rect_mode(True)
        canvas.set_room_pick_mode(True)
        assert canvas.add_room_rect_mode() is False
        assert canvas.room_pick_mode() is True

    def test_add_room_flood_disarms_rect(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_rect_mode(True)
        canvas.set_add_room_flood_mode(True)
        assert canvas.add_room_rect_mode() is False
        assert canvas.add_room_flood_mode() is True


# ----------------- drag flow (synthetic QMouseEvent calls) ---------


def _press(canvas: MapCanvas, scene_x: float, scene_y: float) -> None:
    """Synthesize a left-button press at the given *scene* coords."""
    viewport_pt = canvas.mapFromScene(QtCore.QPointF(scene_x, scene_y))
    event = QtGui.QMouseEvent(
        QtCore.QEvent.Type.MouseButtonPress,
        QtCore.QPointF(viewport_pt),
        QtCore.QPointF(viewport_pt),
        QtCore.Qt.MouseButton.LeftButton,
        QtCore.Qt.MouseButton.LeftButton,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.mousePressEvent(event)


def _move(canvas: MapCanvas, scene_x: float, scene_y: float) -> None:
    viewport_pt = canvas.mapFromScene(QtCore.QPointF(scene_x, scene_y))
    event = QtGui.QMouseEvent(
        QtCore.QEvent.Type.MouseMove,
        QtCore.QPointF(viewport_pt),
        QtCore.QPointF(viewport_pt),
        QtCore.Qt.MouseButton.NoButton,
        QtCore.Qt.MouseButton.LeftButton,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.mouseMoveEvent(event)


def _release(canvas: MapCanvas, scene_x: float, scene_y: float) -> None:
    viewport_pt = canvas.mapFromScene(QtCore.QPointF(scene_x, scene_y))
    event = QtGui.QMouseEvent(
        QtCore.QEvent.Type.MouseButtonRelease,
        QtCore.QPointF(viewport_pt),
        QtCore.QPointF(viewport_pt),
        QtCore.Qt.MouseButton.LeftButton,
        QtCore.Qt.MouseButton.NoButton,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.mouseReleaseEvent(event)


class TestCanvasRectDrag:
    def test_press_creates_preview(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_rect_mode(True)

        _press(canvas, 50, 60)

        assert canvas._rect_preview_item is not None  # noqa: SLF001
        assert canvas._rect_drag_start_scene is not None  # noqa: SLF001
        # Preview lives in the scene so the user actually sees it.
        assert canvas._rect_preview_item.scene() is canvas.scene()  # noqa: SLF001

    def test_move_updates_preview(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_rect_mode(True)

        _press(canvas, 50, 60)
        _move(canvas, 120, 140)

        preview = canvas._rect_preview_item  # noqa: SLF001
        rect = preview.rect()
        assert rect.left() == pytest.approx(50, abs=1.0)
        assert rect.top() == pytest.approx(60, abs=1.0)
        assert rect.right() == pytest.approx(120, abs=1.0)
        assert rect.bottom() == pytest.approx(140, abs=1.0)

    def test_release_emits_rect_and_clears_preview(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_rect_mode(True)

        rects: list[QtCore.QRectF] = []
        canvas.add_room_rect_requested.connect(rects.append)

        _press(canvas, 50, 60)
        _move(canvas, 120, 140)
        _release(canvas, 120, 140)

        assert len(rects) == 1
        rect = rects[0]
        assert rect.width() == pytest.approx(70, abs=1.0)
        assert rect.height() == pytest.approx(80, abs=1.0)
        # Mode disarmed; preview gone.
        assert canvas.add_room_rect_mode() is False
        assert canvas._rect_preview_item is None  # noqa: SLF001
        assert canvas._rect_drag_start_scene is None  # noqa: SLF001

    def test_release_normalizes_reverse_drag(self, qtbot):
        # Dragging bottom-right → top-left should still emit a positive-
        # width / positive-height rectangle. (QRectF.normalized handles it
        # but assert explicitly so a future regression is caught.)
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_rect_mode(True)

        rects: list[QtCore.QRectF] = []
        canvas.add_room_rect_requested.connect(rects.append)

        _press(canvas, 150, 150)
        _move(canvas, 50, 50)
        _release(canvas, 50, 50)

        assert len(rects) == 1
        rect = rects[0]
        assert rect.width() > 0
        assert rect.height() > 0
        # The normalized rect should be (50, 50, 100, 100) modulo
        # viewport rounding.
        assert rect.left() == pytest.approx(50, abs=1.0)
        assert rect.top() == pytest.approx(50, abs=1.0)

    def test_release_with_no_drag_cancels(self, qtbot):
        # Press and release at the same point — no drag. Should emit
        # ``add_room_rect_cancelled``, NOT ``add_room_rect_requested``.
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_rect_mode(True)

        rects: list[QtCore.QRectF] = []
        cancels: list[bool] = []
        canvas.add_room_rect_requested.connect(rects.append)
        canvas.add_room_rect_cancelled.connect(lambda: cancels.append(True))

        _press(canvas, 80, 80)
        _release(canvas, 80, 80)

        assert rects == []
        assert cancels == [True]
        assert canvas.add_room_rect_mode() is False

    def test_esc_mid_drag_clears_preview(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_rect_mode(True)

        _press(canvas, 50, 60)
        _move(canvas, 120, 140)
        assert canvas._rect_preview_item is not None  # noqa: SLF001

        event = QtGui.QKeyEvent(
            QtCore.QEvent.Type.KeyPress,
            QtCore.Qt.Key.Key_Escape,
            QtCore.Qt.KeyboardModifier.NoModifier,
        )
        canvas.keyPressEvent(event)

        assert canvas.add_room_rect_mode() is False
        assert canvas._rect_preview_item is None  # noqa: SLF001

    def test_esc_with_no_drag_in_progress_still_cancels_mode(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_rect_mode(True)

        cancels: list[bool] = []
        canvas.add_room_rect_cancelled.connect(lambda: cancels.append(True))

        event = QtGui.QKeyEvent(
            QtCore.QEvent.Type.KeyPress,
            QtCore.Qt.Key.Key_Escape,
            QtCore.Qt.KeyboardModifier.NoModifier,
        )
        canvas.keyPressEvent(event)

        assert canvas.add_room_rect_mode() is False
        assert cancels == [True]

    def test_disarm_mid_drag_tears_down_preview(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_rect_mode(True)

        _press(canvas, 50, 60)
        _move(canvas, 120, 140)

        # Programmatic disarm (e.g. user toggled the menu off) should
        # leave no preview in the scene.
        canvas.set_add_room_rect_mode(False)

        assert canvas._rect_preview_item is None  # noqa: SLF001
        # No QGraphicsRectItem with the dashed pen should remain.
        from PySide6.QtWidgets import QGraphicsRectItem
        leftover = [
            it for it in canvas.scene().items() if isinstance(it, QGraphicsRectItem)
        ]
        assert leftover == []


# ============================================================ controller


class TestControllerRectArmGuard:
    def test_no_pixmap_arm_warns_and_does_not_arm(
        self, qtbot, tmp_path, monkeypatch
    ):
        cal = _mkcal([], [])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, attach_pixmap=False
        )
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller.set_add_room_rect_mode(True)

        assert canvas.add_room_rect_mode() is False
        assert len(recorder.calls) == 1
        assert "Add room" in recorder.calls[0][0]

    def test_with_pixmap_arms(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, attach_pixmap=True
        )

        controller.set_add_room_rect_mode(True)

        assert canvas.add_room_rect_mode() is True


class TestControllerRectHandler:
    """End-to-end controller path for an emitted ``add_room_rect_requested``."""

    def test_happy_path_appends_room(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(50, 60, 70, 80)
        )

        assert len(cal.rooms) == 2
        new_room = cal.rooms[-1]
        # _next_room_id was max(1) + 1.
        assert new_room.id == 2
        assert new_room.bbox == (50, 60, 70, 80)
        assert new_room.area_px == 70 * 80
        # Mask round-trips and matches the rectangle.
        decoded = rle_to_mask(new_room.polygon_rle)
        assert decoded.shape == (_CANVAS_SIZE, _CANVAS_SIZE)
        assert int((decoded > 0).sum()) == 70 * 80
        # And the new RoomItem reached the canvas.
        assert 2 in canvas.room_items()

    def test_happy_path_pushes_undoable_command(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(50, 60, 70, 80)
        )

        assert controller.undo_stack().canUndo()
        controller.undo_stack().undo()
        assert len(cal.rooms) == 1
        controller.undo_stack().redo()
        assert len(cal.rooms) == 2

    def test_new_room_is_selected(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(50, 60, 70, 80)
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
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(50, 60, 70, 80)
        )
        controller.undo_stack().undo()

        assert 2 not in canvas.room_items()

    def test_rect_partially_outside_image_clamps(self, qtbot, tmp_path):
        # User dragged into the image from above-left — the negative
        # coords should be clamped to zero, not propagated into the
        # Room.bbox or mask offset.
        cal = _mkcal([], [])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(-30, -40, 100, 90)
        )

        assert len(cal.rooms) == 1
        room = cal.rooms[0]
        # Clamped: x=max(0, -30)=0, y=max(0, -40)=0,
        # right=min(W, -30+100)=70, bottom=min(H, -40+90)=50.
        assert room.bbox == (0, 0, 70, 50)
        assert room.area_px == 70 * 50

    def test_rect_extending_past_right_bottom_clamps(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        # Drag extends past the right and bottom edges.
        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(180, 170, 50, 50)
        )

        assert len(cal.rooms) == 1
        room = cal.rooms[0]
        # x_max = min(200, 180+50)=200; w=200-180=20.
        # y_max = min(200, 170+50)=200; h=200-170=30.
        assert room.bbox == (180, 170, 20, 30)
        assert room.area_px == 20 * 30

    def test_rect_entirely_outside_image_rejects(
        self, qtbot, tmp_path, monkeypatch
    ):
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(-100, -100, 50, 50)
        )

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
        assert "outside" in recorder.calls[0][1].lower()

    def test_rect_too_small_rejects(self, qtbot, tmp_path, monkeypatch):
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        # 5x5 = 25 px, below the 50-px floor.
        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(80, 80, 5, 5)
        )

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
        assert "small" in recorder.calls[0][1].lower()

    def test_rect_with_no_pixmap_rejects(
        self, qtbot, tmp_path, monkeypatch
    ):
        # The arm-guard normally prevents this state, but the handler
        # should defensively reject too in case the pixmap was unloaded
        # mid-arm (e.g. a future "open different map" feature).
        cal = _mkcal([], [_mkroom(1)])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, attach_pixmap=False
        )
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller._handle_canvas_add_room_rect(  # noqa: SLF001
            QtCore.QRectF(50, 60, 70, 80)
        )

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
