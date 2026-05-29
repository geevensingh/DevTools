"""Unit tests for ``officemapmaker.editor.controller.save_calibration_with_backup``."""

from __future__ import annotations

from pathlib import Path

import pytest

pytest.importorskip("PySide6")

from officemapmaker.calibration import (  # noqa: E402
    Calibration,
    Label,
    RenderDefaults,
    Room,
    load_calibration,
)
from officemapmaker.editor.controller import save_calibration_with_backup  # noqa: E402


def _mkcal(*, label_id: str = "1480") -> Calibration:
    return Calibration(
        map_image="m.png",
        map_hash="sha256:deadbeef",
        labels=[
            Label(
                id=label_id,
                bbox=(0, 0, 10, 10),
                room_id=1,
                fill_seed=(5, 5),
                ocr_confidence=0.9,
                notes="",
            ),
        ],
        rooms=[Room(id=1, polygon_rle="", area_px=100, bbox=(0, 0, 50, 50))],
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )


# ---------------------------------------------------------------- first save


def test_first_save_creates_no_backup(tmp_path: Path):
    path = tmp_path / "calibration.json"
    cal = _mkcal(label_id="1480")

    backup = save_calibration_with_backup(cal, path)

    assert backup is None
    assert path.exists()
    # The file must be a valid calibration on disk — round-trip via the loader.
    roundtrip = load_calibration(path)
    assert roundtrip.labels[0].id == "1480"
    assert not (tmp_path / "calibration.json.bak").exists()


# ---------------------------------------------------- subsequent save backs up


def test_subsequent_save_writes_bak(tmp_path: Path):
    path = tmp_path / "calibration.json"
    save_calibration_with_backup(_mkcal(label_id="1480"), path)

    backup = save_calibration_with_backup(_mkcal(label_id="1505B"), path)

    bak = tmp_path / "calibration.json.bak"
    assert backup == bak
    assert bak.exists()
    # The current file should now reflect the second save.
    assert load_calibration(path).labels[0].id == "1505B"
    # The .bak should reflect the previous save.
    assert load_calibration(bak).labels[0].id == "1480"


def test_repeated_saves_overwrite_single_bak(tmp_path: Path):
    """We keep one .bak (previous version) — not a rolling N."""
    path = tmp_path / "calibration.json"
    save_calibration_with_backup(_mkcal(label_id="A"), path)
    save_calibration_with_backup(_mkcal(label_id="B"), path)
    save_calibration_with_backup(_mkcal(label_id="C"), path)

    bak = tmp_path / "calibration.json.bak"
    # Current = C, bak = B (the previous version), no .bak.bak or .bak2.
    assert load_calibration(path).labels[0].id == "C"
    assert load_calibration(bak).labels[0].id == "B"
    # No other backup variants littering the directory.
    others = [p.name for p in tmp_path.iterdir() if p.name not in
              {"calibration.json", "calibration.json.bak"}]
    assert others == [], f"unexpected stray files: {others}"


# ------------------------------------------------------- atomic-write hygiene


def test_save_leaves_no_tmp_file_on_success(tmp_path: Path):
    path = tmp_path / "calibration.json"
    save_calibration_with_backup(_mkcal(), path)
    assert not (tmp_path / "calibration.json.tmp").exists()


def test_save_creates_parent_dirs(tmp_path: Path):
    nested = tmp_path / "deep" / "nested" / "calibration.json"
    save_calibration_with_backup(_mkcal(), nested)
    assert nested.exists()
