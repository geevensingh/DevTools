"""Tests for ``officemapmaker.layout`` (pass 3: name placement planner)."""

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
from officemapmaker.layout import (
    FitStrategy,
    Layout,
    LayoutEntry,
    LayoutIssue,
    NameEntry,
    OfficeNumberPlacement,
    load_layout,
    plan_layout,
    render_layout_problems_png,
    render_layout_review_png,
    save_layout,
)


# ---------------------------------------------------------------------------
# Fixture builders
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


def _office_label(label_id: str, room_id: int, fill_seed=(60, 60)) -> Label:
    return Label(
        id=label_id,
        bbox=(fill_seed[0] - 12, fill_seed[1] - 6, 24, 12),
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
        # Use modest defaults so binary-search converges fast in tests.
        render_defaults=RenderDefaults(min_font_pt=7, tile_dpi=150),
    )


def _asn(name: str, office_id: str, team: str = "BITS", row: int = 2) -> Assignment:
    return Assignment(name=name, office_id=office_id, team=team, source_row=row)


# ---------------------------------------------------------------------------
# Round-trip the data model
# ---------------------------------------------------------------------------


def test_layout_round_trips_through_json(tmp_path):
    layout = Layout(
        map_image="map.png",
        map_hash="sha256:abc",
        entries=[
            LayoutEntry(
                office_id="1480",
                room_id=42,
                team="BITS",
                fit_strategy=FitStrategy.FULL,
                names=[
                    NameEntry(
                        full_name="Alice Smith",
                        rendered_text="Alice Smith",
                        bbox=(10, 20, 80, 16),
                        font_px=14,
                    )
                ],
                office_number=OfficeNumberPlacement(
                    text="1480", bbox=(70, 90, 30, 14), font_px=14
                ),
                inscribed_rect=(10, 10, 100, 100),
                leader_lines=[],
            )
        ],
    )
    path = tmp_path / "layout.json"
    save_layout(layout, path)
    loaded = load_layout(path)
    assert loaded == layout


def test_layout_entry_by_office_lookup():
    entry = LayoutEntry(
        office_id="1480", room_id=1, team="BITS",
        fit_strategy=FitStrategy.FULL, names=[],
        office_number=OfficeNumberPlacement(text="1480", bbox=(0,0,1,1), font_px=10),
        inscribed_rect=(0, 0, 1, 1),
    )
    layout = Layout(map_image="m", map_hash="", entries=[entry])
    assert layout.entry_by_office("1480") is entry
    assert layout.entry_by_office("9999") is None


# ---------------------------------------------------------------------------
# plan_layout — happy path
# ---------------------------------------------------------------------------


def test_plan_layout_fits_full_names_in_a_large_room():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    assignments = [_asn("Alice Smith", "1480"), _asn("Bob Jones", "1480")]
    layout, issues = plan_layout(cal, assignments)
    assert [i for i in issues if i.severity == "error"] == []
    assert len(layout.entries) == 1
    entry = layout.entries[0]
    assert entry.fit_strategy is FitStrategy.FULL
    assert {n.rendered_text for n in entry.names} == {"Alice Smith", "Bob Jones"}
    # Every name bbox is inside the inscribed rect (height-only check; width
    # may be centered).
    ix, iy, iw, ih = entry.inscribed_rect
    for n in entry.names:
        nx, ny, nw, nh = n.bbox
        assert nx >= ix and ny >= iy
        assert nx + nw <= ix + iw and ny + nh <= iy + ih + nh  # name+number share area


def test_plan_layout_office_number_is_always_present():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    layout, _ = plan_layout(cal, [_asn("X Y", "1480")])
    assert layout.entries[0].office_number.text == "1480"
    assert layout.entries[0].office_number.font_px > 0


def test_plan_layout_skips_vacant_offices():
    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(150, 150)),
            _office_label("1481", room_id=2, fill_seed=(450, 150)),
        ],
        rooms=[_square_room(1, 50, 50, 200), _square_room(2, 350, 50, 200)],
    )
    layout, issues = plan_layout(cal, [_asn("Alice Smith", "1480")])
    assert [e.office_id for e in layout.entries] == ["1480"]
    # 1481 has no assignment — should not appear, and not be flagged.
    assert all(i.office_id != "1481" for i in issues)


