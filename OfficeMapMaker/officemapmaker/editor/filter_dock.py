"""``FilterDock`` — left-side dock with search box and visibility filters.

Surfaces the three pain-points called out in the editor sub-plan (12.5):
finding a specific label by id, cutting the noise of tiny orphan rooms,
and bulk-hiding rooms that already have labels.

This module is purely view code: it emits signals on change and reads no
mutation state. The main window wires the signals to :class:`MapCanvas`
setters and search helpers; this keeps the dock decoupled from the
canvas (handy for unit tests and for the future "save filter state
across sessions" extension).
"""

from __future__ import annotations

from PySide6 import QtCore, QtGui, QtWidgets


# Slider range for the min-room-area filter. Real-world door-arc polygons
# top out around ~3000 px on the Millennium B map; the 5000 px shoulder
# is where typical small offices begin. Cap at 10000 because anything
# bigger and the user is clearly looking for one specific big space and
# can dial it in with the spinbox; the slider doesn't need to span the
# full range of room sizes on a 4000×4000 map.
_AREA_SLIDER_MIN = 0
_AREA_SLIDER_MAX = 10000
_AREA_SLIDER_STEP = 100
_AREA_DEFAULT = 0


class FilterDock(QtWidgets.QDockWidget):
    """Left-side dock combining a label search box with visibility filters.

    Signals are emitted *after* the underlying widget has been updated so
    the canvas can read the new value via the corresponding getter on
    this dock if it prefers (rather than re-parsing the signal payload).
    """

    # ----------------------------------------------------------- signals

    search_text_changed = QtCore.Signal(str)
    """Emitted on every keystroke in the search box. Payload is the new text."""

    search_submitted = QtCore.Signal(str)
    """Emitted when the user presses Enter / clicks "Find next".

    The main window's slot does a single forward jump (next match after
    the current selection, wrapping). Payload is the text that was in
    the box at submission time.
    """

    find_next_requested = QtCore.Signal()
    """Emitted when the user presses Enter or clicks the Find Next button.

    F3 routes here too. Separate from :attr:`search_submitted` so that
    F3 with no search text simply does nothing (rather than re-emitting
    an empty search).
    """

    find_previous_requested = QtCore.Signal()
    """Emitted on Shift+F3 / Find Previous button."""

    min_room_area_changed = QtCore.Signal(int)
    """Emitted when the min-room-area slider/spinbox changes. Payload: px."""

    hide_labeled_rooms_changed = QtCore.Signal(bool)
    """Emitted when the "Hide labeled rooms" checkbox toggles."""

    orphans_only_changed = QtCore.Signal(bool)
    """Emitted when the "Orphans only" checkbox toggles."""

    # -------------------------------------------------------------- init

    def __init__(self, parent: QtWidgets.QWidget | None = None) -> None:
        super().__init__("Find & filter", parent)
        self.setAllowedAreas(
            QtCore.Qt.DockWidgetArea.LeftDockWidgetArea
            | QtCore.Qt.DockWidgetArea.RightDockWidgetArea
        )
        self.setObjectName("FilterDock")

        body = QtWidgets.QWidget(self)
        body.setMinimumWidth(220)
        layout = QtWidgets.QVBoxLayout(body)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(8)

        layout.addWidget(self._build_search_group(body))
        layout.addWidget(self._build_filters_group(body))
        layout.addStretch(1)

        self.setWidget(body)

    # --------------------------------------------------- internal builders

    def _build_search_group(self, parent: QtWidgets.QWidget) -> QtWidgets.QGroupBox:
        group = QtWidgets.QGroupBox("Find label by id", parent)
        grid = QtWidgets.QGridLayout(group)
        grid.setContentsMargins(8, 6, 8, 8)
        grid.setSpacing(6)

        self._search_box = QtWidgets.QLineEdit(group)
        self._search_box.setPlaceholderText("e.g. 1480 or 1505B")
        self._search_box.setClearButtonEnabled(True)
        self._search_box.textChanged.connect(self.search_text_changed.emit)
        # Enter in the line edit fires returnPressed; treat it as
        # "find next" — also bubbles a search_submitted with the current
        # text for any consumer that wants the latest query.
        self._search_box.returnPressed.connect(self._on_return_pressed)
        grid.addWidget(self._search_box, 0, 0, 1, 2)

        self._results_label = QtWidgets.QLabel("", group)
        # Subdued; just a hint, never the focus.
        self._results_label.setStyleSheet("color: #666;")
        grid.addWidget(self._results_label, 1, 0, 1, 2)

        self._btn_prev = QtWidgets.QPushButton("◀ Prev", group)
        self._btn_prev.setToolTip("Previous match (Shift+F3)")
        self._btn_prev.clicked.connect(self.find_previous_requested.emit)
        grid.addWidget(self._btn_prev, 2, 0)

        self._btn_next = QtWidgets.QPushButton("Next ▶", group)
        self._btn_next.setToolTip("Next match (F3 / Enter)")
        self._btn_next.clicked.connect(self.find_next_requested.emit)
        grid.addWidget(self._btn_next, 2, 1)

        # Disable Prev/Next until there's something to search.
        self._set_search_actions_enabled(False)
        self._search_box.textChanged.connect(
            lambda text: self._set_search_actions_enabled(bool(text.strip()))
        )

        return group

    def _build_filters_group(self, parent: QtWidgets.QWidget) -> QtWidgets.QGroupBox:
        group = QtWidgets.QGroupBox("Filters", parent)
        grid = QtWidgets.QGridLayout(group)
        grid.setContentsMargins(8, 6, 8, 8)
        grid.setSpacing(6)

        # Min room area row: slider + spinbox kept in lockstep. The
        # spinbox is the "exact" control; the slider is for fast coarse
        # adjustments.
        grid.addWidget(QtWidgets.QLabel("Min room area (px):", group), 0, 0, 1, 2)

        self._area_slider = QtWidgets.QSlider(QtCore.Qt.Orientation.Horizontal, group)
        self._area_slider.setRange(_AREA_SLIDER_MIN, _AREA_SLIDER_MAX)
        self._area_slider.setSingleStep(_AREA_SLIDER_STEP)
        self._area_slider.setPageStep(_AREA_SLIDER_STEP * 10)
        self._area_slider.setValue(_AREA_DEFAULT)
        self._area_slider.setTickPosition(QtWidgets.QSlider.TickPosition.TicksBelow)
        self._area_slider.setTickInterval(_AREA_SLIDER_MAX // 5)
        grid.addWidget(self._area_slider, 1, 0)

        self._area_spin = QtWidgets.QSpinBox(group)
        self._area_spin.setRange(0, 10_000_000)  # spin can go above slider
        self._area_spin.setSingleStep(_AREA_SLIDER_STEP)
        self._area_spin.setValue(_AREA_DEFAULT)
        grid.addWidget(self._area_spin, 1, 1)

        # Two-way sync. Each setter on one widget triggers the other,
        # but ``valueChanged`` is only emitted on actual change, so we
        # don't get a feedback loop — Qt suppresses redundant signals.
        # ``blockSignals`` is still a safety belt for the spinbox → slider
        # direction where the spinbox can exceed the slider range.
        self._area_slider.valueChanged.connect(self._on_area_slider_changed)
        self._area_spin.valueChanged.connect(self._on_area_spin_changed)

        # Filter checkboxes. Both default off so the editor's first-launch
        # behaviour exactly matches pre-ed5.
        self._chk_hide_labeled = QtWidgets.QCheckBox("Hide labeled rooms", group)
        self._chk_hide_labeled.toggled.connect(self.hide_labeled_rooms_changed.emit)
        grid.addWidget(self._chk_hide_labeled, 2, 0, 1, 2)

        self._chk_orphans_only = QtWidgets.QCheckBox("Orphans only", group)
        self._chk_orphans_only.setToolTip(
            "Hide healthy labels and labeled rooms — show only "
            "the items that still need attention. (Same as the View menu's "
            "Orphans only / O.)"
        )
        self._chk_orphans_only.toggled.connect(self.orphans_only_changed.emit)
        grid.addWidget(self._chk_orphans_only, 3, 0, 1, 2)

        return group

    # ------------------------------------------------------------ helpers

    def _set_search_actions_enabled(self, enabled: bool) -> None:
        self._btn_next.setEnabled(enabled)
        self._btn_prev.setEnabled(enabled)

    def _on_return_pressed(self) -> None:
        text = self._search_box.text()
        self.search_submitted.emit(text)
        if text.strip():
            self.find_next_requested.emit()

    def _on_area_slider_changed(self, value: int) -> None:
        if self._area_spin.value() != value:
            self._area_spin.blockSignals(True)
            self._area_spin.setValue(value)
            self._area_spin.blockSignals(False)
        self.min_room_area_changed.emit(value)

    def _on_area_spin_changed(self, value: int) -> None:
        # Clamp the slider but let the spinbox track the user's exact value.
        slider_value = max(_AREA_SLIDER_MIN, min(_AREA_SLIDER_MAX, value))
        if self._area_slider.value() != slider_value:
            self._area_slider.blockSignals(True)
            self._area_slider.setValue(slider_value)
            self._area_slider.blockSignals(False)
        self.min_room_area_changed.emit(value)

    # ----------------------------------------------------- public surface

    def focus_search(self) -> None:
        """Focus the search line edit and select-all (Ctrl+F-style)."""
        self._search_box.setFocus(QtCore.Qt.FocusReason.ShortcutFocusReason)
        self._search_box.selectAll()

    def search_text(self) -> str:
        return self._search_box.text()

    def set_results_text(self, text: str) -> None:
        """Update the "N of M" hint under the search box."""
        self._results_label.setText(text)

    def min_room_area(self) -> int:
        return self._area_spin.value()

    def set_min_room_area(self, value: int) -> None:
        # Set via the spinbox so the clamp + slider sync logic runs.
        self._area_spin.setValue(int(value))

    def hide_labeled_rooms(self) -> bool:
        return self._chk_hide_labeled.isChecked()

    def set_hide_labeled_rooms(self, checked: bool) -> None:
        self._chk_hide_labeled.setChecked(bool(checked))

    def orphans_only(self) -> bool:
        return self._chk_orphans_only.isChecked()

    def set_orphans_only(self, checked: bool) -> None:
        # Allow the canvas / menu toggle to drive this without re-firing
        # the signal back; QCheckBox suppresses no-op setChecked calls,
        # so the only loop-risk is bouncing between the dock and the menu
        # — and the menu action uses ``blockSignals`` for the same reason.
        self._chk_orphans_only.setChecked(bool(checked))

    # ----------------------------------------------------------- testing

    # Tests poke these directly to simulate user actions without spinning
    # a real event loop.

    def _search_box_widget(self) -> QtWidgets.QLineEdit:
        return self._search_box

    def _area_slider_widget(self) -> QtWidgets.QSlider:
        return self._area_slider

    def _area_spin_widget(self) -> QtWidgets.QSpinBox:
        return self._area_spin

    def _hide_labeled_checkbox(self) -> QtWidgets.QCheckBox:
        return self._chk_hide_labeled

    def _orphans_only_checkbox(self) -> QtWidgets.QCheckBox:
        return self._chk_orphans_only
