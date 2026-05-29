"""Pass 0 — produce ``calibration.json`` from a floor-plan image.

This module implements the calibration pipeline described in plan.md §8:

    1. Load + grayscale + adaptive-binarize the map.
    2. Run Tesseract OCR (psm=11, alphanumeric whitelist) to find labels.
    3. Compute connected components of the inverted binary — each enclosed
       interior region is a candidate room polygon.
    4. Associate each OCR label with the CC whose mask contains the label's
       bbox center.
    5. Auto-classify each labeled room as office / hallway / common by polygon
       area and aspect ratio.
    6. Default fill_seed = room centroid (more reliable than label center,
       which often sits in a corner).
    7. Run the auto-checks (orphans, duplicate IDs, ambiguous rooms).
    8. Return ``(Calibration, list[CalibrationIssue])``.

The function deliberately returns **both** the calibration AND the issue list
rather than raising — callers (the CLI in particular) need to write the
calibration to disk even when there are issues, so the user can review/edit
the JSON to resolve them.
"""

from __future__ import annotations

import os
import re
import shutil
from dataclasses import dataclass
from pathlib import Path
from typing import Optional

import numpy as np

from .calibration import (
    Calibration,
    Classification,
    Label,
    RenderDefaults,
    Room,
    compute_map_hash,
)
from .geometry import (
    ConnectedComponent,
    bbox_center,
    bbox_contains_point,
    find_connected_components,
    mask_centroid,
    mask_contains_point,
    mask_to_rle,
)


# ---------------------------------------------------------------------------
# Public types
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class CalibrationIssue:
    """One problem (or warning) discovered during calibration.

    Attributes:
        severity: ``"error"`` (blocks the next pass) or ``"warning"`` (worth
            looking at in the review PDF).
        code: A short machine-readable key, e.g. ``"orphan_label"``.
        message: Human-readable detail with offending IDs and coordinates.
    """

    severity: str
    code: str
    message: str

    def __str__(self) -> str:
        return f"[{self.severity}] {self.code}: {self.message}"


class TesseractNotFoundError(RuntimeError):
    """Raised when we can't locate a tesseract executable on this machine."""


# ---------------------------------------------------------------------------
# Tunables (all tweakable per-call via parameters, defaults here)
# ---------------------------------------------------------------------------


# Minimum room polygon area (in pixels) to keep — anything smaller is noise.
DEFAULT_MIN_ROOM_AREA = 500

# Tesseract's per-word confidence is reported 0-100. Anything below this is
# discarded as likely noise.
DEFAULT_MIN_OCR_CONFIDENCE = 30

# Aspect-ratio thresholds for hallway classification (max side / min side).
HALLWAY_ASPECT_RATIO = 4.0

# A room whose polygon area is more than this multiple of the median is
# considered "very large" -> common-area classification.
COMMON_AREA_MULTIPLIER = 4.0

# Tesseract recognises labels matching this regex (after applying its
# alphanumeric whitelist). Pure-digit, optional trailing letter, or
# letter-prefix patterns are accepted; anything else is filtered out.
_LABEL_PATTERN = re.compile(r"^[A-Z]{0,4}[0-9]{3,5}[A-Z]?$")


# ---------------------------------------------------------------------------
# Tesseract location
# ---------------------------------------------------------------------------


def find_tesseract() -> Optional[str]:
    """Locate ``tesseract.exe`` on this machine.

    Resolution order:
        1. ``TESSERACT_PATH`` environment variable.
        2. The Windows UB-Mannheim default install path.
        3. Whatever ``shutil.which`` finds on ``PATH``.

    Returns:
        Absolute path to the executable, or ``None`` if not found.
    """
    env = os.environ.get("TESSERACT_PATH")
    if env and Path(env).is_file():
        return env

    win_default = Path(r"C:\Program Files\Tesseract-OCR\tesseract.exe")
    if win_default.is_file():
        return str(win_default)

    found = shutil.which("tesseract")
    return found


def _configure_pytesseract() -> None:
    """Point pytesseract at our resolved executable; raise if missing."""
    import pytesseract

    cmd = find_tesseract()
    if cmd is None:
        raise TesseractNotFoundError(
            "tesseract executable not found. Install the UB-Mannheim build "
            "(https://github.com/UB-Mannheim/tesseract/wiki) and either add "
            "its install directory to PATH or set the TESSERACT_PATH "
            "environment variable to point at tesseract.exe."
        )
    pytesseract.pytesseract.tesseract_cmd = cmd


# ---------------------------------------------------------------------------
# OCR
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class _OCRLabel:
    text: str
    bbox: tuple[int, int, int, int]
    confidence: float   # 0.0 - 1.0