def test_plan_layout_ignores_assignments_for_unknown_offices():
    """validate_labels owns the office_not_on_map error; plan_layout just skips."""
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    layout, issues = plan_layout(
        cal, [_asn("Alice Smith", "1480"), _asn("Bob Jones", "9999")]
    )
    assert [e.office_id for e in layout.entries] == ["1480"]
    # plan_layout emits person_not_placed for the orphan.
    codes = {i.code for i in issues}
    assert "person_not_placed" in codes


# ---------------------------------------------------------------------------
# plan_layout — abbreviation ladder
# ---------------------------------------------------------------------------


def test_plan_layout_falls_back_to_abbreviated_in_a_tighter_room():
    """A narrow room forces the planner off the full-name rung.

    With the first-name-preserving ladder, ``Sravani Punyamurthula``
    shortens to ``Sravani P.`` (level 1) and then to ``Sravani``
    (level 2). Either of those (or LEADER if even that won't fit) is
    acceptable here -- the test just guarantees the planner *did*
    shorten and the first token survived.
    """
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(95, 80))],
        rooms=[_square_room(1, 70, 50, 50)],  # 50x50 room
    )
    layout, issues = plan_layout(
        cal,
        [
            _asn("Sravani Punyamurthula", "1480"),
            _asn("Christopher Vanderbilt", "1480"),
        ],
    )
    entry = layout.entries[0]
    assert entry.fit_strategy in (FitStrategy.ABBREVIATED, FitStrategy.LEADER)
    if entry.fit_strategy is FitStrategy.ABBREVIATED:
        # Each rendered text still starts with the original first name
        # (we prefer keeping first names readable).
        for n in entry.names:
            assert n.rendered_text.split()[0] in {"Sravani", "Christopher"}


def test_plan_layout_falls_back_to_first_name_only_in_a_very_tight_room():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(85, 70))],
        rooms=[_square_room(1, 70, 60, 30)],  # 30x30 room
    )
    layout, issues = plan_layout(
        cal,
        [
            _asn("Sravani Punyamurthula", "1480"),
            _asn("Christopher Vanderbilt", "1480"),
        ],
    )
    entry = layout.entries[0]
    assert entry.fit_strategy in (FitStrategy.ABBREVIATED, FitStrategy.LEADER)
    if entry.fit_strategy is FitStrategy.ABBREVIATED:
        # At the most-aggressive rung the names collapse to just first
        # names. We don't insist on that exact level (the planner may
        # stop earlier if "Sravani P." fits) -- just that the first
        # token is preserved.
        for n in entry.names:
            assert n.rendered_text.split()[0] in {"Sravani", "Christopher"}
    # Either way, a warning issue was raised.
    codes = {i.code for i in issues}
    assert "abbreviation_fallback" in codes or "leader_line_fallback" in codes


def test_plan_layout_uses_leader_line_for_impossibly_small_rooms():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(80, 70))],
        rooms=[_square_room(1, 75, 65, 10)],  # 10x10 — too small for any text
    )
    layout, issues = plan_layout(cal, [_asn("Sravani Punyamurthula", "1480")])
    entry = layout.entries[0]
    assert entry.fit_strategy is FitStrategy.LEADER
    assert len(entry.leader_lines) >= 1
    assert any(i.code == "leader_line_fallback" for i in issues)


# ---------------------------------------------------------------------------
# plan_layout — full-rect fallback (Bug 2) + room-aware leader (Bug 1)
# ---------------------------------------------------------------------------


def _rect_room(rid: int, x: int, y: int, w: int, h: int, *, img_size=(800, 600)) -> Room:
    """Like ``_square_room`` but with independent width/height."""
    mask = np.zeros((img_size[1], img_size[0]), dtype=bool)
    mask[y : y + h, x : x + w] = True
    return Room(
        id=rid,
        polygon_rle=mask_to_rle(mask),
        area_px=int(mask.sum()),
        bbox=(x, y, w, h),
    )


def test_plan_layout_short_wide_room_uses_full_rect_instead_of_leader():
    """Bug 2: reserving a bottom strip for the office number used to shrink
    the names area below one line tall for wide-but-short rooms, forcing
    a leader-line fallback even though a single short name would have fit
    in the full rect. After the fix the planner retries against the full
    inscribed rect before punting.
    """
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(125, 82))],
        rooms=[_rect_room(1, 25, 65, 200, 35)],  # 200 wide, only 35 tall
    )
    layout, _ = plan_layout(cal, [_asn("Ali", "1480")])
    entry = layout.entries[0]
    assert entry.fit_strategy is not FitStrategy.LEADER
    assert entry.leader_lines == []
    # The single name must be placed somewhere inside the inscribed rect.
    ix, iy, iw, ih = entry.inscribed_rect
    nx, ny, nw, nh = entry.names[0].bbox
    assert ix <= nx and nx + nw <= ix + iw
    assert iy <= ny and ny + nh <= iy + ih


