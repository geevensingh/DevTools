"""Tests for the ``officemapmaker.calibration`` data model."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from officemapmaker.calibration import (
    Calibration,
    CalibrationFormatError,
    Classification,
    Label,
    RenderDefaults,
    Room,
    compute_map_hash,
    load_calibration,
    save_calibration,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


def _sample_calibration() -> Calibration:
    return Calibration(
        map_image="map.png",
        map_hash="sha256:" + "0" * 64,
        labels=[
            Label(
                id="1480",
                bbox=(10, 20, 30, 12),
                room_id=1,
                classification=Classification.OFFICE,
                fill_seed=(25, 26),
                ocr_confidence=0.92,
                notes="",
            ),
            Label(
                id="1479A",
                bbox=(60, 20, 36, 12),
                room_id=2,
                classification=Classification.OFFICE,
                fill_seed=(78, 26),
                ocr_confidence=0.81,
                notes="touched up by hand",
            ),
            Label(
                id="HALL-N",
                bbox=(100, 100, 60, 12),
                room_id=3,
                classification=Classification.HALLWAY,
                fill_seed=(130, 106),
                ocr_confidence=0.55,
            ),
        ],
        rooms=[
            Room(id=1, polygon_rle="10x10:eJxjYGAAAAADAAE=", area_px=100, bbox=(0, 0, 10, 10)),
            Room(id=2, polygon_rle="10x10:eJxjYGAAAAADAAE=", area_px=120, bbox=(11, 0, 10, 10)),
            Room(id=3, polygon_rle="10x10:eJxjYGAAAAADAAE=", area_px=200, bbox=(0, 11, 30, 10)),
        ],
        wall_patches=[(612, 940, 612, 968), (245, 100, 245, 130)],
    )


# ---------------------------------------------------------------------------
# Round-trip serialization
# ---------------------------------------------------------------------------


def test_calibration_round_trips_through_json(tmp_path: Path) -> None:
    original = _sample_calibration()
    path = tmp_path / "calibration.json"
    save_calibration(original, path)
    reloaded = load_calibration(path)
    assert reloaded == original


def test_saved_calibration_is_human_editable_json(tmp_path: Path) -> None:
    """File should be 2-space indented and valid plain JSON (no jsonc tricks)."""
    path = tmp_path / "calibration.json"
    save_calibration(_sample_calibration(), path)
    text = path.read_text(encoding="utf-8")
    # 2-space indent is visible
    assert '\n  "map_image"' in text
    # Trailing newline for nice diffs
    assert text.endswith("\n")
    # Valid JSON
    parsed = json.loads(text)
    assert parsed["labels"][0]["id"] == "1480"


def test_classification_serializes_as_plain_string(tmp_path: Path) -> None:
    path = tmp_path / "calibration.json"
    save_calibration(_sample_calibration(), path)
    parsed = json.loads(path.read_text(encoding="utf-8"))
    assert parsed["labels"][0]["classification"] == "office"
    assert parsed["labels"][2]["classification"] == "hallway"


def test_tuples_are_serialized_as_lists(tmp_path: Path) -> None:
    path = tmp_path / "calibration.json"
    save_calibration(_sample_calibration(), path)
    parsed = json.loads(path.read_text(encoding="utf-8"))
    assert parsed["labels"][0]["bbox"] == [10, 20, 30, 12]
    assert parsed["labels"][0]["fill_seed"] == [25, 26]
    assert parsed["wall_patches"][0] == [612, 940, 612, 968]


# ---------------------------------------------------------------------------
# Convenience lookups
# ---------------------------------------------------------------------------


def test_label_by_id_and_room_by_id() -> None:
    c = _sample_calibration()
    assert c.label_by_id("1479A") is not None and c.label_by_id("1479A").id == "1479A"
    assert c.label_by_id("does-not-exist") is None
    assert c.room_by_id(2) is not None and c.room_by_id(2).area_px == 120
    assert c.room_by_id(99) is None


def test_labels_for_room_returns_matches() -> None:
    c = _sample_calibration()
    # Add a second label to room 1 for the test
    c.labels.append(
        Label(
            id="1480-alt",
            bbox=(0, 0, 1, 1),
            room_id=1,
            classification=Classification.OFFICE,
            fill_seed=(0, 0),
            ocr_confidence=0.1,
        )
    )
    matches = c.labels_for_room(1)
    assert {lab.id for lab in matches} == {"1480", "1480-alt"}
    assert c.labels_for_room(99) == []


def test_office_labels_excludes_hallway_and_common() -> None:
    c = _sample_calibration()
    ids = [lab.id for lab in c.office_labels()]
    assert ids == ["1480", "1479A"]


# ---------------------------------------------------------------------------
# RenderDefaults
# ---------------------------------------------------------------------------


def test_render_defaults_tolerate_missing_fields() -> None:
    """Calibrations from older tool versions may lack some render_defaults keys."""
    rd = RenderDefaults.from_dict({"min_font_pt": 9})  # only one key supplied
    assert rd.min_font_pt == 9
    assert rd.preferred_font == "Segoe UI"  # falls back to default
    assert rd.tile_dpi == 150


def test_render_defaults_round_trip() -> None:
    rd = RenderDefaults(
        min_font_pt=8,
        preferred_font="Arial",
        tile_dpi=200,
        tile_paper="a4",
        tile_overlap_in=0.5,
        legend_corner="top-left",
    )
    assert RenderDefaults.from_dict(rd.to_dict()) == rd


# ---------------------------------------------------------------------------
# Error handling
# ---------------------------------------------------------------------------


def test_load_calibration_missing_file_raises() -> None:
    with pytest.raises(CalibrationFormatError, match="not found"):
        load_calibration(Path("does-not-exist.json"))


def test_load_calibration_malformed_json_raises(tmp_path: Path) -> None:
    p = tmp_path / "bad.json"
    p.write_text("not valid json {{", encoding="utf-8")
    with pytest.raises(CalibrationFormatError, match="not valid JSON"):
        load_calibration(p)


def test_load_calibration_non_object_root_raises(tmp_path: Path) -> None:
    p = tmp_path / "list.json"
    p.write_text("[1, 2, 3]", encoding="utf-8")
    with pytest.raises(CalibrationFormatError, match="JSON object"):
        load_calibration(p)


def test_load_calibration_missing_required_key_raises(tmp_path: Path) -> None:
    p = tmp_path / "missing.json"
    p.write_text(json.dumps({"map_image": "m.png"}), encoding="utf-8")  # no map_hash
    with pytest.raises(CalibrationFormatError, match="map_hash"):
        load_calibration(p)


def test_invalid_label_classification_raises(tmp_path: Path) -> None:
    p = tmp_path / "bad-class.json"
    raw = _sample_calibration().to_dict()
    raw["labels"][0]["classification"] = "garage"  # not a valid enum value
    p.write_text(json.dumps(raw), encoding="utf-8")
    with pytest.raises(CalibrationFormatError, match="invalid Label"):
        load_calibration(p)


def test_orphan_label_room_id_can_be_null(tmp_path: Path) -> None:
    """A label with no enclosing room (orphan) must round-trip with room_id=None."""
    cal = _sample_calibration()
    cal.labels.append(
        Label(
            id="OUTSIDE",
            bbox=(0, 0, 10, 10),
            room_id=None,
            classification=Classification.SKIP,
            fill_seed=(5, 5),
            ocr_confidence=0.3,
        )
    )
    p = tmp_path / "calibration.json"
    save_calibration(cal, p)
    reloaded = load_calibration(p)
    assert reloaded.label_by_id("OUTSIDE").room_id is None


# ---------------------------------------------------------------------------
# compute_map_hash
# ---------------------------------------------------------------------------


def test_compute_map_hash_is_deterministic(tmp_path: Path) -> None:
    p = tmp_path / "fake-map.png"
    p.write_bytes(b"PNG" * 1000)
    h1 = compute_map_hash(p)
    h2 = compute_map_hash(p)
    assert h1 == h2
    assert h1.startswith("sha256:")
    assert len(h1) == len("sha256:") + 64  # 64 hex chars


def test_compute_map_hash_detects_content_change(tmp_path: Path) -> None:
    p = tmp_path / "map.png"
    p.write_bytes(b"original")
    h1 = compute_map_hash(p)
    p.write_bytes(b"changed")
    h2 = compute_map_hash(p)
    assert h1 != h2


def test_compute_map_hash_known_value(tmp_path: Path) -> None:
    """Cross-check against a known SHA-256 of a fixed payload."""
    p = tmp_path / "x"
    p.write_bytes(b"hello")
    # sha256("hello") = 2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824
    assert compute_map_hash(p) == "sha256:2cf24dba5fb0a30e26e83b2ac5b9e29e1b161e5c1fa7425e73043362938b9824"
