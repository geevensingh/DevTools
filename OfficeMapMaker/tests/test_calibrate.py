"""Tests for ``officemapmaker.calibrate``.

These tests cover three concerns:

1. **Pure-function unit tests** — label filtering, room association — that
   don't need Tesseract or even cv2.
2. **End-to-end pipeline tests against a synthetic map** rendered with PIL.
   These are skipped automatically if Tesseract isn't installed, so the test
   suite still works on CI machines without OCR.
3. **CLI exit-code tests** for graceful failures (missing map, no Tesseract).
"""

from __future__ import annotations

import os
import shutil
from dataclasses import replace
from pathlib import Path

import numpy as np
import pytest

from officemapmaker.calibrate import (
    CalibrationIssue,
    TesseractNotFoundError,
    _build_calibration,
    _LABEL_PATTERN,
    calibrate_map,
    find_tesseract,
)
from officemapmaker.geometry import ConnectedComponent


# ---------------------------------------------------------------------------
# Fixtures + helpers
# ---------------------------------------------------------------------------


def _has_tesseract() -> bool:
    return find_tesseract() is not None


requires_tesseract = pytest.mark.skipif(
    not _has_tesseract(), reason="tesseract executable not available"
)


def _make_synthetic_map(path: Path) -> dict[str, tuple[int, int, int, int]]:
    """Render a tiny 3-room floor plan with PIL.

    Returns a dict mapping label-text -> (x, y, w, h) of the room bbox, useful
    if a test wants to make assertions in image coordinates.
    """
    from PIL import Image, ImageDraw, ImageFont

    W, H = 800, 400
    img = Image.new("L", (W, H), color=255)
    draw = ImageDraw.Draw(img)

    # Three rooms in a row, separated by walls.
    rooms = {
        "1480": (40, 40, 200, 320),
        "1481": (300, 40, 200, 320),
        "1482": (560, 40, 200, 320),
    }
    # Outer wall around the building.
    draw.rectangle((20, 20, 780, 380), outline=0, width=4)
    for x, y, w, h in rooms.values():
        # Each room is a hollow rectangle.
        draw.rectangle((x, y, x + w, y + h), outline=0, width=4)

    # Try to use a real TrueType font; fall back to the default bitmap font.
    try:
        font = ImageFont.truetype("arial.ttf", 48)
    except (OSError, IOError):
        font = ImageFont.load_default()

    for text, (x, y, w, h) in rooms.items():
        # Render label well away from any wall.
        draw.text((x + w // 2 - 60, y + h // 2 - 30), text, fill=0, font=font)

    img.save(path)
    return rooms


# ---------------------------------------------------------------------------
# Pure-function tests
# ---------------------------------------------------------------------------


@pytest.mark.parametrize(
    "text,should_match",
    [
        ("1480", True),
        ("1479A", True),
        ("MER101", True),
        ("ELV101", True),
        ("0", False),       # too short
        ("12", False),      # too short
        ("12345", True),    # 5 digits is the boundary
        ("ABCDE", False),   # no digits
        ("1480 ", False),   # raw_text is stripped before regex; trailing space wouldn't reach here
        ("", False),
        ("---", False),
    ],
)
def test_label_pattern_accepts_only_room_id_like_strings(text, should_match):
    assert bool(_LABEL_PATTERN.match(text)) is should_match


# ---------------------------------------------------------------------------
# _build_calibration() — exercised without Tesseract by feeding fake OCR
# ---------------------------------------------------------------------------


def _square_cc(cc_id: int, x: int, y: int, side: int) -> ConnectedComponent:
    """Build a synthetic ConnectedComponent representing a filled square."""
    mask = np.zeros((y + side + 10, x + side + 10), dtype=np.uint8)
    mask[y : y + side, x : x + side] = 1
    return ConnectedComponent(
        cc_id=cc_id,
        area_px=side * side,
        bbox=(x, y, side, side),
        centroid=(x + side // 2, y + side // 2),
        mask=mask.astype(bool),
    )


def _ocr(text: str, bbox: tuple[int, int, int, int], conf: float = 0.9):
    from officemapmaker.calibrate import _OCRLabel
    return _OCRLabel(text=text, bbox=bbox, confidence=conf)


def test_build_calibration_associates_labels_to_rooms(tmp_path: Path):
    # Two rooms side by side, one label each.
    components = [
        _square_cc(1, 10, 10, 100),
        _square_cc(2, 200, 10, 100),
    ]
    ocr_labels = [
        _ocr("1480", (40, 40, 30, 20)),
        _ocr("1481", (230, 40, 30, 20)),
    ]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"\x89PNG\r\n\x1a\nfake")  # SHA only — not actually decoded

    cal, issues = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )

    assert len(cal.labels) == 2
    assert len(cal.rooms) == 2
    assert cal.map_image == "map.png"
    assert cal.map_hash.startswith("sha256:")
    for label in cal.labels:
        assert label.room_id is not None

    errors = [i for i in issues if i.severity == "error"]
    assert errors == []


def test_orphan_label_outside_any_room_warns(tmp_path: Path):
    components = [_square_cc(1, 10, 10, 100)]
    ocr_labels = [
        _ocr("1480", (40, 40, 30, 20)),     # inside room 1
        _ocr("9999", (500, 500, 30, 20)),   # nowhere near any CC
    ]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")

    cal, issues = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    orphan_warnings = [i for i in issues if i.code == "orphan_label"]
    assert len(orphan_warnings) == 1
    assert "9999" in orphan_warnings[0].message

    orphan = next(lab for lab in cal.labels if lab.id == "9999")
    assert orphan.room_id is None


def test_two_labels_in_one_room_both_associate(tmp_path: Path):
    """Without classification we no longer error on this — both labels just
    associate to the same room. Validation will flag this case only when a
    real spreadsheet assigns both as offices."""
    components = [_square_cc(1, 10, 10, 200)]
    ocr_labels = [
        _ocr("1480", (40, 40, 30, 20)),
        _ocr("1481", (140, 140, 30, 20)),
    ]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")

    cal, issues = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    # Both labels associate; no merged-room error.
    assert len(cal.labels) == 2
    assert all(lab.room_id == 1 for lab in cal.labels)
    assert [i for i in issues if i.code == "multiple_office_labels_in_room"] == []


def test_duplicate_office_id_no_longer_errors(tmp_path: Path):
    """Without classification this is purely a spreadsheet-validation concern.
    The calibrate pass keeps both labels and lets validation flag any conflict."""
    components = [_square_cc(1, 10, 10, 100), _square_cc(2, 200, 10, 100)]
    ocr_labels = [_ocr("1003", (40, 40, 30, 20)), _ocr("1003", (230, 40, 30, 20))]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")

    cal, issues = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    assert len(cal.labels) == 2
    assert [i for i in issues if i.code == "duplicate_office_id"] == []


def test_room_with_no_label_emits_orphan_warning(tmp_path: Path):
    components = [
        _square_cc(1, 10, 10, 100),
        _square_cc(2, 200, 10, 100),  # no label in this room
    ]
    ocr_labels = [_ocr("1480", (40, 40, 30, 20))]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")

    _cal, issues = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    orphan_room = [i for i in issues if i.code == "orphan_room"]
    assert len(orphan_room) == 1


def test_no_rooms_detected_is_error(tmp_path: Path):
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")
    _cal, issues = _build_calibration(
        map_path=map_path,
        components=[],
        ocr_labels=[_ocr("1480", (40, 40, 30, 20))],
    )
    codes = {i.code for i in issues}
    assert "no_rooms_detected" in codes


def test_no_labels_detected_is_error(tmp_path: Path):
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")
    _cal, issues = _build_calibration(
        map_path=map_path,
        components=[_square_cc(1, 10, 10, 100)],
        ocr_labels=[],
    )
    codes = {i.code for i in issues}
    assert "no_labels_detected" in codes


def test_fill_seed_uses_room_centroid_not_label_center(tmp_path: Path):
    # Big room with the label in the top-left corner.
    components = [_square_cc(1, 0, 0, 400)]
    ocr_labels = [_ocr("1480", (10, 10, 30, 20))]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")

    cal, _ = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    label = cal.labels[0]
    # The room centroid is at (~200, ~200); the label center is (~25, ~20).
    # The fill seed should be near the room centroid, not the label center.
    cx, cy = label.fill_seed
    assert cx > 100 and cy > 100


# ---------------------------------------------------------------------------
# Tesseract location
# ---------------------------------------------------------------------------


def test_find_tesseract_uses_env_var_first(tmp_path: Path, monkeypatch):
    fake = tmp_path / "tesseract.exe"
    fake.write_bytes(b"")  # just needs to be a file
    monkeypatch.setenv("TESSERACT_PATH", str(fake))
    assert find_tesseract() == str(fake)


def test_find_tesseract_returns_none_when_missing(monkeypatch):
    monkeypatch.delenv("TESSERACT_PATH", raising=False)
    # Point PATH at an empty directory and ensure the Windows default doesn't
    # exist on this test machine (skip the assertion if it does).
    if Path(r"C:\Program Files\Tesseract-OCR\tesseract.exe").exists():
        pytest.skip("real Tesseract installed at the Windows default path")
    monkeypatch.setenv("PATH", "")
    assert find_tesseract() is None


# ---------------------------------------------------------------------------
# End-to-end synthetic map (gated on Tesseract availability)
# ---------------------------------------------------------------------------


@requires_tesseract
def test_calibrate_map_end_to_end_on_synthetic_image(tmp_path: Path):
    map_path = tmp_path / "synthetic.png"
    expected_rooms = _make_synthetic_map(map_path)

    cal, issues = calibrate_map(map_path)

    # We don't insist on every OCR hit — Tesseract can miss one — but the
    # majority of our labels should appear, all classified as office, and
    # each label should be associated to some room.
    found_ids = {lab.id for lab in cal.labels}
    assert len(found_ids & set(expected_rooms.keys())) >= 2, (
        f"expected at least 2 of {set(expected_rooms.keys())}, got {found_ids}"
    )
    office_labels = [lab for lab in cal.labels if lab.id in expected_rooms]
    for lab in office_labels:
        assert lab.room_id is not None, f"label {lab.id} was not associated to a room"


def test_calibrate_map_missing_file_raises(tmp_path: Path):
    with pytest.raises(FileNotFoundError):
        calibrate_map(tmp_path / "does-not-exist.png")


# ---------------------------------------------------------------------------
# CLI integration
# ---------------------------------------------------------------------------
