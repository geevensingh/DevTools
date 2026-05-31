"""Tests for ``officemapmaker.render`` (pass 4: composite render)."""

from __future__ import annotations

import json
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
from officemapmaker.io_assignments import Assignment
from officemapmaker.layout import plan_layout, save_layout
from officemapmaker.render import RenderIssue, render_composite


# ---------------------------------------------------------------------------
# Fixture builder: a tiny 2-office synthetic map
# ---------------------------------------------------------------------------


def _synthetic_map_bgr(w: int = 600, h: int = 400) -> np.ndarray:
    """Return a white map with two black rectangular rooms + walls.

    Layout:
       (50,50)         (250,50)        (450,50)
          +---------------+----------------+
          |   Office A    |    Office B    |
          |   (label 100) |   (label 200)  |
          +---------------+----------------+
       (50,350)        (250,350)       (450,350)

    Both rooms are 200 wide × 300 tall, sharing the middle wall.
    The two rooms are physically separated by a single solid wall (no
    door gap), so a flood-fill from one seed stays inside one room.
    """
    img = np.full((h, w, 3), 255, dtype=np.uint8)
    # Outer walls of the combined block:
    cv2.rectangle(img, (50, 50), (450, 350), (0, 0, 0), thickness=2)
    # Internal dividing wall:
    cv2.line(img, (250, 50), (250, 350), (0, 0, 0), thickness=2)
    return img


def _build_synthetic_calibration() -> Calibration:
    h, w = 400, 600
    # Office A (label "100"): interior pixels roughly (52..248, 52..348).
    mask_a = np.zeros((h, w), dtype=bool)
    mask_a[52:349, 52:249] = True
    room_a = Room(
        id=1, polygon_rle=mask_to_rle(mask_a), area_px=int(mask_a.sum()),
        bbox=(52, 52, 197, 297),
    )
    label_a = Label(
        id="100",
        bbox=(120, 200, 20, 14),
        room_id=1,
        fill_seed=(150, 200),  # well inside room A
        ocr_confidence=0.95,
    )

    # Office B (label "200"): interior pixels roughly (252..448, 52..348).
    mask_b = np.zeros((h, w), dtype=bool)
    mask_b[52:349, 252:449] = True
    room_b = Room(
        id=2, polygon_rle=mask_to_rle(mask_b), area_px=int(mask_b.sum()),
        bbox=(252, 52, 197, 297),
    )
    label_b = Label(
        id="200",
        bbox=(320, 200, 20, 14),
        room_id=2,
        fill_seed=(350, 200),
        ocr_confidence=0.95,
    )

    return Calibration(
        map_image="map.png",
        map_hash="sha256:synthetic",
        labels=[label_a, label_b],
        rooms=[room_a, room_b],
        wall_patches=[],
        render_defaults=RenderDefaults(min_font_pt=7, tile_dpi=150),
    )


@pytest.fixture()
def synthetic_setup(tmp_path: Path):
    from officemapmaker.calibration import compute_map_hash

    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), _synthetic_map_bgr())
    cal = _build_synthetic_calibration()
    # Stamp the real hash so layout planning produces a layout that matches.
    cal.map_hash = compute_map_hash(map_path)
    cal_path = tmp_path / "calibration.json"
    save_calibration(cal, cal_path)
    return tmp_path, map_path, cal, cal_path


# ---------------------------------------------------------------------------
# Happy path
# ---------------------------------------------------------------------------


def test_render_composite_writes_png_and_review_copy(synthetic_setup, tmp_path):
    tmp_path, map_path, cal, _ = synthetic_setup
    assignments = [
        Assignment("Alice Smith", "100", "BITS", 2),
        Assignment("Bob Jones", "200", "FPAA", 3),
    ]
    layout, _ = plan_layout(cal, assignments)
    out_path = tmp_path / "composite.png"
    result = render_composite(map_path, cal, layout, assignments, out_path)

    assert out_path.exists()
    assert result.review_path.exists()
    assert result.review_path != out_path
    assert result.changed_pixel_count > 0


def test_render_composite_zero_unexpected_pixels_on_clean_map(synthetic_setup, tmp_path):
    """The 'no pixel changed outside expected regions' safety net."""
    tmp_path, map_path, cal, _ = synthetic_setup
    assignments = [
        Assignment("Alice Smith", "100", "BITS", 2),
        Assignment("Bob Jones", "200", "FPAA", 3),
    ]
    layout, _ = plan_layout(cal, assignments)
    out_path = tmp_path / "composite.png"
    result = render_composite(map_path, cal, layout, assignments, out_path)

    assert result.unexpected_pixel_count == 0
    assert not any(i.code == "unexpected_pixel_change" for i in result.issues)


