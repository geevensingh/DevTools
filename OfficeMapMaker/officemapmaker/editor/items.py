"""``QGraphicsItem`` subclasses for the calibration editor.

The two interactive overlay item types:

* ``LabelItem`` — a clickable rectangle drawn around an OCR-detected label.
  Color encodes status: green = healthy (linked to a room, unique id),
  yellow = orphan (no room link), red = duplicate id (conflicts with another
  label).
* ``RoomItem`` — a translucent polygon over a connected-component room.
  Color encodes classification: cyan = office, gray = hallway, tan = common,
  red = skip, light yellow = orphan (no label points at it).

Both items keep a reference to their underlying calibration record so the
controller (added in ed3) can mutate the model from a click without having
to do a second lookup.
"""

from __future__ import annotations

from typing import Optional

import cv2
import numpy as np
from PySide6 import QtCore, QtGui, QtWidgets

from ..calibration import Classification, Label, Room
from ..geometry import rle_to_mask


# Z-order: the map pixmap lives at -1000 (set in MapCanvas.set_map_image).
# Rooms go above the map but below labels so label rectangles are always
# visible even over a heavily-tinted room.
Z_ROOM = -10
Z_LABEL = 0

# Label box visual style.
_LABEL_PEN_WIDTH = 2.0
_LABEL_COLOR_LINKED = QtGui.QColor("#1f8a1f")   # green
_LABEL_COLOR_ORPHAN = QtGui.QColor("#d18b00")   # amber
_LABEL_COLOR_DUPLICATE = QtGui.QColor("#cc0000")  # red

# Room polygon visual style. Translucent fill + slightly darker thin outline.
_ROOM_OUTLINE_WIDTH = 1.0
_ROOM_FILL_ALPHA = 70           # out of 255 — visible but doesn't obscure walls
_ROOM_OUTLINE_ALPHA = 200

# Classification → fill color (room is labeled; first label wins).
_ROOM_FILL_BY_CLASSIFICATION: dict[Classification, QtGui.QColor] = {
    Classification.OFFICE: QtGui.QColor("#6fbfff"),   # cyan
    Classification.HALLWAY: QtGui.QColor("#a0a0a0"),  # gray
    Classification.COMMON: QtGui.QColor("#dab880"),   # tan
    Classification.SKIP: QtGui.QColor("#e57373"),     # soft red
}
# An orphan room (no label points at it) is rendered in a distinct color so
# the user can spot the polygons the OCR pass missed entirely.
_ROOM_FILL_ORPHAN = QtGui.QColor("#fff176")  # light yellow

# Polygon simplification tolerance (pixels). Most room polygons are nearly
# rectangular; we don't need every contour vertex from cv2.findContours.
# Tighter values keep more detail at the cost of more QGraphicsItem vertices.
_POLYGON_APPROX_EPSILON_PX = 1.0


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _qcolor_with_alpha(c: QtGui.QColor, alpha: int) -> QtGui.QColor:
    out = QtGui.QColor(c)
    out.setAlpha(alpha)
    return out


def room_mask_to_polygons(mask: np.ndarray) -> list[QtGui.QPolygonF]:
    """Convert a binary room mask into one or more polygons for rendering.

    Most rooms produce a single outer contour; we collect all external
    contours so a room that happens to be two disjoint blobs still renders
    correctly. Holes (inner contours) are ignored — fine for v1, since
    the user only cares about the *outline* for hit-testing and overlay.
    """
    if mask.dtype != np.uint8:
        mask = mask.astype(np.uint8)
    # cv2 needs a non-zero "ink" pixel value. ``rle_to_mask`` already returns
    # 0/255 but we coerce to be safe in case a caller passes 0/1.
    if mask.max() <= 1:
        mask = (mask * 255).astype(np.uint8)

    contours, _ = cv2.findContours(
        mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE
    )
    polygons: list[QtGui.QPolygonF] = []
    for contour in contours:
        if len(contour) < 3:
            continue
        # Approximate to drop redundant collinear vertices. Cheaper renders,
        # cheaper hit-tests, no visible quality loss at editor zoom levels.
        approx = cv2.approxPolyDP(contour, _POLYGON_APPROX_EPSILON_PX, closed=True)
        polygon = QtGui.QPolygonF()
        for point in approx.reshape(-1, 2):
            polygon.append(QtCore.QPointF(float(point[0]), float(point[1])))
        polygons.append(polygon)
    return polygons


