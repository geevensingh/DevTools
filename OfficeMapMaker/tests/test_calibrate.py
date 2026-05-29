"""Tests for ``officemapmaker.calibrate``.

These tests cover three concerns:

1. **Pure-function unit tests** — classification, label filtering, room
   association — that don't need Tesseract or even cv2.
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
    _classify,
    _LABEL_PATTERN,
    calibrate_map,
    find_tesseract,
)
from officemapmaker.calibration import Classification
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


def test_classify_normal_office():
    bbox = (0, 0, 100, 100)
    assert _classify(bbox, room_area=10_000, median_area=10_000) == Classification.OFFICE


def test_classify_long_thin_hallway():
    # 1000x100 ratio = 10 > HALLWAY_ASPECT_RATIO (4.0)
    bbox = (0, 0, 1000, 100)
    assert _classify(bbox, room_area=100_000, median_area=10_000) == Classification.HALLWAY


def test_classify_very_large_common_area():
    # Square but >> 4x the median.
    bbox = (0, 0, 400, 400)
    assert _classify(bbox, room_area=160_000, median_area=10_000) == Classification.COMMON


def test_classify_zero_width_is_skip():
    assert _classify((0, 0, 0, 100), room_area=0, median_area=10_000) == Classification.SKIP


def test_classify_l_shaped_office_is_not_hallway():
    # L-shaped office: bounding box is 1000x200 (aspect=5, would be hallway
    # by aspect alone) but the polygon only fills ~40% of the bbox -> office.
    bbox = (0, 0, 1000, 200)
    bbox_area = 1000 * 200
    polygon_area = int(bbox_area * 0.4)
    assert _classify(bbox, room_area=polygon_area, median_area=polygon_area) == Classification.OFFICE


def test_classify_long_solid_corridor_is_hallway():
    # Same 1000x200 bounding box but the polygon is solid -> hallway.
    bbox = (0, 0, 1000, 200)
    bbox_area = 1000 * 200
    polygon_area = int(bbox_area * 0.95)
    assert (
        _classify(bbox, room_area=polygon_area, median_area=10_000)
        == Classification.HALLWAY
    )


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
        assert label.classification == Classification.OFFICE

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
    assert orphan.classification == Classification.SKIP


def test_two_office_labels_in_one_room_is_error(tmp_path: Path):
    components = [_square_cc(1, 10, 10, 200)]
    # Both labels fall inside the single CC -> merged-room error.
    ocr_labels = [
        _ocr("1480", (40, 40, 30, 20)),
        _ocr("1481", (140, 140, 30, 20)),
    ]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")

    _cal, issues = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    merge_errors = [i for i in issues if i.code == "multiple_office_labels_in_room"]
    assert len(merge_errors) == 1
    assert merge_errors[0].severity == "error"


def test_duplicate_office_id_across_rooms_is_error(tmp_path: Path):
    components = [_square_cc(1, 10, 10, 100), _square_cc(2, 200, 10, 100)]
    ocr_labels = [_ocr("1003", (40, 40, 30, 20)), _ocr("1003", (230, 40, 30, 20))]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"x")

    _cal, issues = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    dup_errors = [i for i in issues if i.code == "duplicate_office_id"]
    assert len(dup_errors) == 1
    assert "1003" in dup_errors[0].message


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
        assert lab.classification == Classification.OFFICE


def test_calibrate_map_missing_file_raises(tmp_path: Path):
    with pytest.raises(FileNotFoundError):
        calibrate_map(tmp_path / "does-not-exist.png")


# ---------------------------------------------------------------------------
# CLI integration
# ---------------------------------------------------------------------------


@requires_tesseract
def test_cli_calibrate_writes_calibration_json(tmp_path: Path, monkeypatch, capsys):
    from officemapmaker.__main__ import main

    map_path = tmp_path / "synthetic.png"
    _make_synthetic_map(map_path)
    out = tmp_path / "calibration.json"

    rc = main(["calibrate", "--map", str(map_path), "--out", str(out)])

    assert out.exists()
    # Exit code is 0 for clean calibration, 1 if there are issues — both are OK
    # here as long as the JSON was written.
    assert rc in (0, 1)


def test_cli_calibrate_missing_map_returns_2(tmp_path: Path, capsys):
    from officemapmaker.__main__ import main

    rc = main(["calibrate", "--map", str(tmp_path / "nope.png"), "--out", str(tmp_path / "cal.json")])
    assert rc == 2
    captured = capsys.readouterr()
    assert "not found" in captured.err.lower() or "no such" in captured.err.lower()
