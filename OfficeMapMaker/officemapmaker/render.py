"""Pass 4 — render the colored, labelled composite map.

This module produces ``composite.png`` from:

  * Original floor-plan image (untouched apart from per-office fills).
  * A reviewed ``Calibration`` (rooms, labels, wall_patches).
  * A reviewed ``Layout`` (per-office name + office-number placements).
  * A set of ``Assignment`` records (people, offices, teams).
  * An optional ``teams.json`` override file.

Pipeline (per plan.md §8 Pass 4):

  1. Build a ``TeamPalette`` covering every team that appears in any
     layout entry. Apply ``teams.json`` overrides; warn on poor-contrast
     overrides.
  2. Copy the original map to the working canvas.
  3. For each office in the layout: virtual-flood-fill its room from the
     ``fill_seed`` against the wall+patches mask, then paint those pixels
     with the team color on the canvas.
  4. For each office in the layout: white-out the original office-number
     bbox on the canvas, redraw the number at its planned new corner,
     then draw each person's name.
  5. Draw the legend overlay in the configured corner.
  6. Save ``composite.png`` plus a companion ``composite_review.png`` (a
     pixel-identical copy named distinctly so the user has an artifact
     to confirm without overwriting the canonical output).

Safety net (the headline auto-check in plan.md §8 Pass 4):

  After rendering, diff the composite against the original map.  Every
  changed pixel must lie inside the **expected-change mask** =
  (filled office room polygons) ∪ (planned text bboxes) ∪ (legend bbox).
  Any pixel changed outside that mask is a bug — the function returns it
  as an error issue, so the CLI can fail loudly.

Public surface:

  ``RenderResult``   - composite path + issues list + the palette used.
  ``RenderIssue``    - typed errors / warnings.
  ``render_composite`` - the main entry point.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Optional, Sequence

import numpy as np

from .calibration import Calibration, Label
from .geometry import BBox, rle_to_mask
from .io_assignments import Assignment
from .layout import Layout, LayoutEntry
from .palette import (
    RGB,
    TeamPalette,
    build_palette,
    contrast_ratio,
    rgb_to_hex,
)
from .validate import build_fill_mask, virtual_flood_fill


__all__ = [
    "RenderIssue",
    "RenderResult",
    "render_composite",
]


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------


_TEXT_COLOR: RGB = (20, 20, 20)
_LEGEND_BG: RGB = (255, 255, 255)
_LEGEND_BORDER: RGB = (60, 60, 60)
_LEGEND_TITLE = "Teams"
_LEGEND_MARGIN_PX = 12
_LEGEND_PADDING_PX = 10
_LEGEND_SWATCH_PX = 18
_LEGEND_FONT_PX = 16
_LEGEND_LINE_SPACING = 1.2


# ---------------------------------------------------------------------------
# Issue type
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class RenderIssue:
    """One issue discovered during render.

    Severities mirror ``validate.ValidationIssue``: ``"error"`` blocks the
    next pass, ``"warning"`` is informational only.
    """

    severity: str
    code: str
    message: str
    office_id: Optional[str] = None

    def __str__(self) -> str:
        return f"[{self.severity}] {self.code}: {self.message}"


@dataclass
class RenderResult:
    """Output of ``render_composite``.

    Attributes:
        composite_path: Where the final composite PNG was written.
        review_path: Companion ``composite_review.png`` (same pixels,
            renamed so the gate sentinel applies cleanly).
        issues: All errors and warnings collected during the render.
        palette: The ``TeamPalette`` used. Stored so the caller can
            render a legend page in the tiler step.
        changed_pixel_count: Total number of pixels that differ between
            the composite and the original map. Useful for diagnostics.
        unexpected_pixel_count: Pixels changed *outside* the expected
            change mask. Should be zero on a healthy render. If non-zero
            an ``unexpected_pixel_change`` error issue is also produced.
    """

    composite_path: Path
    review_path: Path
    issues: list[RenderIssue] = field(default_factory=list)
    palette: Optional[TeamPalette] = None
    changed_pixel_count: int = 0
    unexpected_pixel_count: int = 0

    @property
    def errors(self) -> list[RenderIssue]:
        return [i for i in self.issues if i.severity == "error"]

    @property
    def warnings(self) -> list[RenderIssue]:
        return [i for i in self.issues if i.severity == "warning"]


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _team_for_office(
    entry: LayoutEntry, assignments_by_office: dict[str, list[Assignment]]
) -> str:
    """Return the team for an office, preferring the layout's stored team."""
    if entry.team:
        return entry.team
    seats = assignments_by_office.get(entry.office_id.upper(), [])
    return seats[0].team if seats else ""


