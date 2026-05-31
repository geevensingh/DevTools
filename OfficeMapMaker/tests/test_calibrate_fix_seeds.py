"""Tests for ``calibrate fix-seeds`` (CLI + handler).

The fix-seeds command heals fill_seed values that landed on walls (a real
bug surfaced when running validate fill on the Millennium B sample: the
geometric centroid frequently lands on the OCR'd label glyphs themselves,
so the flood-fill produces 0 pixels).

These tests build tiny synthetic floor plans the same way test_validate_fill
does, so we don't need to run the real OCR pipeline.
"""

from __future__ import annotations

from pathlib import Path

import cv2
import numpy as np

from officemapmaker.__main__ import main
from officemapmaker.calibration import (
    Calibration,
    Label,
    RenderDefaults,
    Room,
    load_calibration,
    save_calibration,
)
from officemapmaker.geometry import mask_to_rle


# ---------------------------------------------------------------------------
# Fixture helpers (kept local — copied from test_validate_fill for clarity)
# ---------------------------------------------------------------------------


def _white_canvas(h: int = 200, w: int = 300) -> np.ndarray:
    return np.full((h, w, 3), 255, dtype=np.uint8)


def _draw_box(img: np.ndarray, x: int, y: int, w: int, h: int, *, thickness: int = 3) -> None:
    cv2.rectangle(img, (x, y), (x + w, y + h), (0, 0, 0), thickness=thickness)


def _room_for_box(
    rid: int,
    x: int,
    y: int,
    box_w: int,
    box_h: int,
    *,
    img_h: int,
    img_w: int,
    thickness: int = 3,
) -> Room:
    mask = np.zeros((img_h, img_w), dtype=np.uint8)
    interior_x1 = x + thickness
    interior_y1 = y + thickness
    interior_x2 = x + box_w - thickness
    interior_y2 = y + box_h - thickness
    mask[interior_y1:interior_y2, interior_x1:interior_x2] = 255
    area = int((mask > 0).sum())
    return Room(
        id=rid,
        polygon_rle=mask_to_rle(mask > 0),
        area_px=area,
        bbox=(
            interior_x1,
            interior_y1,
            interior_x2 - interior_x1,
            interior_y2 - interior_y1,
        ),
    )


def _office_label(label_id: str, room_id: int, fill_seed: tuple[int, int]) -> Label:
    sx, sy = fill_seed
    return Label(
        id=label_id,
        bbox=(sx - 12, sy - 6, 24, 12),
        room_id=room_id,
        fill_seed=fill_seed,
        ocr_confidence=0.9,
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


def _setup_one_room_with_glyph(
    tmp_path: Path, *, broken_seed: bool
) -> tuple[Path, Path, tuple[int, int, int, int]]:
    """Write a 1-room floor plan with a "glyph" (a small black blob) blocking
    the centroid, plus calibration.json. Returns (cal_path, map_path,
    glyph_bbox).

    If ``broken_seed`` is True, the label's fill_seed points at the centre
    of the glyph (on a wall). If False, it points at a safe interior pixel.

    We paint an explicit 8x4 black rectangle for the glyph (rather than
    using ``cv2.putText``) because text-rendering produces font-dependent
    output where many pixels inside the "1234" bounding box are still white;
    we need a guaranteed-dark pixel under the broken-seed coordinate.
    """
    img = _white_canvas(200, 300)
    _draw_box(img, 50, 40, 200, 120, thickness=4)
    # Solid black blob centred at (150, 100): an 8-wide × 4-tall black
    # rectangle so the seed coord (150, 100) is unambiguously on "ink".
    glyph_x, glyph_y, glyph_w, glyph_h = 146, 98, 8, 4
    cv2.rectangle(
        img,
        (glyph_x, glyph_y),
        (glyph_x + glyph_w, glyph_y + glyph_h),
        (0, 0, 0),
        thickness=-1,
    )
    glyph_bbox = (glyph_x, glyph_y, glyph_w, glyph_h)

    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), img)

    rooms = [_room_for_box(1, 50, 40, 200, 120, img_h=200, img_w=300, thickness=4)]
    if broken_seed:
        seed = (150, 100)  # inside the black blob
    else:
        seed = (70, 50)  # safe interior corner
    labels = [_office_label("1234", 1, seed)]

    cal = _build_cal(labels, rooms)
    cal_path = tmp_path / "calibration.json"
    save_calibration(cal, cal_path)
    return cal_path, map_path, glyph_bbox


# ---------------------------------------------------------------------------
# Handler / CLI integration
# ---------------------------------------------------------------------------


def test_fix_seeds_heals_seed_on_glyph(tmp_path: Path) -> None:
    cal_path, map_path, _ = _setup_one_room_with_glyph(tmp_path, broken_seed=True)
    pre = load_calibration(cal_path)
    assert pre.labels[0].fill_seed == (150, 100)

    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(cal_path),
                 "--map", str(map_path)])
    assert code == 0

    post = load_calibration(cal_path)
    new_seed = post.labels[0].fill_seed
    assert new_seed != (150, 100), "seed should have been moved off the glyph"
    # New seed should be inside the room interior (not on a wall).
    img = cv2.imread(str(map_path), cv2.IMREAD_GRAYSCALE)
    assert img[new_seed[1], new_seed[0]] == 255, (
        f"new seed {new_seed} should land on a white interior pixel, "
        f"got grey={img[new_seed[1], new_seed[0]]}"
    )


