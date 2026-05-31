"""Step 3 — Validate fill (advisory).

This step virtual-flood-fills every labeled room and reports any
leaks: filled area much larger than the polygon (wall gap), filled
area larger than the median (polygon may itself be two rooms merged),
fill reaching another office's seed (rooms connected through a gap),
or filled area too small (seed sat on a wall).

Since render-time leak clipping was added in commit ``e38ddae`` (see
plan §12.6), **all leak codes are warnings**: the renderer auto-clips
each fill to its polygon, so leaks can never bleed into the composite.
This step is therefore advisory only -- it's a polish tool that lets
the user add ``wall_patches`` to silence warnings before sharing the
file, but the wizard never blocks Next on a leak.

Like ``ValidateLabelsStep``, the UI is a ``QStackedWidget`` with three
panes (no-calibration / landing / results). The results pane is a table
with per-row "Show in editor" / "Ignore" actions; "Show in editor"
jumps back to Step 1 and selects either the offending room polygon
(for ``leak_oversized_vs_median``) or the offending label (for the
other codes) via ``MainWindow.navigate_to_step``.
"""

from __future__ import annotations

from pathlib import Path
from typing import List, Optional, Tuple

from PySide6 import QtCore, QtGui, QtWidgets

from ...calibration import Calibration
from ...validate import FillLeak, validate_fill
from ..main_window import StepStatus
from .base import StepBase


# ---------------------------------------------------------------------------
# Pipeline adapter: validate_fill() doesn't accept progress_cb/cancel_cb and
# returns just ``list[FillLeak]``. Wrap it so ``PipelineRunner`` can call it.
# ---------------------------------------------------------------------------


def _run_validate_fill(
    map_path: Path,
    calibration: Calibration,
    *,
    progress_cb,
    cancel_cb,
) -> Tuple[Optional[object], List[FillLeak]]:
    """Worker-thread entry point.

    Returns ``(None, leaks)`` because ``validate_fill`` produces only
    diagnostics -- there's no cached artifact downstream steps will
    consume. The first slot is ``None`` to satisfy the runner's
    2-tuple contract.
    """
    progress_cb(0.0, "Building wall mask...")
    if cancel_cb():
        from ...pipeline import PipelineCanceled

        raise PipelineCanceled()
    progress_cb(0.2, "Flood-filling each labeled room...")
    leaks = validate_fill(map_path, calibration)
    progress_cb(1.0, "Done")
    return None, leaks


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


# Codes for which the most useful jump-back is to select the room
# polygon (the user is being asked "is this polygon actually two rooms
# the OCR/CC step glued together?"). All other codes are about the gap
# in the walls of one specific room, where selecting the label (and
# bringing it into view) is the right answer.
_ROOM_CENTRIC_CODES = frozenset({"leak_oversized_vs_median"})


def _classify_issues(
    issues: List[FillLeak],
) -> Tuple[StepStatus, List[str], List[str], List[str]]:
    """Map a list of ``FillLeak`` to (step status, messages, per-leak
    codes, per-leak severities).

    Since ``e38ddae`` demoted every leak code to ``severity="warning"``,
    we never raise to ERROR here. Any non-ignored leak -> ADVISORY
    (informational; Next still allowed). Empty -> OK.

    Defensive: if a future code re-introduces ``severity="error"``
    we still escalate to ERROR rather than silently swallowing it.
    """
    has_err = any(i.severity == "error" for i in issues)
    has_any = bool(issues)
    if has_err:
        status = StepStatus.ERROR
    elif has_any:
        status = StepStatus.ADVISORY
    else:
        status = StepStatus.OK
    # FillLeaks live at ADVISORY level so the chip color stays blue
    # rather than orange / red; use that for all rows regardless of
    # the per-issue ``severity`` field (which may still say
    # "warning" from the validator's own taxonomy).
    sevs = ["advisory"] * len(issues) if not has_err else [
        i.severity if i.severity == "error" else "advisory" for i in issues
    ]
    return (
        status,
        [str(i) for i in issues],
        [i.code for i in issues],
        sevs,
    )


def _issue_key(leak: FillLeak) -> str:
    """A stable key for the in-memory ignored set.

    Uniquely identifies the (code, office_id, leak_into_office_id) triple
    so the same leak can be re-ignored after a Re-run iff the user
    chooses to (Re-run clears the set anyway).
    """
    parts = (
        leak.code,
        leak.office_id or "",
        leak.leak_into_office_id or "",
    )
    return "|".join(parts)


