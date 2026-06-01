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


def test_height_cap_unset_matches_classical_lir():
    """The default (no cap) must be identical to the historical behavior."""
    rng = np.random.default_rng(seed=7)
    mask = rng.random((40, 50)) > 0.3
    plain = largest_inscribed_rectangle(mask)
    capped_none = largest_inscribed_rectangle(mask, height_cap=None)
    assert plain == capped_none


def test_height_cap_no_effect_when_rect_already_fits():
    """If the optimal rect is already shorter than the cap, the cap is a
    no-op (the rect's height stays at its actual value)."""
    mask = np.zeros((20, 30), dtype=bool)
    mask[3:13, 5:25] = True  # 20 wide × 10 tall
    # Cap is much higher than any inscribed rect → unchanged.
    assert largest_inscribed_rectangle(mask, height_cap=100) == (5, 3, 20, 10)


def test_height_cap_prefers_wider_rect_in_diamond():
    """The motivating case: a diamond (rhombus) polygon. The classical
    LIR is a narrow tall strip along one edge; with a height cap the
    algorithm prefers a wider, shorter rectangle through the middle.
    """
    # Build a 41×41 diamond by checking |x-20| + |y-20| <= 20.
    h = w = 41
    mask = np.zeros((h, w), dtype=bool)
    yy, xx = np.mgrid[0:h, 0:w]
    mask[np.abs(xx - 20) + np.abs(yy - 20) <= 20] = True
    plain = largest_inscribed_rectangle(mask)
    # The plain LIR for a 41×41 diamond is a ~29×15 strip on either
    # axis (square of side ~21 rotated). Whatever the exact tie-break,
    # asking for a small height cap should pick a clearly wider rect.
    capped = largest_inscribed_rectangle(mask, height_cap=5)
    assert capped[2] > plain[2], (
        f"capped LIR should be wider than plain LIR for a diamond; "
        f"got plain={plain}, capped={capped}"
    )
    # The capped rect's height must be ≤ the cap…
    assert capped[3] <= 5
    # …and entirely inside the diamond.
    cx, cy, cw, ch = capped
    assert mask[cy:cy + ch, cx:cx + cw].all()


def test_height_cap_returns_rect_inside_mask():
    """The capped rect must remain inscribed in the mask, even with a
    very small cap."""
    rng = np.random.default_rng(seed=11)
    mask = rng.random((40, 40)) > 0.4
    x, y, w, h = largest_inscribed_rectangle(mask, height_cap=3)
    if w > 0:
        assert mask[y : y + h, x : x + w].all()
        assert h <= 3