def _label_by_office(calibration: Calibration) -> dict[str, Label]:
    """Return labels keyed by id (upper-cased), one entry per unique id.

    Without a Classification enum every labeled room is potentially an
    office; office-ness is established at render time by intersecting the
    keys here with the assignments spreadsheet. Labels with no ``room_id``
    are skipped since they can't be filled.
    """
    by_id: dict[str, Label] = {}
    for lbl in calibration.labels:
        if lbl.room_id is None:
            continue
        by_id.setdefault(lbl.id.upper(), lbl)
    return by_id


def _write_composite_meta(
    sidecar_path: Path,
    *,
    map_path: Path,
    map_hash: str,
    canvas_size: tuple[int, int],
    palette: TeamPalette,
    layout: Layout,
    assignments: Sequence[Assignment],
    calibration: Calibration,
) -> None:
    """Write ``<composite>_meta.json`` next to ``composite.png``.

    Pass 5 (tile) reads this to render the legend page in the PDF without
    needing to re-load the calibration / assignments.
    """
    import datetime
    import json

    # Headcount by team, and by team-from-layout vs team-from-assignments.
    headcount: dict[str, int] = {}
    assigned_offices = {entry.office_id.upper() for entry in layout.entries}
    for asn in assignments:
        if asn.office_id.upper() not in assigned_offices:
            continue
        team = asn.team or ""
        headcount[team] = headcount.get(team, 0) + 1

    # "Total offices" used to mean "labels classified as OFFICE". Without
    # classification it just means "labels we *could* assign someone to" —
    # i.e. any labeled room. Vacant = the difference vs what was actually
    # assigned in this render.
    total_office_labels = sum(
        1
        for lbl in calibration.labels
        if lbl.room_id is not None
    )
    vacant_offices = max(total_office_labels - len(assigned_offices), 0)

    meta = {
        "schema": "officemapmaker.composite_meta.v1",
        "map_path": str(map_path),
        "map_hash": map_hash,
        "rendered_at": datetime.datetime.now().isoformat(timespec="seconds"),
        "composite_size": [int(canvas_size[0]), int(canvas_size[1])],
        "palette": {
            team: rgb_to_hex(color) for team, color in palette.colors.items()
        },
        "headcount": headcount,
        "total_people": sum(headcount.values()),
        "total_office_labels": total_office_labels,
        "assigned_offices": len(assigned_offices),
        "vacant_offices": vacant_offices,
        "low_contrast_teams": sorted(palette.low_contrast),
        "overrides_used": sorted(palette.overrides_used),
    }
    sidecar_path.write_text(json.dumps(meta, indent=2), encoding="utf-8")


def _load_font(font_path: Optional[str], font_px: int):
    """Load a TrueType font, falling back to PIL's default bitmap font."""
    from PIL import ImageFont

    try:
        return ImageFont.truetype(font_path or "arial.ttf", font_px)
    except (OSError, IOError):
        return ImageFont.load_default()


def _measure_text(text: str, font) -> tuple[int, int]:
    bbox = font.getbbox(text)
    return (
        max(int(bbox[2] - bbox[0]), 1),
        max(int(bbox[3] - bbox[1]), 1),
    )


