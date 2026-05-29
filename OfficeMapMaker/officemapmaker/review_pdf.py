"""Pass 0 — render ``calibration_review.pdf`` for human review.

This is the artifact a person eyeballs before confirming a calibration.
Per plan.md §8 Pass 0, the PDF has four pages:

    1. The full map with each detected label boxed in green, the OCR-read
       text overlaid, and a translucent fill over each assigned room polygon.
    2. Every connected-component polygon outlined in a distinct color, with
       its computed area + classification annotated.
    3. A grid of label thumbnails sorted by ascending OCR confidence — the
       fastest way to spot misreads.
    4. Orphans page: detected rooms with no label and labels outside all
       rooms (the two failure modes that need human intervention).

All four pages embed the map as a single inline image, then overlay
reportlab primitives on top — this keeps the PDF size sane and the
overlays crisp regardless of viewer zoom level.
"""

from __future__ import annotations

import colorsys
import io
from dataclasses import dataclass
from pathlib import Path
from typing import Callable, Iterable, Optional

import numpy as np

from .calibration import Calibration, Classification, Label, Room
from .geometry import rle_to_mask


# ---------------------------------------------------------------------------
# Layout constants (PDF point space, 72 pt = 1 inch)
# ---------------------------------------------------------------------------

_MARGIN_PT = 36                # 0.5"
_TITLE_HEIGHT_PT = 36          # space reserved at the top for page title + caption
_THUMB_PADDING_PT = 6
_THUMB_COLS = 6                # default grid width on pages 3 and 4
_MAX_THUMBNAILS_PER_PAGE = 60  # cap so the PDF stays usable


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


def build_calibration_review_pdf(
    map_path: Path | str,
    calibration: Calibration,
    output_pdf: Path | str,
) -> None:
    """Render the four-page calibration_review.pdf next to the calibration.

    Args:
        map_path: Path to the raster map image used to build the calibration.
        calibration: The calibration to visualize.
        output_pdf: Where to write the PDF. Parent directory must exist.

    Raises:
        FileNotFoundError: if ``map_path`` doesn't exist.
    """
    from PIL import Image
    from reportlab.lib.pagesizes import letter
    from reportlab.pdfgen import canvas as rl_canvas

    map_path = Path(map_path)
    output_pdf = Path(output_pdf)
    if not map_path.exists():
        raise FileNotFoundError(map_path)

    with Image.open(map_path) as im:
        map_image = im.convert("RGB").copy()

    c = rl_canvas.Canvas(str(output_pdf), pagesize=letter)
    page_w, page_h = letter

    _render_page1_labels(c, page_w, page_h, map_image, calibration)
    c.showPage()
    _render_page2_polygons(c, page_w, page_h, map_image, calibration)
    c.showPage()
    _render_page3_confidence(c, page_w, page_h, map_image, calibration)
    c.showPage()
    _render_page4_orphans(c, page_w, page_h, map_image, calibration)
    c.showPage()

    c.save()


# ---------------------------------------------------------------------------
# Page renderers
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class _MapFit:
    """How an embedded map image is positioned on a PDF page."""

    origin_x: float    # left edge of the image, in points
    origin_y: float    # bottom edge of the image, in points
    width: float       # rendered width, in points
    height: float      # rendered height, in points
    scale: float       # points per source pixel
    src_height: int    # source image height (for y-flip)

    def to_pdf(self, x: int, y: int) -> tuple[float, float]:
        """Convert PIL-space (x, y) into PDF-space (x, y).

        PIL has y=0 at the top of the image; PDF has y=0 at the bottom of
        the page. This helper does the flip plus the scale + offset.
        """
        return (
            self.origin_x + x * self.scale,
            self.origin_y + (self.src_height - y) * self.scale,
        )