def test_place_office_number_prefers_clear_corner():
    """The corner picker tries bottom-right, bottom-left, top-right,
    top-left in order and returns the first one that doesn't overlap any
    placed name bbox.
    """
    from officemapmaker.layout import _place_office_number  # type: ignore[attr-defined]

    rect = (0, 0, 100, 100)
    number_size = (20, 10)
    # A single name pinned at the bottom of the rect. Bottom-right is
    # occupied; bottom-left is free.
    name_at_bottom = NameEntry(
        full_name="Z", rendered_text="Z", bbox=(60, 90, 30, 10), font_px=10
    )
    bbox, overlaps = _place_office_number(
        rect=rect, number_size=number_size, names=[name_at_bottom]
    )
    assert overlaps is False
    # Falls through to bottom-left.
    assert bbox == (2, 88, 20, 10)


def test_place_office_number_warns_when_every_corner_overlaps():
    """If every corner overlaps at least one name, the helper signals the
    overlap so the planner can emit ``office_number_overlaps_names``.
    """
    from officemapmaker.layout import _place_office_number  # type: ignore[attr-defined]

    rect = (0, 0, 60, 60)
    number_size = (20, 10)
    # A name in each corner — every candidate slot overlaps one of them.
    names = [
        NameEntry(full_name="A", rendered_text="A", bbox=(0, 0, 60, 12), font_px=10),
        NameEntry(full_name="B", rendered_text="B", bbox=(0, 24, 60, 12), font_px=10),
        NameEntry(full_name="C", rendered_text="C", bbox=(0, 48, 60, 12), font_px=10),
    ]
    bbox, overlaps = _place_office_number(
        rect=rect, number_size=number_size, names=names
    )
    assert overlaps is True
    # Falls back to bottom-right (historical default).
    assert bbox == (38, 48, 20, 10)


def test_plan_layout_leader_line_avoids_other_labeled_rooms():
    """Bug 1: leader-line fallback used to pick left/right of the room
    purely from the map midpoint, so a small room in the left half always
    pushed text rightward — even if the next room over was a labeled
    office (causing one office's name to appear inside another office).
    After the fix, the planner builds a union mask of every labeled room
    and chooses a leader-text position that doesn't overlap any of them.
    """
    from officemapmaker.geometry import rle_to_mask

    # Tiny room A in the left half; big labeled room B immediately to its
    # right. Old code put A's text at x = A_max + 8 = 68, deep inside B.
    cal = _build_cal(
        labels=[
            _office_label("1015", room_id=1, fill_seed=(55, 55)),
            _office_label("1009", room_id=2, fill_seed=(165, 140)),
        ],
        rooms=[
            _rect_room(1, 50, 50, 10, 10),
            _rect_room(2, 65, 40, 200, 200),
        ],
    )
    layout, _ = plan_layout(
        cal, [_asn("Bahnasawy", "1015"), _asn("Smith", "1009")]
    )
    entry_1015 = next(e for e in layout.entries if e.office_id == "1015")
    assert entry_1015.fit_strategy is FitStrategy.LEADER
    # Decode room 2's polygon and confirm no name pixel from 1015 lands in it.
    room2 = next(r for r in cal.rooms if r.id == 2)
    other_mask = rle_to_mask(room2.polygon_rle) > 0
    for n in entry_1015.names:
        nx, ny, nw, nh = n.bbox
        x0 = max(0, nx)
        y0 = max(0, ny)
        x1 = min(other_mask.shape[1], nx + nw)
        y1 = min(other_mask.shape[0], ny + nh)
        if x0 < x1 and y0 < y1:
            assert not other_mask[y0:y1, x0:x1].any(), (
                f"leader text {n.rendered_text!r} bbox={n.bbox} overlaps room 2"
            )


# ---------------------------------------------------------------------------
# plan_layout — polygon-aware fit (Bug 3) + in-room leader skip
# ---------------------------------------------------------------------------


