"""``QApplication`` bootstrap and the main window for the calibration editor.

This module exposes ``launch(calibration_path, map_path=None) -> int`` which is
called from the ``OfficeMapMaker calibrate edit`` CLI command. The return
value is propagated as the process exit code so failures (e.g. map missing)
surface naturally to the shell.

The main window is intentionally a thin shell at this milestone (ed1) —
it only knows how to display the map. Overlays, side panel, undo stack, etc.
are added in milestones ed2 / ed3 / ed4.
"""

from __future__ import annotations

import sys
from pathlib import Path
from typing import Optional

from PySide6 import QtCore, QtGui, QtWidgets

from ..calibration import (
    Calibration,
    CalibrationFormatError,
    load_calibration,
)
from .canvas import MapCanvas
from .controller import EditorController, save_calibration_with_backup
from .sidebar import InspectorPanel


# Window starts at a reasonable working size but not so big that it
# off-screens on a 1366x768 laptop.
_DEFAULT_WINDOW_SIZE = QtCore.QSize(1280, 800)


class EditorMainWindow(QtWidgets.QMainWindow):
    """Top-level editor window.

    Held weakly by ``launch``; closing it ends the event loop. Holds the
    ``Calibration`` in memory as the editing model and the path it was
    loaded from so save-on-write knows where to go.
    """

    def __init__(
        self,
        *,
        calibration: Calibration,
        calibration_path: Path,
        map_path: Path,
    ) -> None:
        super().__init__()
        self._calibration = calibration
        self._calibration_path = calibration_path
        self._map_path = map_path

        self.setWindowTitle(self._compose_title())
        self.resize(_DEFAULT_WINDOW_SIZE)

        self._canvas = MapCanvas(self)
        self.setCentralWidget(self._canvas)
        self._canvas.set_map_image(map_path)
        self._canvas.set_calibration(calibration)

        # Inspector dock on the right. Built before the controller so the
        # controller can wire selection signals at construction time.
        self._inspector = InspectorPanel(self)
        self._inspector_dock = QtWidgets.QDockWidget("Inspector", self)
        self._inspector_dock.setWidget(self._inspector)
        self._inspector_dock.setAllowedAreas(
            QtCore.Qt.DockWidgetArea.LeftDockWidgetArea
            | QtCore.Qt.DockWidgetArea.RightDockWidgetArea
        )
        # Inspector starts ~320 px wide — enough for the longest field labels
        # without crowding the canvas on a 1280-wide window.
        self._inspector.setMinimumWidth(280)
        self.addDockWidget(
            QtCore.Qt.DockWidgetArea.RightDockWidgetArea, self._inspector_dock
        )

        self._controller = EditorController(
            calibration=calibration,
            calibration_path=calibration_path,
            canvas=self._canvas,
            inspector=self._inspector,
            parent=self,
        )
        self._controller.dirty_changed.connect(self._on_dirty_changed)

        self._build_menus()
        self._build_status_bar()

    # ---------------------------------------------------------------- UI

    def _build_menus(self) -> None:
        """Construct the menu bar.

        File: Save / Save As / Reload / Close.
        Edit: Undo / Redo (provided by the controller's QUndoStack).
        View: zoom + layer toggles.
        Tools / Help are added by later milestones as the corresponding
        features land.
        """
        file_menu = self.menuBar().addMenu("&File")

        act_save = QtGui.QAction("&Save", self)
        act_save.setShortcut(QtGui.QKeySequence.StandardKey.Save)
        act_save.triggered.connect(self._save)
        file_menu.addAction(act_save)

        act_save_as = QtGui.QAction("Save &As…", self)
        act_save_as.setShortcut(QtGui.QKeySequence.StandardKey.SaveAs)
        act_save_as.triggered.connect(self._save_as)
        file_menu.addAction(act_save_as)

        act_reload = QtGui.QAction("&Reload from disk", self)
        # Ctrl+R is the conventional reload shortcut on Windows; tooltip
        # spells it out since QKeySequence doesn't display it the same way
        # on every platform.
        act_reload.setShortcut(QtGui.QKeySequence("Ctrl+R"))
        act_reload.setStatusTip(
            "Reload calibration.json from disk, discarding unsaved edits."
        )
        act_reload.triggered.connect(self._reload)
        file_menu.addAction(act_reload)

        file_menu.addSeparator()

        act_close = QtGui.QAction("&Close", self)
        act_close.setShortcut(QtGui.QKeySequence.StandardKey.Close)
        act_close.triggered.connect(self.close)
        file_menu.addAction(act_close)

        # Edit menu — Undo / Redo come from the controller's QUndoStack so
        # they are auto-enabled/disabled by Qt as the stack state changes.
        edit_menu = self.menuBar().addMenu("&Edit")
        undo_stack = self._controller.undo_stack()
        act_undo = undo_stack.createUndoAction(self, "&Undo")
        act_undo.setShortcuts(QtGui.QKeySequence.StandardKey.Undo)
        act_redo = undo_stack.createRedoAction(self, "&Redo")
        # StandardKey.Redo gives Ctrl+Y on Windows / Shift+Ctrl+Z on Mac/Linux.
        act_redo.setShortcuts(QtGui.QKeySequence.StandardKey.Redo)
        edit_menu.addAction(act_undo)
        edit_menu.addAction(act_redo)

        view_menu = self.menuBar().addMenu("&View")

        act_fit = QtGui.QAction("&Fit to window", self)
        act_fit.setShortcut(QtGui.QKeySequence("0"))
        act_fit.triggered.connect(self._canvas.fit_in_view)
        view_menu.addAction(act_fit)

        act_zoom_in = QtGui.QAction("Zoom &in", self)
        act_zoom_in.setShortcut(QtGui.QKeySequence.StandardKey.ZoomIn)
        act_zoom_in.triggered.connect(lambda: self._canvas.zoom_by(1.25))
        view_menu.addAction(act_zoom_in)

        act_zoom_out = QtGui.QAction("Zoom &out", self)
        act_zoom_out.setShortcut(QtGui.QKeySequence.StandardKey.ZoomOut)
        act_zoom_out.triggered.connect(lambda: self._canvas.zoom_by(1.0 / 1.25))
        view_menu.addAction(act_zoom_out)

        view_menu.addSeparator()

        # Layer toggles. The shortcuts (L, R, O) match what the plan promises.
        # ``setCheckable(True)`` + ``setChecked`` keeps the menu in sync with
        # the canvas if some other code path flips the toggle programmatically.
        self._act_labels = QtGui.QAction("Show &labels", self)
        self._act_labels.setShortcut(QtGui.QKeySequence("L"))
        self._act_labels.setCheckable(True)
        self._act_labels.setChecked(self._canvas.labels_visible())
        self._act_labels.toggled.connect(self._canvas.set_labels_visible)
        view_menu.addAction(self._act_labels)

        self._act_rooms = QtGui.QAction("Show &rooms", self)
        self._act_rooms.setShortcut(QtGui.QKeySequence("R"))
        self._act_rooms.setCheckable(True)
        self._act_rooms.setChecked(self._canvas.rooms_visible())
        self._act_rooms.toggled.connect(self._canvas.set_rooms_visible)
        view_menu.addAction(self._act_rooms)

        self._act_orphans = QtGui.QAction("&Orphans only", self)
        self._act_orphans.setShortcut(QtGui.QKeySequence("O"))
        self._act_orphans.setCheckable(True)
        self._act_orphans.setChecked(self._canvas.orphans_only())
        self._act_orphans.toggled.connect(self._canvas.set_orphans_only)
        view_menu.addAction(self._act_orphans)

        # Tools menu — actions that change the canvas's interaction mode.
        # Add-label is a toggle (canvas stays armed until the user clicks
        # somewhere or presses Esc); delete-label is a one-shot.
        tools_menu = self.menuBar().addMenu("&Tools")

        self._act_add_label = QtGui.QAction("Add &label", self)
        self._act_add_label.setShortcut(QtGui.QKeySequence("N"))
        self._act_add_label.setCheckable(True)
        self._act_add_label.setStatusTip(
            "Arm add-label mode. The next click on the map drops a new label "
            "(prompts for the id). Esc cancels."
        )
        self._act_add_label.toggled.connect(self._on_add_label_toggled)
        tools_menu.addAction(self._act_add_label)

        self._act_delete_label = QtGui.QAction("&Delete selected label", self)
        # Del + Backspace both feel natural for delete; bind both.
        self._act_delete_label.setShortcuts(
            [QtGui.QKeySequence(QtCore.Qt.Key.Key_Delete),
             QtGui.QKeySequence(QtCore.Qt.Key.Key_Backspace)]
        )
        self._act_delete_label.setStatusTip(
            "Delete the currently-selected label. Undoable."
        )
        self._act_delete_label.triggered.connect(self._on_delete_label)
        tools_menu.addAction(self._act_delete_label)

    def _build_status_bar(self) -> None:
        """Status bar shows cursor coords, calibration counts, and dirty state.

        The dirty indicator is a small label that turns into a bullet
        (``● unsaved``) the first time the user makes a change, and clears
        when the file is saved.
        """
        self._cursor_label = QtWidgets.QLabel("")
        self._cursor_label.setMinimumWidth(180)
        self._counts_label = QtWidgets.QLabel(self._compose_counts_text())
        self._dirty_label = QtWidgets.QLabel("")
        self._dirty_label.setStyleSheet("color: #b85c00;")
        self._dirty_label.setMinimumWidth(80)

        bar = self.statusBar()
        bar.addPermanentWidget(self._dirty_label)
        bar.addPermanentWidget(self._counts_label)
        bar.addPermanentWidget(self._cursor_label)

        self._canvas.cursor_scene_pos_changed.connect(self._on_cursor_moved)

        # Show a transient prompt while the user is in "click a room to
        # link" mode so they know what's expected (the crosshair cursor
        # alone is easy to miss).
        self._canvas.room_picked.connect(self._on_pick_mode_ended)
        self._canvas.room_pick_cancelled.connect(self._on_pick_mode_ended)
        self._inspector.room_pick_requested.connect(self._on_pick_mode_requested)

        # Same dance for add-label mode: status-bar prompt while armed,
        # and reset the menu toggle whenever the canvas leaves add mode
        # (so a click-to-place flow doesn't leave the menu stuck "on").
        self._canvas.add_label_requested.connect(self._on_add_label_mode_ended)
        self._canvas.add_label_cancelled.connect(self._on_add_label_mode_ended)

    # -------------------------------------------------------------- slots

    def _on_cursor_moved(self, point: QtCore.QPointF) -> None:
        # Quantize to integer pixels — the map's native units.
        x = int(point.x())
        y = int(point.y())
        self._cursor_label.setText(f"x={x}  y={y}")

    def _on_pick_mode_requested(self, _label_index: int, active: bool) -> None:
        """Status-bar prompt while the user is mid-pick.

        ``showMessage`` with timeout 0 keeps the message until explicitly
        cleared (which we do via ``_on_pick_mode_ended``).
        """
        if active:
            self.statusBar().showMessage(
                "Click a room on the map to link it to this label. "
                "(Esc to cancel)", 0,
            )

    def _on_pick_mode_ended(self, *_args) -> None:
        # Clears any sticky pick-mode prompt; transient messages set
        # elsewhere (e.g. save confirmations) are unaffected because they
        # were posted with their own timeout.
        self.statusBar().clearMessage()

    def _on_add_label_toggled(self, checked: bool) -> None:
        """Drive the canvas's add-label mode from the menu toggle.

        Also posts / clears a sticky status-bar prompt so the user
        understands what the crosshair cursor means.
        """
        self._canvas.set_add_label_mode(checked)
        if checked:
            self.statusBar().showMessage(
                "Click on the map to place a new label. (Esc to cancel)", 0
            )
        else:
            self.statusBar().clearMessage()

    def _on_add_label_mode_ended(self, *_args) -> None:
        """Canvas dropped out of add mode → re-sync the menu toggle.

        Guards against re-entrancy: ``setChecked`` would re-fire ``toggled``
        and bounce back into ``set_add_label_mode``, but the canvas's own
        ``set_add_label_mode`` is already idempotent so the redundant call
        is harmless. We still ``blockSignals`` to keep the status-bar
        message change happening exactly once per transition.
        """
        if not self._act_add_label.isChecked():
            return
        self._act_add_label.blockSignals(True)
        self._act_add_label.setChecked(False)
        self._act_add_label.blockSignals(False)
        self.statusBar().clearMessage()

    def _on_delete_label(self) -> None:
        """Delete the selected label, or flash a hint if nothing's selected."""
        if not self._controller.delete_selected_label():
            self.statusBar().showMessage(
                "Select a label first (click a yellow / green box on the map).",
                2500,
            )

    def _on_dirty_changed(self, dirty: bool) -> None:
        """Update the window title + status-bar marker on stack clean/dirty.

        ``QMainWindow.setWindowModified`` + a ``[*]`` placeholder in the
        title is the Qt-native idiom for "unsaved changes". We use it for
        the title bar, and a redundant explicit string in the status bar
        so the state is visible even when the title is truncated.
        """
        self.setWindowModified(dirty)
        self._dirty_label.setText("● unsaved" if dirty else "")
        # Counts may also be relevant if the user just changed a label's
        # room link (orphan counts shift); cheap to recompute.
        self._counts_label.setText(self._compose_counts_text())

    # ---------------------------------------------------------- file ops

    def _save(self) -> None:
        """Write the calibration to its existing path with a .bak backup."""
        try:
            backup = self._controller.save()
        except OSError as exc:
            QtWidgets.QMessageBox.critical(
                self,
                "Save failed",
                f"Could not write {self._calibration_path.name}:\n\n{exc}",
            )
            return
        msg = f"Saved {self._calibration_path.name}"
        if backup is not None:
            msg += f" (previous version → {backup.name})"
        self.statusBar().showMessage(msg, 4000)

    def _save_as(self) -> None:
        """Save the calibration to a user-chosen path. Updates the bound path."""
        new_path_str, _filter = QtWidgets.QFileDialog.getSaveFileName(
            self,
            "Save calibration as",
            str(self._calibration_path),
            "Calibration JSON (*.json);;All files (*)",
        )
        if not new_path_str:
            return
        new_path = Path(new_path_str)
        try:
            save_calibration_with_backup(self._calibration, new_path)
        except OSError as exc:
            QtWidgets.QMessageBox.critical(
                self,
                "Save failed",
                f"Could not write {new_path.name}:\n\n{exc}",
            )
            return
        # Re-target the controller and the window title to the new path so
        # subsequent saves go to the same place.
        self._calibration_path = new_path
        self._controller._calibration_path = new_path  # noqa: SLF001 — intentional rebinding
        self._controller.undo_stack().setClean()
        self.setWindowTitle(self._compose_title())
        self.statusBar().showMessage(f"Saved as {new_path.name}", 4000)

    def _reload(self) -> None:
        """Reload calibration.json from disk, discarding unsaved edits.

        Refuses to proceed without confirmation if the user has unsaved
        changes — losing edits silently would be a poor experience even
        for an internal tool.
        """
        if self._controller.is_dirty():
            choice = QtWidgets.QMessageBox.question(
                self,
                "Discard unsaved changes?",
                "You have unsaved changes. Reloading will discard them.\n\n"
                "Continue?",
                QtWidgets.QMessageBox.StandardButton.Discard
                | QtWidgets.QMessageBox.StandardButton.Cancel,
                QtWidgets.QMessageBox.StandardButton.Cancel,
            )
            if choice != QtWidgets.QMessageBox.StandardButton.Discard:
                return

        try:
            fresh = load_calibration(self._calibration_path)
        except (OSError, CalibrationFormatError) as exc:
            QtWidgets.QMessageBox.critical(
                self,
                "Reload failed",
                f"Could not load {self._calibration_path.name}:\n\n{exc}",
            )
            return

        # Swap in the new calibration. The simplest path is a full
        # rebuild — incremental refresh is for in-session edits, not
        # arbitrary disk-to-memory diffs.
        self._calibration = fresh
        # Hot-swap the controller's model reference too so subsequent
        # commands target the freshly loaded objects.
        self._controller._cal = fresh  # noqa: SLF001 — intentional rebinding
        self._canvas.set_calibration(fresh)
        self._controller.undo_stack().clear()
        self._controller.undo_stack().setClean()
        self._inspector.show_nothing()
        self._counts_label.setText(self._compose_counts_text())
        self.statusBar().showMessage(
            f"Reloaded {self._calibration_path.name}", 4000
        )

    def closeEvent(self, event: QtGui.QCloseEvent) -> None:  # noqa: N802 — Qt API
        """Prompt to save unsaved changes before the window closes."""
        if not self._controller.is_dirty():
            event.accept()
            return

        choice = QtWidgets.QMessageBox.question(
            self,
            "Unsaved changes",
            "Save changes to "
            f"{self._calibration_path.name} before closing?",
            QtWidgets.QMessageBox.StandardButton.Save
            | QtWidgets.QMessageBox.StandardButton.Discard
            | QtWidgets.QMessageBox.StandardButton.Cancel,
            QtWidgets.QMessageBox.StandardButton.Save,
        )
        if choice == QtWidgets.QMessageBox.StandardButton.Cancel:
            event.ignore()
            return
        if choice == QtWidgets.QMessageBox.StandardButton.Save:
            try:
                self._controller.save()
            except OSError as exc:
                QtWidgets.QMessageBox.critical(
                    self,
                    "Save failed",
                    f"Could not write {self._calibration_path.name}:\n\n{exc}\n\n"
                    "Close cancelled.",
                )
                event.ignore()
                return
        event.accept()

    # ---------------------------------------------------------- helpers

    def _compose_title(self) -> str:
        # The trailing ``[*]`` is a Qt placeholder for "modified marker" —
        # Qt expands it to ``*`` when ``setWindowModified(True)`` is set,
        # nothing otherwise.
        return f"OfficeMapMaker editor — {self._calibration_path.name}[*]"

    def _compose_counts_text(self) -> str:
        cal = self._calibration
        n_labels = len(cal.labels)
        n_rooms = len(cal.rooms)
        n_orphan_labels = sum(1 for lab in cal.labels if lab.room_id is None)
        room_ids_with_labels = {lab.room_id for lab in cal.labels if lab.room_id is not None}
        n_orphan_rooms = sum(1 for room in cal.rooms if room.id not in room_ids_with_labels)
        return (
            f"labels: {n_labels} ({n_orphan_labels} orphan) · "
            f"rooms: {n_rooms} ({n_orphan_rooms} unlabeled)"
        )


