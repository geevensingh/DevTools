"""Top-level wizard window: sidebar + stacked content + issues panel + footer.

This is the W1+W2+W3 shell. It owns step navigation, status badges, the
persisted ``Session`` (W2), and the background ``PipelineRunner``
plumbing (W3). Real per-step content widgets are added in W4..W9;
until then each step shows a placeholder. See plan.md section 14 for
the design rationale.

Navigation rules (per plan.md section 14.3):
- Sidebar lists the six steps with status badges (pending/running/ok/
  warning/error/advisory). Clicking a sidebar row jumps to that step.
- Jumping BACK to an earlier step prompts to invalidate downstream
  steps (they revert to pending and their cached results clear).
- The Back / Next footer is keyboard-equivalent. Next is disabled
  whenever the current step's status is ``running`` or ``error``.
  Advisory steps still require an explicit Next click (per resolved
  decision Q3 in section 14.10).

W2 additions:
- The window owns a ``Session`` and persists every status/issues
  change to ``<map_basename>.session.json`` in the output dir.
- On launch, the launcher prompts before construction if input files
  changed since the last save; ``MainWindow`` itself only sees the
  resolved session.

W3 additions:
- ``run_pipeline_step(step_id, func, ...)`` runs ``func`` on a worker
  thread via ``PipelineRunner``, with progress bar + Cancel button
  in the footer and automatic status-badge bookkeeping.
- Step widgets (W4+) call ``run_pipeline_step`` instead of invoking
  pipeline functions directly so the UI never blocks on OCR /
  flood-fill / layout / render.
"""

from __future__ import annotations

import enum
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Dict, List, Optional, Tuple

from PySide6 import QtCore, QtGui, QtWidgets

from ..pipeline import PipelineRunner
from ..session import (
    Issue as SessionIssue,
    STEP_IDS,
    Session,
    StepState,
    StepStatus as SessionStepStatus,
)


# Window starts at a reasonable working size that fits on a 1366x768
# laptop while leaving room for the sidebar.
_DEFAULT_WINDOW_SIZE = QtCore.QSize(1366, 800)
_SIDEBAR_WIDTH = 260
_ISSUES_PANEL_INITIAL_HEIGHT = 140


# QSettings keys for persisted window geometry. The org / app name pair
# determines where Qt writes them (Windows registry under
# HKCU\Software\DevTools\OfficeMapMaker; ~/.config/DevTools/OfficeMapMaker.conf
# on Linux; ~/Library/Preferences/com.devtools.OfficeMapMaker.plist on Mac).
# These are intentionally machine-scoped (not stored in the session JSON)
# so the user's preferred window layout follows them across sessions /
# different floor plans rather than getting captured per-map.
_SETTINGS_ORG = "DevTools"
_SETTINGS_APP = "OfficeMapMaker"
_SETTINGS_GEOMETRY_KEY = "MainWindow/geometry"
_SETTINGS_STATE_KEY = "MainWindow/windowState"


def _make_settings() -> QtCore.QSettings:
    """Return the QSettings instance used for window-geometry persistence.

    Factored out so tests can monkeypatch a tmp-path INI settings
    object in here without poisoning the user's real registry / preferences.
    """
    return QtCore.QSettings(_SETTINGS_ORG, _SETTINGS_APP)


def _geometry_is_on_screen(rect: QtCore.QRect) -> bool:
    """True if ``rect`` overlaps at least one connected screen by a usable amount.

    Used after ``restoreGeometry`` to detect the "monitor unplugged"
    case: a window saved on a now-disconnected external display would
    otherwise come back at coordinates that put it entirely outside
    every available screen, leaving the user unable to drag it back.
    We require at least 200x100 px of overlap with *some* available
    screen geometry (the per-screen rect excludes the OS taskbar) so
    a sliver-on-screen window still counts as visible.
    """
    app = QtWidgets.QApplication.instance()
    if app is None:
        return True  # No QApplication yet -- nothing to clamp against.
    min_overlap = QtCore.QSize(200, 100)
    for screen in app.screens():
        avail = screen.availableGeometry()
        inter = avail.intersected(rect)
        if (
            inter.width() >= min_overlap.width()
            and inter.height() >= min_overlap.height()
        ):
            return True
    return False


class StepStatus(enum.Enum):
    """Lifecycle state shown in the sidebar badge for each step."""

    PENDING = "pending"          # not yet attempted
    RUNNING = "running"          # background pipeline call in progress
    OK = "ok"                    # finished cleanly, no issues
    WARNING = "warning"          # finished with warnings only
    ADVISORY = "advisory"        # finished with informational notes only
    ERROR = "error"              # finished with blocking errors


