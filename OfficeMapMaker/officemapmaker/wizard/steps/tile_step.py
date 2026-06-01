"""Step 6 (terminal) -- Tile + PDF.

This step takes the ``composite.png`` from Step 5 and chops it into
letter-size print tiles (plus a contact sheet + a multi-page PDF) via
``officemapmaker.tile.tile_composite``. Inputs are paper size, DPI, and
inter-tile overlap; outputs land in ``<output_dir>/tiles/``.

Issues come from ``officemapmaker.tile`` (``TileIssue`` dataclass) and
include ``meta_sidecar_missing`` (Pass 4 metadata not found -- legend
page will be sparse), ``coverage_gap`` (a stripe of the composite is
not covered by any tile), ``min_font_warning`` (chosen DPI shrinks
existing text below the readable threshold), ``pdf_page_count_mismatch``
(bundled PDF is missing pages), and ``sha_spot_check_failed`` (a tile
PNG no longer matches the composite slice it claims to be -- usually
file-corruption or a wrong-tile bug). None of these are hard errors --
the tile output is still usable -- so the step never returns
``StepStatus.ERROR`` unless the adapter raises.

Like ``BuildStep``, the UI is a ``QStackedWidget`` with three panes:

* **No-composite** -- Step 5 hasn't finished (or composite.png was
  deleted from disk). Button sends them back to the Build step.
* **Landing** -- composite exists but no tile run cached. Paper size /
  DPI / overlap controls + big "Build tiles + PDF" button.
* **Results** -- splitter with the contact-sheet preview on the left
  and a tile-thumbnail icon view + issues table on the right. Header
  row keeps the paper/DPI/overlap controls accessible so the user can
  tweak and re-run without leaving the pane. "Open output folder"
  reveals ``tiles/`` in the file manager. "Done" closes the wizard
  (this is the terminal step).

The expensive ``tile_composite`` call runs on a worker thread via
``run_pipeline_step``. The wizard's Next button is naturally disabled
on the last step, so users finish by clicking Done or just closing
the window.
"""

from __future__ import annotations

import subprocess
import sys
from pathlib import Path
from typing import List, Optional, Tuple

from PySide6 import QtCore, QtGui, QtWidgets

from ...tile import (
    ORIENTATIONS,
    PAPER_SIZES_IN,
    TileIssue,
    TileResult,
    compute_fit_to_one_page_percent,
    tile_composite,
)
from ..main_window import StepStatus
from ._preview_view import PreviewGraphicsView
from .base import StepBase


# ---------------------------------------------------------------------------
# Pipeline adapter
# ---------------------------------------------------------------------------


def _run_tile_composite(
    composite_path: Path,
    out_dir: Path,
    *,
    dpi: int,
    paper: str,
    overlap_in: float,
    orientation: str,
    scale_percent: float,
    progress_cb,
    cancel_cb,
) -> Tuple[Tuple[TileResult], List[TileIssue]]:
    """Worker-thread entry point for Step 6.

    ``tile_composite`` is fast enough on letter-size paper (a 4000x4000
    composite at 150 DPI is ~9 tiles, sub-second) that we don't bother
    threading per-tile progress. We just publish a small set of phase
    messages so the footer progress bar moves.

    Returns ``((tile_result,), issues)``. The single-element tuple
    keeps the on-finished handler signature consistent with the other
    steps (which return ``(result, extra)``) even though Tile has no
    extra payload.
    """
    progress_cb(0.0, "Computing tile grid...")
    if cancel_cb():
        from ...pipeline import PipelineCanceled

        raise PipelineCanceled()

    progress_cb(0.1, "Rendering tiles...")
    result = tile_composite(
        composite_path,
        out_dir=out_dir,
        dpi=dpi,
        paper=paper,
        overlap_in=overlap_in,
        orientation=orientation,
        scale_percent=scale_percent,
    )
    if cancel_cb():
        from ...pipeline import PipelineCanceled

        raise PipelineCanceled()

    progress_cb(1.0, "Done")
    return (result,), list(result.issues)


# ---------------------------------------------------------------------------
# Issue classification + ignore-key helpers
# ---------------------------------------------------------------------------


