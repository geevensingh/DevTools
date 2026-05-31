"""Editor controller: glues the canvas, inspector, and undo stack together.

Why a controller module rather than putting this logic in ``EditorMainWindow``:

* The main window is the *Qt* shell — it owns menus, the dock, the status bar.
  Once it's longer than 200 lines it stops being readable.
* The controller owns the *behavior* — what happens when the user selects
  an item, when the inspector emits a change, when the user hits Ctrl+S.
* Save-with-backup is a pure file-system function that's worth unit-testing
  without instantiating any Qt widgets.

Most of the controller is intentionally thin; it just connects signals. The
real logic lives in the commands (``commands.py``) and the inspector
widget (``sidebar.py``).
"""

from __future__ import annotations

import os
import shutil
from pathlib import Path
from typing import Optional

from PySide6 import QtCore, QtGui, QtWidgets

from ..calibration import Calibration, Label, Room, save_calibration
from ..geometry import mask_to_rle, pole_of_inaccessibility, rle_to_mask
from .canvas import MapCanvas
from .commands import (
    AddLabelCommand,
    AddRoomCommand,
    AddWallPatchCommand,
    ChangeRoomLinkCommand,
    DeleteLabelCommand,
    DeleteRoomCommand,
    EditLabelIdCommand,
    EditLabelNotesCommand,
    LabelChange,
    RoomChange,
    WallPatchChange,
)
from .items import LabelItem, RoomItem, compute_label_status, find_duplicate_label_ids
from .sidebar import InspectorPanel


# Default size (px) of the bbox we draw for a user-added label. The number
# is irrelevant downstream — only the label's ``fill_seed`` and ``room_id``
# matter for fill/render — but the box is visible in the editor so it
# needs a reasonable size. Roughly matches a typical 4-digit Tesseract
# bbox at the maps we work with.
_NEW_LABEL_BBOX_W = 48
_NEW_LABEL_BBOX_H = 18


# Upper bound on a single flood-fill's area as a fraction of the full map.
# A typical office on a Millennium B-scale map is well under 1% of the
# map's pixel area, so anything above this threshold means the flood
# escaped the room (the click was on the wrong side of a wall gap, or
# the user clicked a hallway). Reject with a friendly message instead of
# silently creating a bogus full-map room.
_ADD_ROOM_FLOOD_MAX_FRACTION = 0.30

# Below this many pixels we refuse to create a room: real offices are
# always > a couple hundred pixels at usable map resolutions, and tiny
# slivers are almost always the "click landed in a single-pixel pocket
# between walls" misclick.
_ADD_ROOM_FLOOD_MIN_PIXELS = 50

# Smallest acceptable rectangle (in pixels) for add-room-rect. Smaller
# than this is almost certainly a misclick / accidental shake; rooms
# on real maps are always significantly larger. Same threshold as the
# flood-fill minimum so both add-room modes feel consistent.
_ADD_ROOM_RECT_MIN_PIXELS = 50

# Smallest acceptable polygon area for add-room-polygon. Same threshold
# as rect / flood so all three add-room modes reject the same set of
# "too small to be a room" drafts.
_ADD_ROOM_POLYGON_MIN_PIXELS = 50

# Minimum vertex count for a polygon close. The canvas enforces this
# at the gesture level too, but the controller re-validates so a
# programmatic / scripted caller can't slip a degenerate polygon
# past the safety net.
_ADD_ROOM_POLYGON_MIN_VERTICES = 3


# ---------------------------------------------------------------------------
# Save with backup
# ---------------------------------------------------------------------------


def save_calibration_with_backup(calibration: Calibration, path: Path) -> Optional[Path]:
    """Persist ``calibration`` to ``path`` after backing up the previous file.

    If ``path`` already exists, it's copied to ``path.bak`` first (overwriting
    any older .bak — one level of history is enough; users keep version
    control for anything beyond that).

    The write itself goes via a sibling temp file + ``os.replace`` so a crash
    mid-write can never leave a half-written ``calibration.json`` on disk.

    Returns:
        The backup path if one was written, ``None`` if there was nothing
        to back up (first-time save). Useful for telling the user where their
        previous file went.
    """
    backup_path: Optional[Path] = None
    if path.exists():
        backup_path = path.with_suffix(path.suffix + ".bak")
        # ``copy2`` preserves mtime so the .bak's timestamp reflects when the
        # original was last saved, not when this backup was written.
        shutil.copy2(path, backup_path)

    tmp_path = path.with_suffix(path.suffix + ".tmp")
    try:
        save_calibration(calibration, tmp_path)
        # os.replace is atomic on the same filesystem (POSIX + NTFS both).
        os.replace(tmp_path, path)
    except Exception:
        # Clean up the temp file on failure so we don't leave litter behind.
        if tmp_path.exists():
            try:
                tmp_path.unlink()
            except OSError:
                pass
        raise

    return backup_path


# ---------------------------------------------------------------------------
# EditorController
# ---------------------------------------------------------------------------


