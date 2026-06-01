"""Tests for ``officemapmaker.tile`` (pass 5: tile composite + multi-page PDF)."""

from __future__ import annotations

import json
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

from officemapmaker.tile import (
    PAPER_SIZES_IN,
    TileIssue,
    compute_tile_grid,
    tile_composite,
)


# ---------------------------------------------------------------------------
# compute_tile_grid — pure geometry
# ---------------------------------------------------------------------------


def test_compute_tile_grid_single_tile_when_composite_fits_in_one_page():
    grid = compute_tile_grid((500, 700), dpi=150, paper="letter", overlap_in=0.25)
    assert grid.rows == 1
    assert grid.cols == 1
    assert len(grid.tiles) == 1
    placement = grid.tiles[0]
    assert placement.row == 1
    assert placement.col == 1
    # Tile fills a full letter page even though composite is smaller.
    assert placement.bbox == (0, 0, grid.tile_px[0], grid.tile_px[1])


def test_compute_tile_grid_letter_dimensions_at_150_dpi():
    grid = compute_tile_grid((100, 100), dpi=150, paper="letter", overlap_in=0.25)
    # 8.5 × 11 in @ 150 DPI → 1275 × 1650 px.
    assert grid.tile_px == (1275, 1650)
    # 0.25 in @ 150 DPI → 38 px (rounded).
    assert grid.overlap_px == 38


def test_compute_tile_grid_a4_paper_supported():
    grid = compute_tile_grid((100, 100), dpi=150, paper="a4")
    # 8.27 × 11.69 in @ 150 DPI → 1240 × 1754 (rounded) px.
    assert grid.tile_px == (1240, 1754)


def test_compute_tile_grid_two_by_two_grid_for_large_composite():
    # Pick a composite that requires 2 tiles in each axis.
    # Tile = 1275×1650; step = 1275-38=1237 horizontally.
    # comp_w=1500 > 1275 → cols = 1 + ceil((1500-1275)/1237) = 2.
    grid = compute_tile_grid((1500, 2000), dpi=150, paper="letter", overlap_in=0.25)
    assert grid.cols == 2
    assert grid.rows == 2
    assert len(grid.tiles) == 4

    # Tile (1,1) starts at origin.
    assert grid.tiles[0].bbox[:2] == (0, 0)
    # Last column tile is pulled back to fit composite right edge exactly.
    last_col_tile = next(t for t in grid.tiles if t.row == 1 and t.col == 2)
    assert last_col_tile.bbox[0] == 1500 - 1275
    last_row_tile = next(t for t in grid.tiles if t.row == 2 and t.col == 1)
    assert last_row_tile.bbox[1] == 2000 - 1650


def test_compute_tile_grid_covers_every_composite_pixel():
    comp_w, comp_h = 3000, 4000
    grid = compute_tile_grid((comp_w, comp_h), dpi=150, paper="letter")
    coverage = np.zeros((comp_h, comp_w), dtype=bool)
    for tile in grid.tiles:
        x, y, w, h = tile.bbox
        x0, y0 = max(0, x), max(0, y)
        x1, y1 = min(comp_w, x + w), min(comp_h, y + h)
        coverage[y0:y1, x0:x1] = True
    assert coverage.all(), "every composite pixel must be inside at least one tile"


def test_compute_tile_grid_overlap_zero_is_allowed():
    grid = compute_tile_grid((3000, 4000), dpi=150, paper="letter", overlap_in=0.0)
    assert grid.overlap_px == 0


def test_compute_tile_grid_rejects_unknown_paper():
    with pytest.raises(ValueError, match="unsupported paper"):
        compute_tile_grid((100, 100), paper="legal")


def test_compute_tile_grid_rejects_non_positive_dpi():
    with pytest.raises(ValueError, match="dpi must be positive"):
        compute_tile_grid((100, 100), dpi=0)


def test_compute_tile_grid_rejects_negative_overlap():
    with pytest.raises(ValueError, match="overlap_in must be non-negative"):
        compute_tile_grid((100, 100), overlap_in=-0.1)


def test_compute_tile_grid_rejects_oversized_overlap():
    with pytest.raises(ValueError, match="larger than a page"):
        # 12 in overlap at 150 DPI = 1800 px, larger than letter page (1650 tall).
        compute_tile_grid((100, 100), overlap_in=12.0)


