"""Unit tests for ``officemapmaker.editor.commands``.

These tests need a ``QCoreApplication`` instance because ``QUndoCommand``
inherits from ``QObject`` and Qt requires one to be alive when QObjects
are constructed. The commands themselves operate on plain ``Calibration``
objects with no widget/event-loop coupling.
"""

from __future__ import annotations

import sys

import pytest

# A QApplication has to exist before any QObject is constructed. Without
# this fixture, even importing PySide6.QtGui.QUndoCommand inside a test
# fails on platforms where Qt insists on an event loop owner.
pytest.importorskip("PySide6")
from PySide6 import QtCore  # noqa: E402

from officemapmaker.calibration import Calibration, Label, RenderDefaults, Room  # noqa: E402
from officemapmaker.editor.commands import (  # noqa: E402
    ChangeRoomLinkCommand,
    EditLabelIdCommand,
    EditLabelNotesCommand,
    LabelChange,
    _indices_of_label_id,
)


@pytest.fixture(scope="module", autouse=True)
def _qapp():
    app = QtCore.QCoreApplication.instance()
    if app is None:
        app = QtCore.QCoreApplication(sys.argv or [""])
    yield app


# --------------------------------------------------------------------- helpers


def _mklabel(
    id_: str,
    *,
    room_id: int | None = 1,
    notes: str = "",
    bbox: tuple[int, int, int, int] = (0, 0, 10, 10),
) -> Label:
    return Label(
        id=id_,
        bbox=bbox,
        room_id=room_id,
        fill_seed=(5, 5),
        ocr_confidence=0.9,
        notes=notes,
    )


def _mkroom(id_: int) -> Room:
    return Room(id=id_, polygon_rle="", area_px=100, bbox=(0, 0, 50, 50))


def _mkcal(labels: list[Label], rooms: list[Room]) -> Calibration:
    return Calibration(
        map_image="m.png",
        map_hash="sha256:deadbeef",
        labels=labels,
        rooms=rooms,
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )


class _Recorder:
    """Captures every LabelChange the command emits via on_change."""

    def __init__(self) -> None:
        self.events: list[LabelChange] = []

    def __call__(self, change: LabelChange) -> None:
        self.events.append(change)


# ---------------------------------------------------------- _indices_of_label_id


def test_indices_of_label_id_returns_all_matches():
    cal = _mkcal(
        labels=[_mklabel("1480"), _mklabel("1481"), _mklabel("1480")],
        rooms=[_mkroom(1)],
    )
    assert _indices_of_label_id(cal, "1480") == [0, 2]
    assert _indices_of_label_id(cal, "1481") == [1]
    assert _indices_of_label_id(cal, "9999") == []


# ----------------------------------------------------- EditLabelIdCommand


def test_edit_label_id_round_trip():
    cal = _mkcal(
        labels=[_mklabel("1480"), _mklabel("1481")],
        rooms=[_mkroom(1)],
    )
    rec = _Recorder()
    cmd = EditLabelIdCommand(cal, 0, "1505B", on_change=rec)

    cmd.redo()
    assert cal.labels[0].id == "1505B"
    cmd.undo()
    assert cal.labels[0].id == "1480"
    cmd.redo()
    assert cal.labels[0].id == "1505B"


def test_edit_label_id_notifies_about_old_and_new_duplicates():
    # Two labels share id "1178" — editing one of them transitions the *other*
    # one out of the duplicate set, so both indices must be in the change.
    cal = _mkcal(
        labels=[
            _mklabel("1178"),  # the one we're editing
            _mklabel("1178"),  # the sibling that should stop being a duplicate
            _mklabel("1180"),  # unaffected
        ],
        rooms=[_mkroom(1)],
    )
    rec = _Recorder()
    cmd = EditLabelIdCommand(cal, 0, "1174", on_change=rec)
    cmd.redo()

    assert len(rec.events) == 1
    affected = set(rec.events[0].label_indices)
    # Both the edited label (0) and its previous duplicate sibling (1) must
    # be in the affected set so the canvas can recolor them.
    assert 0 in affected
    assert 1 in affected
    # The unrelated label (2) should NOT be in the affected set.
    assert 2 not in affected


def test_edit_label_id_notifies_about_new_duplicate_collision():
    # Editing label 0 to collide with label 1's id turns both into duplicates.
    cal = _mkcal(
        labels=[_mklabel("1480"), _mklabel("1481")],
        rooms=[_mkroom(1)],
    )
    rec = _Recorder()
    cmd = EditLabelIdCommand(cal, 0, "1481", on_change=rec)
    cmd.redo()

    affected = set(rec.events[0].label_indices)
    assert 0 in affected and 1 in affected


def test_edit_label_id_without_on_change_does_not_explode():
    cal = _mkcal(labels=[_mklabel("1480")], rooms=[_mkroom(1)])
    cmd = EditLabelIdCommand(cal, 0, "1505B")
    cmd.redo()
    assert cal.labels[0].id == "1505B"


# --------------------------------------------------- EditLabelNotesCommand


def test_edit_label_notes_round_trip():
    cal = _mkcal(labels=[_mklabel("1480", notes="old")], rooms=[_mkroom(1)])
    rec = _Recorder()
    cmd = EditLabelNotesCommand(cal, 0, "new note", on_change=rec)

    cmd.redo()
    assert cal.labels[0].notes == "new note"
    cmd.undo()
    assert cal.labels[0].notes == "old"

    # Both redo and undo should notify so any consumers stay in sync.
    assert len(rec.events) == 2
    for ev in rec.events:
        assert ev.label_indices == [0]
        assert ev.room_ids == []


# ----------------------------------------------------- ChangeRoomLinkCommand


def test_change_room_link_to_new_room_round_trip():
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=1)],
        rooms=[_mkroom(1), _mkroom(2)],
    )
    rec = _Recorder()
    cmd = ChangeRoomLinkCommand(cal, 0, 2, on_change=rec)

    cmd.redo()
    assert cal.labels[0].room_id == 2
    cmd.undo()
    assert cal.labels[0].room_id == 1

    # The first event (redo) should mention both rooms (1 lost a label, 2 gained one).
    redo_event = rec.events[0]
    assert set(redo_event.room_ids) == {1, 2}
    assert redo_event.label_indices == [0]


def test_change_room_link_to_none_marks_old_room():
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=1)],
        rooms=[_mkroom(1)],
    )
    rec = _Recorder()
    cmd = ChangeRoomLinkCommand(cal, 0, None, on_change=rec)

    cmd.redo()
    assert cal.labels[0].room_id is None
    redo_event = rec.events[0]
    assert set(redo_event.room_ids) == {1}


def test_change_room_link_from_none_marks_new_room():
    cal = _mkcal(
        labels=[_mklabel("1480", room_id=None)],
        rooms=[_mkroom(1)],
    )
    rec = _Recorder()
    cmd = ChangeRoomLinkCommand(cal, 0, 1, on_change=rec)

    cmd.redo()
    assert cal.labels[0].room_id == 1
    redo_event = rec.events[0]
    assert set(redo_event.room_ids) == {1}
