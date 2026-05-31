"""Tests for ``officemapmaker.validate`` (pass 2: flood-fill leak detection).

The Pass-2 validator needs a real raster floor plan (because it binarizes the
image and flood-fills it). We synthesize tiny floor plans on the fly with
``numpy`` + ``cv2``, write them as PNGs, and craft matching ``Calibration``
fixtures whose ``polygon_rle`` reflects what the calibration would have
produced for those plans. This lets us assert specific leak codes without
running the OCR/CC pipeline end-to-end.
"""

from __future__ import annotations

from pathlib import Path

import cv2
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
from officemapmaker.validate import (
    FillLeak,
    build_fill_mask,
    render_leak_overlay_png,
    render_rooms_overview_png,
    validate_fill,
    virtual_flood_fill,
)


# ---------------------------------------------------------------------------
# Fixture builders
# ---------------------------------------------------------------------------


def _white_canvas(h: int = 300, w: int = 400) -> np.ndarray:
    """Return a 3-channel BGR canvas, all white (interior)."""
    return np.full((h, w, 3), 255, dtype=np.uint8)


def _draw_box(img: np.ndarray, x: int, y: int, w: int, h: int, *, thickness: int = 3) -> None:
    """Draw a black rectangle outline (a room) on the image."""
    cv2.rectangle(img, (x, y), (x + w, y + h), (0, 0, 0), thickness=thickness)


def _office_label(
    label_id: str,
    room_id: int,
    fill_seed: tuple[int, int],
    *,
    bbox: tuple[int, int, int, int] | None = None,
    conf: float = 0.9,
) -> Label:
    if bbox is None:
        sx, sy = fill_seed
        bbox = (sx - 12, sy - 6, 24, 12)
    return Label(
        id=label_id,
        bbox=bbox,
        room_id=room_id,
        fill_seed=fill_seed,
        ocr_confidence=conf,
    )


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
    """Build a Room whose polygon mirrors what the calibration would compute
    for the interior of a rectangle outlined at ``(x, y, w, h)`` with the
    given wall thickness. The interior is the closed area inside the walls.
    """
    mask = np.zeros((img_h, img_w), dtype=np.uint8)
    # Interior of the rectangle: from just inside the top-left wall to just
    # inside the bottom-right wall.
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


def _build_cal(labels: list[Label], rooms: list[Room], *, wall_patches=None) -> Calibration:
    return Calibration(
        map_image="map.png",
        map_hash="sha256:dummy",
        labels=labels,
        rooms=rooms,
        wall_patches=list(wall_patches or []),
        render_defaults=RenderDefaults(),
    )


def _codes(leaks: list[FillLeak]) -> list[str]:
    return [l.code for l in leaks]


# ---------------------------------------------------------------------------
# virtual_flood_fill primitive
# ---------------------------------------------------------------------------


def test_virtual_flood_fill_inside_closed_box_does_not_escape():
    canvas = _white_canvas(120, 160)
    _draw_box(canvas, 20, 20, 80, 60, thickness=4)
    wall_mask = build_fill_mask(canvas, wall_patches=[])

    filled = virtual_flood_fill(wall_mask, (60, 50))
    # Fill stayed inside the box: every filled pixel must be inside the box bounds.
    ys, xs = np.where(filled)
    assert xs.min() > 20 and xs.max() < 100
    assert ys.min() > 20 and ys.max() < 80


def test_virtual_flood_fill_returns_empty_when_seed_on_wall():
    canvas = _white_canvas(120, 160)
    _draw_box(canvas, 20, 20, 80, 60, thickness=4)
    wall_mask = build_fill_mask(canvas, wall_patches=[])

    # (20, 50) is on the left wall.
    filled = virtual_flood_fill(wall_mask, (20, 50))
    assert not filled.any()


def test_virtual_flood_fill_returns_empty_when_seed_off_image():
    canvas = _white_canvas(60, 60)
    wall_mask = build_fill_mask(canvas, wall_patches=[])
    assert not virtual_flood_fill(wall_mask, (-1, 10)).any()
    assert not virtual_flood_fill(wall_mask, (10, 999)).any()


def test_virtual_flood_fill_leaks_through_gap_without_patch():
    """A box with a 6-pixel gap on the right wall leaks into the outside."""
    canvas = _white_canvas(120, 200)
    _draw_box(canvas, 20, 20, 80, 60, thickness=4)
    # Punch a 6px hole in the right wall around y=50.
    cv2.rectangle(canvas, (96, 47), (104, 53), (255, 255, 255), thickness=-1)

    wall_mask = build_fill_mask(canvas, wall_patches=[])
    filled = virtual_flood_fill(wall_mask, (60, 50))
    # Filled region should extend well beyond x=100 (out the hole).
    xs = np.where(filled.any(axis=0))[0]
    assert xs.max() > 130, f"expected leak past x=130; got xmax={xs.max()}"


