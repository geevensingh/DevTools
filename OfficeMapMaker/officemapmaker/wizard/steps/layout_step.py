"""Step 4 — Plan name layout.

This step takes the calibration from Step 1 plus the assignments
spreadsheet and runs the name-fit planner (see ``officemapmaker.layout``):

* For each office that has at least one person assigned, compute the
  largest inscribed rectangle inside the room polygon.
* Run the abbreviation ladder: full names ``shrink`` to ``initials``
  to ``last_only`` to ``leader`` line.
* Pick a corner for the relocated office number.

Issues come in two flavors:

* ``error`` (blocks Next): ``office_has_no_room``, ``empty_inscribed_rect``,
  ``person_not_placed``. These usually mean the spreadsheet refers to an
  office whose room polygon disagrees with the calibration, or the
  calibration has a degenerate polygon. Either way, the user goes back
  to Step 1 to fix it.
* ``warning`` (Next allowed): ``mixed_teams_in_office`` (multiple teams
  in one office; first team alphabetically wins), ``abbreviation_fallback``
  (names didn't fit at full length), ``leader_line_fallback`` (didn't
  even fit with last-name-only; name placed outside with a callout
  line). These are all "FYI, the planner did its best" cases.

Like ``ValidateLabelsStep`` / ``ValidateFillStep``, the UI is a
``QStackedWidget`` with four panes:

* **No-calibration** — Step 1 hasn't finished. Button sends them back.
* **Landing** — calibration exists but no plan yet. Big "Plan layout"
  button.
* **Results** — splitter with the rendered preview on the left
  (faded map + planned text + leader lines) and the issues table on
  the right. Per-row "Show on map" pans/zooms the preview to that
  office; "Ignore" hides advisory warnings the user has decided are
  fine.

The preview is rendered by ``render_layout_review_png`` on the worker
thread (it's the slow part — iterates every entry and draws text) and
loaded as a ``QPixmap`` into a ``QGraphicsView`` on the main thread.
The view supports wheel-zoom and middle-button drag-pan, same as the
calibrate canvas.
"""

from __future__ import annotations

from pathlib import Path
from typing import List, Optional, Tuple

from PySide6 import QtCore, QtGui, QtWidgets

from ...calibration import Calibration
from ...io_assignments import Assignment, load_assignments
from ...layout import (
    Layout,
    LayoutEntry,
    LayoutIssue,
    plan_layout,
    render_layout_review_png,
)
from ..main_window import StepStatus
from ._preview_view import PreviewGraphicsView
from .base import StepBase


# ---------------------------------------------------------------------------
# Pipeline adapter
# ---------------------------------------------------------------------------


def _run_plan_layout(
    map_path: Path,
    calibration: Calibration,
    assignments_path: Path,
    cached_assignments: Optional[List[Assignment]],
    preview_path: Optional[Path],
    *,
    progress_cb,
    cancel_cb,
) -> Tuple[Tuple[Layout, List[Assignment], Optional[Path]], List[LayoutIssue]]:
    """Worker-thread entry point for Step 4.

    Returns ``((layout, assignments, preview_path_or_none), issues)``.
    Splitting the result this way lets the on-finished handler:

    * stash ``layout`` on ``session.layout``,
    * cache ``assignments`` on the session for W8 (build) to reuse,
    * mount ``preview_path`` (if rendering succeeded) into the
      results pane's QGraphicsView.

    Cancellation is checked at three coarse boundaries (after the
    assignments load, after the plan, and before the preview render).
    Planning itself now reports per-office progress via the inner
    ``progress_cb`` thread-through; the preview render is still a
    single Python call with no inner cancellation hook.
    """
    progress_cb(0.0, "Loading assignments...")
    if cached_assignments is not None:
        assignments = cached_assignments
    else:
        assignments = load_assignments(assignments_path)
    if cancel_cb():
        from ...pipeline import PipelineCanceled

        raise PipelineCanceled()

    progress_cb(0.2, f"Planning layout for {len(assignments)} assignment(s)...")

    # Map planner's 0.0..1.0 per-office progress into the [0.2, 0.85]
    # window of the wizard's overall progress bar.
    def _planner_progress(frac: float, msg: str) -> None:
        progress_cb(0.2 + 0.65 * max(0.0, min(1.0, frac)), msg)

    layout, issues = plan_layout(
        calibration, assignments, progress_cb=_planner_progress,
    )
    if cancel_cb():
        from ...pipeline import PipelineCanceled

        raise PipelineCanceled()

    rendered_preview: Optional[Path] = None
    if preview_path is not None:
        progress_cb(0.85, "Rendering layout preview...")
        try:
            render_layout_review_png(map_path, calibration, layout, preview_path)
            rendered_preview = preview_path
        except Exception:
            # Preview is optional — if it fails (bad PNG, missing font,
            # disk full) the step still has the layout + issues to show.
            rendered_preview = None

    progress_cb(1.0, "Done")
    return (layout, assignments, rendered_preview), issues