# ---------------------------------------------------------------------------
# RoomItem
# ---------------------------------------------------------------------------


class RoomItem(QtWidgets.QGraphicsPolygonItem):
    """One translucent polygon overlay for a calibration ``Room``.

    Stores a back-reference to the underlying ``Room`` record plus the
    classification it was drawn with so the canvas can recolor it in place
    after an edit (instead of tearing it down and rebuilding).
    """

    def __init__(
        self,
        polygon: QtGui.QPolygonF,
        *,
        room: Room,
        classification: Optional[Classification],
        parent: Optional[QtWidgets.QGraphicsItem] = None,
    ) -> None:
        super().__init__(polygon, parent)
        self.room: Room = room
        # ``None`` means orphan room (no label points at it).
        self.classification: Optional[Classification] = classification
        self.setZValue(Z_ROOM)
        self.setData(0, "room")
        self.setData(1, room.id)
        self.setAcceptHoverEvents(True)
        self.setFlag(
            QtWidgets.QGraphicsItem.GraphicsItemFlag.ItemIsSelectable, True
        )
        self._apply_style()

    def set_classification(self, classification: Optional[Classification]) -> None:
        """Recolor the polygon in place after an edit."""
        self.classification = classification
        self._apply_style()

    def _apply_style(self) -> None:
        if self.classification is None:
            base = _ROOM_FILL_ORPHAN
        else:
            base = _ROOM_FILL_BY_CLASSIFICATION.get(
                self.classification, _ROOM_FILL_ORPHAN
            )
        brush = QtGui.QBrush(_qcolor_with_alpha(base, _ROOM_FILL_ALPHA))
        pen = QtGui.QPen(_qcolor_with_alpha(base, _ROOM_OUTLINE_ALPHA))
        pen.setWidthF(_ROOM_OUTLINE_WIDTH)
        pen.setCosmetic(True)  # constant 1px regardless of zoom
        self.setBrush(brush)
        self.setPen(pen)


# ---------------------------------------------------------------------------
# LabelItem
# ---------------------------------------------------------------------------


class LabelItem(QtWidgets.QGraphicsRectItem):
    """Clickable rectangle drawn around an OCR-detected label.

    The rectangle uses ``bbox`` exactly — no inflation — so the user can see
    precisely what region of the map was identified as the label. Visual
    status (linked / orphan / duplicate-id) is encoded as a colored outline;
    the interior is left transparent so the user can still read the OCR'd
    glyphs through it.
    """

    # Statuses derived from the surrounding calibration; used to pick a color.
    STATUS_LINKED = "linked"
    STATUS_ORPHAN = "orphan"
    STATUS_DUPLICATE = "duplicate"

    def __init__(
        self,
        label: Label,
        label_index: int,
        *,
        status: str,
        parent: Optional[QtWidgets.QGraphicsItem] = None,
    ) -> None:
        x, y, w, h = label.bbox
        super().__init__(QtCore.QRectF(x, y, w, h), parent)
        self.label: Label = label
        self.label_index: int = label_index
        self.status: str = status
        self.setZValue(Z_LABEL)
        self.setData(0, "label")
        self.setData(1, label_index)
        self.setAcceptHoverEvents(True)
        self.setFlag(
            QtWidgets.QGraphicsItem.GraphicsItemFlag.ItemIsSelectable, True
        )
        self._apply_style()

    def set_status(self, status: str) -> None:
        """Recolor the box in place after the calibration changes."""
        self.status = status
        self._apply_style()

    def _apply_style(self) -> None:
        if self.status == self.STATUS_DUPLICATE:
            color = _LABEL_COLOR_DUPLICATE
        elif self.status == self.STATUS_ORPHAN:
            color = _LABEL_COLOR_ORPHAN
        else:
            color = _LABEL_COLOR_LINKED
        pen = QtGui.QPen(color)
        pen.setWidthF(_LABEL_PEN_WIDTH)
        pen.setCosmetic(True)
        self.setPen(pen)
        self.setBrush(QtGui.QBrush(QtCore.Qt.BrushStyle.NoBrush))