# ---------------------------------------------------------------------------
# Widget
# ---------------------------------------------------------------------------


class ValidateFillStep(StepBase):
    """Wizard pane for the Validate-fill step."""

    STEP_ID = "validate_fill"

    def __init__(self, main_window) -> None:
        super().__init__(main_window)

        # In-memory ignore set keyed by ``_issue_key``. Resets on Re-run.
        self._ignored: set[str] = set()
        # Cached results from the last run (or None before first run).
        self._last_leaks: Optional[List[FillLeak]] = None

        outer = QtWidgets.QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)

        self._stack = QtWidgets.QStackedWidget(self)
        outer.addWidget(self._stack)

        self._no_cal_pane = self._build_no_calibration_pane()
        self._landing_pane = self._build_landing_pane()
        self._results_pane = self._build_results_pane()

        self._stack.addWidget(self._no_cal_pane)
        self._stack.addWidget(self._landing_pane)
        self._stack.addWidget(self._results_pane)

    # ------------------------------------------------------------------
    # Pane builders
    # ------------------------------------------------------------------

    def _build_no_calibration_pane(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(40, 40, 40, 40)
        layout.setSpacing(16)

        title = QtWidgets.QLabel("Step 1 (Calibrate) needs to finish first")
        f = title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        title.setFont(f)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(
            "This step looks for flood-fill leaks in each labeled room. "
            "We don't have a calibration yet -- go back to Step 1, run "
            "calibration, then come back here."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        btn = QtWidgets.QPushButton("Back to Step 1: Calibrate")
        btn.setMaximumWidth(260)
        btn.setMinimumHeight(36)
        btn.clicked.connect(self._on_back_to_calibrate_clicked)
        layout.addWidget(btn)

        layout.addStretch(1)
        return widget

    def _build_landing_pane(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(40, 40, 40, 40)
        layout.setSpacing(16)

        title = QtWidgets.QLabel("Validate fill (advisory)")
        f = title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        title.setFont(f)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(
            "This step flood-fills every labeled room and reports any leaks "
            "through wall gaps. It's purely advisory -- the renderer clips "
            "each fill to its room polygon so leaks can't bleed into the "
            "composite. Use this step's results if you want to add "
            "wall_patches in Step 1 for a perfectly clean calibration."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        self._run_button = QtWidgets.QPushButton("Run fill check")
        self._run_button.setMinimumHeight(36)
        self._run_button.setMaximumWidth(220)
        self._run_button.clicked.connect(self._on_run_clicked)
        layout.addWidget(self._run_button)

        layout.addStretch(1)
        return widget

    def _build_results_pane(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.setSpacing(8)

        # Header row: summary on the left, Re-run on the right.
        header_row = QtWidgets.QHBoxLayout()
        self._summary_label = QtWidgets.QLabel("")
        f = self._summary_label.font()
        f.setBold(True)
        self._summary_label.setFont(f)
        header_row.addWidget(self._summary_label)
        header_row.addStretch(1)

        self._rerun_button = QtWidgets.QPushButton("Re-run fill check")
        self._rerun_button.clicked.connect(self._on_rerun_clicked)
        header_row.addWidget(self._rerun_button)
        layout.addLayout(header_row)

        # Empty-state label (no leaks found, or all ignored).
        self._empty_label = QtWidgets.QLabel(
            "No fill leaks detected. Click Next to continue."
        )
        self._empty_label.setStyleSheet(
            "QLabel { color: #2e7d32; padding: 16px; "
            "background-color: #e8f5e9; border-radius: 4px; }"
        )
        self._empty_label.setVisible(False)
        layout.addWidget(self._empty_label)

        # The table of remaining (non-ignored) leaks.
        self._table = QtWidgets.QTableWidget(0, 5, widget)
        self._table.setHorizontalHeaderLabels(
            ["Severity", "Office", "Code", "Issue", "Actions"]
        )
        self._table.verticalHeader().setVisible(False)
        self._table.setEditTriggers(
            QtWidgets.QAbstractItemView.EditTrigger.NoEditTriggers
        )
        self._table.setSelectionBehavior(
            QtWidgets.QAbstractItemView.SelectionBehavior.SelectRows
        )
        header = self._table.horizontalHeader()
        header.setSectionResizeMode(
            0, QtWidgets.QHeaderView.ResizeMode.ResizeToContents
        )
        header.setSectionResizeMode(
            1, QtWidgets.QHeaderView.ResizeMode.ResizeToContents
        )
        header.setSectionResizeMode(
            2, QtWidgets.QHeaderView.ResizeMode.ResizeToContents
        )
        header.setSectionResizeMode(3, QtWidgets.QHeaderView.ResizeMode.Stretch)
        header.setSectionResizeMode(
            4, QtWidgets.QHeaderView.ResizeMode.ResizeToContents
        )
        layout.addWidget(self._table)

        # Footer status: how many ignored.
        self._footer_label = QtWidgets.QLabel("")
        self._footer_label.setStyleSheet("QLabel { color: #777; }")
        layout.addWidget(self._footer_label)

        return widget

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def on_activated(self) -> None:
        if self.main_window.session.calibration is None:
            self._stack.setCurrentWidget(self._no_cal_pane)
            return

        if self._last_leaks is None:
            self._stack.setCurrentWidget(self._landing_pane)
            return

        # Already have results -- show them.
        self._stack.setCurrentWidget(self._results_pane)

    # ------------------------------------------------------------------
    # Run / re-run
    # ------------------------------------------------------------------

    def _on_run_clicked(self) -> None:
        self._kick_off_check()

    def _on_rerun_clicked(self) -> None:
        # Reset ignored set + cached results, then re-fire. We do NOT
        # invalidate downstream steps here -- validate_fill has no
        # cached artifact other steps depend on; the only thing this
        # check influences is whether the user wants to add more
        # wall_patches in Step 1 (which has its own invalidation
        # cascade).
        self._ignored.clear()
        self._last_leaks = None
        self._kick_off_check()

    def _kick_off_check(self) -> None:
        self._run_button.setEnabled(False)
        self._rerun_button.setEnabled(False)
        cal = self.main_window.session.calibration
        if cal is None:
            self._stack.setCurrentWidget(self._no_cal_pane)
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)
            return

        runner = self.main_window.run_pipeline_step(
            self.STEP_ID,
            _run_validate_fill,
            args=(self.main_window.map_path, cal),
            on_finished=self._on_check_finished,
            on_failed=self._on_check_failed,
            on_canceled=self._on_check_canceled,
        )
        if runner is None:
            # Another runner in flight; re-enable buttons so the user
            # can try again after the other run finishes.
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)

    def _on_check_finished(self, result, issues: list) -> None:
        # Adapter returns (None, leaks); result is None, issues is
        # the list of FillLeaks already split by the runner.
        self._last_leaks = list(issues)
        self._populate_table()
        self._stack.setCurrentWidget(self._results_pane)
        self._refresh_status()
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_check_failed(self, exc: BaseException) -> None:
        # MainWindow already sets ERROR + the one-line summary; just
        # re-enable our buttons.
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_check_canceled(self) -> None:
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    # ------------------------------------------------------------------
    # Table population
    # ------------------------------------------------------------------

    def _populate_table(self) -> None:
        """Rebuild the table from ``_last_leaks`` minus ``_ignored``."""
        self._table.setRowCount(0)
        visible = self._visible_leaks()

        if not visible:
            self._empty_label.setVisible(True)
            self._table.setVisible(False)
        else:
            self._empty_label.setVisible(False)
            self._table.setVisible(True)
            for row_idx, leak in enumerate(visible):
                self._append_row(row_idx, leak)

        self._refresh_summary()

    def _visible_leaks(self) -> List[FillLeak]:
        if not self._last_leaks:
            return []
        return [
            leak
            for leak in self._last_leaks
            if _issue_key(leak) not in self._ignored
        ]

    def _append_row(self, row_idx: int, leak: FillLeak) -> None:
        self._table.insertRow(row_idx)

        # Severity badge: warnings (the common case) get orange; the
        # defensive error path gets red.
        sev_item = QtWidgets.QTableWidgetItem(leak.severity.upper())
        if leak.severity == "error":
            sev_item.setForeground(QtGui.QBrush(QtGui.QColor("#c62828")))
        else:
            sev_item.setForeground(QtGui.QBrush(QtGui.QColor("#ef6c00")))
        f = sev_item.font()
        f.setBold(True)
        sev_item.setFont(f)
        self._table.setItem(row_idx, 0, sev_item)

        self._table.setItem(
            row_idx, 1, QtWidgets.QTableWidgetItem(leak.office_id or "-")
        )
        self._table.setItem(
            row_idx, 2, QtWidgets.QTableWidgetItem(leak.code)
        )

        msg_item = QtWidgets.QTableWidgetItem(leak.message)
        msg_item.setToolTip(leak.message)
        self._table.setItem(row_idx, 3, msg_item)

        # Action buttons.
        actions = QtWidgets.QWidget()
        h = QtWidgets.QHBoxLayout(actions)
        h.setContentsMargins(4, 2, 4, 2)
        h.setSpacing(4)

        # "Show in editor" is contextual: room-centric codes show the
        # polygon, label-centric codes show the label.
        show_btn = QtWidgets.QPushButton("Show in editor")
        if leak.code in _ROOM_CENTRIC_CODES:
            show_btn.setToolTip(
                "Jump to Step 1 and select this room's polygon. "
                "The polygon may itself be two rooms the calibration "
                "glued together -- consider deleting it and re-adding "
                "the two halves separately."
            )
            show_btn.clicked.connect(
                lambda _=False, rid=leak.room_id: self._on_show_room(rid)
            )
        else:
            show_btn.setToolTip(
                "Jump to Step 1 and select this office's label. "
                "Add a wall_patches entry to close the gap if you "
                "want a perfectly clean calibration."
            )
            show_btn.clicked.connect(
                lambda _=False, oid=leak.office_id: self._on_show_label(oid)
            )
        h.addWidget(show_btn)

        ignore_btn = QtWidgets.QPushButton("Ignore")
        ignore_btn.clicked.connect(
            lambda _=False, lk=leak: self._on_ignore_clicked(lk)
        )
        h.addWidget(ignore_btn)
        h.addStretch(1)
        self._table.setCellWidget(row_idx, 4, actions)

    # ------------------------------------------------------------------
    # Action handlers
    # ------------------------------------------------------------------

    def _on_show_label(self, office_id: str) -> None:
        """Navigate to Step 1 and center on the label with ``office_id``."""
        self.main_window.navigate_to_step(0)
        canvas = self._calibrate_canvas()
        if canvas is None:
            return
        indices = canvas.find_label_indices(office_id)
        if indices:
            canvas.center_on_label(indices[0])

    def _on_show_room(self, room_id: int) -> None:
        """Navigate to Step 1 and select the room polygon by id."""
        self.main_window.navigate_to_step(0)
        canvas = self._calibrate_canvas()
        if canvas is None:
            return
        canvas.select_room(room_id)

    def _calibrate_canvas(self):
        """Best-effort lookup of the Step 1 canvas, or None."""
        calibrate_entry = self.main_window._steps[0]
        cal_widget = calibrate_entry.widget
        return getattr(cal_widget, "_canvas", None)

    def _on_ignore_clicked(self, leak: FillLeak) -> None:
        self._ignored.add(_issue_key(leak))
        self._populate_table()
        self._refresh_status()

    def _on_back_to_calibrate_clicked(self) -> None:
        self.main_window.navigate_to_step(0)

    # ------------------------------------------------------------------
    # Status + summary refresh
    # ------------------------------------------------------------------

    def _refresh_status(self) -> None:
        """Push the current (non-ignored) leak set up to MainWindow."""
        visible = self._visible_leaks()
        status, msgs, codes, sevs = _classify_issues(visible)
        self.main_window.set_step_status(
            self.STEP_ID, status, issues=msgs,
            issue_codes=codes, issue_severities=sevs,
        )

    def _refresh_summary(self) -> None:
        if self._last_leaks is None:
            self._summary_label.setText("")
            self._footer_label.setText("")
            return
        total = len(self._last_leaks)
        ignored = len(self._ignored)
        visible = self._visible_leaks()
        if total == 0:
            self._summary_label.setText("No leaks detected.")
        elif not visible:
            self._summary_label.setText(f"All {total} leak(s) ignored.")
        else:
            self._summary_label.setText(f"{len(visible)} leak(s) (advisory)")
        if ignored:
            self._footer_label.setText(f"{ignored} leak(s) ignored")
        else:
            self._footer_label.setText("")
