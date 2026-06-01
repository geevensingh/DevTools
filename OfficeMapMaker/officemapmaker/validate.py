"""Pass 1 + Pass 2 — validate calibration against assignments and against itself.

This module implements two distinct validation passes:

    Pass 1 (validate_labels): cross-check the assignments spreadsheet against
        the calibration. Output is a list of ValidationIssue records plus an
        optional review PNG with red circles around flagged calibration labels.

    Pass 2 (validate_fill): virtual-flood-fill every ``office``-classified
        label from its ``fill_seed`` against the wall mask plus the
        calibration's ``wall_patches``. Each fill is compared to the room's
        CC polygon and to every other office's seed/fill. Output is a list
        of FillLeak records plus per-leak overlay PNGs and a rooms_overview.png.

Errors block the next pass. Warnings are informational.
"""

from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass, field
from pathlib import Path
from typing import Iterable, Optional

import numpy as np

from .calibration import Calibration, Label
from .geometry import rle_to_mask
from .io_assignments import Assignment


# Pad (in pixels) applied around every Label.bbox when treating the
# label area as "interior" for flood-fill, polygon inflation, and the
# layout planner's polygon_cache. The OCR'd label bbox is tight to the
# rasterized digit glyphs, but anti-aliased / sub-pixel-rendered digit
# ink frequently extends 1-2 pixels beyond the tight bbox. Without
# padding, those edge pixels remain as walls in wall_mask and persist
# as visible gray dots in the rendered composite (and they shrink the
# layout planner's LIR by 1-2 pixels too). Every site that inflates
# the label area MUST use this value to stay consistent — otherwise
# the flood-fill, polygon clip, and leak check disagree on what
# counts as "inside the label area" and spurious leak warnings fire.
LABEL_BBOX_PAD: int = 2


# ---------------------------------------------------------------------------
# Issue type
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class ValidationIssue:
    """One problem (or warning) discovered during label validation.

    Attributes:
        severity: ``"error"`` (blocks the next pass) or ``"warning"``.
        code: Short machine-readable key, e.g. ``"office_not_on_map"``.
        message: Human-readable detail.
        office_id: Office ID involved (None for spreadsheet-level issues like
            team-name variants and duplicate rows).
        person: Person's name for assignment-level issues; None otherwise.
        source_row: Spreadsheet row number for assignment-level issues.
    """

    severity: str
    code: str
    message: str
    office_id: Optional[str] = None
    person: Optional[str] = None
    source_row: Optional[int] = None

    def __str__(self) -> str:
        return f"[{self.severity}] {self.code}: {self.message}"


# ---------------------------------------------------------------------------
# Pure-function validation
# ---------------------------------------------------------------------------


DEFAULT_LOW_CONFIDENCE_THRESHOLD = 0.5