# ---------------------------------------------------------------------------
# Status computation
# ---------------------------------------------------------------------------


def compute_label_status(label: Label, duplicate_ids: set[str]) -> str:
    """Decide which color a ``LabelItem`` should use.

    Pure function — easy to unit-test without instantiating Qt items.
    """
    if label.id in duplicate_ids:
        return LabelItem.STATUS_DUPLICATE
    if label.room_id is None:
        return LabelItem.STATUS_ORPHAN
    return LabelItem.STATUS_LINKED


def find_duplicate_label_ids(labels: list[Label]) -> set[str]:
    """Return label ``id`` values that appear on more than one label.

    Used by the canvas to color all conflicting labels red. Two labels
    are considered duplicates if their ``Label.id`` string matches exactly
    — empty-string ids count too (degenerate but possible after a bad edit).
    """
    seen: dict[str, int] = {}
    for lab in labels:
        seen[lab.id] = seen.get(lab.id, 0) + 1
    return {lid for lid, count in seen.items() if count > 1}


def room_classification(room: Room, labels_for_room: list[Label]) -> Optional[Classification]:
    """Pick the classification used to color a room.

    Selection order:

    1. If the room has at least one label, return the first label's
       classification (matches the existing render pipeline).
    2. Otherwise infer from the room's bounding-box shape: long-and-thin
       rooms are almost certainly hallways even when nobody bothered to
       OCR-label them. This is a display-only inference — it never writes
       back to ``calibration.json`` and never trips the calibrate
       auto-checks. Returns ``None`` for rooms that don't match any
       shape heuristic (caller renders as orphan).
    """
    if labels_for_room:
        return labels_for_room[0].classification
    return _infer_classification_from_shape(room)


def _infer_classification_from_shape(room: Room) -> Optional[Classification]:
    """Guess a classification for a label-less room from its bbox shape.

    Mirrors the hallway rule in ``calibrate._classify`` (aspect >= 4 AND
    solidity >= 0.6) so the editor view stays consistent with the
    persisted calibration.
    """
    _, _, w, h = room.bbox
    if w <= 0 or h <= 0:
        return None
    long_side, short_side = max(w, h), min(w, h)
    aspect = long_side / short_side
    bbox_area = w * h
    solidity = (room.area_px / bbox_area) if bbox_area > 0 else 0.0
    if aspect >= 4.0 and solidity >= 0.6:
        return Classification.HALLWAY
    return None


def build_room_polygon(room: Room) -> Optional[QtGui.QPolygonF]:
    """Decode a room's RLE mask and return its largest external polygon.

    Returns ``None`` if the mask decodes to an empty or invalid shape.
    Callers should skip the room in that case rather than render nothing.
    """
    try:
        mask = rle_to_mask(room.polygon_rle)
    except ValueError:
        return None
    polygons = room_mask_to_polygons(mask)
    if not polygons:
        return None
    # Render the largest external polygon; ignore any disconnected blobs
    # (those are typically room-CC noise that survived calibration).
    return max(polygons, key=lambda p: abs(_polygon_area(p)))


def _polygon_area(polygon: QtGui.QPolygonF) -> float:
    """Signed polygon area via the shoelace formula (sign doesn't matter)."""
    n = polygon.count()
    if n < 3:
        return 0.0
    total = 0.0
    for i in range(n):
        a = polygon.at(i)
        b = polygon.at((i + 1) % n)
        total += a.x() * b.y() - b.x() * a.y()
    return 0.5 * total


__all__ = [
    "LabelItem",
    "RoomItem",
    "build_room_polygon",
    "compute_label_status",
    "find_duplicate_label_ids",
    "room_classification",
    "room_mask_to_polygons",
    "Z_LABEL",
    "Z_ROOM",
]
