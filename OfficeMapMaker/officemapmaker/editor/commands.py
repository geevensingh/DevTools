"""``QUndoCommand`` subclasses that mutate the editor's ``Calibration`` model.

Why commands instead of direct mutation: every user edit (rename a label,
re-link a label's room, edit notes) needs to be undoable. ``QUndoStack``
gives us that for free — but only if mutations flow through ``redo()``
and the inverse through ``undo()``. So all model changes that originate
from the UI live here, even simple one-line setters.

Each command:

* Stores the index/id of the affected record (so it survives reordering, though
  in practice we never reorder labels or rooms in the editor).
* Captures the *old* value at construction time so ``undo()`` is exact even
  if the user makes a second edit on the same record before undoing.
* Calls an optional ``on_change`` callback after every mutation so the canvas
  can refresh the affected items in place (no full scene rebuild needed).

The commands operate on plain ``Calibration`` objects with no Qt dependency
in the mutation path, which keeps them unit-testable without a Qt event loop.
"""

from __future__ import annotations

from typing import Callable, Optional

from PySide6 import QtGui

from ..calibration import Calibration, Label, Room


# ---------------------------------------------------------------------------
# Change-notification payloads
# ---------------------------------------------------------------------------


class LabelChange:
    """Notification payload for an edit that affected one or more labels.

    The canvas listens for these and re-styles the affected ``LabelItem``s
    in place. We use a plain class (not a dataclass) so the callback API
    is dead simple: ``on_change(LabelChange(...))``.

    Attributes:
        label_indices: Indices into ``Calibration.labels`` whose visual state
            may have changed (color, bbox, text). For structural changes
            (Add/Delete) the *new* indices of any labels the caller would
            like the canvas to select afterwards — empty means "no selection
            target" (the controller will clear the inspector).
        room_ids: Room ids whose ``labeled`` flag may have flipped.
        structural: True if the set of labels itself changed (one was added
            or removed). The canvas can no longer trust its cached
            ``label_index → LabelItem`` dict — indices may have shifted —
            so it does a label-only rebuild. Room polygons are untouched
            (rebuilding them is expensive and they didn't change).
    """

    def __init__(
        self,
        *,
        label_indices: Optional[list[int]] = None,
        room_ids: Optional[list[int]] = None,
        structural: bool = False,
    ) -> None:
        self.label_indices: list[int] = list(label_indices or [])
        self.room_ids: list[int] = list(room_ids or [])
        self.structural: bool = structural


ChangeCallback = Callable[[LabelChange], None]


class RoomChange:
    """Notification payload for an edit that affected one or more rooms.

    Parallel to :class:`LabelChange` but on the room side. v1 only fired
    on Add-room (Phase 1 of the add-room feature). ed8 added Delete-room,
    which can also affect *labels* (the deleted room's links get cleared),
    so this payload now also carries an optional list of label indices
    whose visual state may have changed.

    Attributes:
        room_ids: Room ids whose existence or geometry just changed.
            For Add: the new room's id (after redo) or the just-removed
            room's id (after undo) — used by the controller to focus the
            inspector / select the new item.
        label_indices: Indices into ``Calibration.labels`` whose visual
            state may have changed because of this room edit — e.g. on
            Delete-room, every label that was linked to the deleted room
            had its ``room_id`` cleared and so flipped to orphan status.
            Empty by default (Add-room doesn't affect labels).
        structural: True if the set of rooms itself changed (one was added
            or removed). The canvas can no longer trust its cached
            ``room_id → RoomItem`` dict and does a room-only rebuild via
            ``rebuild_rooms()``. Label items are untouched (no label
            indices shift on a room change) — but they may need
            *re-styling* if ``label_indices`` is non-empty.
    """

    def __init__(
        self,
        *,
        room_ids: Optional[list[int]] = None,
        label_indices: Optional[list[int]] = None,
        structural: bool = False,
    ) -> None:
        self.room_ids: list[int] = list(room_ids or [])
        self.label_indices: list[int] = list(label_indices or [])
        self.structural: bool = structural


RoomChangeCallback = Callable[[RoomChange], None]