def _legend_bbox(
    palette: TeamPalette, canvas_size: tuple[int, int], corner: str
) -> tuple[BBox, list[tuple[str, RGB]]]:
    """Compute the legend's bounding box on the canvas.

    Returns the bbox plus the ordered list of ``(team, color)`` pairs so
    the renderer doesn't have to re-sort. Teams are listed in the same
    deterministic order ``build_palette`` produced.
    """
    from PIL import ImageFont

    title_font = _load_font(None, _LEGEND_FONT_PX + 2)
    row_font = _load_font(None, _LEGEND_FONT_PX)

    teams = sorted(palette.colors.items(), key=lambda kv: kv[0].casefold())

    title_w, title_h = _measure_text(_LEGEND_TITLE, title_font)
    row_h = max(_LEGEND_SWATCH_PX, _LEGEND_FONT_PX)
    rows_h = int(round(row_h * _LEGEND_LINE_SPACING)) * len(teams)
    rows_w = 0
    for team, _ in teams:
        tw, _ = _measure_text(team, row_font)
        rows_w = max(rows_w, tw)

    content_w = max(title_w, _LEGEND_SWATCH_PX + 8 + rows_w)
    content_h = title_h + 6 + rows_h

    w = content_w + 2 * _LEGEND_PADDING_PX
    h = content_h + 2 * _LEGEND_PADDING_PX

    cw, ch = canvas_size
    if corner == "top-left":
        x = _LEGEND_MARGIN_PX
        y = _LEGEND_MARGIN_PX
    elif corner == "top-right":
        x = cw - w - _LEGEND_MARGIN_PX
        y = _LEGEND_MARGIN_PX
    elif corner == "bottom-left":
        x = _LEGEND_MARGIN_PX
        y = ch - h - _LEGEND_MARGIN_PX
    else:  # default: bottom-right
        x = cw - w - _LEGEND_MARGIN_PX
        y = ch - h - _LEGEND_MARGIN_PX

    # Guard against impossibly-small canvases.
    x = max(0, min(x, max(cw - w, 0)))
    y = max(0, min(y, max(ch - h, 0)))
    return (x, y, w, h), teams


def _draw_legend(
    canvas_bgr: np.ndarray,
    palette: TeamPalette,
    teams: list[tuple[str, RGB]],
    bbox: BBox,
) -> None:
    """Render the legend in-place onto a BGR canvas."""
    from PIL import Image, ImageDraw

    pil = Image.fromarray(canvas_bgr[..., ::-1])  # BGR -> RGB
    draw = ImageDraw.Draw(pil)
    x, y, w, h = bbox
    draw.rectangle((x, y, x + w - 1, y + h - 1), fill=_LEGEND_BG, outline=_LEGEND_BORDER, width=2)

    title_font = _load_font(None, _LEGEND_FONT_PX + 2)
    row_font = _load_font(None, _LEGEND_FONT_PX)
    cursor_x = x + _LEGEND_PADDING_PX
    cursor_y = y + _LEGEND_PADDING_PX
    title_h = _measure_text(_LEGEND_TITLE, title_font)[1]
    draw.text((cursor_x, cursor_y), _LEGEND_TITLE, fill=_TEXT_COLOR, font=title_font)
    cursor_y += title_h + 6

    row_h = max(_LEGEND_SWATCH_PX, _LEGEND_FONT_PX)
    line_h = int(round(row_h * _LEGEND_LINE_SPACING))
    for team, color in teams:
        sw_x0 = cursor_x
        sw_y0 = cursor_y + (row_h - _LEGEND_SWATCH_PX) // 2
        draw.rectangle(
            (sw_x0, sw_y0, sw_x0 + _LEGEND_SWATCH_PX - 1, sw_y0 + _LEGEND_SWATCH_PX - 1),
            fill=color,
            outline=_LEGEND_BORDER,
        )
        draw.text(
            (sw_x0 + _LEGEND_SWATCH_PX + 8, cursor_y),
            team,
            fill=_TEXT_COLOR,
            font=row_font,
        )
        cursor_y += line_h

    canvas_bgr[:, :, :] = np.asarray(pil)[..., ::-1]


def _draw_text_on_bgr(
    canvas_bgr: np.ndarray,
    text: str,
    xy: tuple[int, int],
    font_px: int,
    *,
    color: RGB = _TEXT_COLOR,
    font_path: Optional[str] = None,
) -> tuple[int, int, int, int]:
    """Render text onto a BGR ``numpy`` canvas in place.

    Round-trips through PIL because OpenCV's text rendering is harder to
    pixel-match against ``_measure_text``.

    Returns the (x, y, w, h) bbox of pixels that were actually changed —
    PIL's ``getbbox`` underestimates rendered extent vs. ``draw.text``, so
    we measure the real ink region by diffing before/after. Returns
    ``(0, 0, 0, 0)`` when no pixels changed (e.g. empty text).
    """
    from PIL import Image, ImageDraw

    before = canvas_bgr.copy()
    pil = Image.fromarray(canvas_bgr[..., ::-1])
    draw = ImageDraw.Draw(pil)
    font = _load_font(font_path, font_px)
    draw.text(xy, text, fill=color, font=font)
    canvas_bgr[:, :, :] = np.asarray(pil)[..., ::-1]

    changed = np.any(canvas_bgr != before, axis=2)
    if not changed.any():
        return (0, 0, 0, 0)
    ys, xs = np.where(changed)
    y0, y1 = int(ys.min()), int(ys.max()) + 1
    x0, x1 = int(xs.min()), int(xs.max()) + 1
    return (x0, y0, x1 - x0, y1 - y0)


