"""Step 5 — Build composite.

This step takes the calibration from Step 1 and the layout from Step 4,
runs ``render_composite()`` to produce the final coloured + labelled
``composite.png`` (plus a ``composite_review.png`` companion), and shows
the result in a pan/zoom viewer with an issues table on the side.

Issues come from ``officemapmaker.render`` (``RenderIssue`` dataclass):

* ``error`` (blocks Next):
  ``layout_office_not_in_calibration`` (the layout references an office
  the calibration doesn't know about), ``palette_team_missing`` (a
  team that ended up in the layout isn't in the palette), and
  ``unexpected_pixel_change`` (render touched pixels outside its
  expected-change mask -- a safety-net diff check; usually indicates
  a layout bug, not a user problem).
* ``warning`` (Next allowed):
  ``fill_leak_clipped`` (a flood-fill leaked outside its room polygon
  and was clipped at render time; the composite is safe but the
  underlying map has a wall gap), ``seed_unreachable_at_render``
  (fill seed sat on a wall pixel -- room won't be colored),
  ``palette_low_contrast`` (a team override color doesn't meet WCAG
  AAA contrast against black text), and ``fill_polygon_mismatch_at_render``.

Like ``LayoutStep``, the UI is a ``QStackedWidget`` with three panes:

* **No-layout** -- Step 4 hasn't finished. Button sends them back.
* **Landing** -- layout exists but no composite yet. Big "Build composite"
  button.
* **Results** -- splitter with the rendered composite on the left and
  the issues table on the right. Per-row "Show on map" pans/zooms the
  preview to that office (using the office's calibration room bbox).
  "Ignore" hides advisory warnings the user has decided are fine.

The composite is rendered on a worker thread. ``render_composite``
exposes per-office progress and cancel hooks (added when the per-office
crop optimization landed -- a 4000x4000 map x 200+ offices used to take
~5 minutes as one atomic call). The result is auto-saved to
``output_dir/composite.png`` (plus the review copy) and an "Open in
Explorer" button reveals it in the file manager.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import List, Optional, Tuple

from PySide6 import QtCore, QtGui, QtWidgets

from ...calibration import Calibration
from ...io_assignments import Assignment, load_assignments
from ...layout import Layout
from ...render import RenderIssue, RenderResult, render_composite
from ..main_window import StepStatus
from ._preview_view import PreviewGraphicsView
from .base import StepBase


# ---------------------------------------------------------------------------
# Pipeline adapter
# ---------------------------------------------------------------------------


def _run_render_composite(
    map_path: Path,
    calibration: Calibration,
    layout: Layout,
    assignments_path: Path,
    cached_assignments: Optional[List[Assignment]],
    output_png: Path,
    *,
    progress_cb,
    cancel_cb,
) -> Tuple[Tuple[RenderResult, List[Assignment]], List[RenderIssue]]:
    """Worker-thread entry point for Step 5.

    Returns ``((render_result, assignments), issues)``. ``render_result``
    carries both the on-disk path (for the preview viewer + "Open in
    Explorer") and the palette (so a future tile step can reuse it
    without re-rendering).

    The cached assignments slot is re-passed back so the on-finished
    handler can stash it for the tile step downstream.
    """
    progress_cb(0.0, "Loading assignments...")
    if cached_assignments is not None:
        assignments = cached_assignments
    else:
        assignments = load_assignments(assignments_path)
    if cancel_cb():
        from ...pipeline import PipelineCanceled

        raise PipelineCanceled()

    # render_composite now exposes progress + cancel hooks of its own.
    # Reserve the first 5% of the wizard progress bar for assignment
    # loading; map render_composite's internal 0.0 -> 1.0 onto the
    # remaining 0.05 -> 1.0 band.
    def _inner_progress(fraction: float, message: str) -> None:
        progress_cb(0.05 + 0.95 * fraction, message)

    result = render_composite(
        map_path, calibration, layout, assignments, output_png,
        progress_cb=_inner_progress,
        cancel_cb=cancel_cb,
    )
    if cancel_cb():
        from ...pipeline import PipelineCanceled

        raise PipelineCanceled()

    progress_cb(1.0, "Done")
    return (result, assignments), list(result.issues)


# ---------------------------------------------------------------------------
# Issue classification + ignore-key helpers
# ---------------------------------------------------------------------------


def _classify_issues(
    issues: List[RenderIssue],
) -> Tuple[StepStatus, List[str], List[str], List[str]]:
    """Map a list of ``RenderIssue`` to (step status, messages, per-issue
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


def _issue_key(issue: RenderIssue) -> str:
    """Stable key for the in-memory ignored set."""
    return f"{issue.code}|{issue.office_id or ''}|{issue.message}"


# ---------------------------------------------------------------------------
# OS-native "show file in explorer"
# ---------------------------------------------------------------------------


def _reveal_in_explorer(path: Path) -> None:
    """Open the OS file manager with ``path`` selected.

    Windows: ``explorer /select,<path>``. macOS/Linux fall back to
    opening the parent folder (no portable "select this file" API).
    """
    if not path.exists():
        return
    try:
        if sys.platform == "win32":
            subprocess.Popen(["explorer", f"/select,{path}"])
        elif sys.platform == "darwin":
            subprocess.Popen(["open", "-R", str(path)])
        else:
            subprocess.Popen(["xdg-open", str(path.parent)])
    except OSError:
        # Best-effort -- don't crash the wizard if the launcher fails.
        pass


# ---------------------------------------------------------------------------
# Widget
# ---------------------------------------------------------------------------


class BuildStep(StepBase):
    """Wizard pane for the Build-composite step."""

    STEP_ID = "build"

    def __init__(self, main_window) -> None:
        super().__init__(main_window)

        # In-memory ignore set keyed by ``_issue_key``. Resets on Re-build.
        self._ignored: set[str] = set()
        # Cached results from the last run (or None before first run).
        self._last_issues: Optional[List[RenderIssue]] = None
        self._composite_path: Optional[Path] = None
        # Cached on the widget (not the session): used by "Show on map"
        # to look up an office's room polygon.
        self._calibration: Optional[Calibration] = None

        outer = QtWidgets.QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)

        self._stack = QtWidgets.QStackedWidget(self)
        outer.addWidget(self._stack)

        self._no_layout_pane = self._build_no_layout_pane()
        self._landing_pane = self._build_landing_pane()
        self._results_pane = self._build_results_pane()

        self._stack.addWidget(self._no_layout_pane)
        self._stack.addWidget(self._landing_pane)
        self._stack.addWidget(self._results_pane)

    # ------------------------------------------------------------------
    # Pane builders
    # ------------------------------------------------------------------

    def _build_no_layout_pane(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(40, 40, 40, 40)
        layout.setSpacing(16)

        title = QtWidgets.QLabel("Step 4 (Plan layout) needs to finish first")
        f = title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        title.setFont(f)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(
            "This step renders the final coloured + labelled composite. "
            "We don't have a planned layout yet -- go back to Step 4, "
            "run the planner, then come back here."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        btn = QtWidgets.QPushButton("Back to Step 4: Plan layout")
        btn.setMaximumWidth(260)
        btn.setMinimumHeight(36)
        btn.clicked.connect(self._on_back_to_layout_clicked)
        layout.addWidget(btn)

        layout.addStretch(1)
        return widget

    def _build_landing_pane(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(40, 40, 40, 40)
        layout.setSpacing(16)

        title = QtWidgets.QLabel("Build composite")
        f = title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        title.setFont(f)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(
            "This renders the final composite by flood-filling each office "
            "with its team color, then drawing every planned name on top. "
            "The result is saved to <i>composite.png</i> in the output "
            "folder. This is the slow step -- expect 10-60 seconds on a "
            "large floor plan."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        self._run_button = QtWidgets.QPushButton("Build composite")
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

        # Header row: summary on the left, action buttons on the right.
        header_row = QtWidgets.QHBoxLayout()
        self._summary_label = QtWidgets.QLabel("")
        f = self._summary_label.font()
        f.setBold(True)
        self._summary_label.setFont(f)
        header_row.addWidget(self._summary_label)
        header_row.addStretch(1)

        self._fit_button = QtWidgets.QPushButton("Fit preview")
        self._fit_button.setToolTip("Reset zoom so the whole composite fits in the view")
        self._fit_button.clicked.connect(self._on_fit_clicked)
        header_row.addWidget(self._fit_button)

        self._open_button = QtWidgets.QPushButton("Open in Explorer")
        self._open_button.setToolTip("Reveal composite.png in the file manager")
        self._open_button.clicked.connect(self._on_open_in_explorer_clicked)
        self._open_button.setEnabled(False)
        header_row.addWidget(self._open_button)

        self._rerun_button = QtWidgets.QPushButton("Re-build composite")
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
            "No preview available -- the composite was rendered but the "
            "PNG could not be loaded. Try Re-build composite."
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
            "Composite built cleanly. Click Next to continue."
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
        if (
            self.main_window.session.calibration is None
            or self.main_window.session.layout is None
        ):
            self._stack.setCurrentWidget(self._no_layout_pane)
            return

        # Calibration is needed by "Show on map" to look up the office's
        # room polygon -- stash it on the widget every time we activate
        # so it stays in sync with edits.
        self._calibration = self.main_window.session.calibration

        if self._last_issues is None and self._composite_path is None:
            self._stack.setCurrentWidget(self._landing_pane)
            return

        # We have cached results from a previous run -- show results.
        self._stack.setCurrentWidget(self._results_pane)

    # ------------------------------------------------------------------
    # Run / re-run
    # ------------------------------------------------------------------

    def _on_run_clicked(self) -> None:
        self._kick_off_build()

    def _on_rerun_clicked(self) -> None:
        # Drop cached results + ignore set, cascade invalidation to
        # the downstream tile step, then re-fire.
        self._ignored.clear()
        self._last_issues = None
        self._composite_path = None
        build_idx = next(
            (i for i, e in enumerate(self.main_window._steps)
             if e.step_id == self.STEP_ID),
            None,
        )
        if build_idx is not None:
            self.main_window._invalidate_downstream(build_idx)
        self._kick_off_build()

    def _kick_off_build(self) -> None:
        self._run_button.setEnabled(False)
        self._rerun_button.setEnabled(False)
        sess = self.main_window.session
        cal = sess.calibration
        lay = sess.layout
        if cal is None or lay is None:
            self._stack.setCurrentWidget(self._no_layout_pane)
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)
            return

        # Reuse the cached assignments from W5/W7 if available;
        # otherwise the adapter will load the spreadsheet.
        cached = getattr(sess, "_cached_assignments", None)

        composite_path = Path(self.main_window.output_dir) / "composite.png"

        runner = self.main_window.run_pipeline_step(
            self.STEP_ID,
            _run_render_composite,
            args=(
                self.main_window.map_path,
                cal,
                lay,
                self.main_window.assignments_path,
                cached,
                composite_path,
            ),
            on_finished=self._on_build_finished,
            on_failed=self._on_build_failed,
            on_canceled=self._on_build_canceled,
        )
        if runner is None:
            # Another runner in flight; re-enable buttons so the user
            # can retry after it finishes.
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)

    def _on_build_finished(self, result, issues: list) -> None:
        # Adapter returns ((render_result, assignments), issues).
        render_result, assignments = result

        # Cache assignments back to the session so the tile step can
        # reuse them without reloading the spreadsheet.
        self.main_window.session._cached_assignments = assignments  # type: ignore[attr-defined]

        self._last_issues = list(issues)
        self._composite_path = render_result.composite_path
        self._calibration = self.main_window.session.calibration
        self._mount_preview()
        self._populate_table()
        self._open_button.setEnabled(self._composite_path is not None)
        self._stack.setCurrentWidget(self._results_pane)
        self._refresh_status()
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_build_failed(self, exc: BaseException) -> None:
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_build_canceled(self) -> None:
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    # ------------------------------------------------------------------
    # Preview mounting
    # ------------------------------------------------------------------

    def _mount_preview(self) -> None:
        """Load ``self._composite_path`` into the QGraphicsView, or show
        the no-preview fallback."""
        if self._composite_path is None or not self._composite_path.exists():
            self._preview_view.clear_pixmap()
            self._preview_view.setVisible(False)
            self._no_preview_label.setVisible(True)
            return

        pixmap = QtGui.QPixmap(str(self._composite_path))
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

    def _on_open_in_explorer_clicked(self) -> None:
        if self._composite_path is None:
            return
        _reveal_in_explorer(self._composite_path)

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

    def _visible_issues(self) -> List[RenderIssue]:
        if not self._last_issues:
            return []
        return [
            i for i in self._last_issues if _issue_key(i) not in self._ignored
        ]

    def _append_row(self, row_idx: int, issue: RenderIssue) -> None:
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
                "If the calibration has no room polygon for it (e.g. "
                "layout_office_not_in_calibration), this falls back "
                "to jumping to Step 1."
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
        """Pan/zoom the composite preview to the office's room polygon.

        If the calibration has no label or no room polygon for this
        office, fall back to jumping to Step 1.
        """
        cal = self._calibration or self.main_window.session.calibration
        if cal is not None:
            bbox = self._room_bbox_for_office(cal, office_id)
            if bbox is not None:
                self._preview_view.center_on_bbox(bbox)
                return

        # Fallback: navigate back to Step 1 and select the label there.
        self.main_window.navigate_to_step(0)
        cal_widget = self.main_window._steps[0].widget
        canvas = getattr(cal_widget, "_canvas", None)
        if canvas is None:
            return
        indices = canvas.find_label_indices(office_id)
        if indices:
            canvas.center_on_label(indices[0])

    @staticmethod
    def _room_bbox_for_office(
        calibration: Calibration, office_id: str,
    ) -> Optional[Tuple[int, int, int, int]]:
        """Resolve ``office_id`` -> Label -> Room.bbox.

        Returns None if the label is unknown, the label has no room_id,
        or the room is missing from the calibration. office_id matching
        is case-insensitive to mirror ``_label_by_office`` in render.py.
        """
        upper = office_id.upper()
        label = next(
            (lb for lb in calibration.labels if lb.id.upper() == upper), None,
        )
        if label is None or label.room_id is None:
            return None
        room = next(
            (r for r in calibration.rooms if r.id == label.room_id), None,
        )
        if room is None:
            return None
        return room.bbox

    def _on_ignore_clicked(self, issue: RenderIssue) -> None:
        self._ignored.add(_issue_key(issue))
        self._populate_table()
        self._refresh_status()

    def _on_back_to_layout_clicked(self) -> None:
        # Step 4 (layout) is at index 3 in the canonical step list.
        self.main_window.navigate_to_step(3)

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
        total = len(self._last_issues)
        ignored = len(self._ignored)
        visible = self._visible_issues()
        errs = sum(1 for i in visible if i.severity == "error")
        warns = sum(1 for i in visible if i.severity == "warning")

        if self._composite_path is not None:
            head = f"Composite written to {self._composite_path.name}"
        else:
            head = "Composite built"
        if total == 0:
            self._summary_label.setText(f"{head} -- no issues")
        else:
            parts: list[str] = []
            if errs:
                parts.append(f"{errs} error(s)")
            if warns:
                parts.append(f"{warns} warning(s)")
            if not parts:
                self._summary_label.setText(
                    f"{head} -- all {total} issue(s) ignored"
                )
            else:
                self._summary_label.setText(f"{head} -- {', '.join(parts)}")
        if ignored:
            self._footer_label.setText(f"{ignored} issue(s) ignored")
        else:
            self._footer_label.setText("")