class WallPatchChange:
    """Notification payload for an edit that affected the wall_patches list.

    Parallel to :class:`LabelChange` / :class:`RoomChange` on the wall-patch
    side. Wall patches don't carry a stable id (they're a plain list of
    4-tuples in the data model), so this payload is intentionally minimal:
    every wall-patch mutation is *structural* in the sense that the canvas
    has to rebuild its wall-patch layer from scratch (insert/delete shifts
    every subsequent patch's index).

    The controller's handler is also responsible for calling
    :meth:`EditorController.invalidate_wall_mask` so the next add-room
    flood-fill picks up the new (or undone) patch — wall patches are a
    *fill-mask repair*, so their whole purpose is to alter what flood-fill
    can reach.

    Attributes:
        structural: Always True today (add/delete are the only operations).
            Kept as a field for parity with the other change payloads and
            in case future "move a wall patch endpoint" edits want a
            non-structural variant.
    """

    def __init__(self, *, structural: bool = True) -> None:
        self.structural: bool = structural


WallPatchChangeCallback = Callable[[WallPatchChange], None]


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _indices_of_label_id(calibration: Calibration, label_id: str) -> list[int]:
    """All indices in ``calibration.labels`` whose ``id`` equals ``label_id``.

    Used by the id-edit command to figure out which sibling labels may have
    transitioned in/out of the "duplicate" status when one of them changed id.
    Case-sensitive on purpose: the data model itself is case-sensitive; the
    spreadsheet match is case-insensitive but that's a downstream concern.
    """
    return [i for i, lab in enumerate(calibration.labels) if lab.id == label_id]


# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------


class EditLabelIdCommand(QtGui.QUndoCommand):
    """Change a label's ``id`` string.

    A change in id can move a label *into* or *out of* the duplicate-id set,
    so the change notification includes every label index that shared either
    the old id or the new id (before and after the change). The canvas uses
    that list to recompute the duplicate set and recolor those items.
    """

    def __init__(
        self,
        calibration: Calibration,
        label_index: int,
        new_id: str,
        *,
        on_change: Optional[ChangeCallback] = None,
    ) -> None:
        super().__init__(f"edit label id → {new_id!r}")
        self._cal = calibration
        self._label_index = label_index
        self._new_id = new_id
        self._old_id = calibration.labels[label_index].id
        self._on_change = on_change

    def redo(self) -> None:  # noqa: D401 — Qt API
        self._apply(self._new_id)

    def undo(self) -> None:  # noqa: D401 — Qt API
        self._apply(self._old_id)

    def _apply(self, new_id: str) -> None:
        # Collect every label whose visual status might flip. That's the
        # union of labels sharing the old id (some may leave the dup set),
        # labels sharing the new id (some may enter the dup set), plus the
        # one being edited.
        affected = set(_indices_of_label_id(self._cal, self._cal.labels[self._label_index].id))
        affected.update(_indices_of_label_id(self._cal, new_id))
        affected.add(self._label_index)

        self._cal.labels[self._label_index].id = new_id

        # Recompute *after* the mutation so the canvas sees a consistent view.
        affected.update(_indices_of_label_id(self._cal, new_id))

        if self._on_change is not None:
            self._on_change(LabelChange(label_indices=sorted(affected)))


class EditLabelNotesCommand(QtGui.QUndoCommand):
    """Change a label's free-form ``notes`` text.

    Notes don't affect any other label or any room, so the change
    notification only mentions the one label that changed (purely so the
    sidebar can re-render in case the model was edited from elsewhere —
    not strictly required today but cheap).
    """

    def __init__(
        self,
        calibration: Calibration,
        label_index: int,
        new_notes: str,
        *,
        on_change: Optional[ChangeCallback] = None,
    ) -> None:
        super().__init__("edit label notes")
        self._cal = calibration
        self._label_index = label_index
        self._new_notes = new_notes
        self._old_notes = calibration.labels[label_index].notes
        self._on_change = on_change

    def redo(self) -> None:  # noqa: D401
        self._cal.labels[self._label_index].notes = self._new_notes
        if self._on_change is not None:
            self._on_change(LabelChange(label_indices=[self._label_index]))

    def undo(self) -> None:  # noqa: D401
        self._cal.labels[self._label_index].notes = self._old_notes
        if self._on_change is not None:
            self._on_change(LabelChange(label_indices=[self._label_index]))