def test_wall_patches_close_the_gap():
    canvas = _white_canvas(120, 200)
    _draw_box(canvas, 20, 20, 80, 60, thickness=4)
    cv2.rectangle(canvas, (96, 47), (104, 53), (255, 255, 255), thickness=-1)

    wall_mask = build_fill_mask(canvas, wall_patches=[(100, 46, 100, 54)])
    filled = virtual_flood_fill(wall_mask, (60, 50))
    xs = np.where(filled.any(axis=0))[0]
    assert xs.max() < 110, "expected wall_patches to contain the fill"


# ---------------------------------------------------------------------------
# validate_fill — happy path
# ---------------------------------------------------------------------------


def test_validate_fill_clean_map_produces_no_leaks(tmp_path):
    h, w = 200, 320
    canvas = _white_canvas(h, w)
    _draw_box(canvas, 20, 20, 80, 80, thickness=4)
    _draw_box(canvas, 180, 20, 80, 80, thickness=4)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)

    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(60, 60)),
            _office_label("1481", room_id=2, fill_seed=(220, 60)),
        ],
        rooms=[
            _room_for_box(1, 20, 20, 80, 80, img_h=h, img_w=w),
            _room_for_box(2, 180, 20, 80, 80, img_h=h, img_w=w),
        ],
    )

    leaks = validate_fill(map_path, cal)
    assert leaks == [], f"expected no leaks; got {[str(l) for l in leaks]}"


def test_validate_fill_handles_calibration_with_no_office_labels(tmp_path):
    canvas = _white_canvas(100, 100)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)
    cal = _build_cal(labels=[], rooms=[])

    assert validate_fill(map_path, cal) == []


# ---------------------------------------------------------------------------
# validate_fill — leak detection
# ---------------------------------------------------------------------------


def test_validate_fill_detects_leak_into_other_office(tmp_path):
    """Two adjacent boxes share a wall with a gap → fill from #1 reaches #2's seed."""
    h, w = 200, 320
    canvas = _white_canvas(h, w)
    # Two boxes touching at x=100 sharing the wall there.
    _draw_box(canvas, 20, 20, 80, 80, thickness=4)
    _draw_box(canvas, 100, 20, 80, 80, thickness=4)
    # Punch a hole in the shared wall (around x=100, y=50).
    cv2.rectangle(canvas, (97, 48), (103, 56), (255, 255, 255), thickness=-1)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)

    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(60, 60)),
            _office_label("1481", room_id=2, fill_seed=(140, 60)),
        ],
        rooms=[
            _room_for_box(1, 20, 20, 80, 80, img_h=h, img_w=w),
            _room_for_box(2, 100, 20, 80, 80, img_h=h, img_w=w),
        ],
    )

    leaks = validate_fill(map_path, cal)
    codes = _codes(leaks)
    assert "leak_into_other_office" in codes, codes
    # Both directions are independent fills, so we'd expect a symmetric pair.
    pairs = {
        (l.office_id, l.leak_into_office_id)
        for l in leaks
        if l.code == "leak_into_other_office"
    }
    assert ("1480", "1481") in pairs or ("1481", "1480") in pairs


def test_validate_fill_suggested_patch_is_inside_image_bounds(tmp_path):
    h, w = 200, 320
    canvas = _white_canvas(h, w)
    _draw_box(canvas, 20, 20, 80, 80, thickness=4)
    _draw_box(canvas, 100, 20, 80, 80, thickness=4)
    cv2.rectangle(canvas, (97, 48), (103, 56), (255, 255, 255), thickness=-1)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)

    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(60, 60)),
            _office_label("1481", room_id=2, fill_seed=(140, 60)),
        ],
        rooms=[
            _room_for_box(1, 20, 20, 80, 80, img_h=h, img_w=w),
            _room_for_box(2, 100, 20, 80, 80, img_h=h, img_w=w),
        ],
    )
    leaks = validate_fill(map_path, cal)
    # At least one leak should carry a suggested_patch.
    patches = [l.suggested_patch for l in leaks if l.suggested_patch]
    assert patches, f"no suggested_patch on any leak; leaks were {leaks}"
    for x1, y1, x2, y2 in patches:
        assert 0 <= x1 < w and 0 <= x2 < w
        assert 0 <= y1 < h and 0 <= y2 < h


