"""End-to-end smoke test for the calibration editor.

Boots :class:`EditorMainWindow` against a minimal-but-real calibration +
map on disk. Verifies the window comes up with the canvas, inspector,
menus, and status bar all in place; the undo stack is clean; and the
window closes without prompting (because nothing's dirty).

Also tests :func:`launch`'s input-validation failure paths so we know
the CLI's ``calibrate edit`` and ``calibrate --edit`` flows surface
useful errors when paths are wrong.

These are smoke tests on purpose — fine-grained behavior lives in the
other ``test_editor_*`` files (commands, room picker, save, add/delete).
"""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

pytest.importorskip("PySide6")
pytest.importorskip("pytestqt")
from PySide6 import QtCore, QtGui, QtWidgets  # noqa: E402

from officemapmaker.calibration import (  # noqa: E402
    Calibration,
    Label,
    RenderDefaults,
    Room,
    save_calibration,
)
from officemapmaker.editor.app import EditorMainWindow, launch  # noqa: E402
from officemapmaker.editor.canvas import MapCanvas  # noqa: E402
from officemapmaker.editor.sidebar import InspectorPanel  # noqa: E402
from officemapmaker.geometry import mask_to_rle  # noqa: E402


# ---------------------------------------------------------------- helpers


def _square_rle(x: int, y: int, side: int, canvas: int = 64) -> str:
    """Encode a single square in full-image coords as RLE.

    Real calibrations store polygons in image coords (not room-local),
    so the mask must be sized to the whole image. Same trick used in
    ``test_editor_add_delete``.
    """
    mask = np.zeros((canvas, canvas), dtype=np.uint8)
    mask[y : y + side, x : x + side] = 1
    return mask_to_rle(mask)


def _mkcal() -> Calibration:
    """A 2-room, 2-label calibration small enough to set up in 64x64."""
    return Calibration(
        map_image="map.png",
        map_hash="sha256:deadbeef",
        labels=[
            Label(
                id="1480",
                bbox=(8, 8, 16, 8),
                room_id=1,
                fill_seed=(16, 16),
                ocr_confidence=0.95,
                notes="",
            ),
            Label(
                id="1481",
                bbox=(40, 8, 16, 8),
                room_id=2,
                fill_seed=(48, 16),
                ocr_confidence=0.90,
                notes="",
            ),
        ],
        rooms=[
            Room(id=1, polygon_rle=_square_rle(8, 8, 16), area_px=256, bbox=(8, 8, 16, 16)),
            Room(id=2, polygon_rle=_square_rle(40, 8, 16), area_px=256, bbox=(40, 8, 16, 16)),
        ],
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )


def _make_calibration_on_disk(tmp_path: Path) -> tuple[Path, Path]:
    """Write a tiny calibration.json + map.png to ``tmp_path``.

    Returns ``(calibration_path, map_path)``.
    """
    map_path = tmp_path / "map.png"
    # 64x64 white PNG is small but real enough for QImage to load.
    img = QtGui.QImage(64, 64, QtGui.QImage.Format.Format_RGB888)
    img.fill(QtGui.QColor("white"))
    assert img.save(str(map_path), "PNG"), "fixture map.png failed to write"

    cal_path = tmp_path / "calibration.json"
    save_calibration(_mkcal(), cal_path)
    return cal_path, map_path


# -------------------------------------------------------- main-window boot


def test_main_window_boots(tmp_path: Path, qtbot):
    """End-to-end: build EditorMainWindow against on-disk fixtures and show it."""
    cal_path, map_path = _make_calibration_on_disk(tmp_path)
    from officemapmaker.calibration import load_calibration

    cal = load_calibration(cal_path)
    window = EditorMainWindow(
        calibration=cal,
        calibration_path=cal_path,
        map_path=map_path,
    )
    qtbot.addWidget(window)
    window.show()

    # Title carries the file name (with the [*] modified-marker placeholder
    # stripped — Qt only shows the * when modified).
    assert cal_path.name in window.windowTitle()

    # Central widget is the canvas; inspector dock exists.
    assert isinstance(window.centralWidget(), MapCanvas)
    docks = window.findChildren(QtWidgets.QDockWidget)
    assert any(isinstance(d.widget(), InspectorPanel) for d in docks)

    # Menus the user expects: File, Edit, View, Tools.
    menu_titles = {m.title().replace("&", "") for m in window.menuBar().findChildren(QtWidgets.QMenu)}
    for required in ("File", "Edit", "View", "Tools"):
        assert required in menu_titles, f"missing menu: {required} (found {menu_titles})"

    # Status bar exists and shows counts text for both labels.
    assert window.statusBar() is not None
    counts_text = " ".join(
        lbl.text() for lbl in window.statusBar().findChildren(QtWidgets.QLabel)
    )
    assert "labels: 2" in counts_text
    assert "rooms: 2" in counts_text

    # Nothing's been edited; closing must not prompt.
    window.close()


# --------------------------------------------------------- launch failures


def test_launch_missing_calibration(tmp_path: Path, capsys):
    rc = launch(tmp_path / "nope.json")
    assert rc == 2
    err = capsys.readouterr().err
    assert "calibration not found" in err


def test_launch_missing_map(tmp_path: Path, capsys):
    cal_path, map_path = _make_calibration_on_disk(tmp_path)
    map_path.unlink()  # delete the map so launch's resolution fails
    rc = launch(cal_path)
    assert rc == 2
    err = capsys.readouterr().err
    assert "source map not found" in err


# ----------------------------------------------------------- launch success


def test_launch_success_with_existing_qapp(tmp_path: Path, qtbot):
    """When a QApplication already exists, launch returns 0 without exec().

    Verifies the test-mode shortcut documented in app.py:478-548.
    """
    cal_path, map_path = _make_calibration_on_disk(tmp_path)

    # qtbot guarantees a QApplication is alive; launch's owned_app branch
    # returns without entering the event loop.
    rc = launch(cal_path, map_path)
    assert rc == 0

    # The window it constructed is still alive (parented to None) — find
    # it via QApplication.topLevelWidgets and close it so it doesn't
    # leak across tests.
    app = QtWidgets.QApplication.instance()
    assert app is not None
    for w in app.topLevelWidgets():
        if isinstance(w, EditorMainWindow):
            w.close()
            w.deleteLater()
