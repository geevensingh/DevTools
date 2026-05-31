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
    revalidate_calibration,
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


@requires_tesseract
def test_calibrate_map_reports_progress(tmp_path: Path):
    """progress_cb is invoked with monotonically non-decreasing fractions in [0,1]."""
    map_path = tmp_path / "synthetic.png"
    _make_synthetic_map(map_path)

    records: list[tuple[float, str]] = []
    calibrate_map(
        map_path,
        progress_cb=lambda frac, msg: records.append((frac, msg)),
    )
    assert records, "progress_cb was never called"
    fractions = [r[0] for r in records]
    assert all(0.0 <= f <= 1.0 for f in fractions), fractions
    assert fractions == sorted(fractions), (
        f"progress fractions should be non-decreasing: {fractions}"
    )
    assert fractions[-1] == 1.0, f"last fraction should be 1.0: {fractions[-1]}"
    # And every progress call should carry a non-empty status message.
    assert all(msg for _, msg in records)


def test_calibrate_map_honors_cancel_cb(tmp_path: Path):
    """cancel_cb returning True between phases raises PipelineCanceled."""
    from officemapmaker.pipeline import PipelineCanceled

    # Use a real file so the FileNotFoundError isn't what trips first;
    # the cancel check happens before the (expensive) cv2.imread, so we
    # don't even need a valid PNG.
    map_path = tmp_path / "synthetic.png"
    map_path.write_bytes(b"not a real png")

    with pytest.raises(PipelineCanceled):
        calibrate_map(map_path, cancel_cb=lambda: True)


@requires_tesseract
def test_calibrate_map_via_pipeline_runner_blocking(tmp_path: Path):
    """End-to-end: calibrate_map plays nicely with PipelineRunner.run_blocking."""
    from officemapmaker.pipeline import PipelineRunner

    map_path = tmp_path / "synthetic.png"
    _make_synthetic_map(map_path)

    progress: list = []
    result, issues = PipelineRunner.run_blocking(
        calibrate_map,
        args=(map_path,),
        progress_records=progress,
    )
    assert result is not None
    assert isinstance(issues, list)
    assert progress, "runner did not record any progress"


# ---------------------------------------------------------------------------
# CLI integration
# ---------------------------------------------------------------------------


# ---------------------------------------------------------------------------
# revalidate_calibration() -- cheap re-check used by the wizard after each edit
# ---------------------------------------------------------------------------


def _two_room_calibration(tmp_path: Path):
    """Build a clean 2-room/2-label calibration via _build_calibration."""
    components = [
        _square_cc(1, 10, 10, 100),
        _square_cc(2, 200, 10, 100),
    ]
    ocr_labels = [
        _ocr("1480", (40, 40, 30, 20)),
        _ocr("1481", (230, 40, 30, 20)),
    ]
    map_path = tmp_path / "map.png"
    map_path.write_bytes(b"\x89PNG\r\n\x1a\nfake")
    cal, _ = _build_calibration(
        map_path=map_path, components=components, ocr_labels=ocr_labels
    )
    return cal


def test_revalidate_clean_calibration_returns_empty(tmp_path: Path):
    cal = _two_room_calibration(tmp_path)
    assert revalidate_calibration(cal) == []


def test_revalidate_flags_orphan_label_after_user_unassigns(tmp_path: Path):
    from dataclasses import replace as dc_replace

    cal = _two_room_calibration(tmp_path)
    # Simulate the user unassigning a label by setting room_id to None.
    cal.labels[0] = dc_replace(cal.labels[0], room_id=None)

    issues = revalidate_calibration(cal)
    codes = [(i.severity, i.code) for i in issues]
    assert ("warning", "orphan_label") in codes
    # And the room that lost its only label now becomes an orphan_room.
    assert ("warning", "orphan_room") in codes


def test_revalidate_flags_label_outside_assigned_room(tmp_path: Path):
    from dataclasses import replace as dc_replace

    cal = _two_room_calibration(tmp_path)
    # Move label 1480's bbox to coordinates outside room 1.
    cal.labels[0] = dc_replace(cal.labels[0], bbox=(900, 900, 30, 20))

    issues = revalidate_calibration(cal)
    codes = [(i.severity, i.code) for i in issues]
    assert ("error", "label_outside_assigned_room") in codes


def test_revalidate_flags_label_referencing_deleted_room(tmp_path: Path):
    cal = _two_room_calibration(tmp_path)
    # Simulate the user deleting room 1 without reassigning label 1480.
    cal.rooms = [r for r in cal.rooms if r.id != 1]

    issues = revalidate_calibration(cal)
    codes = [(i.severity, i.code) for i in issues]
    assert ("error", "label_outside_assigned_room") in codes
    # The "no longer exists" branch should fire with a recognisable message.
    msgs = [i.message for i in issues if i.code == "label_outside_assigned_room"]
    assert any("no longer exists" in m for m in msgs), msgs