def _run_ocr(image: np.ndarray, *, min_confidence: int) -> list[_OCRLabel]:
    """Run Tesseract in sparse-text mode and return clean label candidates."""
    import pytesseract

    _configure_pytesseract()

    config = (
        "--psm 11 "  # Sparse text — find as much as possible, no assumed layout.
        "-c tessedit_char_whitelist=0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-"
    )
    data = pytesseract.image_to_data(
        image, config=config, output_type=pytesseract.Output.DICT
    )

    labels: list[_OCRLabel] = []
    n = len(data["text"])
    for i in range(n):
        raw_text = data["text"][i] or ""
        text = raw_text.strip().upper()
        if not text:
            continue
        try:
            conf = float(data["conf"][i])
        except (TypeError, ValueError):
            continue
        if conf < min_confidence:
            continue
        if not _LABEL_PATTERN.match(text):
            continue
        bbox = (
            int(data["left"][i]),
            int(data["top"][i]),
            int(data["width"][i]),
            int(data["height"][i]),
        )
        labels.append(_OCRLabel(text=text, bbox=bbox, confidence=conf / 100.0))

    return labels


# ---------------------------------------------------------------------------
# Image preprocessing
# ---------------------------------------------------------------------------


def _binarize(image_gray: np.ndarray) -> np.ndarray:
    """Adaptive-threshold a grayscale image into a wall mask (walls=255)."""
    import cv2

    # Adaptive threshold handles scanned maps with uneven brightness better
    # than a global Otsu threshold.
    return cv2.adaptiveThreshold(
        image_gray,
        maxValue=255,
        adaptiveMethod=cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
        thresholdType=cv2.THRESH_BINARY_INV,
        blockSize=15,
        C=10,
    )


def _interior_mask(wall_mask: np.ndarray) -> np.ndarray:
    """Invert a wall mask so room interiors become foreground for CC labeling."""
    return (255 - wall_mask).astype(np.uint8)


# ---------------------------------------------------------------------------
# Classification
# ---------------------------------------------------------------------------


def _classify(
    room_bbox: tuple[int, int, int, int],
    room_area: int,
    median_area: float,
) -> Classification:
    """Classify a room by polygon area + aspect ratio."""
    _, _, w, h = room_bbox
    if w == 0 or h == 0:
        return Classification.SKIP
    long_side, short_side = max(w, h), min(w, h)

    if long_side / short_side >= HALLWAY_ASPECT_RATIO:
        return Classification.HALLWAY
    if room_area >= median_area * COMMON_AREA_MULTIPLIER:
        return Classification.COMMON
    return Classification.OFFICE


# ---------------------------------------------------------------------------
# Main pipeline
# ---------------------------------------------------------------------------


def calibrate_map(
    map_path: Path | str,
    *,
    min_room_area: int = DEFAULT_MIN_ROOM_AREA,
    min_ocr_confidence: int = DEFAULT_MIN_OCR_CONFIDENCE,
) -> tuple[Calibration, list[CalibrationIssue]]:
    """Run the calibration pipeline against a map image.

    Args:
        map_path: Path to the map image (PNG / JPEG — anything OpenCV reads).
        min_room_area: Discard CC polygons smaller than this many pixels.
        min_ocr_confidence: Discard OCR detections below this confidence
            (Tesseract scale, 0-100).

    Returns:
        Tuple of ``(Calibration, issues)``. Issues may be errors or warnings;
        the calibration is always populated (the user resolves issues by
        hand-editing the JSON).

    Raises:
        FileNotFoundError: if ``map_path`` doesn't exist.
        TesseractNotFoundError: if no Tesseract executable can be located.
    """
    import cv2

    map_path = Path(map_path)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    image = cv2.imread(str(map_path), cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"could not decode image: {map_path}")

    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    wall_mask = _binarize(gray)
    interior = _interior_mask(wall_mask)

    components = find_connected_components(
        interior, min_area=min_room_area, discard_largest=True
    )
    ocr_labels = _run_ocr(gray, min_confidence=min_ocr_confidence)

    cal, issues = _build_calibration(
        map_path=map_path,
        components=components,
        ocr_labels=ocr_labels,
    )
    return cal, issues