# UI enum <-> session enum. The session uses ``INFO`` where the wizard
# uses ``ADVISORY``; otherwise they're identical strings. Kept as a
# bidirectional table so the two layers can evolve independently.
_TO_SESSION_STATUS: Dict[StepStatus, SessionStepStatus] = {
    StepStatus.PENDING: SessionStepStatus.PENDING,
    StepStatus.RUNNING: SessionStepStatus.RUNNING,
    StepStatus.OK: SessionStepStatus.OK,
    StepStatus.WARNING: SessionStepStatus.WARNING,
    StepStatus.ADVISORY: SessionStepStatus.INFO,
    StepStatus.ERROR: SessionStepStatus.ERROR,
}
_FROM_SESSION_STATUS: Dict[SessionStepStatus, StepStatus] = {
    v: k for k, v in _TO_SESSION_STATUS.items()
}


# Order matters: this list IS the wizard's step order. Each tuple is
# (step_id, sidebar_label). Step IDs are stable strings used by the
# session state and by jump-to-step actions from issues-panel buttons.
_STEPS: List[tuple[str, str]] = [
    ("calibrate", "1. Calibrate map"),
    ("validate_labels", "2. Validate labels"),
    ("validate_fill", "3. Validate fill"),
    ("layout", "4. Plan name layout"),
    ("build", "5. Build composite"),
    ("tile", "6. Tile + PDF"),
]


@dataclass
class _StepEntry:
    """Per-step state held by the MainWindow."""

    step_id: str
    label: str
    status: StepStatus = StepStatus.PENDING
    widget: Optional[QtWidgets.QWidget] = None
    # When the step finishes (W4+) it will populate this list and the
    # issues panel will render the entries. Kept here in W1 so the
    # navigation/wiring code can be unit-tested before pipeline hookup.
    issues: List[str] = field(default_factory=list)
    # Parallel to ``issues``: an optional ``(x, y)`` pixel coordinate
    # on the map for each issue. When present, clicking that row in
    # the issues panel pans/zooms the step's canvas to that point
    # (via the step widget's ``navigate_to_issue_target`` hook).
    # ``None`` entries mean "this issue has no single location" (e.g.
    # "no rooms detected") and clicking them is a no-op.
    issue_targets: List[Optional[Tuple[int, int]]] = field(default_factory=list)


# Status badges. Using short ASCII text keeps them legible at small
# font sizes and avoids font-dependent unicode glyph fallbacks.
_BADGE_TEXT: Dict[StepStatus, str] = {
    StepStatus.PENDING: "",
    StepStatus.RUNNING: "...",
    StepStatus.OK: "OK",
    StepStatus.WARNING: "WARN",
    StepStatus.ADVISORY: "INFO",
    StepStatus.ERROR: "ERR",
}

_BADGE_COLOR: Dict[StepStatus, str] = {
    StepStatus.PENDING: "#888888",
    StepStatus.RUNNING: "#1976d2",
    StepStatus.OK: "#2e7d32",
    StepStatus.WARNING: "#ed6c02",
    StepStatus.ADVISORY: "#0277bd",
    StepStatus.ERROR: "#c62828",
}


