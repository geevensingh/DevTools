"""Step 1 of the wizard: interactive calibration.

This step ports the existing standalone editor (``officemapmaker.editor``)
into the wizard's content area. The widget has two visual states:

1. **Landing pane** -- shown when ``session.calibration is None``. A short
   explanation plus a Run button. On first activation we auto-fire the
   pipeline call (so a fresh session with no cached calibration starts
   the OCR + room-detection in the background immediately, without the
   user having to discover the button).
2. **Editor pane** -- shown once a calibration exists (either freshly
   computed or restored from the persisted session). A simple toolbar
   on top, the ``MapCanvas`` in the centre, and the ``InspectorPanel``
   on the right (inside a ``QSplitter`` so the user can resize). All
   edits go through the existing ``EditorController`` so undo / redo /
   add-label / add-room / add-wall-patch all work exactly as they do in
   the standalone editor.

The Calibration object held by the controller IS the same object stored
in ``session.calibration`` (same Python reference), so every command
that mutates the calibration also mutates the session in place. We
listen to the undo stack and call ``session.save()`` after every
push / undo / redo so the on-disk session file stays in sync.

The editor's File menu (Save / Save As / Reload) is intentionally
*not* exposed here: the session.json is the single source of truth in
the wizard model, and the wizard's footer is the gate to move past
this step. A Re-run calibration button is offered after the first
successful run so the user can wipe everything and start over without
hand-editing the session file.
"""

from __future__ import annotations

from pathlib import Path
from typing import TYPE_CHECKING, List, Optional

from PySide6 import QtCore, QtGui, QtWidgets

from ...calibrate import CalibrationIssue, calibrate_map, revalidate_calibration
from ..main_window import StepStatus
from .base import StepBase

if TYPE_CHECKING:
    from ...calibration import Calibration
    from ..main_window import MainWindow


# Splitter starts with the inspector ~320px wide -- enough for the
# longest field labels without crowding the canvas on a 1366-wide
# window. The canvas takes the rest.
_INSPECTOR_INITIAL_WIDTH = 320