def _irregular_room_polygon(img_size=(400, 400)) -> tuple[Room, np.ndarray]:
    """Build a polygon where the largest inscribed rect is wide-but-flat
    and a stacked name pair only fits in a taller-but-narrower secondary
    region. Used to exercise the polygon-aware fallback (Bug 3).

    Layout::

        (0,0) +---------------------------------+ (200, 0)
              |  R1: 200 wide, 18 tall          |
              +------------------+--+------------+ (200, 18)
                                 |  | bridge: 5 wide, 22 tall
              +------------------+  +
              |  R2: 60 wide,    |  |
              |       80 tall    |  |
              |                  |  |
              +------------------+--+ (60, 120)
    """
    img_w, img_h = img_size
    mask = np.zeros((img_h, img_w), dtype=bool)
    # R1: wide-but-flat top bar — biggest inscribed rect by area (200*18=3600).
    mask[0:18, 0:200] = True
    # Bridge connecting top bar to bottom area so we have a single CC.
    mask[18:40, 80:85] = True
    # R2: narrow-but-tall lower area (60*80=4800). Largest *single* rect that
    # can hold two stacked names.
    mask[40:120, 0:60] = True
    rid = 99
    room = Room(
        id=rid,
        polygon_rle=mask_to_rle(mask),
        area_px=int(mask.sum()),
        bbox=(0, 0, 200, 120),
    )
    return room, mask


def test_plan_layout_polygon_aware_fit_succeeds_when_inscribed_rect_too_short():
    """Bug 3: the inscribed-rect ladder used to give up if no axis-aligned
    rectangle inside the polygon could hold the stacked names — even when
    a different region of the same polygon had the right shape. The new
    ``_try_fit_polygon`` step searches the polygon mask per-pixel and
    finds positions that the rect-based ladder missed.
    """
    room, _ = _irregular_room_polygon()
    # Two short stacked names: each ~30x15 at min font; total stack ~30x32.
    # Top bar (200x18) can't hold them (18 < 32 stacked).
    # Lower region (60x80) can.
    cal = _build_cal(
        labels=[_office_label("2100", room_id=99, fill_seed=(30, 80))],
        rooms=[room],
    )
    layout, issues = plan_layout(cal, [_asn("AB", "2100"), _asn("CD", "2100")])
    entry = layout.entries[0]
    # Polygon-aware fit should succeed — no LEADER fallback.
    assert entry.fit_strategy is not FitStrategy.LEADER
    assert entry.leader_lines == []
    # Every placed name's bbox must lie fully inside the polygon.
    from officemapmaker.geometry import rle_to_mask

    poly = rle_to_mask(room.polygon_rle) > 0
    for n in entry.names:
        nx, ny, nw, nh = n.bbox
        assert poly[ny:ny + nh, nx:nx + nw].all(), (
            f"name {n.rendered_text!r} bbox={n.bbox} not fully inside polygon"
        )


def test_try_fit_polygon_places_names_in_disjoint_polygon_regions():
    """``_try_fit_polygon`` is allowed to give each stacked name its own
    horizontal position. Two short names should each find a spot inside
    the polygon, both fully inside, stacked top-to-bottom (no overlap).
    """
    from officemapmaker.layout import _try_fit_polygon  # type: ignore[attr-defined]

    room, mask = _irregular_room_polygon()
    people = [_asn("AB", "2100"), _asn("CD", "2100")]
    placed = _try_fit_polygon(
        texts=["AB", "CD"],
        people=people,
        polygon=mask,
        room_bbox=room.bbox,
        min_px=15,
        max_px=24,
        font_path=None,
    )
    assert placed is not None
    assert len(placed) == 2
    # Both names fully inside polygon.
    for n in placed:
        nx, ny, nw, nh = n.bbox
        assert mask[ny:ny + nh, nx:nx + nw].all()
    # Second name strictly below the first (no overlap, in stack order).
    assert placed[1].bbox[1] >= placed[0].bbox[1] + placed[0].bbox[3]


def test_try_fit_polygon_returns_none_when_no_name_fits():
    """If even the smallest name doesn't fit anywhere inside the polygon
    at ``min_px``, the helper returns ``None`` (the caller then falls to
    LEADER).
    """
    from officemapmaker.layout import _try_fit_polygon  # type: ignore[attr-defined]

    # 5x5 polygon — way too small for any text at min_px=15.
    mask = np.zeros((40, 40), dtype=bool)
    mask[10:15, 10:15] = True
    room_bbox = (10, 10, 5, 5)
    placed = _try_fit_polygon(
        texts=["Bahnasawy"],
        people=[_asn("Bahnasawy", "1015")],
        polygon=mask,
        room_bbox=room_bbox,
        min_px=15,
        max_px=24,
        font_path=None,
    )
    assert placed is None