def _embed_map(
    c,
    page_w: float,
    page_h: float,
    map_image,
    title: str,
    caption: str,
) -> _MapFit:
    """Draw a page title + the map as a fitted inline image. Return MapFit."""
    from reportlab.lib.utils import ImageReader

    c.setFillGray(0)
    c.setFont("Helvetica-Bold", 14)
    c.drawString(_MARGIN_PT, page_h - _MARGIN_PT - 14, title)
    c.setFont("Helvetica", 9)
    c.drawString(_MARGIN_PT, page_h - _MARGIN_PT - 30, caption)

    avail_w = page_w - 2 * _MARGIN_PT
    avail_h = page_h - 2 * _MARGIN_PT - _TITLE_HEIGHT_PT
    src_w, src_h = map_image.size
    scale = min(avail_w / src_w, avail_h / src_h)
    draw_w = src_w * scale
    draw_h = src_h * scale
    origin_x = _MARGIN_PT + (avail_w - draw_w) / 2
    origin_y = _MARGIN_PT + (avail_h - draw_h) / 2

    c.drawImage(
        ImageReader(map_image),
        origin_x,
        origin_y,
        width=draw_w,
        height=draw_h,
    )
    return _MapFit(
        origin_x=origin_x,
        origin_y=origin_y,
        width=draw_w,
        height=draw_h,
        scale=scale,
        src_height=src_h,
    )


def _render_page1_labels(c, page_w, page_h, map_image, cal: Calibration) -> None:
    """Page 1: every OCR label boxed in green + assigned room translucent fill."""
    fit = _embed_map(
        c, page_w, page_h, map_image,
        title="Page 1 — Detected labels on the map",
        caption=(
            f"{len(cal.labels)} labels detected. "
            "Green box = OCR bbox, text = OCR-read ID. Translucent fill = assigned room polygon. "
            "Look for boxes around things that aren't room numbers."
        ),
    )

    # 1) Translucent fills for each assigned room polygon.
    drawn_rooms: set[int] = set()
    for label in cal.labels:
        if label.room_id is None or label.room_id in drawn_rooms:
            continue
        drawn_rooms.add(label.room_id)
        room = cal.room_by_id(label.room_id)
        if room is None:
            continue
        color = _color_for_classification(label.classification)
        _draw_room_fill(c, fit, room, color, alpha=0.25)

    # 2) Green bboxes + OCR text.
    c.setStrokeColorRGB(0.0, 0.6, 0.0)
    c.setLineWidth(0.6)
    c.setFont("Helvetica", 6)
    c.setFillColorRGB(0.0, 0.4, 0.0)
    for label in cal.labels:
        _draw_label_box(c, fit, label, text=label.id)


def _render_page2_polygons(c, page_w, page_h, map_image, cal: Calibration) -> None:
    """Page 2: every CC polygon outlined in a distinct color + classification labels."""
    fit = _embed_map(
        c, page_w, page_h, map_image,
        title="Page 2 — Connected-component room polygons",
        caption=(
            f"{len(cal.rooms)} rooms detected. Each polygon outlined in a distinct color. "
            "Look for two rooms merged into one polygon (open doorways) or a room "
            "split into two by a stray dark pixel."
        ),
    )

    classifications = {lab.room_id: lab.classification for lab in cal.labels if lab.room_id is not None}

    c.setLineWidth(0.8)
    c.setFont("Helvetica", 5)
    for idx, room in enumerate(cal.rooms):
        rgb = _distinct_color(idx, len(cal.rooms))
        c.setStrokeColorRGB(*rgb)
        c.setFillColorRGB(*rgb)
        _draw_room_outline(c, fit, room)

        # Annotate with id + area + classification at the room's bbox top-left.
        x_pdf, y_pdf = fit.to_pdf(room.bbox[0], room.bbox[1])
        classification = classifications.get(room.id, Classification.SKIP)
        annotation = f"#{room.id} {room.area_px}px {classification.value}"
        c.drawString(x_pdf, y_pdf - 6, annotation)


