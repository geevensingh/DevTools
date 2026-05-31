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
import re
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
_ISSUES_PANEL_INITIAL_HEIGHT = 200


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
    # Parallel to ``issues``: the short machine-readable code for each
    # issue (e.g. ``"orphan_room"``, ``"leak_into_other_office"``) and
    # its severity (``"error"`` / ``"warning"`` / ``"info"``). Used by
    # the issues panel to group identical kinds under filter chips and
    # to color those chips by severity. Empty strings are valid for
    # callers that don't yet pass a code (the chip row will then hide).
    issue_codes: List[str] = field(default_factory=list)
    issue_severities: List[str] = field(default_factory=list)


def _pad_parallel(
    values: Optional[List], length: int, *, fill
) -> List:
    """Return a list of exactly ``length`` items, padding / truncating
    ``values``. Used to keep ``_StepEntry``'s parallel issue lists in
    lock-step with the message list even when a caller passes the
    wrong length (or ``None``)."""
    if values is None:
        return [fill] * length
    out = list(values)
    if len(out) < length:
        out += [fill] * (length - len(out))
    return out[:length]


# Severity rank used by :func:`_max_severity` to pick the "worst" of a
# set. Unknown / empty severities sort lowest. ``advisory`` falls
# between ``info`` and ``warning``: it's stronger than a hint but
# weaker than an actual warning.
_SEVERITY_RANK: Dict[str, int] = {
    "": 0,
    "info": 1,
    "advisory": 2,
    "warning": 3,
    "error": 4,
}


def _max_severity(a: str, b: str) -> str:
    """Return whichever severity is worst (highest rank)."""
    return a if _SEVERITY_RANK.get(a, 0) >= _SEVERITY_RANK.get(b, 0) else b


# Colors for the chip border (severity-coded). Keep these subtle so the
# chips don't fight the rest of the UI; full color is only used on the
# 1-2px border, not the fill.
_CHIP_SEVERITY_COLOR: Dict[str, str] = {
    "error": "#c62828",       # red 800
    "warning": "#ef6c00",     # orange 800
    "advisory": "#1565c0",    # blue 800
    "info": "#1565c0",
    "": "#888888",
}


def _chip_stylesheet(severity: str) -> str:
    """Return a QToolButton stylesheet for a chip of this severity.

    The chip uses a colored border to encode severity, white fill when
    inactive, and a tinted fill when checked so the active filter is
    unmistakable at a glance.
    """
    border = _CHIP_SEVERITY_COLOR.get(severity, "#888888")
    # Pre-multiplied 20%-opacity tint for the checked state.
    tint = {
        "error": "#fde7e7",
        "warning": "#ffeed5",
        "advisory": "#e3effa",
        "info": "#e3effa",
        "": "#eeeeee",
    }.get(severity, "#eeeeee")
    return (
        "QToolButton {"
        f"  border: 1px solid {border};"
        "  border-radius: 10px;"
        "  padding: 2px 8px;"
        "  background: white;"
        f"  color: {border};"
        "  font-size: 11px;"
        "}"
        "QToolButton:hover {"
        f"  background: {tint};"
        "}"
        "QToolButton:checked {"
        f"  background: {tint};"
        f"  border: 2px solid {border};"
        "  font-weight: bold;"
        "}"
    )


# Pattern used to peel off the leading "[severity] code: " prefix that
# ``CalibrationIssue.__str__`` and friends prepend to every message.
# Used when a single-kind chip is active so the rows aren't dominated
# by repeated prefix text.
_PREFIX_RE = re.compile(r"^\[(?:error|warning|info|advisory)\]\s+[^:]+:\s+")


def _strip_severity_code_prefix(text: str) -> str:
    return _PREFIX_RE.sub("", text, count=1)