def test_try_fit_vertically_centers_names_in_oversized_area():
    """When the safe area is taller than the names need (common in
    L-shaped rooms where the largest inscribed rectangle is sized for
    the wider axis but the names only need 2-3 short lines), the names
    block should sit visually centered in the area, not hug the top
    edge.
    """
    from officemapmaker.layout import _try_fit  # type: ignore[attr-defined]

    # Area 200 wide x 200 tall; two short names fit easily — the
    # binary search picks the max font (24 px). At line-spacing 1.15
    # each line is round(24 * 1.15) = 28 px, two names = 56 px total.
    # Centering offset = (200 - 56) // 2 = 72 px from the top edge.
    people = [_asn("Ab", "1000"), _asn("Cd", "1000")]
    placed = _try_fit(
        texts=["Ab", "Cd"],
        people=people,
        area=(0, 0, 200, 200),
        min_px=16,
        max_px=24,
        font_path=None,
    )
    assert placed is not None
    assert len(placed) == 2

    font_px = placed[0].font_px
    line_h = int(round(font_px * 1.15))
    total_h = 2 * line_h  # one line each, two names
    expected_top = (200 - total_h) // 2

    first_y = placed[0].bbox[1]
    # Allow off-by-one for integer rounding inside _try_fit.
    assert abs(first_y - expected_top) <= 1, (
        f"first_y={first_y}, expected ~{expected_top} "
        f"(font_px={font_px}, line_h={line_h})"
    )
    # And the regression bites if someone restores ``y_cursor = area_y``:
    # the top padding should be substantial, not zero.
    assert first_y >= 30
    # Second name should sit one line_h below the first.
    assert placed[1].bbox[1] == first_y + line_h


def test_plan_layout_inflates_polygon_to_include_label_bboxes():
    """The original office-number digits punch holes in the white-pixel
    CC polygon. ``_plan_one_office`` should OR each room's label bboxes
    back into the polygon before LIR / ``_try_fit_polygon`` see it, so
    name placement isn't forced to avoid the soon-to-be-redrawn digits.
    """
    img_w, img_h = 200, 100
    # Build a polygon that is a 100x60 rectangle MINUS a 40x20 hole
    # right in its center (simulating where the OCR'd label sits).
    mask = np.zeros((img_h, img_w), dtype=bool)
    mask[20:80, 50:150] = True
    # Hole at (80,40)-(120,60) — i.e. dead center.
    mask[40:60, 80:120] = False
    assert mask.sum() == 100 * 60 - 40 * 20  # sanity

    room = Room(
        id=1,
        polygon_rle=mask_to_rle(mask),
        area_px=int(mask.sum()),
        bbox=(50, 20, 100, 60),
    )
    # Label sits exactly in the hole.
    label = Label(
        id="1480",
        bbox=(80, 40, 40, 20),
        room_id=1,
        fill_seed=(100, 50),
        ocr_confidence=0.9,
    )
    cal = Calibration(
        map_image="map.png",
        map_hash="sha256:dummy",
        labels=[label],
        rooms=[room],
        wall_patches=[],
        render_defaults=RenderDefaults(min_font_pt=7, tile_dpi=150),
    )

    layout, issues = plan_layout(cal, [_asn("Geeven Singh", "1480")])
    assert [i for i in issues if i.severity == "error"] == []
    entry = layout.entries[0]

    # Without polygon inflation, the LIR would have to avoid the central
    # hole and the largest rect would be ~100x20 (top or bottom strip).
    # With inflation, the LIR is the full 100x60 rectangle.
    ix, iy, iw, ih = entry.inscribed_rect
    assert iw >= 95 and ih >= 55, (
        f"expected near-full-room LIR after label inflation; got {entry.inscribed_rect}"
    )
    # Sanity: the inscribed rect overlaps the original label bbox.
    lx, ly, lw, lh = label.bbox
    overlap_x = max(0, min(ix + iw, lx + lw) - max(ix, lx))
    overlap_y = max(0, min(iy + ih, ly + lh) - max(iy, ly))
    assert overlap_x > 0 and overlap_y > 0, (
        "inscribed rect should overlap the (soon-to-be-redrawn) label bbox"
    )