def _render_page3_confidence(c, page_w, page_h, map_image, cal: Calibration) -> None:
    """Page 3: thumbnail grid of labels sorted by ascending OCR confidence."""
    sorted_labels = sorted(cal.labels, key=lambda lab: lab.ocr_confidence)
    displayed = sorted_labels[:_MAX_THUMBNAILS_PER_PAGE]
    omitted = len(sorted_labels) - len(displayed)

    suffix = f" ({omitted} more not shown)" if omitted else ""
    _render_label_thumbnail_grid(
        c, page_w, page_h, map_image,
        labels=displayed,
        title="Page 3 — Labels by ascending OCR confidence",
        caption=(
            f"{len(displayed)} lowest-confidence labels shown{suffix}. "
            "Misreads almost always live here — compare the green text "
            "(OCR result) to the original digits in each crop."
        ),
    )


def _render_page4_orphans(c, page_w, page_h, map_image, cal: Calibration) -> None:
    """Page 4: orphan labels (no room) + orphan rooms (no label)."""
    orphan_labels = [lab for lab in cal.labels if lab.room_id is None]
    referenced_rooms = {lab.room_id for lab in cal.labels if lab.room_id is not None}
    orphan_rooms = [r for r in cal.rooms if r.id not in referenced_rooms]

    # Build pseudo-labels for orphan rooms so we can reuse the thumbnail grid.
    pseudo: list[Label] = []
    for room in orphan_rooms:
        pseudo.append(
            Label(
                id=f"room#{room.id}",
                bbox=room.bbox,
                room_id=room.id,
                classification=Classification.SKIP,
                fill_seed=(room.bbox[0] + room.bbox[2] // 2, room.bbox[1] + room.bbox[3] // 2),
                ocr_confidence=0.0,
            )
        )

    combined = orphan_labels + pseudo
    displayed = combined[:_MAX_THUMBNAILS_PER_PAGE]
    omitted = len(combined) - len(displayed)
    suffix = f" ({omitted} more not shown)" if omitted else ""

    _render_label_thumbnail_grid(
        c, page_w, page_h, map_image,
        labels=displayed,
        title="Page 4 — Orphans",
        caption=(
            f"{len(orphan_labels)} labels with no room + {len(orphan_rooms)} rooms with no label"
            f"{suffix}. Each tile is the relevant bbox cropped from the map. "
            "Resolve by editing calibration.json (assign room_id, add a label, "
            "or change classification to 'skip')."
        ),
    )


# ---------------------------------------------------------------------------
# Drawing helpers
# ---------------------------------------------------------------------------


def _draw_label_box(c, fit: _MapFit, label: Label, *, text: str) -> None:
    x, y, w, h = label.bbox
    x0, y0 = fit.to_pdf(x, y + h)            # lower-left in PDF space
    width_pt = w * fit.scale
    height_pt = h * fit.scale
    c.rect(x0, y0, width_pt, height_pt, stroke=1, fill=0)
    c.drawString(x0, y0 + height_pt + 1, text)


def _draw_room_outline(c, fit: _MapFit, room: Room) -> None:
    """Outline the room's polygon using contours of the RLE mask."""
    import cv2

    mask = rle_to_mask(room.polygon_rle).astype(np.uint8)
    if not mask.any():
        return
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    for contour in contours:
        if len(contour) < 2:
            continue
        path = c.beginPath()
        first = True
        for (px, py) in contour.reshape(-1, 2):
            x_pdf, y_pdf = fit.to_pdf(int(px), int(py))
            if first:
                path.moveTo(x_pdf, y_pdf)
                first = False
            else:
                path.lineTo(x_pdf, y_pdf)
        path.close()
        c.drawPath(path, stroke=1, fill=0)


def _draw_room_fill(c, fit: _MapFit, room: Room, rgb: tuple[float, float, float], *, alpha: float) -> None:
    """Translucent fill of the room polygon."""
    import cv2

    mask = rle_to_mask(room.polygon_rle).astype(np.uint8)
    if not mask.any():
        return
    contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    c.saveState()
    c.setFillColorRGB(*rgb, alpha=alpha)
    for contour in contours:
        if len(contour) < 3:
            continue
        path = c.beginPath()
        first = True
        for (px, py) in contour.reshape(-1, 2):
            x_pdf, y_pdf = fit.to_pdf(int(px), int(py))
            if first:
                path.moveTo(x_pdf, y_pdf)
                first = False
            else:
                path.lineTo(x_pdf, y_pdf)
        path.close()
        c.drawPath(path, stroke=0, fill=1)
    c.restoreState()


def _render_label_thumbnail_grid(
    c,
    page_w: float,
    page_h: float,
    map_image,
    *,
    labels: list[Label],
    title: str,
    caption: str,
) -> None:
    """Render up to _MAX_THUMBNAILS_PER_PAGE label crops in a uniform grid."""
    from reportlab.lib.utils import ImageReader

    c.setFillGray(0)
    c.setFont("Helvetica-Bold", 14)
    c.drawString(_MARGIN_PT, page_h - _MARGIN_PT - 14, title)
    c.setFont("Helvetica", 9)
    c.drawString(_MARGIN_PT, page_h - _MARGIN_PT - 30, caption)

    if not labels:
        c.setFont("Helvetica-Oblique", 12)
        c.drawString(_MARGIN_PT, page_h / 2, "(none)")
        return

    grid_top = page_h - _MARGIN_PT - _TITLE_HEIGHT_PT
    grid_bottom = _MARGIN_PT
    avail_w = page_w - 2 * _MARGIN_PT
    cell_w = avail_w / _THUMB_COLS
    thumb_w = cell_w - _THUMB_PADDING_PT
    thumb_h = thumb_w * 0.70  # 5:7 aspect leaves room for a caption line
    cell_h = thumb_h + 22
    rows_per_page = max(1, int((grid_top - grid_bottom) // cell_h))
    capacity = rows_per_page * _THUMB_COLS

    for i, label in enumerate(labels[:capacity]):
        col = i % _THUMB_COLS
        row = i // _THUMB_COLS
        x = _MARGIN_PT + col * cell_w
        y = grid_top - (row + 1) * cell_h
        crop = _crop_label_region(map_image, label.bbox, expand=4.0)
        if crop is not None:
            c.drawImage(
                ImageReader(crop),
                x, y + 18, width=thumb_w, height=thumb_h, preserveAspectRatio=True, anchor="c",
            )
        c.setFont("Helvetica", 7)
        c.setFillGray(0)
        c.drawString(x, y + 9, f"{label.id}  conf={label.ocr_confidence:.2f}")
        c.setFillGray(0.4)
        room_str = "(orphan)" if label.room_id is None else f"room {label.room_id}"
        c.drawString(x, y + 1, room_str)


def _crop_label_region(map_image, bbox: tuple[int, int, int, int], *, expand: float):
    """Return a PIL crop around ``bbox`` with ``expand``× margin on each side."""
    x, y, w, h = bbox
    cx, cy = x + w / 2, y + h / 2
    half_w = max(20, int(w * expand / 2))
    half_h = max(20, int(h * expand / 2))
    src_w, src_h = map_image.size
    x0 = max(0, int(cx - half_w))
    y0 = max(0, int(cy - half_h))
    x1 = min(src_w, int(cx + half_w))
    y1 = min(src_h, int(cy + half_h))
    if x1 <= x0 or y1 <= y0:
        return None
    return map_image.crop((x0, y0, x1, y1))


# ---------------------------------------------------------------------------
# Palette helpers
# ---------------------------------------------------------------------------


def _distinct_color(index: int, total: int) -> tuple[float, float, float]:
    """Generate a distinct RGB color for an ordinal index in [0, total)."""
    if total <= 0:
        return (0.5, 0.5, 0.5)
    hue = (index / total) % 1.0
    return colorsys.hls_to_rgb(hue, 0.50, 0.65)


_CLASSIFICATION_RGB: dict[Classification, tuple[float, float, float]] = {
    Classification.OFFICE: (0.40, 0.70, 1.00),
    Classification.HALLWAY: (1.00, 0.85, 0.40),
    Classification.COMMON: (0.55, 0.85, 0.55),
    Classification.SKIP: (0.80, 0.80, 0.80),
}


def _color_for_classification(c: Classification) -> tuple[float, float, float]:
    return _CLASSIFICATION_RGB.get(c, (0.7, 0.7, 0.7))


__all__ = ["build_calibration_review_pdf"]
