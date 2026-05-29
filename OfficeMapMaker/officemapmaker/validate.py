"""Pass 1 — cross-check the assignments spreadsheet against the calibration.

This module implements the label-validation step described in plan.md §8
Pass 1. The pipeline:

    1. Group calibration labels by ID.
    2. For each spreadsheet assignment, ensure the office ID matches exactly
       one ``office``-classified label. Hallway/common matches and ambiguous
       multi-match IDs are errors; missing IDs are errors too.
    3. Surface warnings for vacant offices, low-confidence labels without
       any assignment (likely OCR misreads), exact duplicate spreadsheet
       rows, and team names that differ only in case.
    4. Render ``validation_labels_review.png`` (the map with every flagged
       calibration label circled in red) so the user can scan for clusters.

Errors block the next pass. Warnings are informational.
"""

from __future__ import annotations

from collections import Counter, defaultdict
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Optional

from .calibration import Calibration, Classification, Label
from .io_assignments import Assignment


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

        office_candidates = [c for c in candidates if c.classification == Classification.OFFICE]

        if not office_candidates:
            # All matching labels are hallway/common/skip.
            classifications = sorted({c.classification.value for c in candidates})
            issues.append(
                ValidationIssue(
                    severity="error",
                    code="office_is_not_an_office",
                    message=(
                        f"row {asn.source_row}: person {asn.name!r} is assigned to "
                        f"{asn.office_id!r}, but the matching label is classified "
                        f"{'/'.join(classifications)} (not 'office')"
                    ),
                    office_id=asn.office_id,
                    person=asn.name,
                    source_row=asn.source_row,
                )
            )
            continue

        if len(office_candidates) > 1:
            rooms = sorted({c.room_id for c in office_candidates if c.room_id is not None})
            issues.append(
                ValidationIssue(
                    severity="error",
                    code="ambiguous_office",
                    message=(
                        f"row {asn.source_row}: person {asn.name!r} is assigned to "
                        f"{asn.office_id!r}, which is ambiguous — it appears as an "
                        f"office in {len(office_candidates)} rooms ({rooms!r}); "
                        f"disambiguate in calibration.json (e.g. {asn.office_id!r}-N / "
                        f"{asn.office_id!r}-S) and update the spreadsheet"
                    ),
                    office_id=asn.office_id,
                    person=asn.name,
                    source_row=asn.source_row,
                )
            )
            continue

        matched_office_ids.add(office_candidates[0].id.upper())

    # --- per-calibration-label warnings ----------------------------------
    for label in calibration.office_labels():
        if label.id.upper() in matched_office_ids:
            continue
        issues.append(
            ValidationIssue(
                severity="warning",
                code="vacant_office",
                message=(
                    f"office {label.id!r} (room {label.room_id}) has no assigned occupants; "
                    "it will be left white"
                ),
                office_id=label.id,
            )
        )
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


__all__ = [
    "ValidationIssue",
    "render_validation_labels_review_png",
    "validate_labels",
]
