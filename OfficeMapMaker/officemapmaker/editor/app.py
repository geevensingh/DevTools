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

        self._build_menus()
        self._build_status_bar()

    # ---------------------------------------------------------------- UI

    def _build_menus(self) -> None:
        """Construct the menu bar.

        Only File and View exist in ed1-ed2. Edit/Tools/Help are added by
        later milestones as the corresponding features land.
        """
        file_menu = self.menuBar().addMenu("&File")

        act_close = QtGui.QAction("&Close", self)
        act_close.setShortcut(QtGui.QKeySequence.StandardKey.Close)
        act_close.triggered.connect(self.close)
        file_menu.addAction(act_close)

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

    def _build_status_bar(self) -> None:
        """Status bar shows live cursor coords and calibration counts.

        At this milestone only the cursor coords and the counts are wired up;
        later milestones add the dirty indicator and the active-tool label.
        """
        self._cursor_label = QtWidgets.QLabel("")
        self._cursor_label.setMinimumWidth(180)
        self._counts_label = QtWidgets.QLabel(self._compose_counts_text())

        bar = self.statusBar()
        bar.addPermanentWidget(self._counts_label)
        bar.addPermanentWidget(self._cursor_label)

        self._canvas.cursor_scene_pos_changed.connect(self._on_cursor_moved)

    # -------------------------------------------------------------- slots

    def _on_cursor_moved(self, point: QtCore.QPointF) -> None:
        # Quantize to integer pixels — the map's native units.
        x = int(point.x())
        y = int(point.y())
        self._cursor_label.setText(f"x={x}  y={y}")

    # ---------------------------------------------------------- helpers

    def _compose_title(self) -> str:
        return f"OfficeMapMaker editor — {self._calibration_path.name}"

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