# ---------------------------------------------------------------------------
# Issue classification + ignore-key helpers
# ---------------------------------------------------------------------------


def _classify_issues(
    issues: List[LayoutIssue],
) -> Tuple[StepStatus, List[str], List[str], List[str]]:
    """Map a list of ``LayoutIssue`` to (step status, messages, per-issue
    codes, per-issue severities)."""
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


def _issue_key(issue: LayoutIssue) -> str:
    """Stable key for the in-memory ignored set."""
    parts = (issue.code, issue.office_id or "", issue.person or "")
    return "|".join(parts)


# ---------------------------------------------------------------------------
# Widget
# ---------------------------------------------------------------------------


class LayoutStep(StepBase):
    """Wizard pane for the Plan-layout step."""

    STEP_ID = "layout"

    def __init__(self, main_window) -> None:
        super().__init__(main_window)

        # In-memory ignore set keyed by ``_issue_key``. Resets on Re-run.
        self._ignored: set[str] = set()
        # Cached results from the last run (or None before first run).
        self._last_issues: Optional[List[LayoutIssue]] = None
        # Path to the last successfully rendered preview, or None.
        self._preview_path: Optional[Path] = None

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
            "This step plans where each person's name will be drawn inside "
            "their office. We don't have a calibration yet -- go back to "
            "Step 1, run calibration, then come back here."
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

        title = QtWidgets.QLabel("Plan name layout")
        f = title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        title.setFont(f)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(
            "For each office that has at least one person assigned, we compute "
            "the largest rectangle that fits inside the room and lay out the "
            "names there. Long names are abbreviated (first initial, then "
            "last name only) if they don't fit, with a leader-line callout "
            "as a last resort. The preview lets you eyeball the placements "
            "before rendering the final composite."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        self._run_button = QtWidgets.QPushButton("Plan layout")
        self._run_button.setMinimumHeight(36)
        self._run_button.setMaximumWidth(220)
        self._run_button.clicked.connect(self._on_run_clicked)
        layout.addWidget(self._run_button)

        layout.addStretch(1)
        return widget

    def _build_results_pane(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(8, 8, 8, 8)
        layout.setSpacing(6)

        # Header row: summary on the left, Re-run on the right.
        header_row = QtWidgets.QHBoxLayout()
        self._summary_label = QtWidgets.QLabel("")
        f = self._summary_label.font()
        f.setBold(True)
        self._summary_label.setFont(f)
        header_row.addWidget(self._summary_label)
        header_row.addStretch(1)

        self._fit_button = QtWidgets.QPushButton("Fit preview")
        self._fit_button.setToolTip("Reset zoom so the whole preview fits in the view")
        self._fit_button.clicked.connect(self._on_fit_clicked)
        header_row.addWidget(self._fit_button)

        self._rerun_button = QtWidgets.QPushButton("Re-plan layout")
        self._rerun_button.clicked.connect(self._on_rerun_clicked)
        header_row.addWidget(self._rerun_button)
        layout.addLayout(header_row)

        # Splitter: preview on the left, issues table on the right.
        splitter = QtWidgets.QSplitter(QtCore.Qt.Orientation.Horizontal, widget)
        layout.addWidget(splitter, 1)

        # --- Preview side ---
        preview_container = QtWidgets.QWidget(splitter)
        pv_layout = QtWidgets.QVBoxLayout(preview_container)
        pv_layout.setContentsMargins(0, 0, 0, 0)
        pv_layout.setSpacing(4)
        self._preview_view = PreviewGraphicsView(preview_container)
        pv_layout.addWidget(self._preview_view, 1)
        self._no_preview_label = QtWidgets.QLabel(
            "No preview available — the planner may have produced no offices, "
            "or rendering the preview failed."
        )
        self._no_preview_label.setWordWrap(True)
        self._no_preview_label.setAlignment(QtCore.Qt.AlignmentFlag.AlignCenter)
        self._no_preview_label.setStyleSheet(
            "QLabel { color: #777; padding: 24px; }"
        )
        self._no_preview_label.setVisible(False)
        pv_layout.addWidget(self._no_preview_label)
        splitter.addWidget(preview_container)

        # --- Issues side ---
        issues_container = QtWidgets.QWidget(splitter)
        is_layout = QtWidgets.QVBoxLayout(issues_container)
        is_layout.setContentsMargins(0, 0, 0, 0)
        is_layout.setSpacing(4)

        self._empty_label = QtWidgets.QLabel(
            "Layout planned cleanly. Click Next to continue."
        )
        self._empty_label.setStyleSheet(
            "QLabel { color: #2e7d32; padding: 12px; "
            "background-color: #e8f5e9; border-radius: 4px; }"
        )
        self._empty_label.setWordWrap(True)
        self._empty_label.setVisible(False)
        is_layout.addWidget(self._empty_label)

        self._table = QtWidgets.QTableWidget(0, 5, issues_container)
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
        is_layout.addWidget(self._table, 1)

        self._footer_label = QtWidgets.QLabel("")
        self._footer_label.setStyleSheet("QLabel { color: #777; }")
        is_layout.addWidget(self._footer_label)

        splitter.addWidget(issues_container)
        # Default 60/40 split: preview gets more space than the table.
        splitter.setStretchFactor(0, 3)
        splitter.setStretchFactor(1, 2)
        splitter.setSizes([600, 400])

        return widget

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def on_activated(self) -> None:
        if self.main_window.session.calibration is None:
            self._stack.setCurrentWidget(self._no_cal_pane)
            return

        if self._last_issues is None and self.main_window.session.layout is None:
            self._stack.setCurrentWidget(self._landing_pane)
            return

        # We have a cached layout from a previous run -- show results.
        self._stack.setCurrentWidget(self._results_pane)

    # ------------------------------------------------------------------
    # Run / re-run
    # ------------------------------------------------------------------

    def _on_run_clicked(self) -> None:
        self._kick_off_plan()

    def _on_rerun_clicked(self) -> None:
        # Drop cached layout + ignore set, cascade invalidation to
        # downstream artifact-dependent steps (build, tile), then
        # re-fire. The cascade is necessary because session.layout is
        # an artifact downstream steps depend on -- a stale layout
        # would otherwise yield a stale composite.
        self._ignored.clear()
        self._last_issues = None
        self._preview_path = None
        self.main_window.session.layout = None
        # Use MainWindow's _invalidate_downstream so the sidebar mirror
        # is reset alongside the session state. Plain
        # session.invalidate_from would only update the persisted state
        # and leave the sidebar showing stale OK/WARNING badges.
        layout_idx = next(
            (i for i, e in enumerate(self.main_window._steps)
             if e.step_id == self.STEP_ID),
            None,
        )
        if layout_idx is not None:
            self.main_window._invalidate_downstream(layout_idx)
        self._kick_off_plan()

    def _kick_off_plan(self) -> None:
        self._run_button.setEnabled(False)
        self._rerun_button.setEnabled(False)
        cal = self.main_window.session.calibration
        if cal is None:
            self._stack.setCurrentWidget(self._no_cal_pane)
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)
            return

        # If W5 was visited and cached assignments, reuse them. Otherwise
        # the adapter will load the spreadsheet itself.
        cached = getattr(
            self.main_window.session, "_cached_assignments", None
        )

        # Preview path next to the session file so it's easy to find.
        preview_path = Path(self.main_window.output_dir) / "layout_review.png"

        runner = self.main_window.run_pipeline_step(
            self.STEP_ID,
            _run_plan_layout,
            args=(
                self.main_window.map_path,
                cal,
                self.main_window.assignments_path,
                cached,
                preview_path,
            ),
            on_finished=self._on_plan_finished,
            on_failed=self._on_plan_failed,
            on_canceled=self._on_plan_canceled,
        )
        if runner is None:
            # Another runner in flight; re-enable buttons so the user
            # can retry after it finishes.
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)

    def _on_plan_finished(self, result, issues: list) -> None:
        # Adapter returns ((layout, assignments, preview_path), issues);
        # the runner splits that into (result, issues) so ``result`` is
        # the 3-tuple.
        layout, assignments, preview_path = result

        # Stash artifacts on the session FIRST so any subsequent save
        # (e.g. from set_step_status below) persists them.
        self.main_window.session.layout = layout
        self.main_window.session._cached_assignments = assignments  # type: ignore[attr-defined]

        self._last_issues = list(issues)
        self._preview_path = preview_path
        self._mount_preview()
        self._populate_table()
        self._stack.setCurrentWidget(self._results_pane)
        self._refresh_status()
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_plan_failed(self, exc: BaseException) -> None:
        # MainWindow already sets ERROR + the one-line summary; just
        # re-enable our buttons.
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_plan_canceled(self) -> None:
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    # ------------------------------------------------------------------
    # Preview mounting
    # ------------------------------------------------------------------

    def _mount_preview(self) -> None:
        """Load ``self._preview_path`` into the QGraphicsView, or show
        the no-preview fallback."""
        if self._preview_path is None or not self._preview_path.exists():
            self._preview_view.clear_pixmap()
            self._preview_view.setVisible(False)
            self._no_preview_label.setVisible(True)
            return

        pixmap = QtGui.QPixmap(str(self._preview_path))
        if pixmap.isNull():
            self._preview_view.clear_pixmap()
            self._preview_view.setVisible(False)
            self._no_preview_label.setVisible(True)
            return

        self._no_preview_label.setVisible(False)
        self._preview_view.setVisible(True)
        self._preview_view.set_pixmap(pixmap)

    def _on_fit_clicked(self) -> None:
        self._preview_view.fit_to_window()

    # ------------------------------------------------------------------
    # Table population
    # ------------------------------------------------------------------

    def _populate_table(self) -> None:
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

    def _visible_issues(self) -> List[LayoutIssue]:
        if not self._last_issues:
            return []
        return [
            i for i in self._last_issues if _issue_key(i) not in self._ignored
        ]

    def _append_row(self, row_idx: int, issue: LayoutIssue) -> None:
        self._table.insertRow(row_idx)

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
            row_idx, 1, QtWidgets.QTableWidgetItem(issue.office_id or "-")
        )
        self._table.setItem(row_idx, 2, QtWidgets.QTableWidgetItem(issue.code))

        msg_item = QtWidgets.QTableWidgetItem(issue.message)
        msg_item.setToolTip(issue.message)
        self._table.setItem(row_idx, 3, msg_item)

        actions = QtWidgets.QWidget()
        h = QtWidgets.QHBoxLayout(actions)
        h.setContentsMargins(4, 2, 4, 2)
        h.setSpacing(4)

        if issue.office_id:
            show_btn = QtWidgets.QPushButton("Show on map")
            show_btn.setToolTip(
                "Pan and zoom the preview to this office. "
                "If no layout entry exists for it (e.g. office_has_no_room), "
                "this falls back to jumping to Step 1 with the label selected."
            )
            show_btn.clicked.connect(
                lambda _=False, oid=issue.office_id: self._on_show_on_map(oid)
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

    def _on_show_on_map(self, office_id: str) -> None:
        """Pan/zoom the preview to the office's planned position.

        If no entry was produced for the office (the planner skipped it
        because of an error), fall back to jumping to Step 1 and
        selecting the label there.
        """
        layout = self.main_window.session.layout
        entry = self._find_entry(layout, office_id) if layout else None
        if entry is not None:
            bbox = self._entry_bbox(entry)
            self._preview_view.center_on_bbox(bbox)
            return

        # Fallback: no entry to center on -- jump to Step 1.
        self.main_window.navigate_to_step(0)
        cal_widget = self.main_window._steps[0].widget
        canvas = getattr(cal_widget, "_canvas", None)
        if canvas is None:
            return
        indices = canvas.find_label_indices(office_id)
        if indices:
            canvas.center_on_label(indices[0])

    @staticmethod
    def _find_entry(layout: Layout, office_id: str) -> Optional[LayoutEntry]:
        # The layout stores office_id as written; ours from issues may
        # be upper-cased (person_not_placed uppercases). Try both.
        direct = layout.entry_by_office(office_id)
        if direct is not None:
            return direct
        for e in layout.entries:
            if e.office_id.upper() == office_id.upper():
                return e
        return None

    @staticmethod
    def _entry_bbox(entry: LayoutEntry) -> Tuple[int, int, int, int]:
        """Bounding box covering the office's inscribed rect + any leader
        line endpoints (so a leader-line fallback also centers on the
        text rendered outside the room)."""
        x, y, w, h = entry.inscribed_rect
        x2, y2 = x + w, y + h
        for lx1, ly1, lx2, ly2 in entry.leader_lines:
            x = min(x, lx1, lx2)
            y = min(y, ly1, ly2)
            x2 = max(x2, lx1, lx2)
            y2 = max(y2, ly1, ly2)
        for name in entry.names:
            nx, ny, nw, nh = name.bbox
            x = min(x, nx)
            y = min(y, ny)
            x2 = max(x2, nx + nw)
            y2 = max(y2, ny + nh)
        return (int(x), int(y), int(x2 - x), int(y2 - y))

    def _on_ignore_clicked(self, issue: LayoutIssue) -> None:
        self._ignored.add(_issue_key(issue))
        self._populate_table()
        self._refresh_status()

    def _on_back_to_calibrate_clicked(self) -> None:
        self.main_window.navigate_to_step(0)

    # ------------------------------------------------------------------
    # Status + summary refresh
    # ------------------------------------------------------------------

    def _refresh_status(self) -> None:
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
        layout = self.main_window.session.layout
        entry_count = len(layout.entries) if layout is not None else 0
        total = len(self._last_issues)
        ignored = len(self._ignored)
        visible = self._visible_issues()
        errs = sum(1 for i in visible if i.severity == "error")
        warns = sum(1 for i in visible if i.severity == "warning")

        head = f"{entry_count} office(s) planned"
        if total == 0:
            self._summary_label.setText(f"{head} — no issues")
        else:
            parts: list[str] = []
            if errs:
                parts.append(f"{errs} error(s)")
            if warns:
                parts.append(f"{warns} warning(s)")
            if not parts:
                self._summary_label.setText(
                    f"{head} — all {total} issue(s) ignored"
                )
            else:
                self._summary_label.setText(f"{head} — {', '.join(parts)}")
        if ignored:
            self._footer_label.setText(f"{ignored} issue(s) ignored")
        else:
            self._footer_label.setText("")