class CalibrateStep(StepBase):
    """Calibrate-map step: landing pane → background pipeline → embedded editor."""

    def __init__(self, main_window: "MainWindow") -> None:
        super().__init__(main_window)

        # Lazy-built editor pane: the canvas / inspector / controller
        # are heavy to construct (loading the full map pixmap, decoding
        # ~440 RLE polygons, etc.) so we only build them once we have a
        # calibration to show.
        self._editor_built = False
        self._canvas: Optional["object"] = None  # MapCanvas; typed object to avoid PySide6 import at module load
        self._inspector: Optional["object"] = None
        self._controller: Optional["object"] = None
        self._toolbar: Optional[QtWidgets.QToolBar] = None

        self._stack = QtWidgets.QStackedWidget(self)
        layout = QtWidgets.QVBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        layout.addWidget(self._stack)

        self._landing = self._build_landing()
        self._stack.addWidget(self._landing)

        # Editor pane built lazily; placeholder until then so
        # ``_stack.indexOf`` is well-defined.
        self._editor_pane: Optional[QtWidgets.QWidget] = None

    # ------------------------------------------------------------------
    # Landing pane
    # ------------------------------------------------------------------

    def _build_landing(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        outer = QtWidgets.QVBoxLayout(widget)
        outer.setContentsMargins(60, 60, 60, 60)
        outer.setSpacing(20)

        title = QtWidgets.QLabel("Calibrate the map")
        f = title.font()
        f.setPointSize(f.pointSize() + 6)
        f.setBold(True)
        title.setFont(f)
        outer.addWidget(title)

        desc = QtWidgets.QLabel(
            "Calibration scans the map image with OCR to detect office "
            "numbers and identifies enclosed rooms via connected-component "
            "analysis. The result is a list of labels and rooms you can "
            "review and edit before moving on.\n\n"
            "This usually takes 30-90 seconds for a typical floor plan; "
            "OCR is the slow part."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        outer.addWidget(desc)

        self._run_button = QtWidgets.QPushButton("Run calibration")
        self._run_button.setMinimumHeight(36)
        self._run_button.setMaximumWidth(220)
        self._run_button.clicked.connect(self._on_run_clicked)
        outer.addWidget(self._run_button)

        outer.addStretch(1)
        return widget

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def on_activated(self) -> None:
        cal = self.main_window.session.calibration
        if cal is not None:
            # Coming back to a step that's already calibrated --
            # rebuild the editor pane on first activation, otherwise
            # just stay where we are. If the map image can't be loaded
            # (corrupt file, missing, etc.), fall back to the landing
            # pane and let the user Re-run.
            try:
                self._ensure_editor_built()
                if self._editor_pane is not None:
                    self._stack.setCurrentWidget(self._editor_pane)
                    return
            except (FileNotFoundError, OSError):
                self._stack.setCurrentWidget(self._landing)
                return

        # No calibration yet: show the landing pane and wait for the
        # user to click Run. We deliberately do NOT auto-fire here:
        #
        # 1. Auto-firing on a fresh window robs the user of a chance to
        #    sanity-check the input paths shown in the status bar
        #    before the slow OCR pass starts.
        # 2. Auto-firing conflicts with tests that exercise
        #    ``run_pipeline_step`` against the calibrate slot with
        #    synthetic functions.
        #
        # The Run button is loud + obviously clickable, so the one
        # extra click on first launch is fine.
        self._stack.setCurrentWidget(self._landing)

    # ------------------------------------------------------------------
    # Pipeline run
    # ------------------------------------------------------------------

    def _on_run_clicked(self) -> None:
        self._kick_off_calibration()

    def _kick_off_calibration(self) -> None:
        """Start the background calibrate_map call via the pipeline runner."""
        self._run_button.setEnabled(False)
        runner = self.main_window.run_pipeline_step(
            "calibrate",
            calibrate_map,
            args=(self.main_window.map_path,),
            on_finished=self._on_calibration_finished,
            on_failed=self._on_calibration_failed,
            on_canceled=self._on_calibration_canceled,
        )
        if runner is None:
            # Another runner is in-flight (shouldn't happen in normal
            # flow); re-enable the button so the user can try again.
            self._run_button.setEnabled(True)

    def _on_calibration_finished(self, result, issues: list) -> None:
        from ...calibration import Calibration

        if not isinstance(result, Calibration):
            # Shouldn't happen given calibrate_map's contract, but
            # report it cleanly rather than crashing.
            self._run_button.setEnabled(True)
            self.main_window.set_step_status(
                "calibrate",
                StepStatus.ERROR,
                issues=[
                    f"calibrate_map returned an unexpected type: {type(result).__name__}"
                ],
            )
            return

        # Stash on the session FIRST so anything that triggers a save
        # (e.g. set_step_status below) persists the new calibration.
        self.main_window.session.calibration = result

        # Update the step badge based on the issues list BEFORE
        # attempting to mount the editor pane. The mount loads the
        # map image into a QPixmap and can fail (corrupt PNG, file
        # vanished, etc.); we don't want a mount failure to leave the
        # step stuck on RUNNING when the calibration itself is valid.
        status, issue_strs = _classify_issues(issues)
        self.main_window.set_step_status(
            "calibrate", status, issues=issue_strs
        )

        # Now try to mount the editor. If the map image can't be
        # loaded, surface it as an additional warning rather than
        # propagating the exception (the calibration data is still
        # valid; the user can fix the file and Re-run).
        try:
            self._ensure_editor_built()
            if self._editor_pane is not None:
                self._stack.setCurrentWidget(self._editor_pane)
        except (FileNotFoundError, OSError) as exc:
            extra = f"Could not load map image for editing: {exc}"
            self.main_window.set_step_status(
                "calibrate",
                StepStatus.WARNING if status == StepStatus.OK else status,
                issues=issue_strs + [extra],
            )

        self._run_button.setEnabled(True)

    def _on_calibration_failed(self, exc: BaseException) -> None:
        self._run_button.setEnabled(True)
        # MainWindow.run_pipeline_step has already set ERROR + an
        # issue message; nothing else to do here.

    def _on_calibration_canceled(self) -> None:
        self._run_button.setEnabled(True)
        # Status reverted to prior (PENDING) by the runner; user is
        # back on the landing pane.

    # ------------------------------------------------------------------
    # Editor pane (lazy)
    # ------------------------------------------------------------------

    def _ensure_editor_built(self) -> None:
        if self._editor_built:
            self._refresh_editor_from_session()
            return

        cal = self.main_window.session.calibration
        if cal is None:
            return

        from ...editor.canvas import MapCanvas
        from ...editor.controller import EditorController
        from ...editor.sidebar import InspectorPanel

        pane = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(pane)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)

        # Toolbar at the top: most-used editor actions. The full menu
        # bar from the standalone editor's QMainWindow doesn't fit
        # cleanly into the wizard's content area, so we expose the
        # actions that drive day-to-day fixes (OCR id edit comes via
        # the inspector; this toolbar handles undo / redo / view
        # toggles / add tools / delete).
        self._toolbar = self._build_toolbar()
        layout.addWidget(self._toolbar)

        # Splitter: canvas on the left (stretchy), inspector on the
        # right. Stored separately so we can resize defaults sensibly.
        splitter = QtWidgets.QSplitter(QtCore.Qt.Orientation.Horizontal, pane)
        splitter.setChildrenCollapsible(False)
        self._canvas = MapCanvas(splitter)
        self._inspector = InspectorPanel(splitter)
        self._inspector.setMinimumWidth(260)
        splitter.addWidget(self._canvas)
        splitter.addWidget(self._inspector)
        splitter.setStretchFactor(0, 1)
        splitter.setStretchFactor(1, 0)
        # Initial sizes: give the inspector its default width, canvas the rest.
        splitter.setSizes([10_000, _INSPECTOR_INITIAL_WIDTH])
        layout.addWidget(splitter, stretch=1)

        # Load the map image + calibration into the canvas.
        self._canvas.set_map_image(self.main_window.map_path)
        self._canvas.set_calibration(cal)

        # Build the controller. We pass a synthetic calibration_path
        # that points at the session file's directory so the editor's
        # own save() (if ever wired) writes somewhere predictable;
        # however we don't expose Save in the toolbar -- the session
        # file is the only persistence path in the wizard.
        synthetic_path = self.main_window.output_dir / "_wizard_calibration.json"
        self._controller = EditorController(
            calibration=cal,
            calibration_path=synthetic_path,
            canvas=self._canvas,
            inspector=self._inspector,
            map_path=self.main_window.map_path,
            parent=self,
        )

        # Wire up the toolbar actions to the controller's undo stack
        # and canvas now that both exist.
        self._wire_toolbar_actions()

        # Mirror to session on every undo / redo / push. The
        # calibration is mutated in place by every command, so we
        # just need to re-save the session. We also re-evaluate the
        # step's status from the live issues set (handy when the user
        # fixes a duplicate id via the inspector -- the warning
        # should clear).
        undo_stack = self._controller.undo_stack()
        undo_stack.indexChanged.connect(self._on_undo_index_changed)

        self._editor_pane = pane
        self._stack.addWidget(pane)
        self._editor_built = True

    def _refresh_editor_from_session(self) -> None:
        """Reload the canvas if the session's calibration has been swapped.

        Currently called only when ``on_activated`` finds an existing
        calibration and an already-built editor. The only path that
        replaces ``session.calibration`` wholesale is a fresh
        Re-run; in that case we tear down the controller's undo stack
        and reload the canvas. In-place mutations don't need this --
        the controller already sees them because it holds the same
        reference.
        """
        if self._controller is None:
            return
        live_cal = self.main_window.session.calibration
        if live_cal is None:
            return
        # Reference comparison: if the session's calibration object
        # is a different instance, the user must have triggered
        # something like a Re-run that replaced it wholesale.
        # pylint: disable=protected-access
        if getattr(self._controller, "_cal", None) is not live_cal:
            self._canvas.set_calibration(live_cal)
            self._controller._cal = live_cal  # noqa: SLF001
            self._controller.undo_stack().clear()

    # ------------------------------------------------------------------
    # Toolbar
    # ------------------------------------------------------------------

    def _build_toolbar(self) -> QtWidgets.QToolBar:
        tb = QtWidgets.QToolBar("Calibrate tools")
        tb.setIconSize(QtCore.QSize(16, 16))
        tb.setToolButtonStyle(QtCore.Qt.ToolButtonStyle.ToolButtonTextOnly)
        return tb

    def _wire_toolbar_actions(self) -> None:
        """Populate the toolbar once the controller / canvas exist."""
        assert self._toolbar is not None
        assert self._controller is not None
        assert self._canvas is not None

        undo_stack = self._controller.undo_stack()

        act_undo = undo_stack.createUndoAction(self, "Undo")
        act_undo.setShortcuts(QtGui.QKeySequence.StandardKey.Undo)
        self._toolbar.addAction(act_undo)

        act_redo = undo_stack.createRedoAction(self, "Redo")
        act_redo.setShortcuts(QtGui.QKeySequence.StandardKey.Redo)
        self._toolbar.addAction(act_redo)

        self._toolbar.addSeparator()

        act_fit = QtGui.QAction("Fit", self)
        act_fit.setShortcut(QtGui.QKeySequence("0"))
        act_fit.triggered.connect(self._canvas.fit_in_view)
        self._toolbar.addAction(act_fit)

        self._toolbar.addSeparator()

        # View toggles. We mirror the standalone editor's L/R/O/W
        # shortcuts so muscle memory transfers; setCheckable makes the
        # button stick "down" while the layer is shown.
        act_labels = QtGui.QAction("Labels", self)
        act_labels.setShortcut(QtGui.QKeySequence("L"))
        act_labels.setCheckable(True)
        act_labels.setChecked(self._canvas.labels_visible())
        act_labels.toggled.connect(self._canvas.set_labels_visible)
        self._toolbar.addAction(act_labels)

        act_rooms = QtGui.QAction("Rooms", self)
        act_rooms.setShortcut(QtGui.QKeySequence("R"))
        act_rooms.setCheckable(True)
        act_rooms.setChecked(self._canvas.rooms_visible())
        act_rooms.toggled.connect(self._canvas.set_rooms_visible)
        self._toolbar.addAction(act_rooms)

        act_wall_patches = QtGui.QAction("Wall patches", self)
        act_wall_patches.setShortcut(QtGui.QKeySequence("W"))
        act_wall_patches.setCheckable(True)
        act_wall_patches.setChecked(self._canvas.wall_patches_visible())
        act_wall_patches.toggled.connect(self._canvas.set_wall_patches_visible)
        self._toolbar.addAction(act_wall_patches)

        act_orphans = QtGui.QAction("Orphans only", self)
        act_orphans.setShortcut(QtGui.QKeySequence("O"))
        act_orphans.setCheckable(True)
        act_orphans.setChecked(self._canvas.orphans_only())
        act_orphans.toggled.connect(self._canvas.set_orphans_only)
        self._toolbar.addAction(act_orphans)

        self._toolbar.addSeparator()

        # Add-tool actions. These mirror the standalone editor's
        # menu entries; the shortcut keys are the same so muscle memory
        # transfers. We route the toggles through the same handlers
        # the standalone editor uses (canvas.set_add_*_mode).
        act_add_label = QtGui.QAction("Add label", self)
        act_add_label.setShortcut(QtGui.QKeySequence("N"))
        act_add_label.setCheckable(True)
        act_add_label.toggled.connect(self._canvas.set_add_label_mode)
        self._toolbar.addAction(act_add_label)
        # Sync the toggle when the canvas drops out of add mode
        # spontaneously (Esc / click / etc.).
        self._canvas.add_label_cancelled.connect(
            lambda *_a: _sync_toggle_off(act_add_label)
        )
        self._canvas.add_label_requested.connect(
            lambda *_a: _sync_toggle_off(act_add_label)
        )

        act_add_wall_patch = QtGui.QAction("Add wall patch", self)
        act_add_wall_patch.setShortcut(QtGui.QKeySequence("Shift+W"))
        act_add_wall_patch.setCheckable(True)
        act_add_wall_patch.toggled.connect(
            self._controller.set_add_wall_patch_mode
        )
        self._toolbar.addAction(act_add_wall_patch)
        self._canvas.add_wall_patch_cancelled.connect(
            lambda *_a: _sync_toggle_off(act_add_wall_patch)
        )
        self._canvas.add_wall_patch_requested.connect(
            lambda *_a: _sync_toggle_off(act_add_wall_patch)
        )

        self._toolbar.addSeparator()

        act_delete = QtGui.QAction("Delete selected", self)
        act_delete.setShortcuts(
            [
                QtGui.QKeySequence(QtCore.Qt.Key.Key_Delete),
                QtGui.QKeySequence(QtCore.Qt.Key.Key_Backspace),
            ]
        )
        act_delete.triggered.connect(self._on_delete_selected)
        self._toolbar.addAction(act_delete)

        # Push everything to the left; spacer pins Re-validate +
        # Re-run to the right.
        spacer = QtWidgets.QWidget(self._toolbar)
        spacer.setSizePolicy(
            QtWidgets.QSizePolicy.Policy.Expanding,
            QtWidgets.QSizePolicy.Policy.Preferred,
        )
        self._toolbar.addWidget(spacer)

        # Re-validate runs the FULL issue check (including the
        # expensive label-in-room mask test that we skip during live
        # edits for responsiveness). User-triggered so the multi-
        # second hang is expected, not surprising.
        act_revalidate = QtGui.QAction("Re-validate", self)
        act_revalidate.setToolTip(
            "Re-run all calibration checks against the current "
            "edits, including the label-position check that's "
            "skipped during live editing for performance. May take "
            "a few seconds on large maps."
        )
        act_revalidate.triggered.connect(self._on_revalidate_clicked)
        self._toolbar.addAction(act_revalidate)

        act_rerun = QtGui.QAction("Re-run calibration", self)
        act_rerun.setToolTip(
            "Discard all edits and re-run OCR + room detection from "
            "the original map image. Cannot be undone."
        )
        act_rerun.triggered.connect(self._on_rerun_clicked)
        self._toolbar.addAction(act_rerun)

    def _on_delete_selected(self) -> None:
        """Try to delete the selected label, wall patch, or room (in that order)."""
        if self._controller is None:
            return
        if self._controller.delete_selected_label():
            return
        if self._controller.delete_selected_wall_patch():
            return
        self._controller.delete_selected_room()

    def _on_rerun_clicked(self) -> None:
        """Wipe the cached calibration and re-fire the pipeline call."""
        answer = QtWidgets.QMessageBox.question(
            self,
            "Re-run calibration?",
            "Re-running will discard all edits (labels added, ids "
            "corrected, wall patches, etc.) and start over from the "
            "original map image. This cannot be undone.\n\n"
            "Continue?",
            QtWidgets.QMessageBox.StandardButton.Yes
            | QtWidgets.QMessageBox.StandardButton.No,
            QtWidgets.QMessageBox.StandardButton.No,
        )
        if answer != QtWidgets.QMessageBox.StandardButton.Yes:
            return

        # Drop the cached calibration and downstream artifacts; the
        # session's invalidate_from cascades the reset through layout
        # / build / tile.
        self.main_window.session.calibration = None
        self.main_window.session.invalidate_from("validate_labels")
        # Drop the editor pane so the next activation rebuilds from
        # the fresh result. (We leave _auto_run_attempted set so we
        # don't double-fire; the explicit Run handler below covers
        # the re-run.)
        self._kick_off_calibration()
        self._stack.setCurrentWidget(self._landing)

    def _on_revalidate_clicked(self) -> None:
        """Run the FULL revalidation (including the slow mask check).

        Live editing uses ``quick=True`` to skip the per-room mask
        decode (multi-second on large maps). This handler runs the
        full check on demand so the user can confirm no label has
        been dragged outside its assigned room.

        Synchronous + wait-cursor: simpler than a background thread,
        and acceptable because the user explicitly asked for it.
        """
        cal = self.main_window.session.calibration
        if cal is None:
            return

        QtWidgets.QApplication.setOverrideCursor(
            QtGui.QCursor(QtCore.Qt.CursorShape.WaitCursor)
        )
        try:
            full_issues = revalidate_calibration(cal, quick=False)
        finally:
            QtWidgets.QApplication.restoreOverrideCursor()

        status, issue_strs = _classify_issues(full_issues)
        self.main_window.set_step_status(
            "calibrate", status, issues=issue_strs
        )

    # ------------------------------------------------------------------
    # Undo-stack mirror to session
    # ------------------------------------------------------------------

    def _on_undo_index_changed(self, _new_index: int) -> None:
        """Persist the session + recompute issues after every edit.

        ``EditorController._cal`` is the same Python object as
        ``session.calibration``, so the in-memory state is already in
        sync -- we just need to flush to disk so a wizard close +
        reopen restores the latest edits.

        We also re-run the lightweight ``revalidate_calibration`` so
        the step's issue count and badge reflect the live state.
        Editing a label id, deleting an orphan label, drawing a new
        room to absorb an orphan -- all of these should update the
        counter immediately.
        """
        # pylint: disable=protected-access
        self.main_window._save_session()  # noqa: SLF001

        cal = self.main_window.session.calibration
        if cal is None:
            return
        live_issues = revalidate_calibration(cal, quick=True)
        status, issue_strs = _classify_issues(live_issues)
        self.main_window.set_step_status(
            "calibrate", status, issues=issue_strs
        )


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _sync_toggle_off(action: QtGui.QAction) -> None:
    """Uncheck ``action`` without re-firing its ``toggled`` slot.

    Used when the canvas spontaneously drops out of an add mode
    (Esc / click-elsewhere) so the toolbar button doesn't get stuck
    in the "down" position.
    """
    if not action.isChecked():
        return
    action.blockSignals(True)
    action.setChecked(False)
    action.blockSignals(False)


def _classify_issues(
    issues: List[CalibrationIssue],
) -> tuple[StepStatus, List[str]]:
    """Decide the step's badge from the calibration issue list.

    Any error → ERROR. Any warning (and no errors) → WARNING. Empty
    list → OK. The returned strings are the human-readable issue
    messages for the issues panel.
    """
    messages = [str(i) for i in issues]
    if any(i.severity == "error" for i in issues):
        return StepStatus.ERROR, messages
    if any(i.severity == "warning" for i in issues):
        return StepStatus.WARNING, messages
    return StepStatus.OK, messages