class EditorController(QtCore.QObject):
    """Owns the undo stack and the wires between canvas, inspector, and save.

    Lifecycle: created by ``EditorMainWindow`` after the canvas + inspector
    exist. The controller is a child of the main window so Qt cleans it up
    on close.

    Signals:
        dirty_changed(bool): emitted when the undo stack moves into or out of
            its clean state (matches the most recent save). The main window
            uses this to flip the title bar's modified marker.
    """

    dirty_changed = QtCore.Signal(bool)

    def __init__(
        self,
        *,
        calibration: Calibration,
        calibration_path: Path,
        canvas: MapCanvas,
        inspector: InspectorPanel,
        map_path: Optional[Path] = None,
        parent: Optional[QtCore.QObject] = None,
    ) -> None:
        super().__init__(parent)
        self._cal = calibration
        self._calibration_path = calibration_path
        self._canvas = canvas
        self._inspector = inspector
        # Path to the source map image. Only needed for tools that
        # re-binarize the image (add-room flood-fill). Stored as
        # ``Optional`` so unit tests can spin up a controller without
        # needing a real map on disk; tools that require it pop a
        # graceful error when it's missing rather than crashing.
        self._map_path = map_path

        # Lazy-built wall mask for flood-fill (cv2 + adaptive threshold +
        # wall_patches rasterization). Built the first time an add-room
        # flood click happens, then reused for subsequent clicks within
        # the session. None means "not built yet"; a numpy array means
        # "ready". We intentionally do not invalidate on every edit:
        # `wall_patches` is the only thing that affects the mask and the
        # editor doesn't have a UI for editing it (yet) so within a
        # single session the mask is stable. If a future feature mutates
        # ``calibration.wall_patches`` it should call
        # :meth:`invalidate_wall_mask` to drop the cache.
        self._wall_mask_cache = None

        self._undo_stack = QtGui.QUndoStack(self)
        self._undo_stack.cleanChanged.connect(self._on_clean_changed)
        # Mark the empty stack as our save baseline; saving later will move
        # the "clean" mark to the current index.
        self._undo_stack.setClean()

        # Inspector → controller: turn user-driven field edits into commands.
        self._inspector.label_id_changed.connect(self._handle_label_id_changed)
        self._inspector.label_notes_changed.connect(self._handle_label_notes_changed)
        self._inspector.label_room_changed.connect(self._handle_label_room_changed)
        self._inspector.room_pick_requested.connect(self._handle_room_pick_requested)
        self._inspector.create_label_for_room.connect(
            self._handle_create_label_for_room
        )

        # Canvas selection → inspector: populate the panel when the user
        # clicks an item. Qt emits ``selectionChanged`` from the scene.
        self._canvas.scene().selectionChanged.connect(self._handle_selection_changed)

        # Canvas pick-room mode → controller: convert the picked room into a
        # ChangeRoomLinkCommand (so the link is undoable), and uncheck the
        # inspector button when the canvas drops out of pick mode for any
        # reason (user picked, Esc, clicked empty space).
        self._canvas.room_picked.connect(self._handle_canvas_room_picked)
        self._canvas.room_pick_cancelled.connect(self._handle_canvas_pick_cancelled)

        # Canvas add-label mode → controller: convert a click in the canvas
        # into a prompt-then-AddLabelCommand sequence.
        self._canvas.add_label_requested.connect(self._handle_canvas_add_label_at)

        # Canvas add-room-flood mode → controller: convert a click into a
        # virtual flood-fill against the binarized map (+ wall_patches),
        # build a Room from the filled mask, and push AddRoomCommand.
        self._canvas.add_room_flood_requested.connect(
            self._handle_canvas_add_room_flood
        )
        self._canvas.add_room_flood_cancelled.connect(
            self._handle_canvas_add_room_flood_cancelled
        )

        # Canvas add-room-rect mode → controller: convert a dragged
        # rectangle into a Room whose polygon IS the rectangle and
        # push AddRoomCommand. No flood-fill / image binarization
        # needed, so this mode works even without a map_path.
        self._canvas.add_room_rect_requested.connect(
            self._handle_canvas_add_room_rect
        )
        self._canvas.add_room_rect_cancelled.connect(
            self._handle_canvas_add_room_rect_cancelled
        )

        # Canvas add-room-polygon mode → controller: rasterize the
        # placed-vertex polygon into a full-image mask via PIL
        # ImageDraw, build a Room, and push AddRoomCommand. Same
        # "no map_path needed" property as rect mode — the editor's
        # loaded pixmap supplies the image dimensions.
        self._canvas.add_room_polygon_requested.connect(
            self._handle_canvas_add_room_polygon
        )
        self._canvas.add_room_polygon_cancelled.connect(
            self._handle_canvas_add_room_polygon_cancelled
        )

        # Canvas add-wall-patch mode → controller: clamp the two
        # endpoints to the image bounds, reject degenerate (zero-length
        # or entirely-outside) segments, push AddWallPatchCommand, and
        # invalidate the wall mask so the next flood-fill picks up the
        # new patch. No map_path needed — wall patches are pure
        # geometry that gets drawn onto whatever mask we build later.
        self._canvas.add_wall_patch_requested.connect(
            self._handle_canvas_add_wall_patch
        )
        self._canvas.add_wall_patch_cancelled.connect(
            self._handle_canvas_add_wall_patch_cancelled
        )

        # Remember which label the user was editing when they entered pick
        # mode. Selection is locked while pick mode is active (clicks on
        # rooms are intercepted before they change selection) but the user
        # can still trigger pick mode for the wrong label if they're fast,
        # so the controller stores the index explicitly.
        self._pick_room_for_label: Optional[int] = None

        # Default empty state on the inspector.
        self._inspector.show_nothing()

    # --------------------------------------------------------------- API

    def undo_stack(self) -> QtGui.QUndoStack:
        """Expose the stack so the main window can add Edit menu actions."""
        return self._undo_stack

    def is_dirty(self) -> bool:
        return not self._undo_stack.isClean()

    def save(self) -> Optional[Path]:
        """Write the current calibration to disk, marking the stack clean."""
        backup = save_calibration_with_backup(self._cal, self._calibration_path)
        self._undo_stack.setClean()
        return backup

    # ----------------------------------------------------- selection

    def _handle_selection_changed(self) -> None:
        """Show the selected item in the inspector (label > room priority).

        If the user has multi-selected (rare with the current drag-mode but
        possible) we just show the first selected item — multi-edit comes
        later if ever needed.
        """
        try:
            selected = self._canvas.scene().selectedItems()
        except RuntimeError:
            # Canvas already torn down (window closing). The signal can
            # still fire once during teardown; just drop it silently.
            return
        # Prefer labels over rooms when both are under the cursor — labels
        # are smaller (so a label-on-room click usually means "I wanted the
        # label").
        label_item = next((it for it in selected if isinstance(it, LabelItem)), None)
        room_item = next((it for it in selected if isinstance(it, RoomItem)), None)

        if label_item is not None:
            available_room_ids = sorted(room.id for room in self._cal.rooms)
            self._inspector.show_label(
                label=label_item.label,
                label_index=label_item.label_index,
                available_room_ids=available_room_ids,
            )
        elif room_item is not None:
            room = room_item.room
            labels_here = [
                lab for lab in self._cal.labels if lab.room_id == room.id
            ]
            self._inspector.show_room(room=room, labels=labels_here)
        else:
            self._inspector.show_nothing()

    # ------------------------------------------------------- commands

    def _handle_label_id_changed(self, label_index: int, new_id: str) -> None:
        if not (0 <= label_index < len(self._cal.labels)):
            return
        if self._cal.labels[label_index].id == new_id:
            return
        cmd = EditLabelIdCommand(
            self._cal, label_index, new_id, on_change=self._on_label_change
        )
        self._undo_stack.push(cmd)

    def _handle_label_notes_changed(self, label_index: int, new_notes: str) -> None:
        if not (0 <= label_index < len(self._cal.labels)):
            return
        if self._cal.labels[label_index].notes == new_notes:
            return
        cmd = EditLabelNotesCommand(
            self._cal, label_index, new_notes, on_change=self._on_label_change
        )
        self._undo_stack.push(cmd)

    def _handle_label_room_changed(
        self, label_index: int, new_room_id: Optional[int]
    ) -> None:
        if not (0 <= label_index < len(self._cal.labels)):
            return
        if self._cal.labels[label_index].room_id == new_room_id:
            return
        cmd = ChangeRoomLinkCommand(
            self._cal, label_index, new_room_id, on_change=self._on_label_change
        )
        self._undo_stack.push(cmd)

    # ------------------------------------------------- pick-room mode

    def _handle_room_pick_requested(
        self, label_index: int, active: bool
    ) -> None:
        """Translate the inspector's pick button into a canvas mode change.

        When the user clicks the button (active=True), remember which label
        they were editing and put the canvas into pick-room mode. When they
        click it again (active=False) — or some other path turns it off —
        leave pick mode without doing anything else.
        """
        if active:
            if not (0 <= label_index < len(self._cal.labels)):
                # Stale label index — silently disarm.
                self._inspector.set_room_pick_active(False)
                return
            self._pick_room_for_label = label_index
            self._canvas.set_room_pick_mode(True)
        else:
            self._pick_room_for_label = None
            self._canvas.set_room_pick_mode(False)

    def _handle_canvas_room_picked(self, room_id: int) -> None:
        """Apply the user's visual room pick as an undoable command."""
        label_index = self._pick_room_for_label
        self._pick_room_for_label = None
        # Authoritative cleanup. The canvas's mouse handler also disarms
        # itself before emitting, but the controller doing it too is cheap,
        # idempotent, and means any future code path that emits this signal
        # (e.g. a hypothetical "pick from keyboard" shortcut) doesn't need
        # to remember the cleanup.
        self._canvas.set_room_pick_mode(False)
        self._inspector.set_room_pick_active(False)
        if label_index is None:
            return
        if not (0 <= label_index < len(self._cal.labels)):
            return
        if self._cal.labels[label_index].room_id == room_id:
            # Already linked to this room — nothing to do, no need to push
            # a no-op command onto the undo stack.
            return
        cmd = ChangeRoomLinkCommand(
            self._cal, label_index, room_id, on_change=self._on_label_change
        )
        self._undo_stack.push(cmd)

    def _handle_canvas_pick_cancelled(self) -> None:
        """Sync the inspector button after Esc / clicked-empty-space."""
        self._pick_room_for_label = None
        self._canvas.set_room_pick_mode(False)
        self._inspector.set_room_pick_active(False)

    # ------------------------------------------------- add / delete label

    def _handle_canvas_add_label_at(self, scene_pos: "QtCore.QPointF") -> None:
        """User clicked the canvas in add-label mode → prompt + AddLabelCommand.

        Hit-tests the click point against room polygons so a click inside
        a room auto-links the new label to that room (the common case).
        A click in a hallway / outside any room creates an orphan label
        the user can link later via the room picker.
        """
        # Defensive: the canvas's mousePressEvent disarms add mode before
        # emitting, but make sure here too so any future emit path is safe.
        self._canvas.set_add_label_mode(False)

        x = int(round(scene_pos.x()))
        y = int(round(scene_pos.y()))

        room_item = self._canvas.room_at_scene_pos(scene_pos)
        room_id = room_item.room.id if room_item is not None else None

        prompt = "Label id (e.g. office number):"
        if room_id is not None:
            prompt += f"\n(will link to room {room_id})"
        else:
            prompt += "\n(no room polygon under this click — will be an orphan)"

        new_id, ok = QtWidgets.QInputDialog.getText(
            self._canvas, "Add label", prompt, text=""
        )
        if not ok:
            return
        new_id = new_id.strip()
        if not new_id:
            # Empty id = silently cancel rather than create a broken label.
            return

        # Center the bbox on the click so the visual rectangle stays
        # underneath the user's cursor — easy to find after placement.
        bx = x - _NEW_LABEL_BBOX_W // 2
        by = y - _NEW_LABEL_BBOX_H // 2
        new_label = Label(
            id=new_id,
            bbox=(bx, by, _NEW_LABEL_BBOX_W, _NEW_LABEL_BBOX_H),
            room_id=room_id,
            fill_seed=(x, y),
            # User-placed labels are "perfectly confident" by definition;
            # they didn't come from OCR. Setting 1.0 also prevents these
            # from cluttering low-confidence review pages in the PDF.
            ocr_confidence=1.0,
            notes="",
        )
        cmd = AddLabelCommand(
            self._cal, new_label, on_change=self._on_label_change
        )
        self._undo_stack.push(cmd)

    def _handle_create_label_for_room(self, room_id: int) -> None:
        """Inspector "Create label for this room" → prompt + AddLabelCommand.

        Same dataflow as :meth:`_handle_canvas_add_label_at` but seeded
        from a room id instead of an arbitrary click position; the new
        label is centered on the room's bbox center (good-enough default
        — most rooms are bbox-convex; the user can drag the seed later).
        """
        room = next((r for r in self._cal.rooms if r.id == room_id), None)
        if room is None:
            # Stale signal (room polygon vanished from under us — shouldn't
            # happen but defensive coding doesn't hurt).
            return

        new_id, ok = QtWidgets.QInputDialog.getText(
            self._canvas,
            "Create label for this room",
            f"Label id for room {room_id}:",
            text="",
        )
        if not ok:
            return
        new_id = new_id.strip()
        if not new_id:
            return

        cx = room.bbox[0] + room.bbox[2] // 2
        cy = room.bbox[1] + room.bbox[3] // 2
        # Prefer the pole of inaccessibility (deepest interior pixel) over
        # the bbox center for the flood-fill seed: bbox center frequently
        # lands on a wall for concave rooms and on a glyph for rooms with
        # embedded text. Fall back to (cx, cy) if the polygon is empty.
        try:
            room_mask = rle_to_mask(room.polygon_rle)
            seed = pole_of_inaccessibility(room_mask) or (cx, cy)
        except Exception:  # noqa: BLE001 — defensive: bad RLE shouldn't crash add-label
            seed = (cx, cy)
        bx = cx - _NEW_LABEL_BBOX_W // 2
        by = cy - _NEW_LABEL_BBOX_H // 2
        new_label = Label(
            id=new_id,
            bbox=(bx, by, _NEW_LABEL_BBOX_W, _NEW_LABEL_BBOX_H),
            room_id=room_id,
            fill_seed=seed,
            ocr_confidence=1.0,
            notes="",
        )
        cmd = AddLabelCommand(
            self._cal, new_label, on_change=self._on_label_change
        )
        self._undo_stack.push(cmd)

    # --------------------------------------------------- add room (flood)

    def invalidate_wall_mask(self) -> None:
        """Drop any cached wall mask (next add-room click rebuilds it).

        Call this if anything that affects the binarized map changes —
        most importantly ``calibration.wall_patches``. The editor itself
        doesn't yet have a UI for editing wall_patches, so this is
        currently unreachable from user actions, but it's the public
        invalidation hook future code should use.
        """
        self._wall_mask_cache = None

    def _get_wall_mask(self):
        """Return (and cache) the binary wall mask used for flood-fills.

        Lazy: only loaded the first time an add-room flood click happens
        so the editor's startup cost stays just the calibration decode.
        Returns ``None`` if the map can't be loaded (missing file, unsupported
        format, no ``map_path`` configured); the caller is responsible for
        showing the user an error in that case.
        """
        if self._wall_mask_cache is not None:
            return self._wall_mask_cache
        if self._map_path is None or not Path(self._map_path).exists():
            return None
        # Import lazily so unit tests that don't use add-room don't pay
        # the cv2 import cost (and so the import failure surfaces only
        # when the feature is actually used).
        import cv2  # noqa: WPS433 — intentional lazy import

        from ..validate import build_fill_mask

        image = cv2.imread(str(self._map_path), cv2.IMREAD_COLOR)
        if image is None:
            return None
        self._wall_mask_cache = build_fill_mask(image, self._cal.wall_patches)
        return self._wall_mask_cache

    def set_add_room_flood_mode(self, active: bool) -> None:
        """Public toggle for the canvas's add-room-flood mode.

        Routes through the controller so the menu action and the canvas
        stay in sync, and so we can short-circuit with a friendly error
        if the feature can't run (no map path configured).
        """
        if active and self._map_path is None:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "Add-room mode needs the source map image to do the\n"
                "flood-fill, but no map path was supplied when the editor\n"
                "was launched. Re-open the editor with --map MAP.png.",
            )
            return
        self._canvas.set_add_room_flood_mode(active)

    def _handle_canvas_add_room_flood(
        self, scene_pos: "QtCore.QPointF"
    ) -> None:
        """User clicked the canvas in add-room-flood mode.

        Runs a virtual flood-fill from the click point against the
        binarized map (+ wall_patches), builds a Room from the filled
        mask, and pushes :class:`AddRoomCommand`. The new room is
        selected so the inspector immediately offers "Create label for
        this room".

        Rejects with a status-bar / dialog message when:

        * the seed point is on a wall (flood-fill returns empty),
        * the filled region is implausibly small (< _MIN_PIXELS),
        * the filled region covers more than _MAX_FRACTION of the map
          (the click leaked into a hallway or escaped the wall),
        * the map image can't be loaded.
        """
        # Defensive: the canvas disarms before emitting, but make sure.
        self._canvas.set_add_room_flood_mode(False)

        wall_mask = self._get_wall_mask()
        if wall_mask is None:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "Could not load the source map image for flood-fill.\n"
                "Make sure the map file exists and the editor was launched\n"
                "with --map MAP.png.",
            )
            return

        h, w = wall_mask.shape[:2]
        x = int(round(scene_pos.x()))
        y = int(round(scene_pos.y()))
        if not (0 <= x < w and 0 <= y < h):
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "Click was outside the map image. Click somewhere inside\n"
                "the room you want to add.",
            )
            return

        from ..validate import virtual_flood_fill  # lazy

        filled = virtual_flood_fill(wall_mask, (x, y))
        area_px = int(filled.sum())

        if area_px < _ADD_ROOM_FLOOD_MIN_PIXELS:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "The click landed on a wall (or in a tiny pocket of\n"
                "background). Click a clear spot well inside the room.",
            )
            return

        max_pixels = int(_ADD_ROOM_FLOOD_MAX_FRACTION * h * w)
        if area_px > max_pixels:
            pct = 100.0 * area_px / float(h * w)
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                f"Flood-fill from that point covered {pct:.0f}% of the map\n"
                f"(threshold: {int(_ADD_ROOM_FLOOD_MAX_FRACTION * 100)}%).\n\n"
                "That usually means the room has a wall gap and the fill\n"
                "leaked into a hallway. Add a wall_patches entry to plug\n"
                "the gap in calibration.json, reload, and try again.",
            )
            return

        # Compute bbox of filled pixels for Room.bbox.
        ys, xs = filled.nonzero()
        x_min, x_max = int(xs.min()), int(xs.max())
        y_min, y_max = int(ys.min()), int(ys.max())
        bbox = (x_min, y_min, x_max - x_min + 1, y_max - y_min + 1)

        # mask_to_rle expects a uint8 mask with values in {0, 1} (or 0/255 —
        # any nonzero is treated as foreground). filled is bool; cast.
        rle = mask_to_rle(filled.astype("uint8"))

        new_id = self._next_room_id()
        new_room = Room(id=new_id, polygon_rle=rle, area_px=area_px, bbox=bbox)
        cmd = AddRoomCommand(
            self._cal, new_room, on_change=self._on_room_change
        )
        self._undo_stack.push(cmd)

    def _handle_canvas_add_room_flood_cancelled(self) -> None:
        """Esc out of add-room-flood mode — purely informational hook.

        The canvas already cleared the cursor; nothing else to do at the
        controller level today. Kept as a named slot so future status-bar
        / toolbar wiring has a place to hang off.
        """
        return

    # --------------------------------------------------- add room (rect)

    def set_add_room_rect_mode(self, active: bool) -> None:
        """Public toggle for the canvas's add-room-rect mode.

        Unlike the flood-fill mode, this one doesn't need the source
        image (the rectangle IS the geometry) so there's no map_path
        guard. We do still need a loaded pixmap to know the image
        dimensions for the full-size mask; if no map is loaded we
        refuse to arm and warn.
        """
        if active and self._canvas.image_size() is None:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "Add-room mode needs the map image to be loaded so the\n"
                "new room's polygon can be sized to match. Re-open the\n"
                "editor with --map MAP.png.",
            )
            return
        self._canvas.set_add_room_rect_mode(active)

    def _handle_canvas_add_room_rect(self, rect: "QtCore.QRectF") -> None:
        """User finished a rubber-band drag in add-room-rect mode.

        Clamps the rectangle to the image bounds, validates that what
        remains is large enough to plausibly be a room, builds a Room
        with a full-image-sized mask (matching the convention used by
        :meth:`_handle_canvas_add_room_flood`), and pushes an
        :class:`AddRoomCommand` so the action is undoable.
        """
        # Defensive: the canvas disarms before emitting, but be sure.
        self._canvas.set_add_room_rect_mode(False)

        size = self._canvas.image_size()
        if size is None:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "Could not read the map image dimensions. Re-open the\n"
                "editor with --map MAP.png.",
            )
            return
        img_w, img_h = size

        # Clamp the dragged rectangle to the image bounds. Drags that
        # extended past the edge are common (rectangle handles are
        # off-screen at high zoom); clipping is friendlier than rejecting.
        x_min = max(0, int(round(rect.left())))
        y_min = max(0, int(round(rect.top())))
        x_max = min(img_w, int(round(rect.right())))
        y_max = min(img_h, int(round(rect.bottom())))
        w = x_max - x_min
        h = y_max - y_min

        if w <= 0 or h <= 0:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "The dragged rectangle was entirely outside the map\n"
                "image. Drag inside the map to add a room.",
            )
            return

        area_px = w * h
        if area_px < _ADD_ROOM_RECT_MIN_PIXELS:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "The dragged rectangle is too small to be a room "
                f"({area_px} px;\nminimum {_ADD_ROOM_RECT_MIN_PIXELS}). "
                "Try again with a bigger drag.",
            )
            return

        # Build a full-image-sized mask matching the convention used by
        # the existing calibrate / flood-fill code (Room.polygon_rle is
        # always full-image-sized; the bbox tells us where the geometry
        # lives within it).
        import numpy as np  # noqa: WPS433 — lazy: avoid numpy cost for editor-only sessions

        mask = np.zeros((img_h, img_w), dtype=np.uint8)
        mask[y_min:y_max, x_min:x_max] = 1
        rle = mask_to_rle(mask)
        bbox = (x_min, y_min, w, h)

        new_id = self._next_room_id()
        new_room = Room(id=new_id, polygon_rle=rle, area_px=area_px, bbox=bbox)
        cmd = AddRoomCommand(
            self._cal, new_room, on_change=self._on_room_change
        )
        self._undo_stack.push(cmd)

    def _handle_canvas_add_room_rect_cancelled(self) -> None:
        """Esc / misclick during add-room-rect mode — informational hook."""
        return

    # ------------------------------------------------ add room (polygon)

    def set_add_room_polygon_mode(self, active: bool) -> None:
        """Public toggle for the canvas's add-room-polygon mode.

        Same arm-guard as :meth:`set_add_room_rect_mode` — needs a
        loaded pixmap so we know the image dimensions for the mask.
        Doesn't need a map_path (unlike flood-fill) because the polygon
        IS the geometry; the source image is only consulted for size.
        """
        if active and self._canvas.image_size() is None:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "Add-room mode needs the map image to be loaded so the\n"
                "new room's polygon can be sized to match. Re-open the\n"
                "editor with --map MAP.png.",
            )
            return
        self._canvas.set_add_room_polygon_mode(active)

    def _handle_canvas_add_room_polygon(
        self, polygon: "QtGui.QPolygonF"
    ) -> None:
        """User closed a polygon in add-room-polygon mode.

        Clamps vertices to the image bounds, validates that the
        rasterized polygon's area clears the same floor we use for
        rect / flood, builds a Room with a full-image-sized mask
        (matching the convention used elsewhere), and pushes an
        :class:`AddRoomCommand` so the action is undoable.

        Why PIL for the rasterization: ``PIL.ImageDraw.Draw.polygon``
        is already a dependency used throughout the rendering pipeline
        (see ``render.py``, ``layout.py``, ``tile.py``), and it handles
        concave / self-intersecting polygons exactly the way ``cv2``
        and ``QPainter`` do (even-odd rule). Rolling our own scanline
        rasterizer would be extra code and behaviour for no benefit.
        """
        # Defensive: the canvas disarms before emitting, but be sure.
        self._canvas.set_add_room_polygon_mode(False)

        size = self._canvas.image_size()
        if size is None:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "Could not read the map image dimensions. Re-open the\n"
                "editor with --map MAP.png.",
            )
            return
        img_w, img_h = size

        # Pull vertices out of QPolygonF in image-pixel coordinates,
        # clamp to image bounds. (Vertices off the image are common at
        # zoomed-in editing — the user can drag past the edge — so we
        # clip rather than reject the whole polygon.)
        verts: list[tuple[int, int]] = []
        for i in range(polygon.size()):
            p = polygon.at(i)
            x = max(0, min(img_w, int(round(p.x()))))
            y = max(0, min(img_h, int(round(p.y()))))
            verts.append((x, y))

        if len(verts) < _ADD_ROOM_POLYGON_MIN_VERTICES:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "A polygon needs at least "
                f"{_ADD_ROOM_POLYGON_MIN_VERTICES} vertices to be a room.\n"
                "Click more points before closing.",
            )
            return

        # Rasterize the polygon onto a full-image-sized mask. We draw
        # into an L-mode PIL image with fill=1, then numpy-array it
        # back out. (PIL's polygon rasterizer uses the even-odd fill
        # rule, which matches every other polygon renderer in this
        # codebase.)
        from PIL import Image, ImageDraw  # noqa: WPS433 — lazy import
        import numpy as np  # noqa: WPS433 — lazy import

        canvas_img = Image.new("L", (img_w, img_h), 0)
        draw = ImageDraw.Draw(canvas_img)
        draw.polygon(verts, fill=1)
        mask = np.array(canvas_img, dtype=np.uint8)

        area_px = int(mask.sum())
        if area_px < _ADD_ROOM_POLYGON_MIN_PIXELS:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add room",
                "The drawn polygon's area is too small to be a room "
                f"({area_px} px;\nminimum {_ADD_ROOM_POLYGON_MIN_PIXELS}). "
                "Try a larger polygon — degenerate shapes "
                "(collinear vertices, self-overlapping loops) often\n"
                "rasterize to zero pixels.",
            )
            return

        # Bounding box from the actual rasterized mask, not from the
        # raw vertices: a polygon with collinear "spurs" outside the
        # filled area shouldn't inflate the bbox. ``np.where`` returns
        # row / col indices of non-zero pixels.
        ys, xs = np.where(mask > 0)
        x_min = int(xs.min())
        y_min = int(ys.min())
        # Add 1 to make the bbox half-open / width-style (matches the
        # convention used by the rect handler and by calibrate.py).
        bbox_w = int(xs.max()) - x_min + 1
        bbox_h = int(ys.max()) - y_min + 1
        bbox = (x_min, y_min, bbox_w, bbox_h)

        rle = mask_to_rle(mask)

        new_id = self._next_room_id()
        new_room = Room(id=new_id, polygon_rle=rle, area_px=area_px, bbox=bbox)
        cmd = AddRoomCommand(
            self._cal, new_room, on_change=self._on_room_change
        )
        self._undo_stack.push(cmd)

    def _handle_canvas_add_room_polygon_cancelled(self) -> None:
        """Esc / too-few-vertices close — informational hook."""
        return

    # ------------------------------------------------- add wall-patch

    def set_add_wall_patch_mode(self, active: bool) -> None:
        """Public toggle for the canvas's add-wall-patch mode.

        Same arm-guard as :meth:`set_add_room_rect_mode` — needs a loaded
        pixmap so we know the image dimensions for endpoint clamping.
        The map_path itself isn't needed (wall patches are pure
        geometry; the source image is only consulted later, when
        ``invalidate_wall_mask`` forces a rebuild).
        """
        if active and self._canvas.image_size() is None:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add wall patch",
                "Add-wall-patch mode needs the map image to be loaded\n"
                "so endpoints can be clamped to the image bounds.\n"
                "Re-open the editor with --map MAP.png.",
            )
            return
        self._canvas.set_add_wall_patch_mode(active)

    def _handle_canvas_add_wall_patch(
        self,
        start: "QtCore.QPointF",
        end: "QtCore.QPointF",
    ) -> None:
        """User finished a two-click line in add-wall-patch mode.

        Clamps both endpoints to the image bounds, validates that the
        line is non-degenerate (zero-length after clamp means the user
        drew entirely off-map or both points collapsed onto the same
        edge), builds an integer 4-tuple, and pushes an
        :class:`AddWallPatchCommand`. After the command is pushed,
        :meth:`invalidate_wall_mask` is called via ``_on_wall_patch_change``
        so the next flood-fill picks up the new patch.
        """
        # Defensive: canvas disarms before emitting, but be sure.
        self._canvas.set_add_wall_patch_mode(False)

        size = self._canvas.image_size()
        if size is None:
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add wall patch",
                "Could not read the map image dimensions. Re-open the\n"
                "editor with --map MAP.png.",
            )
            return
        img_w, img_h = size

        # Clamp each endpoint independently to the image bounds. A line
        # that straddles the edge is still useful (only the in-bounds
        # portion ends up on the mask anyway); only reject if both
        # points clamp to the *same* point (zero-length after clamp).
        x1 = max(0, min(img_w - 1, int(round(start.x()))))
        y1 = max(0, min(img_h - 1, int(round(start.y()))))
        x2 = max(0, min(img_w - 1, int(round(end.x()))))
        y2 = max(0, min(img_h - 1, int(round(end.y()))))

        if (x1, y1) == (x2, y2):
            QtWidgets.QMessageBox.warning(
                self._canvas,
                "Add wall patch",
                "The two endpoints collapsed onto the same pixel after\n"
                "clamping to the image bounds. Draw a non-degenerate\n"
                "line inside the map.",
            )
            return

        cmd = AddWallPatchCommand(
            self._cal,
            (x1, y1, x2, y2),
            on_change=self._on_wall_patch_change,
        )
        self._undo_stack.push(cmd)

    def _handle_canvas_add_wall_patch_cancelled(self) -> None:
        """Esc / mid-gesture disarm — informational hook."""
        return

    def _on_wall_patch_change(self, change: WallPatchChange) -> None:
        """Apply a :class:`WallPatchChange` to the canvas + invalidate the mask.

        Wall patches are pure mask repair: every add / undo-add /
        delete / undo-delete invalidates the cached wall mask so the
        next flood-fill rebuilds it from the source image plus the new
        patch set. Visual side: the canvas's wall-patch layer is
        rebuilt wholesale (indices shift on insert/remove, so a partial
        update isn't safe — and the layer is cheap to rebuild anyway).
        """
        if change.structural:
            self._canvas.rebuild_wall_patches()
        self.invalidate_wall_mask()

    def _next_room_id(self) -> int:
        """Return the next unused integer ``Room.id``.

        Strategy: max existing id + 1. Simple and stable across undo
        because undo removes whatever redo added (so the room id doesn't
        get recycled unless the user deliberately deletes a room).
        """
        existing = [r.id for r in self._cal.rooms]
        return (max(existing) + 1) if existing else 1

    # --------------------------------------------------- delete label

    def delete_selected_label(self) -> bool:
        """Push a ``DeleteLabelCommand`` for the currently-selected label.

        Returns:
            ``True`` if a label was selected and a delete command was pushed,
            ``False`` if there was nothing to delete. Used by the menu /
            shortcut action to decide whether to show a status-bar hint.
        """
        try:
            selected = self._canvas.scene().selectedItems()
        except RuntimeError:
            return False
        label_item = next(
            (it for it in selected if isinstance(it, LabelItem)), None
        )
        if label_item is None:
            return False
        cmd = DeleteLabelCommand(
            self._cal,
            label_item.label_index,
            on_change=self._on_label_change,
        )
        self._undo_stack.push(cmd)
        return True

    # --------------------------------------------------- delete room

    def delete_selected_room(self) -> bool:
        """Push a ``DeleteRoomCommand`` for the currently-selected room.

        Returns:
            ``True`` if a room was selected and a delete command was pushed,
            ``False`` if there was nothing to delete. Used by the menu /
            shortcut action to decide whether to show a status-bar hint.

        Note: priority order is label > room, mirroring selection priority
        in :meth:`_handle_selection_changed`. The caller is expected to
        check :meth:`delete_selected_label` first (the unified app-level
        action does this).
        """
        try:
            selected = self._canvas.scene().selectedItems()
        except RuntimeError:
            return False
        room_item = next(
            (it for it in selected if isinstance(it, RoomItem)), None
        )
        if room_item is None:
            return False
        # Find the room's index in cal.rooms (commands address by index,
        # not by id, so undo can restore at the exact original slot).
        target_id = room_item.room.id
        room_index = next(
            (i for i, r in enumerate(self._cal.rooms) if r.id == target_id),
            None,
        )
        if room_index is None:
            # Selection out of sync with model — shouldn't happen but guard.
            return False
        cmd = DeleteRoomCommand(
            self._cal,
            room_index,
            on_change=self._on_room_change,
        )
        self._undo_stack.push(cmd)
        return True

    # -------------------------------------------- in-place refresh hook

    def _on_label_change(self, change: LabelChange) -> None:
        """Apply a ``LabelChange`` to the canvas items + re-render inspector.

        Called by every command's ``redo``/``undo`` so the view tracks the
        model after both edits and undo operations.

        Structural changes (add / delete) shift label indices, so per-item
        in-place style refresh isn't enough — the canvas's ``_label_items``
        dict needs a full rebuild. We do that lazily here rather than
        teaching every command how to splice the dict.
        """
        # ---- Structural fast path: rebuild label layer wholesale -----
        if change.structural:
            self._canvas.rebuild_labels()
            # Room "labeled?" flags may flip when a room loses its last
            # label or gains its first; mirror the in-place path below
            # so the room recolor still happens.
            if change.room_ids:
                room_items = self._canvas.room_items()
                labels_by_room: dict[int, int] = {}
                for lab in self._cal.labels:
                    if lab.room_id is not None:
                        labels_by_room[lab.room_id] = (
                            labels_by_room.get(lab.room_id, 0) + 1
                        )
                for rid in change.room_ids:
                    ritem = room_items.get(rid)
                    if ritem is None:
                        continue
                    ritem.set_labeled(bool(labels_by_room.get(rid)))
            # Re-select the new (or resurrected) label so the inspector
            # focuses it — for Add this puts the user right at the new
            # label with the id field ready; for Undo-Delete it returns
            # focus to the label that just came back. Empty list means
            # Delete-redo: nothing to focus, drop to the empty pane.
            if change.label_indices:
                self._canvas.select_label(change.label_indices[-1])
            else:
                self._inspector.show_nothing()
            return

        # ---- Non-structural: in-place style + selective inspector refresh
        # 1. Recompute duplicate-id set once and update each affected label.
        duplicate_ids = find_duplicate_label_ids(self._cal.labels)
        label_items = self._canvas.label_items()
        for idx in change.label_indices:
            item = label_items.get(idx)
            if item is None:
                continue
            item.status = compute_label_status(self._cal.labels[idx], duplicate_ids)
            item._apply_style()  # noqa: SLF001 — intentional cross-module style refresh

        # 2. Recompute ``labeled`` for affected rooms.
        if change.room_ids:
            room_items = self._canvas.room_items()
            labels_by_room: dict[int, int] = {}
            for lab in self._cal.labels:
                if lab.room_id is not None:
                    labels_by_room[lab.room_id] = labels_by_room.get(lab.room_id, 0) + 1
            for rid in change.room_ids:
                ritem = room_items.get(rid)
                if ritem is None:
                    continue
                ritem.set_labeled(bool(labels_by_room.get(rid)))

        # 3. Refresh the inspector if the currently-edited label is in scope.
        current = self._inspector.current_label_index()
        if current is not None and current in change.label_indices:
            label = self._cal.labels[current]
            available_room_ids = sorted(room.id for room in self._cal.rooms)
            self._inspector.show_label(
                label=label,
                label_index=current,
                available_room_ids=available_room_ids,
            )

    # ------------------------------------------ room change handler

    def _on_room_change(self, change: RoomChange) -> None:
        """Apply a ``RoomChange`` to the canvas + inspector.

        Fired by :class:`AddRoomCommand` (Add + Undo-Add) and
        :class:`DeleteRoomCommand` (Delete + Undo-Delete). Structural
        changes always rebuild the room layer (room id sets change,
        polygon decode is needed for any resurrected entry).

        When ``change.label_indices`` is non-empty (delete-room cleared
        some labels' ``room_id``, or undo-delete restored them), the
        corresponding label items are re-styled in place and the
        inspector is refreshed if it's currently showing one of them.
        """
        if change.structural:
            self._canvas.rebuild_rooms()
            inspector_refreshed = False
            # Re-style any labels whose orphan status flipped because
            # the deleted (or resurrected) room's links changed. Mirrors
            # the in-place style refresh in ``_on_label_change``.
            if change.label_indices:
                duplicate_ids = find_duplicate_label_ids(self._cal.labels)
                label_items = self._canvas.label_items()
                for idx in change.label_indices:
                    item = label_items.get(idx)
                    if item is None:
                        continue
                    if not (0 <= idx < len(self._cal.labels)):
                        continue
                    item.status = compute_label_status(
                        self._cal.labels[idx], duplicate_ids
                    )
                    item._apply_style()  # noqa: SLF001 — intentional cross-module style refresh
                # If the inspector is currently showing one of the
                # affected labels, refresh it so the room combo reflects
                # the new (cleared / restored) room_id.
                current = self._inspector.current_label_index()
                if current is not None and current in change.label_indices:
                    if 0 <= current < len(self._cal.labels):
                        label = self._cal.labels[current]
                        available_room_ids = sorted(
                            room.id for room in self._cal.rooms
                        )
                        self._inspector.show_label(
                            label=label,
                            label_index=current,
                            available_room_ids=available_room_ids,
                        )
                        inspector_refreshed = True
            if change.room_ids:
                # Select the newly-added (or resurrected) room so the
                # inspector shows it right away — letting the user click
                # "Create label for this room" without any extra
                # navigation.
                self._canvas.select_room(change.room_ids[-1])
            elif not inspector_refreshed and self._inspector.current_label_index() is None:
                # Add-undo or delete-redo path: the room is gone. Only
                # clear the inspector if it wasn't showing a label — a
                # label-currently-displayed could be unrelated to the
                # deleted room (or could be one we just refreshed via
                # the affected-labels branch above), and clobbering it
                # would erase the user's working context.
                self._inspector.show_nothing()

    # --------------------------------------------------- clean tracking

    def _on_clean_changed(self, clean: bool) -> None:
        self.dirty_changed.emit(not clean)


__all__ = [
    "EditorController",
    "save_calibration_with_backup",
]