def test_build_leader_placement_skips_line_when_text_lands_inside_polygon():
    """Fix A: if the leader-line grid happens to find a clean spot inside
    the office's own polygon, the leader line itself is pointless (and
    confusing — it would be drawn from the centroid to text in the same
    room). The helper must return an empty leader_lines list in that
    case.
    """
    from officemapmaker.layout import _build_leader_placement  # type: ignore[attr-defined]

    # A roomy 200x200 polygon with no other rooms around it. The grid
    # will easily find a spot inside the polygon.
    map_h, map_w = 400, 400
    polygon = np.zeros((map_h, map_w), dtype=bool)
    polygon[50:250, 50:250] = True
    placed, leader_lines = _build_leader_placement(
        texts=["X"],
        people=[_asn("X", "2100")],
        polygon=polygon,
        other_rooms_mask=None,
        map_h=map_h,
        map_w=map_w,
        min_px=15,
        font_path=None,
    )
    assert len(placed) == 1
    # Text should land inside the polygon.
    nx, ny, nw, nh = placed[0].bbox
    assert polygon[ny:ny + nh, nx:nx + nw].all()
    # And therefore no leader line is drawn.
    assert leader_lines == []


# ---------------------------------------------------------------------------
# plan_layout — error / warning paths
# ---------------------------------------------------------------------------


def test_plan_layout_mixed_teams_warns_and_picks_one():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    layout, issues = plan_layout(
        cal,
        [
            _asn("Alice Smith", "1480", team="BITS"),
            _asn("Bob Jones", "1480", team="FPAA"),
        ],
    )
    assert layout.entries[0].team in {"BITS", "FPAA"}
    assert any(i.code == "mixed_teams_in_office" for i in issues)


def test_plan_layout_lookup_is_case_insensitive():
    cal = _build_cal(
        labels=[_office_label("1479A", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    # Spreadsheet has lowercase 'a'.
    layout, issues = plan_layout(cal, [_asn("Alice Smith", "1479a")])
    assert len(layout.entries) == 1
    assert layout.entries[0].office_id == "1479A"
    assert [i for i in issues if i.severity == "error"] == []


def test_plan_layout_uses_provided_map_hash():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    layout, _ = plan_layout(cal, [_asn("X Y", "1480")], map_hash="sha256:CAFEBABE")
    assert layout.map_hash == "sha256:CAFEBABE"


def test_plan_layout_inherits_calibration_hash_when_none_supplied():
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    layout, _ = plan_layout(cal, [_asn("X Y", "1480")])
    assert layout.map_hash == cal.map_hash


# ---------------------------------------------------------------------------
# plan_layout — progress callback
# ---------------------------------------------------------------------------


def test_plan_layout_progress_callback_fires_once_per_office_plus_final():
    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(150, 150)),
            _office_label("1481", room_id=2, fill_seed=(450, 150)),
            _office_label("1482", room_id=3, fill_seed=(650, 150)),
        ],
        rooms=[
            _square_room(1, 50, 50, 200),
            _square_room(2, 350, 50, 200),
            _square_room(3, 550, 50, 200, img_size=(800, 600)),
        ],
    )
    assignments = [
        _asn("A B", "1480"), _asn("C D", "1481"), _asn("E F", "1482"),
    ]
    calls: list[tuple[float, str]] = []
    plan_layout(cal, assignments, progress_cb=lambda f, m: calls.append((f, m)))
    # One pre-office tick per office + one final 1.0 tick.
    assert len(calls) == 4
    # First three are monotonically increasing, in [0, 1).
    fractions = [f for f, _ in calls]
    assert fractions[0] == 0.0
    assert fractions[1] == pytest.approx(1 / 3)
    assert fractions[2] == pytest.approx(2 / 3)
    assert fractions[3] == 1.0
    # Messages mention which office and the running counter.
    assert "1 of 3" in calls[0][1] and "1480" in calls[0][1]
    assert "2 of 3" in calls[1][1] and "1481" in calls[1][1]
    assert "3 of 3" in calls[2][1] and "1482" in calls[2][1]
    assert "3" in calls[3][1]  # final summary mentions the count


def test_plan_layout_progress_callback_with_zero_offices_still_terminates():
    cal = _build_cal(labels=[], rooms=[])
    calls: list[tuple[float, str]] = []
    plan_layout(cal, [], progress_cb=lambda f, m: calls.append((f, m)))
    # No per-office ticks; just the final summary tick.
    assert len(calls) == 1
    assert calls[0][0] == 1.0