def validate_labels(
    calibration: Calibration,
    assignments: Iterable[Assignment],
    *,
    low_confidence_threshold: float = DEFAULT_LOW_CONFIDENCE_THRESHOLD,
) -> list[ValidationIssue]:
    """Run the auto-checks described in plan.md Pass 1.

    Args:
        calibration: A loaded ``Calibration`` (see ``calibration.load_calibration``).
        assignments: Iterable of ``Assignment`` records (see ``io_assignments.load_assignments``).
        low_confidence_threshold: Labels with OCR confidence below this AND
            no spreadsheet match emit a "probable OCR misread" warning.

    Returns:
        A list of ``ValidationIssue`` entries. Empty list means a perfectly
        clean validation; the next pass may proceed unconditionally.
    """
    assignments = list(assignments)
    issues: list[ValidationIssue] = []

    # Group calibration labels by their (uppercased) ID so spreadsheet
    # lookups are case-insensitive — the spreadsheet is human-edited.
    by_id: dict[str, list[Label]] = defaultdict(list)
    for label in calibration.labels:
        by_id[label.id.upper()].append(label)

    matched_office_ids: set[str] = set()

    # --- per-assignment checks -------------------------------------------
    for asn in assignments:
        lookup_id = asn.office_id.upper()
        candidates = by_id.get(lookup_id, [])

        if not candidates:
            issues.append(
                ValidationIssue(
                    severity="error",
                    code="office_not_on_map",
                    message=(
                        f"row {asn.source_row}: person {asn.name!r} is assigned to office "
                        f"{asn.office_id!r}, which does not appear on the map"
                    ),
                    office_id=asn.office_id,
                    person=asn.name,
                    source_row=asn.source_row,
                )
            )
            continue

        if len(candidates) > 1:
            rooms = sorted({c.room_id for c in candidates if c.room_id is not None})
            issues.append(
                ValidationIssue(
                    severity="error",
                    code="ambiguous_office",
                    message=(
                        f"row {asn.source_row}: person {asn.name!r} is assigned to "
                        f"{asn.office_id!r}, which is ambiguous — it appears in "
                        f"{len(candidates)} rooms ({rooms!r}); "
                        f"disambiguate in calibration.json (e.g. {asn.office_id!r}-N / "
                        f"{asn.office_id!r}-S) and update the spreadsheet"
                    ),
                    office_id=asn.office_id,
                    person=asn.name,
                    source_row=asn.source_row,
                )
            )
            continue

        matched_office_ids.add(candidates[0].id.upper())

    # --- per-calibration-label warnings ----------------------------------
    #
    # Without a Classification enum we can't tell which labeled rooms were
    # *expected* to be occupied — almost every label in a real map (offices,
    # hallways, lobbies, copy rooms) is a perfectly fine candidate for being
    # absent from the spreadsheet. So we drop the old ``vacant_office``
    # warning entirely (it would fire on every non-office room and bury real
    # signal). The low-confidence-AND-unmatched warning is still useful as
    # a probable-OCR-misread signal regardless of classification.
    for label in calibration.labels:
        if label.room_id is None:
            continue
        if label.id.upper() in matched_office_ids:
            continue
        if label.ocr_confidence < low_confidence_threshold:
            issues.append(
                ValidationIssue(
                    severity="warning",
                    code="low_confidence_no_match",
                    message=(
                        f"label {label.id!r} has low OCR confidence "
                        f"({label.ocr_confidence:.2f}) AND no spreadsheet match — "
                        "this is the strongest signal of an OCR misread"
                    ),
                    office_id=label.id,
                )
            )

    # --- spreadsheet-level warnings --------------------------------------
    duplicates = Counter((a.name, a.office_id, a.team) for a in assignments)
    for (name, office_id, team), count in sorted(duplicates.items()):
        if count > 1:
            issues.append(
                ValidationIssue(
                    severity="warning",
                    code="duplicate_row",
                    message=(
                        f"({name!r}, office {office_id!r}, team {team!r}) appears "
                        f"{count} times in the spreadsheet — only the first will be "
                        "placed; the rest are ignored"
                    ),
                    office_id=office_id,
                    person=name,
                )
            )

    teams_by_norm: dict[str, set[str]] = defaultdict(set)
    for asn in assignments:
        teams_by_norm[asn.team.casefold().strip()].add(asn.team)
    for variants in teams_by_norm.values():
        if len(variants) > 1:
            issues.append(
                ValidationIssue(
                    severity="warning",
                    code="team_name_variants",
                    message=(
                        f"team name has {len(variants)} case-variant forms: "
                        f"{sorted(variants)!r} — pick one canonical form and update "
                        "the spreadsheet"
                    ),
                )
            )

    return issues


# ---------------------------------------------------------------------------
# Visual review artifact
# ---------------------------------------------------------------------------


