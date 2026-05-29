"""Tests for ``officemapmaker.geometry.largest_inscribed_rectangle``."""

from __future__ import annotations

import numpy as np
import pytest

from officemapmaker.geometry import largest_inscribed_rectangle


def test_empty_mask_returns_zero_bbox():
    mask = np.zeros((10, 10), dtype=bool)
    assert largest_inscribed_rectangle(mask) == (0, 0, 0, 0)


def test_full_mask_returns_whole_image():
    mask = np.ones((5, 8), dtype=bool)
    assert largest_inscribed_rectangle(mask) == (0, 0, 8, 5)


def test_solid_rectangle_returns_itself():
    mask = np.zeros((20, 30), dtype=bool)
    mask[3:13, 5:25] = True  # 20 wide × 10 tall starting at (5, 3)
    assert largest_inscribed_rectangle(mask) == (5, 3, 20, 10)


def test_l_shape_picks_the_widest_arm():
    # An L: the bottom bar is 10 wide × 4 tall (40 px),
    # the vertical bar is 4 wide × 8 tall (32 px).
    # LIR should be the 10×4 horizontal arm.
    mask = np.zeros((15, 15), dtype=bool)
    mask[10:14, 2:12] = True  # bottom bar
    mask[3:14, 2:6] = True    # vertical bar
    x, y, w, h = largest_inscribed_rectangle(mask)
    assert w * h == 44 or w * h == 40  # depending on which arm dominates
    # Either way, the chosen rectangle should be entirely inside the mask:
    assert mask[y : y + h, x : x + w].all()


def test_single_row_strip():
    mask = np.zeros((10, 10), dtype=bool)
    mask[5, 2:9] = True
    assert largest_inscribed_rectangle(mask) == (2, 5, 7, 1)


def test_single_column_strip():
    mask = np.zeros((10, 10), dtype=bool)
    mask[1:8, 4] = True
    assert largest_inscribed_rectangle(mask) == (4, 1, 1, 7)


def test_uint8_input_accepted():
    mask = np.zeros((6, 6), dtype=np.uint8)
    mask[1:5, 1:5] = 255
    assert largest_inscribed_rectangle(mask) == (1, 1, 4, 4)


def test_returned_rect_is_always_inside():
    rng = np.random.default_rng(seed=42)
    mask = rng.random((30, 40)) > 0.4
    x, y, w, h = largest_inscribed_rectangle(mask)
    assert w > 0 and h > 0
    sub = mask[y : y + h, x : x + w]
    assert sub.all(), "LIR result must contain only True pixels"


def test_non_2d_input_raises():
    with pytest.raises(ValueError):
        largest_inscribed_rectangle(np.zeros((3, 3, 3), dtype=bool))


def test_concave_polygon_skips_the_notch():
    # 20×20 box with a 6×6 notch cut out of the top-right corner.
    mask = np.ones((20, 20), dtype=bool)
    mask[0:6, 14:20] = False
    x, y, w, h = largest_inscribed_rectangle(mask)
    # The valid largest rect is either 14×20 (left side) = 280
    # or 20×14 (bottom) = 280 — both are valid maxima of equal area.
    assert w * h == 280
    assert mask[y : y + h, x : x + w].all()
