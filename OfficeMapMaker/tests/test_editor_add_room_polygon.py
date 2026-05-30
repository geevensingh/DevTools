"""Tests for the add-room (polygon) workflow — ed7c (Phase 3 of 3).

The polygon mode reuses ``AddRoomCommand`` + ``RoomChange`` + the
canvas's ``image_size()`` helper from earlier phases, so this file
focuses on the surfaces that are new in ed7c:

* :class:`~officemapmaker.editor.canvas.MapCanvas` add-room-polygon
  mode: the new signals (``add_room_polygon_requested`` /
  ``add_room_polygon_cancelled``), the 5-way mutual exclusion now
  that polygon joins room-pick / add-label / add-room-flood / rect,
  the click-to-place vertex flow, the live preview polygon that
  includes the cursor as a provisional last vertex, the close
  paths (Enter, double-click, right-click), Backspace to drop the
  most recent vertex, and Esc cancellation.

* :class:`~officemapmaker.editor.controller.EditorController` glue:
  the no-image-loaded guard on ``set_add_room_polygon_mode``, the
  polygon → rasterized mask → ``AddRoomCommand`` flow, bbox derived
  from the rasterized mask (not the raw vertices), clamping at the
  image edges, and rejection of degenerate / too-small polygons.

``QMessageBox.warning`` is monkeypatched to silently record calls
so rejection-path assertions don't pop a modal dialog under
headless Qt.
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
        # Polygon mode doesn't need map_path — only image dimensions,
        # which come from the canvas's loaded pixmap.
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


# --------------- synthetic event helpers (scene → viewport rounding) ---


def _press(
    canvas: MapCanvas,
    scene_x: float,
    scene_y: float,
    *,
    button: QtCore.Qt.MouseButton = QtCore.Qt.MouseButton.LeftButton,
) -> None:
    viewport_pt = canvas.mapFromScene(QtCore.QPointF(scene_x, scene_y))
    event = QtGui.QMouseEvent(
        QtCore.QEvent.Type.MouseButtonPress,
        QtCore.QPointF(viewport_pt),
        QtCore.QPointF(viewport_pt),
        button,
        button,
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
        QtCore.Qt.MouseButton.NoButton,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.mouseMoveEvent(event)


def _double_click(canvas: MapCanvas, scene_x: float, scene_y: float) -> None:
    viewport_pt = canvas.mapFromScene(QtCore.QPointF(scene_x, scene_y))
    event = QtGui.QMouseEvent(
        QtCore.QEvent.Type.MouseButtonDblClick,
        QtCore.QPointF(viewport_pt),
        QtCore.QPointF(viewport_pt),
        QtCore.Qt.MouseButton.LeftButton,
        QtCore.Qt.MouseButton.LeftButton,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.mouseDoubleClickEvent(event)


def _key(
    canvas: MapCanvas, key: QtCore.Qt.Key
) -> None:
    event = QtGui.QKeyEvent(
        QtCore.QEvent.Type.KeyPress,
        key,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.keyPressEvent(event)


# ============================================================ canvas


class TestCanvasAddRoomPolygonMode:
    def test_signals_exist(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.add_room_polygon_requested.connect(lambda _p: None)
        canvas.add_room_polygon_cancelled.connect(lambda: None)

    def test_toggle_state(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.add_room_polygon_mode() is False
        canvas.set_add_room_polygon_mode(True)
        assert canvas.add_room_polygon_mode() is True
        canvas.set_add_room_polygon_mode(False)
        assert canvas.add_room_polygon_mode() is False

    def test_toggle_is_idempotent(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_polygon_mode(True)
        canvas.set_add_room_polygon_mode(True)
        assert canvas.add_room_polygon_mode() is True

    def test_polygon_vertex_count_starts_at_zero(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.polygon_vertex_count() == 0
        canvas.set_add_room_polygon_mode(True)
        assert canvas.polygon_vertex_count() == 0

    # --- 5-way mutual exclusion (new mode + each existing mode) -----

    def test_mutex_with_add_label_armed_first(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_label_mode(True)
        canvas.set_add_room_polygon_mode(True)
        assert canvas.add_label_mode() is False
        assert canvas.add_room_polygon_mode() is True

    def test_mutex_with_room_pick_armed_first(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_room_pick_mode(True)
        canvas.set_add_room_polygon_mode(True)
        assert canvas.room_pick_mode() is False
        assert canvas.add_room_polygon_mode() is True

    def test_mutex_with_add_room_flood_armed_first(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_flood_mode(True)
        canvas.set_add_room_polygon_mode(True)
        assert canvas.add_room_flood_mode() is False
        assert canvas.add_room_polygon_mode() is True

    def test_mutex_with_add_room_rect_armed_first(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_rect_mode(True)
        canvas.set_add_room_polygon_mode(True)
        assert canvas.add_room_rect_mode() is False
        assert canvas.add_room_polygon_mode() is True

    def test_add_label_disarms_polygon(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_polygon_mode(True)
        canvas.set_add_label_mode(True)
        assert canvas.add_room_polygon_mode() is False
        assert canvas.add_label_mode() is True

    def test_room_pick_disarms_polygon(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_polygon_mode(True)
        canvas.set_room_pick_mode(True)
        assert canvas.add_room_polygon_mode() is False
        assert canvas.room_pick_mode() is True

    def test_add_room_flood_disarms_polygon(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_polygon_mode(True)
        canvas.set_add_room_flood_mode(True)
        assert canvas.add_room_polygon_mode() is False
        assert canvas.add_room_flood_mode() is True

    def test_add_room_rect_disarms_polygon(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_polygon_mode(True)
        canvas.set_add_room_rect_mode(True)
        assert canvas.add_room_polygon_mode() is False
        assert canvas.add_room_rect_mode() is True


class TestCanvasPolygonClickFlow:
    def test_clicks_append_vertices(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _press(canvas, 80, 120)

        assert canvas.polygon_vertex_count() == 3

    def test_preview_item_exists_after_two_vertices(self, qtbot):
        # 2 vertices alone is not a polygon (degenerate line) but together
        # with the cursor we already render the preview after the first
        # mouseMove; either way, by the time two vertices are placed and
        # the cursor moves, a preview QGraphicsPolygonItem should exist.
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _move(canvas, 80, 120)

        assert canvas._polygon_preview_item is not None  # noqa: SLF001
        assert canvas._polygon_preview_item.scene() is canvas.scene()  # noqa: SLF001
        # Polygon should have placed-vertices + cursor = 3 points.
        polygon = canvas._polygon_preview_item.polygon()  # noqa: SLF001
        assert polygon.size() == 3

    def test_mouse_move_updates_preview_with_cursor(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        _press(canvas, 30, 30)
        _move(canvas, 100, 100)
        first_poly = QtGui.QPolygonF(
            canvas._polygon_preview_item.polygon()  # noqa: SLF001
        )
        _move(canvas, 150, 150)
        second_poly = canvas._polygon_preview_item.polygon()  # noqa: SLF001

        # Provisional last vertex tracks the cursor.
        assert second_poly.size() == 2
        assert second_poly.at(1).x() == pytest.approx(150, abs=1.0)
        assert second_poly.at(1).y() == pytest.approx(150, abs=1.0)
        assert first_poly.at(1) != second_poly.at(1)

    def test_no_preview_with_zero_vertices(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        # Move the cursor without ever clicking — there's nothing to
        # rubber-band from, so no preview should appear.
        _move(canvas, 100, 100)

        assert canvas._polygon_preview_item is None  # noqa: SLF001


class TestCanvasPolygonClose:
    def test_double_click_closes_polygon(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        polygons: list[QtGui.QPolygonF] = []
        canvas.add_room_polygon_requested.connect(polygons.append)

        # Three left-presses to place three vertices, then a double-click
        # to close. ``_double_click`` only fires the doubleClick event
        # (not a fresh press), so the closing position uses whatever was
        # already placed.
        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _press(canvas, 80, 120)
        _double_click(canvas, 80, 120)

        assert len(polygons) == 1
        polygon = polygons[0]
        assert polygon.size() == 3
        assert canvas.add_room_polygon_mode() is False
        # Preview torn down after close.
        assert canvas._polygon_preview_item is None  # noqa: SLF001

    def test_enter_closes_polygon(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        polygons: list[QtGui.QPolygonF] = []
        canvas.add_room_polygon_requested.connect(polygons.append)

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _press(canvas, 80, 120)
        _press(canvas, 40, 110)
        _key(canvas, QtCore.Qt.Key.Key_Return)

        assert len(polygons) == 1
        assert polygons[0].size() == 4
        assert canvas.add_room_polygon_mode() is False

    def test_right_click_closes_polygon(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        polygons: list[QtGui.QPolygonF] = []
        canvas.add_room_polygon_requested.connect(polygons.append)

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _press(canvas, 80, 120)
        _press(canvas, 50, 50, button=QtCore.Qt.MouseButton.RightButton)

        assert len(polygons) == 1
        # Right-click is a close gesture, NOT a vertex add: should still
        # be 3 vertices, not 4.
        assert polygons[0].size() == 3
        assert canvas.add_room_polygon_mode() is False

    def test_close_with_one_vertex_cancels(self, qtbot):
        # Closing too early should fire the cancelled signal, not
        # requested.
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        polygons: list[QtGui.QPolygonF] = []
        cancels: list[bool] = []
        canvas.add_room_polygon_requested.connect(polygons.append)
        canvas.add_room_polygon_cancelled.connect(lambda: cancels.append(True))

        _press(canvas, 30, 30)
        _key(canvas, QtCore.Qt.Key.Key_Return)

        assert polygons == []
        assert cancels == [True]
        assert canvas.add_room_polygon_mode() is False

    def test_close_with_two_vertices_cancels(self, qtbot):
        # Two vertices is below the 3-vertex floor.
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        polygons: list[QtGui.QPolygonF] = []
        cancels: list[bool] = []
        canvas.add_room_polygon_requested.connect(polygons.append)
        canvas.add_room_polygon_cancelled.connect(lambda: cancels.append(True))

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _key(canvas, QtCore.Qt.Key.Key_Return)

        assert polygons == []
        assert cancels == [True]

    def test_esc_cancels_and_clears_vertices(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        cancels: list[bool] = []
        canvas.add_room_polygon_cancelled.connect(lambda: cancels.append(True))

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _press(canvas, 80, 120)
        _key(canvas, QtCore.Qt.Key.Key_Escape)

        assert cancels == [True]
        assert canvas.add_room_polygon_mode() is False
        assert canvas.polygon_vertex_count() == 0
        assert canvas._polygon_preview_item is None  # noqa: SLF001


class TestCanvasPolygonBackspace:
    def test_backspace_removes_last_vertex(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _press(canvas, 80, 120)
        assert canvas.polygon_vertex_count() == 3

        _key(canvas, QtCore.Qt.Key.Key_Backspace)
        assert canvas.polygon_vertex_count() == 2

        _key(canvas, QtCore.Qt.Key.Key_Backspace)
        assert canvas.polygon_vertex_count() == 1

    def test_backspace_with_no_vertices_is_a_noop(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        # Doesn't crash, doesn't toggle anything off.
        _key(canvas, QtCore.Qt.Key.Key_Backspace)
        assert canvas.add_room_polygon_mode() is True
        assert canvas.polygon_vertex_count() == 0

    def test_backspace_with_two_vertices_then_one_keeps_preview_in_sync(
        self, qtbot
    ):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _move(canvas, 200, 200)
        # Pre-backspace: vertices = 2, preview includes cursor = 3 pts.
        assert canvas._polygon_preview_item.polygon().size() == 3  # noqa: SLF001

        _key(canvas, QtCore.Qt.Key.Key_Backspace)
        # 1 vertex left, no cursor override on refresh — preview empty.
        polygon = canvas._polygon_preview_item.polygon()  # noqa: SLF001
        assert polygon.size() == 0


class TestCanvasPolygonDisarmCleanup:
    def test_disarm_mid_draft_clears_state(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_room_polygon_mode(True)

        _press(canvas, 30, 30)
        _press(canvas, 100, 40)
        _move(canvas, 200, 200)
        assert canvas._polygon_preview_item is not None  # noqa: SLF001

        # Programmatic disarm (menu toggle, mutual exclusion, etc.).
        canvas.set_add_room_polygon_mode(False)

        assert canvas._polygon_preview_item is None  # noqa: SLF001
        assert canvas.polygon_vertex_count() == 0
        # No QGraphicsPolygonItem should be left in the scene.
        leftover = [
            it
            for it in canvas.scene().items()
            if isinstance(it, QtWidgets.QGraphicsPolygonItem)
        ]
        assert leftover == []


# ============================================================ controller


class TestControllerPolygonArmGuard:
    def test_no_pixmap_arm_warns_and_does_not_arm(
        self, qtbot, tmp_path, monkeypatch
    ):
        cal = _mkcal([], [])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, attach_pixmap=False
        )
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller.set_add_room_polygon_mode(True)

        assert canvas.add_room_polygon_mode() is False
        assert len(recorder.calls) == 1
        assert "Add room" in recorder.calls[0][0]

    def test_with_pixmap_arms(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, attach_pixmap=True
        )

        controller.set_add_room_polygon_mode(True)

        assert canvas.add_room_polygon_mode() is True


class TestControllerPolygonHandler:
    """End-to-end controller path for an emitted ``add_room_polygon_requested``."""

    def _triangle(self, x0=50, y0=50, side=60) -> QtGui.QPolygonF:
        """Right-triangle polygon entirely inside the canvas bounds."""
        return QtGui.QPolygonF(
            [
                QtCore.QPointF(x0, y0),
                QtCore.QPointF(x0 + side, y0),
                QtCore.QPointF(x0, y0 + side),
            ]
        )

    def test_happy_path_appends_room(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        polygon = self._triangle()
        controller._handle_canvas_add_room_polygon(polygon)  # noqa: SLF001

        assert len(cal.rooms) == 2
        new_room = cal.rooms[-1]
        assert new_room.id == 2
        # Triangle (50,50)-(110,50)-(50,110) — bbox is the enclosing rect.
        # Width / height = 61 px because bbox spans inclusive coords.
        assert new_room.bbox == (50, 50, 61, 61)
        # Triangle area = ~0.5 * 60 * 60 = ~1800; rasterized count is
        # close (PIL polygon fill is inclusive of vertex pixels).
        assert 1700 <= new_room.area_px <= 2000
        # Mask round-trips.
        decoded = rle_to_mask(new_room.polygon_rle)
        assert decoded.shape == (_CANVAS_SIZE, _CANVAS_SIZE)
        # The RoomItem reached the canvas.
        assert 2 in canvas.room_items()

    def test_happy_path_pushes_undoable_command(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        controller._handle_canvas_add_room_polygon(self._triangle())  # noqa: SLF001

        assert controller.undo_stack().canUndo()
        controller.undo_stack().undo()
        assert len(cal.rooms) == 1
        controller.undo_stack().redo()
        assert len(cal.rooms) == 2

    def test_new_room_is_selected(self, qtbot, tmp_path):
        cal = _mkcal([], [_mkroom(1)])
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        controller._handle_canvas_add_room_polygon(self._triangle())  # noqa: SLF001

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

        controller._handle_canvas_add_room_polygon(self._triangle())  # noqa: SLF001
        controller.undo_stack().undo()

        assert 2 not in canvas.room_items()

    def test_polygon_partially_outside_clamps(self, qtbot, tmp_path):
        # Two vertices off the top-left edge; one inside. Clamping pulls
        # the off-image vertices to (0, 0), then PIL rasterizes the
        # resulting triangle — bbox should sit at the image origin.
        cal = _mkcal([], [])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)

        polygon = QtGui.QPolygonF(
            [
                QtCore.QPointF(-50, -50),
                QtCore.QPointF(80, -50),
                QtCore.QPointF(80, 60),
            ]
        )
        controller._handle_canvas_add_room_polygon(polygon)  # noqa: SLF001

        assert len(cal.rooms) == 1
        room = cal.rooms[0]
        # Bbox should start at the image origin (clamped) and stop at
        # the inside vertex (80, 60).
        assert room.bbox[0] == 0
        assert room.bbox[1] == 0
        assert room.bbox[2] <= 81
        assert room.bbox[3] <= 61

    def test_too_few_vertices_after_emit_rejects(
        self, qtbot, tmp_path, monkeypatch
    ):
        # The canvas usually filters this at the gesture level, but the
        # controller should defensively reject too in case a programmatic
        # caller bypasses the canvas.
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        polygon = QtGui.QPolygonF(
            [QtCore.QPointF(50, 50), QtCore.QPointF(80, 50)]
        )
        controller._handle_canvas_add_room_polygon(polygon)  # noqa: SLF001

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
        assert "vertices" in recorder.calls[0][1].lower()

    def test_degenerate_collinear_polygon_rejects(
        self, qtbot, tmp_path, monkeypatch
    ):
        # Three collinear vertices rasterize to zero pixels under PIL's
        # even-odd fill rule. Should reject with the "too small" message.
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        polygon = QtGui.QPolygonF(
            [
                QtCore.QPointF(50, 50),
                QtCore.QPointF(70, 50),
                QtCore.QPointF(90, 50),
            ]
        )
        controller._handle_canvas_add_room_polygon(polygon)  # noqa: SLF001

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
        assert "small" in recorder.calls[0][1].lower()

    def test_tiny_triangle_rejects(self, qtbot, tmp_path, monkeypatch):
        # A 5x5 right triangle is ~12.5 px area, below the 50-px floor.
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        polygon = QtGui.QPolygonF(
            [
                QtCore.QPointF(80, 80),
                QtCore.QPointF(85, 80),
                QtCore.QPointF(80, 85),
            ]
        )
        controller._handle_canvas_add_room_polygon(polygon)  # noqa: SLF001

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
        assert "small" in recorder.calls[0][1].lower()

    def test_polygon_with_no_pixmap_rejects(
        self, qtbot, tmp_path, monkeypatch
    ):
        # Same defensive check as rect: handler should reject if the
        # pixmap was unloaded mid-arm (future "open new map" feature).
        cal = _mkcal([], [_mkroom(1)])
        controller, _canvas, _inspector = _build_controller(
            cal, tmp_path, qtbot, attach_pixmap=False
        )
        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        polygon = QtGui.QPolygonF(
            [
                QtCore.QPointF(50, 50),
                QtCore.QPointF(110, 50),
                QtCore.QPointF(50, 110),
            ]
        )
        controller._handle_canvas_add_room_polygon(polygon)  # noqa: SLF001

        assert len(cal.rooms) == 1
        assert len(recorder.calls) == 1
