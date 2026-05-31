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
    6. Default fill_seed = pole of inaccessibility of the room's interior
       mask (the pixel farthest from any boundary). Geometric centroid is a
       bad choice here because it frequently lands on the room's OCR'd label
       text — i.e. on a wall pixel — making the seed useless for flood-fill.
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
from typing import Any, Callable, Optional

import numpy as np

from .calibration import (
    Calibration,
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
    mask_contains_point,
    mask_to_rle,
    pole_of_inaccessibility,
    rle_to_mask,
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
# Main pipeline
# ---------------------------------------------------------------------------


def calibrate_map(
    map_path: Path | str,
    *,
    min_room_area: int = DEFAULT_MIN_ROOM_AREA,
    min_ocr_confidence: int = DEFAULT_MIN_OCR_CONFIDENCE,
    progress_cb: Optional[Callable[[float, str], None]] = None,
    cancel_cb: Optional[Callable[[], bool]] = None,
) -> tuple[Calibration, list[CalibrationIssue]]:
    """Run the calibration pipeline against a map image.

    Args:
        map_path: Path to the map image (PNG / JPEG -- anything OpenCV reads).
        min_room_area: Discard CC polygons smaller than this many pixels.
        min_ocr_confidence: Discard OCR detections below this confidence
            (Tesseract scale, 0-100).
        progress_cb: Optional callback ``(fraction, message)`` invoked
            between phases. The wizard's PipelineRunner injects this so
            the UI can show a progress bar / status string. Pipeline
            unit tests typically pass ``None``.
        cancel_cb: Optional callback returning ``True`` once the user
            has asked to cancel. Checked between phases. When it
            returns True we raise :class:`PipelineCanceled` instead of
            finishing the run. Same source as ``progress_cb``.

    Returns:
        Tuple of ``(Calibration, issues)``. Issues may be errors or warnings;
        the calibration is always populated (the user resolves issues by
        hand-editing the JSON).

    Raises:
        FileNotFoundError: if ``map_path`` doesn't exist.
        TesseractNotFoundError: if no Tesseract executable can be located.
        PipelineCanceled: if ``cancel_cb()`` returns True between phases.
    """
    import cv2

    from .pipeline import PipelineCanceled

    def _report(fraction: float, message: str) -> None:
        if progress_cb is not None:
            progress_cb(fraction, message)

    def _check_cancel() -> None:
        if cancel_cb is not None and cancel_cb():
            raise PipelineCanceled()

    map_path = Path(map_path)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    _report(0.0, f"Loading {map_path.name}...")
    _check_cancel()

    image = cv2.imread(str(map_path), cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"could not decode image: {map_path}")

    _report(0.10, "Preparing image...")
    _check_cancel()
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    wall_mask = _binarize(gray)
    interior = _interior_mask(wall_mask)

    _report(0.25, "Finding rooms...")
    _check_cancel()
    components = find_connected_components(
        interior, min_area=min_room_area, discard_largest=True
    )

    _report(0.45, "Running OCR (this is the slow part)...")
    _check_cancel()
    ocr_labels = _run_ocr(gray, min_confidence=min_ocr_confidence)

    _report(0.90, "Matching labels to rooms...")
    _check_cancel()
    cal, issues = _build_calibration(
        map_path=map_path,
        components=components,
        ocr_labels=ocr_labels,
    )
    _report(1.0, "Done.")
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

    # Associate each OCR result with a room (or mark as orphan). The
    # ``room_label_count`` map is kept solely so the post-loop loop can
    # tell which rooms have at least one label (it used to drive the
    # classification heuristic; that heuristic has been removed).
    ocr_assignments: list[tuple[Any, Optional[int], tuple[int, int]]] = []
    room_label_count: dict[int, list[str]] = {rid: [] for rid in rooms}

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
            ocr_assignments.append((ocr, None, center))
        else:
            # pole_of_inaccessibility = the pixel deepest inside the CC,
            # which avoids landing on a glyph (the geometric centroid often
            # falls on the OCR'd label text itself for typical office rooms).
            room_seed = pole_of_inaccessibility(cc_by_id[room_id].mask) or center
            room_label_count[room_id].append(ocr.text)
            ocr_assignments.append((ocr, room_id, room_seed))

    # Build Label objects. Office-ness is no longer baked into the label —
    # it's derived from the assignments spreadsheet at validate/render time.
    labels: list[Label] = []
    id_to_label_indices: dict[str, list[int]] = {}

    for ocr, room_id, fill_seed in ocr_assignments:
        label = Label(
            id=ocr.text,
            bbox=ocr.bbox,
            room_id=room_id,
            fill_seed=fill_seed,
            ocr_confidence=ocr.confidence,
        )
        labels.append(label)
        id_to_label_indices.setdefault(ocr.text, []).append(len(labels) - 1)

    # Auto-checks ----------------------------------------------------------
    #
    # The earlier "duplicate OFFICE id" and "multiple OFFICE labels in one
    # room" checks have been removed: without a Classification enum and
    # without the assignments spreadsheet, calibrate cannot tell whether a
    # repeated id is a real conflict (two offices both labeled 1480) or
    # something benign (a hallway sign appearing on multiple doors). Those
    # checks are now done in ``validate`` against the spreadsheet.

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


def revalidate_calibration(cal: Calibration) -> list[CalibrationIssue]:
    """Recompute the calibration issue list from a Calibration object alone.

    Unlike :func:`calibrate_map`, this does **not** re-run OCR or
    connected-components analysis -- it just re-evaluates the issue
    checks that depend only on the current state of the Calibration
    (label / room assignments, polygons, bboxes). Cheap enough to call
    after every wizard edit so the issue count + step badge update
    live as the user fixes problems.

    Issue codes produced are a strict subset of :func:`calibrate_map`'s:
    ``no_rooms_detected``, ``no_labels_detected``, ``orphan_label``,
    ``label_outside_assigned_room``, ``orphan_room``. The check
    "label_outside_assigned_room" can additionally fire when a label
    references a ``room_id`` that no longer exists (e.g. the user
    deleted the room without reassigning).

    Memory: room masks are materialized one room at a time and
    discarded between rooms (each mask is H*W bytes, so on a
    4000x5000 map ~20 MB; holding 200 of them at once would be
    multi-GB). Worst-case wall time on a ~200-room map is well
    under a second.
    """
    issues: list[CalibrationIssue] = []

    if not cal.rooms:
        issues.append(
            CalibrationIssue(
                severity="error",
                code="no_rooms_detected",
                message=(
                    "the calibration has no rooms; add at least one room "
                    "polygon before continuing"
                ),
            )
        )
    if not cal.labels:
        issues.append(
            CalibrationIssue(
                severity="error",
                code="no_labels_detected",
                message=(
                    "the calibration has no labels; add at least one label "
                    "before continuing"
                ),
            )
        )

    rooms_by_id: dict[int, Any] = {r.id: r for r in cal.rooms}

    # Group labels by their assigned room so we materialize each room
    # mask at most once and discard it before moving on.
    labels_by_room: dict[int, list[Any]] = {}
    orphan_labels: list[Any] = []
    for label in cal.labels:
        if label.room_id is None:
            orphan_labels.append(label)
        else:
            labels_by_room.setdefault(label.room_id, []).append(label)

    for label in orphan_labels:
        issues.append(
            CalibrationIssue(
                severity="warning",
                code="orphan_label",
                message=(
                    f"label {label.id!r} at bbox {label.bbox} is not "
                    "assigned to any room; it will be ignored unless you "
                    "assign a room"
                ),
            )
        )

    # Per-room pass: check each label's bbox center is inside its
    # room's polygon. Done one room at a time so only one (potentially
    # large) mask is in memory at any moment.
    for room_id in sorted(labels_by_room):
        room = rooms_by_id.get(room_id)
        if room is None:
            for label in labels_by_room[room_id]:
                issues.append(
                    CalibrationIssue(
                        severity="error",
                        code="label_outside_assigned_room",
                        message=(
                            f"label {label.id!r} is assigned to room "
                            f"{room_id}, which no longer exists; reassign "
                            "or delete this label"
                        ),
                    )
                )
            continue

        mask = rle_to_mask(room.polygon_rle)
        for label in labels_by_room[room_id]:
            if not mask_contains_point(mask, bbox_center(label.bbox)):
                issues.append(
                    CalibrationIssue(
                        severity="error",
                        code="label_outside_assigned_room",
                        message=(
                            f"label {label.id!r} (bbox {label.bbox}) is "
                            f"not inside its assigned room {room_id}"
                        ),
                    )
                )

    # Rooms with no labels are warnings.
    referenced_room_ids = set(labels_by_room)
    for room in cal.rooms:
        if room.id not in referenced_room_ids:
            issues.append(
                CalibrationIssue(
                    severity="warning",
                    code="orphan_room",
                    message=(
                        f"room {room.id} (area {room.area_px}px, bbox "
                        f"{room.bbox}) has no label; it will be ignored "
                        "unless you add one"
                    ),
                )
            )

    return issues


__all__ = [
    "CalibrationIssue",
    "TesseractNotFoundError",
    "calibrate_map",
    "find_tesseract",
    "revalidate_calibration",
]
