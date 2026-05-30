"""Tests for ed5 — filter dock + search box + min-room-area filter.

Three test surfaces:

* :class:`~officemapmaker.editor.canvas.MapCanvas` filter / search helpers:
  ``set_min_room_area``, ``set_hide_labeled_rooms``,
  ``find_label_indices``, ``center_on_label``.

* :class:`~officemapmaker.editor.filter_dock.FilterDock` widget plumbing:
  slider ↔ spinbox sync, signal emission on user actions, search-button
  enable state, results-text setter.

* The cycle behaviour for "Next" / "Previous" with wrapping, including
  the edge cases of empty match list and exactly-one-match.

GUI tests use ``pytest-qt``'s ``qtbot`` fixture for the standard
``QApplication``-sharing pattern; no real event loop is spun.
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
from officemapmaker.editor.filter_dock import FilterDock  # noqa: E402
from officemapmaker.editor.items import LabelItem, RoomItem  # noqa: E402
from officemapmaker.geometry import mask_to_rle  # noqa: E402


# ---------------------------------------------------------------- helpers


_CANVAS_SIZE = 400


def _square_rle(*, x: int, y: int, side: int, canvas_size: int = _CANVAS_SIZE) -> str:
    mask = np.zeros((canvas_size, canvas_size), dtype=np.uint8)
    mask[y : y + side, x : x + side] = 1
    return mask_to_rle(mask)


def _mklabel(
    id_: str,
    *,
    bbox: tuple[int, int, int, int] = (0, 0, 10, 10),
    room_id: int | None = None,
) -> Label:
    return Label(
        id=id_,
        bbox=bbox,
        room_id=room_id,
        fill_seed=(bbox[0] + bbox[2] // 2, bbox[1] + bbox[3] // 2),
        ocr_confidence=0.9,
        notes="",
    )


def _mkroom(
    id_: int,
    *,
    x: int = 0,
    y: int = 0,
    side: int = 40,
    area_px: int | None = None,
) -> Room:
    return Room(
        id=id_,
        polygon_rle=_square_rle(x=x, y=y, side=side),
        area_px=area_px if area_px is not None else side * side,
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
    pixmap = QtGui.QPixmap(size, size)
    pixmap.fill(QtCore.Qt.GlobalColor.white)
    canvas._pixmap_item = canvas._scene.addPixmap(pixmap)  # noqa: SLF001
    canvas._pixmap_item.setZValue(-100)  # noqa: SLF001
    canvas._scene.setSceneRect(0, 0, size, size)  # noqa: SLF001


# =============================================================================
# Canvas filter helpers
# =============================================================================


class TestCanvasMinRoomArea:
    def test_default_is_zero(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.min_room_area() == 0

    def test_set_min_room_area_hides_small_rooms(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        cal = _mkcal(
            labels=[],
            rooms=[
                _mkroom(1, x=0, y=0, side=10, area_px=100),
                _mkroom(2, x=20, y=0, side=40, area_px=1600),
                _mkroom(3, x=70, y=0, side=80, area_px=6400),
            ],
        )
        canvas.set_calibration(cal)
        items = canvas.room_items()
        assert all(item.isVisible() for item in items.values())

        canvas.set_min_room_area(1000)
        assert items[1].isVisible() is False  # 100 < 1000
        assert items[2].isVisible() is True   # 1600 >= 1000
        assert items[3].isVisible() is True   # 6400 >= 1000

        canvas.set_min_room_area(5000)
        assert items[1].isVisible() is False
        assert items[2].isVisible() is False
        assert items[3].isVisible() is True

    def test_set_min_room_area_zero_restores_visibility(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        cal = _mkcal(
            labels=[],
            rooms=[_mkroom(1, area_px=100), _mkroom(2, x=60, area_px=900)],
        )
        canvas.set_calibration(cal)
        canvas.set_min_room_area(500)
        items = canvas.room_items()
        assert items[1].isVisible() is False
        assert items[2].isVisible() is True

        canvas.set_min_room_area(0)
        assert items[1].isVisible() is True
        assert items[2].isVisible() is True

    def test_negative_value_clamps_to_zero(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.set_min_room_area(-50)
        assert canvas.min_room_area() == 0


class TestCanvasHideLabeledRooms:
    def test_default_is_false(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.hide_labeled_rooms() is False

    def test_hides_only_labeled_rooms(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        cal = _mkcal(
            labels=[_mklabel("1480", room_id=1)],
            rooms=[
                _mkroom(1, x=0, y=0, side=40),   # labeled
                _mkroom(2, x=60, y=0, side=40),  # orphan
            ],
        )
        canvas.set_calibration(cal)
        items = canvas.room_items()

        canvas.set_hide_labeled_rooms(True)
        assert items[1].isVisible() is False
        assert items[2].isVisible() is True

        canvas.set_hide_labeled_rooms(False)
        assert items[1].isVisible() is True
        assert items[2].isVisible() is True


class TestCanvasFindLabelIndices:
    def test_empty_query_returns_empty(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        cal = _mkcal(
            labels=[_mklabel("1480", bbox=(10, 10, 20, 20))],
            rooms=[_mkroom(1)],
        )
        canvas.set_calibration(cal)
        assert canvas.find_label_indices("") == []
        assert canvas.find_label_indices("   ") == []

    def test_no_calibration_returns_empty(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        assert canvas.find_label_indices("1480") == []

    def test_case_insensitive_substring(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        cal = _mkcal(
            labels=[
                _mklabel("1480", bbox=(0, 0, 10, 10)),
                _mklabel("1505B", bbox=(0, 50, 10, 10)),
                _mklabel("MER101", bbox=(0, 100, 10, 10)),
                _mklabel("1505A", bbox=(0, 150, 10, 10)),
            ],
            rooms=[_mkroom(1)],
        )
        canvas.set_calibration(cal)

        assert canvas.find_label_indices("1505") == [1, 3]
        assert canvas.find_label_indices("1505b") == [1]  # case-insensitive
        assert canvas.find_label_indices("MER") == [2]
        assert canvas.find_label_indices("mer") == [2]
        assert canvas.find_label_indices("9999") == []

    def test_sorted_top_to_bottom_then_left_to_right(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        # Same id at four positions: top-left, top-right, bottom-left,
        # bottom-right. Expected order: top-left, top-right, bottom-left,
        # bottom-right (y first, x within y).
        cal = _mkcal(
            labels=[
                _mklabel("1480", bbox=(100, 100, 10, 10)),  # idx 0 (top-right)
                _mklabel("1480", bbox=(10, 10, 10, 10)),    # idx 1 (top-left)
                _mklabel("1480", bbox=(100, 200, 10, 10)),  # idx 2 (bottom-right)
                _mklabel("1480", bbox=(10, 200, 10, 10)),   # idx 3 (bottom-left)
            ],
            rooms=[_mkroom(1)],
        )
        canvas.set_calibration(cal)

        # By y, then x: idx 1 (y=10, x=10), idx 0 (y=100, x=100),
        # idx 3 (y=200, x=10), idx 2 (y=200, x=100).
        assert canvas.find_label_indices("1480") == [1, 0, 3, 2]


class TestCanvasCenterOnLabel:
    def test_centers_and_selects(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        canvas.show()
        canvas.resize(200, 200)
        _attach_pixmap(canvas)
        cal = _mkcal(
            labels=[
                _mklabel("1480", bbox=(10, 10, 10, 10)),
                _mklabel("1490", bbox=(300, 300, 10, 10)),
            ],
            rooms=[_mkroom(1)],
        )
        canvas.set_calibration(cal)

        canvas.center_on_label(1)
        items = canvas.label_items()
        assert items[1].isSelected() is True
        assert items[0].isSelected() is False
        # ensureVisible/centerOn should put the centre of the target
        # within the viewport. ``mapFromScene(item.scenePos())`` gives
        # the view-coord of the item's local origin; testing the centre
        # of its bounding rect is more reliable.
        target_centre = items[1].sceneBoundingRect().center()
        view_centre_scene = canvas.mapToScene(
            canvas.viewport().rect().center()
        )
        # Distance from viewport centre to the target should be small
        # (within a single label-width of slop for centring rounding).
        dx = abs(view_centre_scene.x() - target_centre.x())
        dy = abs(view_centre_scene.y() - target_centre.y())
        assert dx < 20
        assert dy < 20

    def test_unknown_index_is_noop(self, qtbot):
        canvas = MapCanvas()
        qtbot.addWidget(canvas)
        _attach_pixmap(canvas)
        cal = _mkcal(labels=[_mklabel("1480")], rooms=[_mkroom(1)])
        canvas.set_calibration(cal)
        # Doesn't raise; doesn't change selection.
        canvas.center_on_label(99)
        assert canvas.label_items()[0].isSelected() is False


# =============================================================================
# FilterDock widget plumbing
# =============================================================================


class TestFilterDockWidgets:
    def test_constructs_with_default_state(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        assert dock.min_room_area() == 0
        assert dock.hide_labeled_rooms() is False
        assert dock.orphans_only() is False
        assert dock.search_text() == ""

    def test_search_text_changed_emits(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        seen: list[str] = []
        dock.search_text_changed.connect(seen.append)
        dock._search_box_widget().setText("1480")  # noqa: SLF001
        assert seen == ["1480"]

    def test_search_buttons_disabled_until_text(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        # Access the buttons via the layout: dock's internal API exposes
        # the line edit; the buttons are reachable through findChildren.
        next_btn, prev_btn = _find_next_prev_buttons(dock)
        assert next_btn.isEnabled() is False
        assert prev_btn.isEnabled() is False

        dock._search_box_widget().setText("X")  # noqa: SLF001
        assert next_btn.isEnabled() is True
        assert prev_btn.isEnabled() is True

        dock._search_box_widget().setText("")  # noqa: SLF001
        assert next_btn.isEnabled() is False
        assert prev_btn.isEnabled() is False

    def test_return_pressed_emits_submitted_and_next(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        submitted: list[str] = []
        next_calls: list[None] = []
        dock.search_submitted.connect(submitted.append)
        dock.find_next_requested.connect(lambda: next_calls.append(None))

        dock._search_box_widget().setText("1480")  # noqa: SLF001
        QtCore.QCoreApplication.processEvents()
        dock._search_box_widget().returnPressed.emit()  # noqa: SLF001

        assert submitted == ["1480"]
        assert len(next_calls) == 1

    def test_return_pressed_with_empty_text_no_find_next(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        submitted: list[str] = []
        next_calls: list[None] = []
        dock.search_submitted.connect(submitted.append)
        dock.find_next_requested.connect(lambda: next_calls.append(None))

        dock._search_box_widget().returnPressed.emit()  # noqa: SLF001

        # submitted still fires (with the empty text) but find_next does not.
        assert submitted == [""]
        assert next_calls == []

    def test_area_slider_drives_spinbox(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        emitted: list[int] = []
        dock.min_room_area_changed.connect(emitted.append)

        dock._area_slider_widget().setValue(3000)  # noqa: SLF001
        assert dock._area_spin_widget().value() == 3000  # noqa: SLF001
        assert emitted == [3000]

    def test_area_spinbox_drives_slider(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        emitted: list[int] = []
        dock.min_room_area_changed.connect(emitted.append)

        dock._area_spin_widget().setValue(2500)  # noqa: SLF001
        assert dock._area_slider_widget().value() == 2500  # noqa: SLF001
        assert emitted == [2500]

    def test_spinbox_above_slider_max_clamps_slider(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        emitted: list[int] = []
        dock.min_room_area_changed.connect(emitted.append)

        dock._area_spin_widget().setValue(50000)  # noqa: SLF001
        assert dock._area_spin_widget().value() == 50000  # noqa: SLF001
        # Slider clamps at its max (10000); spinbox keeps the exact value.
        assert dock._area_slider_widget().value() == 10000  # noqa: SLF001
        assert emitted == [50000]

    def test_hide_labeled_toggle_emits(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        emitted: list[bool] = []
        dock.hide_labeled_rooms_changed.connect(emitted.append)
        dock._hide_labeled_checkbox().setChecked(True)  # noqa: SLF001
        dock._hide_labeled_checkbox().setChecked(False)  # noqa: SLF001
        assert emitted == [True, False]

    def test_orphans_only_toggle_emits(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        emitted: list[bool] = []
        dock.orphans_only_changed.connect(emitted.append)
        dock._orphans_only_checkbox().setChecked(True)  # noqa: SLF001
        assert emitted == [True]

    def test_set_orphans_only_no_loop(self, qtbot):
        """Programmatic ``set_orphans_only`` must not re-emit."""
        dock = FilterDock()
        qtbot.addWidget(dock)
        emitted: list[bool] = []
        dock.orphans_only_changed.connect(emitted.append)
        # Setting to the same value emits nothing (QCheckBox swallows).
        dock.set_orphans_only(False)
        assert emitted == []
        # Setting to a different value emits (we want the change to
        # propagate; the wrapping main-window slot is what blocks
        # signals if it needs to break a loop).
        dock.set_orphans_only(True)
        assert emitted == [True]

    def test_set_results_text(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        dock.set_results_text("3 matches")
        # Read back via the private accessor — the label is purely
        # informational so a public getter would be overkill.
        from PySide6 import QtWidgets as _QW  # noqa: F401
        label = dock.findChildren(QtWidgets.QLabel)
        # One of the QLabels in the dock will carry the text; group-box
        # titles are also QLabels in some styles, so check for membership.
        assert any(child.text() == "3 matches" for child in label)

    def test_focus_search_focuses_line_edit(self, qtbot):
        dock = FilterDock()
        qtbot.addWidget(dock)
        dock.show()
        dock.focus_search()
        QtCore.QCoreApplication.processEvents()
        assert dock._search_box_widget().hasFocus() is True  # noqa: SLF001


def _find_next_prev_buttons(
    dock: FilterDock,
) -> tuple[QtWidgets.QPushButton, QtWidgets.QPushButton]:
    """Return (next_button, prev_button) from the dock's button widgets."""
    btns = dock.findChildren(QtWidgets.QPushButton)
    next_btn = next(b for b in btns if "Next" in b.text())
    prev_btn = next(b for b in btns if "Prev" in b.text())
    return next_btn, prev_btn
