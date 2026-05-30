"""``MapCanvas`` — the pan/zoom-able view that holds the map + overlays.

Why ``QGraphicsView`` instead of a hand-rolled paintEvent: the user's maps
are ~3500×4000 px with hundreds of clickable items. ``QGraphicsScene`` keeps
items in a BSP-indexed scene graph and culls draws to the viewport, so pan
and zoom stay smooth without us writing any of that bookkeeping.

This module owns the visual representation of the calibration:

* Pan/zoom/scroll mouse + keyboard plumbing.
* The pixmap of the map image.
* The label/room overlay items (added in milestone ed2).
* Layer-visibility toggles (labels on/off, rooms on/off, orphans only).

Mutation of the underlying ``Calibration`` happens via ``QUndoCommand``s
in milestone ed3+; this module just renders and re-styles items in place.
"""

from __future__ import annotations

from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
from typing import Optional

from PySide6 import QtCore, QtGui, QtWidgets

from ..calibration import Calibration
from .items import (
    LabelItem,
    RoomItem,
    build_room_polygon,
    compute_label_status,
    find_duplicate_label_ids,
)


# Wheel zoom factor per notch. 1.15 ≈ 15% per scroll step, which feels right
# on a precision touchpad and isn't violent on a discrete mouse wheel.
_ZOOM_FACTOR_PER_NOTCH = 1.15

# Clamp zoom so the user can't lose the map entirely.
_MIN_ZOOM = 0.05
_MAX_ZOOM = 40.0

# Below this size in viewport pixels (in either dimension), a press-then-
# release in add-room-rect mode is treated as a "I didn't mean to drag"
# misclick rather than a real rectangle. Picked at 4 px because real
# add-room-rect gestures are always > 50 px on a workable map; 4 px is
# well under the smallest plausible intentional drag.
_RECT_MIN_DRAG_PIXELS = 4


