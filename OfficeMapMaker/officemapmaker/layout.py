"""Pass 3 — plan name placement per office.

For each office that has at least one person assigned to it, the planner:

  1. Computes the largest axis-aligned rectangle inscribed in the room's
     CC polygon (using ``geometry.largest_inscribed_rectangle``).
  2. Reserves a corner for the relocated office number.
  3. Runs the name-fit ladder:

       a. ``full``        — full names, binary-search font size from
                             ``preferred_font_px`` down to ``min_font_px``.
       b. ``abbreviated`` — uniformly shortens names in the room one
                             ladder rung at a time (see ``_name_forms``)
                             until everyone fits. Examples:
                                 ``Geeven Singh`` → ``Geeven S.`` → ``Geeven``
                                 ``Sai Ram Kuchibhatla`` → ``Sai Ram K.``
                                     → ``Sai R. K.`` → ``Sai R.`` → ``Sai``
       c. ``leader``      — render at ``min_font_px`` outside the room near
                             the nearest map margin; draw a red leader line
                             from the room centroid to the text bbox.

  4. Persists the result to ``layout.json``.

This module exposes:

  ``Layout`` / ``LayoutEntry`` / ``NameEntry`` / ``OfficeNumberPlacement``
        Serializable data model.
  ``plan_layout``
        End-to-end planner that produces a ``Layout`` plus issue list.
  ``save_layout`` / ``load_layout``
        JSON round-trip.
  ``render_layout_review_png`` / ``render_layout_problems_png``
        Human-review artifacts described in plan.md §8 Pass 3.
"""

from __future__ import annotations

import json
from collections import defaultdict
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Callable, Iterable, Optional

import numpy as np

from .calibration import Calibration, Label, RenderDefaults, Room
from .geometry import (
    BBox,
    Point,
    largest_inscribed_rectangle,
    mask_centroid,
    rle_to_mask,
)
from .io_assignments import Assignment


# ---------------------------------------------------------------------------
# Data model
# ---------------------------------------------------------------------------


class FitStrategy(str, Enum):
    """Which abbreviation tier was used for an office's names.

    Values:
        ``FULL``         — every name fit at its full spreadsheet form.
        ``ABBREVIATED``  — one or more names were shortened so the whole
                            room fits inside the inscribed area. The exact
                            shortening per name is preserved in
                            ``NameEntry.rendered_text``; see
                            :func:`_name_forms` for the ladder.
        ``LEADER``       — even the most aggressive abbreviation didn't
                            fit; names render outside the room with a
                            red leader line back to the centroid.
    """

    FULL = "full"
    ABBREVIATED = "abbreviated"
    LEADER = "leader"

    @classmethod
    def _missing_(cls, value):
        # Backward compatibility: older sessions (and tests) used
        # "shrink" / "initials" / "last_only" for the three abbreviation
        # tiers. Map them onto the new two-bucket model so old
        # layout.json files round-trip without a manual migration.
        compat = {
            "shrink": cls.FULL,
            "initials": cls.ABBREVIATED,
            "last_only": cls.ABBREVIATED,
        }
        if isinstance(value, str) and value in compat:
            return compat[value]
        return None


@dataclass(frozen=True)
class NameEntry:
    """One person's planned text placement inside (or near) an office.

    Attributes:
        full_name: Spreadsheet name, unchanged. Used by reporting.
        rendered_text: The string we'll actually draw (post-abbreviation).
        bbox: ``(x, y, w, h)`` of the rendered text on the composite.
        font_px: Pixel-height of the font used.
    """

    full_name: str
    rendered_text: str
    bbox: BBox
    font_px: int

    def to_dict(self) -> dict:
        return {
            "full_name": self.full_name,
            "rendered_text": self.rendered_text,
            "bbox": list(self.bbox),
            "font_px": self.font_px,
        }

    @staticmethod
    def from_dict(d: dict) -> "NameEntry":
        return NameEntry(
            full_name=str(d["full_name"]),
            rendered_text=str(d["rendered_text"]),
            bbox=tuple(int(v) for v in d["bbox"]),  # type: ignore[arg-type]
            font_px=int(d["font_px"]),
        )


@dataclass(frozen=True)
class OfficeNumberPlacement:
    """Where to draw the original office number after relocation."""

    text: str
    bbox: BBox
    font_px: int

    def to_dict(self) -> dict:
        return {"text": self.text, "bbox": list(self.bbox), "font_px": self.font_px}

    @staticmethod
    def from_dict(d: dict) -> "OfficeNumberPlacement":
        return OfficeNumberPlacement(
            text=str(d["text"]),
            bbox=tuple(int(v) for v in d["bbox"]),  # type: ignore[arg-type]
            font_px=int(d["font_px"]),
        )


@dataclass(frozen=True)
class LayoutEntry:
    """All layout decisions for one office room.

    Attributes:
        office_id: The label.id this entry refers to.
        room_id: The room.id this entry refers to.
        team: Team name (used at render-time for color).
        fit_strategy: Which abbreviation tier was used.
        names: Placed names, in render order (top to bottom).
        office_number: Where to draw the relocated office number.
        inscribed_rect: The LIR we fit into. Stored for review/diagnostics.
        leader_lines: ``(x1, y1, x2, y2)`` segments for any leader callouts.
    """

    office_id: str
    room_id: int
    team: str
    fit_strategy: FitStrategy
    names: list[NameEntry]
    office_number: OfficeNumberPlacement
    inscribed_rect: BBox
    leader_lines: list[tuple[int, int, int, int]] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "office_id": self.office_id,
            "room_id": self.room_id,
            "team": self.team,
            "fit_strategy": self.fit_strategy.value,
            "names": [n.to_dict() for n in self.names],
            "office_number": self.office_number.to_dict(),
            "inscribed_rect": list(self.inscribed_rect),
            "leader_lines": [list(seg) for seg in self.leader_lines],
        }

    @staticmethod
    def from_dict(d: dict) -> "LayoutEntry":
        return LayoutEntry(
            office_id=str(d["office_id"]),
            room_id=int(d["room_id"]),
            team=str(d["team"]),
            fit_strategy=FitStrategy(d["fit_strategy"]),
            names=[NameEntry.from_dict(n) for n in d["names"]],
            office_number=OfficeNumberPlacement.from_dict(d["office_number"]),
            inscribed_rect=tuple(int(v) for v in d["inscribed_rect"]),  # type: ignore[arg-type]
            leader_lines=[
                tuple(int(v) for v in seg) for seg in d.get("leader_lines", [])
            ],
        )


@dataclass
class Layout:
    """Top-level layout document persisted as ``layout.json``.

    Attributes:
        map_image: Same value as ``Calibration.map_image`` — basename only.
        map_hash: SHA recorded at planning time. Lets the next pass detect
            a map change since the layout was reviewed.
        entries: One entry per office with at least one person assigned.
            Vacant offices have no entry — they'll be rendered white.
    """

    map_image: str
    map_hash: str
    entries: list[LayoutEntry] = field(default_factory=list)

    def to_dict(self) -> dict:
        return {
            "map_image": self.map_image,
            "map_hash": self.map_hash,
            "entries": [e.to_dict() for e in self.entries],
        }

    @staticmethod
    def from_dict(d: dict) -> "Layout":
        return Layout(
            map_image=str(d["map_image"]),
            map_hash=str(d["map_hash"]),
            entries=[LayoutEntry.from_dict(e) for e in d.get("entries", [])],
        )

    def entry_by_office(self, office_id: str) -> Optional[LayoutEntry]:
        for e in self.entries:
            if e.office_id == office_id:
                return e
        return None


