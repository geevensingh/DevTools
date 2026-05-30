"""``InspectorPanel`` — the right-hand dock that edits the selected item.

The panel has three faces:

* **Empty** — nothing selected. A static "click a label or a room" hint.
* **Label** — the user clicked a ``LabelItem``. Editable: id, room link, notes.
  Read-only: bbox, ocr_confidence.
* **Room** — the user clicked a ``RoomItem``. Read-only summary plus a list of
  labels that point at this room (since the room itself has no editable fields
  beyond what the labels carry — and editing polygons is out of scope for v1).

User-visible field-edit semantics:

* Text fields commit on Enter or focus loss (``editingFinished`` signal).
* The room picker commits on every selection change.
* The panel does **not** know about ``QUndoStack``; it just emits signals
  describing what the user changed. The controller turns those into
  ``QUndoCommand``s, so panel-driven edits are properly undoable.
"""

from __future__ import annotations

from typing import Optional

from PySide6 import QtCore, QtGui, QtWidgets

from ..calibration import Label, Room


# Sentinel value used in the room combo box to represent "no room linked"
# (i.e., orphan label). ``QComboBox`` items store user data per index, and
# ``None`` isn't safe to round-trip through Qt's variant glue on every
# platform, so we use a clearly-out-of-band int that no real room can have.
_ORPHAN_SENTINEL = -1


