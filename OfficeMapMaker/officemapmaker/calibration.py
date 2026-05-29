"""Data model + JSON I/O for ``calibration.json``.

This module defines the typed Python representation of the calibration file
described in the plan / README. It is responsible for serializing to and
deserializing from JSON, plus a couple of convenience lookups
(``label_by_id``, ``room_by_id``). The actual *production* of a calibration
from a map image (OCR + connected components) lives in the ``calibrate``
module — this one knows nothing about OpenCV or Tesseract.

The on-disk format is hand-editable JSON with 2-space indentation. We
deliberately use plain ``json`` (not ``jsonc``) for write-time and tolerate
comments only via the documentation; users edit values, not structure.
"""

from __future__ import annotations

import hashlib
import json
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Any, Optional


# ---------------------------------------------------------------------------
# Sub-records
# ---------------------------------------------------------------------------


@dataclass
class Label:
    """One OCR-detected label associated with a room.

    Attributes:
        id: The room identifier as a string (preserves leading zeros and
            alphanumerics like ``"1479A"`` / ``"MER101"``). Duplicate IDs
            across the floor must be disambiguated by the user
            (e.g. ``"1003-N"`` vs ``"1003-S"``).
        bbox: ``(x, y, w, h)`` of the OCR-detected label glyphs on the map.
        room_id: The numeric ID of the ``Room`` this label is associated with.
            ``None`` means "orphan label" (no enclosing room found) — that's
            a calibration error to be resolved by the user.
        fill_seed: ``(x, y)`` pixel used as the flood-fill seed for this room.
            Defaults to the room polygon's centroid during calibration.
        ocr_confidence: Tesseract's per-word confidence in [0.0, 1.0]. Used
            to surface low-confidence labels in the review PDF.
        notes: Free-form text the user can add to remember why a manual edit
            was made. Never read by the tool.

    Note: Older calibrations stored a 4-way ``classification`` enum (office /
    hallway / common / skip) which was dropped because the heuristic was
    unreliable. The loader silently discards any legacy ``classification``
    key for backwards compatibility. Office-ness is now derived from the
    spreadsheet (a label is "an office" iff its id appears in the
    assignments file).
    """

    id: str
    bbox: tuple[int, int, int, int]
    room_id: Optional[int]
    fill_seed: tuple[int, int]
    ocr_confidence: float
    notes: str = ""

    def to_dict(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "bbox": list(self.bbox),
            "room_id": self.room_id,
            "fill_seed": list(self.fill_seed),
            "ocr_confidence": round(float(self.ocr_confidence), 4),
            "notes": self.notes,
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "Label":
        try:
            return cls(
                id=str(d["id"]),
                bbox=tuple(int(v) for v in d["bbox"]),  # type: ignore[arg-type]
                room_id=(int(d["room_id"]) if d.get("room_id") is not None else None),
                fill_seed=tuple(int(v) for v in d["fill_seed"]),  # type: ignore[arg-type]
                ocr_confidence=float(d.get("ocr_confidence", 0.0)),
                notes=str(d.get("notes", "")),
            )
        except (KeyError, ValueError, TypeError) as e:
            raise CalibrationFormatError(f"invalid Label entry {d!r}: {e}") from e


@dataclass
class Room:
    """One connected-component room polygon.

    Attributes:
        id: Stable integer ID assigned at calibration time. Referenced by
            ``Label.room_id``.
        polygon_rle: Base64-zlib-bitpacked binary mask of this room's pixels,
            same dimensions as the source map. Use
            ``geometry.rle_to_mask(room.polygon_rle)`` to materialize.
        area_px: Pixel count of the room polygon (cached so we don't have
            to decode the RLE just to compute it).
        bbox: ``(x, y, w, h)`` of the room polygon's bounding box.
    """

    id: int
    polygon_rle: str
    area_px: int
    bbox: tuple[int, int, int, int]

    def to_dict(self) -> dict[str, Any]:
        return {
            "id": self.id,
            "polygon_rle": self.polygon_rle,
            "area_px": self.area_px,
            "bbox": list(self.bbox),
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "Room":
        try:
            return cls(
                id=int(d["id"]),
                polygon_rle=str(d["polygon_rle"]),
                area_px=int(d["area_px"]),
                bbox=tuple(int(v) for v in d["bbox"]),  # type: ignore[arg-type]
            )
        except (KeyError, ValueError, TypeError) as e:
            raise CalibrationFormatError(f"invalid Room entry {d!r}: {e}") from e


@dataclass
class RenderDefaults:
    """Render defaults persisted with the calibration. Per plan Section 9."""

    min_font_pt: int = 7
    preferred_font: str = "Segoe UI"
    tile_dpi: int = 150
    tile_paper: str = "letter"
    tile_overlap_in: float = 0.25
    legend_corner: str = "bottom-right"

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "RenderDefaults":
        # Be liberal with missing keys — calibration files generated by older
        # versions of the tool may not contain every field. Use defaults.
        defaults = cls()
        return cls(
            min_font_pt=int(d.get("min_font_pt", defaults.min_font_pt)),
            preferred_font=str(d.get("preferred_font", defaults.preferred_font)),
            tile_dpi=int(d.get("tile_dpi", defaults.tile_dpi)),
            tile_paper=str(d.get("tile_paper", defaults.tile_paper)),
            tile_overlap_in=float(d.get("tile_overlap_in", defaults.tile_overlap_in)),
            legend_corner=str(d.get("legend_corner", defaults.legend_corner)),
        )


# ---------------------------------------------------------------------------
# Top-level container
# ---------------------------------------------------------------------------


@dataclass
class Calibration:
    """The full calibration record persisted as ``calibration.json``.

    Attributes:
        map_image: Path of the map image, *relative to the calibration file*.
            This lets a calibration travel with its map (e.g., committed
            together under ``samples/``).
        map_hash: ``"sha256:<hex>"`` of the map image at calibration time.
            Used by later passes to refuse to run against a different map
            without explicit re-calibration.
        labels: One ``Label`` per OCR-detected room number.
        rooms: One ``Room`` per CC polygon associated with at least one label.
        wall_patches: List of ``[x1, y1, x2, y2]`` line segments drawn onto
            the *fill mask* (only) to close gaps in walls that cause flood-fill
            leaks. The visible map is never altered.
        render_defaults: Per-map render settings (font, DPI, etc.).
    """

    map_image: str
    map_hash: str
    labels: list[Label] = field(default_factory=list)
    rooms: list[Room] = field(default_factory=list)
    wall_patches: list[tuple[int, int, int, int]] = field(default_factory=list)
    render_defaults: RenderDefaults = field(default_factory=RenderDefaults)

    # -- Convenience lookups ------------------------------------------------

    def label_by_id(self, label_id: str) -> Optional[Label]:
        for lab in self.labels:
            if lab.id == label_id:
                return lab
        return None

    def room_by_id(self, room_id: int) -> Optional[Room]:
        for room in self.rooms:
            if room.id == room_id:
                return room
        return None

    def labels_for_room(self, room_id: int) -> list[Label]:
        return [lab for lab in self.labels if lab.room_id == room_id]

    # -- Serialization ------------------------------------------------------

    def to_dict(self) -> dict[str, Any]:
        return {
            "map_image": self.map_image,
            "map_hash": self.map_hash,
            "labels": [lab.to_dict() for lab in self.labels],
            "rooms": [room.to_dict() for room in self.rooms],
            "wall_patches": [list(p) for p in self.wall_patches],
            "render_defaults": self.render_defaults.to_dict(),
        }

    @classmethod
    def from_dict(cls, d: dict[str, Any]) -> "Calibration":
        try:
            return cls(
                map_image=str(d["map_image"]),
                map_hash=str(d["map_hash"]),
                labels=[Label.from_dict(x) for x in d.get("labels", [])],
                rooms=[Room.from_dict(x) for x in d.get("rooms", [])],
                wall_patches=[
                    tuple(int(v) for v in patch)  # type: ignore[misc]
                    for patch in d.get("wall_patches", [])
                ],
                render_defaults=RenderDefaults.from_dict(d.get("render_defaults", {})),
            )
        except KeyError as e:
            raise CalibrationFormatError(
                f"calibration is missing required top-level key: {e}"
            ) from e


class CalibrationFormatError(Exception):
    """Raised when ``calibration.json`` can't be parsed into a Calibration."""


# ---------------------------------------------------------------------------
# Disk I/O
# ---------------------------------------------------------------------------


def load_calibration(path: Path | str) -> Calibration:
    """Load and parse ``calibration.json`` from disk."""
    path = Path(path)
    if not path.exists():
        raise CalibrationFormatError(f"calibration not found: {path}")
    try:
        with path.open("r", encoding="utf-8") as f:
            raw = json.load(f)
    except json.JSONDecodeError as e:
        raise CalibrationFormatError(f"calibration is not valid JSON ({path}): {e}") from e
    if not isinstance(raw, dict):
        raise CalibrationFormatError(
            f"calibration root must be a JSON object, got {type(raw).__name__} ({path})"
        )
    return Calibration.from_dict(raw)


def save_calibration(calibration: Calibration, path: Path | str) -> None:
    """Write a calibration to disk as hand-editable JSON (2-space indented)."""
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    text = json.dumps(calibration.to_dict(), indent=2, sort_keys=False)
    path.write_text(text + "\n", encoding="utf-8")


def compute_map_hash(map_path: Path | str) -> str:
    """Return ``"sha256:<hex>"`` for the given map image file."""
    map_path = Path(map_path)
    h = hashlib.sha256()
    with map_path.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return f"sha256:{h.hexdigest()}"


__all__ = [
    "Calibration",
    "CalibrationFormatError",
    "Label",
    "RenderDefaults",
    "Room",
    "compute_map_hash",
    "load_calibration",
    "save_calibration",
]