def _classify_issues(
    issues: List[TileIssue],
) -> Tuple[StepStatus, List[str], List[str], List[str]]:
    """Map a list of ``TileIssue`` to (step status, messages, codes,
    severities)."""
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
        [f"[{i.code}] {i.message}" for i in issues],
        [i.code for i in issues],
        [i.severity for i in issues],
    )


def _issue_key(issue: TileIssue) -> str:
    """Stable key for the in-memory ignored set."""
    return f"{issue.code}|{issue.message}"


# ---------------------------------------------------------------------------
# OS-native "show file in explorer"
# ---------------------------------------------------------------------------


def _reveal_in_explorer(path: Path) -> None:
    """Open the OS file manager with ``path`` selected.

    Windows: ``explorer /select,<path>``. macOS/Linux fall back to
    opening the parent folder (no portable "select this file" API).
    Best-effort: never raises if the launcher fails.
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
        pass


# ---------------------------------------------------------------------------
# Widget
# ---------------------------------------------------------------------------


class TileStep(StepBase):
    """Wizard pane for the Tile + PDF (terminal) step."""

    STEP_ID = "tile"

    # Thumbnail size (px) for the right-side tile-grid list.
    _THUMB_W = 140
    _THUMB_H = 180

    def __init__(self, main_window) -> None:
        super().__init__(main_window)

        # In-memory ignore set keyed by ``_issue_key``. Resets on Re-build.
        self._ignored: set[str] = set()
        # Cached results from the last run (or None before first run).
        self._last_issues: Optional[List[TileIssue]] = None
        self._tile_result: Optional[TileResult] = None
        # Cached composite path checked in on_activated -- if the build
        # step's composite has been re-rendered we should re-detect it.
        self._composite_path: Optional[Path] = None

        outer = QtWidgets.QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)

        self._stack = QtWidgets.QStackedWidget(self)
        outer.addWidget(self._stack)

        self._no_composite_pane = self._build_no_composite_pane()
        self._landing_pane = self._build_landing_pane()
        self._results_pane = self._build_results_pane()

        self._stack.addWidget(self._no_composite_pane)
        self._stack.addWidget(self._landing_pane)
        self._stack.addWidget(self._results_pane)

    # ------------------------------------------------------------------
    # Pane builders
    # ------------------------------------------------------------------

    def _build_no_composite_pane(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(40, 40, 40, 40)
        layout.setSpacing(16)

        title = QtWidgets.QLabel("Step 5 (Build composite) needs to finish first")
        f = title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        title.setFont(f)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(
            "This step splits the rendered composite into letter-size "
            "print tiles plus a PDF that bundles them. We don't have a "
            "composite yet -- go back to Step 5, build it, then come "
            "back here."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        btn = QtWidgets.QPushButton("Back to Step 5: Build composite")
        btn.setMaximumWidth(280)
        btn.setMinimumHeight(36)
        btn.clicked.connect(self._on_back_to_build_clicked)
        layout.addWidget(btn)

        layout.addStretch(1)
        return widget

    def _make_controls_row(
        self, *, parent: QtWidgets.QWidget
    ) -> Tuple[QtWidgets.QWidget, QtWidgets.QComboBox,
               QtWidgets.QComboBox, QtWidgets.QSpinBox,
               QtWidgets.QDoubleSpinBox, QtWidgets.QDoubleSpinBox,
               QtWidgets.QPushButton]:
        """Build a row with paper / orientation / DPI / overlap / scale controls.

        Returned so each pane can have its own row but share defaults.
        The landing-pane row and the results-pane header row both call
        this; we keep them in sync via :meth:`_sync_controls`.
        """
        row_widget = QtWidgets.QWidget(parent)
        row = QtWidgets.QHBoxLayout(row_widget)
        row.setContentsMargins(0, 0, 0, 0)
        row.setSpacing(8)

        row.addWidget(QtWidgets.QLabel("Paper:"))
        paper = QtWidgets.QComboBox()
        for key in sorted(PAPER_SIZES_IN):
            paper.addItem(key)
        paper.setCurrentText("letter")
        paper.setToolTip("Page size for the printed tiles + bundled PDF.")
        row.addWidget(paper)

        row.addSpacing(8)
        row.addWidget(QtWidgets.QLabel("Orientation:"))
        orientation = QtWidgets.QComboBox()
        for key in ORIENTATIONS:
            orientation.addItem(key)
        orientation.setCurrentText("auto")
        orientation.setToolTip(
            "Portrait or landscape page layout. 'Auto' picks whichever "
            "produces fewer total tiles (tiebreak: portrait)."
        )
        row.addWidget(orientation)

        row.addSpacing(8)
        row.addWidget(QtWidgets.QLabel("DPI:"))
        dpi = QtWidgets.QSpinBox()
        dpi.setRange(72, 600)
        dpi.setSingleStep(25)
        dpi.setValue(150)
        dpi.setToolTip(
            "Print resolution. Higher = sharper text but more pages "
            "(the composite gets sliced into more letter pages)."
        )
        row.addWidget(dpi)

        row.addSpacing(8)
        row.addWidget(QtWidgets.QLabel("Overlap (in):"))
        overlap = QtWidgets.QDoubleSpinBox()
        overlap.setRange(0.0, 1.0)
        overlap.setSingleStep(0.05)
        overlap.setDecimals(2)
        overlap.setValue(0.25)
        overlap.setToolTip(
            "Inches of overlap between adjacent printed pages. The "
            "overlap band is where you tape pages together."
        )
        row.addWidget(overlap)

        row.addSpacing(8)
        row.addWidget(QtWidgets.QLabel("Scale (%):"))
        scale = QtWidgets.QDoubleSpinBox()
        scale.setRange(1.0, 1000.0)
        scale.setSingleStep(5.0)
        scale.setDecimals(1)
        scale.setValue(100.0)
        scale.setToolTip(
            "Resize the composite before tiling. 100% = unchanged; 50% "
            "halves the printed size (and the tile count); 200% doubles "
            "it (more pages, larger text). Use the 'Fit to 1 page' "
            "button to compute the scale that fits the whole map on a "
            "single sheet."
        )
        row.addWidget(scale)

        fit_btn = QtWidgets.QPushButton("Fit to 1 page")
        fit_btn.setToolTip(
            "Compute the scale that makes the entire composite fit "
            "on a single page at the current paper size + orientation, "
            "and write that value into the Scale field."
        )
        row.addWidget(fit_btn)

        return row_widget, paper, orientation, dpi, overlap, scale, fit_btn

    def _build_landing_pane(self) -> QtWidgets.QWidget:
        widget = QtWidgets.QWidget()
        layout = QtWidgets.QVBoxLayout(widget)
        layout.setContentsMargins(40, 40, 40, 40)
        layout.setSpacing(16)

        title = QtWidgets.QLabel("Tile + PDF")
        f = title.font()
        f.setPointSize(f.pointSize() + 4)
        f.setBold(True)
        title.setFont(f)
        layout.addWidget(title)

        desc = QtWidgets.QLabel(
            "This splits <i>composite.png</i> into letter-size pages you "
            "can print and stitch together, plus a bundled "
            "<i>all.pdf</i> and a 4-up <i>contact_sheet.png</i> for "
            "review. Outputs land in <i>tiles\\</i> under the output "
            "folder."
        )
        desc.setWordWrap(True)
        desc.setStyleSheet("QLabel { color: #555; }")
        layout.addWidget(desc)

        controls, self._landing_paper, self._landing_orientation, \
            self._landing_dpi, self._landing_overlap, \
            self._landing_scale, self._landing_fit_btn = \
            self._make_controls_row(parent=widget)
        self._landing_fit_btn.clicked.connect(
            lambda: self._on_fit_to_one_page_clicked(landing=True)
        )
        layout.addWidget(controls)

        self._run_button = QtWidgets.QPushButton("Build tiles + PDF")
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

        # Header row: summary on the left, controls + buttons on the right.
        header_row = QtWidgets.QHBoxLayout()
        self._summary_label = QtWidgets.QLabel("")
        f = self._summary_label.font()
        f.setBold(True)
        self._summary_label.setFont(f)
        header_row.addWidget(self._summary_label)
        header_row.addStretch(1)

        controls, self._results_paper, self._results_orientation, \
            self._results_dpi, self._results_overlap, \
            self._results_scale, self._results_fit_btn = \
            self._make_controls_row(parent=widget)
        self._results_fit_btn.clicked.connect(
            lambda: self._on_fit_to_one_page_clicked(landing=False)
        )
        header_row.addWidget(controls)

        self._fit_button = QtWidgets.QPushButton("Fit preview")
        self._fit_button.setToolTip(
            "Reset zoom so the whole contact sheet fits in the view"
        )
        self._fit_button.clicked.connect(self._on_fit_clicked)
        header_row.addWidget(self._fit_button)

        self._open_button = QtWidgets.QPushButton("Open output folder")
        self._open_button.setToolTip("Reveal tiles\\ in the file manager")
        self._open_button.clicked.connect(self._on_open_folder_clicked)
        self._open_button.setEnabled(False)
        header_row.addWidget(self._open_button)

        self._rerun_button = QtWidgets.QPushButton("Re-build tiles")
        self._rerun_button.clicked.connect(self._on_rerun_clicked)
        header_row.addWidget(self._rerun_button)

        self._done_button = QtWidgets.QPushButton("Done")
        self._done_button.setToolTip("Close OfficeMapMaker")
        self._done_button.setStyleSheet(
            "QPushButton { font-weight: bold; padding: 4px 16px; }"
        )
        self._done_button.clicked.connect(self._on_done_clicked)
        header_row.addWidget(self._done_button)
        layout.addLayout(header_row)

        # Splitter: contact-sheet preview on the left, thumbnails +
        # issues table on the right.
        splitter = QtWidgets.QSplitter(QtCore.Qt.Orientation.Horizontal, widget)
        layout.addWidget(splitter, 1)

        # --- Contact sheet preview ---
        preview_container = QtWidgets.QWidget(splitter)
        pv_layout = QtWidgets.QVBoxLayout(preview_container)
        pv_layout.setContentsMargins(0, 0, 0, 0)
        pv_layout.setSpacing(4)
        pv_layout.addWidget(QtWidgets.QLabel("Contact sheet"))
        self._preview_view = PreviewGraphicsView(preview_container)
        pv_layout.addWidget(self._preview_view, 1)
        self._no_preview_label = QtWidgets.QLabel(
            "No contact sheet -- the tile build finished but the "
            "PNG could not be loaded. Try Re-build tiles."
        )
        self._no_preview_label.setWordWrap(True)
        self._no_preview_label.setAlignment(QtCore.Qt.AlignmentFlag.AlignCenter)
        self._no_preview_label.setStyleSheet(
            "QLabel { color: #777; padding: 24px; }"
        )
        self._no_preview_label.setVisible(False)
        pv_layout.addWidget(self._no_preview_label)
        splitter.addWidget(preview_container)

        # --- Right side: thumbnails on top, issues on bottom ---
        right_container = QtWidgets.QWidget(splitter)
        right_layout = QtWidgets.QVBoxLayout(right_container)
        right_layout.setContentsMargins(0, 0, 0, 0)
        right_layout.setSpacing(6)

        right_layout.addWidget(QtWidgets.QLabel("Tile pages"))
        self._tile_list = QtWidgets.QListWidget(right_container)
        self._tile_list.setViewMode(QtWidgets.QListWidget.ViewMode.IconMode)
        self._tile_list.setIconSize(QtCore.QSize(self._THUMB_W, self._THUMB_H))
        self._tile_list.setResizeMode(
            QtWidgets.QListWidget.ResizeMode.Adjust
        )
        self._tile_list.setMovement(QtWidgets.QListWidget.Movement.Static)
        self._tile_list.setSpacing(8)
        self._tile_list.setUniformItemSizes(True)
        self._tile_list.itemActivated.connect(self._on_tile_activated)
        right_layout.addWidget(self._tile_list, 1)

        right_layout.addWidget(QtWidgets.QLabel("Issues"))
        self._empty_label = QtWidgets.QLabel(
            "Tiles built cleanly. Click Open output folder to find them, "
            "or Done to close the wizard."
        )
        self._empty_label.setStyleSheet(
            "QLabel { color: #2e7d32; padding: 12px; "
            "background-color: #e8f5e9; border-radius: 4px; }"
        )
        self._empty_label.setWordWrap(True)
        self._empty_label.setVisible(False)
        right_layout.addWidget(self._empty_label)

        self._table = QtWidgets.QTableWidget(0, 4, right_container)
        self._table.setHorizontalHeaderLabels(
            ["Severity", "Code", "Issue", "Actions"]
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
        header.setSectionResizeMode(2, QtWidgets.QHeaderView.ResizeMode.Stretch)
        header.setSectionResizeMode(
            3, QtWidgets.QHeaderView.ResizeMode.ResizeToContents
        )
        right_layout.addWidget(self._table)

        self._footer_label = QtWidgets.QLabel("")
        self._footer_label.setStyleSheet("QLabel { color: #777; }")
        right_layout.addWidget(self._footer_label)

        splitter.addWidget(right_container)
        # Default 55/45 split: preview gets a bit more space.
        splitter.setStretchFactor(0, 11)
        splitter.setStretchFactor(1, 9)
        splitter.setSizes([550, 450])

        return widget

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def on_activated(self) -> None:
        composite_path = self._lookup_composite_path()

        if composite_path is None or not composite_path.exists():
            self._stack.setCurrentWidget(self._no_composite_pane)
            return

        # If the composite path changed since last activation (build
        # step re-rendered to a different path), drop cached results.
        if (
            self._composite_path is not None
            and self._composite_path != composite_path
        ):
            self._last_issues = None
            self._tile_result = None
        self._composite_path = composite_path

        if self._last_issues is None and self._tile_result is None:
            self._stack.setCurrentWidget(self._landing_pane)
            return

        self._stack.setCurrentWidget(self._results_pane)

    def _lookup_composite_path(self) -> Optional[Path]:
        """Find the composite produced by Step 5.

        Prefers the path remembered on the build step (so we see
        re-renders immediately); falls back to the well-known
        ``<output_dir>/composite.png`` location.
        """
        build_step = next(
            (e.widget for e in self.main_window._steps
             if e.step_id == "build"),
            None,
        )
        cached = getattr(build_step, "_composite_path", None)
        if cached is not None:
            return cached
        fallback = Path(self.main_window.output_dir) / "composite.png"
        return fallback if fallback.exists() else None

    # ------------------------------------------------------------------
    # Run / re-run
    # ------------------------------------------------------------------

    def _on_run_clicked(self) -> None:
        self._sync_controls_from_landing_to_results()
        self._kick_off_tile()

    def _on_rerun_clicked(self) -> None:
        self._sync_controls_from_results_to_landing()
        self._ignored.clear()
        self._last_issues = None
        self._tile_result = None
        self._kick_off_tile()

    def _sync_controls_from_landing_to_results(self) -> None:
        self._results_paper.setCurrentText(self._landing_paper.currentText())
        self._results_orientation.setCurrentText(
            self._landing_orientation.currentText()
        )
        self._results_dpi.setValue(self._landing_dpi.value())
        self._results_overlap.setValue(self._landing_overlap.value())
        self._results_scale.setValue(self._landing_scale.value())

    def _sync_controls_from_results_to_landing(self) -> None:
        self._landing_paper.setCurrentText(self._results_paper.currentText())
        self._landing_orientation.setCurrentText(
            self._results_orientation.currentText()
        )
        self._landing_dpi.setValue(self._results_dpi.value())
        self._landing_overlap.setValue(self._results_overlap.value())
        self._landing_scale.setValue(self._results_scale.value())

    def _current_controls(self) -> Tuple[str, str, int, float, float]:
        """Whichever pane is currently visible owns the live values.

        Returns (paper, orientation, dpi, overlap_in, scale_percent).
        """
        if self._stack.currentWidget() is self._results_pane:
            return (
                self._results_paper.currentText(),
                self._results_orientation.currentText(),
                self._results_dpi.value(),
                self._results_overlap.value(),
                self._results_scale.value(),
            )
        return (
            self._landing_paper.currentText(),
            self._landing_orientation.currentText(),
            self._landing_dpi.value(),
            self._landing_overlap.value(),
            self._landing_scale.value(),
        )

    def _on_fit_to_one_page_clicked(self, *, landing: bool) -> None:
        """Compute the scale that fits the composite on one page.

        Reads the composite size via PIL (only the header is parsed,
        not the pixel data) and writes the result into whichever pane's
        Scale field initiated the click.
        """
        composite_path = self._lookup_composite_path()
        if composite_path is None or not composite_path.exists():
            QtWidgets.QMessageBox.warning(
                self, "Composite missing",
                "Cannot compute scale without a composite. Run Step 5 "
                "(Build composite) first."
            )
            return

        try:
            from PIL import Image
            with Image.open(composite_path) as img:
                comp_size = img.size  # (w, h); only the header is parsed
        except (OSError, ValueError) as exc:
            QtWidgets.QMessageBox.warning(
                self, "Composite unreadable",
                f"Could not read {composite_path.name}: {exc}"
            )
            return

        if landing:
            paper = self._landing_paper.currentText()
            orientation = self._landing_orientation.currentText()
            dpi = self._landing_dpi.value()
            scale_field = self._landing_scale
        else:
            paper = self._results_paper.currentText()
            orientation = self._results_orientation.currentText()
            dpi = self._results_dpi.value()
            scale_field = self._results_scale

        try:
            percent = compute_fit_to_one_page_percent(
                comp_size, dpi=dpi, paper=paper, orientation=orientation,
            )
        except ValueError as exc:
            QtWidgets.QMessageBox.warning(
                self, "Cannot compute fit", str(exc)
            )
            return

        # Clamp to the spinbox range so we don't silently truncate.
        clamped = max(scale_field.minimum(), min(scale_field.maximum(), percent))
        scale_field.setValue(clamped)

    def _kick_off_tile(self) -> None:
        self._run_button.setEnabled(False)
        self._rerun_button.setEnabled(False)

        composite_path = self._lookup_composite_path()
        if composite_path is None or not composite_path.exists():
            self._stack.setCurrentWidget(self._no_composite_pane)
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)
            return
        self._composite_path = composite_path

        out_dir = Path(self.main_window.output_dir) / "tiles"
        paper, orientation, dpi, overlap, scale = self._current_controls()

        runner = self.main_window.run_pipeline_step(
            self.STEP_ID,
            _run_tile_composite,
            args=(composite_path, out_dir),
            kwargs={
                "dpi": dpi,
                "paper": paper,
                "overlap_in": overlap,
                "orientation": orientation,
                "scale_percent": scale,
            },
            on_finished=self._on_tile_finished,
            on_failed=self._on_tile_failed,
            on_canceled=self._on_tile_canceled,
        )
        if runner is None:
            self._run_button.setEnabled(True)
            self._rerun_button.setEnabled(True)

    def _on_tile_finished(self, result, issues: list) -> None:
        # Adapter returns ((tile_result,), issues).
        (tile_result,) = result

        self._tile_result = tile_result
        self._last_issues = list(issues)
        self._mount_preview()
        self._populate_thumbnails()
        self._populate_table()
        self._open_button.setEnabled(True)
        self._stack.setCurrentWidget(self._results_pane)
        self._refresh_status()
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_tile_failed(self, exc: BaseException) -> None:
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    def _on_tile_canceled(self) -> None:
        self._run_button.setEnabled(True)
        self._rerun_button.setEnabled(True)

    # ------------------------------------------------------------------
    # Preview + thumbnails
    # ------------------------------------------------------------------

    def _mount_preview(self) -> None:
        """Load the contact sheet into the preview view."""
        if self._tile_result is None:
            return
        path = self._tile_result.contact_sheet_path
        if not path.exists():
            self._preview_view.clear_pixmap()
            self._preview_view.setVisible(False)
            self._no_preview_label.setVisible(True)
            return
        pixmap = QtGui.QPixmap(str(path))
        if pixmap.isNull():
            self._preview_view.clear_pixmap()
            self._preview_view.setVisible(False)
            self._no_preview_label.setVisible(True)
            return
        self._no_preview_label.setVisible(False)
        self._preview_view.setVisible(True)
        self._preview_view.set_pixmap(pixmap)

    def _populate_thumbnails(self) -> None:
        self._tile_list.clear()
        if self._tile_result is None:
            return
        for tile_path in self._tile_result.tile_paths:
            pixmap = QtGui.QPixmap(str(tile_path))
            if pixmap.isNull():
                icon = QtGui.QIcon()
            else:
                scaled = pixmap.scaled(
                    self._THUMB_W,
                    self._THUMB_H,
                    QtCore.Qt.AspectRatioMode.KeepAspectRatio,
                    QtCore.Qt.TransformationMode.SmoothTransformation,
                )
                icon = QtGui.QIcon(scaled)
            item = QtWidgets.QListWidgetItem(icon, tile_path.name)
            item.setData(QtCore.Qt.ItemDataRole.UserRole, str(tile_path))
            item.setToolTip(str(tile_path))
            self._tile_list.addItem(item)

    def _on_tile_activated(self, item: QtWidgets.QListWidgetItem) -> None:
        path_str = item.data(QtCore.Qt.ItemDataRole.UserRole)
        if not path_str:
            return
        _reveal_in_explorer(Path(path_str))

    def _on_open_folder_clicked(self) -> None:
        if self._tile_result is None:
            return
        # Reveal the contact sheet (cleanest "this is the output" anchor).
        target = self._tile_result.contact_sheet_path
        if not target.exists():
            target = self._tile_result.out_dir
        _reveal_in_explorer(target)

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

    def _visible_issues(self) -> List[TileIssue]:
        if not self._last_issues:
            return []
        return [
            i for i in self._last_issues if _issue_key(i) not in self._ignored
        ]

    def _append_row(self, row_idx: int, issue: TileIssue) -> None:
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

        self._table.setItem(row_idx, 1, QtWidgets.QTableWidgetItem(issue.code))

        msg_item = QtWidgets.QTableWidgetItem(issue.message)
        msg_item.setToolTip(issue.message)
        self._table.setItem(row_idx, 2, msg_item)

        actions = QtWidgets.QWidget()
        h = QtWidgets.QHBoxLayout(actions)
        h.setContentsMargins(4, 2, 4, 2)
        h.setSpacing(4)
        ignore_btn = QtWidgets.QPushButton("Ignore")
        ignore_btn.clicked.connect(
            lambda _=False, iss=issue: self._on_ignore_clicked(iss)
        )
        h.addWidget(ignore_btn)
        h.addStretch(1)
        self._table.setCellWidget(row_idx, 3, actions)

    # ------------------------------------------------------------------
    # Action handlers
    # ------------------------------------------------------------------

    def _on_ignore_clicked(self, issue: TileIssue) -> None:
        self._ignored.add(_issue_key(issue))
        self._populate_table()
        self._refresh_status()

    def _on_back_to_build_clicked(self) -> None:
        # Step 5 (build) is at index 4 in the canonical step list.
        self.main_window.navigate_to_step(4)

    def _on_done_clicked(self) -> None:
        # Terminal step: close the main window (the app exits when the
        # last window closes).
        self.main_window.close()

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
        if self._tile_result is None or self._last_issues is None:
            self._summary_label.setText("")
            self._footer_label.setText("")
            return
        total = len(self._last_issues)
        ignored = len(self._ignored)
        visible = self._visible_issues()
        errs = sum(1 for i in visible if i.severity == "error")
        warns = sum(1 for i in visible if i.severity == "warning")

        n_tiles = len(self._tile_result.tile_paths)
        grid = self._tile_result.grid
        scale = self._results_scale.value()
        scale_suffix = f", {scale:g}%" if scale != 100.0 else ""
        head = (
            f"{n_tiles} tile(s) ({grid.rows}x{grid.cols}, "
            f"{grid.orientation}{scale_suffix}) "
            f"-> {self._tile_result.out_dir.name}\\"
        )
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