def test_validate_fill_wall_patches_make_leak_disappear(tmp_path):
    h, w = 200, 320
    canvas = _white_canvas(h, w)
    _draw_box(canvas, 20, 20, 80, 80, thickness=4)
    _draw_box(canvas, 100, 20, 80, 80, thickness=4)
    cv2.rectangle(canvas, (97, 48), (103, 56), (255, 255, 255), thickness=-1)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)

    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(60, 60)),
            _office_label("1481", room_id=2, fill_seed=(140, 60)),
        ],
        rooms=[
            _room_for_box(1, 20, 20, 80, 80, img_h=h, img_w=w),
            _room_for_box(2, 100, 20, 80, 80, img_h=h, img_w=w),
        ],
        # Close the gap with a vertical 1px wall segment.
        wall_patches=[(100, 46, 100, 58)],
    )
    leaks = validate_fill(map_path, cal)
    assert leaks == [], f"expected no leaks after patch; got {leaks}"


def test_validate_fill_warns_when_seed_on_wall(tmp_path):
    """If the calibration's fill_seed is itself on a wall, fill is empty → warn."""
    h, w = 120, 200
    canvas = _white_canvas(h, w)
    _draw_box(canvas, 20, 20, 80, 60, thickness=4)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)

    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(20, 50))],  # on left wall
        rooms=[_room_for_box(1, 20, 20, 80, 60, img_h=h, img_w=w)],
    )
    leaks = validate_fill(map_path, cal)
    assert any(l.code == "seed_on_wall" and l.severity == "warning" for l in leaks)


def test_validate_fill_errors_on_missing_map(tmp_path):
    cal = _build_cal(labels=[], rooms=[])
    with pytest.raises(FileNotFoundError):
        validate_fill(tmp_path / "does_not_exist.png", cal)


# ---------------------------------------------------------------------------
# Review-artifact rendering
# ---------------------------------------------------------------------------


def test_render_leak_overlay_png_writes_a_png(tmp_path):
    h, w = 200, 320
    canvas = _white_canvas(h, w)
    _draw_box(canvas, 20, 20, 80, 80, thickness=4)
    _draw_box(canvas, 100, 20, 80, 80, thickness=4)
    cv2.rectangle(canvas, (97, 48), (103, 56), (255, 255, 255), thickness=-1)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)

    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(60, 60)),
            _office_label("1481", room_id=2, fill_seed=(140, 60)),
        ],
        rooms=[
            _room_for_box(1, 20, 20, 80, 80, img_h=h, img_w=w),
            _room_for_box(2, 100, 20, 80, 80, img_h=h, img_w=w),
        ],
    )
    leaks = validate_fill(map_path, cal)
    assert leaks, "this fixture should produce at least one leak"

    out = tmp_path / "leak.png"
    render_leak_overlay_png(map_path, cal, leaks[0], out)
    assert out.exists() and out.stat().st_size > 200


def test_render_leak_overlay_raises_for_unknown_office(tmp_path):
    canvas = _white_canvas(60, 60)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)
    cal = _build_cal(labels=[], rooms=[])
    bogus = FillLeak(
        severity="warning",
        code="leak_oversized",
        office_id="999",
        room_id=1,
        message="x",
    )
    with pytest.raises(ValueError):
        render_leak_overlay_png(map_path, cal, bogus, tmp_path / "out.png")


def test_render_rooms_overview_writes_a_png(tmp_path):
    h, w = 200, 320
    canvas = _white_canvas(h, w)
    _draw_box(canvas, 20, 20, 80, 80)
    _draw_box(canvas, 180, 20, 80, 80)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)
    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(60, 60)),
            _office_label("1481", room_id=2, fill_seed=(220, 60)),
        ],
        rooms=[
            _room_for_box(1, 20, 20, 80, 80, img_h=h, img_w=w),
            _room_for_box(2, 180, 20, 80, 80, img_h=h, img_w=w),
        ],
    )
    out = tmp_path / "overview.png"
    render_rooms_overview_png(map_path, cal, out)
    assert out.exists() and out.stat().st_size > 200


def test_render_rooms_overview_handles_calibration_with_no_offices(tmp_path):
    canvas = _white_canvas(60, 60)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)
    cal = _build_cal(labels=[], rooms=[])
    out = tmp_path / "overview.png"
    render_rooms_overview_png(map_path, cal, out)
    assert out.exists()


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def _write_minimal_cal_and_map(tmp_path: Path):
    h, w = 200, 320
    canvas = _white_canvas(h, w)
    _draw_box(canvas, 20, 20, 80, 80)
    _draw_box(canvas, 180, 20, 80, 80)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), canvas)
    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(60, 60)),
            _office_label("1481", room_id=2, fill_seed=(220, 60)),
        ],
        rooms=[
            _room_for_box(1, 20, 20, 80, 80, img_h=h, img_w=w),
            _room_for_box(2, 180, 20, 80, 80, img_h=h, img_w=w),
        ],
    )
    cal_path = tmp_path / "calibration.json"
    save_calibration(cal, cal_path)
    return map_path, cal_path
