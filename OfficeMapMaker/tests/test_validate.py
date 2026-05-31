"""Tests for ``officemapmaker.validate`` (pass 1: labels vs assignments)."""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

from officemapmaker.calibration import (
    Calibration,
    Label,
    RenderDefaults,
    Room,
    save_calibration,
)
from officemapmaker.geometry import mask_to_rle
from officemapmaker.io_assignments import Assignment
from officemapmaker.validate import (
    ValidationIssue,
    render_validation_labels_review_png,
    validate_labels,
)


# ---------------------------------------------------------------------------
# Fixtures / helpers
# ---------------------------------------------------------------------------


def _square_room(rid: int, x: int, y: int, side: int, *, img_size=(800, 600)) -> Room:
    mask = np.zeros((img_size[1], img_size[0]), dtype=bool)
    mask[y : y + side, x : x + side] = True
    return Room(
        id=rid,
        polygon_rle=mask_to_rle(mask),
        area_px=int(mask.sum()),
        bbox=(x, y, side, side),
    )


def _office_label(label_id: str, room_id: int, bbox=(50, 50, 30, 14), conf=0.9) -> Label:
    return Label(
        id=label_id,
        bbox=bbox,
        room_id=room_id,
        fill_seed=(bbox[0] + bbox[2] // 2, bbox[1] + bbox[3] // 2),
        ocr_confidence=conf,
    )


def _build_cal(labels: list[Label], rooms: list[Room]) -> Calibration:
    return Calibration(
        map_image="map.png",
        map_hash="sha256:dummy",
        labels=labels,
        rooms=rooms,
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )


def _asn(name: str, office_id: str, team: str, row: int = 2) -> Assignment:
    return Assignment(name=name, office_id=office_id, team=team, source_row=row)


def _codes(issues: list[ValidationIssue]) -> set[str]:
    return {i.code for i in issues}


# ---------------------------------------------------------------------------
# Happy path
# ---------------------------------------------------------------------------


def test_clean_validation_produces_no_issues():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1), _office_label("1481", room_id=2)],
        rooms=[_square_room(1, 40, 40, 80), _square_room(2, 200, 40, 80)],
    )
    assignments = [
        _asn("Alice Smith", "1480", "BITS"),
        _asn("Bob Jones", "1481", "BITS"),
    ]
    issues = validate_labels(cal, assignments)
    assert issues == []


# ---------------------------------------------------------------------------
# Errors
# ---------------------------------------------------------------------------


def test_office_not_on_map_is_error():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1)],
        rooms=[_square_room(1, 40, 40, 80)],
    )
    assignments = [_asn("Alice", "9999", "BITS")]
    issues = validate_labels(cal, assignments)
    errors = [i for i in issues if i.severity == "error"]
    assert len(errors) == 1
    assert errors[0].code == "office_not_on_map"
    assert errors[0].person == "Alice"
    assert errors[0].office_id == "9999"


def test_ambiguous_office_id_is_error():
    cal = _build_cal(
        labels=[
            _office_label("1003", room_id=1),
            _office_label("1003", room_id=2),
        ],
        rooms=[_square_room(1, 40, 40, 80), _square_room(2, 200, 40, 80)],
    )
    assignments = [_asn("Alice", "1003", "BITS")]
    issues = validate_labels(cal, assignments)
    errors = [i for i in issues if i.severity == "error"]
    assert any(e.code == "ambiguous_office" for e in errors)


def test_lookup_is_case_insensitive():
    cal = _build_cal(
        labels=[_office_label("1479A", room_id=1)],
        rooms=[_square_room(1, 40, 40, 80)],
    )
    # Lowercase from spreadsheet should still match.
    assignments = [_asn("Alice", "1479a", "BITS")]
    issues = validate_labels(cal, assignments)
    assert not any(i.severity == "error" for i in issues)


# ---------------------------------------------------------------------------
# Warnings
# ---------------------------------------------------------------------------


def test_vacant_office_no_longer_warned():
    """Without classification, an unassigned label is just data — silent."""
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1), _office_label("1481", room_id=2)],
        rooms=[_square_room(1, 40, 40, 80), _square_room(2, 200, 40, 80)],
    )
    assignments = [_asn("Alice", "1480", "BITS")]  # 1481 unassigned
    issues = validate_labels(cal, assignments)
    assert not any(i.code == "vacant_office" for i in issues)


