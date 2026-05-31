"""Top-level wizard window: sidebar + stacked content + issues panel + footer.

This is the W1 shell. It owns step navigation and status badges. Real
per-step content widgets are added in W4..W9; until then each step shows
a placeholder. See plan.md section 14 for the design rationale.

Navigation rules (per plan.md section 14.3):
- Sidebar lists the six steps with status badges (pending/running/ok/
  warning/error/advisory). Clicking a sidebar row jumps to that step.
- Jumping BACK to an earlier step prompts to invalidate downstream
  steps (they revert to pending and their cached results clear).
- The Back / Next footer is keyboard-equivalent. Next is disabled
  whenever the current step's status is ``running`` or ``error``.
  Advisory steps still require an explicit Next click (per resolved
  decision Q3 in section 14.10).
"""

from __future__ import annotations

import enum
from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable, Dict, List, Optional

from PySide6 import QtCore, QtGui, QtWidgets


# Window starts at a reasonable working size that fits on a 1366x768
# laptop while leaving room for the sidebar.
_DEFAULT_WINDOW_SIZE = QtCore.QSize(1366, 800)
_SIDEBAR_WIDTH = 260
_ISSUES_PANEL_INITIAL_HEIGHT = 140


class StepStatus(enum.Enum):
    """Lifecycle state shown in the sidebar badge for each step."""

    PENDING = "pending"          # not yet attempted
    RUNNING = "running"          # background pipeline call in progress
    OK = "ok"                    # finished cleanly, no issues
    WARNING = "warning"          # finished with warnings only
    ADVISORY = "advisory"        # finished with informational notes only
    ERROR = "error"              # finished with blocking errors


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
        map_path: Path,
        assignments_path: Path,
        output_dir: Path,
        teams_path: Optional[Path] = None,
    ) -> None:
        super().__init__()
        self._map_path = map_path
        self._assignments_path = assignments_path
        self._output_dir = output_dir
        self._teams_path = teams_path

        self.setWindowTitle(self._compose_title())
        self.resize(_DEFAULT_WINDOW_SIZE)

        self._steps: List[_StepEntry] = [
            _StepEntry(step_id=sid, label=label) for sid, label in _STEPS
        ]
        self._current_index = 0

        # Build UI: central widget is a horizontal splitter holding the
        # sidebar and a right-hand container; the right container stacks
        # content + issues panel vertically, with the Back/Next footer
        # pinned at the bottom.
        self._build_ui()
        self._populate_sidebar()
        self._populate_steps()
        self._refresh_navigation()
        # Initial selection so the right pane shows something useful.
        self._sidebar.setCurrentRow(0)

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
        issues_layout.addWidget(self._issues_list)
        # Toggling the groupbox shows/hides its content; this gives a
        # cheap collapse-to-titlebar without writing a custom widget.
        self._issues_group.toggled.connect(self._issues_list.setVisible)
        self._issues_list.setVisible(False)
        right_layout.addWidget(self._issues_group)

        # Footer: status label on the left, Back / Next on the right.
        footer = QtWidgets.QFrame(right)
        footer.setFrameShape(QtWidgets.QFrame.Shape.StyledPanel)
        footer.setStyleSheet("QFrame { background: #fafafa; }")
        footer_layout = QtWidgets.QHBoxLayout(footer)
        footer_layout.setContentsMargins(12, 8, 12, 8)
        self._footer_status = QtWidgets.QLabel("", footer)
        self._footer_status.setStyleSheet("QLabel { color: #555; }")
        footer_layout.addWidget(self._footer_status, stretch=1)
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
        for entry in self._steps:
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
    ) -> None:
        """Update a step's status badge and (optionally) issues list.

        Public so W3+ pipeline runners can call it as their finished
        signal fires. Also called from the W1 dev simulate-completion
        buttons.
        """
        entry = self._step_by_id(step_id)
        if entry is None:
            return
        entry.status = status
        entry.issues = list(issues or [])
        idx = self._steps.index(entry)
        self._sidebar.item(idx).setText(self._format_sidebar_label(entry))
        if idx == self._current_index:
            self._refresh_issues_panel()
        self._refresh_navigation()

    def _invalidate_downstream(self, from_index: int) -> None:
        """Reset steps after ``from_index`` to PENDING with no issues.

        Called when the user jumps back and confirms invalidation.
        """
        for i in range(from_index + 1, len(self._steps)):
            entry = self._steps[i]
            entry.status = StepStatus.PENDING
            entry.issues = []
            self._sidebar.item(i).setText(self._format_sidebar_label(entry))

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

    def _activate_step(self, new_index: int) -> None:
        self._current_index = new_index
        self._content_stack.setCurrentIndex(new_index)
        self._refresh_issues_panel()
        self._refresh_navigation()
        self.step_changed.emit(new_index, self._steps[new_index].step_id)

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
        for issue in entry.issues:
            self._issues_list.addItem(issue)
        has_issues = bool(entry.issues)
        # Auto-expand when there are issues; collapse when empty so the
        # panel doesn't waste vertical space on clean steps.
        self._issues_group.setChecked(has_issues)
        self._issues_list.setVisible(has_issues)
        self._issues_group.setTitle(
            f"Issues ({len(entry.issues)})" if has_issues else "Issues"
        )

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
