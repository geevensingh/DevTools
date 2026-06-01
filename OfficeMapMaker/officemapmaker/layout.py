"""Pass 3 — plan name placement per office.

For each office that has at least one person assigned to it, the planner:

  1. Computes the largest axis-aligned rectangle inscribed in the room's
     CC polygon (using ``geometry.largest_inscribed_rectangle``).
  2. Reserves a corner for the relocated office number.
  3. Runs the name-fit ladder:

       a. ``shrink``     — full names, binary-search font size from
                            ``preferred_font_px`` down to ``min_font_px``.
       b. ``initials``   — replace each first name with its initial
                            (e.g. ``Sravani Punyamurthula`` → ``S. Punyamurthula``)
                            and re-shrink.
       c. ``last_only``  — last word of each name only, re-shrink.
       d. ``leader``     — render at ``min_font_px`` outside the room near
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
    """Which abbreviation strategy was used for an office's names."""

    SHRINK = "shrink"
    INITIALS = "initials"
    LAST_ONLY = "last_only"
    LEADER = "leader"


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
        office that fell back to ``LEADER`` or ``LAST_ONLY``.
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

    # Union mask of every labeled room. Leader-line text should not land
    # inside any of these — putting a name inside someone else's office is
    # worse than going to a wider margin or accepting some hallway encroachment.
    # Built lazily on first office that needs a polygon; cached for the rest.
    labeled_room_ids = {lab.room_id for lab in office_labels}
    labeled_rooms_mask: Optional[np.ndarray] = None
    for room_id in labeled_room_ids:
        room = rooms_by_id.get(room_id)
        if room is None:
            continue
        pmask = rle_to_mask(room.polygon_rle) > 0
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

        polygon = rle_to_mask(room.polygon_rle) > 0
        rect = largest_inscribed_rectangle(polygon)
        if rect[2] == 0 or rect[3] == 0:
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

    # Reserve a small bottom strip for the office number. This is the
    # preferred layout: names cleanly above, number cleanly below, zero
    # overlap.
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

    # Try each strategy in order against the reserved (number-clear) area.
    placement: Optional[list[NameEntry]] = None
    chosen: Optional[FitStrategy] = None
    crowded = False
    for strategy in (FitStrategy.SHRINK, FitStrategy.INITIALS, FitStrategy.LAST_ONLY):
        texts = _format_names(people, strategy)
        placement = _try_fit(
            texts=texts,
            people=people,
            area=names_area,
            min_px=min_font_px,
            max_px=preferred_font_px,
            font_path=font_path,
        )
        if placement is not None:
            chosen = strategy
            break

    if placement is None:
        # Reserved-strip layout failed — names didn't fit with the
        # number-reservation honored. Retry against the *full* inscribed
        # rect: the names get the whole room and the office number has to
        # share a corner with them. The result may visually crowd the
        # number, but that's far better than punting to a leader line that
        # could land in another office or in the hallway.
        for strategy in (
            FitStrategy.SHRINK,
            FitStrategy.INITIALS,
            FitStrategy.LAST_ONLY,
        ):
            texts = _format_names(people, strategy)
            placement = _try_fit(
                texts=texts,
                people=people,
                area=rect,
                min_px=min_font_px,
                max_px=preferred_font_px,
                font_path=font_path,
            )
            if placement is not None:
                chosen = strategy
                crowded = True
                break

    leader_lines: list[tuple[int, int, int, int]] = []
    if placement is None:
        # Even the full rect won't hold the names — fall back to a leader
        # line that respects neighboring rooms (Bug 1 fix).
        chosen = FitStrategy.LEADER
        texts = _format_names(people, FitStrategy.LAST_ONLY)
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
                    f"with last-name-only; rendering names outside the room with "
                    f"a leader line"
                ),
                office_id=label.id,
            )
        )
    elif chosen is not FitStrategy.SHRINK:
        issues.append(
            LayoutIssue(
                severity="warning",
                code="abbreviation_fallback",
                message=(
                    f"office {label.id} ({len(people)} people) required {chosen.value} "
                    f"abbreviation to fit"
                ),
                office_id=label.id,
            )
        )

    # Place the office number. The default corner is bottom-right of the
    # inscribed rect (the historical placement). In the crowded fallback
    # path we try every corner and pick one that doesn't overlap a name;
    # if every corner overlaps we accept the overlap and warn.
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


def _format_names(people: list[Assignment], strategy: FitStrategy) -> list[str]:
    """Format each name per the chosen strategy. Returns one string per person."""
    out = []
    for p in people:
        parts = p.name.split()
        if not parts:
            out.append(p.name)
            continue
        if strategy is FitStrategy.SHRINK:
            out.append(p.name)
        elif strategy is FitStrategy.INITIALS:
            if len(parts) == 1:
                out.append(parts[0])
            else:
                first = parts[0][0].upper() + "."
                rest = " ".join(parts[1:])
                out.append(f"{first} {rest}")
        elif strategy is FitStrategy.LAST_ONLY:
            out.append(parts[-1])
        else:  # LEADER uses LAST_ONLY texts
            out.append(parts[-1])
    return out


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

    Returns a list of ``NameEntry`` (one per person) at the largest font that
    fits, or ``None`` if even ``min_px`` doesn't fit.
    """
    if not texts:
        return []
    area_x, area_y, area_w, area_h = area
    if area_w <= 0 or area_h <= 0:
        return None

    # Binary search for largest font_px that fits.
    lo, hi = min_px, max_px
    best_size: Optional[int] = None
    best_layout: Optional[list[NameEntry]] = None
    while lo <= hi:
        mid = (lo + hi) // 2
        sizes = [_measure_text(t, mid, font_path) for t in texts]
        line_h = int(round(mid * line_spacing))
        total_h = line_h * len(texts)
        max_w = max(w for w, _ in sizes)
        if max_w <= area_w and total_h <= area_h:
            # Fits — try larger.
            best_size = mid
            # Build layout at this size.
            placed: list[NameEntry] = []
            y_cursor = area_y
            for person, text, (tw, th) in zip(people, texts, sizes):
                tx = area_x + (area_w - tw) // 2  # center horizontally
                placed.append(
                    NameEntry(
                        full_name=person.name,
                        rendered_text=text,
                        bbox=(tx, y_cursor, tw, th),
                        font_px=mid,
                    )
                )
                y_cursor += line_h
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

    # Single leader line from room centroid to the midpoint-left of the text block.
    leader_x = text_x if text_x > cx else text_x + block_w
    leader_y = text_y + block_h // 2
    leader_lines = [(int(cx), int(cy), int(leader_x), int(leader_y))]
    return placed, leader_lines


_FONT_CACHE: dict[tuple[Optional[str], int], object] = {}


def _measure_text(
    text: str, font_px: int, font_path: Optional[str]
) -> tuple[int, int]:
    """Return ``(width, height)`` in pixels of ``text`` rendered at ``font_px``.

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
    bbox = font.getbbox(text)  # type: ignore[union-attr]
    w = int(bbox[2] - bbox[0])
    h = int(bbox[3] - bbox[1])
    # Guard against zero-height/width from empty strings.
    return (max(w, 1), max(h, max(font_px // 2, 4)))


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
    flagged = [e for e in layout.entries if e.fit_strategy is not FitStrategy.SHRINK]

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
