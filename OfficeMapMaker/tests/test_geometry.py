"""Tests for ``officemapmaker.geometry`` (pure geometry helpers)."""

from __future__ import annotations

import numpy as np
import pytest

from officemapmaker.geometry import (
    bbox_area,
    bbox_center,
    bbox_contains_bbox,
    bbox_contains_point,
    bbox_intersects,
    expand_bbox,
    find_connected_components,
    mask_area,
    mask_bbox,
    mask_centroid,
    mask_contains_point,
    mask_to_rle,
    rle_to_mask,
)


# ---------------------------------------------------------------------------
# Bounding-box geometry
# ---------------------------------------------------------------------------


def test_bbox_center_of_simple_box() -> None:
    assert bbox_center((10, 20, 30, 40)) == (25, 40)


def test_bbox_contains_point_inclusive_top_left_exclusive_bottom_right() -> None:
    bb = (10, 10, 20, 20)
    assert bbox_contains_point(bb, (10, 10))
    assert bbox_contains_point(bb, (29, 29))
    assert not bbox_contains_point(bb, (30, 29))
    assert not bbox_contains_point(bb, (29, 30))
    assert not bbox_contains_point(bb, (9, 10))


def test_bbox_contains_bbox() -> None:
    outer = (0, 0, 100, 100)
    assert bbox_contains_bbox(outer, (10, 10, 50, 50))
    assert bbox_contains_bbox(outer, (0, 0, 100, 100))  # equal == contained
    assert not bbox_contains_bbox(outer, (50, 50, 60, 60))  # extends past right
    assert not bbox_contains_bbox(outer, (-1, 0, 50, 50))   # negative origin


def test_bbox_intersects() -> None:
    a = (0, 0, 10, 10)
    assert bbox_intersects(a, (5, 5, 10, 10))
    assert bbox_intersects(a, (9, 9, 1, 1))
    assert not bbox_intersects(a, (10, 0, 5, 5))   # touching == not intersecting
    assert not bbox_intersects(a, (-5, -5, 5, 5))


def test_bbox_area_and_expand() -> None:
    assert bbox_area((0, 0, 10, 20)) == 200
    assert expand_bbox((10, 10, 5, 5), margin=2) == (8, 8, 9, 9)


# ---------------------------------------------------------------------------
# Mask helpers
# ---------------------------------------------------------------------------


def _make_mask(shape: tuple[int, int], filled_rect: tuple[int, int, int, int]) -> np.ndarray:
    m = np.zeros(shape, dtype=np.uint8)
    x, y, w, h = filled_rect
    m[y:y + h, x:x + w] = 255
    return m


def test_mask_contains_point_respects_image_bounds() -> None:
    m = _make_mask((50, 50), (10, 10, 20, 20))
    assert mask_contains_point(m, (15, 15))
    assert not mask_contains_point(m, (5, 5))     # outside the rect
    assert not mask_contains_point(m, (-1, 15))   # outside the image
    assert not mask_contains_point(m, (50, 50))   # past the image edge


def test_mask_area_and_centroid_and_bbox() -> None:
    m = _make_mask((100, 100), (10, 20, 30, 40))
    assert mask_area(m) == 30 * 40
    # Pixels span x in [10, 39] (mean 24.5 -> 24 via banker's rounding)
    # and y in [20, 59] (mean 39.5 -> 40 via banker's rounding).
    assert mask_centroid(m) == (24, 40)
    assert mask_bbox(m) == (10, 20, 30, 40)


def test_empty_mask_helpers_return_none() -> None:
    empty = np.zeros((10, 10), dtype=np.uint8)
    assert mask_centroid(empty) is None
    assert mask_bbox(empty) is None
    assert mask_area(empty) == 0


# ---------------------------------------------------------------------------
# Connected components
# ---------------------------------------------------------------------------