def _build_calibration(
    *,
    map_path: Path,
    components: list[ConnectedComponent],
    ocr_labels: list[_OCRLabel],
) -> tuple[Calibration, list[CalibrationIssue]]:
    """Combine raw CC + OCR output into a typed Calibration plus issue list."""
    issues: list[CalibrationIssue] = []

    if not components:
        issues.append(
            CalibrationIssue(
                severity="error",
                code="no_rooms_detected",
                message=(
                    f"connected-components found no rooms in {map_path}; "
                    "the binarization may need tuning"
                ),
            )
        )
    if not ocr_labels:
        issues.append(
            CalibrationIssue(
                severity="error",
                code="no_labels_detected",
                message=(
                    f"OCR found no labels matching the room-ID pattern in {map_path}; "
                    "try increasing the image resolution or lowering --min-ocr-confidence"
                ),
            )
        )

    # Assign a stable id to each kept CC.
    rooms: dict[int, Room] = {}
    cc_by_id: dict[int, ConnectedComponent] = {}
    for new_id, cc in enumerate(components, start=1):
        rooms[new_id] = Room(
            id=new_id,
            polygon_rle=mask_to_rle(cc.mask),
            area_px=cc.area_px,
            bbox=cc.bbox,
        )
        cc_by_id[new_id] = cc

    # Classification depends on knowing the median room area.
    if rooms:
        median_area = float(np.median([r.area_px for r in rooms.values()]))
    else:
        median_area = 0.0
    classifications: dict[int, Classification] = {
        rid: _classify(r.bbox, r.area_px, median_area) for rid, r in rooms.items()
    }

    # Associate each OCR label with the CC whose mask contains the label center.
    labels: list[Label] = []
    room_label_count: dict[int, list[str]] = {rid: [] for rid in rooms}
    id_to_label_indices: dict[str, list[int]] = {}

    for ocr in ocr_labels:
        center = bbox_center(ocr.bbox)
        room_id = _find_containing_room(center, cc_by_id)
        if room_id is None:
            issues.append(
                CalibrationIssue(
                    severity="warning",
                    code="orphan_label",
                    message=(
                        f"label {ocr.text!r} at bbox {ocr.bbox} is not inside "
                        "any detected room; it will be ignored unless you "
                        "edit calibration.json to assign a room_id"
                    ),
                )
            )
            classification = Classification.SKIP
            fill_seed = center
        else:
            classification = classifications[room_id]
            room_centroid = mask_centroid(cc_by_id[room_id].mask) or center
            fill_seed = room_centroid
            room_label_count[room_id].append(ocr.text)

        label = Label(
            id=ocr.text,
            bbox=ocr.bbox,
            room_id=room_id,
            classification=classification,
            fill_seed=fill_seed,
            ocr_confidence=ocr.confidence,
        )
        labels.append(label)
        id_to_label_indices.setdefault(ocr.text, []).append(len(labels) - 1)

    # Auto-checks ----------------------------------------------------------

    # No CC polygon should contain more than one OFFICE label (would mean two
    # rooms merged into one CC, almost always via a flood-fill leak).
    for room_id, ids in room_label_count.items():
        office_ids = [
            i for i in ids if classifications[room_id] == Classification.OFFICE
        ]
        if len(office_ids) > 1:
            issues.append(
                CalibrationIssue(
                    severity="error",
                    code="multiple_office_labels_in_room",
                    message=(
                        f"room {room_id} contains {len(office_ids)} office "
                        f"labels {office_ids!r}; usually this means two rooms "
                        "merged via an open doorway — add a wall_patches entry "
                        "and re-calibrate"
                    ),
                )
            )

    # No two OFFICE labels should share the same ID across the whole floor.
    for label_id, indices in id_to_label_indices.items():
        office_indices = [
            i for i in indices if labels[i].classification == Classification.OFFICE
        ]
        if len(office_indices) > 1:
            offending_rooms = sorted(
                {labels[i].room_id for i in office_indices if labels[i].room_id is not None}
            )
            issues.append(
                CalibrationIssue(
                    severity="error",
                    code="duplicate_office_id",
                    message=(
                        f"office id {label_id!r} appears in rooms "
                        f"{offending_rooms!r}; disambiguate by editing the labels "
                        f"in calibration.json (e.g. {label_id!r}-N / {label_id!r}-S)"
                    ),
                )
            )

    # Every label bbox should be fully inside its assigned polygon. Skip orphans.
    for label in labels:
        if label.room_id is None:
            continue
        if not mask_contains_point(cc_by_id[label.room_id].mask, bbox_center(label.bbox)):
            # This shouldn't happen given how we associated, but guard anyway.
            issues.append(
                CalibrationIssue(
                    severity="error",
                    code="label_outside_assigned_room",
                    message=(
                        f"label {label.id!r} is not inside its assigned room "
                        f"{label.room_id} — internal bug, please file a report"
                    ),
                )
            )

    # Only retain rooms that have at least one label — rooms with no label are
    # captured in a separate "orphan rooms" warning so the user can decide.
    referenced_room_ids = {lab.room_id for lab in labels if lab.room_id is not None}
    orphan_rooms = sorted(set(rooms) - referenced_room_ids)
    for rid in orphan_rooms:
        issues.append(
            CalibrationIssue(
                severity="warning",
                code="orphan_room",
                message=(
                    f"room {rid} (area {rooms[rid].area_px}px, bbox {rooms[rid].bbox}) "
                    "has no OCR label; it will be ignored unless you add one in "
                    "calibration.json"
                ),
            )
        )

    cal = Calibration(
        map_image=map_path.name,
        map_hash=compute_map_hash(map_path),
        labels=labels,
        rooms=list(rooms.values()),
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )
    return cal, issues


def _find_containing_room(
    point: tuple[int, int],
    cc_by_id: dict[int, ConnectedComponent],
) -> Optional[int]:
    """Return the room id whose CC mask contains ``point``, or None.

    The cheap test (bbox-contains) is done first to avoid the expensive
    per-mask lookup when the point is obviously outside.
    """
    for room_id, cc in cc_by_id.items():
        if not bbox_contains_point(cc.bbox, point):
            continue
        if mask_contains_point(cc.mask, point):
            return room_id
    return None


__all__ = [
    "CalibrationIssue",
    "TesseractNotFoundError",
    "calibrate_map",
    "find_tesseract",
]
