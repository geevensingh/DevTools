"""Step 2 — Validate labels.

This step cross-checks the assignments spreadsheet against the
calibration produced by Step 1:

* Every assigned office must match exactly one calibration label
  (errors: ``office_not_on_map``, ``ambiguous_office``).
* Optional warnings: low-confidence-unmatched labels (probable OCR
  misreads), duplicate spreadsheet rows, near-duplicate team names.

The pure ``validate_labels()`` function lives in ``validate.py``; this
file is the wizard wrapper that runs it on a worker thread and presents
results in a table with per-row "Show in editor" / "Ignore" actions.

Like ``CalibrateStep``, the UI is a ``QStackedWidget`` with three panes:

  * **No-calibration** — shown when ``session.calibration`` is ``None``
    (the user navigated to step 2 before completing step 1). Button
    sends them back to step 1.
  * **Landing** — calibration exists but no run yet. Description + big
    "Run validation" button.
  * **Results** — table of issues with action buttons + Re-run button.
"""

from __future__ import annotations

from typing import List, Optional, Tuple

from PySide6 import QtCore, QtGui, QtWidgets

from ...calibration import Calibration
from ...io_assignments import Assignment, load_assignments
from ...validate import ValidationIssue, validate_labels
from ..main_window import StepStatus
from .base import StepBase


# ---------------------------------------------------------------------------
# Pipeline adapter: validate_labels() doesn't accept progress_cb/cancel_cb
# and doesn't return a (result, issues) tuple. Wrap it so PipelineRunner
# can call it directly.
# ---------------------------------------------------------------------------


def _run_validate_labels(
    calibration: Calibration,
    assignments_path,
    *,
    progress_cb,
    cancel_cb,
) -> Tuple[List[Assignment], List[ValidationIssue]]:
    """Worker-thread entry point.

    Returns ``(assignments, issues)`` so the wizard can stash the
    loaded assignments on the session for later steps (layout, build)
    and surface ``issues`` via the standard runner contract.
    """
    progress_cb(0.0, "Loading assignments...")
    assignments = load_assignments(assignments_path)
    if cancel_cb():
        from ...pipeline import PipelineCanceled

        raise PipelineCanceled()
    progress_cb(0.5, f"Validating {len(assignments)} assignment(s)...")
    issues = validate_labels(calibration, assignments)
    progress_cb(1.0, "Done")
    return assignments, issues


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _classify_issues(
    issues: List[ValidationIssue],
) -> Tuple[StepStatus, List[str], List[str], List[str]]:
    """Map a list of ``ValidationIssue`` to (step status, messages,
    per-issue codes, per-issue severities)."""
    has_err = any(i.severity == "error" for i in issues)
    has_warn = any(i.severity == "warning" for i in issues)
    if has_err:
        status = StepStatus.ERROR
    elif has_warn:
        status = StepStatus.WARNING
    else:
        status = StepStatus.OK
    return (
        status,
        [str(i) for i in issues],
        [i.code for i in issues],
        [i.severity for i in issues],
    )


def _issue_key(issue: ValidationIssue) -> str:
    """A stable key used to track ignored issues within a session.

    Uniquely identifies the (code, person, office_id, source_row) tuple.
    """
    parts = (
        issue.code,
        issue.person or "",
        issue.office_id or "",
        str(issue.source_row or ""),
    )
    return "|".join(parts)


# ---------------------------------------------------------------------------
# Widget
# ---------------------------------------------------------------------------