class ChangeRoomLinkCommand(QtGui.QUndoCommand):
    """Re-point a label at a different room (or unlink it: ``room_id=None``).

    Changing the room link can flip the orphan/linked status of the *label*
    AND flip the ``labeled`` flag of the *old* and *new* rooms (the old
    room may now be unlabeled, the new room may now be labeled), so the
    change notification carries both lists.
    """

    def __init__(
        self,
        calibration: Calibration,
        label_index: int,
        new_room_id: Optional[int],
        *,
        on_change: Optional[ChangeCallback] = None,
    ) -> None:
        super().__init__(
            f"link label → room {new_room_id}" if new_room_id is not None
            else "unlink label from room"
        )
        self._cal = calibration
        self._label_index = label_index
        self._new_room_id = new_room_id
        self._old_room_id = calibration.labels[label_index].room_id
        self._on_change = on_change

    def redo(self) -> None:  # noqa: D401
        self._apply(self._new_room_id)

    def undo(self) -> None:  # noqa: D401
        self._apply(self._old_room_id)

    def _apply(self, new_room_id: Optional[int]) -> None:
        affected_rooms: list[int] = []
        old = self._cal.labels[self._label_index].room_id
        if old is not None:
            affected_rooms.append(old)
        if new_room_id is not None and new_room_id != old:
            affected_rooms.append(new_room_id)

        self._cal.labels[self._label_index].room_id = new_room_id

        if self._on_change is not None:
            self._on_change(
                LabelChange(
                    label_indices=[self._label_index],
                    room_ids=affected_rooms,
                )
            )


class AddLabelCommand(QtGui.QUndoCommand):
    """Append a new ``Label`` to the calibration (undoable).

    The label is always appended (never inserted at an arbitrary index),
    so undo just removes the last entry the redo added. This keeps existing
    label indices stable for every prior command on the stack — important
    because other queued commands may reference them.

    The change notification has ``structural=True`` because the set of
    labels itself just changed: any cached ``label_index → LabelItem``
    map in the canvas is now incomplete (missing the new item). The
    canvas reacts by rebuilding all label items from the updated
    calibration; room polygons are untouched.

    ``label_indices=[self._new_index]`` after redo so the controller can
    optionally select the new label and focus the inspector on it.
    """

    def __init__(
        self,
        calibration: Calibration,
        new_label: Label,
        *,
        on_change: Optional[ChangeCallback] = None,
    ) -> None:
        super().__init__(f"add label {new_label.id!r}")
        self._cal = calibration
        self._label = new_label
        self._on_change = on_change
        # Index is recorded at first redo (always equal to len(labels) at
        # that moment) and reused thereafter so undo + redo are exact.
        self._new_index: Optional[int] = None

    def redo(self) -> None:  # noqa: D401
        if self._new_index is None:
            self._new_index = len(self._cal.labels)
        # Defensive: if some other command shrank the list (shouldn't happen
        # in practice, but undo/redo interleavings can be subtle) just
        # append to the end again rather than insert at a stale index.
        if self._new_index > len(self._cal.labels):
            self._new_index = len(self._cal.labels)
        self._cal.labels.insert(self._new_index, self._label)
        if self._on_change is not None:
            self._on_change(
                LabelChange(
                    label_indices=[self._new_index],
                    room_ids=(
                        [self._label.room_id] if self._label.room_id is not None else []
                    ),
                    structural=True,
                )
            )

    def undo(self) -> None:  # noqa: D401
        if self._new_index is None or self._new_index >= len(self._cal.labels):
            return
        del self._cal.labels[self._new_index]
        if self._on_change is not None:
            self._on_change(
                LabelChange(
                    label_indices=[],
                    room_ids=(
                        [self._label.room_id] if self._label.room_id is not None else []
                    ),
                    structural=True,
                )
            )


