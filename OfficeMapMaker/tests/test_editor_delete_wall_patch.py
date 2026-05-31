"""Tests for the select-and-delete wall-patch workflow (w9c).

Covers five feature surfaces in the calibration editor:

* :class:`~officemapmaker.editor.commands.DeleteWallPatchCommand` model
  mutations: removes the patch, restores it at the original index on undo,
  emits a structural ``WallPatchChange`` on both redo and undo, and is
  safe to undo / redo repeatedly.
* :class:`~officemapmaker.editor.sidebar.InspectorPanel`'s new wall-patch
  face: ``show_wall_patch`` populates the read-only endpoint summary;
  ``show_label`` / ``show_room`` / ``show_nothing`` clear the
  ``current_wall_patch_index`` tracker.
* :meth:`~officemapmaker.editor.controller.EditorController._handle_selection_changed`
  priority: label > wall_patch > room. The wall-patch branch shows the
  new inspector face when a :class:`WallPatchItem` is selected and no
  label sits in the same selection.
* :meth:`~officemapmaker.editor.controller.EditorController.delete_selected_wall_patch`
  glue: returns False when no patch is selected; on success pushes the
  command, invalidates the cached wall mask, and rebuilds the canvas
  overlay layer; undo restores both the patch and the mask.
* The unified "Delete selected" cascade in ``EditorMainWindow``:
  label first, wall patch second, room last.
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
    DeleteWallPatchCommand,
    WallPatchChange,
)
from officemapmaker.editor.controller import EditorController  # noqa: E402
from officemapmaker.editor.items import LabelItem, RoomItem, WallPatchItem  # noqa: E402
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
        map_path=None,
    )
    return controller, canvas, inspector


def _wall_patch_item(canvas: MapCanvas, patch_index: int) -> WallPatchItem:
    """Locate the WallPatchItem in the scene for ``patch_index`` (or raise)."""
    for item in canvas.wall_patch_items():
        if item.patch_index == patch_index:
            return item
    raise AssertionError(
        f"no WallPatchItem with patch_index={patch_index} in scene "
        f"(have {[it.patch_index for it in canvas.wall_patch_items()]})"
    )


# ---------------------------------------------------------------- command


class TestDeleteWallPatchCommand:
    def test_redo_removes_patch_at_index(self, qtbot):
        cal = _mkcal(
            labels=[],
            rooms=[],
            wall_patches=[(10, 10, 20, 20), (30, 30, 40, 40), (50, 50, 60, 60)],
        )
        cmd = DeleteWallPatchCommand(cal, patch_index=1)
        cmd.redo()
        assert cal.wall_patches == [(10, 10, 20, 20), (50, 50, 60, 60)]

    def test_undo_restores_at_original_index(self, qtbot):
        cal = _mkcal(
            labels=[],
            rooms=[],
            wall_patches=[(10, 10, 20, 20), (30, 30, 40, 40), (50, 50, 60, 60)],
        )
        cmd = DeleteWallPatchCommand(cal, patch_index=1)
        cmd.redo()
        cmd.undo()
        assert cal.wall_patches == [
            (10, 10, 20, 20),
            (30, 30, 40, 40),
            (50, 50, 60, 60),
        ]

    def test_undo_restores_at_end_when_list_shrank(self, qtbot):
        """If concurrent edits shrank the list below the original index, undo
        clamps the insertion point to len() — the patch still comes back
        rather than raising."""
        cal = _mkcal(
            labels=[],
            rooms=[],
            wall_patches=[(10, 10, 20, 20), (30, 30, 40, 40), (50, 50, 60, 60)],
        )
        cmd = DeleteWallPatchCommand(cal, patch_index=2)
        cmd.redo()
        # External edit removes another patch — the list is now length 1,
        # the original index 2 is out of range.
        del cal.wall_patches[0]
        cmd.undo()
        # Patch was restored at the end (clamped).
        assert (50, 50, 60, 60) in cal.wall_patches

    def test_redo_undo_redo_idempotent(self, qtbot):
        cal = _mkcal(
            labels=[],
            rooms=[],
            wall_patches=[(10, 10, 20, 20), (30, 30, 40, 40)],
        )
        cmd = DeleteWallPatchCommand(cal, patch_index=0)
        cmd.redo()
        cmd.undo()
        cmd.redo()
        assert cal.wall_patches == [(30, 30, 40, 40)]

    def test_on_change_fires_structural_on_redo(self, qtbot):
        cal = _mkcal(labels=[], rooms=[], wall_patches=[(1, 2, 3, 4)])
        captured: list[WallPatchChange] = []
        cmd = DeleteWallPatchCommand(cal, patch_index=0, on_change=captured.append)
        cmd.redo()
        assert len(captured) == 1
        assert captured[0].structural is True

    def test_on_change_fires_structural_on_undo(self, qtbot):
        cal = _mkcal(labels=[], rooms=[], wall_patches=[(1, 2, 3, 4)])
        captured: list[WallPatchChange] = []
        cmd = DeleteWallPatchCommand(cal, patch_index=0, on_change=captured.append)
        cmd.redo()
        cmd.undo()
        assert len(captured) == 2
        assert captured[1].structural is True

    def test_on_change_is_optional(self, qtbot):
        cal = _mkcal(labels=[], rooms=[], wall_patches=[(1, 2, 3, 4)])
        cmd = DeleteWallPatchCommand(cal, patch_index=0)
        cmd.redo()
        cmd.undo()
        assert cal.wall_patches == [(1, 2, 3, 4)]

    def test_out_of_range_raises(self, qtbot):
        cal = _mkcal(labels=[], rooms=[], wall_patches=[(1, 2, 3, 4)])
        with pytest.raises(IndexError):
            DeleteWallPatchCommand(cal, patch_index=1)
        with pytest.raises(IndexError):
            DeleteWallPatchCommand(cal, patch_index=-1)

    def test_redo_safe_when_index_already_gone(self, qtbot):
        """If a concurrent edit deleted the same patch, redo silently no-ops
        rather than raising IndexError."""
        cal = _mkcal(labels=[], rooms=[], wall_patches=[(1, 2, 3, 4)])
        cmd = DeleteWallPatchCommand(cal, patch_index=0)
        del cal.wall_patches[0]  # someone else got there first
        cmd.redo()  # should not raise
        assert cal.wall_patches == []

    def test_description_contains_endpoints(self, qtbot):
        cal = _mkcal(labels=[], rooms=[], wall_patches=[(11, 22, 33, 44)])
        cmd = DeleteWallPatchCommand(cal, patch_index=0)
        text = cmd.text()
        assert "wall patch" in text
        assert "11" in text and "22" in text
        assert "33" in text and "44" in text

    def test_undo_then_redo_idempotent_across_many_cycles(self, qtbot):
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[(10, 10, 20, 20), (30, 30, 40, 40), (50, 50, 60, 60)],
        )
        cmd = DeleteWallPatchCommand(cal, patch_index=1)
        for _ in range(5):
            cmd.redo()
            assert cal.wall_patches == [(10, 10, 20, 20), (50, 50, 60, 60)]
            cmd.undo()
            assert cal.wall_patches == [
                (10, 10, 20, 20),
                (30, 30, 40, 40),
                (50, 50, 60, 60),
            ]


# ----------------------------------------------------- inspector wall-patch face


class TestInspectorWallPatchFace:
    def test_show_wall_patch_populates_endpoint_labels(self, qtbot):
        inspector = InspectorPanel()
        qtbot.addWidget(inspector)
        inspector.show_wall_patch(patch_index=3, endpoints=(10, 20, 30, 40))
        widgets = inspector._wall_patch_widgets  # noqa: SLF001 — test only
        assert "3" in widgets["index"].text()
        assert "10" in widgets["start"].text()
        assert "20" in widgets["start"].text()
        assert "30" in widgets["end"].text()
        assert "40" in widgets["end"].text()

    def test_show_wall_patch_switches_stack_face(self, qtbot):
        inspector = InspectorPanel()
        qtbot.addWidget(inspector)
        inspector.show_wall_patch(patch_index=0, endpoints=(1, 2, 3, 4))
        assert (
            inspector._stack.currentWidget()  # noqa: SLF001
            is inspector._wall_patch_form  # noqa: SLF001
        )

    def test_show_wall_patch_sets_current_wall_patch_index(self, qtbot):
        inspector = InspectorPanel()
        qtbot.addWidget(inspector)
        inspector.show_wall_patch(patch_index=7, endpoints=(1, 2, 3, 4))
        assert inspector.current_wall_patch_index() == 7

    def test_show_wall_patch_rounds_fractional_coords(self, qtbot):
        inspector = InspectorPanel()
        qtbot.addWidget(inspector)
        inspector.show_wall_patch(
            patch_index=0, endpoints=(10.4, 20.6, 30.49, 40.51)
        )
        widgets = inspector._wall_patch_widgets  # noqa: SLF001
        # round() should match Python's banker's rounding semantics.
        assert "(10, 21)" in widgets["start"].text()
        assert "(30, 41)" in widgets["end"].text()

    def test_show_nothing_clears_wall_patch_index(self, qtbot):
        inspector = InspectorPanel()
        qtbot.addWidget(inspector)
        inspector.show_wall_patch(patch_index=3, endpoints=(1, 2, 3, 4))
        inspector.show_nothing()
        assert inspector.current_wall_patch_index() is None

    def test_show_label_clears_wall_patch_index(self, qtbot):
        inspector = InspectorPanel()
        qtbot.addWidget(inspector)
        inspector.show_wall_patch(patch_index=3, endpoints=(1, 2, 3, 4))
        inspector.show_label(
            label=_mklabel("100", room_id=None),
            label_index=0,
            available_room_ids=[],
        )
        assert inspector.current_wall_patch_index() is None

    def test_show_room_clears_wall_patch_index(self, qtbot):
        inspector = InspectorPanel()
        qtbot.addWidget(inspector)
        inspector.show_wall_patch(patch_index=3, endpoints=(1, 2, 3, 4))
        inspector.show_room(room=_mkroom(1), labels=[])
        assert inspector.current_wall_patch_index() is None

    def test_default_current_wall_patch_index_is_none(self, qtbot):
        inspector = InspectorPanel()
        qtbot.addWidget(inspector)
        assert inspector.current_wall_patch_index() is None


# ---------------------------------------------------- selection-changed priority


class TestSelectionPriorityWithWallPatch:
    def test_selecting_wall_patch_shows_wall_patch_face(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[],
            rooms=[],
            wall_patches=[(10, 10, 30, 30)],
        )
        _, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
        patch_item = _wall_patch_item(canvas, 0)
        patch_item.setSelected(True)
        assert inspector.current_wall_patch_index() == 0

    def test_label_wins_over_wall_patch(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[_mklabel("100", room_id=None)],
            rooms=[],
            wall_patches=[(10, 10, 30, 30)],
        )
        _, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
        # Multi-select: both label and wall patch in the selection.
        label_item = canvas.label_items()[0]
        patch_item = _wall_patch_item(canvas, 0)
        label_item.setSelected(True)
        patch_item.setSelected(True)
        # Label face should win.
        assert inspector.current_label_index() == 0
        assert inspector.current_wall_patch_index() is None

    def test_wall_patch_wins_over_room(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[],
            rooms=[_mkroom(1, x=0, y=0, side=100)],
            wall_patches=[(10, 10, 30, 30)],
        )
        _, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
        # Multi-select: both wall patch and room in the selection.
        room_item = canvas.room_items()[1]
        patch_item = _wall_patch_item(canvas, 0)
        room_item.setSelected(True)
        patch_item.setSelected(True)
        # Wall-patch face should win.
        assert inspector.current_wall_patch_index() == 0

    def test_deselecting_wall_patch_returns_to_empty(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[],
            rooms=[],
            wall_patches=[(10, 10, 30, 30)],
        )
        _, canvas, inspector = _build_controller(cal, tmp_path, qtbot)
        patch_item = _wall_patch_item(canvas, 0)
        patch_item.setSelected(True)
        assert inspector.current_wall_patch_index() == 0
        canvas.scene().clearSelection()
        assert inspector.current_wall_patch_index() is None


# ----------------------------------------------- controller delete-selected


class TestControllerDeleteWallPatch:
    def test_returns_false_when_nothing_selected(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, _canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        assert controller.delete_selected_wall_patch() is False
        # Nothing was deleted.
        assert cal.wall_patches == [(10, 10, 20, 20)]

    def test_returns_false_when_only_label_selected(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[_mklabel("100", room_id=None)],
            rooms=[],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        canvas.label_items()[0].setSelected(True)
        assert controller.delete_selected_wall_patch() is False
        assert cal.wall_patches == [(10, 10, 20, 20)]

    def test_returns_false_when_only_room_selected(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[],
            rooms=[_mkroom(1)],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        canvas.room_items()[1].setSelected(True)
        assert controller.delete_selected_wall_patch() is False
        assert cal.wall_patches == [(10, 10, 20, 20)]

    def test_deletes_selected_patch(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[
                (10, 10, 20, 20),
                (30, 30, 40, 40),
                (50, 50, 60, 60),
            ],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        patch_item = _wall_patch_item(canvas, 1)
        patch_item.setSelected(True)
        assert controller.delete_selected_wall_patch() is True
        assert cal.wall_patches == [(10, 10, 20, 20), (50, 50, 60, 60)]

    def test_undo_restores_patch(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[(10, 10, 20, 20), (30, 30, 40, 40)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        _wall_patch_item(canvas, 0).setSelected(True)
        controller.delete_selected_wall_patch()
        assert cal.wall_patches == [(30, 30, 40, 40)]
        controller.undo_stack().undo()
        assert cal.wall_patches == [(10, 10, 20, 20), (30, 30, 40, 40)]

    def test_canvas_layer_rebuilt_on_commit(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[(10, 10, 20, 20), (30, 30, 40, 40)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        _wall_patch_item(canvas, 1).setSelected(True)
        assert len(canvas.wall_patch_items()) == 2
        controller.delete_selected_wall_patch()
        assert len(canvas.wall_patch_items()) == 1
        # The remaining item is patch_index=0 with endpoints (10,10,20,20).
        remaining = canvas.wall_patch_items()[0]
        assert remaining.patch_index == 0
        assert remaining.endpoints == (10.0, 10.0, 20.0, 20.0)

    def test_canvas_layer_rebuilt_on_undo(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[(10, 10, 20, 20), (30, 30, 40, 40)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        _wall_patch_item(canvas, 1).setSelected(True)
        controller.delete_selected_wall_patch()
        assert len(canvas.wall_patch_items()) == 1
        controller.undo_stack().undo()
        assert len(canvas.wall_patch_items()) == 2
        # Indices restored 0..1 in the original order.
        indices = sorted(it.patch_index for it in canvas.wall_patch_items())
        assert indices == [0, 1]

    def test_mask_cache_invalidated_on_commit(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        # Prime the cache (the controller caches at first use of the mask).
        controller._wall_mask_cache = "primed"  # noqa: SLF001 — test sentinel
        _wall_patch_item(canvas, 0).setSelected(True)
        controller.delete_selected_wall_patch()
        assert controller._wall_mask_cache is None  # noqa: SLF001

    def test_mask_cache_invalidated_on_undo(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        _wall_patch_item(canvas, 0).setSelected(True)
        controller.delete_selected_wall_patch()
        controller._wall_mask_cache = "primed-after-delete"  # noqa: SLF001
        controller.undo_stack().undo()
        assert controller._wall_mask_cache is None  # noqa: SLF001

    def test_stale_patch_index_returns_false(self, qtbot, tmp_path):
        """If the item's stored patch_index is out of range (would only happen
        on a concurrent rebuild race), the controller refuses silently
        rather than pushing a command that would IndexError in its ctor."""
        cal = _mkcal(
            labels=[], rooms=[],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        patch_item = _wall_patch_item(canvas, 0)
        patch_item.patch_index = 99  # simulate stale index
        patch_item.setSelected(True)
        assert controller.delete_selected_wall_patch() is False
        # No command was pushed.
        assert controller.undo_stack().count() == 0


# -------------------------------------------------- delete-selected cascade


class TestDeleteSelectedCascadeWithWallPatch:
    """Verify the unified ``EditorMainWindow._on_delete_label`` cascade:
    label first, wall patch second, room last."""

    def test_cascade_deletes_label_when_label_selected(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[_mklabel("100", room_id=None)],
            rooms=[],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        canvas.label_items()[0].setSelected(True)
        # Simulate the cascade order from EditorMainWindow._on_delete_label.
        assert controller.delete_selected_label() is True
        assert cal.labels == []
        # Wall patch left alone.
        assert cal.wall_patches == [(10, 10, 20, 20)]

    def test_cascade_deletes_wall_patch_when_patch_selected(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[_mklabel("100", room_id=None)],
            rooms=[_mkroom(1)],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        _wall_patch_item(canvas, 0).setSelected(True)
        # Label cascade misses (no label selected).
        assert controller.delete_selected_label() is False
        # Wall-patch cascade hits.
        assert controller.delete_selected_wall_patch() is True
        assert cal.wall_patches == []
        # Other entities unaffected.
        assert len(cal.labels) == 1
        assert len(cal.rooms) == 1

    def test_cascade_falls_through_to_room(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[_mklabel("100", room_id=None)],
            rooms=[_mkroom(1)],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        canvas.room_items()[1].setSelected(True)
        assert controller.delete_selected_label() is False
        assert controller.delete_selected_wall_patch() is False
        assert controller.delete_selected_room() is True
        assert cal.rooms == []
        # Wall patch unaffected.
        assert cal.wall_patches == [(10, 10, 20, 20)]


# ---------------------------------------------- selection-priority for delete


class TestDeleteSelectedPriorityWhenMultiSelected:
    """When multi-selected, ``delete_selected_X`` for each type works
    independently — the cascade order in app.py decides which wins."""

    def test_delete_label_wins_when_label_plus_patch_selected(self, qtbot, tmp_path):
        cal = _mkcal(
            labels=[_mklabel("100", room_id=None)],
            rooms=[],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        canvas.label_items()[0].setSelected(True)
        _wall_patch_item(canvas, 0).setSelected(True)
        # In cascade order: label first wins.
        assert controller.delete_selected_label() is True
        assert cal.labels == []
        # Wall patch left for the next user action.
        assert cal.wall_patches == [(10, 10, 20, 20)]

    def test_delete_wall_patch_wins_over_room_when_both_selected(
        self, qtbot, tmp_path
    ):
        cal = _mkcal(
            labels=[],
            rooms=[_mkroom(1)],
            wall_patches=[(10, 10, 20, 20)],
        )
        controller, canvas, _inspector = _build_controller(cal, tmp_path, qtbot)
        canvas.room_items()[1].setSelected(True)
        _wall_patch_item(canvas, 0).setSelected(True)
        # Cascade order: label (no), wall_patch (yes) — room never tried.
        assert controller.delete_selected_label() is False
        assert controller.delete_selected_wall_patch() is True
        assert cal.wall_patches == []
        # Room still there.
        assert len(cal.rooms) == 1