class MainWindow(QtWidgets.QMainWindow):
    """The single top-level wizard window.

    Constructed once per ``OfficeMapMaker <map> <people>`` invocation.
    The window holds the per-step state and routes Back/Next/sidebar
    clicks into step changes.

    W1 scope: shell only. The map_path/assignments_path are stored on
    the instance for W2 to load into the Session, and shown in the
    title bar so the user sees what they're working on. No pipeline
    calls are made yet — every step shows a placeholder.
    """

    # Emitted whenever the active step changes. (current_index, step_id).
    # External code (tests) can connect to this for assertions.
    step_changed = QtCore.Signal(int, str)

    def __init__(
        self,
        *,
        map_path: Optional[Path] = None,
        assignments_path: Optional[Path] = None,
        output_dir: Optional[Path] = None,
        teams_path: Optional[Path] = None,
        session: Optional[Session] = None,
    ) -> None:
        super().__init__()

        # Two construction modes:
        #   (a) pass a fully-resolved ``session`` — the launcher does
        #       this after running ``Session.load_or_create`` and any
        #       input-mismatch prompt.
        #   (b) pass raw paths — used by tests + first-time launches in
        #       embedded contexts. We construct a fresh session from
        #       them, silently treating any prior session file as
        #       "restored" (no prompt) to keep the convenience
        #       constructor side-effect-free.
        if session is None:
            if map_path is None or assignments_path is None or output_dir is None:
                raise TypeError(
                    "MainWindow requires either session= or (map_path, "
                    "assignments_path, output_dir)."
                )
            session = Session.load_or_create(
                map_path=map_path,
                assignments_path=assignments_path,
                output_dir=output_dir,
                teams_path=teams_path,
            ).session

        self._session = session
        self._map_path = session.map_path
        self._assignments_path = session.assignments_path
        self._output_dir = session.output_dir
        self._teams_path = session.teams_path

        self.setWindowTitle(self._compose_title())
        self._restore_window_geometry()

        # Seed UI-level _StepEntry rows from the persisted step_state
        # so a restored session shows last run's status + issues.
        self._steps: List[_StepEntry] = []
        for sid, label in _STEPS:
            st = session.step_state.get(sid, StepState())
            issues_text = [iss.message for iss in st.issues]
            self._steps.append(
                _StepEntry(
                    step_id=sid,
                    label=label,
                    status=_FROM_SESSION_STATUS.get(st.status, StepStatus.PENDING),
                    issues=issues_text,
                )
            )
        # Restore to the step the user was last on (or step 0 for fresh
        # sessions / unknown ids).
        try:
            self._current_index = STEP_IDS.index(session.current_step)
        except ValueError:
            self._current_index = 0

        # Pipeline-runner bookkeeping (W3). Only one pipeline call may
        # be in flight at a time; the wizard's UX is linear.
        self._active_runner: Optional[PipelineRunner] = None
        self._active_runner_step_id: Optional[str] = None
        self._active_runner_prior_status: Optional[StepStatus] = None
        self._active_runner_prior_issues: Optional[List[str]] = None

        # Build UI: central widget is a horizontal splitter holding the
        # sidebar and a right-hand container; the right container stacks
        # content + issues panel vertically, with the Back/Next footer
        # pinned at the bottom.
        self._build_ui()
        self._populate_sidebar()
        self._populate_steps()
        self._refresh_navigation()
        # Initial selection so the right pane shows something useful.
        # The setCurrentRow call short-circuits in _on_sidebar_row_changed
        # when the row is already self._current_index, so we also
        # explicitly fire on_activated() on the initial step here.
        self._sidebar.setCurrentRow(self._current_index)
        initial_widget = self._steps[self._current_index].widget
        if isinstance(initial_widget, self._step_base_cls):
            initial_widget.on_activated()
        # Save once on launch so a freshly-created session lands on
        # disk even if the user closes the window without doing
        # anything (so they can pick up where they left off).
        self._save_session()

    # ------------------------------------------------------------------
    # UI construction
    # ------------------------------------------------------------------

    def _build_ui(self) -> None:
        central = QtWidgets.QWidget(self)
        self.setCentralWidget(central)
        root = QtWidgets.QHBoxLayout(central)
        root.setContentsMargins(0, 0, 0, 0)
        root.setSpacing(0)

        # Sidebar: a QListWidget keeps things simple. Each row is one
        # step. We render the badge using a stylesheet on the item's
        # status; the badge text is appended to the label so the same
        # widget conveys both label and status without per-row custom
        # delegates (overkill for six rows).
        self._sidebar = QtWidgets.QListWidget(central)
        self._sidebar.setFixedWidth(_SIDEBAR_WIDTH)
        self._sidebar.setSelectionMode(
            QtWidgets.QAbstractItemView.SelectionMode.SingleSelection
        )
        # Larger font / row height so steps don't feel cramped.
        side_font = self._sidebar.font()
        side_font.setPointSize(side_font.pointSize() + 1)
        self._sidebar.setFont(side_font)
        self._sidebar.setStyleSheet(
            "QListWidget { background: #f4f4f4; border: none; }"
            " QListWidget::item { padding: 12px 14px; }"
            " QListWidget::item:selected { background: #1976d2; color: white; }"
        )
        self._sidebar.currentRowChanged.connect(self._on_sidebar_row_changed)
        root.addWidget(self._sidebar)

        # Right pane: content (top) + issues panel (collapsible) + footer.
        right = QtWidgets.QWidget(central)
        right_layout = QtWidgets.QVBoxLayout(right)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(0)

        self._content_stack = QtWidgets.QStackedWidget(right)
        right_layout.addWidget(self._content_stack, stretch=1)

        # Issues panel: a collapsible QGroupBox with a scrollable list.
        # In W1 it's always empty; W4+ steps populate it as their
        # pipeline calls complete.
        self._issues_group = QtWidgets.QGroupBox("Issues", right)
        self._issues_group.setCheckable(True)
        self._issues_group.setChecked(False)
        self._issues_group.setMaximumHeight(_ISSUES_PANEL_INITIAL_HEIGHT)
        issues_layout = QtWidgets.QVBoxLayout(self._issues_group)
        issues_layout.setContentsMargins(8, 8, 8, 8)
        self._issues_list = QtWidgets.QListWidget(self._issues_group)
        self._issues_list.setStyleSheet(
            "QListWidget { background: white; border: 1px solid #ddd; }"
        )
        # Single-click on an issue with a target pans/zooms the current
        # step's canvas to that point. itemActivated covers Enter +
        # double-click for keyboard / accessibility users.
        self._issues_list.itemClicked.connect(self._on_issue_item_activated)
        self._issues_list.itemActivated.connect(self._on_issue_item_activated)
        issues_layout.addWidget(self._issues_list)
        # Toggling the groupbox shows/hides its content; this gives a
        # cheap collapse-to-titlebar without writing a custom widget.
        self._issues_group.toggled.connect(self._issues_list.setVisible)
        self._issues_list.setVisible(False)
        right_layout.addWidget(self._issues_group)

        # Footer: status label + (hidden by default) progress bar on
        # the left, Cancel + Back / Next on the right.
        footer = QtWidgets.QFrame(right)
        footer.setFrameShape(QtWidgets.QFrame.Shape.StyledPanel)
        footer.setStyleSheet("QFrame { background: #fafafa; }")
        footer_layout = QtWidgets.QHBoxLayout(footer)
        footer_layout.setContentsMargins(12, 8, 12, 8)
        self._footer_status = QtWidgets.QLabel("", footer)
        self._footer_status.setStyleSheet("QLabel { color: #555; }")
        footer_layout.addWidget(self._footer_status, stretch=1)
        # Progress bar is hidden when no pipeline step is running.
        self._progress_bar = QtWidgets.QProgressBar(footer)
        self._progress_bar.setRange(0, 1000)
        self._progress_bar.setFixedWidth(220)
        self._progress_bar.setTextVisible(False)
        self._progress_bar.setVisible(False)
        footer_layout.addWidget(self._progress_bar)
        # Cancel is shown only while a pipeline step is running.
        self._cancel_button = QtWidgets.QPushButton("Cancel", footer)
        self._cancel_button.setVisible(False)
        self._cancel_button.clicked.connect(self._on_cancel_pipeline)
        footer_layout.addWidget(self._cancel_button)
        self._back_button = QtWidgets.QPushButton("< Back", footer)
        self._back_button.clicked.connect(self._on_back)
        footer_layout.addWidget(self._back_button)
        self._next_button = QtWidgets.QPushButton("Next >", footer)
        self._next_button.setDefault(True)
        self._next_button.clicked.connect(self._on_next)
        footer_layout.addWidget(self._next_button)
        right_layout.addWidget(footer)

        root.addWidget(right, stretch=1)

        # Status bar shows the input paths so the user always knows what
        # session they're working in.
        self.statusBar().showMessage(
            f"Map: {self._map_path.name}    Assignments: {self._assignments_path.name}    Output: {self._output_dir}"
        )

    def _populate_sidebar(self) -> None:
        for entry in self._steps:
            item = QtWidgets.QListWidgetItem(self._format_sidebar_label(entry))
            self._sidebar.addItem(item)

    def _populate_steps(self) -> None:
        # Deferred import so calibrate_step.py (which imports
        # ``StepStatus`` from this module at the top of its file) can
        # finish loading without a circular import.
        from .steps.base import StepBase
        from .steps.calibrate_step import CalibrateStep
        from .steps.validate_labels_step import ValidateLabelsStep
        from .steps.validate_fill_step import ValidateFillStep

        self._step_base_cls = StepBase  # cached for _activate_step lifecycle dispatch
        for entry in self._steps:
            if entry.step_id == "calibrate":
                entry.widget = CalibrateStep(self)
            elif entry.step_id == "validate_labels":
                entry.widget = ValidateLabelsStep(self)
            elif entry.step_id == "validate_fill":
                entry.widget = ValidateFillStep(self)
            else:
                entry.widget = self._make_placeholder_step(entry)
            self._content_stack.addWidget(entry.widget)

    # ------------------------------------------------------------------
    # Placeholder step widget
    # ------------------------------------------------------------------

    def _make_placeholder_step(self, entry: _StepEntry) -> QtWidgets.QWidget:
        """Build the W1 placeholder content for a step.

        Each placeholder shows the step's label, a one-line description
        of what the eventual implementation will do, and a button to
        simulate completion (so navigation can be exercised before the
        real per-step widgets exist).
        """
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(40, 40, 40, 40)
        layout.setSpacing(16)

        title = QtWidgets.QLabel(entry.label, widget)
        title_font = title.font()
        title_font.setPointSize(title_font.pointSize() + 4)
        title_font.setBold(True)
        title.setFont(title_font)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(_PLACEHOLDER_DESCRIPTIONS[entry.step_id], widget)
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        layout.addStretch(1)

        # Dev-only: simulate-completion buttons. These let a tester
        # exercise navigation + status badges before the real steps
        # land in W4..W9. Removed in W10 polish.
        sim_row = QtWidgets.QHBoxLayout()
        for status in (
            StepStatus.OK,
            StepStatus.WARNING,
            StepStatus.ADVISORY,
            StepStatus.ERROR,
        ):
            btn = QtWidgets.QPushButton(f"(dev) set {status.value}", widget)
            btn.clicked.connect(
                lambda _checked=False, sid=entry.step_id, s=status: self.set_step_status(
                    sid, s, issues=[f"placeholder {s.value} issue"]
                    if s in (StepStatus.WARNING, StepStatus.ADVISORY, StepStatus.ERROR)
                    else [],
                )
            )
            sim_row.addWidget(btn)
        sim_row.addStretch(1)
        layout.addLayout(sim_row)

        return widget

    # ------------------------------------------------------------------
    # State mutation
    # ------------------------------------------------------------------

    def set_step_status(
        self,
        step_id: str,
        status: StepStatus,
        *,
        issues: Optional[List[str]] = None,
        issue_targets: Optional[List[Optional[Tuple[int, int]]]] = None,
    ) -> None:
        """Update a step's status badge and (optionally) issues list.

        Public so W3+ pipeline runners can call it as their finished
        signal fires. Also called from the W1 dev simulate-completion
        buttons.

        ``issue_targets`` is an optional list parallel to ``issues``:
        each entry is either an ``(x, y)`` pixel coordinate on the map
        (clicking that row will pan/zoom the step's canvas there) or
        ``None`` (clicking that row is a no-op). When omitted, every
        issue is treated as having no target.
        """
        entry = self._step_by_id(step_id)
        if entry is None:
            return
        entry.status = status
        entry.issues = list(issues or [])
        if issue_targets is None:
            entry.issue_targets = [None] * len(entry.issues)
        else:
            # Defensively pad / truncate so the parallel-list invariant
            # holds even if a caller passes the wrong length.
            targets = list(issue_targets)
            if len(targets) < len(entry.issues):
                targets += [None] * (len(entry.issues) - len(targets))
            entry.issue_targets = targets[: len(entry.issues)]
        idx = self._steps.index(entry)
        self._sidebar.item(idx).setText(self._format_sidebar_label(entry))
        if idx == self._current_index:
            self._refresh_issues_panel()
        self._refresh_navigation()
        # Mirror into the persisted session. We translate the string
        # issues into Issue objects with the step's severity baked in;
        # W4+ pipeline steps will pass real Issue objects via a new
        # set_step_issues(...) API.
        sess_status = _TO_SESSION_STATUS[status]
        severity = (
            "warning" if status == StepStatus.WARNING
            else "info" if status == StepStatus.ADVISORY
            else "error" if status == StepStatus.ERROR
            else "info"
        )
        sess_issues = [
            SessionIssue(code="placeholder", severity=severity, message=msg)
            for msg in entry.issues
        ]
        self._session.set_step(step_id, status=sess_status, issues=sess_issues)
        self._save_session()

    def _invalidate_downstream(self, from_index: int) -> None:
        """Reset steps after ``from_index`` to PENDING with no issues.

        Called when the user jumps back and confirms invalidation.
        """
        for i in range(from_index + 1, len(self._steps)):
            entry = self._steps[i]
            entry.status = StepStatus.PENDING
            entry.issues = []
            entry.issue_targets = []
            self._sidebar.item(i).setText(self._format_sidebar_label(entry))
        # Mirror to the session: invalidate_from clears steps from
        # ``from_index + 1`` forward.
        if from_index + 1 < len(STEP_IDS):
            self._session.invalidate_from(STEP_IDS[from_index + 1])
        self._save_session()

    # ------------------------------------------------------------------
    # Navigation handlers
    # ------------------------------------------------------------------

    def _on_sidebar_row_changed(self, new_index: int) -> None:
        if new_index < 0 or new_index == self._current_index:
            return
        # Jumping backward to an earlier step: warn that downstream
        # steps will be invalidated, but only if any of them have run.
        if new_index < self._current_index and self._any_downstream_run(new_index):
            answer = QtWidgets.QMessageBox.question(
                self,
                "Re-run downstream steps?",
                f"Going back to '{self._steps[new_index].label}' will reset steps "
                f"after it to pending and their cached results will be cleared. "
                f"Continue?",
                QtWidgets.QMessageBox.StandardButton.Yes
                | QtWidgets.QMessageBox.StandardButton.No,
                QtWidgets.QMessageBox.StandardButton.No,
            )
            if answer != QtWidgets.QMessageBox.StandardButton.Yes:
                # Revert sidebar selection without firing this handler again.
                self._sidebar.blockSignals(True)
                self._sidebar.setCurrentRow(self._current_index)
                self._sidebar.blockSignals(False)
                return
            self._invalidate_downstream(new_index)
        self._activate_step(new_index)

    def _on_back(self) -> None:
        if self._current_index > 0:
            self._sidebar.setCurrentRow(self._current_index - 1)

    def _on_next(self) -> None:
        if self._can_advance() and self._current_index < len(self._steps) - 1:
            self._sidebar.setCurrentRow(self._current_index + 1)

    def navigate_to_step(self, new_index: int) -> None:
        """Programmatic navigation that bypasses the "invalidate
        downstream?" prompt.

        Used by step widgets' "Show in editor" / "Jump to step" action
        buttons -- the user wants to look at a previous step, not
        wipe out cached results. Cached results stay intact. If the
        user actually edits the previous step's data, the relevant
        step widget is responsible for cascading invalidation.
        """
        if new_index < 0 or new_index >= len(self._steps):
            return
        if new_index == self._current_index:
            return
        # Sync the sidebar selection without re-firing _on_sidebar_row_changed
        # (which would re-trigger the invalidate-prompt path).
        self._sidebar.blockSignals(True)
        self._sidebar.setCurrentRow(new_index)
        self._sidebar.blockSignals(False)
        self._activate_step(new_index)

    def _activate_step(self, new_index: int) -> None:
        # Notify the outgoing step (if it's a StepBase) before swapping
        # so it can pause animations / flush transient state. The
        # _step_base_cls cache is set in _populate_steps; guard against
        # being called before that (shouldn't happen in normal flow).
        base_cls = getattr(self, "_step_base_cls", None)
        if base_cls is not None and 0 <= self._current_index < len(self._steps):
            outgoing = self._steps[self._current_index].widget
            if isinstance(outgoing, base_cls) and outgoing is not None:
                outgoing.on_deactivated()

        self._current_index = new_index
        self._content_stack.setCurrentIndex(new_index)
        self._refresh_issues_panel()
        self._refresh_navigation()
        # Persist "where the user was" so reopening drops them on the
        # same step.
        self._session.current_step = STEP_IDS[new_index]
        self._save_session()
        self.step_changed.emit(new_index, self._steps[new_index].step_id)

        # Notify the incoming step. We do this AFTER the content stack
        # swap so on_activated sees a visible widget; sometimes that
        # matters for Qt layout calculations.
        if base_cls is not None:
            incoming = self._steps[new_index].widget
            if isinstance(incoming, base_cls) and incoming is not None:
                incoming.on_activated()

    def _refresh_navigation(self) -> None:
        self._back_button.setEnabled(self._current_index > 0)
        self._next_button.setEnabled(
            self._can_advance() and self._current_index < len(self._steps) - 1
        )
        # Footer status message reflects the current step's state.
        entry = self._steps[self._current_index]
        if entry.status == StepStatus.RUNNING:
            msg = "Running..."
        elif entry.status == StepStatus.ERROR:
            msg = "Resolve errors before continuing."
        elif entry.status == StepStatus.PENDING:
            msg = "Step has not been run yet."
        elif entry.status == StepStatus.OK:
            msg = "Step complete."
        elif entry.status == StepStatus.WARNING:
            msg = "Step finished with warnings. Review and click Next when ready."
        elif entry.status == StepStatus.ADVISORY:
            msg = "Step finished with advisory notes. Review and click Next when ready."
        else:
            msg = ""
        self._footer_status.setText(msg)

    def _refresh_issues_panel(self) -> None:
        entry = self._steps[self._current_index]
        self._issues_list.clear()
        # Pad targets to issues length so we never index past the end
        # (defensive: set_step_status normally enforces this).
        targets = list(entry.issue_targets)
        if len(targets) < len(entry.issues):
            targets += [None] * (len(entry.issues) - len(targets))
        for issue, target in zip(entry.issues, targets):
            item = QtWidgets.QListWidgetItem(issue)
            if target is not None:
                # Store the (x, y) tuple so the click handler can read
                # it back without re-parsing the message string. The
                # PointingHand cursor + tooltip signal that this row
                # is interactive (most rows will have a target).
                item.setData(QtCore.Qt.ItemDataRole.UserRole, tuple(target))
                item.setToolTip("Click to show on map")
            self._issues_list.addItem(item)
        has_issues = bool(entry.issues)
        # Auto-expand when there are issues; collapse when empty so the
        # panel doesn't waste vertical space on clean steps.
        self._issues_group.setChecked(has_issues)
        self._issues_list.setVisible(has_issues)
        self._issues_group.setTitle(
            f"Issues ({len(entry.issues)})" if has_issues else "Issues"
        )

    def _on_issue_item_activated(
        self, item: QtWidgets.QListWidgetItem
    ) -> None:
        """Dispatch an issue-row click to the current step's canvas.

        The list item carries the issue's pixel target in its
        ``UserRole`` data (set by :meth:`_refresh_issues_panel`). If
        the target is present and the current step widget defines
        ``navigate_to_issue_target``, we call it. Otherwise the click
        is a no-op -- not every issue has a single location (e.g.
        "no rooms detected"), and not every step supports navigation.
        """
        target = item.data(QtCore.Qt.ItemDataRole.UserRole)
        if target is None:
            return
        if self._current_index < 0 or self._current_index >= len(self._steps):
            return
        widget = self._steps[self._current_index].widget
        navigator = getattr(widget, "navigate_to_issue_target", None)
        if navigator is None:
            return
        navigator(target)

    # ------------------------------------------------------------------
    # Helpers
    # ------------------------------------------------------------------

    def _can_advance(self) -> bool:
        """Next is disabled if the current step is running or has errors."""
        entry = self._steps[self._current_index]
        return entry.status not in (StepStatus.RUNNING, StepStatus.ERROR)

    def _any_downstream_run(self, from_index: int) -> bool:
        return any(
            self._steps[i].status != StepStatus.PENDING
            for i in range(from_index + 1, len(self._steps))
        )

    def _step_by_id(self, step_id: str) -> Optional[_StepEntry]:
        for entry in self._steps:
            if entry.step_id == step_id:
                return entry
        return None

    def _format_sidebar_label(self, entry: _StepEntry) -> str:
        badge = _BADGE_TEXT.get(entry.status, "")
        if badge:
            return f"{entry.label}    [{badge}]"
        return entry.label

    def _compose_title(self) -> str:
        return f"OfficeMapMaker - {self._map_path.name}"

    # ------------------------------------------------------------------
    # Public accessors (for tests + W2+ wiring)
    # ------------------------------------------------------------------

    @property
    def map_path(self) -> Path:
        return self._map_path

    @property
    def assignments_path(self) -> Path:
        return self._assignments_path

    @property
    def output_dir(self) -> Path:
        return self._output_dir

    @property
    def teams_path(self) -> Optional[Path]:
        return self._teams_path

    @property
    def current_step_id(self) -> str:
        return self._steps[self._current_index].step_id

    @property
    def current_step_index(self) -> int:
        return self._current_index

    @property
    def session(self) -> Session:
        """The persisted session this window is backed by."""
        return self._session

    # ------------------------------------------------------------------
    # Pipeline runner integration (W3)
    # ------------------------------------------------------------------

    def run_pipeline_step(
        self,
        step_id: str,
        func: Callable[..., tuple],
        *,
        args: tuple = (),
        kwargs: Optional[dict] = None,
        on_finished: Optional[Callable[[object, list], None]] = None,
        on_failed: Optional[Callable[[BaseException], None]] = None,
        on_canceled: Optional[Callable[[], None]] = None,
    ) -> "Optional[PipelineRunner]":
        """Kick off a background pipeline call for ``step_id``.

        The wizard's per-step widgets (W4+) call this to run their
        expensive function off the UI thread. The runner is wired to:

        - flip the step's status to ``RUNNING``, show the footer
          progress bar + Cancel button, and disable Back / Next;
        - on ``finished``, hand ``(result, issues)`` to ``on_finished``
          (the caller's responsibility to set the step's final status
          via :meth:`set_step_status` and stash the result on the
          session, e.g. ``session.calibration = result``);
        - on ``canceled``, revert the step to its prior status and
          invoke ``on_canceled``;
        - on ``failed``, set the step to ERROR with a one-line issue
          summarizing the exception, then invoke ``on_failed``.

        Returns the runner so callers can hold a reference (we also
        retain ``self._active_runner`` so the runner outlives this
        call). Returns ``None`` if another pipeline call is already
        running on this window (we serialize for simplicity -- the
        wizard's linear UX never needs concurrent runs).
        """
        from ..pipeline import PipelineRunner

        if self._active_runner is not None:
            # Don't queue / pre-empt: just refuse. The caller is
            # expected to have disabled its "Run" button already, so
            # this branch only fires on a logic bug.
            return None

        entry = self._step_by_id(step_id)
        if entry is None:
            raise ValueError(f"unknown step id: {step_id!r}")

        prior_status = entry.status
        prior_issues = list(entry.issues)
        self.set_step_status(step_id, StepStatus.RUNNING)

        # Footer UI: show progress + Cancel; disable Back / Next.
        self._progress_bar.setValue(0)
        self._progress_bar.setVisible(True)
        self._cancel_button.setVisible(True)
        self._cancel_button.setEnabled(True)
        self._back_button.setEnabled(False)
        self._next_button.setEnabled(False)

        runner = PipelineRunner(func, args=args, kwargs=kwargs, parent=self)
        self._active_runner = runner
        self._active_runner_step_id = step_id
        self._active_runner_prior_status = prior_status
        self._active_runner_prior_issues = prior_issues

        runner.progress.connect(self._on_pipeline_progress)
        runner.finished.connect(
            lambda result, issues: self._on_pipeline_finished(
                step_id, result, issues, on_finished
            )
        )
        runner.failed.connect(
            lambda exc: self._on_pipeline_failed(step_id, exc, on_failed)
        )
        runner.canceled.connect(
            lambda: self._on_pipeline_canceled(
                step_id, prior_status, prior_issues, on_canceled
            )
        )
        runner.start()
        return runner

    def _on_pipeline_progress(self, fraction: float, message: str) -> None:
        # Map [0..1] to the progress bar's int range.
        self._progress_bar.setValue(int(round(fraction * 1000)))
        if message:
            self._footer_status.setText(message)

    def _on_pipeline_finished(
        self,
        step_id: str,
        result: object,
        issues: list,
        on_finished: Optional[Callable[[object, list], None]],
    ) -> None:
        self._reset_pipeline_footer()
        self._active_runner = None
        self._active_runner_step_id = None
        self._active_runner_prior_status = None
        self._active_runner_prior_issues = None
        if on_finished is not None:
            on_finished(result, issues)
        # If on_finished didn't update the step (e.g. tests that don't
        # care), revert it from RUNNING so the UI isn't stuck.
        entry = self._step_by_id(step_id)
        if entry is not None and entry.status == StepStatus.RUNNING:
            self.set_step_status(step_id, StepStatus.OK)

    def _on_pipeline_failed(
        self,
        step_id: str,
        exc: BaseException,
        on_failed: Optional[Callable[[BaseException], None]],
    ) -> None:
        self._reset_pipeline_footer()
        self._active_runner = None
        self._active_runner_step_id = None
        self._active_runner_prior_status = None
        self._active_runner_prior_issues = None
        message = f"{type(exc).__name__}: {exc}"
        self.set_step_status(step_id, StepStatus.ERROR, issues=[message])
        if on_failed is not None:
            on_failed(exc)

    def _on_pipeline_canceled(
        self,
        step_id: str,
        prior_status: StepStatus,
        prior_issues: List[str],
        on_canceled: Optional[Callable[[], None]],
    ) -> None:
        self._reset_pipeline_footer()
        self._active_runner = None
        self._active_runner_step_id = None
        self._active_runner_prior_status = None
        self._active_runner_prior_issues = None
        # Revert to the step's status + issues from before the run so
        # the user can re-attempt without confusing leftover state.
        self.set_step_status(step_id, prior_status, issues=prior_issues)
        self._footer_status.setText("Canceled.")
        if on_canceled is not None:
            on_canceled()

    def _on_cancel_pipeline(self) -> None:
        if self._active_runner is not None:
            self._cancel_button.setEnabled(False)
            self._footer_status.setText("Canceling...")
            self._active_runner.cancel()

    def _reset_pipeline_footer(self) -> None:
        self._progress_bar.setVisible(False)
        self._progress_bar.setValue(0)
        self._cancel_button.setVisible(False)
        self._cancel_button.setEnabled(True)
        # Re-enable nav: _refresh_navigation handles enable/disable
        # based on the new step status.
        self._refresh_navigation()

    # ------------------------------------------------------------------
    # Persistence
    # ------------------------------------------------------------------

    def _save_session(self) -> None:
        """Write the current session to disk.

        Called from every state-mutating handler. Swallows write
        errors and surfaces them in the footer so a broken save
        doesn't take the whole window down. (The session file is a
        convenience for resume — if it can't be written, the in-memory
        wizard is still usable for this run.)
        """
        try:
            self._session.save()
        except OSError as exc:
            self._footer_status.setText(
                f"Warning: could not save session ({exc.strerror or exc}). "
                "Your work is still in memory for this run."
            )

    def closeEvent(self, event: QtGui.QCloseEvent) -> None:
        """Final save when the user closes the window."""
        self._save_window_geometry()
        self._save_session()
        super().closeEvent(event)

    # ------------------------------------------------------------------
    # Window geometry persistence
    # ------------------------------------------------------------------

    def _restore_window_geometry(self) -> None:
        """Restore the last-saved window geometry, or fall back to defaults.

        Reads the persisted geometry blob from :func:`_make_settings`
        and applies it via ``restoreGeometry``. If nothing is saved,
        if the restored geometry sits entirely off any connected
        display (e.g. saved on a monitor that has since been
        unplugged), or if ``restoreGeometry`` itself rejects the
        blob, we fall back to ``_DEFAULT_WINDOW_SIZE``.
        """
        settings = _make_settings()
        geom = settings.value(_SETTINGS_GEOMETRY_KEY)
        applied = False
        if isinstance(geom, (bytes, QtCore.QByteArray)) and bytes(geom):
            try:
                applied = bool(self.restoreGeometry(QtCore.QByteArray(geom)))
            except Exception:  # pylint: disable=broad-except
                applied = False
            if applied and not _geometry_is_on_screen(self.frameGeometry()):
                # Saved on a now-disconnected screen. Reset.
                applied = False

        if not applied:
            self.resize(_DEFAULT_WINDOW_SIZE)

        # Restore maximized / fullscreen state (a separate blob from
        # geometry; restoreState handles dock layouts but for a wizard
        # without docks it's mostly window-state bits we care about).
        state = settings.value(_SETTINGS_STATE_KEY)
        if isinstance(state, (bytes, QtCore.QByteArray)) and bytes(state):
            try:
                self.restoreState(QtCore.QByteArray(state))
            except Exception:  # pylint: disable=broad-except
                pass

    def _save_window_geometry(self) -> None:
        """Persist the current window geometry + state to QSettings.

        Called from ``closeEvent`` so the next launch reopens with
        the same size, position, and maximized state.
        """
        settings = _make_settings()
        settings.setValue(_SETTINGS_GEOMETRY_KEY, self.saveGeometry())
        settings.setValue(_SETTINGS_STATE_KEY, self.saveState())


# One-line descriptions shown inside each placeholder so the W1 shell is
# self-documenting until the real step widgets land.
_PLACEHOLDER_DESCRIPTIONS: Dict[str, str] = {
    "calibrate": (
        "Run OCR + room detection on the map, then open the editor "
        "(implemented in milestone W4) so you can review and fix "
        "labels, room polygons, and wall patches."
    ),
    "validate_labels": (
        "Cross-check every assigned office against the calibration "
        "(implemented in milestone W5). Errors block; warnings are "
        "informational."
    ),
    "validate_fill": (
        "Look for flood-fill leaks so wall_patches can be added if you "
        "want a perfectly clean render (implemented in milestone W6). "
        "Advisory only — the renderer auto-clips."
    ),
    "layout": (
        "Plan where each person's name goes in their office and stage "
        "abbreviation / leader-line fallbacks (implemented in W7)."
    ),
    "build": (
        "Render the full-resolution colored composite (implemented in W8)."
    ),
    "tile": (
        "Split the composite into letter-size print tiles and a PDF "
        "(implemented in W9). This is the terminal step."
    ),
}