class ValidateLabelsStep(StepBase):
    """Wizard pane for the Validate-labels step."""

    STEP_ID = "validate_labels"

    def __init__(self, main_window) -> None:
        super().__init__(main_window)

        # In-memory ignore set keyed by ``_issue_key``. Resets on Re-run
        # (intentionally -- "ignore" means "for this set of results").
        self._ignored: set[str] = set()
        # Cached results from the last run (or None before first run).
        self._last_issues: Optional[List[ValidationIssue]] = None

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
            "This step compares each person's assigned office against the "
            "labels detected on the map. We don't have a calibration yet -- "
            "go back to Step 1, run calibration, then come back here."
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

        title = QtWidgets.QLabel("Validate labels against assignments")
        f = title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        title.setFont(f)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(
            "This step opens the assignments spreadsheet and confirms that "
            "every person's office number matches a label on the map. Errors "
            "(missing offices, ambiguous offices) block the next step. "
            "Warnings (probable OCR misreads, duplicate rows) are informational."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        self._run_button = QtWidgets.QPushButton("Run validation")
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

        self._rerun_button = QtWidgets.QPushButton("Re-run validation")
        self._rerun_button.clicked.connect(self._on_rerun_clicked)
        header_row.addWidget(self._rerun_button)
        layout.addLayout(header_row)

        # Empty-state label, shown when there are no remaining issues.
        self._empty_label = QtWidgets.QLabel(
            "All assignments validated. Click Next to continue."
        )
        self._empty_label.setStyleSheet(
            "QLabel { color: #2e7d32; padding: 16px; "
            "background-color: #e8f5e9; border-radius: 4px; }"
        )
        self._empty_label.setVisible(False)
        layout.addWidget(self._empty_label)

        # The table of remaining (non-ignored) issues.
        self._table = QtWidgets.QTableWidget(0, 5, widget)
        self._table.setHorizontalHeaderLabels(
            ["Severity", "Person", "Office", "Issue", "Actions"]
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

        if self._last_issues is None:
            self._stack.setCurrentWidget(self._landing_pane)
            return

        # Already have results -- show them.
        self._stack.setCurrentWidget(self._results_pane)

    # ------------------------------------------------------------------
    # Run / re-run
    # ------------------------------------------------------------------

    def _on_run_clicked(self) -> None:
        self._kick_off_validation()

    def _on_rerun_clicked(self) -> None:
        # Reset ignored set + cached issues, then re-fire. We do NOT
        # invalidate downstream steps here because validate_labels
        # itself doesn't produce a cached artifact -- it just gates on
        # error count. Downstream steps are invalidated naturally
        # whenever the user changes calibration in Step 1 (which
        # cascades through Session.invalidate_from).
        self._ignored.clear()
        self._last_issues = None
        self._kick_off_validation()

    def _kick_off_validation(self) -> None:
        self._run_button.setEnabled(False)
        self._rerun_button.setEnabled(False)
        cal = self.main_window.session.calibration
        if cal is None:
            # Shouldn't be reachable (we show no-cal pane when this
            # is true), but defend anyway.
            self._stack.setCurrentWidget(self._no_cal_pane)
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)
            return

        runner = self.main_window.run_pipeline_step(
            self.STEP_ID,
            _run_validate_labels,
            args=(cal, self.main_window.assignments_path),
            on_finished=self._on_validation_finished,
            on_failed=self._on_validation_failed,
            on_canceled=self._on_validation_canceled,
        )
        if runner is None:
            # Another runner in flight; re-enable buttons so the user
            # can try again after the other run finishes.
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)

    def _on_validation_finished(self, result, issues: list) -> None:
        # The runner splits the adapter's (assignments, issues) return
        # tuple into separate signal args: ``result`` is the assignments
        # list, ``issues`` is the ValidationIssue list.
        assignments = result
        # Stash assignments on the session so later steps (W7 layout,
        # W8 build) don't have to reload. We don't add a dedicated
        # field on Session yet -- attach as a non-persistent attribute.
        # W7+ may promote this to a proper session field.
        self.main_window.session._cached_assignments = assignments  # type: ignore[attr-defined]

        self._last_issues = list(issues)
        self._populate_table()
        self._stack.setCurrentWidget(self._results_pane)
        self._refresh_status()
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_validation_failed(self, exc: BaseException) -> None:
        # MainWindow already sets ERROR + the one-line summary; just
        # re-enable our buttons.
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_validation_canceled(self) -> None:
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    # ------------------------------------------------------------------
    # Table population
    # ------------------------------------------------------------------

    def _populate_table(self) -> None:
        """Rebuild the table from ``_last_issues`` minus ``_ignored``."""
        self._table.setRowCount(0)
        visible = self._visible_issues()

        if not visible:
            self._empty_label.setVisible(True)
            self._table.setVisible(False)
        else:
            self._empty_label.setVisible(False)
            self._table.setVisible(True)
            for row_idx, issue in enumerate(visible):
                self._append_row(row_idx, issue)

        self._refresh_summary()

    def _visible_issues(self) -> List[ValidationIssue]:
        if not self._last_issues:
            return []
        return [i for i in self._last_issues if _issue_key(i) not in self._ignored]

    def _append_row(self, row_idx: int, issue: ValidationIssue) -> None:
        self._table.insertRow(row_idx)

        # Severity icon-ish text + color tint.
        sev_item = QtWidgets.QTableWidgetItem(issue.severity.upper())
        if issue.severity == "error":
            sev_item.setForeground(QtGui.QBrush(QtGui.QColor("#c62828")))
        else:
            sev_item.setForeground(QtGui.QBrush(QtGui.QColor("#ef6c00")))
        f = sev_item.font()
        f.setBold(True)
        sev_item.setFont(f)
        self._table.setItem(row_idx, 0, sev_item)

        self._table.setItem(
            row_idx, 1, QtWidgets.QTableWidgetItem(issue.person or "-")
        )
        self._table.setItem(
            row_idx, 2, QtWidgets.QTableWidgetItem(issue.office_id or "-")
        )

        msg_item = QtWidgets.QTableWidgetItem(issue.message)
        msg_item.setToolTip(issue.message)
        self._table.setItem(row_idx, 3, msg_item)

        # Action buttons.
        actions = QtWidgets.QWidget()
        h = QtWidgets.QHBoxLayout(actions)
        h.setContentsMargins(4, 2, 4, 2)
        h.setSpacing(4)
        if issue.office_id:
            show_btn = QtWidgets.QPushButton("Show in editor")
            show_btn.clicked.connect(
                lambda _=False, oid=issue.office_id: self._on_show_in_editor(oid)
            )
            h.addWidget(show_btn)
        ignore_btn = QtWidgets.QPushButton("Ignore")
        ignore_btn.clicked.connect(
            lambda _=False, iss=issue: self._on_ignore_clicked(iss)
        )
        h.addWidget(ignore_btn)
        h.addStretch(1)
        self._table.setCellWidget(row_idx, 4, actions)

    # ------------------------------------------------------------------
    # Action handlers
    # ------------------------------------------------------------------

    def _on_show_in_editor(self, office_id: str) -> None:
        """Navigate to Step 1 and center on the label with ``office_id``."""
        # Jump to step 0.
        self.main_window.navigate_to_step(0)

        # Step 0 is now active; find the CalibrateStep widget and ask
        # its canvas to center on the matching label. We deliberately
        # use the canvas's case-insensitive substring search so a
        # spreadsheet's "1480" matches a label whose text is "1480"
        # exactly (the search box uses the same lookup, so the UX is
        # consistent).
        calibrate_entry = self.main_window._steps[0]
        cal_widget = calibrate_entry.widget
        canvas = getattr(cal_widget, "_canvas", None)
        if canvas is None:
            return
        indices = canvas.find_label_indices(office_id)
        if indices:
            canvas.center_on_label(indices[0])

    def _on_ignore_clicked(self, issue: ValidationIssue) -> None:
        self._ignored.add(_issue_key(issue))
        self._populate_table()
        self._refresh_status()

    def _on_back_to_calibrate_clicked(self) -> None:
        self.main_window.navigate_to_step(0)

    # ------------------------------------------------------------------
    # Status + summary refresh
    # ------------------------------------------------------------------

    def _refresh_status(self) -> None:
        """Push the current (non-ignored) issue set up to MainWindow."""
        visible = self._visible_issues()
        status, msgs, codes, sevs = _classify_issues(visible)
        self.main_window.set_step_status(
            self.STEP_ID, status, issues=msgs,
            issue_codes=codes, issue_severities=sevs,
        )

    def _refresh_summary(self) -> None:
        if self._last_issues is None:
            self._summary_label.setText("")
            self._footer_label.setText("")
            return
        total = len(self._last_issues)
        ignored = len(self._ignored)
        errs = sum(
            1 for i in self._visible_issues() if i.severity == "error"
        )
        warns = sum(
            1 for i in self._visible_issues() if i.severity == "warning"
        )
        if total == 0:
            self._summary_label.setText("No issues found.")
        else:
            parts = []
            if errs:
                parts.append(f"{errs} error(s)")
            if warns:
                parts.append(f"{warns} warning(s)")
            if not parts:
                self._summary_label.setText(
                    f"All {total} issue(s) ignored."
                )
            else:
                self._summary_label.setText(", ".join(parts))
        if ignored:
            self._footer_label.setText(f"{ignored} issue(s) ignored")
        else:
            self._footer_label.setText("")
