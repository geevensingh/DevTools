"""Shared read-only pan/zoom preview view used by W7 (layout) and
W8 (build composite).

Both steps need the same UX: a worker thread renders a PNG, the main
thread loads it as a QPixmap, and the user wants to wheel-zoom around
the cursor + middle-drag pan + double-click to fit. Per-issue "Show on
map" actions call ``center_on_bbox`` to jump to the relevant room.
"""

from __future__ import annotations

from typing import Optional, Tuple

from PySide6 import QtCore, QtGui, QtWidgets


class PreviewGraphicsView(QtWidgets.QGraphicsView):
    """Read-only pan/zoom view of a rendered preview pixmap.

    Mouse wheel zooms around the cursor; middle-button drag pans.
    Double-click resets the view to fit the whole preview.
    """

    _ZOOM_IN = 1.15
    _ZOOM_OUT = 1 / 1.15

    def __init__(self, parent: Optional[QtWidgets.QWidget] = None) -> None:
        super().__init__(parent)
        self.setScene(QtWidgets.QGraphicsScene(self))
        self.setRenderHints(
            QtGui.QPainter.RenderHint.SmoothPixmapTransform
            | QtGui.QPainter.RenderHint.Antialiasing
        )
        self.setTransformationAnchor(
            QtWidgets.QGraphicsView.ViewportAnchor.AnchorUnderMouse
        )
        self.setResizeAnchor(
            QtWidgets.QGraphicsView.ViewportAnchor.AnchorViewCenter
        )
        self.setDragMode(QtWidgets.QGraphicsView.DragMode.NoDrag)
        self.setBackgroundBrush(QtGui.QBrush(QtGui.QColor("#1e1e1e")))
        self._pixmap_item: Optional[QtWidgets.QGraphicsPixmapItem] = None
        # Middle-button drag state.
        self._panning = False
        self._pan_anchor: Optional[QtCore.QPoint] = None

    # ------------------------------------------------------------------
    # Public API
    # ------------------------------------------------------------------

    def set_pixmap(self, pixmap: QtGui.QPixmap) -> None:
        """Replace the displayed pixmap and fit-to-window.

        The fit call is deferred to the next event-loop tick so the
        viewport has its post-layout size by the time we compute the
        scale. Without this, the very first ``set_pixmap`` (when the
        containing pane hasn't been shown yet) ends up calling
        ``fitInView`` on a 0x0 viewport, baking in a near-zero
        transform that survives the subsequent resize.
        """
        scene = self.scene()
        scene.clear()
        self._pixmap_item = scene.addPixmap(pixmap)
        scene.setSceneRect(QtCore.QRectF(pixmap.rect()))
        QtCore.QTimer.singleShot(0, self.fit_to_window)

    def clear_pixmap(self) -> None:
        self.scene().clear()
        self._pixmap_item = None

    def has_pixmap(self) -> bool:
        return self._pixmap_item is not None

    def fit_to_window(self) -> None:
        if self._pixmap_item is None:
            return
        self.fitInView(
            self._pixmap_item, QtCore.Qt.AspectRatioMode.KeepAspectRatio
        )

    def center_on_bbox(self, bbox: Tuple[int, int, int, int]) -> None:
        """Pan + zoom so ``bbox`` (x, y, w, h) fills ~60% of the view."""
        if self._pixmap_item is None:
            return
        x, y, w, h = bbox
        if w <= 0 or h <= 0:
            return
        # Pad the bbox so the target room has breathing room around it.
        pad = max(w, h) * 0.6
        target = QtCore.QRectF(
            x - pad, y - pad, w + 2 * pad, h + 2 * pad
        )
        # Reset transform so fitInView's scale starts from identity —
        # otherwise repeated jumps compound.
        self.resetTransform()
        self.fitInView(target, QtCore.Qt.AspectRatioMode.KeepAspectRatio)

    # ------------------------------------------------------------------
    # Mouse / wheel
    # ------------------------------------------------------------------

    def wheelEvent(self, event: QtGui.QWheelEvent) -> None:
        if self._pixmap_item is None:
            return
        factor = self._ZOOM_IN if event.angleDelta().y() > 0 else self._ZOOM_OUT
        self.scale(factor, factor)

    def mousePressEvent(self, event: QtGui.QMouseEvent) -> None:
        if event.button() == QtCore.Qt.MouseButton.MiddleButton:
            self._panning = True
            self._pan_anchor = event.position().toPoint()
            self.setCursor(QtCore.Qt.CursorShape.ClosedHandCursor)
            event.accept()
            return
        super().mousePressEvent(event)

    def mouseMoveEvent(self, event: QtGui.QMouseEvent) -> None:
        if self._panning and self._pan_anchor is not None:
            pos = event.position().toPoint()
            delta = pos - self._pan_anchor
            self._pan_anchor = pos
            self.horizontalScrollBar().setValue(
                self.horizontalScrollBar().value() - delta.x()
            )
            self.verticalScrollBar().setValue(
                self.verticalScrollBar().value() - delta.y()
            )
            event.accept()
            return
        super().mouseMoveEvent(event)

    def mouseReleaseEvent(self, event: QtGui.QMouseEvent) -> None:
        if event.button() == QtCore.Qt.MouseButton.MiddleButton and self._panning:
            self._panning = False
            self._pan_anchor = None
            self.unsetCursor()
            event.accept()
            return
        super().mouseReleaseEvent(event)

    def mouseDoubleClickEvent(self, event: QtGui.QMouseEvent) -> None:
        self.fit_to_window()
        event.accept()