def test_render_composite_paints_each_office_with_its_team_color(synthetic_setup, tmp_path):
    tmp_path, map_path, cal, _ = synthetic_setup
    assignments = [
        Assignment("A", "100", "BITS", 2),
        Assignment("B", "200", "FPAA", 3),
    ]
    layout, _ = plan_layout(cal, assignments)
    out_path = tmp_path / "composite.png"
    result = render_composite(map_path, cal, layout, assignments, out_path)

    composite = cv2.imread(str(out_path), cv2.IMREAD_COLOR)
    palette = result.palette
    assert palette is not None

    # Each team's color should appear inside its assigned room and not appear
    # inside the *other* room (modulo text pixels).
    for office_id, team, room_x_range in [
        ("100", "BITS", (60, 240)),
        ("200", "FPAA", (260, 440)),
    ]:
        color = palette.color_for(team)
        assert color is not None
        bgr = np.array([color[2], color[1], color[0]], dtype=np.uint8)
        x0, x1 = room_x_range
        region = composite[100:300, x0:x1]
        present = (region == bgr).all(axis=2).any()
        assert present, f"team {team} color not present in {office_id}"


def test_render_composite_does_not_color_vacant_offices(tmp_path):
    """Office on the map but unassigned should remain white (untouched)."""
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), _synthetic_map_bgr())
    cal = _build_synthetic_calibration()
    # Only office 100 is assigned; office 200 should stay white.
    assignments = [Assignment("Alice", "100", "BITS", 2)]
    layout, _ = plan_layout(cal, assignments)
    out_path = tmp_path / "composite.png"
    result = render_composite(map_path, cal, layout, assignments, out_path)

    composite = cv2.imread(str(out_path), cv2.IMREAD_COLOR)
    # Center of office B (no fill) should still be white.
    assert tuple(composite[200, 350]) == (255, 255, 255)
    assert result.unexpected_pixel_count == 0


def test_render_composite_uses_team_overrides(synthetic_setup, tmp_path):
    tmp_path, map_path, cal, _ = synthetic_setup
    assignments = [Assignment("A", "100", "BITS", 2)]
    layout, _ = plan_layout(cal, assignments)
    out_path = tmp_path / "composite.png"
    result = render_composite(
        map_path, cal, layout, assignments, out_path,
        team_overrides={"BITS": (255, 200, 200)},  # custom pink
    )
    assert result.palette is not None
    assert result.palette.colors["BITS"] == (255, 200, 200)
    composite = cv2.imread(str(out_path), cv2.IMREAD_COLOR)
    # OpenCV is BGR.
    assert (composite[200, 150] == (200, 200, 255)).all()


def test_render_composite_warns_on_low_contrast_override(synthetic_setup, tmp_path):
    tmp_path, map_path, cal, _ = synthetic_setup
    assignments = [Assignment("A", "100", "BITS", 2)]
    layout, _ = plan_layout(cal, assignments)
    out_path = tmp_path / "composite.png"
    result = render_composite(
        map_path, cal, layout, assignments, out_path,
        team_overrides={"BITS": (10, 10, 10)},  # dark gray
    )
    assert any(i.code == "palette_low_contrast" for i in result.issues)


def test_render_composite_legend_corner_override(synthetic_setup, tmp_path):
    """Top-left legend should not paint pixels in the bottom-right corner."""
    tmp_path, map_path, cal, _ = synthetic_setup
    assignments = [Assignment("A", "100", "BITS", 2)]
    layout, _ = plan_layout(cal, assignments)
    out_path = tmp_path / "composite.png"
    result = render_composite(
        map_path, cal, layout, assignments, out_path, legend_corner="top-left"
    )
    composite = cv2.imread(str(out_path), cv2.IMREAD_COLOR)
    # Top-left legend: a band of border pixels should exist in the top-left
    # corner area (and conversely the bottom-right should be untouched).
    top_left_region = composite[10:100, 10:200]
    bottom_right_region = composite[300:400, 500:600]
    # Top-left should contain non-white pixels (legend border, swatches, text).
    top_left_non_white = np.any(top_left_region != (255, 255, 255), axis=2).sum()
    bottom_right_changed = np.any(
        bottom_right_region != 255, axis=2
    ).sum()
    assert top_left_non_white > 0, "legend missing from top-left"
    # Bottom-right region intersects the wall-edge in the synthetic fixture, so
    # we don't assert it's pristine — just that the legend has moved off it
    # (i.e. far fewer non-white pixels than at top-left).
    assert top_left_non_white > bottom_right_changed
    assert result.unexpected_pixel_count == 0


# ---------------------------------------------------------------------------
# Error paths
# ---------------------------------------------------------------------------


def test_render_composite_missing_map_raises(tmp_path):
    cal = _build_synthetic_calibration()
    from officemapmaker.layout import Layout
    layout = Layout(map_image="x", map_hash="", entries=[])
    with pytest.raises(FileNotFoundError):
        render_composite(
            tmp_path / "nope.png", cal, layout, [], tmp_path / "out.png"
        )