def test_paper_sizes_in_contains_letter_a4_and_tabloid():
    assert PAPER_SIZES_IN["letter"] == (8.5, 11.0)
    assert "a4" in PAPER_SIZES_IN
    assert PAPER_SIZES_IN["tabloid"] == (11.0, 17.0)


def test_compute_tile_grid_tabloid_uses_full_page():
    # 1500x2300 fits in one tabloid page at 150 DPI (1650x2550 printable),
    # but trying letter would require 2 rows.
    grid_tab = compute_tile_grid(
        (1500, 2300), dpi=150, paper="tabloid", overlap_in=0.25,
    )
    assert grid_tab.rows == 1 and grid_tab.cols == 1
    grid_letter = compute_tile_grid(
        (1500, 2300), dpi=150, paper="letter", overlap_in=0.25,
    )
    assert grid_letter.rows >= 2


# ---------------------------------------------------------------------------
# tile_composite — end-to-end with a synthetic composite
# ---------------------------------------------------------------------------


def _make_synthetic_composite(path: Path, size: tuple[int, int] = (1500, 2000)) -> Path:
    """Write a recognizable RGB composite to ``path``.

    Each tile-sized region gets a distinct flat color so a downstream
    spot-check failure would be loud.  Bottom row is white to keep the
    footer-strip auto-check happy.
    """
    w, h = size
    arr = np.full((h, w, 3), 255, dtype=np.uint8)
    # Quadrants
    arr[: h // 2, : w // 2] = (220, 240, 200)  # top-left
    arr[: h // 2, w // 2 :] = (200, 220, 240)  # top-right
    arr[h // 2 :, : w // 2] = (240, 200, 220)  # bottom-left
    arr[h // 2 :, w // 2 :] = (220, 220, 220)  # bottom-right
    Image.fromarray(arr, "RGB").save(path, "PNG")
    return path


def _write_meta_sidecar(composite_path: Path, *, palette: dict[str, str]) -> Path:
    sidecar = composite_path.with_name(composite_path.stem + "_meta.json")
    sidecar.write_text(
        json.dumps(
            {
                "schema": "officemapmaker.composite_meta.v1",
                "map_path": str(composite_path),
                "map_hash": "sha256:" + "ab" * 32,
                "rendered_at": "2024-01-01T00:00:00",
                "composite_size": [1500, 2000],
                "palette": palette,
                "headcount": {team: 2 for team in palette},
                "total_people": 2 * len(palette),
                "total_office_labels": 6,
                "assigned_offices": 5,
                "vacant_offices": 1,
                "low_contrast_teams": [],
                "overrides_used": [],
            }
        ),
        encoding="utf-8",
    )
    return sidecar


def test_tile_composite_writes_all_expected_files(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    _write_meta_sidecar(composite, palette={"BITS": "#E6BCBC", "FPAA": "#BCE6BC"})
    out_dir = tmp_path / "tiles"

    result = tile_composite(composite, out_dir=out_dir, dpi=150, paper="letter")

    # Per-tile files.
    assert len(result.tile_paths) == result.grid.rows * result.grid.cols
    for path in result.tile_paths:
        assert path.exists()
        with Image.open(path) as im:
            assert im.size == result.grid.tile_px
    # Other artifacts.
    assert (out_dir / "contact_sheet.png").exists()
    assert (out_dir / "legend.png").exists()
    assert (out_dir / "all.pdf").exists()
    assert (out_dir / "all.pdf").stat().st_size > 0


def test_tile_composite_naming_pattern_is_row_x_col(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png", size=(1500, 2000))
    _write_meta_sidecar(composite, palette={"A": "#E6BCBC"})

    result = tile_composite(composite, out_dir=tmp_path / "tiles")

    names = {p.name for p in result.tile_paths}
    expected = {
        f"page-{r}x{c}.png"
        for r in range(1, result.grid.rows + 1)
        for c in range(1, result.grid.cols + 1)
    }
    assert names == expected


def test_tile_composite_clean_run_has_no_errors(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    _write_meta_sidecar(composite, palette={"A": "#E6BCBC"})

    result = tile_composite(composite, out_dir=tmp_path / "tiles")

    assert result.errors == [], f"unexpected errors: {result.errors}"


def test_tile_composite_warns_when_meta_sidecar_missing(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    # Deliberately do NOT write a sidecar.

    result = tile_composite(composite, out_dir=tmp_path / "tiles")

    codes = {issue.code for issue in result.issues}
    assert "meta_sidecar_missing" in codes
    # Legend page should still have been written (sparse but present).
    assert (tmp_path / "tiles" / "legend.png").exists()


def test_tile_composite_pdf_page_count_matches_tiles_plus_legend(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    _write_meta_sidecar(composite, palette={"A": "#E6BCBC"})

    result = tile_composite(composite, out_dir=tmp_path / "tiles")

    # Count pages via bytes-fallback (matches what _auto_checks does).
    blob = result.pdf_path.read_bytes()
    page_count = blob.count(b"/Type /Page") - blob.count(b"/Type /Pages")
    assert page_count == len(result.tile_paths) + 1


def test_tile_composite_creates_out_dir_if_missing(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    _write_meta_sidecar(composite, palette={"A": "#E6BCBC"})
    out_dir = tmp_path / "does" / "not" / "exist" / "yet"

    result = tile_composite(composite, out_dir=out_dir)

    assert out_dir.exists()
    assert result.tile_paths


def test_tile_composite_legend_swatches_match_palette_colors(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    palette = {"BITS": "#E6BCBC", "FPAA": "#BCE6BC"}
    _write_meta_sidecar(composite, palette=palette)

    result = tile_composite(composite, out_dir=tmp_path / "tiles")

    legend = np.asarray(Image.open(result.contact_sheet_path).convert("RGB"))
    assert legend.shape[0] > 0
    legend_img = np.asarray(Image.open(tmp_path / "tiles" / "legend.png").convert("RGB"))
    # Each palette color should appear at least once on the legend page (the swatch).
    for hex_color in palette.values():
        rgb = (
            int(hex_color[1:3], 16),
            int(hex_color[3:5], 16),
            int(hex_color[5:7], 16),
        )
        matches = np.all(legend_img == np.array(rgb), axis=2)
        assert matches.any(), f"swatch for {hex_color} not found on legend page"


def test_tile_composite_meta_argument_overrides_sidecar(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    _write_meta_sidecar(composite, palette={"SIDECAR_TEAM": "#FFFFFF"})

    explicit_meta = {
        "map_path": str(composite),
        "palette": {"EXPLICIT_TEAM": "#E6BCBC"},
        "headcount": {"EXPLICIT_TEAM": 1},
        "total_people": 1,
        "assigned_offices": 1,
        "vacant_offices": 0,
        "rendered_at": "2024-06-01T12:00:00",
        "map_hash": "sha256:" + "cd" * 32,
    }
    result = tile_composite(
        composite, out_dir=tmp_path / "tiles", meta=explicit_meta
    )
    # No missing-sidecar warning because we provided meta explicitly.
    codes = {issue.code for issue in result.issues}
    assert "meta_sidecar_missing" not in codes


def test_tile_composite_raises_for_missing_composite(tmp_path):
    with pytest.raises(FileNotFoundError):
        tile_composite(
            tmp_path / "nope.png", out_dir=tmp_path / "tiles"
        )


def test_tile_composite_min_font_warning_at_low_dpi(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png", size=(800, 1000))
    _write_meta_sidecar(composite, palette={"A": "#E6BCBC"})

    # min_font_pt=5 at 72 DPI → 5 px, below the 7-px floor → warning.
    result = tile_composite(
        composite, out_dir=tmp_path / "tiles", dpi=72, min_font_pt=5
    )

    codes = {issue.code for issue in result.issues}
    assert "tile_text_too_small" in codes


def test_tile_composite_records_grid_geometry_on_result(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    _write_meta_sidecar(composite, palette={"A": "#E6BCBC"})

    result = tile_composite(composite, out_dir=tmp_path / "tiles")

    assert result.grid.dpi == 150
    assert result.grid.page_size_in == (8.5, 11.0)
    assert result.grid.composite_size == (1500, 2000)


# ---------------------------------------------------------------------------
# TileIssue / TileResult helpers
# ---------------------------------------------------------------------------


def test_tile_result_errors_and_warnings_partition_issues(tmp_path):
    composite = _make_synthetic_composite(tmp_path / "composite.png")
    # No sidecar → at least one warning.
    result = tile_composite(composite, out_dir=tmp_path / "tiles")

    # warnings + errors must equal the issues list, with no overlap.
    assert len(result.warnings) + len(result.errors) == len(result.issues)
    for w in result.warnings:
        assert w.severity == "warning"
    for e in result.errors:
        assert e.severity == "error"


# ---------------------------------------------------------------------------
# CLI integration — `officemapmaker tile`
# ---------------------------------------------------------------------------