class InspectorPanel(QtWidgets.QWidget):
    """Right-hand inspector showing details for the selected item.

    Signals carry the *label_index* (stable position in
    ``Calibration.labels``) so the controller can build a command that
    targets the correct record even if the user clicks away mid-edit.
    """

    label_id_changed = QtCore.Signal(int, str)
    """``(label_index, new_id)`` — emitted when the id text-field commits."""

    label_notes_changed = QtCore.Signal(int, str)
    """``(label_index, new_notes)`` — emitted when the notes field commits."""

    label_room_changed = QtCore.Signal(int, object)
    """``(label_index, new_room_id_or_None)`` — emitted on combo change."""

    room_pick_requested = QtCore.Signal(int, bool)
    """``(label_index, active)`` — user toggled the visual "Pick room" button.

    The controller flips the canvas into pick-room mode when ``active`` is
    ``True`` and back out when ``False``. Picking a room directly off the
    map is much friendlier than remembering its id and choosing from the
    combo, especially for orphan labels where the user has no idea what
    nearby room id to look for.
    """

    create_label_for_room = QtCore.Signal(int)
    """``(room_id)`` — user clicked the "Create label for this room" button.

    Emitted only when the selected room currently has no labels (the
    button is shown only in that case). The controller prompts the user
    for the new label id, then pushes an ``AddLabelCommand`` that links
    the new label to the room at the polygon's center.
    """

    def __init__(self, parent: Optional[QtWidgets.QWidget] = None) -> None:
        super().__init__(parent)

        # Stacked widget so we can swap the panel content without re-laying
        # out the dock on every selection change.
        self._stack = QtWidgets.QStackedWidget(self)
        self._empty = self._build_empty_widget()
        self._label_form, self._label_widgets = self._build_label_form()
        self._room_form, self._room_widgets = self._build_room_form()
        self._stack.addWidget(self._empty)
        self._stack.addWidget(self._label_form)
        self._stack.addWidget(self._room_form)

        layout = QtWidgets.QVBoxLayout(self)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.addWidget(self._stack)
        self.setLayout(layout)

        # Track which label is currently shown so commit signals know where
        # to send their payload (the user may have edited the id, then
        # selected something else before pressing Enter).
        self._current_label_index: Optional[int] = None

        # Track which room is currently shown so the "Create label for this
        # room" button knows where to attach the new label. ``None`` when
        # nothing or a label is selected.
        self._current_room_id: Optional[int] = None

        # Suppress signals while we programmatically repopulate fields
        # — without this guard, ``setText`` would re-emit ``editingFinished``
        # and bounce a phantom edit through the undo stack.
        self._suppress_signals = False

    # -------------------------------------------------- public API

    def current_label_index(self) -> Optional[int]:
        return self._current_label_index

    def show_nothing(self) -> None:
        self._current_label_index = None
        self._current_room_id = None
        # Selection cleared → any in-flight pick-room mode no longer has a
        # target label, so reset the button visually. The controller is
        # responsible for taking the canvas out of pick mode in response
        # to its own selectionChanged handler.
        self.set_room_pick_active(False)
        self._stack.setCurrentWidget(self._empty)

    def show_label(
        self,
        *,
        label: Label,
        label_index: int,
        available_room_ids: list[int],
    ) -> None:
        """Populate the label-edit form for ``label`` and switch to it."""
        self._current_label_index = label_index
        self._current_room_id = None
        self._suppress_signals = True
        try:
            w = self._label_widgets
            w["index"].setText(f"label #{label_index} (room_id={label.room_id})")
            w["id"].setText(label.id)
            w["notes"].setText(label.notes)

            confidence_pct = max(0, min(100, int(round(label.ocr_confidence * 100))))
            w["confidence"].setText(f"{confidence_pct}%")
            x, y, bw, bh = label.bbox
            w["bbox"].setText(f"x={x}, y={y}, w={bw}, h={bh}")

            self._repopulate_room_combo(available_room_ids, label.room_id)
            # Switching to a different label always cancels any in-flight
            # pick-room mode — picking a room belongs to whichever label was
            # selected at the time the user pressed the button.
            self._set_pick_button_checked(False)
        finally:
            self._suppress_signals = False

        self._stack.setCurrentWidget(self._label_form)

    def set_room_pick_active(self, active: bool) -> None:
        """Sync the pick-room button's checked state from the controller.

        Called when the canvas exits pick mode (room picked, Esc pressed,
        selection lost) so the inspector button reflects reality without the
        controller having to know about its internal widgets.
        """
        self._set_pick_button_checked(active)

    def show_room(self, *, room: Room, labels: list[Label]) -> None:
        """Populate the read-only room summary and switch to it.

        Shows a "Create label for this room" button when the room has no
        labels — a one-click shortcut for the most common orphan-room
        workflow ("the OCR missed this room entirely").
        """
        self._current_label_index = None
        # A room is selected — any in-flight pick-room mode no longer has a
        # target label, so reset the picker button visually too.
        self.set_room_pick_active(False)
        w = self._room_widgets
        w["id"].setText(str(room.id))
        w["area"].setText(f"{room.area_px:,} px²")
        x, y, bw, bh = room.bbox
        w["bbox"].setText(f"x={x}, y={y}, w={bw}, h={bh}")
        if labels:
            w["labels"].setText(
                ", ".join(lab.id for lab in labels) if labels else "(none)"
            )
        else:
            w["labels"].setText("(none — orphan room)")
        # Remember the current room id so the create-button click handler
        # knows which room to attach the new label to. Stored even when
        # the button is hidden (cheap; avoids reading the QLabel text back
        # out and parsing it).
        self._current_room_id = room.id
        w["create_button"].setVisible(not labels)
        self._stack.setCurrentWidget(self._room_form)

    # ----------------------------------------------------- build UI

    def _build_empty_widget(self) -> QtWidgets.QWidget:
        w = QtWidgets.QLabel(
            "Click a label box or a room polygon to edit it.\n\n"
            "Layer toggles:  L=labels  R=rooms  O=orphans-only\n"
            "Zoom: mouse wheel · Pan: drag · 0=fit"
        )
        w.setAlignment(QtCore.Qt.AlignmentFlag.AlignCenter)
        w.setStyleSheet("color: #888;")
        return w

    def _build_label_form(self) -> tuple[QtWidgets.QWidget, dict]:
        form_box = QtWidgets.QGroupBox("Label")
        form = QtWidgets.QFormLayout(form_box)
        form.setLabelAlignment(QtCore.Qt.AlignmentFlag.AlignRight)

        index_lbl = QtWidgets.QLabel()
        index_lbl.setStyleSheet("color: #888; font-size: 11px;")
        form.addRow(index_lbl)

        id_edit = QtWidgets.QLineEdit()
        id_edit.editingFinished.connect(self._emit_id_changed)
        form.addRow("ID:", id_edit)

        room_combo = QtWidgets.QComboBox()
        room_combo.currentIndexChanged.connect(self._emit_room_changed)
        # Wrap the combo + a "pick room visually" button on the same form row
        # so the user can either type/pick by id or click the room on the map.
        # The button is checkable: clicking it enters pick mode (controller
        # changes the canvas cursor + intercepts the next room click);
        # clicking it again, pressing Esc, or completing the pick uncheck it.
        room_pick_button = QtWidgets.QToolButton()
        room_pick_button.setText("📍")
        room_pick_button.setCheckable(True)
        room_pick_button.setToolTip(
            "Pick room visually: click here, then click the room on the map "
            "you want to link this label to. Press Esc to cancel."
        )
        room_pick_button.toggled.connect(self._emit_room_pick_requested)
        room_row = QtWidgets.QWidget()
        room_row_layout = QtWidgets.QHBoxLayout(room_row)
        room_row_layout.setContentsMargins(0, 0, 0, 0)
        room_row_layout.setSpacing(4)
        room_row_layout.addWidget(room_combo, 1)
        room_row_layout.addWidget(room_pick_button)
        form.addRow("Room:", room_row)

        notes_edit = QtWidgets.QLineEdit()
        notes_edit.editingFinished.connect(self._emit_notes_changed)
        form.addRow("Notes:", notes_edit)

        # Read-only telemetry rows.
        confidence_lbl = QtWidgets.QLabel()
        confidence_lbl.setTextInteractionFlags(
            QtCore.Qt.TextInteractionFlag.TextSelectableByMouse
        )
        form.addRow("OCR confidence:", confidence_lbl)

        bbox_lbl = QtWidgets.QLabel()
        bbox_lbl.setTextInteractionFlags(
            QtCore.Qt.TextInteractionFlag.TextSelectableByMouse
        )
        form.addRow("Bbox (read-only):", bbox_lbl)

        wrapper = QtWidgets.QWidget()
        wrap_layout = QtWidgets.QVBoxLayout(wrapper)
        wrap_layout.setContentsMargins(0, 0, 0, 0)
        wrap_layout.addWidget(form_box)
        wrap_layout.addStretch(1)

        widgets = {
            "index": index_lbl,
            "id": id_edit,
            "room": room_combo,
            "room_pick_button": room_pick_button,
            "notes": notes_edit,
            "confidence": confidence_lbl,
            "bbox": bbox_lbl,
        }
        return wrapper, widgets

    def _build_room_form(self) -> tuple[QtWidgets.QWidget, dict]:
        box = QtWidgets.QGroupBox("Room")
        form = QtWidgets.QFormLayout(box)
        form.setLabelAlignment(QtCore.Qt.AlignmentFlag.AlignRight)

        id_lbl = QtWidgets.QLabel()
        id_lbl.setTextInteractionFlags(
            QtCore.Qt.TextInteractionFlag.TextSelectableByMouse
        )
        form.addRow("Room ID:", id_lbl)

        area_lbl = QtWidgets.QLabel()
        form.addRow("Area:", area_lbl)

        bbox_lbl = QtWidgets.QLabel()
        bbox_lbl.setTextInteractionFlags(
            QtCore.Qt.TextInteractionFlag.TextSelectableByMouse
        )
        form.addRow("Bbox:", bbox_lbl)

        labels_lbl = QtWidgets.QLabel()
        labels_lbl.setWordWrap(True)
        labels_lbl.setTextInteractionFlags(
            QtCore.Qt.TextInteractionFlag.TextSelectableByMouse
        )
        form.addRow("Labels here:", labels_lbl)

        # "Create label" affordance: visible only when the selected room
        # has no labels (set by ``show_room``). Lets the user fix an
        # orphan room with one click instead of "add label tool, click
        # the room, type id" — same end result, fewer steps when the
        # user is already looking at the offending room.
        create_button = QtWidgets.QPushButton("Create label for this room")
        create_button.setToolTip(
            "Add a new label centered on this room. You'll be prompted "
            "for the label id (e.g. office number). Undoable."
        )
        create_button.clicked.connect(self._emit_create_label_for_room)
        create_button.setVisible(False)

        # Polygon-edit reminder. Editing polygons is a Future Work item;
        # the fill-mask workflow via wall_patches is the supported path.
        hint = QtWidgets.QLabel(
            "Editing room polygons is not supported in this editor. "
            "To fix a merged or split room, edit ``wall_patches`` in "
            "calibration.json and re-run ``calibrate``."
        )
        hint.setWordWrap(True)
        hint.setStyleSheet("color: #888; font-size: 11px;")

        wrapper = QtWidgets.QWidget()
        wrap_layout = QtWidgets.QVBoxLayout(wrapper)
        wrap_layout.setContentsMargins(0, 0, 0, 0)
        wrap_layout.addWidget(box)
        wrap_layout.addWidget(create_button)
        wrap_layout.addWidget(hint)
        wrap_layout.addStretch(1)

        widgets = {
            "id": id_lbl,
            "area": area_lbl,
            "bbox": bbox_lbl,
            "labels": labels_lbl,
            "create_button": create_button,
        }
        return wrapper, widgets

    # ------------------------------------------------- combo helpers

    def _repopulate_room_combo(
        self, available_room_ids: list[int], current_room_id: Optional[int]
    ) -> None:
        combo: QtWidgets.QComboBox = self._label_widgets["room"]
        combo.blockSignals(True)
        try:
            combo.clear()
            combo.addItem("(none — orphan)", _ORPHAN_SENTINEL)
            for rid in available_room_ids:
                combo.addItem(f"room {rid}", rid)
            target = _ORPHAN_SENTINEL if current_room_id is None else current_room_id
            idx = combo.findData(target)
            if idx < 0:
                # The room_id refers to a room we don't have a polygon for
                # (data corruption?) — show it anyway so the user can fix it.
                combo.addItem(f"room {current_room_id} (unknown!)", current_room_id)
                idx = combo.count() - 1
            combo.setCurrentIndex(idx)
        finally:
            combo.blockSignals(False)

    # ----------------------------------------------- signal forwarders

    def _emit_id_changed(self) -> None:
        if self._suppress_signals or self._current_label_index is None:
            return
        new_id = self._label_widgets["id"].text().strip()
        if not new_id:
            # Don't allow blanking out the id — the data model treats id as a
            # required string. Revert the field to the previous value by
            # re-firing show_label() once the controller responds; for now,
            # just swallow the event.
            return
        self.label_id_changed.emit(self._current_label_index, new_id)

    def _emit_notes_changed(self) -> None:
        if self._suppress_signals or self._current_label_index is None:
            return
        new_notes = self._label_widgets["notes"].text()
        self.label_notes_changed.emit(self._current_label_index, new_notes)

    def _emit_room_changed(self, combo_index: int) -> None:
        if self._suppress_signals or self._current_label_index is None:
            return
        combo: QtWidgets.QComboBox = self._label_widgets["room"]
        data = combo.itemData(combo_index)
        if data == _ORPHAN_SENTINEL:
            new_room_id: Optional[int] = None
        else:
            new_room_id = int(data)
        self.label_room_changed.emit(self._current_label_index, new_room_id)

    def _emit_room_pick_requested(self, checked: bool) -> None:
        # Programmatic check/uncheck (e.g. ``set_room_pick_active``) must not
        # bounce back through the controller — only user clicks on the
        # button should request the canvas to enter / leave pick mode.
        if self._suppress_signals or self._current_label_index is None:
            return
        self.room_pick_requested.emit(self._current_label_index, checked)

    def _emit_create_label_for_room(self) -> None:
        # Guarded against signal bouncing the same way as the other emitters.
        # ``_current_room_id`` is set whenever the room form is shown, so a
        # ``None`` here means the user somehow clicked the button while no
        # room was selected — silently ignore rather than crash.
        if self._suppress_signals or self._current_room_id is None:
            return
        self.create_label_for_room.emit(self._current_room_id)

    def _set_pick_button_checked(self, checked: bool) -> None:
        """Set the button state without re-emitting ``room_pick_requested``."""
        button = self._label_widgets.get("room_pick_button")
        if button is None:
            return
        if button.isChecked() == checked:
            return
        # Toggle while suppress_signals is True so the toggled() handler
        # short-circuits. We can't simply blockSignals here because the
        # controller listens to the signal via this object's slot, not the
        # button's; suppress_signals is the established guard.
        previous = self._suppress_signals
        self._suppress_signals = True
        try:
            button.setChecked(checked)
        finally:
            self._suppress_signals = previous


__all__ = ["InspectorPanel"]
