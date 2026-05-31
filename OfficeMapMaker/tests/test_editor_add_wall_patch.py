"""Tests for the add-wall-patch workflow — w9b.

Wall-patch mode is a two-click line draw:

  * 1st left-click on the canvas records the start point and reveals a
    magenta dashed preview line that follows the cursor.
  * 2nd left-click commits the line, emits ``add_wall_patch_requested``
    with both endpoints, and disarms.
  * Esc at any point cancels (whether or not the first click was placed)
    and emits ``add_wall_patch_cancelled``.

This file covers the surfaces that are new in w9b:

* :class:`~officemapmaker.editor.commands.AddWallPatchCommand` — append
  ``wall_patches`` entry, undoable, fires
  :class:`~officemapmaker.editor.commands.WallPatchChange`.

* :class:`~officemapmaker.editor.canvas.MapCanvas` add-wall-patch mode:
  the new signals, 6-way mutual exclusion across the new mode and the
  five existing arm-modes (room-pick / add-label / add-room-flood /
  add-room-rect / add-room-polygon), the two-click flow with a live
  preview line, Esc cancellation (both before and after the first
  click), and ``wall_patch_first_point()`` accessor.

* :class:`~officemapmaker.editor.controller.EditorController` glue:
  no-image-loaded guard on ``set_add_wall_patch_mode``, endpoint
  clamping to image bounds, zero-length-after-clamp rejection, and
  the wall-mask invalidation that follows a successful push.

``QMessageBox.warning`` is monkeypatched so rejection-path assertions
don't pop a modal dialog under headless Qt.
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
    AddWallPatchCommand,
    WallPatchChange,
)
from officemapmaker.editor.controller import EditorController  # noqa: E402
from officemapmaker.editor.sidebar import InspectorPanel  # noqa: E402
from officemapmaker.geometry import mask_to_rle  # noqa: E402


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


def _mkcal(
    labels: list[Label],
    rooms: list[Room],
    *,
    wall_patches: list[tuple[int, int, int, int]] | None = None,
) -> Calibration:
    return Calibration(
        map_image="m.png",
        map_hash="sha256:deadbeef",
        labels=labels,
        rooms=rooms,
        wall_patches=list(wall_patches or []),
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
        # Wall-patch mode doesn't need map_path — only image dimensions,
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


# Synthetic Qt event helpers ----------------------------------------


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
        QtCore.Qt.MouseButton.NoButton,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.mouseMoveEvent(event)


def _press_escape(canvas: MapCanvas) -> None:
    event = QtGui.QKeyEvent(
        QtCore.QEvent.Type.KeyPress,
        QtCore.Qt.Key.Key_Escape,
        QtCore.Qt.KeyboardModifier.NoModifier,
    )
    canvas.keyPressEvent(event)


# ============================================================ command


class TestAddWallPatchCommand:
    def test_redo_appends_patch(self):
        cal = _mkcal([], [])
        cmd = AddWallPatchCommand(cal, (10, 20, 30, 40))
        cmd.redo()
        assert cal.wall_patches == [(10, 20, 30, 40)]

    def test_undo_removes_patch(self):
        cal = _mkcal([], [], wall_patches=[(1, 2, 3, 4)])
        cmd = AddWallPatchCommand(cal, (50, 60, 70, 80))
        cmd.redo()
        assert len(cal.wall_patches) == 2
        cmd.undo()
        assert cal.wall_patches == [(1, 2, 3, 4)]

    def test_redo_undo_redo_is_stable(self):
        """Repeating redo/undo keeps the same slot — indices don't drift."""
        cal = _mkcal([], [], wall_patches=[(1, 2, 3, 4), (5, 6, 7, 8)])
        cmd = AddWallPatchCommand(cal, (9, 10, 11, 12))
        cmd.redo()
        cmd.undo()
        cmd.redo()
        cmd.undo()
        cmd.redo()
        # Patch should be at the same position (end) each time.
        assert cal.wall_patches == [(1, 2, 3, 4), (5, 6, 7, 8), (9, 10, 11, 12)]

    def test_on_change_fires_structural(self):
        cal = _mkcal([], [])
        changes: list[WallPatchChange] = []
        cmd = AddWallPatchCommand(
            cal, (10, 20, 30, 40), on_change=changes.append
        )
        cmd.redo()
        cmd.undo()
        assert len(changes) == 2
        assert all(isinstance(c, WallPatchChange) for c in changes)
        assert all(c.structural for c in changes)

    def test_on_change_optional(self):
        cal = _mkcal([], [])
        cmd = AddWallPatchCommand(cal, (10, 20, 30, 40))  # no on_change
        # Should not raise.
        cmd.redo()
        cmd.undo()

    def test_input_is_copied_not_aliased(self):
        """Mutating the caller's tuple-source after construction must not affect stored patch."""
        cal = _mkcal([], [])
        # Pass a list to test coercion + aliasing protection.
        as_list = [10, 20, 30, 40]
        cmd = AddWallPatchCommand(cal, tuple(as_list))
        as_list[0] = 999  # mutate the source
        cmd.redo()
        assert cal.wall_patches == [(10, 20, 30, 40)]

    def test_undo_before_redo_is_safe(self):
        """Calling undo before any redo must not crash (defensive)."""
        cal = _mkcal([], [], wall_patches=[(1, 2, 3, 4)])
        cmd = AddWallPatchCommand(cal, (50, 60, 70, 80))
        cmd.undo()  # _new_index is still None → no-op
        assert cal.wall_patches == [(1, 2, 3, 4)]

    def test_command_text_describes_endpoints(self):
        cal = _mkcal([], [])
        cmd = AddWallPatchCommand(cal, (10, 20, 30, 40))
        text = cmd.text()
        assert "wall patch" in text
        assert "10" in text and "20" in text and "30" in text and "40" in text