def save_layout(layout: Layout, path: Path | str) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8") as f:
        json.dump(layout.to_dict(), f, indent=2)


def load_layout(path: Path | str) -> Layout:
    path = Path(path)
    with path.open("r", encoding="utf-8") as f:
        data = json.load(f)
    return Layout.from_dict(data)


@dataclass(frozen=True)
class LayoutIssue:
    """One problem (or warning) discovered while planning the layout."""

    severity: str
    code: str
    message: str
    office_id: Optional[str] = None
    person: Optional[str] = None

    def __str__(self) -> str:
        prefix = f"[{self.severity}] {self.code}"
        suffix = []
        if self.office_id:
            suffix.append(f"office {self.office_id}")
        if self.person:
            suffix.append(f"person {self.person!r}")
        tail = f"  ({', '.join(suffix)})" if suffix else ""
        return f"{prefix}: {self.message}{tail}"


# ---------------------------------------------------------------------------
# Planning
# ---------------------------------------------------------------------------


def plan_layout(
    calibration: Calibration,
    assignments: Iterable[Assignment],
    *,
    map_hash: str = "",
    font_path: Optional[str] = None,
    progress_cb: Optional[Callable[[float, str], None]] = None,
) -> tuple[Layout, list[LayoutIssue]]:
    """Plan name placement for every office that has at least one assignee.

    Args:
        calibration: The reviewed calibration (already gated by the
            ``.reviewed`` sentinel in the CLI).
        assignments: Spreadsheet rows.
        map_hash: SHA of the source map at planning time. Stored in
            ``Layout.map_hash`` for the next pass's staleness gate.
        font_path: Path to a TTF used for measuring text. Defaults to
            Arial on Windows; falls back to PIL's default if missing.
        progress_cb: Optional callback invoked once per office as
            ``progress_cb(fraction, message)`` where ``fraction`` is in
            ``[0.0, 1.0]`` (offices processed / total) and ``message``
            describes the current office (e.g. ``"Planning office 47 of
            210 (1480)"``). The callback is invoked *before* each
            office's inscribed-rectangle search begins, which is the
            expensive per-office step. Skipped offices (those with no
            label in calibration) still advance the counter so the bar
            moves monotonically. Caller is responsible for any
            thread-safety / event-loop marshalling.

    Returns:
        ``(layout, issues)``. ``issues`` may contain warnings for any
        office that fell back to ``LEADER`` or ``ABBREVIATED``.
    """
    issues: list[LayoutIssue] = []

    rd = calibration.render_defaults
    min_font_px = max(8, int(round(rd.min_font_pt * rd.tile_dpi / 72)))
    preferred_font_px = max(min_font_px + 4, int(round(min_font_px * 2)))
    number_font_px = max(min_font_px, int(round(min_font_px * 1.2)))

    # Group assignments by office_id (case-insensitive — matches validate.py).
    by_office: dict[str, list[Assignment]] = defaultdict(list)
    for asn in assignments:
        by_office[asn.office_id.upper()].append(asn)

    rooms_by_id = {r.id: r for r in calibration.rooms}
    # Every labeled room is a potential office target. Office-ness is
    # established by the spreadsheet (``by_office``) intersecting this index.
    office_labels = [lab for lab in calibration.labels if lab.room_id is not None]
    # Index for fast lookup; only one office_id per label by construction.
    labels_by_id: dict[str, Label] = {lab.id.upper(): lab for lab in office_labels}

    # Group labels by their room so each room's polygon can be inflated to
    # cover every label bbox inside it (see below).
    labels_by_room: dict[int, list[Label]] = {}
    for lab in office_labels:
        labels_by_room.setdefault(lab.room_id, []).append(lab)

    # Union mask of every labeled room. Leader-line text should not land
    # inside any of these — putting a name inside someone else's office is
    # worse than going to a wider margin or accepting some hallway encroachment.
    # We also cache the decoded polygon for each labeled room so the per-
    # office loop below doesn't pay the rle_to_mask cost (~22 ms each on a
    # 3500x3500 map) twice.
    #
    # As we cache each polygon we ALSO OR in every label.bbox that belongs
    # to that room. The original office-number digits in the source image
    # are black pixels, so the connected-component polygon has digit-shaped
    # holes where the original numbers sat. Those holes shrink the largest
    # inscribed rectangle away from the digit area and force the layout
    # planner to avoid an area we're about to redraw anyway. Inflating the
    # polygon to cover the label bbox treats the digit area as room
    # interior for planning purposes (the rendered output then erases the
    # original digits via ``build_fill_mask(..., labels=...)``).
    labeled_room_ids = {lab.room_id for lab in office_labels}
    labeled_rooms_mask: Optional[np.ndarray] = None
    polygon_cache: dict[int, np.ndarray] = {}
    for room_id in labeled_room_ids:
        room = rooms_by_id.get(room_id)
        if room is None:
            continue
        pmask = rle_to_mask(room.polygon_rle) > 0
        h_mask, w_mask = pmask.shape
        for lab in labels_by_room.get(room_id, ()):
            x, y, lw, lh = lab.bbox
            x0, y0 = max(int(x), 0), max(int(y), 0)
            x1, y1 = min(int(x + lw), w_mask), min(int(y + lh), h_mask)
            if x1 > x0 and y1 > y0:
                pmask[y0:y1, x0:x1] = True
        polygon_cache[room_id] = pmask
        if labeled_rooms_mask is None:
            labeled_rooms_mask = np.zeros_like(pmask)
        labeled_rooms_mask |= pmask

    entries: list[LayoutEntry] = []
    placed_people: set[tuple[str, str]] = set()  # (office_id_upper, full_name)

    sorted_offices = sorted(by_office.items())
    total_offices = len(sorted_offices)

    # Materialize the polygons up-front so any error message can reference room.id.
    for office_idx, (office_id_upper, people) in enumerate(sorted_offices):
        if progress_cb is not None:
            progress_cb(
                office_idx / total_offices if total_offices else 1.0,
                f"Planning office {office_idx + 1} of {total_offices} "
                f"({office_id_upper})",
            )
        label = labels_by_id.get(office_id_upper)
        if label is None:
            # validate_labels already reports office_not_on_map; skip here.
            continue
        room = rooms_by_id.get(label.room_id)
        if room is None:
            issues.append(
                LayoutIssue(
                    severity="error",
                    code="office_has_no_room",
                    message=(
                        f"office {label.id} maps to room_id={label.room_id} "
                        f"but no Room with that id exists in calibration"
                    ),
                    office_id=label.id,
                )
            )
            continue

        polygon = polygon_cache.get(label.room_id)
        if polygon is None:
            polygon = rle_to_mask(room.polygon_rle) > 0
        # ``largest_inscribed_rectangle`` is O(H*W) over the entire mask;
        # on a 3500x3500 map that is ~3 seconds per office. The room itself
        # only occupies its bbox, so crop first and translate the result
        # back to map coordinates (~2000x speedup).
        bx, by, bw, bh = room.bbox
        cropped = polygon[by:by + bh, bx:bx + bw]
        local_rect = largest_inscribed_rectangle(cropped)
        # Diamond / rhombus / oval-shaped rooms have a counterintuitive
        # quirk: the largest *axis-aligned* inscribed rectangle by area
        # is a narrow tall strip on one edge, even though a wider/shorter
        # rectangle in the middle of the polygon would fit names much
        # better. (Example: Millennium B office 1015 — diamond polygon
        # whose plain LIR is 68×189 but whose middle is ~150 px wide.)
        # The fix: compute a second LIR with a height cap derived from
        # the number of people + line spacing. This makes wider-but-
        # shorter rectangles competitive against tall narrow strips.
        # For rooms where the plain LIR is already within the cap (the
        # common rectangular case), both LIRs are identical and we keep
        # the plain one (more vertical breathing room).
        n_people = max(1, len(people))
        # Reserve enough height for every name at preferred line spacing
        # plus the office number strip + a small margin. Line spacing
        # matches ``_try_fit``'s default of 1.15.
        useful_h = (
            int(round(n_people * preferred_font_px * 1.15))
            + number_font_px
            + 8
        )
        text_rect = largest_inscribed_rectangle(cropped, height_cap=useful_h)
        # Only switch to the text-optimized rectangle if it's meaningfully
        # wider AND still tall enough to fit one line at the minimum font.
        # (If the polygon is so narrow that the capped LIR loses height
        # without gaining width, stay with the plain LIR.)
        min_useful_h = int(round(min_font_px * 1.15)) + number_font_px + 4
        if text_rect[2] > local_rect[2] and text_rect[3] >= min_useful_h:
            local_rect = text_rect
        if local_rect[2] == 0 or local_rect[3] == 0:
            issues.append(
                LayoutIssue(
                    severity="error",
                    code="empty_inscribed_rect",
                    message=(
                        f"could not compute an inscribed rectangle for office "
                        f"{label.id} (room polygon is empty)"
                    ),
                    office_id=label.id,
                )
            )
            continue
        rect = (
            local_rect[0] + bx,
            local_rect[1] + by,
            local_rect[2],
            local_rect[3],
        )

        # The first team listed wins for the office's color; warn if there's disagreement.
        teams = {p.team for p in people}
        team = sorted(teams)[0]
        if len(teams) > 1:
            issues.append(
                LayoutIssue(
                    severity="warning",
                    code="mixed_teams_in_office",
                    message=(
                        f"office {label.id} has people from multiple teams "
                        f"({sorted(teams)!r}); using {team!r} for color"
                    ),
                    office_id=label.id,
                )
            )

        entry, entry_issues = _plan_one_office(
            label=label,
            room=room,
            polygon=polygon,
            rect=rect,
            people=people,
            team=team,
            min_font_px=min_font_px,
            preferred_font_px=preferred_font_px,
            number_font_px=number_font_px,
            font_path=font_path,
            labeled_rooms_mask=labeled_rooms_mask,
        )
        entries.append(entry)
        issues.extend(entry_issues)
        for p in people:
            placed_people.add((office_id_upper, p.name))

    # Sanity check: every spreadsheet person ended up in exactly one entry.
    expected = {(asn.office_id.upper(), asn.name) for asn in assignments}
    missing = expected - placed_people
    for office_id_upper, name in sorted(missing):
        issues.append(
            LayoutIssue(
                severity="error",
                code="person_not_placed",
                message=(
                    f"{name!r} (office {office_id_upper}) was not placed in any "
                    f"layout entry — usually means the office is unknown/hallway/leak"
                ),
                office_id=office_id_upper,
                person=name,
            )
        )

    # Duplicate displayed-name detection. Two people whose abbreviated
    # text comes out identical confuse anyone reading the map who knows
    # the full spreadsheet name. Same-office collisions are errors (the
    # room can never be disambiguated); floor-wide collisions are
    # warnings (a reader who knows which area to look in is usually OK).
    issues.extend(_detect_duplicate_displayed_names(entries))

    layout = Layout(
        map_image=calibration.map_image,
        map_hash=map_hash or calibration.map_hash,
        entries=entries,
    )
    if progress_cb is not None:
        progress_cb(1.0, f"Planned {total_offices} office(s)")
    return layout, issues