def test_revalidate_flags_orphan_room_when_label_deleted(tmp_path: Path):
    cal = _two_room_calibration(tmp_path)
    # Simulate deleting the only label that referenced room 1.
    cal.labels = [lab for lab in cal.labels if lab.id != "1480"]

    issues = revalidate_calibration(cal)
    codes = [(i.severity, i.code) for i in issues]
    assert ("warning", "orphan_room") in codes
    # Room 2 still has its label so no orphan for it.
    msgs = [i.message for i in issues if i.code == "orphan_room"]
    assert all("room 1 " in m for m in msgs), msgs


def test_revalidate_no_rooms_is_error(tmp_path: Path):
    cal = _two_room_calibration(tmp_path)
    cal.rooms = []
    issues = revalidate_calibration(cal)
    codes = [(i.severity, i.code) for i in issues]
    assert ("error", "no_rooms_detected") in codes


def test_revalidate_no_labels_is_error(tmp_path: Path):
    cal = _two_room_calibration(tmp_path)
    cal.labels = []
    issues = revalidate_calibration(cal)
    codes = [(i.severity, i.code) for i in issues]
    assert ("error", "no_labels_detected") in codes


def test_revalidate_count_decreases_as_user_fixes_issues(tmp_path: Path):
    """The behaviour the wizard relies on: count goes down as edits are made."""
    from dataclasses import replace as dc_replace

    cal = _two_room_calibration(tmp_path)
    # Start broken: both labels unassigned.
    cal.labels[0] = dc_replace(cal.labels[0], room_id=None)
    cal.labels[1] = dc_replace(cal.labels[1], room_id=None)

    n_initial = len(revalidate_calibration(cal))
    # Fix one: reassign label 1480 to room 1.
    cal.labels[0] = dc_replace(cal.labels[0], room_id=1)
    n_after_one_fix = len(revalidate_calibration(cal))
    # Fix the other: reassign label 1481 to room 2.
    cal.labels[1] = dc_replace(cal.labels[1], room_id=2)
    n_after_two_fixes = len(revalidate_calibration(cal))

    assert n_after_one_fix < n_initial
    assert n_after_two_fixes < n_after_one_fix
    assert n_after_two_fixes == 0


# ---------------------------------------------------------------------------
# revalidate_calibration(..., quick=True) — used by live editing for speed
# ---------------------------------------------------------------------------


def test_revalidate_quick_skips_label_outside_room_mask_check(tmp_path: Path):
    """quick=True must NOT report a label-outside-room issue when the
    label is geometrically outside its assigned room's polygon but the
    room itself still exists. The mask test is too expensive to run on
    every keystroke; the full check is reserved for the on-demand
    Re-validate button.
    """
    from dataclasses import replace as dc_replace

    cal = _two_room_calibration(tmp_path)
    # Move label 1480 far outside room 1's polygon, but keep room 1.
    cal.labels[0] = dc_replace(cal.labels[0], bbox=(900, 900, 30, 20))

    # Full check should still flag it.
    full = revalidate_calibration(cal, quick=False)
    assert any(
        i.code == "label_outside_assigned_room" for i in full
    ), [(i.code, i.message) for i in full]

    # Quick check must not flag it (room still exists, so the cheap
    # "room missing" branch doesn't apply either).
    quick = revalidate_calibration(cal, quick=True)
    assert not any(
        i.code == "label_outside_assigned_room" for i in quick
    ), [(i.code, i.message) for i in quick]


def test_revalidate_quick_still_flags_label_referencing_deleted_room(tmp_path: Path):
    """quick=True must still catch labels whose room_id no longer exists.
    That sub-case of label_outside_assigned_room is free to check (just
    a dict lookup) and is the exact thing a user can produce by
    deleting a room mid-edit, so it should still surface live.
    """
    cal = _two_room_calibration(tmp_path)
    cal.rooms = [r for r in cal.rooms if r.id != 1]

    quick = revalidate_calibration(cal, quick=True)
    msgs = [
        i.message
        for i in quick
        if i.code == "label_outside_assigned_room"
    ]
    assert any("no longer exists" in m for m in msgs), msgs


def test_revalidate_quick_still_flags_orphan_label(tmp_path: Path):
    """quick=True should still catch the most common live-editing
    issue: an unassigned label."""
    from dataclasses import replace as dc_replace

    cal = _two_room_calibration(tmp_path)
    cal.labels[0] = dc_replace(cal.labels[0], room_id=None)

    quick = revalidate_calibration(cal, quick=True)
    assert any(i.code == "orphan_label" for i in quick), [
        (i.code, i.message) for i in quick
    ]


def test_revalidate_quick_still_flags_orphan_room(tmp_path: Path):
    """quick=True should still catch a room with no labels."""
    cal = _two_room_calibration(tmp_path)
    # Delete both labels so both rooms become orphans.
    cal.labels = []

    quick = revalidate_calibration(cal, quick=True)
    orphan_codes = [i.code for i in quick if i.code == "orphan_room"]
    assert len(orphan_codes) == len(cal.rooms), [
        (i.code, i.message) for i in quick
    ]

