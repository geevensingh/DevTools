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

from ..calibration import Calibration, save_calibration
from .canvas import MapCanvas
from .commands import (
    ChangeRoomLinkCommand,
    EditLabelIdCommand,
    EditLabelNotesCommand,
    LabelChange,
)
from .items import LabelItem, RoomItem, compute_label_status, find_duplicate_label_ids
from .sidebar import InspectorPanel


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
        parent: Optional[QtCore.QObject] = None,
    ) -> None:
        super().__init__(parent)
        self._cal = calibration
        self._calibration_path = calibration_path
        self._canvas = canvas
        self._inspector = inspector

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

        # Canvas selection → inspector: populate the panel when the user
        # clicks an item. Qt emits ``selectionChanged`` from the scene.
        self._canvas.scene().selectionChanged.connect(self._handle_selection_changed)

        # Canvas pick-room mode → controller: convert the picked room into a
        # ChangeRoomLinkCommand (so the link is undoable), and uncheck the
        # inspector button when the canvas drops out of pick mode for any
        # reason (user picked, Esc, clicked empty space).
        self._canvas.room_picked.connect(self._handle_canvas_room_picked)
        self._canvas.room_pick_cancelled.connect(self._handle_canvas_pick_cancelled)

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

    # -------------------------------------------- in-place refresh hook

    def _on_label_change(self, change: LabelChange) -> None:
        """Apply a ``LabelChange`` to the canvas items + re-render inspector.

        Called by every command's ``redo``/``undo`` so the view tracks the
        model after both edits and undo operations.
        """
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

    # --------------------------------------------------- clean tracking

    def _on_clean_changed(self, clean: bool) -> None:
        self.dirty_changed.emit(not clean)


__all__ = [
    "EditorController",
    "save_calibration_with_backup",
]