def _plan_one_office(
    *,
    label: Label,
    room: Room,
    polygon: np.ndarray,
    rect: BBox,
    people: list[Assignment],
    team: str,
    min_font_px: int,
    preferred_font_px: int,
    number_font_px: int,
    font_path: Optional[str],
    labeled_rooms_mask: Optional[np.ndarray] = None,
) -> tuple[LayoutEntry, list[LayoutIssue]]:
    """Plan one office's layout. Returns (entry, issues)."""
    issues: list[LayoutIssue] = []

    # Two layout strategies share most of this function:
    #
    #   A. "Polygon-number": names get the *full* LIR for layout, and the
    #      office number is placed anywhere in the polygon that doesn't
    #      overlap names. Optimal for L-shaped/irregular rooms where the
    #      polygon extends beyond the LIR — the L-extension absorbs the
    #      number with room to spare while names use the LIR's full
    #      height (bigger font, better vertical centering).
    #
    #   B. "Reserved-strip": names get the LIR minus a bottom strip; the
    #      office number lives in that strip. Optimal for rectangular
    #      rooms where polygon ≈ LIR and strategy A would have to crowd
    #      the number on top of a name.
    #
    # We try A first; if its polygon-anywhere number placement reports
    # ``crowded`` (no clear spot outside name bboxes), we fall back to B.
    rect_x, rect_y, rect_w, rect_h = rect
    number_text = label.id
    number_size = _measure_text(number_text, number_font_px, font_path)
    reserved_h = number_size[1] + 4
    names_area: BBox = (
        rect_x,
        rect_y,
        rect_w,
        max(rect_h - reserved_h, 0),
    )

    # Abbreviation ladder is now per-level (0..max_level) rather than
    # per-strategy enum. Level 0 is full names; higher levels progressively
    # shorten each person's name from the end while preserving the first
    # token. See ``_name_forms`` for the per-person ladder. Within each
    # tier (polygon-number → reserved-strip → full-rect → polygon-fit) we
    # try every level in order and stop at the first one that fits.
    max_level = _max_level_for(people)

    placement: Optional[list[NameEntry]] = None
    chosen: Optional[FitStrategy] = None
    chosen_level: int = 0
    crowded = False
    number_bbox: Optional[BBox] = None

    # === Strategy A: full LIR for names, number anywhere in polygon. ===
    placement_a, level_a = _fit_at_any_level(
        people=people,
        area=rect,
        max_level=max_level,
        min_px=min_font_px,
        max_px=preferred_font_px,
        font_path=font_path,
    )
    if placement_a is not None:
        nb_a, poly_crowded = _place_office_number_in_polygon(
            polygon=polygon,
            room_bbox=room.bbox,
            number_size=number_size,
            names=placement_a,
            inscribed_rect=rect,
            original_label_bbox=label.bbox,
        )
        if not poly_crowded:
            placement = placement_a
            chosen_level = level_a
            number_bbox = nb_a
            chosen = (
                FitStrategy.FULL if chosen_level == 0 else FitStrategy.ABBREVIATED
            )

    # === Strategy B: reserved-strip layout (classic). ===
    if placement is None:
        placement, chosen_level = _fit_at_any_level(
            people=people,
            area=names_area,
            max_level=max_level,
            min_px=min_font_px,
            max_px=preferred_font_px,
            font_path=font_path,
        )
        if placement is not None:
            chosen = FitStrategy.FULL if chosen_level == 0 else FitStrategy.ABBREVIATED

    if placement is None:
        # Reserved-strip layout failed — names didn't fit with the
        # number-reservation honored. Retry against the *full* inscribed
        # rect: the names get the whole room and the office number has to
        # share a corner with them. The result may visually crowd the
        # number, but that's far better than punting to a leader line that
        # could land in another office or in the hallway.
        placement, chosen_level = _fit_at_any_level(
            people=people,
            area=rect,
            max_level=max_level,
            min_px=min_font_px,
            max_px=preferred_font_px,
            font_path=font_path,
        )
        if placement is not None:
            chosen = (
                FitStrategy.FULL if chosen_level == 0 else FitStrategy.ABBREVIATED
            )
            crowded = True

    if placement is None:
        # Full inscribed rect failed too. Before giving up to LEADER, try
        # a polygon-aware fit (Bug 3): instead of requiring a single
        # axis-aligned rectangle, search the polygon mask per-pixel and
        # let each name find its own spot anywhere inside the room.
        # Handles irregular polygons (door cutouts, stairwell notches)
        # whose largest inscribed rectangle is much smaller than the
        # visible area.
        placement, chosen_level = _fit_polygon_at_any_level(
            people=people,
            polygon=polygon,
            room_bbox=room.bbox,
            max_level=max_level,
            min_px=min_font_px,
            max_px=preferred_font_px,
            font_path=font_path,
        )
        if placement is not None:
            chosen = (
                FitStrategy.FULL if chosen_level == 0 else FitStrategy.ABBREVIATED
            )
            crowded = True

    leader_lines: list[tuple[int, int, int, int]] = []
    if placement is None:
        # Even the polygon-aware fit won't hold the names — fall back to a
        # leader line that respects neighboring rooms (Bug 1 fix). The
        # leader uses the most-abbreviated form to minimize the bbox we
        # have to find clear space for.
        chosen = FitStrategy.LEADER
        chosen_level = max_level
        texts = _format_names_at_level(people, max_level)
        other_rooms_mask: Optional[np.ndarray] = None
        if labeled_rooms_mask is not None:
            other_rooms_mask = labeled_rooms_mask & ~polygon
        placement, leader_lines = _build_leader_placement(
            texts=texts,
            people=people,
            polygon=polygon,
            other_rooms_mask=other_rooms_mask,
            map_h=polygon.shape[0],
            map_w=polygon.shape[1],
            min_px=min_font_px,
            font_path=font_path,
        )

    if chosen is FitStrategy.LEADER:
        issues.append(
            LayoutIssue(
                severity="warning",
                code="leader_line_fallback",
                message=(
                    f"office {label.id} ({len(people)} people) did not fit even "
                    f"at the shortest abbreviation; rendering names outside the "
                    f"room with a leader line"
                ),
                office_id=label.id,
            )
        )
    elif chosen is FitStrategy.ABBREVIATED:
        # Find one person whose rendered form actually differs from
        # their full name -- that's the example the warning quotes so
        # the user can eyeball "how aggressive was the shortening?"
        example: Optional[tuple[str, str]] = None
        for p, t in zip(people, [n.rendered_text for n in placement]):
            if t != p.name:
                example = (p.name, t)
                break
        msg = (
            f"office {label.id} ({len(people)} people) shortened names to fit"
        )
        if example is not None:
            msg += f" (e.g. {example[0]!r} → {example[1]!r})"
        issues.append(
            LayoutIssue(
                severity="warning",
                code="abbreviation_fallback",
                message=msg,
                office_id=label.id,
            )
        )

    # Strategy A may have already set ``number_bbox`` (polygon-anywhere
    # placement succeeded). For every other path — including the leader
    # fallback — fall back to the classic LIR-corner placement.
    if number_bbox is None:
        number_bbox, number_overlaps_name = _place_office_number(
            rect=rect, number_size=number_size, names=placement
        )
        if crowded and number_overlaps_name:
            issues.append(
                LayoutIssue(
                    severity="warning",
                    code="office_number_overlaps_names",
                    message=(
                        f"office {label.id} ({len(people)} people) had no clear "
                        f"corner for the office number; the number will render on "
                        f"top of a name"
                    ),
                    office_id=label.id,
                )
            )
    office_number = OfficeNumberPlacement(
        text=number_text, bbox=number_bbox, font_px=number_font_px
    )

    entry = LayoutEntry(
        office_id=label.id,
        room_id=room.id,
        team=team,
        fit_strategy=chosen,
        names=placement,
        office_number=office_number,
        inscribed_rect=rect,
        leader_lines=leader_lines,
    )
    return entry, issues