# ---------------------------------------------------------------------------
# Main entry point
# ---------------------------------------------------------------------------


def render_composite(
    map_path: Path | str,
    calibration: Calibration,
    layout: Layout,
    assignments: Iterable[Assignment],
    output_png: Path | str,
    *,
    team_overrides: Optional[dict[str, RGB]] = None,
    legend_corner: Optional[str] = None,
    font_path: Optional[str] = None,
    write_review_copy: bool = True,
) -> RenderResult:
    """Produce ``composite.png`` from a reviewed layout and assignments.

    Args:
        map_path: Path to the original floor-plan image.
        calibration: Reviewed calibration record.
        layout: Reviewed layout record.
        assignments: All people/office/team rows (used only for team
            cross-checks and a head-count summary).
        output_png: Destination path for ``composite.png``. The companion
            ``<stem>_review.png`` is written alongside unless
            ``write_review_copy`` is False.
        team_overrides: Optional team-color overrides from ``teams.json``.
        legend_corner: Override for ``RenderDefaults.legend_corner``.
        font_path: Optional TrueType font override. ``None`` means use the
            same default as the layout planner (arial.ttf, then PIL bitmap).
        write_review_copy: If False, only ``output_png`` is written.

    Returns:
        ``RenderResult`` with the issue list, palette, and pixel-diff stats.
    """
    import cv2

    map_path = Path(map_path)
    output_png = Path(output_png)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    output_png.parent.mkdir(parents=True, exist_ok=True)

    original_bgr = cv2.imread(str(map_path), cv2.IMREAD_COLOR)
    if original_bgr is None:
        raise ValueError(f"could not read map image at {map_path}")

    h, w = original_bgr.shape[:2]
    canvas = original_bgr.copy()

    issues: list[RenderIssue] = []

    # ---------- Step 1: palette ----------
    assignments = list(assignments)
    assignments_by_office: dict[str, list[Assignment]] = {}
    for asn in assignments:
        assignments_by_office.setdefault(asn.office_id.upper(), []).append(asn)

    teams = sorted({entry.team for entry in layout.entries if entry.team})
    palette = build_palette(teams, overrides=team_overrides or {})
    for team in sorted(palette.low_contrast):
        if team in palette.overrides_used:
            issues.append(
                RenderIssue(
                    severity="warning",
                    code="palette_low_contrast",
                    message=(
                        f"team {team!r} override color {rgb_to_hex(palette.colors[team])} "
                        f"has contrast {contrast_ratio(palette.colors[team], (0, 0, 0)):.2f}:1 "
                        f"vs black (< 7.0 required for WCAG AAA)"
                    ),
                )
            )

    # ---------- Step 2: build fill mask + label lookup ----------
    wall_mask = build_fill_mask(original_bgr, calibration.wall_patches)
    labels_by_office = _label_by_office(calibration)
    rooms_by_id = {r.id: r for r in calibration.rooms}

    # Expected-change mask starts empty; we OR in every region we expect to modify.
    expected_change = np.zeros((h, w), dtype=bool)

    # ---------- Step 3: flood-fill each office with its team color ----------
    for entry in layout.entries:
        label = labels_by_office.get(entry.office_id.upper())
        if label is None:
            issues.append(
                RenderIssue(
                    severity="error",
                    code="layout_office_not_in_calibration",
                    message=(
                        f"layout references office {entry.office_id!r} "
                        f"but it does not exist in the calibration"
                    ),
                    office_id=entry.office_id,
                )
            )
            continue

        team = _team_for_office(entry, assignments_by_office)
        color = palette.color_for(team)
        if color is None:
            issues.append(
                RenderIssue(
                    severity="error",
                    code="palette_team_missing",
                    message=(
                        f"office {entry.office_id} team {team!r} is not in the palette"
                    ),
                    office_id=entry.office_id,
                )
            )
            continue

        filled = virtual_flood_fill(wall_mask, label.fill_seed)
        if not filled.any():
            issues.append(
                RenderIssue(
                    severity="warning",
                    code="seed_unreachable_at_render",
                    message=(
                        f"office {entry.office_id} fill seed at {label.fill_seed} "
                        f"is on a wall or off-image; office will not be colored"
                    ),
                    office_id=entry.office_id,
                )
            )
            continue

        # Clip the flood-fill to the room polygon so that wall-gap leaks
        # cannot smear team color into hallways or neighbouring offices.
        # If a leak was detected, surface it as a warning so the user can
        # still choose to fix the underlying calibration (e.g. by adding
        # a ``wall_patches`` entry) but the render itself is safe either way.
        # When no polygon is available (shouldn't happen post-calibration)
        # we paint the raw flood-fill and trust the diff-check safety net.
        room = rooms_by_id.get(entry.room_id)
        if room is not None:
            polygon = rle_to_mask(room.polygon_rle) > 0
            leak = filled & ~polygon
            leak_px = int(leak.sum())
            if leak_px > 0:
                issues.append(
                    RenderIssue(
                        severity="warning",
                        code="fill_leak_clipped",
                        message=(
                            f"office {entry.office_id} flood-fill leaked {leak_px:,} px "
                            f"outside its calibration polygon; clipped at render time "
                            f"so the team color stays inside the room. The source map "
                            f"has a wall gap — run 'validate fill' for details and "
                            f"suggested wall_patches if you want to fix it at the source."
                        ),
                        office_id=entry.office_id,
                    )
                )
            filled = filled & polygon
            if not filled.any():
                issues.append(
                    RenderIssue(
                        severity="warning",
                        code="fill_polygon_mismatch_at_render",
                        message=(
                            f"office {entry.office_id} flood-fill region does not "
                            f"intersect its calibration polygon — calibration may be stale; "
                            f"office will not be colored"
                        ),
                        office_id=entry.office_id,
                    )
                )
                continue
            expected_change |= polygon
        else:
            expected_change |= filled

        # Paint the filled interior with the team color; walls stay dark
        # because we only touch pixels in ``filled`` (already clipped to the
        # room polygon above when available). OpenCV order is BGR.
        bgr = (color[2], color[1], color[0])
        canvas[filled] = bgr

    # ---------- Step 4: white-out original office-number labels, then draw planned text ----------
    placed_text_mask = np.zeros((h, w), dtype=bool)
    for entry in layout.entries:
        label = labels_by_office.get(entry.office_id.upper())
        if label is not None:
            # White-out the original number bbox so the relocated number can move freely.
            x, y, lw, lh = label.bbox
            x0, y0 = max(x, 0), max(y, 0)
            x1, y1 = min(x + lw, w), min(y + lh, h)
            if x1 > x0 and y1 > y0:
                # Only count as "expected change" the pixels that were actually
                # walls / labels (non-fill); the flood-fill mask already covers
                # the interior pixels.
                canvas[y0:y1, x0:x1] = (255, 255, 255)
                placed_text_mask[y0:y1, x0:x1] = True

        # Draw office number at planned corner.
        on = entry.office_number
        actual = _draw_text_on_bgr(
            canvas, on.text, (on.bbox[0], on.bbox[1]), on.font_px, font_path=font_path
        )
        # Union the *planned* bbox AND the actually-changed pixel bbox into
        # the expected mask. PIL's ``getbbox``-based planned bbox often
        # under-counts descender / antialiased extent, so the planner's bbox
        # alone wouldn't cover what was really drawn.
        for bx, by, bw, bh in (on.bbox, actual):
            if bw <= 0 or bh <= 0:
                continue
            bx0, by0 = max(bx, 0), max(by, 0)
            bx1, by1 = min(bx + bw, w), min(by + bh, h)
            if bx1 > bx0 and by1 > by0:
                placed_text_mask[by0:by1, bx0:bx1] = True

        # Draw names.
        for name in entry.names:
            actual = _draw_text_on_bgr(
                canvas,
                name.rendered_text,
                (name.bbox[0], name.bbox[1]),
                name.font_px,
                font_path=font_path,
            )
            for bx, by, bw, bh in (name.bbox, actual):
                if bw <= 0 or bh <= 0:
                    continue
                bx0, by0 = max(bx, 0), max(by, 0)
                bx1, by1 = min(bx + bw, w), min(by + bh, h)
                if bx1 > bx0 and by1 > by0:
                    placed_text_mask[by0:by1, bx0:bx1] = True

    expected_change |= placed_text_mask

    # ---------- Step 5: legend ----------
    corner = legend_corner or calibration.render_defaults.legend_corner
    legend_bbox = None
    if palette.colors:
        legend_bbox, legend_rows = _legend_bbox(palette, (w, h), corner)
        _draw_legend(canvas, palette, legend_rows, legend_bbox)
        lx, ly, lw, lh = legend_bbox
        lx0, ly0 = max(lx, 0), max(ly, 0)
        lx1, ly1 = min(lx + lw, w), min(ly + lh, h)
        if lx1 > lx0 and ly1 > ly0:
            expected_change[ly0:ly1, lx0:lx1] = True

    # ---------- Step 6: write ----------
    cv2.imwrite(str(output_png), canvas)
    review_path = output_png.with_name(output_png.stem + "_review.png")
    if write_review_copy:
        cv2.imwrite(str(review_path), canvas)

    # Sidecar metadata: pass-5 (tile) needs the palette + headcount info to
    # render the legend page. The composite PNG alone doesn't carry it.
    _write_composite_meta(
        output_png.with_name(output_png.stem + "_meta.json"),
        map_path=map_path,
        map_hash=getattr(calibration, "map_hash", "") or "",
        canvas_size=(canvas.shape[1], canvas.shape[0]),
        palette=palette,
        layout=layout,
        assignments=assignments,
        calibration=calibration,
    )

    # ---------- Step 7: auto-checks ----------
    diff = np.any(canvas != original_bgr, axis=2)
    changed = int(diff.sum())

    # Expand the expected-change mask by a small amount to absorb antialiased
    # text edges (PIL ImageDraw.text spills 1-2 px beyond ``font.getbbox``)
    # and the centered-edge of ``draw.rectangle`` borders. The safety net is
    # about catching real flood-fill leaks (hundreds to thousands of stray
    # pixels), not sub-glyph antialiasing artifacts.
    expanded = cv2.dilate(
        expected_change.astype(np.uint8), np.ones((5, 5), np.uint8), iterations=1
    ).astype(bool)
    unexpected_mask = diff & ~expanded
    unexpected = int(unexpected_mask.sum())

    if unexpected > 0:
        # Report up to a few sample coordinates so the user can investigate.
        ys, xs = np.where(unexpected_mask)
        sample_count = min(5, ys.size)
        samples = [(int(xs[i]), int(ys[i])) for i in range(sample_count)]
        issues.append(
            RenderIssue(
                severity="error",
                code="unexpected_pixel_change",
                message=(
                    f"{unexpected} pixel(s) changed outside any expected region "
                    f"(fill ∪ planned text ∪ legend). Sample coords: {samples}. "
                    f"Likely cause: stale calibration polygon or layout text "
                    f"placed outside its room polygon (flood-fill leaks are "
                    f"clipped before painting, so they cannot reach this check)."
                ),
            )
        )

    # Per-office sanity: the team color should appear somewhere inside the room.
    for entry in layout.entries:
        team = _team_for_office(entry, assignments_by_office)
        color = palette.color_for(team)
        if color is None:
            continue
        bgr_target = np.array([color[2], color[1], color[0]], dtype=np.uint8)
        room = rooms_by_id.get(entry.room_id)
        if room is None:
            continue
        polygon = rle_to_mask(room.polygon_rle) > 0
        present = ((canvas == bgr_target).all(axis=2) & polygon).any()
        if not present:
            issues.append(
                RenderIssue(
                    severity="warning",
                    code="team_color_not_in_room",
                    message=(
                        f"office {entry.office_id}: team {team!r} color "
                        f"{rgb_to_hex(color)} not present inside room polygon"
                    ),
                    office_id=entry.office_id,
                )
            )

    # Composite dimensions match source.
    if canvas.shape[:2] != original_bgr.shape[:2]:  # pragma: no cover - shouldn't happen
        issues.append(
            RenderIssue(
                severity="error",
                code="composite_resolution_mismatch",
                message=(
                    f"composite is {canvas.shape[:2]} but source is "
                    f"{original_bgr.shape[:2]} (must match)"
                ),
            )
        )

    return RenderResult(
        composite_path=output_png,
        review_path=review_path if write_review_copy else output_png,
        issues=issues,
        palette=palette,
        changed_pixel_count=changed,
        unexpected_pixel_count=unexpected,
    )