def test_plan_layout_works_without_progress_callback():
    """Default behavior unchanged — progress_cb is opt-in."""
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    # Just shouldn't raise.
    layout, _ = plan_layout(cal, [_asn("X Y", "1480")])
    assert len(layout.entries) == 1


# ---------------------------------------------------------------------------
# Review-artifact rendering
# ---------------------------------------------------------------------------


def _write_white_map(path: Path, w: int = 400, h: int = 400) -> None:
    img = np.full((h, w, 3), 255, dtype=np.uint8)
    cv2.imwrite(str(path), img)


def test_render_layout_review_png_writes_a_png(tmp_path):
    map_path = tmp_path / "map.png"
    _write_white_map(map_path)
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200, img_size=(400, 400))],
    )
    layout, _ = plan_layout(cal, [_asn("Alice Smith", "1480")])
    out = tmp_path / "layout_review.png"
    render_layout_review_png(map_path, cal, layout, out)
    assert out.exists() and out.stat().st_size > 200


def test_render_layout_problems_png_writes_a_png_even_with_no_problems(tmp_path):
    map_path = tmp_path / "map.png"
    _write_white_map(map_path)
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200, img_size=(400, 400))],
    )
    layout, _ = plan_layout(cal, [_asn("Alice Smith", "1480")])
    out = tmp_path / "problems.png"
    render_layout_problems_png(map_path, cal, layout, out)
    assert out.exists()


def test_render_layout_review_raises_for_missing_map(tmp_path):
    cal = _build_cal(labels=[], rooms=[])
    layout = Layout(map_image="x", map_hash="", entries=[])
    with pytest.raises(FileNotFoundError):
        render_layout_review_png(tmp_path / "missing.png", cal, layout, tmp_path / "o.png")


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------


def _write_calibration(tmp_path: Path, cal: Calibration) -> Path:
    p = tmp_path / "calibration.json"
    save_calibration(cal, p)
    return p


def _write_csv_assignments(tmp_path: Path, rows: list[tuple[str, str, str]]) -> Path:
    p = tmp_path / "people.csv"
    with p.open("w", encoding="utf-8") as f:
        f.write("Full Name,Office number,Team\n")
        for name, office, team in rows:
            f.write(f"{name},{office},{team}\n")
    return p



# ---------------------------------------------------------------------------
# Name-abbreviation ladder (first-name-preferring)
# ---------------------------------------------------------------------------


def test_name_forms_single_token_is_itself():
    from officemapmaker.layout import _name_forms  # type: ignore[attr-defined]
    assert _name_forms("Anna") == ["Anna"]


def test_name_forms_two_token_ladder():
    from officemapmaker.layout import _name_forms  # type: ignore[attr-defined]
    assert _name_forms("Geeven Singh") == ["Geeven Singh", "Geeven S.", "Geeven"]


def test_name_forms_three_token_ladder():
    from officemapmaker.layout import _name_forms  # type: ignore[attr-defined]
    assert _name_forms("Sai Ram Kuchibhatla") == [
        "Sai Ram Kuchibhatla",
        "Sai Ram K.",
        "Sai R. K.",
        "Sai R.",
        "Sai",
    ]


def test_name_forms_four_token_ladder_length_is_seven():
    from officemapmaker.layout import _name_forms  # type: ignore[attr-defined]
    forms = _name_forms("A B C D")
    # 2*N - 1 = 7
    assert len(forms) == 7
    assert forms[0] == "A B C D"
    assert forms[-1] == "A"


# ---------------------------------------------------------------------------
# Wrap variants (whitespace-to-newline)
# ---------------------------------------------------------------------------


def test_wrap_variants_single_token_is_itself():
    from officemapmaker.layout import _wrap_variants  # type: ignore[attr-defined]
    assert _wrap_variants("Anna") == ["Anna"]


def test_wrap_variants_two_tokens_yields_no_wrap_then_wrap():
    from officemapmaker.layout import _wrap_variants  # type: ignore[attr-defined]
    # 1 gap -> 2 variants. No-wrap first (fewer lines).
    variants = _wrap_variants("Geeven Singh")
    assert variants == ["Geeven Singh", "Geeven\nSingh"]


