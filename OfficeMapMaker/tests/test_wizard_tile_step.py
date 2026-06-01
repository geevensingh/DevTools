"""Tests for the W9 Tile + PDF terminal step pane.

Same shape as the W8 build-step wizard tests: monkeypatch the
``tile_composite`` adapter and verify the step's 3-pane state machine,
table population, ignore list, status mapping, re-run, Open-folder,
and Done button behavior.
"""

from __future__ import annotations

import time
from pathlib import Path

import pytest
from PIL import Image
from PySide6 import QtCore, QtWidgets

from officemapmaker.calibration import Calibration
from officemapmaker.layout import Layout
from officemapmaker.tile import TileGrid, TileIssue, TilePlacement, TileResult
from officemapmaker.wizard.main_window import MainWindow, StepStatus
from officemapmaker.wizard.steps.build_step import BuildStep
from officemapmaker.wizard.steps.tile_step import (
    TileStep,
    _classify_issues,
    _issue_key,
    _run_tile_composite,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


@pytest.fixture(scope="module")
def qapp():
    import os

    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication([])
    yield app


@pytest.fixture
def inputs(tmp_path: Path):
    map_path = tmp_path / "demo.png"
    assn_path = tmp_path / "demo.xlsx"
    Image.new("RGB", (64, 64), color=(255, 255, 255)).save(map_path)
    assn_path.write_bytes(b"PK\x03\x04" + b"b" * 64)
    return map_path, assn_path, tmp_path


def _drain_until(predicate, *, timeout_s: float = 5.0, tick_ms: int = 10) -> bool:
    deadline = time.monotonic() + timeout_s
    app = QtWidgets.QApplication.instance()
    assert app is not None
    while time.monotonic() < deadline:
        app.processEvents(
            QtCore.QEventLoop.ProcessEventsFlag.AllEvents, tick_ms
        )
        if predicate():
            return True
    return False


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _install_layout_and_composite(
    w: MainWindow, *, out: Path, composite_name: str = "composite.png",
) -> Path:
    """Mark the wizard as if Build composite has run successfully.

    Installs an (empty) layout + calibration on the session, writes a
    tiny dummy composite.png, and seeds BuildStep._composite_path so
    TileStep.on_activated sees a complete prerequisite chain.
    """
    cal = Calibration(map_image="demo.png", map_hash="sha256:x")
    w.session.calibration = cal
    w.session.layout = Layout(
        map_image="demo.png", map_hash="sha256:x", entries=[]
    )

    composite = out / composite_name
    Image.new("RGB", (200, 200), color=(255, 255, 255)).save(composite)

    build_step = w._steps[4].widget
    assert isinstance(build_step, BuildStep)
    build_step._composite_path = composite
    return composite


def _go_to_tile(w: MainWindow) -> TileStep:
    w._sidebar.setCurrentRow(5)
    step = w._steps[5].widget
    assert isinstance(step, TileStep)
    return step


def _make_tile_result(
    out_dir: Path,
    *,
    issues: list[TileIssue] | None = None,
    n_tiles: int = 2,
    rows: int = 1,
    cols: int = 2,
) -> TileResult:
    out_dir.mkdir(parents=True, exist_ok=True)
    tile_paths: list[Path] = []
    for i in range(n_tiles):
        r = (i // cols) + 1
        c = (i % cols) + 1
        p = out_dir / f"page-{r}x{c}.png"
        Image.new("RGB", (50, 60), color=(240, 240, 240)).save(p)
        tile_paths.append(p)
    contact = out_dir / "contact_sheet.png"
    Image.new("RGB", (120, 80), color=(255, 255, 255)).save(contact)
    pdf = out_dir / "all.pdf"
    pdf.write_bytes(b"%PDF-1.4\n%%EOF\n")
    placements = tuple(
        TilePlacement(row=(i // cols) + 1, col=(i % cols) + 1,
                      bbox=(0, 0, 100, 100))
        for i in range(n_tiles)
    )
    grid = TileGrid(
        composite_size=(200, 200),
        page_size_in=(8.5, 11.0),
        dpi=150,
        overlap_in=0.25,
        rows=rows,
        cols=cols,
        tile_px=(50, 60),
        overlap_px=37,
        tiles=placements,
    )
    return TileResult(
        out_dir=out_dir,
        tile_paths=tile_paths,
        contact_sheet_path=contact,
        pdf_path=pdf,
        grid=grid,
        issues=list(issues or []),
    )


# ---------------------------------------------------------------------------
# Classifier
# ---------------------------------------------------------------------------


def test_classify_error_wins():
    issues = [
        TileIssue(severity="warning", code="meta_sidecar_missing", message="m"),
        TileIssue(severity="error", code="coverage_gap", message="g"),
    ]
    status, msgs, codes, sevs = _classify_issues(issues)
    assert status == StepStatus.ERROR
    assert codes == ["meta_sidecar_missing", "coverage_gap"]
    assert sevs == ["warning", "error"]
    assert all("[" in m for m in msgs)


def test_classify_warning_when_no_errors():
    status, _, _, _ = _classify_issues([
        TileIssue(severity="warning", code="x", message="m"),
    ])
    assert status == StepStatus.WARNING


def test_classify_empty_is_ok():
    status, msgs, codes, sevs = _classify_issues([])
    assert status == StepStatus.OK
    assert msgs == codes == sevs == []


def test_issue_key_distinguishes_codes_and_messages():
    a = TileIssue(severity="warning", code="x", message="m1")
    b = TileIssue(severity="warning", code="y", message="m1")
    c = TileIssue(severity="warning", code="x", message="m2")
    assert _issue_key(a) != _issue_key(b)
    assert _issue_key(a) != _issue_key(c)
    assert _issue_key(a) == _issue_key(
        TileIssue(severity="error", code="x", message="m1"),
    )


# ---------------------------------------------------------------------------
# Step-6 instance check
# ---------------------------------------------------------------------------


def test_step_six_is_tile_step_instance(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        assert isinstance(w._steps[5].widget, TileStep)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# No-composite pane when build hasn't run
# ---------------------------------------------------------------------------


def test_no_composite_pane_shown_when_build_not_finished(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        # No layout, no composite.
        step = _go_to_tile(w)
        assert step._stack.currentWidget() is step._no_composite_pane
    finally:
        w.close()


def test_no_composite_pane_shown_when_composite_missing_from_disk(
    qapp, inputs,
):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        composite = _install_layout_and_composite(w, out=out)
        # Delete the composite to simulate the user removing it externally.
        composite.unlink()
        step = _go_to_tile(w)
        assert step._stack.currentWidget() is step._no_composite_pane
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Landing pane shown when composite exists but no run cached
# ---------------------------------------------------------------------------


def test_landing_pane_shown_when_composite_exists(qapp, inputs):
    map_path, assn_path, out = inputs
    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        assert step._stack.currentWidget() is step._landing_pane
        assert step._run_button.isEnabled()
        # Default control values.
        assert step._landing_paper.currentText() == "letter"
        assert step._landing_dpi.value() == 150
        assert step._landing_overlap.value() == pytest.approx(0.25)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Run -> results pane with thumbnails + status mapping
# ---------------------------------------------------------------------------


def test_run_button_populates_results_with_warning_status(
    qapp, inputs, monkeypatch,
):
    map_path, assn_path, out = inputs
    composite = None
    tile_dir = out / "tiles"
    issues = [
        TileIssue(severity="warning", code="meta_sidecar_missing",
                  message="no metadata"),
    ]

    captured: dict = {}

    def fake_adapter(comp_path, out_dir, *, dpi, paper, overlap_in,
                     progress_cb, cancel_cb):
        captured["composite"] = comp_path
        captured["out_dir"] = out_dir
        captured["dpi"] = dpi
        captured["paper"] = paper
        captured["overlap_in"] = overlap_in
        progress_cb(0.5, "rendering")
        result = _make_tile_result(out_dir, issues=issues, n_tiles=2)
        return (result,), list(result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._run_tile_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        composite = _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert step._stack.currentWidget() is step._results_pane
        assert step._table.rowCount() == 1
        assert step._tile_list.count() == 2
        assert w._steps[5].status == StepStatus.WARNING
        # Adapter received the right inputs from the controls.
        assert captured["composite"] == composite
        assert captured["out_dir"] == tile_dir
        assert captured["paper"] == "letter"
        assert captured["dpi"] == 150
        assert captured["overlap_in"] == pytest.approx(0.25)
        # Open folder is now enabled.
        assert step._open_button.isEnabled()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Clean run -> OK + empty-state visible
# ---------------------------------------------------------------------------


def test_clean_run_shows_empty_state(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    def fake_adapter(comp_path, out_dir, *, dpi, paper, overlap_in,
                     progress_cb, cancel_cb):
        result = _make_tile_result(out_dir, issues=[])
        return (result,), []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._run_tile_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert not step._empty_label.isHidden()
        assert step._table.rowCount() == 0
        assert w._steps[5].status == StepStatus.OK
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Paper / DPI / overlap controls flow into adapter args
# ---------------------------------------------------------------------------


def test_controls_pass_through_to_adapter(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    captured: dict = {}

    def fake_adapter(comp_path, out_dir, *, dpi, paper, overlap_in,
                     progress_cb, cancel_cb):
        captured["dpi"] = dpi
        captured["paper"] = paper
        captured["overlap_in"] = overlap_in
        result = _make_tile_result(out_dir, issues=[])
        return (result,), []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._run_tile_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        step._landing_paper.setCurrentText("a4")
        step._landing_dpi.setValue(300)
        step._landing_overlap.setValue(0.50)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert captured["paper"] == "a4"
        assert captured["dpi"] == 300
        assert captured["overlap_in"] == pytest.approx(0.50)
        # Landing values are mirrored to the results-pane controls.
        assert step._results_paper.currentText() == "a4"
        assert step._results_dpi.value() == 300
        assert step._results_overlap.value() == pytest.approx(0.50)
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Ignore removes a row and (if all issues ignored) flips status to OK
# ---------------------------------------------------------------------------


def test_ignore_removes_row_and_updates_status(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    issue = TileIssue(severity="warning", code="min_font_warning",
                      message="text too small at this DPI")

    def fake_adapter(comp_path, out_dir, *, dpi, paper, overlap_in,
                     progress_cb, cancel_cb):
        result = _make_tile_result(out_dir, issues=[issue])
        return (result,), list(result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._run_tile_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        assert w._steps[5].status == StepStatus.WARNING
        assert step._table.rowCount() == 1
        step._on_ignore_clicked(issue)
        assert step._table.rowCount() == 0
        assert not step._empty_label.isHidden()
        assert w._steps[5].status == StepStatus.OK
        assert "1 issue" in step._footer_label.text()
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Re-build clears cached results + ignore set
# ---------------------------------------------------------------------------


def test_rerun_clears_cached_results_and_ignore(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    issue = TileIssue(severity="warning", code="x", message="m")

    def fake_adapter(comp_path, out_dir, *, dpi, paper, overlap_in,
                     progress_cb, cancel_cb):
        result = _make_tile_result(out_dir, issues=[issue])
        return (result,), list(result.issues)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._run_tile_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        step._on_ignore_clicked(issue)
        assert _issue_key(issue) in step._ignored
        assert step._table.rowCount() == 0

        # Wait for the runner to fully release before re-firing.
        assert _drain_until(lambda: w._active_runner is None)

        step._rerun_button.click()
        assert _drain_until(lambda: step._table.rowCount() == 1)
        assert step._ignored == set()
        assert w._steps[5].status == StepStatus.WARNING
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Done button closes the wizard
# ---------------------------------------------------------------------------


def test_done_button_closes_main_window(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    def fake_adapter(comp_path, out_dir, *, dpi, paper, overlap_in,
                     progress_cb, cancel_cb):
        result = _make_tile_result(out_dir, issues=[])
        return (result,), []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._run_tile_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        assert w.isVisible() is False or w.isVisible() is True  # tolerant

        closed: dict = {"called": False}
        orig_close = w.close

        def fake_close():
            closed["called"] = True
            return orig_close()

        w.close = fake_close  # type: ignore[assignment]
        step._done_button.click()
        assert closed["called"]
    finally:
        # Best-effort: window may already be closed via the fake above.
        try:
            w.close()
        except Exception:
            pass


# ---------------------------------------------------------------------------
# Open-folder button calls the reveal helper
# ---------------------------------------------------------------------------


def test_open_folder_button_invokes_reveal(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    def fake_adapter(comp_path, out_dir, *, dpi, paper, overlap_in,
                     progress_cb, cancel_cb):
        result = _make_tile_result(out_dir, issues=[])
        return (result,), []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._run_tile_composite",
        fake_adapter,
    )

    reveal_calls: list[Path] = []

    def fake_reveal(p: Path) -> None:
        reveal_calls.append(p)

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._reveal_in_explorer",
        fake_reveal,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)

        step._open_button.click()
        assert reveal_calls
        # The reveal target should be the contact sheet (or the dir).
        assert reveal_calls[-1].name in {"contact_sheet.png", "tiles"}
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Activation after a successful run shows cached results
# ---------------------------------------------------------------------------


def test_activation_with_cached_state_shows_results(qapp, inputs, monkeypatch):
    map_path, assn_path, out = inputs

    def fake_adapter(comp_path, out_dir, *, dpi, paper, overlap_in,
                     progress_cb, cancel_cb):
        result = _make_tile_result(out_dir, issues=[])
        return (result,), []

    monkeypatch.setattr(
        "officemapmaker.wizard.steps.tile_step._run_tile_composite",
        fake_adapter,
    )

    w = MainWindow(map_path=map_path, assignments_path=assn_path, output_dir=out)
    try:
        _install_layout_and_composite(w, out=out)
        step = _go_to_tile(w)
        step._run_button.click()
        assert _drain_until(lambda: step._last_issues is not None)
        assert step._stack.currentWidget() is step._results_pane

        w.navigate_to_step(0)
        w.navigate_to_step(5)
        assert step._stack.currentWidget() is step._results_pane
    finally:
        w.close()


# ---------------------------------------------------------------------------
# Real adapter smoke test: tiny composite -> real tile_composite() call
# ---------------------------------------------------------------------------


def test_real_adapter_writes_tile_outputs(tmp_path: Path):
    composite = tmp_path / "composite.png"
    # 1700x2200 fits in one letter tile at 150 DPI (1275x1650 printable + 0.25" overlap).
    Image.new("RGB", (1700, 2200), color=(255, 255, 255)).save(composite)

    out_dir = tmp_path / "tiles"

    def progress_cb(_frac, _msg):
        pass

    def cancel_cb():
        return False

    result, issues = _run_tile_composite(
        composite, out_dir,
        dpi=150, paper="letter", overlap_in=0.25,
        progress_cb=progress_cb, cancel_cb=cancel_cb,
    )
    (tile_result,) = result
    assert isinstance(tile_result, TileResult)
    assert tile_result.out_dir == out_dir
    assert tile_result.contact_sheet_path.exists()
    assert tile_result.pdf_path.exists()
    assert len(tile_result.tile_paths) == len(tile_result.grid.tiles)
    # No meta sidecar -> we expect a warning but no errors.
    assert all(i.severity == "warning" for i in issues)


def test_real_adapter_cancels_before_render(tmp_path: Path):
    from officemapmaker.pipeline import PipelineCanceled

    composite = tmp_path / "composite.png"
    Image.new("RGB", (200, 200), color=(255, 255, 255)).save(composite)

    out_dir = tmp_path / "tiles"

    def progress_cb(_frac, _msg):
        pass

    def cancel_cb():
        return True  # cancel immediately

    with pytest.raises(PipelineCanceled):
        _run_tile_composite(
            composite, out_dir,
            dpi=150, paper="letter", overlap_in=0.25,
            progress_cb=progress_cb, cancel_cb=cancel_cb,
        )
    assert not out_dir.exists() or not any(out_dir.iterdir())