# ---------------------------------------------------------------------------
# Public entry point
# ---------------------------------------------------------------------------


def launch(
    calibration_path: Path,
    map_path: Optional[Path] = None,
    *,
    argv: Optional[list[str]] = None,
) -> int:
    """Open the interactive editor and run the event loop until close.

    Args:
        calibration_path: Path to ``calibration.json``. Must already exist
            — the editor edits an existing calibration; bootstrap is the
            CLI ``calibrate`` command's job.
        map_path: Optional override for the source map image. If ``None``,
            resolved from ``calibration.map_image`` relative to the
            calibration's directory (matching the CLI ``review`` flow).
        argv: ``sys.argv`` substitute for tests. ``QApplication`` requires
            *some* argv; default to ``sys.argv`` so command-line Qt flags
            (``-style``, ``-platform``, etc.) work as expected.

    Returns:
        0 on clean exit, 2 on missing input, 3 if the Qt event loop itself
        returns a non-zero code (rare; usually means the platform plugin
        failed to load).
    """
    if not calibration_path.exists():
        print(
            f"error: calibration not found at {calibration_path}; "
            f"run 'officemapmaker calibrate --map MAP.png --out {calibration_path}' first",
            file=sys.stderr,
        )
        return 2

    try:
        cal = load_calibration(calibration_path)
    except CalibrationFormatError as exc:
        print(f"error: could not load calibration: {exc}", file=sys.stderr)
        return 2

    if map_path is None:
        map_path = (calibration_path.parent / cal.map_image).resolve()
    if not map_path.exists():
        print(
            f"error: source map not found at {map_path}.\n"
            f"Pass --map to point at the map explicitly, or move the map "
            f"next to {calibration_path.name} so the recorded 'map_image' "
            f"({cal.map_image!r}) resolves.",
            file=sys.stderr,
        )
        return 2

    # Reuse an existing QApplication if one is already running (the case
    # under pytest-qt). Otherwise construct one for this process.
    app = QtWidgets.QApplication.instance()
    owned_app = app is None
    if app is None:
        app = QtWidgets.QApplication(argv if argv is not None else sys.argv)

    window = EditorMainWindow(
        calibration=cal,
        calibration_path=calibration_path,
        map_path=map_path,
    )
    window.show()

    if not owned_app:
        # Caller (test) owns the event loop; just return the window so it
        # can run app.processEvents() and inspect state.
        return 0

    rc = app.exec()
    return 0 if rc == 0 else 3


__all__ = ["EditorMainWindow", "launch"]