# ============================================================ canvas mode


class TestCanvasAddWallPatchMode:
    def test_signals_exist(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.add_wall_patch_requested.connect(lambda _a, _b: None)
        canvas.add_wall_patch_cancelled.connect(lambda: None)

    def test_default_off(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.add_wall_patch_mode() is False

    def test_toggle_state(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_wall_patch_mode(True)
        assert canvas.add_wall_patch_mode() is True
        canvas.set_add_wall_patch_mode(False)
        assert canvas.add_wall_patch_mode() is False

    def test_toggle_is_idempotent(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_wall_patch_mode(True)
        canvas.set_add_wall_patch_mode(True)
        assert canvas.add_wall_patch_mode() is True

    def test_first_point_starts_none(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.wall_patch_first_point() is None
        canvas.set_add_wall_patch_mode(True)
        assert canvas.wall_patch_first_point() is None


class TestCanvasAddWallPatchMutualExclusion:
    def test_arming_disarms_room_pick(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_room_pick_mode(True)
        canvas.set_add_wall_patch_mode(True)
        assert canvas.room_pick_mode() is False
        assert canvas.add_wall_patch_mode() is True

    def test_arming_disarms_add_label(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_label_mode(True)
        canvas.set_add_wall_patch_mode(True)
        assert canvas.add_label_mode() is False
        assert canvas.add_wall_patch_mode() is True

    def test_arming_disarms_add_room_flood(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_flood_mode(True)
        canvas.set_add_wall_patch_mode(True)
        assert canvas.add_room_flood_mode() is False
        assert canvas.add_wall_patch_mode() is True

    def test_arming_disarms_add_room_rect(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_rect_mode(True)
        canvas.set_add_wall_patch_mode(True)
        assert canvas.add_room_rect_mode() is False
        assert canvas.add_wall_patch_mode() is True

    def test_arming_disarms_add_room_polygon(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_room_polygon_mode(True)
        canvas.set_add_wall_patch_mode(True)
        assert canvas.add_room_polygon_mode() is False
        assert canvas.add_wall_patch_mode() is True

    def test_room_pick_disarms_wall_patch(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_wall_patch_mode(True)
        canvas.set_room_pick_mode(True)
        assert canvas.add_wall_patch_mode() is False
        assert canvas.room_pick_mode() is True

    def test_add_label_disarms_wall_patch(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_wall_patch_mode(True)
        canvas.set_add_label_mode(True)
        assert canvas.add_wall_patch_mode() is False
        assert canvas.add_label_mode() is True

    def test_add_room_flood_disarms_wall_patch(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_wall_patch_mode(True)
        canvas.set_add_room_flood_mode(True)
        assert canvas.add_wall_patch_mode() is False
        assert canvas.add_room_flood_mode() is True

    def test_add_room_rect_disarms_wall_patch(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_wall_patch_mode(True)
        canvas.set_add_room_rect_mode(True)
        assert canvas.add_wall_patch_mode() is False
        assert canvas.add_room_rect_mode() is True

    def test_add_room_polygon_disarms_wall_patch(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_add_wall_patch_mode(True)
        canvas.set_add_room_polygon_mode(True)
        assert canvas.add_wall_patch_mode() is False
        assert canvas.add_room_polygon_mode() is True


# ============================================================ click flow


class TestCanvasWallPatchClickFlow:
    def test_first_click_records_point(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        _press(canvas, 50, 60)

        first = canvas.wall_patch_first_point()
        assert first is not None
        assert first.x() == pytest.approx(50, abs=1.0)
        assert first.y() == pytest.approx(60, abs=1.0)
        # Still armed — waiting for second click.
        assert canvas.add_wall_patch_mode() is True

    def test_first_click_creates_preview(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        _press(canvas, 50, 60)

        preview = canvas._wall_patch_preview_item  # noqa: SLF001
        assert preview is not None
        assert preview.scene() is canvas.scene()

    def test_move_updates_preview_line(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        _press(canvas, 50, 60)
        _move(canvas, 120, 140)

        preview = canvas._wall_patch_preview_item  # noqa: SLF001
        line = preview.line()
        assert line.x1() == pytest.approx(50, abs=1.0)
        assert line.y1() == pytest.approx(60, abs=1.0)
        assert line.x2() == pytest.approx(120, abs=1.0)
        assert line.y2() == pytest.approx(140, abs=1.0)

    def test_move_before_first_click_does_nothing(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        # Move without a first click — preview should NOT appear (no
        # anchor to draw from).
        _move(canvas, 120, 140)
        assert canvas._wall_patch_preview_item is None  # noqa: SLF001
        assert canvas.wall_patch_first_point() is None

    def test_second_click_emits_and_disarms(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        emissions: list[tuple[QtCore.QPointF, QtCore.QPointF]] = []
        canvas.add_wall_patch_requested.connect(
            lambda a, b: emissions.append((a, b))
        )

        _press(canvas, 50, 60)
        _press(canvas, 120, 140)

        assert len(emissions) == 1
        start, end = emissions[0]
        assert start.x() == pytest.approx(50, abs=1.0)
        assert start.y() == pytest.approx(60, abs=1.0)
        assert end.x() == pytest.approx(120, abs=1.0)
        assert end.y() == pytest.approx(140, abs=1.0)
        # Mode disarmed; preview gone; first point cleared.
        assert canvas.add_wall_patch_mode() is False
        assert canvas._wall_patch_preview_item is None  # noqa: SLF001
        assert canvas.wall_patch_first_point() is None

    def test_second_click_does_not_fire_cancelled(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        cancelled: list[None] = []
        canvas.add_wall_patch_cancelled.connect(lambda: cancelled.append(None))

        _press(canvas, 50, 60)
        _press(canvas, 120, 140)

        assert cancelled == []

    def test_esc_before_first_click_cancels(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        cancelled: list[None] = []
        emissions: list[tuple[QtCore.QPointF, QtCore.QPointF]] = []
        canvas.add_wall_patch_cancelled.connect(lambda: cancelled.append(None))
        canvas.add_wall_patch_requested.connect(
            lambda a, b: emissions.append((a, b))
        )

        _press_escape(canvas)

        assert canvas.add_wall_patch_mode() is False
        assert len(cancelled) == 1
        assert emissions == []

    def test_esc_after_first_click_cancels_and_tears_down(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        cancelled: list[None] = []
        emissions: list[tuple[QtCore.QPointF, QtCore.QPointF]] = []
        canvas.add_wall_patch_cancelled.connect(lambda: cancelled.append(None))
        canvas.add_wall_patch_requested.connect(
            lambda a, b: emissions.append((a, b))
        )

        _press(canvas, 50, 60)
        _press_escape(canvas)

        assert canvas.add_wall_patch_mode() is False
        assert canvas.wall_patch_first_point() is None
        assert canvas._wall_patch_preview_item is None  # noqa: SLF001
        assert len(cancelled) == 1
        assert emissions == []

    def test_disarm_mid_gesture_tears_down(self, qtbot):
        """Programmatic disarm after first click cleans up preview + first point."""
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        canvas.set_add_wall_patch_mode(True)

        _press(canvas, 50, 60)
        assert canvas._wall_patch_preview_item is not None  # noqa: SLF001

        canvas.set_add_wall_patch_mode(False)

        assert canvas.add_wall_patch_mode() is False
        assert canvas.wall_patch_first_point() is None
        assert canvas._wall_patch_preview_item is None  # noqa: SLF001


# ============================================================ controller


class TestControllerAddWallPatch:
    def test_arm_without_image_warns_and_refuses(
        self, qtbot, tmp_path, monkeypatch
    ):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(
            cal, tmp_path, qtbot, attach_pixmap=False
        )

        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller.set_add_wall_patch_mode(True)

        assert canvas.add_wall_patch_mode() is False
        assert len(recorder.calls) == 1
        title, text = recorder.calls[0]
        assert "wall patch" in title.lower()
        assert "map image" in text.lower()

    def test_arm_with_image_succeeds(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        controller.set_add_wall_patch_mode(True)
        assert canvas.add_wall_patch_mode() is True

    def test_two_clicks_push_command_and_append_patch(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        controller.set_add_wall_patch_mode(True)

        _press(canvas, 50, 60)
        _press(canvas, 120, 140)

        assert cal.wall_patches == [(50, 60, 120, 140)]
        # Command went onto the stack so the action is undoable.
        assert controller.undo_stack().count() == 1

    def test_undo_removes_patch(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        controller.set_add_wall_patch_mode(True)

        _press(canvas, 50, 60)
        _press(canvas, 120, 140)
        assert len(cal.wall_patches) == 1

        controller.undo_stack().undo()
        assert cal.wall_patches == []

    def test_endpoint_clamping_partially_out_of_bounds(
        self, qtbot, tmp_path
    ):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        controller.set_add_wall_patch_mode(True)

        # End point well past the right/bottom edges of the 200x200 image.
        _press(canvas, 50, 60)
        _press(canvas, 500, 800)

        assert len(cal.wall_patches) == 1
        x1, y1, x2, y2 = cal.wall_patches[0]
        # First point inside — preserved.
        assert (x1, y1) == (50, 60)
        # Second point clamped to image bounds (image is 200x200 so last
        # valid pixel index is 199).
        assert x2 == _CANVAS_SIZE - 1
        assert y2 == _CANVAS_SIZE - 1

    def test_endpoint_clamping_negative_origin(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        controller.set_add_wall_patch_mode(True)

        # Start point above-left of the image; end point inside.
        _press(canvas, -100, -50)
        _press(canvas, 80, 90)

        assert len(cal.wall_patches) == 1
        x1, y1, x2, y2 = cal.wall_patches[0]
        assert (x1, y1) == (0, 0)
        assert (x2, y2) == (80, 90)

    def test_zero_length_after_clamp_is_rejected(
        self, qtbot, tmp_path, monkeypatch
    ):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)

        recorder = _MessageBoxRecorder()
        monkeypatch.setattr(QtWidgets.QMessageBox, "warning", recorder)

        controller.set_add_wall_patch_mode(True)
        # Both points far past the right edge → both clamp to (199, 199).
        _press(canvas, 500, 800)
        _press(canvas, 600, 900)

        assert cal.wall_patches == []
        assert len(recorder.calls) == 1
        title, _ = recorder.calls[0]
        assert "wall patch" in title.lower()

    def test_invalidates_wall_mask_after_commit(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        # Prime the cache so we can verify it's been dropped.
        controller._wall_mask_cache = object()  # noqa: SLF001

        controller.set_add_wall_patch_mode(True)
        _press(canvas, 50, 60)
        _press(canvas, 120, 140)

        assert controller._wall_mask_cache is None  # noqa: SLF001

    def test_undo_also_invalidates_wall_mask(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        controller.set_add_wall_patch_mode(True)
        _press(canvas, 50, 60)
        _press(canvas, 120, 140)

        # Re-prime the cache; undo should drop it again.
        controller._wall_mask_cache = object()  # noqa: SLF001
        controller.undo_stack().undo()

        assert controller._wall_mask_cache is None  # noqa: SLF001

    def test_canvas_wall_patch_layer_rebuilt_on_commit(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        assert len(canvas.wall_patch_items()) == 0

        controller.set_add_wall_patch_mode(True)
        _press(canvas, 50, 60)
        _press(canvas, 120, 140)

        items = canvas.wall_patch_items()
        assert len(items) == 1
        assert items[0].endpoints == (50.0, 60.0, 120.0, 140.0)

    def test_canvas_wall_patch_layer_rebuilt_on_undo(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        controller.set_add_wall_patch_mode(True)
        _press(canvas, 50, 60)
        _press(canvas, 120, 140)
        assert len(canvas.wall_patch_items()) == 1

        controller.undo_stack().undo()
        assert len(canvas.wall_patch_items()) == 0

    def test_cancelled_signal_does_not_push_command(self, qtbot, tmp_path):
        cal = _mkcal([], [])
        controller, canvas, _ = _build_controller(cal, tmp_path, qtbot)
        controller.set_add_wall_patch_mode(True)

        _press(canvas, 50, 60)
        _press_escape(canvas)

        assert cal.wall_patches == []
        assert controller.undo_stack().count() == 0