class DeleteLabelCommand(QtGui.QUndoCommand):
    """Remove an existing ``Label`` from the calibration (undoable).

    Captures the deleted Label and its original list index at construction
    so undo can restore it to the exact same position — preserving the
    indices of all labels that originally came after it.

    Like ``AddLabelCommand`` this is a structural change; the canvas
    rebuilds its label items rather than trying to splice indices.
    On undo, ``label_indices=[self._index]`` so the controller can
    re-select the resurrected label.
    """

    def __init__(
        self,
        calibration: Calibration,
        label_index: int,
        *,
        on_change: Optional[ChangeCallback] = None,
    ) -> None:
        if not (0 <= label_index < len(calibration.labels)):
            raise IndexError(
                f"DeleteLabelCommand: label_index {label_index} out of range "
                f"(have {len(calibration.labels)} labels)"
            )
        snapshot = calibration.labels[label_index]
        super().__init__(f"delete label {snapshot.id!r}")
        self._cal = calibration
        self._index = label_index
        self._label = snapshot  # captured by reference — the controller will not mutate it once deleted
        self._on_change = on_change

    def redo(self) -> None:  # noqa: D401
        if not (0 <= self._index < len(self._cal.labels)):
            # Concurrent edit pulled the rug — log silently rather than crash.
            return
        del self._cal.labels[self._index]
        if self._on_change is not None:
            self._on_change(
                LabelChange(
                    label_indices=[],
                    room_ids=(
                        [self._label.room_id] if self._label.room_id is not None else []
                    ),
                    structural=True,
                )
            )

    def undo(self) -> None:  # noqa: D401
        # Re-insert at the original index. If the list has grown beyond
        # that index, the resurrected label still occupies the same
        # logical slot it had before deletion.
        insert_at = min(self._index, len(self._cal.labels))
        self._cal.labels.insert(insert_at, self._label)
        if self._on_change is not None:
            self._on_change(
                LabelChange(
                    label_indices=[insert_at],
                    room_ids=(
                        [self._label.room_id] if self._label.room_id is not None else []
                    ),
                    structural=True,
                )
            )


class AddRoomCommand(QtGui.QUndoCommand):
    """Append a new ``Room`` to the calibration (undoable).

    Mirror of :class:`AddLabelCommand` on the room side. The room is
    always appended (never inserted at an arbitrary position) so undo
    just removes whatever entry redo last added. Existing room ids
    therefore stay stable across an Add/Undo cycle — important because
    labels reference rooms by ``room_id`` and we don't want an undo on
    a brand-new room to silently re-number an unrelated one.

    The fired :class:`RoomChange` carries ``structural=True`` so the
    canvas knows to rebuild its room overlay items wholesale (room ids
    don't shift, but the set just grew, and the canvas's cached
    ``_room_items`` dict needs the new entry plus a fresh polygon
    decode for it).
    """

    def __init__(
        self,
        calibration: Calibration,
        new_room: Room,
        *,
        on_change: Optional[RoomChangeCallback] = None,
    ) -> None:
        super().__init__(f"add room {new_room.id}")
        self._cal = calibration
        self._room = new_room
        self._on_change = on_change
        # Index recorded at first redo so undo / redo always touch the
        # same slot (same idiom as AddLabelCommand).
        self._new_index: Optional[int] = None

    def redo(self) -> None:  # noqa: D401
        if self._new_index is None:
            self._new_index = len(self._cal.rooms)
        if self._new_index > len(self._cal.rooms):
            self._new_index = len(self._cal.rooms)
        self._cal.rooms.insert(self._new_index, self._room)
        if self._on_change is not None:
            self._on_change(
                RoomChange(room_ids=[self._room.id], structural=True)
            )

    def undo(self) -> None:  # noqa: D401
        if self._new_index is None or self._new_index >= len(self._cal.rooms):
            return
        del self._cal.rooms[self._new_index]
        if self._on_change is not None:
            # Empty room_ids: the room is gone, so the controller should
            # drop selection on it (no item to focus). Still structural
            # because the canvas needs to drop the RoomItem.
            self._on_change(RoomChange(room_ids=[], structural=True))