def render_validation_labels_review_png(
    map_path: Path | str,
    calibration: Calibration,
    issues: Iterable[ValidationIssue],
    output_png: Path | str,
) -> None:
    """Render ``validation_labels_review.png``: map + red circles on flagged labels.

    For each issue whose ``office_id`` matches a calibration label, draw a
    red circle around the label's bbox and annotate with the issue code.
    Issues that don't reference a calibration label (e.g. office_not_on_map,
    duplicate_row) appear only in the text report — there's no visual to
    draw for them.
    """
    from PIL import Image, ImageDraw, ImageFont

    map_path = Path(map_path)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    issues = list(issues)

    # Index calibration labels for fast lookup.
    labels_by_id: dict[str, list[Label]] = defaultdict(list)
    for label in calibration.labels:
        labels_by_id[label.id.upper()].append(label)

    with Image.open(map_path) as im:
        img = im.convert("RGBA")
    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    draw = ImageDraw.Draw(overlay)

    try:
        font = ImageFont.truetype("arial.ttf", 14)
    except (OSError, IOError):
        font = ImageFont.load_default()

    # Pair each issue with the labels it affects.
    drawn = 0
    for issue in issues:
        if issue.office_id is None:
            continue
        for label in labels_by_id.get(issue.office_id.upper(), []):
            _draw_red_circle(draw, label.bbox, code=issue.code, font=font)
            drawn += 1

    composed = Image.alpha_composite(img, overlay)
    composed.convert("RGB").save(str(output_png))


def _draw_red_circle(draw, bbox: tuple[int, int, int, int], *, code: str, font) -> None:
    """Draw a red circle around ``bbox`` plus the issue code below it."""
    x, y, w, h = bbox
    cx, cy = x + w // 2, y + h // 2
    radius = max(w, h) + 8
    draw.ellipse(
        (cx - radius, cy - radius, cx + radius, cy + radius),
        outline=(220, 20, 30, 255),
        width=2,
    )
    draw.text(
        (cx - radius, cy + radius + 2),
        code,
        fill=(220, 20, 30, 255),
        font=font,
    )


# ---------------------------------------------------------------------------
# Pass 2 — Fill leak detection
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class FillLeak:
    """One flood-fill leak discovered during pass-2 validation.

    Attributes:
        severity: ``"error"`` (blocks the next pass) or ``"warning"``.
        code: Short machine key — ``leak_oversized``,
            ``leak_into_other_office``, ``leak_oversized_vs_median``,
            ``fill_undersized``, ``seed_on_wall``.
        office_id: The source office (the room being flood-filled).
        room_id: The source room id.
        message: Human-readable detail.
        leak_into_office_id: When the fill reached another office's seed
            point, this is that other office's id (otherwise ``None``).
        overflow_bbox: ``(x, y, w, h)`` bbox of the leak overflow region
            (filled pixels that are NOT inside the source polygon).
        suggested_patch: Candidate ``[x1, y1, x2, y2]`` line segment for
            ``calibration.wall_patches`` that would close the gap.
            Heuristic only — the user typically needs to tweak.
    """

    severity: str
    code: str
    office_id: str
    room_id: int
    message: str
    leak_into_office_id: Optional[str] = None
    overflow_bbox: Optional[tuple[int, int, int, int]] = None
    suggested_patch: Optional[tuple[int, int, int, int]] = None

    def __str__(self) -> str:
        prefix = f"[{self.severity}] {self.code}"
        suffix = f"  (office {self.office_id})"
        return f"{prefix}: {self.message}{suffix}"


# Thresholds — tunable but conservative defaults for v1.
_LEAK_RATIO_VS_POLYGON = 3.0       # filled / polygon > 3× → error
_LEAK_RATIO_VS_MEDIAN = 3.0        # filled / median > 3× → warning (polygon may itself be merged)
_FILL_UNDERSIZED_RATIO = 0.30      # filled / polygon < 30% → warning (seed on wall?)