def _format_issues_title(total: int, severities: List[str]) -> str:
    """Build the group-box title: ``Issues (N) - X errors, Y warnings``.

    Trailing parts are omitted when their count is zero (so the title
    stays compact when there are only errors, etc.). ``Issues`` alone
    is returned when ``total == 0``.
    """
    if total == 0:
        return "Issues"
    err = sum(1 for s in severities if s == "error")
    warn = sum(1 for s in severities if s == "warning")
    adv = sum(1 for s in severities if s == "advisory")
    parts: List[str] = []
    if err:
        parts.append(f"{err} error{'s' if err != 1 else ''}")
    if warn:
        parts.append(f"{warn} warning{'s' if warn != 1 else ''}")
    if adv:
        parts.append(f"{adv} advisory")
    suffix = f" - {', '.join(parts)}" if parts else ""
    return f"Issues ({total}){suffix}"


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
            issue_codes = [iss.code for iss in st.issues]
            issue_severities = [iss.severity for iss in st.issues]
            self._steps.append(
                _StepEntry(
                    step_id=sid,
                    label=label,
                    status=_FROM_SESSION_STATUS.get(st.status, StepStatus.PENDING),
                    issues=issues_text,
                    issue_codes=issue_codes,
                    issue_severities=issue_severities,
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

        # Issues panel: a collapsible QGroupBox with a filter-chip row
        # plus a scrollable list. The chip row groups issues by
        # ``code`` (e.g. ``orphan_room``) so the user can see "how
        # many of each kind" at a glance and click to filter the list
        # to one kind. In W1 the panel is always empty; W4+ steps
        # populate it as their pipeline calls complete.
        self._issues_group = QtWidgets.QGroupBox("Issues", right)
        self._issues_group.setCheckable(True)
        self._issues_group.setChecked(False)
        self._issues_group.setMaximumHeight(_ISSUES_PANEL_INITIAL_HEIGHT)
        issues_layout = QtWidgets.QVBoxLayout(self._issues_group)
        issues_layout.setContentsMargins(8, 8, 8, 8)
        issues_layout.setSpacing(4)

        # Chip row -- horizontally scrollable, holds an "All (N)" chip
        # plus one chip per unique kind. Hidden when no chips would
        # have content (e.g. on legacy sessions where codes are empty).
        self._chip_scroll = QtWidgets.QScrollArea(self._issues_group)
        self._chip_scroll.setWidgetResizable(True)
        self._chip_scroll.setFrameShape(QtWidgets.QFrame.Shape.NoFrame)
        self._chip_scroll.setVerticalScrollBarPolicy(
            QtCore.Qt.ScrollBarPolicy.ScrollBarAlwaysOff
        )
        self._chip_scroll.setHorizontalScrollBarPolicy(
            QtCore.Qt.ScrollBarPolicy.ScrollBarAsNeeded
        )
        self._chip_scroll.setFixedHeight(34)
        chip_container = QtWidgets.QWidget(self._chip_scroll)
        self._chip_row_layout = QtWidgets.QHBoxLayout(chip_container)
        self._chip_row_layout.setContentsMargins(0, 0, 0, 0)
        self._chip_row_layout.setSpacing(4)
        self._chip_row_layout.addStretch(1)
        self._chip_scroll.setWidget(chip_container)
        issues_layout.addWidget(self._chip_scroll)
        # Cache: code -> chip button (None key = "All"). Rebuilt on
        # every _refresh_issues_panel call.
        self._chip_buttons: Dict[Optional[str], QtWidgets.QToolButton] = {}
        # Active chip key (None = "All", "" = "Other" / uncoded,
        # else a real code like "orphan_room").
        self._active_issue_code: Optional[str] = None
        # Track which step the active chip belongs to so we reset on
        # step change rather than carrying a stale filter across
        # unrelated step panels.
        self._active_issue_step_id: Optional[str] = None

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
        self._issues_group.toggled.connect(self._on_issues_group_toggled)
        self._issues_list.setVisible(False)
        self._chip_scroll.setVisible(False)
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
        from .steps.layout_step import LayoutStep
        from .steps.validate_fill_step import ValidateFillStep
        from .steps.validate_labels_step import ValidateLabelsStep

        self._step_base_cls = StepBase  # cached for _activate_step lifecycle dispatch
        for entry in self._steps:
            if entry.step_id == "calibrate":
                entry.widget = CalibrateStep(self)
            elif entry.step_id == "validate_labels":
                entry.widget = ValidateLabelsStep(self)
            elif entry.step_id == "validate_fill":
                entry.widget = ValidateFillStep(self)
            elif entry.step_id == "layout":
                entry.widget = LayoutStep(self)
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
        issue_codes: Optional[List[str]] = None,
        issue_severities: Optional[List[str]] = None,
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

        ``issue_codes`` and ``issue_severities`` are optional lists
        parallel to ``issues``. ``issue_codes`` holds the short
        machine-readable kind (e.g. ``"orphan_room"``) used by the
        issues-panel filter chips; empty string for "unclassified".
        ``issue_severities`` holds ``"error"`` / ``"warning"`` /
        ``"info"`` per row. When omitted, codes default to ``""`` and
        severities are derived from the step ``status`` (the old
        placeholder behaviour) so legacy callers still work.
        """
        entry = self._step_by_id(step_id)
        if entry is None:
            return
        entry.status = status
        entry.issues = list(issues or [])
        entry.issue_targets = _pad_parallel(
            issue_targets, len(entry.issues), fill=None
        )
        entry.issue_codes = _pad_parallel(
            issue_codes, len(entry.issues), fill=""
        )
        default_severity = (
            "warning" if status == StepStatus.WARNING
            else "info" if status == StepStatus.ADVISORY
            else "error" if status == StepStatus.ERROR
            else "info"
        )
        entry.issue_severities = _pad_parallel(
            issue_severities, len(entry.issues), fill=default_severity
        )
        idx = self._steps.index(entry)
        self._sidebar.item(idx).setText(self._format_sidebar_label(entry))
        if idx == self._current_index:
            self._refresh_issues_panel()
        self._refresh_navigation()
        # Mirror into the persisted session. We now have real per-issue
        # codes + severities so we no longer collapse them all to a
        # single "placeholder" Issue.
        sess_status = _TO_SESSION_STATUS[status]
        sess_issues = [
            SessionIssue(
                code=entry.issue_codes[i] or "placeholder",
                severity=entry.issue_severities[i],
                message=entry.issues[i],
            )
            for i in range(len(entry.issues))
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
            entry.issue_codes = []
            entry.issue_severities = []
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

        # 1. Defensive: enforce parallel-list invariants. This protects
        #    against legacy session-restore paths that may seed
        #    ``issues`` without filling the parallel lists.
        n = len(entry.issues)
        targets = _pad_parallel(entry.issue_targets, n, fill=None)
        codes = _pad_parallel(entry.issue_codes, n, fill="")
        severities = _pad_parallel(entry.issue_severities, n, fill="")

        # 2. Reset the active chip if the step changed under us, or if
        #    the previously-active code no longer has any items.
        if self._active_issue_step_id != entry.step_id:
            self._active_issue_code = None
            self._active_issue_step_id = entry.step_id
        active = self._active_issue_code
        if active is not None and active not in codes:
            active = None
            self._active_issue_code = None

        # 3. Build kind summary (count + worst severity per code).
        #    Empty-string codes are bucketed under "Other" to keep them
        #    addressable; if every code is empty (legacy data) we skip
        #    the chip row entirely.
        kind_counts: Dict[str, int] = {}
        kind_severity: Dict[str, str] = {}
        for code, severity in zip(codes, severities):
            key = code or ""
            kind_counts[key] = kind_counts.get(key, 0) + 1
            kind_severity[key] = _max_severity(
                kind_severity.get(key, ""), severity
            )
        has_real_codes = any(k for k in kind_counts.keys())

        # 4. Rebuild the chip row from scratch each call. The chip
        #    count is small (rarely > 10) so the cost is negligible
        #    and we avoid stale-button bugs.
        self._rebuild_chip_row(
            kind_counts, kind_severity, active=active,
            show_chips=has_real_codes and n > 0,
        )

        # 5. Render the list, filtering by the active chip.
        self._issues_list.clear()
        for i in range(n):
            code = codes[i]
            if active is not None and (code or "") != active:
                continue
            text = entry.issues[i]
            if active is not None:
                # When filtering by a single kind, drop the redundant
                # "[severity] code: " prefix so the rows read cleanly.
                text = _strip_severity_code_prefix(text)
            item = QtWidgets.QListWidgetItem(text)
            target = targets[i]
            if target is not None:
                item.setData(QtCore.Qt.ItemDataRole.UserRole, tuple(target))
                item.setToolTip("Click to show on map")
            self._issues_list.addItem(item)

        # 6. Title + visibility. Title shows total + severity split so
        #    "how big is the problem?" is answerable without expanding.
        has_issues = n > 0
        self._issues_group.setChecked(has_issues)
        self._issues_list.setVisible(has_issues)
        self._chip_scroll.setVisible(has_issues and has_real_codes)
        self._issues_group.setTitle(_format_issues_title(n, severities))

    def _on_issues_group_toggled(self, checked: bool) -> None:
        """Show / hide the content when the group's collapse arrow fires."""
        self._issues_list.setVisible(checked)
        # Only show chips if we have any and the panel is expanded.
        # ``count() > 1`` accounts for the trailing addStretch item.
        has_chips = bool(self._chip_buttons)
        self._chip_scroll.setVisible(checked and has_chips)

    def _rebuild_chip_row(
        self,
        kind_counts: Dict[str, int],
        kind_severity: Dict[str, str],
        *,
        active: Optional[str],
        show_chips: bool,
    ) -> None:
        """Replace the chip row contents from a fresh count map.

        Chips are sorted by count (desc), then by code (asc) so the
        ordering is stable across refreshes when counts tie. The
        "All (N)" chip is always first.
        """
        # Tear down old chips. The trailing addStretch item must be
        # preserved so the row stays left-aligned.
        for btn in list(self._chip_buttons.values()):
            btn.setParent(None)
            btn.deleteLater()
        self._chip_buttons.clear()

        if not show_chips:
            return

        total = sum(kind_counts.values())
        # Worst severity across all kinds drives the "All" chip color.
        all_sev = ""
        for sev in kind_severity.values():
            all_sev = _max_severity(all_sev, sev)
        # "All" is keyed by None.
        all_btn = self._make_chip(
            label=f"All ({total})", severity=all_sev,
            checked=(active is None),
        )
        all_btn.clicked.connect(lambda _checked=False: self._on_chip_clicked(None))
        self._insert_chip(all_btn)
        self._chip_buttons[None] = all_btn

        # Sort kinds by descending count, then by code asc.
        ordered = sorted(
            kind_counts.items(), key=lambda kv: (-kv[1], kv[0])
        )
        for code, count in ordered:
            label_code = code if code else "Other"
            btn = self._make_chip(
                label=f"{label_code} ({count})",
                severity=kind_severity.get(code, ""),
                checked=(active == code),
            )
            # Bind ``code`` via default-arg trick so each lambda
            # closes over its own value.
            btn.clicked.connect(
                lambda _checked=False, c=code: self._on_chip_clicked(c)
            )
            self._insert_chip(btn)
            self._chip_buttons[code] = btn

    def _make_chip(
        self, *, label: str, severity: str, checked: bool
    ) -> QtWidgets.QToolButton:
        """Create one chip button styled by severity."""
        btn = QtWidgets.QToolButton(self._issues_group)
        btn.setText(label)
        btn.setCheckable(True)
        btn.setChecked(checked)
        btn.setCursor(QtCore.Qt.CursorShape.PointingHandCursor)
        btn.setAutoRaise(False)
        btn.setStyleSheet(_chip_stylesheet(severity))
        return btn

    def _insert_chip(self, btn: QtWidgets.QToolButton) -> None:
        # Insert before the trailing stretch (the last item).
        idx = max(0, self._chip_row_layout.count() - 1)
        self._chip_row_layout.insertWidget(idx, btn)

    def _on_chip_clicked(self, code: Optional[str]) -> None:
        """Filter the issues list to one kind (or All when ``code`` is None)."""
        # If the user clicked the already-active chip, treat that as a
        # second click that toggles back to "All". This mirrors how
        # GitHub's label filters work and avoids "stuck" filters.
        if self._active_issue_code == code:
            self._active_issue_code = None
        else:
            self._active_issue_code = code
        self._refresh_issues_panel()

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