class DeleteRoomCommand(QtGui.QUndoCommand):
    """Remove an existing ``Room`` from the calibration (undoable).

    Mirror of :class:`DeleteLabelCommand` on the room side. Captures the
    deleted room and every label that was linked to it (the ``room_id``
    of each linked label is cleared on redo so we don't leave behind
    stale references, and is restored on undo as part of the same
    command — so a single undo brings back both the room *and* every
    link).

    Like :class:`AddRoomCommand` this is a structural change; the canvas
    rebuilds its room overlay items rather than trying to splice indices.
    The fired :class:`RoomChange` also carries the affected label indices
    so the controller can re-style those label items (their orphan
    status just flipped) and refresh the inspector if it's currently
    showing one of them.
    """

    def __init__(
        self,
        calibration: Calibration,
        room_index: int,
        *,
        on_change: Optional[RoomChangeCallback] = None,
    ) -> None:
        if not (0 <= room_index < len(calibration.rooms)):
            raise IndexError(
                f"DeleteRoomCommand: room_index {room_index} out of range "
                f"(have {len(calibration.rooms)} rooms)"
            )
        snapshot = calibration.rooms[room_index]
        super().__init__(f"delete room {snapshot.id}")
        self._cal = calibration
        self._index = room_index
        self._room = snapshot
        # List of (label_index, original_room_id) for every label that
        # currently points at this room. We record both ends so undo can
        # restore the link exactly even if the original_room_id wasn't
        # the same as ``snapshot.id`` (defensive — it always should be).
        self._affected_labels: list[tuple[int, Optional[int]]] = [
            (i, lab.room_id)
            for i, lab in enumerate(calibration.labels)
            if lab.room_id == snapshot.id
        ]
        self._on_change = on_change

    def redo(self) -> None:  # noqa: D401
        if not (0 <= self._index < len(self._cal.rooms)):
            # Concurrent edit pulled the rug — log silently rather than crash.
            return
        # Clear room_id on every affected label so we don't leave stale
        # references to a now-deleted room.
        for li, _ in self._affected_labels:
            if 0 <= li < len(self._cal.labels):
                self._cal.labels[li].room_id = None
        del self._cal.rooms[self._index]
        if self._on_change is not None:
            self._on_change(
                RoomChange(
                    room_ids=[],
                    label_indices=[li for li, _ in self._affected_labels],
                    structural=True,
                )
            )

    def undo(self) -> None:  # noqa: D401
        # Re-insert at the original index. If the rooms list has grown
        # beyond that index, the resurrected room still occupies the same
        # logical slot it had before deletion.
        insert_at = min(self._index, len(self._cal.rooms))
        self._cal.rooms.insert(insert_at, self._room)
        # Restore each affected label's room_id to its original value.
        for li, original_rid in self._affected_labels:
            if 0 <= li < len(self._cal.labels):
                self._cal.labels[li].room_id = original_rid
        if self._on_change is not None:
            self._on_change(
                RoomChange(
                    room_ids=[self._room.id],
                    label_indices=[li for li, _ in self._affected_labels],
                    structural=True,
                )
            )


class AddWallPatchCommand(QtGui.QUndoCommand):
    """Append a new wall-patch line segment to the calibration (undoable).

    Wall patches are the fill-mask repair mechanism: each entry is an
    ``(x1, y1, x2, y2)`` line drawn onto the *binary fill mask* (never
    onto the visible map) so flood-fill leaks through wall gaps can be
    closed without altering the source image. See
    :attr:`Calibration.wall_patches` for the data model.

    Mirror of :class:`AddRoomCommand` on the wall-patch side. The patch
    is always appended (never inserted at an arbitrary position) so undo
    just removes whatever entry redo last added. The patch tuple is
    stored eagerly so undo / redo remain idempotent across multiple
    cycles (the controller can hand us the same patch and we won't
    mutate it).
    """

    def __init__(
        self,
        calibration: Calibration,
        new_patch: tuple[int, int, int, int],
        *,
        on_change: Optional["WallPatchChangeCallback"] = None,
    ) -> None:
        super().__init__(
            f"add wall patch ({new_patch[0]},{new_patch[1]})→"
            f"({new_patch[2]},{new_patch[3]})"
        )
        self._cal = calibration
        # Coerce to a plain tuple of ints so future mutations on whatever
        # the caller handed us can't change the stored patch.
        self._patch: tuple[int, int, int, int] = (
            int(new_patch[0]),
            int(new_patch[1]),
            int(new_patch[2]),
            int(new_patch[3]),
        )
        self._on_change = on_change
        # Index recorded at first redo so undo / redo always touch the
        # same slot (same idiom as AddLabelCommand / AddRoomCommand).
        self._new_index: Optional[int] = None

    def redo(self) -> None:  # noqa: D401 — Qt API
        if self._new_index is None:
            self._new_index = len(self._cal.wall_patches)
        if self._new_index > len(self._cal.wall_patches):
            self._new_index = len(self._cal.wall_patches)
        self._cal.wall_patches.insert(self._new_index, self._patch)
        if self._on_change is not None:
            self._on_change(WallPatchChange(structural=True))

    def undo(self) -> None:  # noqa: D401 — Qt API
        if self._new_index is None or self._new_index >= len(
            self._cal.wall_patches
        ):
            return
        del self._cal.wall_patches[self._new_index]
        if self._on_change is not None:
            self._on_change(WallPatchChange(structural=True))