def _place_office_number_in_polygon(
    *,
    polygon: np.ndarray,
    room_bbox: BBox,
    number_size: tuple[int, int],
    names: list[NameEntry],
    inscribed_rect: BBox,
    original_label_bbox: BBox,
    margin: int = 3,
    name_padding: int = 3,
) -> tuple[BBox, bool]:
    """Find a position for the office number anywhere inside the room
    polygon that does not overlap any placed name bbox.

    The classic placement (``_place_office_number``) confines the number
    to a corner of the LIR (largest inscribed rectangle). That works for
    rectangular rooms but wastes space in L-shaped rooms whose polygon
    extends beyond the LIR — the L-extension (door-arc cutouts,
    stairwell notches, etc.) is empty real estate the number could
    happily occupy without crowding the names.

    Tries, in order:
      1. Centered on the original label position (visually most natural —
         the number ends up where the user expects, just rendered fresh
         in the team color overlay).
      2. The four corners of the room (polygon) bbox.
      3. The four corners of the LIR (final fallback before declaring
         crowded, matching today's default).

    Returns ``(bbox, crowded)``. ``crowded=True`` means every candidate
    overlapped names or fell outside the polygon. The caller should fall
    back to the reserved-strip layout (``_place_office_number``) which
    shrinks the names area instead of compromising on number placement.
    """
    nw, nh = number_size
    rx, ry, rw, rh = room_bbox
    lx, ly, lw, lh = inscribed_rect
    ox, oy, ow, oh = original_label_bbox

    img_h, img_w = polygon.shape

    # Build the "available" mask: polygon AND NOT any name bbox (padded).
    # Inflating name bboxes by a few pixels keeps the number visually
    # detached from the names rather than touching them.
    avail = polygon.copy()
    for n in names:
        nbx, nby, nbw, nbh = n.bbox
        x1 = max(0, nbx - name_padding)
        y1 = max(0, nby - name_padding)
        x2 = min(img_w, nbx + nbw + name_padding)
        y2 = min(img_h, nby + nbh + name_padding)
        avail[y1:y2, x1:x2] = False

    def fits(x: int, y: int) -> bool:
        if x < 0 or y < 0 or x + nw > img_w or y + nh > img_h:
            return False
        return bool(avail[y:y + nh, x:x + nw].all())

    candidates: list[tuple[int, int]] = []
    # 1. Centered on the original label position.
    candidates.append((ox + ow // 2 - nw // 2, oy + oh // 2 - nh // 2))
    # 2. Polygon (room) bbox corners.
    candidates.extend([
        (rx + rw - nw - margin, ry + rh - nh - margin),  # bottom-right
        (rx + margin, ry + rh - nh - margin),            # bottom-left
        (rx + rw - nw - margin, ry + margin),            # top-right
        (rx + margin, ry + margin),                      # top-left
    ])
    # 3. LIR corners (matches the classic placement as final fallback).
    candidates.extend([
        (lx + lw - nw - margin, ly + lh - nh - margin),
        (lx + margin, ly + lh - nh - margin),
        (lx + lw - nw - margin, ly + margin),
        (lx + margin, ly + margin),
    ])

    for cx, cy in candidates:
        if fits(cx, cy):
            return ((cx, cy, nw, nh), False)

    # Nothing fits cleanly — caller decides what to do (typically falls
    # back to the reserved-strip layout via ``_place_office_number``).
    cx, cy = (lx + lw - nw - margin, ly + lh - nh - margin)
    return ((cx, cy, nw, nh), True)


def _place_office_number(
    *,
    rect: BBox,
    number_size: tuple[int, int],
    names: list[NameEntry],
) -> tuple[BBox, bool]:
    """Pick a corner of ``rect`` for the office number that avoids overlapping any
    placed name.

    Tries bottom-right, bottom-left, top-right, top-left in that order and returns
    the first one whose bbox does not intersect any ``names`` bbox. If every corner
    overlaps a name, returns the bottom-right corner together with ``True`` so the
    caller can warn.
    """
    rect_x, rect_y, rect_w, rect_h = rect
    nw, nh = number_size
    margin = 2
    corners = [
        # bottom-right (historical default — preserves prior behavior for non-crowded layouts)
        (rect_x + rect_w - nw - margin, rect_y + rect_h - nh - margin),
        # bottom-left
        (rect_x + margin, rect_y + rect_h - nh - margin),
        # top-right
        (rect_x + rect_w - nw - margin, rect_y + margin),
        # top-left
        (rect_x + margin, rect_y + margin),
    ]
    name_bboxes = [
        (n.bbox[0], n.bbox[1], n.bbox[0] + n.bbox[2], n.bbox[1] + n.bbox[3])
        for n in names
    ]

    def overlaps_any(x: int, y: int) -> bool:
        nx2, ny2 = x + nw, y + nh
        for bx1, by1, bx2, by2 in name_bboxes:
            if x < bx2 and nx2 > bx1 and y < by2 and ny2 > by1:
                return True
        return False

    for cx, cy in corners:
        if not overlaps_any(cx, cy):
            return ((cx, cy, nw, nh), False)
    cx, cy = corners[0]
    return ((cx, cy, nw, nh), True)


def _name_forms(name: str) -> list[str]:
    """Return the progressively-shorter abbreviation forms for ``name``.

    The first form is always the full name. The last form is the bare
    first token. Each intermediate form drops a token's information by
    one notch — either by abbreviating it to an initial, or by dropping
    a token that was already an initial. The first token (typically the
    given name) is preserved in every form so people stay searchable by
    first name.

    Examples:
        >>> _name_forms("Anna")
        ['Anna']
        >>> _name_forms("Geeven Singh")
        ['Geeven Singh', 'Geeven S.', 'Geeven']
        >>> _name_forms("Sai Ram Kuchibhatla")
        ['Sai Ram Kuchibhatla', 'Sai Ram K.', 'Sai R. K.', 'Sai R.', 'Sai']

    For an N-token name (N >= 2) the ladder length is ``2*N - 1``:
    abbreviate right-to-left until only the first token is still spelled
    out, then drop initials right-to-left until only the first token
    remains.
    """
    parts = name.split()
    if len(parts) <= 1:
        return [name]

    n = len(parts)
    forms = [name]
    current = list(parts)
    # Phase 1: abbreviate tokens right-to-left.
    for k in range(1, n):
        idx = n - k
        current[idx] = current[idx][0].upper() + "."
        forms.append(" ".join(current))
    # Phase 2: drop the now-initialised tokens right-to-left, leaving
    # the first (given) name standing.
    for k in range(1, n):
        forms.append(" ".join(current[:n - k]))
    return forms


def _format_names_at_level(people: list[Assignment], level: int) -> list[str]:
    """Pick the ``level``-th abbreviation form for each person.

    Shorter names are clamped to their shortest form. This keeps the
    visual style uniform per office while still respecting names that
    can't be shortened further.
    """
    out: list[str] = []
    for p in people:
        forms = _name_forms(p.name)
        out.append(forms[min(level, len(forms) - 1)])
    return out


def _wrap_variants(text: str) -> list[str]:
    """Return all whitespace-wrap variants of ``text``.

    Each whitespace gap between tokens is independently kept as a space
    or replaced with ``\\n``. The returned list is sorted by
    ``(line_count, max_line_length)`` ascending — so the no-wrap variant
    is first, then 2-line variants in order of balance, then 3-line, etc.
    Callers that want "the smallest wrap that fits in width W" can iterate
    in order and stop at the first variant whose max-line measured width
    is ``<= W``.

    A text with no internal whitespace returns ``[text]``.
    """
    parts = text.split()
    if len(parts) <= 1:
        return [text]
    n_gaps = len(parts) - 1
    variants: list[tuple[int, int, str]] = []
    for mask in range(1 << n_gaps):
        pieces = [parts[0]]
        for i in range(n_gaps):
            sep = "\n" if (mask >> i) & 1 else " "
            pieces.append(sep)
            pieces.append(parts[i + 1])
        v = "".join(pieces)
        line_count = v.count("\n") + 1
        max_line = max(len(line) for line in v.split("\n"))
        variants.append((line_count, max_line, v))
    variants.sort()
    return [v for _, _, v in variants]


def _max_level_for(people: list[Assignment]) -> int:
    """The highest abbreviation level worth trying for this room."""
    return max(len(_name_forms(p.name)) - 1 for p in people)


def _detect_duplicate_displayed_names(
    entries: list["LayoutEntry"],
) -> list["LayoutIssue"]:
    """Flag pairs (or larger groups) of people whose rendered_text matches.

    Per-office collisions (two people in the same room display the same
    text) are emitted as ``error`` issues with the office_id set —
    these are unambiguously bad because the reader can't tell the two
    people apart even within the room. Floor-wide collisions across
    multiple offices are emitted as ``warning`` issues with no office_id
    (the message lists the affected offices). When the same displayed
    text appears in both the same office *and* other offices on the map,
    both an error (for the in-office pair) and a warning (for the
    cross-office occurrences) are emitted.
    """
    out: list[LayoutIssue] = []

    # 1. Per-office collisions (error).
    for entry in entries:
        by_text: dict[str, list[str]] = defaultdict(list)
        for n in entry.names:
            by_text[n.rendered_text].append(n.full_name)
        for text, fulls in by_text.items():
            if len(fulls) <= 1:
                continue
            out.append(
                LayoutIssue(
                    severity="error",
                    code="duplicate_displayed_name_in_office",
                    message=(
                        f"office {entry.office_id} renders {len(fulls)} "
                        f"people as the same text {text!r}: "
                        f"{', '.join(repr(f) for f in fulls)}"
                    ),
                    office_id=entry.office_id,
                )
            )

    # 2. Floor-wide collisions (warning). Group by rendered_text across
    #    all entries; emit one warning per text that appears in 2+
    #    distinct offices. Same-office repeats already covered above.
    by_text_global: dict[str, list[tuple[str, str]]] = defaultdict(list)
    for entry in entries:
        for n in entry.names:
            by_text_global[n.rendered_text].append((entry.office_id, n.full_name))
    for text, occurrences in sorted(by_text_global.items()):
        offices = {office_id for office_id, _ in occurrences}
        if len(offices) <= 1:
            continue
        # Sort for stable ordering in the message.
        occurrences_sorted = sorted(occurrences)
        full_names = sorted({full for _, full in occurrences})
        office_list = ", ".join(sorted(offices))
        out.append(
            LayoutIssue(
                severity="warning",
                code="duplicate_displayed_name_on_map",
                message=(
                    f"displayed text {text!r} appears in {len(offices)} "
                    f"offices ({office_list}) for "
                    f"{', '.join(repr(n) for n in full_names)}"
                ),
            )
        )

    return out


def _fit_at_any_level(
    *,
    people: list[Assignment],
    area: BBox,
    max_level: int,
    min_px: int,
    max_px: int,
    font_path: Optional[str],
) -> tuple[Optional[list["NameEntry"]], int]:
    """Try every abbreviation level (0..max_level) against ``area``.

    Returns ``(placement, level)`` for the first level that fits, or
    ``(None, -1)`` if no level fits.
    """
    for level in range(max_level + 1):
        texts = _format_names_at_level(people, level)
        placement = _try_fit(
            texts=texts,
            people=people,
            area=area,
            min_px=min_px,
            max_px=max_px,
            font_path=font_path,
        )
        if placement is not None:
            return placement, level
    return None, -1


def _fit_polygon_at_any_level(
    *,
    people: list[Assignment],
    polygon: np.ndarray,
    room_bbox: BBox,
    max_level: int,
    min_px: int,
    max_px: int,
    font_path: Optional[str],
) -> tuple[Optional[list["NameEntry"]], int]:
    """Polygon-aware variant of :func:`_fit_at_any_level`."""
    for level in range(max_level + 1):
        texts = _format_names_at_level(people, level)
        placement = _try_fit_polygon(
            texts=texts,
            people=people,
            polygon=polygon,
            room_bbox=room_bbox,
            min_px=min_px,
            max_px=max_px,
            font_path=font_path,
        )
        if placement is not None:
            return placement, level
    return None, -1


def _try_fit(
    *,
    texts: list[str],
    people: list[Assignment],
    area: BBox,
    min_px: int,
    max_px: int,
    font_path: Optional[str],
    line_spacing: float = 1.15,
) -> Optional[list[NameEntry]]:
    """Try to fit ``texts`` into ``area`` by binary-searching font size.

    Each text is allowed to wrap on whitespace (``\\n``) if doing so lets
    the font stay larger. At each font size we pick the fewest-line wrap
    variant of each text that fits the area width; if no variant fits in
    width, the font is too big. Then we sum the per-text heights (each
    text contributes ``lines * line_h``) and check the area height.

    Returns a list of ``NameEntry`` (one per person) at the largest font
    that fits, or ``None`` if even ``min_px`` doesn't fit.
    """
    if not texts:
        return []
    area_x, area_y, area_w, area_h = area
    if area_w <= 0 or area_h <= 0:
        return None

    lo, hi = min_px, max_px
    best_layout: Optional[list[NameEntry]] = None
    while lo <= hi:
        mid = (lo + hi) // 2
        line_h = int(round(mid * line_spacing))

        # For each text, pick the fewest-line wrap variant that fits in
        # the area width. If no variant fits in width, this font is too
        # big.
        chosen: list[tuple[str, int, int, int]] = []  # (variant, w, h, lines)
        ok = True
        for text in texts:
            picked: Optional[tuple[str, int, int, int]] = None
            for v in _wrap_variants(text):
                vw, vh = _measure_text(v, mid, font_path, line_spacing)
                if vw <= area_w:
                    picked = (v, vw, vh, v.count("\n") + 1)
                    break
            if picked is None:
                ok = False
                break
            chosen.append(picked)

        if not ok:
            hi = mid - 1
            continue

        total_h = sum(c[3] * line_h for c in chosen)
        if total_h > area_h:
            hi = mid - 1
            continue

        placed: list[NameEntry] = []
        # Vertically center the block of names within ``area`` so that
        # when names take less than the full area height (common in
        # L-shaped rooms where the LIR's safe rectangle is wider than
        # it needs to be for 2-3 lines of text), they sit visually
        # centered rather than hugging the top edge.
        y_cursor = area_y + max(0, (area_h - total_h) // 2)
        for person, (variant, vw, vh, vlines) in zip(people, chosen):
            tx = area_x + (area_w - vw) // 2  # center horizontally
            placed.append(
                NameEntry(
                    full_name=person.name,
                    rendered_text=variant,
                    bbox=(tx, y_cursor, vw, vh),
                    font_px=mid,
                )
            )
            y_cursor += vlines * line_h
        best_layout = placed
        lo = mid + 1

    return best_layout


def _try_fit_polygon(
    *,
    texts: list[str],
    people: list[Assignment],
    polygon: np.ndarray,
    room_bbox: BBox,
    min_px: int,
    max_px: int,
    font_path: Optional[str],
    line_spacing: float = 1.15,
) -> Optional[list[NameEntry]]:
    """Try to fit ``texts`` anywhere inside the polygon (per-pixel check).

    Unlike :func:`_try_fit`, which requires every name to fit inside a
    single axis-aligned rectangle, this walks the polygon mask and places
    each name at the first row+column where the name's text bbox is
    fully inside the polygon. Names are still stacked top-to-bottom (so
    later names sit below earlier ones — no overlap) but each name's
    horizontal position is chosen independently.

    This is the Bug 3 fallback for irregular polygons (door cutouts,
    stairwell notches, L-shapes) whose largest inscribed rectangle is
    much smaller than the visible area.

    Returns a list of ``NameEntry`` at the largest font that fits, or
    ``None`` if even ``min_px`` doesn't fit somewhere inside the polygon.
    """
    if not texts:
        return []

    bx, by, bw, bh = room_bbox
    if bw <= 0 or bh <= 0:
        return None

    # Crop the polygon to the room bbox and build a 2D prefix sum so the
    # "is rectangle (x, y, w, h) fully inside the polygon?" query becomes
    # a constant-time numpy expression: rect_area == w * h.
    poly = polygon[by:by + bh, bx:bx + bw].astype(np.int32)
    psum = np.zeros((bh + 1, bw + 1), dtype=np.int32)
    psum[1:, 1:] = poly.cumsum(0).cumsum(1)

    def find_first_fit(
        tw: int, th: int, y_min: int
    ) -> Optional[tuple[int, int]]:
        """First (x, y) >= (0, y_min) where (x, y, tw, th) is fully inside polygon."""
        if tw <= 0 or th <= 0 or tw > bw or th > bh:
            return None
        y_min = max(0, y_min)
        if y_min + th > bh:
            return None
        # Vectorized rect-sum over every candidate (y, x): if the rect-sum
        # equals the rect area, every pixel inside the rect is inside the
        # polygon.
        a = psum[y_min + th : bh + 1, tw : bw + 1]
        b = psum[y_min + th : bh + 1, 0 : bw + 1 - tw]
        c = psum[y_min : bh + 1 - th, tw : bw + 1]
        d = psum[y_min : bh + 1 - th, 0 : bw + 1 - tw]
        full = (a - b - c + d) == tw * th
        if not full.any():
            return None
        ys, xs = np.where(full)
        return int(xs[0]), int(ys[0] + y_min)

    lo, hi = min_px, max_px
    best_layout: Optional[list[NameEntry]] = None
    while lo <= hi:
        mid = (lo + hi) // 2
        line_h = int(round(mid * line_spacing))
        placed: list[NameEntry] = []
        y_cursor = 0
        ok = True
        for person, text in zip(people, texts):
            # Try each wrap variant in order (fewest lines first) and
            # accept the first one that finds a fit at or below y_cursor.
            spot = None
            chosen_variant = None
            chosen_w = chosen_h = 0
            chosen_lines = 1
            for v in _wrap_variants(text):
                vw, vh = _measure_text(v, mid, font_path, line_spacing)
                s = find_first_fit(vw, vh, y_cursor)
                if s is not None:
                    spot = s
                    chosen_variant = v
                    chosen_w, chosen_h = vw, vh
                    chosen_lines = v.count("\n") + 1
                    break
            if spot is None:
                ok = False
                break
            x, y = spot
            placed.append(
                NameEntry(
                    full_name=person.name,
                    rendered_text=chosen_variant,
                    bbox=(bx + x, by + y, chosen_w, chosen_h),
                    font_px=mid,
                )
            )
            # Next name starts below this one, accounting for its line count.
            y_cursor = y + chosen_lines * line_h
        if ok:
            best_layout = placed
            lo = mid + 1
        else:
            hi = mid - 1

    return best_layout


def _build_leader_placement(
    *,
    texts: list[str],
    people: list[Assignment],
    polygon: np.ndarray,
    other_rooms_mask: Optional[np.ndarray],
    map_h: int,
    map_w: int,
    min_px: int,
    font_path: Optional[str],
    line_spacing: float = 1.15,
) -> tuple[list[NameEntry], list[tuple[int, int, int, int]]]:
    """Last-resort: render outside the room near the nearest map margin.

    Picks a position that does not overlap any *other* labeled room (the
    ``other_rooms_mask`` argument). Tries the four cardinal directions
    around the room bounding box, plus three vertical / horizontal
    alignments for each, then picks the candidate closest to the room
    centroid. Falls back to the original "right vs. left of map" heuristic
    only if no clean spot exists (in which case some overlap is accepted).

    Leader line goes from the room centroid to the nearer vertical edge
    of the text block.
    """
    centroid = mask_centroid(polygon)
    if centroid is None:
        cx, cy = map_w // 2, map_h // 2
    else:
        cx, cy = centroid

    sizes = [_measure_text(t, min_px, font_path) for t in texts]
    block_w = max(w for w, _ in sizes)
    line_h = int(round(min_px * line_spacing))
    block_h = line_h * len(texts)

    margin = 8
    ys, xs = np.where(polygon)
    if xs.size:
        rx_min, rx_max = int(xs.min()), int(xs.max())
        ry_min, ry_max = int(ys.min()), int(ys.max())
    else:
        rx_min, rx_max = cx, cx
        ry_min, ry_max = cy, cy
    rcx = (rx_min + rx_max) // 2
    rcy = (ry_min + ry_max) // 2

    def in_bounds(x: int, y: int) -> bool:
        return (
            margin <= x
            and x + block_w <= map_w - margin
            and margin <= y
            and y + block_h <= map_h - margin
        )

    def overlaps_others(x: int, y: int) -> bool:
        if other_rooms_mask is None:
            return False
        h, w = other_rooms_mask.shape
        x0, y0 = max(0, x), max(0, y)
        x1, y1 = min(w, x + block_w), min(h, y + block_h)
        if x0 >= x1 or y0 >= y1:
            return False
        return bool(other_rooms_mask[y0:y1, x0:x1].any())

    # Candidate positions: 4 cardinal sides × 3 alignments adjacent to the
    # room, plus a sparse grid across the rest of the map for cases where
    # the immediate neighborhood is fully covered by another labeled room.
    # Final pick is whichever feasible candidate is closest to the room
    # centroid.
    candidates: list[tuple[int, int]] = []
    x_right = rx_max + margin
    x_left = rx_min - block_w - margin
    y_below = ry_max + margin
    y_above = ry_min - block_h - margin
    for y_ref in (ry_min, rcy - block_h // 2, ry_max - block_h):
        candidates.append((x_right, y_ref))
        candidates.append((x_left, y_ref))
    for x_ref in (rx_min, rcx - block_w // 2, rx_max - block_w):
        candidates.append((x_ref, y_below))
        candidates.append((x_ref, y_above))
    # Coarse map-wide grid. Step is half the longer text dimension so we
    # don't miss thin gaps but stay O(map_w * map_h / step^2) cheap.
    step = max(16, max(block_w, block_h) // 2)
    for y in range(margin, map_h - block_h - margin + 1, step):
        for x in range(margin, map_w - block_w - margin + 1, step):
            candidates.append((x, y))

    best: Optional[tuple[int, int]] = None
    best_dist = float("inf")
    for x, y in candidates:
        if not in_bounds(x, y):
            continue
        if overlaps_others(x, y):
            continue
        bx_center = x + block_w // 2
        by_center = y + block_h // 2
        dist = (bx_center - rcx) ** 2 + (by_center - rcy) ** 2
        if dist < best_dist:
            best_dist = dist
            best = (x, y)

    if best is not None:
        text_x, text_y = best
    else:
        # No clean spot — fall back to the original "left vs. right of map"
        # heuristic. May overlap another room; nothing better available.
        if cx < map_w // 2:
            text_x = min(rx_max + margin, map_w - block_w - margin)
        else:
            text_x = max(rx_min - block_w - margin, margin)
        text_y = max(min(cy - block_h // 2, map_h - block_h - margin), margin)

    placed: list[NameEntry] = []
    y_cursor = text_y
    for person, text, (tw, th) in zip(people, texts, sizes):
        placed.append(
            NameEntry(
                full_name=person.name,
                rendered_text=text,
                bbox=(text_x, y_cursor, tw, th),
                font_px=min_px,
            )
        )
        y_cursor += line_h

    # Skip the callout when the chosen position happens to fall fully
    # inside the office's own polygon — a line from the room centroid to
    # text that's also in the room would be visually meaningless. This
    # can happen when the polygon-aware fit fell through to LEADER but
    # the grid scan happened to land back inside the room.
    block_inside_room = bool(
        polygon[text_y:text_y + block_h, text_x:text_x + block_w].all()
    )
    if block_inside_room:
        return placed, []

    # Single leader line from room centroid to the midpoint-left of the text block.
    leader_x = text_x if text_x > cx else text_x + block_w
    leader_y = text_y + block_h // 2
    leader_lines = [(int(cx), int(cy), int(leader_x), int(leader_y))]
    return placed, leader_lines


_FONT_CACHE: dict[tuple[Optional[str], int], object] = {}


def _measure_text(
    text: str, font_px: int, font_path: Optional[str], line_spacing: float = 1.15
) -> tuple[int, int]:
    """Return ``(width, height)`` in pixels of ``text`` rendered at ``font_px``.

    Handles ``\\n`` as a line break: returns ``(max_line_width,
    (n_lines-1) * line_h + last_line_height)``, where ``line_h`` is
    ``round(font_px * line_spacing)``.

    Cached because the planner re-measures the same strings many times
    during the binary search.
    """
    from PIL import ImageFont

    key = (font_path, font_px)
    font = _FONT_CACHE.get(key)
    if font is None:
        try:
            font = ImageFont.truetype(font_path or "arial.ttf", font_px)
        except (OSError, IOError):
            font = ImageFont.load_default()
        _FONT_CACHE[key] = font

    if "\n" not in text:
        bbox = font.getbbox(text)  # type: ignore[union-attr]
        w = int(bbox[2] - bbox[0])
        h = int(bbox[3] - bbox[1])
        return (max(w, 1), max(h, max(font_px // 2, 4)))

    lines = text.split("\n")
    line_metrics: list[tuple[int, int]] = []
    for line in lines:
        bbox = font.getbbox(line)  # type: ignore[union-attr]
        lw = int(bbox[2] - bbox[0])
        lh = int(bbox[3] - bbox[1])
        line_metrics.append((max(lw, 1), max(lh, max(font_px // 2, 4))))
    max_w = max(w for w, _ in line_metrics)
    line_h = int(round(font_px * line_spacing))
    total_h = (len(lines) - 1) * line_h + line_metrics[-1][1]
    return (max_w, total_h)


# ---------------------------------------------------------------------------
# Pass 3 — review artifacts
# ---------------------------------------------------------------------------


def render_layout_review_png(
    map_path: Path | str,
    calibration: Calibration,
    layout: Layout,
    output_png: Path | str,
) -> None:
    """Render ``layout_review.png``: faded map + planned text + leader lines.

    No fill is applied at this stage — this is purely about layout. The
    user uses it to confirm that the planner's choices read well before
    committing to a full render.
    """
    from PIL import Image, ImageDraw, ImageFont

    map_path = Path(map_path)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    with Image.open(map_path) as im:
        base = im.convert("RGBA")

    faded = Image.blend(
        Image.new("RGBA", base.size, (255, 255, 255, 255)),
        base,
        alpha=0.45,
    )
    draw = ImageDraw.Draw(faded)

    for entry in layout.entries:
        # Leader lines first (so text lands on top).
        for x1, y1, x2, y2 in entry.leader_lines:
            draw.line((x1, y1, x2, y2), fill=(220, 20, 30, 255), width=2)
        # Names.
        for name in entry.names:
            _draw_text(draw, name.rendered_text, name.bbox, name.font_px, color=(20, 20, 20))
        # Office number (slightly distinct color).
        _draw_text(
            draw,
            entry.office_number.text,
            entry.office_number.bbox,
            entry.office_number.font_px,
            color=(60, 60, 200),
        )

    faded.convert("RGB").save(str(output_png))


def render_layout_problems_png(
    map_path: Path | str,
    calibration: Calibration,
    layout: Layout,
    output_png: Path | str,
) -> None:
    """Render only the offices that fell back beyond ``shrink``.

    Helps the human focus on the offices most likely to need a manual
    edit in ``layout.json``.
    """
    from PIL import Image, ImageDraw

    map_path = Path(map_path)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    with Image.open(map_path) as im:
        base = im.convert("RGBA")
    faded = Image.blend(
        Image.new("RGBA", base.size, (255, 255, 255, 255)),
        base,
        alpha=0.6,
    )
    draw = ImageDraw.Draw(faded)

    rooms_by_id = {r.id: r for r in calibration.rooms}
    flagged = [e for e in layout.entries if e.fit_strategy is not FitStrategy.FULL]

    for entry in flagged:
        room = rooms_by_id.get(entry.room_id)
        if room:
            x, y, w, h = room.bbox
            color = (
                (220, 100, 0, 255)
                if entry.fit_strategy is FitStrategy.LEADER
                else (220, 180, 0, 255)
            )
            draw.rectangle((x, y, x + w, y + h), outline=color, width=3)
            _draw_text(
                draw,
                f"{entry.office_id}: {entry.fit_strategy.value}",
                (x + 4, y + 4, w - 8, 14),
                14,
                color=color[:3],
            )

        for x1, y1, x2, y2 in entry.leader_lines:
            draw.line((x1, y1, x2, y2), fill=(220, 20, 30, 255), width=2)
        for name in entry.names:
            _draw_text(draw, name.rendered_text, name.bbox, name.font_px, color=(20, 20, 20))

    faded.convert("RGB").save(str(output_png))


def _draw_text(draw, text: str, bbox: BBox, font_px: int, *, color=(0, 0, 0)) -> None:
    """Draw ``text`` at ``bbox`` top-left using the cached truetype font."""
    from PIL import ImageFont

    key = (None, font_px)
    font = _FONT_CACHE.get(key)
    if font is None:
        try:
            font = ImageFont.truetype("arial.ttf", font_px)
        except (OSError, IOError):
            font = ImageFont.load_default()
        _FONT_CACHE[key] = font
    draw.text((bbox[0], bbox[1]), text, fill=color, font=font)


__all__ = [
    "FitStrategy",
    "Layout",
    "LayoutEntry",
    "LayoutIssue",
    "NameEntry",
    "OfficeNumberPlacement",
    "load_layout",
    "plan_layout",
    "render_layout_problems_png",
    "render_layout_review_png",
    "save_layout",
]