def test_find_connected_components_two_rectangles() -> None:
    """A mask with two disjoint rectangles yields two components."""
    m = np.zeros((100, 100), dtype=np.uint8)
    m[10:30, 10:30] = 255   # 20x20 rectangle, area 400
    m[60:80, 50:90] = 255   # 20x40 rectangle, area 800

    ccs = find_connected_components(m, discard_largest=False)

    # Two real components.
    assert len(ccs) == 2

    # Sorted by descending area.
    assert ccs[0].area_px == 800
    assert ccs[1].area_px == 400

    # Bounding boxes match exactly.
    assert ccs[0].bbox == (50, 60, 40, 20)
    assert ccs[1].bbox == (10, 10, 20, 20)

    # Each per-component mask isolates only that component.
    assert mask_area(ccs[0].mask) == 800
    assert mask_area(ccs[1].mask) == 400


def test_find_connected_components_discards_largest() -> None:
    """``discard_largest=True`` drops the giant background-of-the-floor CC."""
    m = np.zeros((100, 100), dtype=np.uint8)
    m[:, :] = 255                  # the "everything" cc, area 10000
    m[40:60, 40:60] = 0            # a small cutout (background within)
    m_inv = (255 - m).astype(np.uint8)
    m_inv[:5, :5] = 255            # tiny extra component, area 25

    ccs = find_connected_components(m_inv, discard_largest=True)

    # The 20x20 cutout (area 400) is largest and would be dropped; the tiny
    # 5x5 (area 25) remains.
    assert len(ccs) == 1
    assert ccs[0].area_px == 25


def test_find_connected_components_min_area_filters_noise() -> None:
    m = np.zeros((50, 50), dtype=np.uint8)
    m[0, 0] = 255                  # area-1 speck
    m[10:20, 10:20] = 255          # 10x10, area 100

    ccs = find_connected_components(m, min_area=10, discard_largest=False)

    assert len(ccs) == 1
    assert ccs[0].area_px == 100


def test_find_connected_components_rejects_wrong_dtype() -> None:
    bad = np.zeros((5, 5), dtype=np.float32)
    with pytest.raises(ValueError, match="uint8"):
        find_connected_components(bad)


def test_find_connected_components_rejects_3d() -> None:
    bad = np.zeros((5, 5, 3), dtype=np.uint8)
    with pytest.raises(ValueError, match="2-D"):
        find_connected_components(bad)


# ---------------------------------------------------------------------------
# RLE round-trip
# ---------------------------------------------------------------------------


def test_rle_round_trips_empty_mask() -> None:
    m = np.zeros((30, 40), dtype=np.uint8)
    assert np.array_equal(rle_to_mask(mask_to_rle(m)), m)


def test_rle_round_trips_full_mask() -> None:
    m = np.full((30, 40), 255, dtype=np.uint8)
    assert np.array_equal(rle_to_mask(mask_to_rle(m)), m)


def test_rle_round_trips_random_mask() -> None:
    rng = np.random.default_rng(42)
    m = (rng.integers(0, 2, size=(123, 87)) * 255).astype(np.uint8)
    assert np.array_equal(rle_to_mask(mask_to_rle(m)), m)


def test_rle_preserves_shape() -> None:
    m = np.zeros((17, 33), dtype=np.uint8)
    m[5:10, 7:12] = 255
    out = rle_to_mask(mask_to_rle(m))
    assert out.shape == (17, 33)
    assert mask_area(out) == 25


def test_rle_compression_is_actually_compact() -> None:
    """A mostly-empty 1000x1000 mask should serialize to far less than 125 KB."""
    m = np.zeros((1000, 1000), dtype=np.uint8)
    m[10:20, 10:20] = 255
    rle = mask_to_rle(m)
    # Raw bit-packed: 125000 bytes. Base64-zlib of a near-empty mask should be tiny.
    assert len(rle) < 2000


def test_rle_rejects_malformed_input() -> None:
    with pytest.raises(ValueError, match="delimiter"):
        rle_to_mask("not-an-rle-string")
    with pytest.raises(ValueError, match="shape header"):
        rle_to_mask("not-a-shape:AAAA")


def test_mask_to_rle_rejects_3d() -> None:
    with pytest.raises(ValueError, match="2-D"):
        mask_to_rle(np.zeros((5, 5, 3), dtype=np.uint8))