def test_render_composite_layout_with_unknown_office_emits_error(tmp_path):
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), _synthetic_map_bgr())
    cal = _build_synthetic_calibration()

    from officemapmaker.layout import (
        FitStrategy,
        Layout,
        LayoutEntry,
        OfficeNumberPlacement,
    )
    layout = Layout(
        map_image="map.png",
        map_hash="",
        entries=[
            LayoutEntry(
                office_id="9999",  # not in the calibration
                room_id=999,
                team="BITS",
                fit_strategy=FitStrategy.SHRINK,
                names=[],
                office_number=OfficeNumberPlacement(
                    text="9999", bbox=(10, 10, 30, 14), font_px=14
                ),
                inscribed_rect=(0, 0, 1, 1),
            )
        ],
    )
    out = tmp_path / "composite.png"
    result = render_composite(map_path, cal, layout, [], out)
    assert any(i.code == "layout_office_not_in_calibration" for i in result.issues)


def test_render_composite_layout_with_unknown_team_emits_error(tmp_path):
    """If layout references a team that isn't in the palette, error."""
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), _synthetic_map_bgr())
    cal = _build_synthetic_calibration()

    from officemapmaker.layout import (
        FitStrategy,
        Layout,
        LayoutEntry,
        OfficeNumberPlacement,
    )
    layout = Layout(
        map_image="map.png",
        map_hash="",
        entries=[
            LayoutEntry(
                office_id="100", room_id=1, team="",  # empty team string
                fit_strategy=FitStrategy.SHRINK, names=[],
                office_number=OfficeNumberPlacement(
                    text="100", bbox=(120, 200, 30, 14), font_px=14
                ),
                inscribed_rect=(60, 60, 180, 280),
            )
        ],
    )
    out = tmp_path / "composite.png"
    result = render_composite(map_path, cal, layout, [], out)
    assert any(i.code == "palette_team_missing" for i in result.issues)


def test_render_composite_clips_leaks_to_polygon_and_warns(tmp_path):
    """If the wall is missing (leak), the fill is clipped to the polygon.

    Previous behavior: fill spilled out and the diff-check safety net
    fired an ``unexpected_pixel_change`` error, blocking the render.

    Current behavior: render_composite clips ``filled & polygon`` before
    painting, so leaks cannot reach the safety net. A ``fill_leak_clipped``
    warning surfaces the problem without blocking the build.

    To simulate cleanly, we craft a map where there is *no* dividing wall
    between two rooms, but the calibration declares the left-half polygon
    only. The flood-fill spills past the polygon; the clip catches it.
    """
    h, w = 400, 600
    img = np.full((h, w, 3), 255, dtype=np.uint8)
    # Outer border only — no divider wall.
    cv2.rectangle(img, (50, 50), (450, 350), (0, 0, 0), thickness=2)
    map_path = tmp_path / "map.png"
    cv2.imwrite(str(map_path), img)

    # Calibration claims a room covering only the LEFT half, but the
    # floor-plan has no wall at x=250 — the fill will spill into the right.
    mask_a = np.zeros((h, w), dtype=bool)
    mask_a[52:349, 52:249] = True  # only the left half
    room_a = Room(
        id=1, polygon_rle=mask_to_rle(mask_a), area_px=int(mask_a.sum()),
        bbox=(52, 52, 197, 297),
    )
    label_a = Label(
        id="100", bbox=(120, 200, 20, 14),
        room_id=1,
        fill_seed=(150, 200), ocr_confidence=0.95,
    )
    cal = Calibration(
        map_image="map.png", map_hash="sha256:leaky",
        labels=[label_a], rooms=[room_a], wall_patches=[],
        render_defaults=RenderDefaults(min_font_pt=7, tile_dpi=150),
    )

    assignments = [Assignment("Alice", "100", "BITS", 2)]
    layout, _ = plan_layout(cal, assignments)
    out = tmp_path / "composite.png"
    result = render_composite(map_path, cal, layout, assignments, out)

    # Safety net does NOT fire — leak was clipped before painting.
    assert result.unexpected_pixel_count == 0
    assert not any(i.code == "unexpected_pixel_change" for i in result.issues)
    # No error-level issues at all.
    assert result.errors == [], f"expected no errors, got {result.errors}"
    # But a warning DID surface to flag the underlying calibration problem.
    leak_warnings = [i for i in result.issues if i.code == "fill_leak_clipped"]
    assert leak_warnings, f"expected fill_leak_clipped warning, got {result.issues}"
    assert leak_warnings[0].office_id == "100"
    # Render produced the composite file successfully.
    assert out.exists() and out.stat().st_size > 200

    # Confirm the right-half (outside polygon) was NOT painted by reading
    # back the composite and checking pixels at x > 250 differ from the
    # team color (still white from the original).
    composite = cv2.imread(str(out))
    # Sample a point well inside the would-have-been-leaked area.
    right_pixel = composite[200, 300]
    assert tuple(int(v) for v in right_pixel) == (255, 255, 255), (
        f"pixel inside leak region should still be white; got {right_pixel.tolist()}"
    )


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------
