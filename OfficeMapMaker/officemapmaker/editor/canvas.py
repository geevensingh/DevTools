"""``MapCanvas`` — the pan/zoom-able view that holds the map + overlays.

Why ``QGraphicsView`` instead of a hand-rolled paintEvent: the user's maps
are ~3500×4000 px with hundreds of clickable items. ``QGraphicsScene`` keeps
items in a BSP-indexed scene graph and culls draws to the viewport, so pan
and zoom stay smooth without us writing any of that bookkeeping.

This milestone (ed1) only adds the pixmap + pan/zoom/scroll. Overlay items
(labels and rooms) arrive in milestone ed2.
"""

from __future__ import annotations

from pathlib import Path
from typing import Optional

from PySide6 import QtCore, QtGui, QtWidgets


# Wheel zoom factor per notch. 1.15 ≈ 15% per scroll step, which feels right
# on a precision touchpad and isn't violent on a discrete mouse wheel.
_ZOOM_FACTOR_PER_NOTCH = 1.15

# Clamp zoom so the user can't lose the map entirely.
_MIN_ZOOM = 0.05
_MAX_ZOOM = 40.0


class MapCanvas(QtWidgets.QGraphicsView):
    """Scrollable, zoomable view of a floor-plan map.

    Pan: middle-mouse-button drag or ``ScrollHandDrag`` mode while no item
    is being clicked. Zoom: mouse wheel (anchored at the cursor so the
    point under the cursor stays put). Fit-to-window: ``fit_in_view()``.

    Coordinates: the scene coordinate system equals image-pixel coordinates,
    so every other module can pass raw bbox tuples without translation.
    """

    cursor_scene_pos_changed = QtCore.Signal(QtCore.QPointF)
    """Emitted on every mouse move, in scene (image-pixel) coordinates."""

    def __init__(self, parent: Optional[QtWidgets.QWidget] = None) -> None:
        super().__init__(parent)
        self._scene = QtWidgets.QGraphicsScene(self)
        self.setScene(self._scene)

        # Smooth rendering for the pixmap; SmoothPixmapTransform avoids the
        # crunchy nearest-neighbor look when zoomed in past 1:1.
        self.setRenderHints(
            QtGui.QPainter.RenderHint.Antialiasing
            | QtGui.QPainter.RenderHint.SmoothPixmapTransform
        )

        # Drag mode: empty space drags the scroll position; clicking an item
        # (added in ed2+) will instead start a rubber-band selection. For now
        # only the pan behaviour matters.
        self.setDragMode(QtWidgets.QGraphicsView.DragMode.ScrollHandDrag)
        self.setTransformationAnchor(
            QtWidgets.QGraphicsView.ViewportAnchor.AnchorUnderMouse
        )
        self.setResizeAnchor(
            QtWidgets.QGraphicsView.ViewportAnchor.AnchorUnderMouse
        )

        # Track mouse position even when no button is pressed so the status
        # bar can show live image-pixel coords.
        self.setMouseTracking(True)

        self._pixmap_item: Optional[QtWidgets.QGraphicsPixmapItem] = None
        self._zoom: float = 1.0

    # ------------------------------------------------------------------ map

    def set_map_image(self, map_path: Path) -> None:
        """Load and display the map image, resetting the view to fit."""
        pixmap = QtGui.QPixmap(str(map_path))
        if pixmap.isNull():
            raise FileNotFoundError(
                f"could not load map image: {map_path} "
                "(file missing or unsupported format)"
            )

        # Remove any previous pixmap so re-opening a different map works.
        if self._pixmap_item is not None:
            self._scene.removeItem(self._pixmap_item)

        self._pixmap_item = self._scene.addPixmap(pixmap)
        self._pixmap_item.setZValue(-1000)  # always behind future overlays
        self._scene.setSceneRect(QtCore.QRectF(pixmap.rect()))
        self.fit_in_view()

    # ---------------------------------------------------------------- zoom

    def fit_in_view(self) -> None:
        """Reset zoom so the entire scene fits inside the viewport."""
        if self._pixmap_item is None:
            return
        self.resetTransform()
        self._zoom = 1.0
        self.fitInView(
            self._scene.sceneRect(),
            QtCore.Qt.AspectRatioMode.KeepAspectRatio,
        )
        # fitInView leaves a non-unity transform; record the resulting scale
        # so future wheel zooms compose correctly.
        self._zoom = self.transform().m11()

    def zoom_by(self, factor: float) -> None:
        """Multiply the current zoom by ``factor``, clamped to limits."""
        target = self._zoom * factor
        if target < _MIN_ZOOM:
            factor = _MIN_ZOOM / self._zoom
        elif target > _MAX_ZOOM:
            factor = _MAX_ZOOM / self._zoom
        if factor == 1.0:
            return
        self.scale(factor, factor)
        self._zoom *= factor

    def current_zoom(self) -> float:
        """Return the current uniform zoom factor (1.0 = pixel-for-pixel)."""
        return self._zoom

    # -------------------------------------------------------- Qt overrides

    def wheelEvent(self, event: QtGui.QWheelEvent) -> None:  # noqa: N802 — Qt API
        """Wheel scroll → zoom (anchored at the cursor, no Ctrl needed).

        Floor-plan editing is a zoom-heavy workflow; making the wheel scroll
        do anything other than zoom would force the user to hold Ctrl all
        day. If they want to scroll, they can drag.
        """
        notches = event.angleDelta().y() / 120.0
        if notches == 0:
            super().wheelEvent(event)
            return
        factor = _ZOOM_FACTOR_PER_NOTCH ** notches
        self.zoom_by(factor)
        event.accept()

    def mouseMoveEvent(self, event: QtGui.QMouseEvent) -> None:  # noqa: N802 — Qt API
        scene_pos = self.mapToScene(event.position().toPoint())
        self.cursor_scene_pos_changed.emit(scene_pos)
        super().mouseMoveEvent(event)

    def keyPressEvent(self, event: QtGui.QKeyEvent) -> None:  # noqa: N802 — Qt API
        """Keyboard shortcuts owned by the canvas itself.

        Menu / toolbar shortcuts are configured at the ``QAction`` level so
        they work regardless of focus; the ones here are pure-canvas affordances.
        """
        key = event.key()
        if key in (QtCore.Qt.Key.Key_Plus, QtCore.Qt.Key.Key_Equal):
            self.zoom_by(_ZOOM_FACTOR_PER_NOTCH)
            event.accept()
            return
        if key == QtCore.Qt.Key.Key_Minus:
            self.zoom_by(1.0 / _ZOOM_FACTOR_PER_NOTCH)
            event.accept()
            return
        if key == QtCore.Qt.Key.Key_0:
            self.fit_in_view()
            event.accept()
            return
        super().keyPressEvent(event)
