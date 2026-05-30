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

from ..calibration import Calibration, Label


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


__all__ = [
    "AddLabelCommand",
    "ChangeCallback",
    "ChangeRoomLinkCommand",
    "DeleteLabelCommand",
    "EditLabelIdCommand",
    "EditLabelNotesCommand",
    "LabelChange",
]
