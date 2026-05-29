"""Tests for ``officemapmaker.palette``."""

from __future__ import annotations

import json

import pytest

from officemapmaker.palette import (
    auto_palette,
    build_palette,
    contrast_ratio,
    load_team_overrides,
    parse_hex_color,
    rgb_to_hex,
)


# ---------------------------------------------------------------------------
# contrast_ratio
# ---------------------------------------------------------------------------


def test_contrast_ratio_white_vs_black_is_21():
    cr = contrast_ratio((255, 255, 255), (0, 0, 0))
    assert abs(cr - 21.0) < 1e-6


def test_contrast_ratio_same_color_is_1():
    assert contrast_ratio((128, 128, 128), (128, 128, 128)) == pytest.approx(1.0)


def test_contrast_ratio_is_symmetric():
    a = (50, 100, 200)
    b = (250, 220, 200)
    assert contrast_ratio(a, b) == pytest.approx(contrast_ratio(b, a))


# ---------------------------------------------------------------------------
# parse_hex_color / rgb_to_hex
# ---------------------------------------------------------------------------


def test_parse_hex_full_form():
    assert parse_hex_color("#FF8800") == (0xFF, 0x88, 0x00)


def test_parse_hex_no_hash():
    assert parse_hex_color("AABBCC") == (0xAA, 0xBB, 0xCC)


def test_parse_hex_short_form():
    assert parse_hex_color("#F80") == (0xFF, 0x88, 0x00)


def test_parse_hex_rejects_invalid_length():
    with pytest.raises(ValueError):
        parse_hex_color("#12345")


def test_parse_hex_rejects_non_hex():
    with pytest.raises(ValueError):
        parse_hex_color("#ZZ0000")


def test_parse_hex_rejects_non_string():
    with pytest.raises(ValueError):
        parse_hex_color(0xABCDEF)  # type: ignore[arg-type]


def test_rgb_to_hex_round_trip():
    for s in ("#000000", "#FFFFFF", "#1A2B3C"):
        assert rgb_to_hex(parse_hex_color(s)) == s


# ---------------------------------------------------------------------------
# auto_palette
# ---------------------------------------------------------------------------


def test_auto_palette_returns_n_colors():
    assert len(auto_palette(7)) == 7


def test_auto_palette_zero_returns_empty():
    assert auto_palette(0) == []


def test_auto_palette_negative_rejects():
    with pytest.raises(ValueError):
        auto_palette(-1)


def test_auto_palette_default_satisfies_wcag_aaa_for_many_colors():
    # The whole point of the default L/S choice — every color must clear 7:1.
    for c in auto_palette(50):
        assert contrast_ratio(c, (0, 0, 0)) >= 7.0


def test_auto_palette_colors_are_distinct():
    # Golden-ratio spacing should keep adjacent hues well apart.
    colors = auto_palette(12)
    assert len(set(colors)) == len(colors)


def test_auto_palette_rejects_out_of_range_lightness():
    with pytest.raises(ValueError):
        auto_palette(3, lightness=0)
    with pytest.raises(ValueError):
        auto_palette(3, lightness=1.5)


def test_auto_palette_rejects_out_of_range_saturation():
    with pytest.raises(ValueError):
        auto_palette(3, saturation=-0.1)
    with pytest.raises(ValueError):
        auto_palette(3, saturation=1.5)


def test_auto_palette_dark_colors_may_fail_contrast():
    # Sanity: a user who picks L=0.3 is on their own.
    dark = auto_palette(5, lightness=0.3, saturation=0.8)
    assert any(contrast_ratio(c, (0, 0, 0)) < 7.0 for c in dark)


# ---------------------------------------------------------------------------
# build_palette
# ---------------------------------------------------------------------------


def test_build_palette_covers_every_team():
    p = build_palette(["BITS", "FPAA", "Revenue"])
    assert set(p.colors) == {"BITS", "FPAA", "Revenue"}
    assert p.overrides_used == frozenset()
    assert p.low_contrast == frozenset()


def test_build_palette_is_deterministic_across_input_order():
    a = build_palette(["BITS", "FPAA", "Revenue"])
    b = build_palette(["Revenue", "BITS", "FPAA"])
    assert a.colors == b.colors


def test_build_palette_collapses_duplicates_and_blanks():
    p = build_palette(["BITS", "BITS", "", "FPAA"])
    assert set(p.colors) == {"BITS", "FPAA"}


def test_build_palette_applies_overrides():
    p = build_palette(["BITS", "FPAA"], overrides={"BITS": (255, 0, 0)})
    assert p.colors["BITS"] == (255, 0, 0)
    assert "BITS" in p.overrides_used


def test_build_palette_flags_low_contrast_overrides():
    p = build_palette(["BITS"], overrides={"BITS": (0, 0, 0)})  # black on black
    assert "BITS" in p.low_contrast


def test_build_palette_does_not_flag_auto_colors_as_low_contrast():
    p = build_palette(["A", "B", "C", "D", "E"])
    assert p.low_contrast == frozenset()


def test_color_for_returns_none_for_unknown_team():
    p = build_palette(["BITS"])
    assert p.color_for("Unknown") is None
    assert p.color_for("BITS") is not None


# ---------------------------------------------------------------------------
# load_team_overrides
# ---------------------------------------------------------------------------


def test_load_team_overrides_missing_file_returns_empty(tmp_path):
    assert load_team_overrides(tmp_path / "nope.json") == {}


def test_load_team_overrides_hex_strings(tmp_path):
    p = tmp_path / "teams.json"
    p.write_text(json.dumps({"BITS": "#C7E5FF", "FPAA": "#FFE2B3"}))
    out = load_team_overrides(p)
    assert out == {"BITS": (0xC7, 0xE5, 0xFF), "FPAA": (0xFF, 0xE2, 0xB3)}


def test_load_team_overrides_rgb_lists(tmp_path):
    p = tmp_path / "teams.json"
    p.write_text(json.dumps({"BITS": [10, 20, 30]}))
    assert load_team_overrides(p) == {"BITS": (10, 20, 30)}


def test_load_team_overrides_rejects_oob_components(tmp_path):
    p = tmp_path / "teams.json"
    p.write_text(json.dumps({"BITS": [10, 20, 300]}))
    with pytest.raises(ValueError):
        load_team_overrides(p)


def test_load_team_overrides_rejects_bad_top_level(tmp_path):
    p = tmp_path / "teams.json"
    p.write_text(json.dumps(["not", "a", "map"]))
    with pytest.raises(ValueError):
        load_team_overrides(p)


def test_load_team_overrides_rejects_bad_value(tmp_path):
    p = tmp_path / "teams.json"
    p.write_text(json.dumps({"BITS": 1234}))
    with pytest.raises(ValueError):
        load_team_overrides(p)