def test_low_confidence_without_match_emits_warning():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, conf=0.2)],
        rooms=[_square_room(1, 40, 40, 80)],
    )
    # No assignment for 1480.
    issues = validate_labels(cal, [])
    codes = _codes(issues)
    assert "low_confidence_no_match" in codes


def test_low_confidence_with_match_does_not_warn():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, conf=0.2)],
        rooms=[_square_room(1, 40, 40, 80)],
    )
    issues = validate_labels(cal, [_asn("Alice", "1480", "BITS")])
    codes = _codes(issues)
    assert "low_confidence_no_match" not in codes


def test_duplicate_row_warning():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1)],
        rooms=[_square_room(1, 40, 40, 80)],
    )
    assignments = [
        _asn("Alice", "1480", "BITS", row=2),
        _asn("Alice", "1480", "BITS", row=3),  # identical
    ]
    issues = validate_labels(cal, assignments)
    dup = [i for i in issues if i.code == "duplicate_row"]
    assert len(dup) == 1


def test_team_name_case_variants_warning():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1), _office_label("1481", room_id=2)],
        rooms=[_square_room(1, 40, 40, 80), _square_room(2, 200, 40, 80)],
    )
    assignments = [
        _asn("Alice", "1480", "Revenue"),
        _asn("Bob", "1481", "revenue"),
    ]
    issues = validate_labels(cal, assignments)
    variants = [i for i in issues if i.code == "team_name_variants"]
    assert len(variants) == 1
    assert "Revenue" in variants[0].message and "revenue" in variants[0].message


def test_team_name_with_trailing_whitespace_is_same():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1), _office_label("1481", room_id=2)],
        rooms=[_square_room(1, 40, 40, 80), _square_room(2, 200, 40, 80)],
    )
    # Loader strips whitespace already, so this test is precautionary —
    # the casefold().strip() in validate_labels should still treat them as one.
    assignments = [
        _asn("Alice", "1480", "BITS"),
        _asn("Bob", "1481", "BITS"),
    ]
    issues = validate_labels(cal, assignments)
    assert not any(i.code == "team_name_variants" for i in issues)


# ---------------------------------------------------------------------------
# Render PNG
# ---------------------------------------------------------------------------


def _make_map_png(path: Path, size: tuple[int, int] = (400, 300)) -> None:
    from PIL import Image, ImageDraw

    img = Image.new("RGB", size, "white")
    draw = ImageDraw.Draw(img)
    draw.rectangle((20, 20, size[0] - 20, size[1] - 20), outline="black", width=3)
    img.save(path)


def test_render_validation_labels_review_png_writes_a_png(tmp_path: Path):
    map_path = tmp_path / "map.png"
    _make_map_png(map_path)
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, bbox=(50, 50, 30, 14))],
        rooms=[_square_room(1, 40, 40, 80, img_size=(400, 300))],
    )
    issues = [
        ValidationIssue(
            severity="warning", code="low_confidence_no_match", message="x", office_id="1480"
        )
    ]
    out = tmp_path / "review.png"
    render_validation_labels_review_png(map_path, cal, issues, out)
    assert out.exists()
    assert out.read_bytes()[:8] == b"\x89PNG\r\n\x1a\n"


def test_render_validation_labels_review_handles_empty_issues(tmp_path: Path):
    map_path = tmp_path / "map.png"
    _make_map_png(map_path)
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, bbox=(50, 50, 30, 14))],
        rooms=[_square_room(1, 40, 40, 80, img_size=(400, 300))],
    )
    out = tmp_path / "review.png"
    render_validation_labels_review_png(map_path, cal, [], out)
    assert out.exists()


def test_render_validation_labels_review_missing_map_raises(tmp_path: Path):
    cal = _build_cal(labels=[], rooms=[])
    with pytest.raises(FileNotFoundError):
        render_validation_labels_review_png(
            tmp_path / "no.png", cal, [], tmp_path / "out.png"
        )


# ---------------------------------------------------------------------------
# CLI integration
# ---------------------------------------------------------------------------