def test_wrap_variants_three_tokens_sorted_by_line_count_then_max_line():
    from officemapmaker.layout import _wrap_variants  # type: ignore[attr-defined]
    variants = _wrap_variants("Conference Room (12)")
    # 3 tokens -> 2 gaps -> 4 variants.
    # Line counts: variants[0] = 1 line, variants[1..2] = 2 lines, variants[3] = 3 lines.
    line_counts = [v.count("\n") + 1 for v in variants]
    assert line_counts == [1, 2, 2, 3]
    assert variants[0] == "Conference Room (12)"
    # The 3-line variant is "Conference\nRoom\n(12)".
    assert variants[-1] == "Conference\nRoom\n(12)"
    # Both 2-line variants are present.
    assert set(variants[1:3]) == {"Conference\nRoom (12)", "Conference Room\n(12)"}


# ---------------------------------------------------------------------------
# Wrap is preferred over abbreviation when it fits
# ---------------------------------------------------------------------------


def test_plan_layout_prefers_wrap_over_abbreviation_for_long_room_label():
    """A tall but narrow room should wrap a multi-word name onto two
    lines (preserving every word at full length) rather than abbreviate
    it. Regression for the "Conference Room (12)" -> "C. Room (12)"
    bug from the millennium B run.
    """
    # 130px wide, 220px tall: too narrow for "Conference Room (12)" on
    # one line at any readable font, but plenty of vertical headroom
    # for the wrapped two-line variant, and "Conference" alone fits in
    # the width at min font.
    cal = _build_cal(
        labels=[_office_label("1189", room_id=1, fill_seed=(140, 170))],
        rooms=[_rect_room(1, 75, 60, 130, 220)],
    )
    layout, _ = plan_layout(cal, [_asn("Conference Room (12)", "1189")])
    entry = layout.entries[0]
    # FULL means the planner kept every word at full length (used wrap,
    # not abbreviation).
    assert entry.fit_strategy is FitStrategy.FULL
    rendered = entry.names[0].rendered_text
    # Wrap was actually used (newline present) and no token was
    # abbreviated to an initial.
    assert "\n" in rendered
    assert "Conference" in rendered
    assert "Room" in rendered
    assert "(12)" in rendered


# ---------------------------------------------------------------------------
# Duplicate displayed-name detection
# ---------------------------------------------------------------------------


def test_plan_layout_errors_when_two_people_in_office_share_displayed_name():
    """Two people in the SAME office whose displayed (rendered) names
    end up identical -> error. They'd be visually indistinguishable.
    """
    # Big room -> both render at their full names. Two distinct
    # ``Assignment`` rows with literally the same name string.
    cal = _build_cal(
        labels=[_office_label("1480", room_id=1, fill_seed=(150, 150))],
        rooms=[_square_room(1, 50, 50, 200)],
    )
    layout, issues = plan_layout(
        cal,
        [
            _asn("Alice Smith", "1480", row=2),
            _asn("Alice Smith", "1480", row=3),
        ],
    )
    codes = [(i.code, i.severity, i.office_id) for i in issues]
    assert ("duplicate_displayed_name_in_office", "error", "1480") in codes


def test_plan_layout_warns_when_two_people_on_map_share_displayed_name():
    """Two people in DIFFERENT offices whose displayed names collide ->
    warning (not error). The map is still navigable; the duplication
    just looks suspicious.
    """
    cal = _build_cal(
        labels=[
            _office_label("1480", room_id=1, fill_seed=(150, 150)),
            _office_label("1481", room_id=2, fill_seed=(450, 150)),
        ],
        rooms=[_square_room(1, 50, 50, 200), _square_room(2, 350, 50, 200)],
    )
    layout, issues = plan_layout(
        cal,
        [
            _asn("Alice Smith", "1480", row=2),
            _asn("Alice Smith", "1481", row=3),
        ],
    )
    map_dupes = [
        i for i in issues if i.code == "duplicate_displayed_name_on_map"
    ]
    assert len(map_dupes) >= 1
    assert all(i.severity == "warning" for i in map_dupes)


# ---------------------------------------------------------------------------
# Backward-compat: FitStrategy deserializes legacy string values
# ---------------------------------------------------------------------------


def test_fit_strategy_legacy_string_values_map_to_new_members():
    # Pre-refactor JSON files used these string values. They must still
    # load (mapped onto the new members) so we don't break existing
    # session/layout files.
    assert FitStrategy("full") is FitStrategy.FULL
    assert FitStrategy("abbreviated") is FitStrategy.ABBREVIATED
    assert FitStrategy("leader") is FitStrategy.LEADER
    # Legacy names from before the refactor:
    assert FitStrategy("shrink") is FitStrategy.FULL
    assert FitStrategy("initials") is FitStrategy.ABBREVIATED
    assert FitStrategy("last_only") is FitStrategy.ABBREVIATED