def validate_fill(
    map_path: Path | str,
    calibration: Calibration,
) -> list[FillLeak]:
    """Pass 2 — virtual flood-fill every office and report leaks.

    For each office-classified label, flood-fill from its ``fill_seed``
    against a wall mask built from the original map plus
    ``calibration.wall_patches``. A leak is any of:

    - filled area > 3× polygon area (gap in the room's walls)
    - filled area > 3× median office area (room is huge or merged)
    - fill reaches another office's ``fill_seed`` (rooms merged through a gap)
    - filled area < 30% of polygon (seed on a wall?)

    Returns the list of leaks, sorted by severity (errors first) then by
    ``office_id``.

    Raises:
        FileNotFoundError: if ``map_path`` doesn't exist.
        ValueError: if the image can't be decoded.
    """
    import cv2

    map_path = Path(map_path)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    image = cv2.imread(str(map_path), cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"could not decode image: {map_path}")

    wall_mask = build_fill_mask(
        image, calibration.wall_patches, calibration.labels
    )

    rooms_by_id = {room.id: room for room in calibration.rooms}
    # Without a Classification enum we can no longer filter to "offices only";
    # treat every label that's linked to a room as a fill candidate. The few
    # extra hallway-style fills are cheap and the leak-detection signal is
    # still valid (any leak is a real defect in the calibration).
    office_labels = [lab for lab in calibration.labels if lab.room_id is not None]
    if not office_labels:
        return []

    # Median office area for the "polygon may be merged" heuristic.
    areas = [
        rooms_by_id[lab.room_id].area_px
        for lab in office_labels
        if lab.room_id in rooms_by_id
    ]
    median_area = sorted(areas)[len(areas) // 2] if areas else 1

    # First pass: fill every office, collect masks, run per-room checks.
    fills: dict[str, np.ndarray] = {}
    polygons: dict[str, np.ndarray] = {}
    leaks: list[FillLeak] = []

    for label in office_labels:
        room = rooms_by_id.get(label.room_id)
        if room is None:
            # validate_labels already flagged this orphan.
            continue

        polygon = rle_to_mask(room.polygon_rle) > 0
        polygons[label.id] = polygon

        filled = virtual_flood_fill(wall_mask, label.fill_seed)
        fills[label.id] = filled
        filled_area = int(filled.sum())

        if filled_area == 0:
            leaks.append(
                FillLeak(
                    severity="warning",
                    code="seed_on_wall",
                    office_id=label.id,
                    room_id=room.id,
                    message=(
                        f"office {label.id}: flood-fill produced 0 pixels "
                        f"— the fill_seed {label.fill_seed} is on a wall or "
                        f"outside the map. Edit calibration.json to move it."
                    ),
                )
            )
            continue

        if filled_area > _LEAK_RATIO_VS_POLYGON * room.area_px:
            overflow = filled & ~polygon
            leaks.append(
                FillLeak(
                    severity="warning",
                    code="leak_oversized",
                    office_id=label.id,
                    room_id=room.id,
                    message=(
                        f"office {label.id}: flood-fill covered {filled_area:,} px, "
                        f"which is {filled_area / room.area_px:.1f}× its room polygon "
                        f"({room.area_px:,} px). The room's walls have a gap. "
                        f"The render auto-clips this to the polygon so the composite "
                        f"is safe; adding a wall_patches entry will silence this warning."
                    ),
                    overflow_bbox=_bbox_of_bool_mask(overflow),
                    suggested_patch=_suggest_wall_patch(filled, polygon),
                )
            )
            continue  # don't compound with median check

        if filled_area > _LEAK_RATIO_VS_MEDIAN * median_area:
            leaks.append(
                FillLeak(
                    severity="warning",
                    code="leak_oversized_vs_median",
                    office_id=label.id,
                    room_id=room.id,
                    message=(
                        f"office {label.id}: flood-fill covered {filled_area:,} px, "
                        f"which is {filled_area / median_area:.1f}× the median office "
                        f"({median_area:,} px). The polygon itself may be two rooms "
                        f"merged — check calibration_review.pdf page 2."
                    ),
                )
            )

        if filled_area < _FILL_UNDERSIZED_RATIO * room.area_px:
            leaks.append(
                FillLeak(
                    severity="warning",
                    code="fill_undersized",
                    office_id=label.id,
                    room_id=room.id,
                    message=(
                        f"office {label.id}: flood-fill covered only {filled_area:,} px, "
                        f"which is {filled_area / room.area_px:.0%} of its polygon "
                        f"({room.area_px:,} px). The fill_seed may be on a wall."
                    ),
                )
            )

    # Second pass: cross-check — fill from one office reaches another's seed.
    # This is the single most actionable leak class because the user knows
    # exactly which two rooms are merged.
    already_reported: set[tuple[str, str]] = set()
    for source in office_labels:
        if source.id not in fills:
            continue
        filled = fills[source.id]
        h, w = filled.shape
        for other in office_labels:
            if other.id == source.id:
                continue
            sx, sy = other.fill_seed
            if not (0 <= sx < w and 0 <= sy < h):
                continue
            if not filled[sy, sx]:
                continue
            key = (source.id, other.id)
            if key in already_reported:
                continue
            already_reported.add(key)
            polygon = polygons.get(source.id)
            overflow = (filled & ~polygon) if polygon is not None else filled
            leaks.append(
                FillLeak(
                    severity="warning",
                    code="leak_into_other_office",
                    office_id=source.id,
                    room_id=source.room_id,
                    message=(
                        f"office {source.id} flood-fill reached office {other.id}'s "
                        f"seed point ({sx},{sy}) — the two rooms are connected "
                        f"through a gap. The render auto-clips each office to its "
                        f"own polygon so colors stay separate; add a wall_patches "
                        f"entry to silence this warning."
                    ),
                    leak_into_office_id=other.id,
                    overflow_bbox=_bbox_of_bool_mask(overflow),
                    suggested_patch=(
                        _suggest_wall_patch(filled, polygon)
                        if polygon is not None
                        else None
                    ),
                )
            )

    leaks.sort(key=lambda l: (l.severity != "error", l.office_id))
    return leaks


def build_fill_mask(
    image_bgr: np.ndarray,
    wall_patches: Iterable[tuple[int, int, int, int]],
    labels: Optional[Iterable[Label]] = None,
) -> np.ndarray:
    """Build the wall mask used for virtual flood-fill.

    Mirrors ``calibrate._binarize`` (adaptive threshold) so the offline
    flood-fill sees the same walls the calibration saw. Then draws each
    ``wall_patches`` line at width=1 in the wall color (255). The visible
    map image is never modified — patches live only in this mask.

    If ``labels`` is provided, every label's bbox is *cleared* (set to 0,
    i.e. interior) in the wall mask before the patches are applied. The
    rationale: the original office-number digits in the source image are
    going to be replaced by our redrawn numbers, so they should not act
    as walls — leaving them in causes flood-fill leaks through broken
    digit strokes, leaves ghost digit pixels next to the redrawn number,
    and shrinks the room polygon away from the digit area for layout
    planning. Patches are drawn AFTER label clearing so a patch can
    still close a gap that happens to lie inside a label bbox.
    """
    import cv2

    gray = cv2.cvtColor(image_bgr, cv2.COLOR_BGR2GRAY)
    wall_mask = cv2.adaptiveThreshold(
        gray,
        maxValue=255,
        adaptiveMethod=cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
        thresholdType=cv2.THRESH_BINARY_INV,
        blockSize=15,
        C=10,
    )
    if labels is not None:
        h, w = wall_mask.shape
        for lab in labels:
            x, y, lw, lh = lab.bbox
            x0 = max(int(x) - LABEL_BBOX_PAD, 0)
            y0 = max(int(y) - LABEL_BBOX_PAD, 0)
            x1 = min(int(x + lw) + LABEL_BBOX_PAD, w)
            y1 = min(int(y + lh) + LABEL_BBOX_PAD, h)
            if x1 > x0 and y1 > y0:
                wall_mask[y0:y1, x0:x1] = 0
    for x1, y1, x2, y2 in wall_patches:
        cv2.line(wall_mask, (x1, y1), (x2, y2), 255, thickness=1)
    return wall_mask


def virtual_flood_fill(
    wall_mask: np.ndarray, seed: tuple[int, int]
) -> np.ndarray:
    """Flood-fill the interior (0-pixels in ``wall_mask``) from ``seed``.

    Returns a boolean mask of pixels reached by the fill. If the seed is
    on a wall (or off the image), returns an all-False mask.
    """
    import cv2

    h, w = wall_mask.shape
    sx, sy = seed
    if not (0 <= sx < w and 0 <= sy < h):
        return np.zeros((h, w), dtype=bool)
    if wall_mask[sy, sx] != 0:
        return np.zeros((h, w), dtype=bool)

    # 3-state canvas: walls=1, interior=0; the fill repaints reachable
    # 0-pixels as 2 (a value distinct from both walls and unreachable interior).
    canvas = (wall_mask > 0).astype(np.uint8)
    flood_mask = np.zeros((h + 2, w + 2), dtype=np.uint8)
    cv2.floodFill(canvas, flood_mask, (sx, sy), 2)
    return canvas == 2


def _bbox_of_bool_mask(
    mask: np.ndarray,
) -> Optional[tuple[int, int, int, int]]:
    """Return ``(x, y, w, h)`` bbox of True pixels, or ``None`` if empty."""
    if not mask.any():
        return None
    ys, xs = np.where(mask)
    x, y = int(xs.min()), int(ys.min())
    w = int(xs.max()) - x + 1
    h = int(ys.max()) - y + 1
    return (x, y, w, h)


def _suggest_wall_patch(
    filled: np.ndarray, polygon: np.ndarray
) -> Optional[tuple[int, int, int, int]]:
    """Suggest a wall_patches segment that would close a leak.

    Heuristic: find the "bridge" — overflow pixels (filled AND NOT polygon)
    that are 4-connected to the source polygon via 1-pixel dilation. The
    bridge centroid tells us roughly where the gap is; the bridge's
    bbox orientation tells us which way the patch should run (perpendicular
    to the bridge's long axis closes the gap most effectively).

    Returns a 12-pixel segment centered on the bridge centroid, or
    ``None`` if there's no overflow. The user typically tweaks the
    endpoints by hand before committing to ``calibration.wall_patches``.
    """
    import cv2

    overflow = filled & ~polygon
    if not overflow.any():
        return None

    polygon_u8 = (polygon.astype(np.uint8)) * 255
    dilated = cv2.dilate(polygon_u8, np.ones((3, 3), np.uint8), iterations=1)
    bridge = overflow & (dilated > 0)
    if not bridge.any():
        # Pathological case (overflow exists but isn't adjacent to source);
        # fall back to using the whole overflow region.
        bridge = overflow

    ys, xs = np.where(bridge)
    cx, cy = int(round(xs.mean())), int(round(ys.mean()))
    x_span = int(xs.max() - xs.min()) + 1
    y_span = int(ys.max() - ys.min()) + 1
    half_len = 6  # 12-pixel patch total

    if x_span >= y_span:
        # Bridge is wide horizontally → suggest a vertical wall segment.
        return (cx, cy - half_len, cx, cy + half_len)
    return (cx - half_len, cy, cx + half_len, cy)


# ---------------------------------------------------------------------------
# Pass 2 — review artifacts (per-leak overlay + rooms overview)
# ---------------------------------------------------------------------------


def render_leak_overlay_png(
    map_path: Path | str,
    calibration: Calibration,
    leak: FillLeak,
    output_png: Path | str,
) -> None:
    """Render ``leaks/room-<id>.png`` for one leak.

    The output is a faded copy of the original map with the leaked
    fill painted bright cyan, the source seed marked in green, the
    reached-other-office seed (if any) marked in red, and the suggested
    wall_patches segment drawn in magenta.
    """
    from PIL import Image, ImageDraw, ImageFont
    import cv2

    map_path = Path(map_path)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    image = cv2.imread(str(map_path), cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"could not decode image: {map_path}")

    wall_mask = build_fill_mask(
        image, calibration.wall_patches, calibration.labels
    )
    labels_by_id = {lab.id: lab for lab in calibration.labels}

    source = labels_by_id.get(leak.office_id)
    if source is None:
        raise ValueError(
            f"leak references unknown office_id {leak.office_id}"
        )

    filled = virtual_flood_fill(wall_mask, source.fill_seed)

    rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    base = Image.fromarray(rgb).convert("RGBA")
    # Fade the base by blending toward white so the leak overlay stands out.
    faded = Image.blend(
        Image.new("RGBA", base.size, (255, 255, 255, 255)),
        base,
        alpha=0.45,
    )

    h, w = wall_mask.shape
    cyan_rgba = np.zeros((h, w, 4), dtype=np.uint8)
    cyan_rgba[..., 0] = 0
    cyan_rgba[..., 1] = 200
    cyan_rgba[..., 2] = 220
    cyan_rgba[..., 3] = np.where(filled, 150, 0).astype(np.uint8)
    overlay = Image.alpha_composite(
        Image.new("RGBA", base.size, (0, 0, 0, 0)),
        Image.fromarray(cyan_rgba, "RGBA"),
    )
    composed = Image.alpha_composite(faded, overlay)

    draw = ImageDraw.Draw(composed)
    try:
        font = ImageFont.truetype("arial.ttf", 14)
    except (OSError, IOError):
        font = ImageFont.load_default()

    sx, sy = source.fill_seed
    draw.ellipse(
        (sx - 6, sy - 6, sx + 6, sy + 6),
        outline=(0, 160, 0, 255),
        width=2,
    )
    draw.text(
        (sx + 8, sy - 8),
        f"{leak.office_id} (source)",
        fill=(0, 130, 0, 255),
        font=font,
    )

    if leak.leak_into_office_id and leak.leak_into_office_id in labels_by_id:
        other = labels_by_id[leak.leak_into_office_id]
        ox, oy = other.fill_seed
        draw.ellipse(
            (ox - 6, oy - 6, ox + 6, oy + 6),
            outline=(220, 20, 30, 255),
            width=2,
        )
        draw.text(
            (ox + 8, oy - 8),
            f"{leak.leak_into_office_id} (reached)",
            fill=(220, 20, 30, 255),
            font=font,
        )

    if leak.suggested_patch:
        x1, y1, x2, y2 = leak.suggested_patch
        draw.line((x1, y1, x2, y2), fill=(220, 0, 220, 255), width=3)
        draw.text(
            (max(x1, x2) + 6, min(y1, y2) - 4),
            f"patch [{x1},{y1},{x2},{y2}]",
            fill=(180, 0, 180, 255),
            font=font,
        )

    composed.convert("RGB").save(str(output_png))


def render_rooms_overview_png(
    map_path: Path | str,
    calibration: Calibration,
    output_png: Path | str,
) -> None:
    """Render every office room in a distinct color over a faded map.

    Lets the human eyeball the whole floor for merged-room patterns
    (two rooms colored as one means the underlying CC polygon already
    merged them in calibration — needs a ``wall_patches`` entry to fix).
    """
    from PIL import Image
    import cv2

    map_path = Path(map_path)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    image = cv2.imread(str(map_path), cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"could not decode image: {map_path}")

    rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    h, w = rgb.shape[:2]
    base = Image.fromarray(rgb).convert("RGBA")
    faded = Image.blend(
        Image.new("RGBA", base.size, (255, 255, 255, 255)),
        base,
        alpha=0.45,
    )

    rooms_by_id = {r.id: r for r in calibration.rooms}
    office_labels = [lab for lab in calibration.labels if lab.room_id is not None]
    overlay_arr = np.zeros((h, w, 4), dtype=np.uint8)

    n = max(len(office_labels), 1)
    for i, label in enumerate(office_labels):
        room = rooms_by_id.get(label.room_id)
        if room is None:
            continue
        hue = int(180 * i / n)  # OpenCV HSV hue range is 0-180
        rgb_arr = cv2.cvtColor(
            np.array([[[hue, 130, 230]]], dtype=np.uint8),
            cv2.COLOR_HSV2RGB,
        )[0, 0]
        polygon = rle_to_mask(room.polygon_rle) > 0
        overlay_arr[polygon, 0] = rgb_arr[0]
        overlay_arr[polygon, 1] = rgb_arr[1]
        overlay_arr[polygon, 2] = rgb_arr[2]
        overlay_arr[polygon, 3] = 110

    composed = Image.alpha_composite(
        faded, Image.fromarray(overlay_arr, "RGBA")
    )
    composed.convert("RGB").save(str(output_png))


__all__ = [
    "FillLeak",
    "ValidationIssue",
    "build_fill_mask",
    "render_leak_overlay_png",
    "render_rooms_overview_png",
    "render_validation_labels_review_png",
    "validate_fill",
    "validate_labels",
    "virtual_flood_fill",
]