def test_fix_seeds_leaves_already_good_seed_alone(tmp_path: Path) -> None:
    cal_path, map_path, _ = _setup_one_room_with_glyph(tmp_path, broken_seed=False)
    pre = load_calibration(cal_path)
    pre_seed = pre.labels[0].fill_seed

    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(cal_path),
                 "--map", str(map_path)])
    assert code == 0

    post = load_calibration(cal_path)
    assert post.labels[0].fill_seed == pre_seed, (
        f"unchanged seed shouldn't have moved: {pre_seed} -> {post.labels[0].fill_seed}"
    )


def test_fix_seeds_creates_bak_when_changes_made(tmp_path: Path) -> None:
    cal_path, map_path, _ = _setup_one_room_with_glyph(tmp_path, broken_seed=True)

    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(cal_path),
                 "--map", str(map_path)])
    assert code == 0
    bak = cal_path.with_name(cal_path.name + ".bak")
    assert bak.exists(), "expected a .bak alongside the edited calibration.json"


def test_fix_seeds_no_bak_when_nothing_changed(tmp_path: Path) -> None:
    cal_path, map_path, _ = _setup_one_room_with_glyph(tmp_path, broken_seed=False)

    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(cal_path),
                 "--map", str(map_path)])
    assert code == 0
    bak = cal_path.with_name(cal_path.name + ".bak")
    assert not bak.exists(), "no .bak should be written when no seeds were healed"


def test_fix_seeds_dry_run_does_not_modify_file(tmp_path: Path) -> None:
    cal_path, map_path, _ = _setup_one_room_with_glyph(tmp_path, broken_seed=True)
    original_bytes = cal_path.read_bytes()

    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(cal_path),
                 "--map", str(map_path),
                 "--dry-run"])
    assert code == 0
    assert cal_path.read_bytes() == original_bytes, (
        "dry-run shouldn't write calibration.json"
    )
    bak = cal_path.with_name(cal_path.name + ".bak")
    assert not bak.exists()


def test_fix_seeds_uses_recorded_map_image_when_no_flag(tmp_path: Path) -> None:
    cal_path, map_path, _ = _setup_one_room_with_glyph(tmp_path, broken_seed=True)
    # cal_path is tmp_path/calibration.json; map_image='map.png' next to it.
    code = main(["calibrate", "fix-seeds", "--calibration", str(cal_path)])
    assert code == 0
    post = load_calibration(cal_path)
    assert post.labels[0].fill_seed != (150, 100)


def test_fix_seeds_skips_orphan_labels(tmp_path: Path) -> None:
    """Labels with room_id=None should be untouched (we have no polygon to
    re-seed against).
    """
    img = _white_canvas(150, 200)
    _draw_box(img, 30, 30, 100, 80, thickness=3)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), img)

    orphan = Label(id="ORF", bbox=(0, 0, 10, 10), room_id=None,
                   fill_seed=(0, 0), ocr_confidence=0.5)
    cal = _build_cal(
        labels=[orphan],
        rooms=[_room_for_box(1, 30, 30, 100, 80, img_h=150, img_w=200, thickness=3)],
    )
    cal_path = tmp_path / "calibration.json"
    save_calibration(cal, cal_path)

    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(cal_path),
                 "--map", str(map_path)])
    assert code == 0
    post = load_calibration(cal_path)
    assert post.labels[0].fill_seed == (0, 0)


def test_fix_seeds_errors_when_calibration_missing(tmp_path: Path) -> None:
    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(tmp_path / "nope.json"),
                 "--map", str(tmp_path / "nope.png")])
    assert code == 2


def test_fix_seeds_errors_when_map_missing(tmp_path: Path) -> None:
    cal = _build_cal(labels=[], rooms=[])
    cal_path = tmp_path / "calibration.json"
    save_calibration(cal, cal_path)
    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(cal_path),
                 "--map", str(tmp_path / "missing.png")])
    assert code == 2


def test_fix_seeds_threshold_flag_controls_what_counts_as_broken(tmp_path: Path) -> None:
    """With a very-low threshold (1%), a seed that fills only a small chunk
    is still considered 'healthy enough'; with the default (30%), it gets
    healed. We use a partially-blocked seed scenario to make this concrete.

    The simplest test: a seed inside a glyph (fill = 0 pixels) should be
    healed at any positive threshold; we just verify the flag is accepted
    and the threshold influences the broken-count summary indirectly.
    """
    cal_path, map_path, _ = _setup_one_room_with_glyph(tmp_path, broken_seed=True)
    # With threshold=0.0, the seed (fill=0) ties — but the rule is
    # filled_area >= threshold * area => 0 >= 0 => True => unchanged.
    code = main(["calibrate", "fix-seeds",
                 "--calibration", str(cal_path),
                 "--map", str(map_path),
                 "--threshold", "0.0"])
    assert code == 0
    post = load_calibration(cal_path)
    assert post.labels[0].fill_seed == (150, 100), (
        "at threshold=0, even a zero-fill seed is 'good enough'"
    )
