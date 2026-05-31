"""Tests for the wall-patch visualization workflow — w9a.

This first sub-phase of the wall-patch editor is read-only: it renders
existing ``Calibration.wall_patches`` entries as magenta line overlays
so the user can see what's currently there. Add (w9b) and select +
delete (w9c) land in the following sub-phases.

The tests here cover:

* :class:`~officemapmaker.editor.items.WallPatchItem` — constructor sets
  the right endpoints, metadata (``setData`` markers used for hit-test
  filtering), z-value (must sit above labels), selectability flag, and
  the widened :meth:`shape` for click-tolerance.
* :class:`~officemapmaker.editor.canvas.MapCanvas` integration —
  ``set_calibration`` builds one ``WallPatchItem`` per wall_patches
  entry, ``rebuild_wall_patches`` replaces the set wholesale,
  ``set_wall_patches_visible`` flips visibility, and the wall-patch
  toggle is independent of the orphans-only / area / hide-labeled
  filters that already exist for rooms.
"""

from __future__ import annotations

import numpy as np
import pytest

pytest.importorskip("PySide6")
pytest.importorskip("pytestqt")
from PySide6 import QtCore, QtWidgets  # noqa: E402

from officemapmaker.calibration import (  # noqa: E402
    Calibration,
    Label,
    RenderDefaults,
    Room,
)
from officemapmaker.editor.canvas import MapCanvas  # noqa: E402
from officemapmaker.editor.items import (  # noqa: E402
    LabelItem,
    Z_LABEL,
    Z_ROOM,
    Z_WALL_PATCH,
    WallPatchItem,
)
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


# ===================================================== WallPatchItem


class TestWallPatchItem:
    def test_constructor_stores_endpoints(self, qtbot):
        item = WallPatchItem(0, 10.0, 20.0, 100.0, 200.0)
        assert item.endpoints == (10.0, 20.0, 100.0, 200.0)
        line = item.line()
        assert line.x1() == 10.0
        assert line.y1() == 20.0
        assert line.x2() == 100.0
        assert line.y2() == 200.0

    def test_constructor_stores_patch_index(self, qtbot):
        item = WallPatchItem(7, 0, 0, 10, 10)
        assert item.patch_index == 7
        # Also exposed via the Qt setData(1, ...) channel so the canvas
        # / controller can recover the index from a scene item without
        # an isinstance check + attribute access (matches the LabelItem
        # and RoomItem convention).
        assert item.data(1) == 7

    def test_constructor_sets_type_marker(self, qtbot):
        item = WallPatchItem(0, 0, 0, 10, 10)
        # The "wall_patch" tag in setData(0, ...) matches the "label" /
        # "room" tags used by LabelItem / RoomItem for scene filtering.
        assert item.data(0) == "wall_patch"

    def test_z_value_above_labels(self, qtbot):
        item = WallPatchItem(0, 0, 0, 10, 10)
        # Wall patches must paint over labels so the user can see where
        # they plugged a gap that crosses a label area.
        assert item.zValue() == Z_WALL_PATCH
        assert Z_WALL_PATCH > Z_LABEL
        assert Z_WALL_PATCH > Z_ROOM

    def test_selectable_flag_is_set(self, qtbot):
        # Selection is harmless in w9a (no inspector wiring yet) but the
        # flag is needed for w9c. Set it now so the visible behaviour is
        # consistent across milestones.
        item = WallPatchItem(0, 0, 0, 10, 10)
        assert (
            item.flags() & QtWidgets.QGraphicsItem.GraphicsItemFlag.ItemIsSelectable
        )

    def test_shape_widens_hit_area_beyond_visible_stroke(self, qtbot):
        """The widened shape must contain a point ~3 px off the centerline.

        A bare ``QGraphicsLineItem.shape`` would only cover ~1 px on each
        side of the line (matching the visible pen). The override widens
        that to ~4 px so users can click a thin diagonal line without
        pixel-perfect aim — essential for delete in w9c.
        """
        item = WallPatchItem(0, 0.0, 50.0, 100.0, 50.0)  # horizontal line
        shape = item.shape()
        # 3 px above the line (within tolerance).
        assert shape.contains(QtCore.QPointF(50.0, 47.5))
        # 3 px below the line (within tolerance).
        assert shape.contains(QtCore.QPointF(50.0, 52.5))
        # Far away — should be outside the widened stroke.
        assert not shape.contains(QtCore.QPointF(50.0, 70.0))


# ===================================================== Canvas integration