class DeleteWallPatchCommand(QtGui.QUndoCommand):
    """Remove an existing wall-patch line segment from the calibration (undoable).

    Mirror of :class:`DeleteRoomCommand` on the wall-patch side, simpler
    because wall patches don't carry cross-references the way rooms do
    (no labels point at a wall_patch_index). The patch tuple is
    snapshotted at construction so undo can restore the exact same
    ``(x1, y1, x2, y2)`` at the original list index — preserving the
    indices of every patch that originally came after it.

    Like :class:`AddWallPatchCommand` this is a structural change; the
    canvas rebuilds its wall-patch overlay layer wholesale rather than
    splicing indices (insert/remove shifts every subsequent patch's
    index, and the cached fill mask must be invalidated regardless).
    """

    def __init__(
        self,
        calibration: Calibration,
        patch_index: int,
        *,
        on_change: Optional[WallPatchChangeCallback] = None,
    ) -> None:
        if not (0 <= patch_index < len(calibration.wall_patches)):
            raise IndexError(
                f"DeleteWallPatchCommand: patch_index {patch_index} out of range "
                f"(have {len(calibration.wall_patches)} wall patches)"
            )
        snapshot = calibration.wall_patches[patch_index]
        # Coerce to a plain tuple of ints so undo's re-insert puts back a
        # tuple identical in shape to AddWallPatchCommand's contract.
        self._patch: tuple[int, int, int, int] = (
            int(snapshot[0]),
            int(snapshot[1]),
            int(snapshot[2]),
            int(snapshot[3]),
        )
        super().__init__(
            f"delete wall patch ({self._patch[0]},{self._patch[1]})→"
            f"({self._patch[2]},{self._patch[3]})"
        )
        self._cal = calibration
        self._index = patch_index
        self._on_change = on_change

    def redo(self) -> None:  # noqa: D401 — Qt API
        if not (0 <= self._index < len(self._cal.wall_patches)):
            # Concurrent edit pulled the rug — log silently rather than crash.
            return
        del self._cal.wall_patches[self._index]
        if self._on_change is not None:
            self._on_change(WallPatchChange(structural=True))

    def undo(self) -> None:  # noqa: D401 — Qt API
        # Re-insert at the original index. If the list has grown beyond
        # that index, the resurrected patch still occupies the same
        # logical slot it had before deletion.
        insert_at = min(self._index, len(self._cal.wall_patches))
        self._cal.wall_patches.insert(insert_at, self._patch)
        if self._on_change is not None:
            self._on_change(WallPatchChange(structural=True))


__all__ = [
    "AddLabelCommand",
    "AddRoomCommand",
    "AddWallPatchCommand",
    "ChangeCallback",
    "ChangeRoomLinkCommand",
    "DeleteLabelCommand",
    "DeleteRoomCommand",
    "DeleteWallPatchCommand",
    "EditLabelIdCommand",
    "EditLabelNotesCommand",
    "LabelChange",
    "RoomChange",
    "RoomChangeCallback",
    "WallPatchChange",
    "WallPatchChangeCallback",
]