class MapCanvas(QtWidgets.QGraphicsView):
    """Scrollable, zoomable view of a floor-plan map plus overlay items.

    Pan: middle-mouse-button drag or ``ScrollHandDrag`` mode while no item
    is being clicked. Zoom: mouse wheel (anchored at the cursor so the
    point under the cursor stays put). Fit-to-window: ``fit_in_view()``.

    Coordinates: the scene coordinate system equals image-pixel coordinates,
    so every other module can pass raw bbox tuples without translation.
    """

    cursor_scene_pos_changed = QtCore.Signal(QtCore.QPointF)
    """Emitted on every mouse move, in scene (image-pixel) coordinates."""

    room_picked = QtCore.Signal(int)
    """Emitted when the user clicks a room while in pick-room mode.

    Payload is the ``Room.id`` of the clicked room. The controller turns
    this into a ``ChangeRoomLinkCommand`` against whichever label was being
    edited when pick mode started.
    """

    room_pick_cancelled = QtCore.Signal()
    """Emitted when the user exits pick-room mode without picking a room.

    Sources: Esc key, clicking empty space, or clicking a label item (in
    which case selection then proceeds normally).
    """

    add_label_requested = QtCore.Signal(QtCore.QPointF)
    """Emitted when the user clicks the canvas while in add-label mode.

    Payload is the click position in scene (image-pixel) coordinates. The
    controller turns this into an ``AddLabelCommand`` after prompting the
    user for the new label's id (and auto-linking to a room polygon under
    the click, if any).
    """

    add_label_cancelled = QtCore.Signal()
    """Emitted when add-label mode is dismissed without placing a label.

    Sources: Esc key. (Unlike pick-room mode, a click *always* attempts a
    placement — there is no "click empty space to cancel" because every
    pixel of the canvas is a valid location for a manually-added label.)
    """

    add_room_flood_requested = QtCore.Signal(QtCore.QPointF)
    """Emitted when the user clicks the canvas while in add-room-flood mode.

    Payload is the click position in scene (image-pixel) coordinates. The
    controller turns this into an ``AddRoomCommand`` after running a
    virtual flood-fill from the click point against the binarized map
    (+ wall_patches) to discover the room's polygon.
    """

    add_room_flood_cancelled = QtCore.Signal()
    """Emitted when add-room-flood mode is dismissed without placing a room.

    Sources: Esc key. As with add-label, every pixel of the canvas is a
    *candidate* click target — the controller is responsible for rejecting
    seeds that land on a wall or produce an implausibly large fill.
    """

    add_room_rect_requested = QtCore.Signal(QtCore.QRectF)
    """Emitted when the user finishes a rubber-band drag in add-room-rect mode.

    Payload is the dragged rectangle in scene (image-pixel) coordinates,
    already normalized so width / height are positive. The controller turns
    this into a Room whose polygon is the rectangle itself and pushes an
    ``AddRoomCommand``.
    """

    add_room_rect_cancelled = QtCore.Signal()
    """Emitted when add-room-rect mode ends without producing a rectangle.

    Sources: Esc key, or a press-then-release with no perceptible drag
    (the user clicked rather than dragged — we treat this as "I changed
    my mind" rather than building a 1-pixel room).
    """

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

        # Drag mode: ``RubberBandDrag`` is what lets a left-click on an item
        # actually *select* it (``ScrollHandDrag`` swallows clicks for panning
        # before items see them, which is fatal for an editor). On empty
        # canvas, left-drag draws a rubber-band — harmless even though we
        # rarely multi-select. Panning lives on the middle button + shift-
        # plus-left-button so the user still has a one-handed pan option;
        # both are wired up in ``mousePressEvent`` below.
        self.setDragMode(QtWidgets.QGraphicsView.DragMode.RubberBandDrag)
        self.setTransformationAnchor(
            QtWidgets.QGraphicsView.ViewportAnchor.AnchorUnderMouse
        )
        self.setResizeAnchor(
            QtWidgets.QGraphicsView.ViewportAnchor.AnchorUnderMouse
        )

        # Track whether we're currently in a manual pan (middle button or
        # shift-left button drag), and the screen-pixel point we started
        # from so each delta translates the scroll bars.
        self._panning = False
        self._pan_last_pos: Optional[QtCore.QPoint] = None

        # Pick-room mode: while True, the next left-click on a RoomItem is
        # converted into a ``room_picked`` signal instead of changing
        # selection. Clicks on labels or empty space cancel the mode and
        # fall through to normal behavior.
        self._room_pick_mode = False

        # Add-label mode: while True, the next left-click anywhere on the
        # canvas emits ``add_label_requested`` with the scene position.
        # Esc cancels without placement. Pick-room, add-label, and
        # add-room-flood modes are mutually exclusive — turning one on
        # turns the others off.
        self._add_label_mode = False

        # Add-room-flood mode: while True, the next left-click on the
        # canvas emits ``add_room_flood_requested`` with the scene
        # position. The controller runs a virtual flood-fill from that
        # point to build the room polygon. Esc cancels.
        self._add_room_flood_mode = False

        # Add-room-rect mode: while True, the next left-press starts a
        # rubber-band drag; the next left-release emits
        # ``add_room_rect_requested`` with the dragged rectangle (or
        # ``add_room_rect_cancelled`` if the drag was too small).
        # We draw our own preview rect overlay during the drag so the
        # user gets visual feedback regardless of the canvas's
        # ``RubberBandDrag`` selection rectangle (which we suppress
        # for this mode by intercepting press/move/release).
        self._add_room_rect_mode = False
        self._rect_drag_start_scene: Optional[QtCore.QPointF] = None
        self._rect_preview_item: Optional[QtWidgets.QGraphicsRectItem] = None

        # Track mouse position even when no button is pressed so the status
        # bar can show live image-pixel coords.
        self.setMouseTracking(True)

        self._pixmap_item: Optional[QtWidgets.QGraphicsPixmapItem] = None
        self._zoom: float = 1.0

        # Overlay item storage. Keyed by stable identifiers so we can
        # re-style or remove items without re-iterating the scene.
        # Label index = position in ``Calibration.labels`` (stable across
        # in-place edits because we never reorder labels in the data model;
        # structural changes like Add/Delete cause a label-only rebuild
        # via ``rebuild_labels()``).
        self._label_items: dict[int, LabelItem] = {}
        # Room id = ``Room.id`` (the stable integer assigned at calibration).
        self._room_items: dict[int, RoomItem] = {}

        # Reference to the live calibration so structural changes (Add/Delete
        # label) can rebuild the label items without the caller having to
        # re-pass the whole calibration each time.
        self._calibration: Optional[Calibration] = None

        # Layer visibility — separate from item visibility because the
        # "orphans only" filter also flips per-item visibility.
        self._labels_visible = True
        self._rooms_visible = True
        self._orphans_only = False

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

    # ----------------------------------------------------------- overlays

    def set_calibration(self, calibration: Calibration) -> None:
        """Build (or rebuild) all overlay items from a ``Calibration``.

        Tear-and-rebuild is the simplest correct approach for an initial
        load or after a major undo/redo step. Incremental updates for
        in-place edits are handled by the controller calling per-item
        style methods; structural changes (Add/Delete label) call
        :meth:`rebuild_labels` which rebuilds *just* the label items
        without redoing the expensive room polygon decode.
        """
        self._calibration = calibration
        # Clear existing overlay items but keep the pixmap.
        for item in list(self._label_items.values()):
            self._scene.removeItem(item)
        for item in list(self._room_items.values()):
            self._scene.removeItem(item)
        self._label_items.clear()
        self._room_items.clear()

        # Build rooms first so they paint below the labels.
        labels_by_room: dict[int, list] = {}
        for lab in calibration.labels:
            if lab.room_id is not None:
                labels_by_room.setdefault(lab.room_id, []).append(lab)

        # Decompressing room RLE masks is the startup bottleneck on real
        # calibrations (~30ms per room × hundreds of rooms). zlib + numpy
        # release the GIL during the work, so a small thread pool gives a
        # ~5× speedup with no concurrency hazards: ``QPolygonF`` is a value
        # type (not a ``QObject``) so it's safe to build from worker threads.
        rooms = list(calibration.rooms)
        with ThreadPoolExecutor() as pool:
            polygons = list(pool.map(build_room_polygon, rooms))

        for room, polygon in zip(rooms, polygons):
            if polygon is None:
                # Malformed RLE or empty mask — skip rather than crash.
                continue
            labeled = bool(labels_by_room.get(room.id))
            item = RoomItem(polygon, room=room, labeled=labeled)
            self._scene.addItem(item)
            self._room_items[room.id] = item

        self._build_label_items_from_current_calibration()

        # Re-apply current visibility settings to the new items.
        self._apply_visibility()

    def rebuild_labels(self) -> None:
        """Tear down and recreate all ``LabelItem``s from the current model.

        Cheap counterpart to :meth:`set_calibration` for structural label
        changes (Add/Delete): a label-only rebuild leaves the room
        polygons (and the expensive RLE decode that produced them) alone.

        After a rebuild, scene selection is empty (Qt drops selection on
        item removal); the caller is responsible for re-selecting any
        target label via :meth:`select_label` if appropriate.
        """
        if self._calibration is None:
            return
        for item in list(self._label_items.values()):
            self._scene.removeItem(item)
        self._label_items.clear()
        self._build_label_items_from_current_calibration()
        self._apply_visibility()

    def _build_label_items_from_current_calibration(self) -> None:
        """Internal helper: create one ``LabelItem`` per label in the model."""
        if self._calibration is None:
            return
        duplicate_ids = find_duplicate_label_ids(self._calibration.labels)
        for idx, lab in enumerate(self._calibration.labels):
            status = compute_label_status(lab, duplicate_ids)
            item = LabelItem(lab, idx, status=status)
            self._scene.addItem(item)
            self._label_items[idx] = item

    def rebuild_rooms(self) -> None:
        """Tear down and recreate all ``RoomItem``s from the current model.

        Counterpart to :meth:`rebuild_labels` for structural room changes
        (Add-room today; future delete-room / edit-polygon will reuse the
        same path). Re-decodes RLE polygons because the set of rooms just
        changed — but only for rooms; label items keep their existing
        positions and selection state.
        """
        if self._calibration is None:
            return
        for item in list(self._room_items.values()):
            self._scene.removeItem(item)
        self._room_items.clear()

        labels_by_room: dict[int, list] = {}
        for lab in self._calibration.labels:
            if lab.room_id is not None:
                labels_by_room.setdefault(lab.room_id, []).append(lab)

        rooms = list(self._calibration.rooms)
        # Match set_calibration's parallel polygon decode so add-room
        # after many rooms already exist still feels instantaneous.
        with ThreadPoolExecutor() as pool:
            polygons = list(pool.map(build_room_polygon, rooms))

        for room, polygon in zip(rooms, polygons):
            if polygon is None:
                continue
            labeled = bool(labels_by_room.get(room.id))
            item = RoomItem(polygon, room=room, labeled=labeled)
            self._scene.addItem(item)
            self._room_items[room.id] = item

        self._apply_visibility()

    def select_room(self, room_id: int) -> None:
        """Clear scene selection and select the ``RoomItem`` for ``room_id``.

        Used by the controller after add-room so the inspector immediately
        focuses the new room (which lets the user click "Create label for
        this room" right away). No-op if no item exists for that id.
        """
        item = self._room_items.get(room_id)
        scene = self.scene()
        scene.clearSelection()
        if item is not None:
            item.setSelected(True)
            self.ensureVisible(item)

    def select_label(self, label_index: int) -> None:
        """Clear scene selection and select the ``LabelItem`` for ``label_index``.

        Used by the controller after a structural change (Add: select the
        new label; Undo-Delete: select the resurrected label) to drive the
        ``selectionChanged`` signal that re-populates the inspector. No-op
        if no item exists for that index (e.g. just-deleted).
        """
        item = self._label_items.get(label_index)
        scene = self.scene()
        scene.clearSelection()
        if item is not None:
            item.setSelected(True)
            # Bring the new label into view if it's off-screen — easy to
            # miss otherwise after an add at scroll-distant coordinates.
            self.ensureVisible(item)

    def label_items(self) -> dict[int, LabelItem]:
        """Read-only view of the per-label overlay items, keyed by index."""
        return dict(self._label_items)

    def room_items(self) -> dict[int, RoomItem]:
        """Read-only view of the per-room overlay items, keyed by room id."""
        return dict(self._room_items)

    # ----------------------------------------------------------- toggles

    def set_labels_visible(self, visible: bool) -> None:
        self._labels_visible = visible
        self._apply_visibility()

    def set_rooms_visible(self, visible: bool) -> None:
        self._rooms_visible = visible
        self._apply_visibility()

    def set_orphans_only(self, on: bool) -> None:
        """When on, hide labels with rooms and rooms with labels.

        Lets the user focus only on the problems that need attention without
        having to dismiss the healthy items one by one.
        """
        self._orphans_only = on
        self._apply_visibility()

    def labels_visible(self) -> bool:
        return self._labels_visible

    def rooms_visible(self) -> bool:
        return self._rooms_visible

    def orphans_only(self) -> bool:
        return self._orphans_only

    # --------------------------------------------------------- pick-room

    def set_room_pick_mode(self, active: bool) -> None:
        """Enter or leave "click a room to link" mode.

        While active, the viewport shows a crosshair cursor as a visual
        cue and ``mousePressEvent`` intercepts left-clicks (see there).
        Idempotent: setting the same state twice is a no-op. Mutually
        exclusive with the three add-room / add-label modes — turning this
        on turns the others off.
        """
        if self._room_pick_mode == active:
            return
        if active:
            # Don't leave any other arm-modes hot at the same time.
            if self._add_label_mode:
                self.set_add_label_mode(False)
            if self._add_room_flood_mode:
                self.set_add_room_flood_mode(False)
            if self._add_room_rect_mode:
                self.set_add_room_rect_mode(False)
        self._room_pick_mode = active
        if active:
            self.viewport().setCursor(QtCore.Qt.CursorShape.CrossCursor)
            # Make sure Esc reaches our keyPressEvent: focus may have been
            # on the inspector button the user just clicked.
            self.setFocus(QtCore.Qt.FocusReason.OtherFocusReason)
        else:
            self.viewport().unsetCursor()

    def room_pick_mode(self) -> bool:
        return self._room_pick_mode

    def set_add_label_mode(self, active: bool) -> None:
        """Enter or leave "click on the map to add a label" mode.

        While active, the viewport shows a crosshair cursor and the next
        left-click anywhere on the canvas emits ``add_label_requested``
        with the scene position. Idempotent. Mutually exclusive with
        the pick-room and add-room modes.
        """
        if self._add_label_mode == active:
            return
        if active:
            if self._room_pick_mode:
                self.set_room_pick_mode(False)
            if self._add_room_flood_mode:
                self.set_add_room_flood_mode(False)
            if self._add_room_rect_mode:
                self.set_add_room_rect_mode(False)
        self._add_label_mode = active
        if active:
            self.viewport().setCursor(QtCore.Qt.CursorShape.CrossCursor)
            self.setFocus(QtCore.Qt.FocusReason.OtherFocusReason)
        else:
            self.viewport().unsetCursor()

    def add_label_mode(self) -> bool:
        return self._add_label_mode

    def set_add_room_flood_mode(self, active: bool) -> None:
        """Enter or leave "click on the map to flood-fill a new room" mode.

        While active, the viewport shows a crosshair cursor and the next
        left-click on the canvas emits ``add_room_flood_requested`` with
        the scene position. The controller does the actual flood-fill +
        room construction; the canvas is only responsible for the cursor,
        the click capture, and Esc cancellation.

        Idempotent. Mutually exclusive with the other three arm-modes.
        """
        if self._add_room_flood_mode == active:
            return
        if active:
            if self._room_pick_mode:
                self.set_room_pick_mode(False)
            if self._add_label_mode:
                self.set_add_label_mode(False)
            if self._add_room_rect_mode:
                self.set_add_room_rect_mode(False)
        self._add_room_flood_mode = active
        if active:
            self.viewport().setCursor(QtCore.Qt.CursorShape.CrossCursor)
            self.setFocus(QtCore.Qt.FocusReason.OtherFocusReason)
        else:
            self.viewport().unsetCursor()

    def add_room_flood_mode(self) -> bool:
        return self._add_room_flood_mode

    def set_add_room_rect_mode(self, active: bool) -> None:
        """Enter or leave "drag a rectangle to add a new room" mode.

        While active, the viewport shows a crosshair cursor and a left
        press-drag-release sequence draws a preview rectangle and then
        emits ``add_room_rect_requested`` with the dragged QRectF in
        scene coordinates. A press-then-release with no perceptible
        drag (< _RECT_MIN_DRAG_PIXELS in either dimension) emits
        ``add_room_rect_cancelled`` instead so a misclick doesn't
        produce a 1-pixel room.

        Idempotent. Mutually exclusive with the other three arm-modes.
        Disarming mid-drag tears down any preview rect overlay.
        """
        if self._add_room_rect_mode == active:
            return
        if active:
            if self._room_pick_mode:
                self.set_room_pick_mode(False)
            if self._add_label_mode:
                self.set_add_label_mode(False)
            if self._add_room_flood_mode:
                self.set_add_room_flood_mode(False)
        self._add_room_rect_mode = active
        if active:
            self.viewport().setCursor(QtCore.Qt.CursorShape.CrossCursor)
            self.setFocus(QtCore.Qt.FocusReason.OtherFocusReason)
        else:
            self.viewport().unsetCursor()
            # If we were mid-drag, tear down the preview cleanly.
            self._discard_rect_preview()

    def add_room_rect_mode(self) -> bool:
        return self._add_room_rect_mode

    def image_size(self) -> Optional[tuple[int, int]]:
        """Return ``(width, height)`` of the loaded map in image pixels.

        ``None`` if no map has been loaded yet. Used by the controller to
        size the full-image mask for rectangle-based add-room without
        having to re-read the source image file.
        """
        if self._pixmap_item is None:
            return None
        pixmap = self._pixmap_item.pixmap()
        return (pixmap.width(), pixmap.height())

    def _discard_rect_preview(self) -> None:
        """Tear down any in-progress rect-drag preview item.

        Called when add-room-rect mode is turned off mid-drag, when Esc
        is hit during a drag, and after a successful emit. Idempotent.
        """
        if self._rect_preview_item is not None:
            try:
                self._scene.removeItem(self._rect_preview_item)
            except RuntimeError:
                # Scene already torn down — fine, the item is gone too.
                pass
            self._rect_preview_item = None
        self._rect_drag_start_scene = None

    def room_at_scene_pos(self, scene_pos: QtCore.QPointF) -> Optional[RoomItem]:
        """Return the topmost ``RoomItem`` containing ``scene_pos``, if any.

        Used by add-label to auto-link a new label to the room polygon the
        user clicked inside. Iterates ``scene.items(scene_pos)`` (which is
        already z-ordered, topmost first) and returns the first ``RoomItem``
        whose polygon contains the point. Returns ``None`` if the click is
        in a hallway / outside any room.
        """
        for item in self.scene().items(scene_pos):
            if isinstance(item, RoomItem):
                # scene.items already filters by bounding rect; verify the
                # actual polygon shape so concave rooms don't false-positive
                # on a click in their bbox-but-outside-polygon corner.
                if item.contains(item.mapFromScene(scene_pos)):
                    return item
        return None

    def _apply_visibility(self) -> None:
        for item in self._label_items.values():
            base = self._labels_visible
            if self._orphans_only and item.status == LabelItem.STATUS_LINKED:
                base = False
            item.setVisible(base)
        for item in self._room_items.values():
            base = self._rooms_visible
            if self._orphans_only and item.labeled:
                base = False
            item.setVisible(base)

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

        if self._panning and self._pan_last_pos is not None:
            # Translate scroll bars by the cursor delta. Using viewport
            # coordinates (not scene) keeps the pan responsive at any zoom.
            here = event.position().toPoint()
            delta = here - self._pan_last_pos
            self._pan_last_pos = here
            h_bar = self.horizontalScrollBar()
            v_bar = self.verticalScrollBar()
            h_bar.setValue(h_bar.value() - delta.x())
            v_bar.setValue(v_bar.value() - delta.y())
            event.accept()
            return

        if (
            self._add_room_rect_mode
            and self._rect_drag_start_scene is not None
            and self._rect_preview_item is not None
        ):
            # Update the live preview rectangle. ``QRectF`` from two
            # corners normalizes width/height to positive automatically.
            rect = QtCore.QRectF(self._rect_drag_start_scene, scene_pos).normalized()
            self._rect_preview_item.setRect(rect)
            event.accept()
            return

        super().mouseMoveEvent(event)

    def mousePressEvent(self, event: QtGui.QMouseEvent) -> None:  # noqa: N802 — Qt API
        """Intercept middle-button + shift-left for a manual pan gesture.

        Plain left-click falls through to the base class so ``RubberBandDrag``
        + ``ItemIsSelectable`` give the user item selection. Middle button
        and shift-left start a one-handed scroll-by-drag.

        Pick-room mode (set via :meth:`set_room_pick_mode`) intercepts left
        clicks before they reach the scene: clicking a ``RoomItem`` emits
        ``room_picked``; clicking a ``LabelItem`` cancels pick mode and
        falls through to normal selection; clicking empty space cancels
        pick mode silently.
        """
        is_pan_trigger = (
            event.button() == QtCore.Qt.MouseButton.MiddleButton
            or (
                event.button() == QtCore.Qt.MouseButton.LeftButton
                and event.modifiers() & QtCore.Qt.KeyboardModifier.ShiftModifier
            )
        )
        if is_pan_trigger:
            self._panning = True
            self._pan_last_pos = event.position().toPoint()
            self.viewport().setCursor(QtCore.Qt.CursorShape.ClosedHandCursor)
            event.accept()
            return

        if (
            self._add_room_flood_mode
            and event.button() == QtCore.Qt.MouseButton.LeftButton
        ):
            scene_pos = self.mapToScene(event.position().toPoint())
            # Disarm before emitting: the controller will pop a dialog or
            # status message on rejection, and the cleanest UX is for the
            # mode to end on a single click (success or fail). The user
            # re-arms via the menu / shortcut to add another room.
            self.set_add_room_flood_mode(False)
            self.add_room_flood_requested.emit(scene_pos)
            event.accept()
            return

        if (
            self._add_room_rect_mode
            and event.button() == QtCore.Qt.MouseButton.LeftButton
        ):
            # Start a rubber-band rectangle drag. Don't disarm yet —
            # we need the matching mouseMove + mouseRelease to finish
            # the gesture. ``mouseReleaseEvent`` is responsible for
            # disarming and emitting (or cancelling).
            scene_pos = self.mapToScene(event.position().toPoint())
            self._rect_drag_start_scene = scene_pos
            preview = QtWidgets.QGraphicsRectItem(QtCore.QRectF(scene_pos, scene_pos))
            pen = QtGui.QPen(QtGui.QColor("#0078d4"))
            pen.setStyle(QtCore.Qt.PenStyle.DashLine)
            # Cosmetic pen keeps the dashed outline 1 px wide regardless
            # of zoom — otherwise dashes shrink to invisibility at high
            # zoom and bloat into a band at low zoom.
            pen.setCosmetic(True)
            pen.setWidth(2)
            preview.setPen(pen)
            preview.setBrush(QtGui.QBrush(QtGui.QColor(0, 120, 212, 40)))
            preview.setZValue(1000)
            self._scene.addItem(preview)
            self._rect_preview_item = preview
            event.accept()
            return

        if (
            self._add_label_mode
            and event.button() == QtCore.Qt.MouseButton.LeftButton
        ):
            scene_pos = self.mapToScene(event.position().toPoint())
            # Disarm before emitting so the controller's handler (which
            # may pop a dialog) sees the canvas already out of add mode.
            self.set_add_label_mode(False)
            self.add_label_requested.emit(scene_pos)
            event.accept()
            return

        if (
            self._room_pick_mode
            and event.button() == QtCore.Qt.MouseButton.LeftButton
        ):
            viewport_pos = event.position().toPoint()
            # Gather every item under the cursor so a room hidden behind a
            # label box can still be picked. ``items()`` returns topmost
            # first; filtering by type then picks the right one regardless
            # of stacking order.
            items_here = self.items(viewport_pos)
            room_at = next(
                (it for it in items_here if isinstance(it, RoomItem)), None
            )
            if room_at is not None:
                # Disarm before emitting so the controller's handler sees
                # the canvas already out of pick mode (idempotent / clean).
                self.set_room_pick_mode(False)
                self.room_picked.emit(room_at.room.id)
                event.accept()
                return
            label_at = next(
                (it for it in items_here if isinstance(it, LabelItem)), None
            )
            self.set_room_pick_mode(False)
            self.room_pick_cancelled.emit()
            if label_at is None:
                # Empty space — don't deselect or rubber-band; the user was
                # mid-task and the click was just a miss.
                event.accept()
                return
            # Fell on a label: cancel pick mode but let Qt's default
            # selection handling run so clicking another label still works.

        super().mousePressEvent(event)

    def mouseReleaseEvent(self, event: QtGui.QMouseEvent) -> None:  # noqa: N802 — Qt API
        if self._panning and event.button() in (
            QtCore.Qt.MouseButton.MiddleButton,
            QtCore.Qt.MouseButton.LeftButton,
        ):
            self._panning = False
            self._pan_last_pos = None
            self.viewport().unsetCursor()
            event.accept()
            return

        if (
            self._add_room_rect_mode
            and event.button() == QtCore.Qt.MouseButton.LeftButton
            and self._rect_drag_start_scene is not None
        ):
            # Compute the final rect (normalized) and decide whether the
            # gesture was a real drag or a misclick. Either way, disarm
            # the mode + tear down the preview before any signal fires
            # so handlers see a clean canvas state.
            end_scene = self.mapToScene(event.position().toPoint())
            rect = QtCore.QRectF(self._rect_drag_start_scene, end_scene).normalized()
            self.set_add_room_rect_mode(False)
            # set_add_room_rect_mode(False) already calls
            # _discard_rect_preview, which clears _rect_drag_start_scene
            # and removes the preview item.
            if (
                rect.width() < _RECT_MIN_DRAG_PIXELS
                or rect.height() < _RECT_MIN_DRAG_PIXELS
            ):
                self.add_room_rect_cancelled.emit()
            else:
                self.add_room_rect_requested.emit(rect)
            event.accept()
            return

        super().mouseReleaseEvent(event)

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
        if key == QtCore.Qt.Key.Key_Escape and self._room_pick_mode:
            # Esc bails out of pick mode without picking anything. The
            # controller listens for ``room_pick_cancelled`` to uncheck the
            # inspector button.
            self.set_room_pick_mode(False)
            self.room_pick_cancelled.emit()
            event.accept()
            return
        if key == QtCore.Qt.Key.Key_Escape and self._add_label_mode:
            # Esc bails out of add-label mode without placing a label.
            # The toolbar action listens for ``add_label_cancelled`` to
            # un-check itself.
            self.set_add_label_mode(False)
            self.add_label_cancelled.emit()
            event.accept()
            return
        if key == QtCore.Qt.Key.Key_Escape and self._add_room_flood_mode:
            # Esc bails out of add-room-flood mode without placing a room.
            self.set_add_room_flood_mode(False)
            self.add_room_flood_cancelled.emit()
            event.accept()
            return
        if key == QtCore.Qt.Key.Key_Escape and self._add_room_rect_mode:
            # Esc bails out of add-room-rect mode. If a drag is in
            # progress, the preview is torn down by
            # ``set_add_room_rect_mode(False)`` so no stray rectangle
            # is left on the scene.
            self.set_add_room_rect_mode(False)
            self.add_room_rect_cancelled.emit()
            event.accept()
            return
        super().keyPressEvent(event)