class TestCanvasBuildsWallPatches:
    def test_empty_calibration_no_items(self, qtbot):
        cal = _mkcal([], [], wall_patches=[])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        assert canvas.wall_patch_items() == []

    def test_one_patch_one_item(self, qtbot):
        cal = _mkcal([], [], wall_patches=[(10, 20, 30, 40)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        items = canvas.wall_patch_items()
        assert len(items) == 1
        assert items[0].endpoints == (10.0, 20.0, 30.0, 40.0)
        assert items[0].patch_index == 0

    def test_multiple_patches_indexed_in_order(self, qtbot):
        cal = _mkcal(
            [],
            [],
            wall_patches=[(0, 0, 10, 10), (20, 20, 30, 30), (40, 40, 50, 50)],
        )
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        items = canvas.wall_patch_items()
        assert len(items) == 3
        assert [it.patch_index for it in items] == [0, 1, 2]
        assert items[1].endpoints == (20.0, 20.0, 30.0, 30.0)

    def test_set_calibration_clears_previous_patches(self, qtbot):
        """Re-loading a calibration must replace, not accumulate, wall patches."""
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        cal1 = _mkcal([], [], wall_patches=[(0, 0, 10, 10), (20, 20, 30, 30)])
        canvas.set_calibration(cal1)
        assert len(canvas.wall_patch_items()) == 2
        cal2 = _mkcal([], [], wall_patches=[(99, 99, 100, 100)])
        canvas.set_calibration(cal2)
        items = canvas.wall_patch_items()
        assert len(items) == 1
        assert items[0].endpoints == (99.0, 99.0, 100.0, 100.0)

    def test_items_added_to_scene(self, qtbot):
        """Items must actually be in the scene (not just in the canvas's list)."""
        cal = _mkcal([], [], wall_patches=[(10, 20, 30, 40)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        items = canvas.wall_patch_items()
        scene_items = canvas.scene().items()
        assert items[0] in scene_items


class TestCanvasRebuildWallPatches:
    def test_rebuild_picks_up_new_patches(self, qtbot):
        cal = _mkcal([], [], wall_patches=[(0, 0, 10, 10)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        assert len(canvas.wall_patch_items()) == 1
        # Mutate the model out-of-band (mirrors what AddWallPatchCommand
        # will do in w9b).
        cal.wall_patches.append((50, 50, 60, 60))
        canvas.rebuild_wall_patches()
        items = canvas.wall_patch_items()
        assert len(items) == 2
        assert items[1].endpoints == (50.0, 50.0, 60.0, 60.0)

    def test_rebuild_drops_removed_patches(self, qtbot):
        cal = _mkcal(
            [],
            [],
            wall_patches=[(0, 0, 10, 10), (20, 20, 30, 30)],
        )
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        assert len(canvas.wall_patch_items()) == 2
        del cal.wall_patches[0]
        canvas.rebuild_wall_patches()
        items = canvas.wall_patch_items()
        assert len(items) == 1
        # The remaining patch is now at index 0 (indices shift on remove).
        assert items[0].patch_index == 0
        assert items[0].endpoints == (20.0, 20.0, 30.0, 30.0)

    def test_rebuild_does_not_touch_label_items(self, qtbot):
        """Wall-patch rebuild must not invalidate cached label items."""
        cal = _mkcal(
            [_mklabel("1480", room_id=None)],
            [],
            wall_patches=[(0, 0, 10, 10)],
        )
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        before = canvas.label_items()
        canvas.rebuild_wall_patches()
        after = canvas.label_items()
        # Same objects, not just same indices — proves we didn't tear
        # them down + rebuild them as a side effect.
        assert list(before.values()) == list(after.values())


class TestCanvasWallPatchVisibility:
    def test_default_visible(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.wall_patches_visible() is True

    def test_toggle_hides_items(self, qtbot):
        cal = _mkcal([], [], wall_patches=[(0, 0, 10, 10), (20, 20, 30, 30)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        canvas.set_wall_patches_visible(False)
        assert canvas.wall_patches_visible() is False
        for item in canvas.wall_patch_items():
            assert item.isVisible() is False

    def test_toggle_back_on_restores_items(self, qtbot):
        cal = _mkcal([], [], wall_patches=[(0, 0, 10, 10)])
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        canvas.set_wall_patches_visible(False)
        canvas.set_wall_patches_visible(True)
        assert canvas.wall_patches_visible() is True
        for item in canvas.wall_patch_items():
            assert item.isVisible() is True

    def test_orphans_only_does_not_hide_wall_patches(self, qtbot):
        """Wall-patch toggle is independent of orphans-only filter.

        Wall patches are mask-only repairs — they have no notion of
        "orphan" or "labeled". The orphans-only filter hides linked
        labels and labeled rooms; it must leave wall patches alone.
        """
        cal = _mkcal(
            [_mklabel("1480", room_id=42)],
            [_mkroom(42)],
            wall_patches=[(0, 0, 10, 10)],
        )
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        canvas.set_orphans_only(True)
        # The labeled label has been hidden by the orphans-only filter…
        for item in canvas.label_items().values():
            if item.status == LabelItem.STATUS_LINKED:
                assert item.isVisible() is False
        # …but the wall patch is still visible (independent toggle).
        for item in canvas.wall_patch_items():
            assert item.isVisible() is True

    def test_min_room_area_does_not_hide_wall_patches(self, qtbot):
        """The min-room-area filter is room-specific; wall patches ignore it."""
        cal = _mkcal(
            [],
            [_mkroom(1, side=5)],  # tiny — would be filtered out
            wall_patches=[(0, 0, 10, 10)],
        )
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_calibration(cal)
        canvas.set_min_room_area(1000)
        for item in canvas.wall_patch_items():
            assert item.isVisible() is True
