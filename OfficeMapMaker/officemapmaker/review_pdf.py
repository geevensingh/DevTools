"""Pass 0 — render ``calibration_review.pdf`` for human review.

This is the artifact a person eyeballs before confirming a calibration.
Per plan.md §8 Pass 0, the PDF has four pages:

    1. The full map with each detected label boxed in green, the OCR-read
       text overlaid, and a translucent fill over each assigned room polygon.
    2. Every connected-component polygon outlined in a distinct color, with
       its computed area annotated.
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

from .calibration import Calibration, Label, Room
from .geometry import rle_to_mask


# ---------------------------------------------------------------------------
# Layout constants (PDF point space, 72 pt = 1 inch)
# ---------------------------------------------------------------------------

_MARGIN_PT = 36                # 0.5"
_TITLE_FONT_SIZE_PT = 14
_CAPTION_FONT_SIZE_PT = 9
_CAPTION_LEADING_PT = 11       # vertical space between caption lines
_TITLE_GAP_PT = 8              # gap between title baseline and first caption line
_TITLE_BLOCK_GAP_BELOW_PT = 10 # gap between last caption line and page content
_THUMB_PADDING_PT = 6
_THUMB_COLS = 6                # default grid width on pages 3 and 4
# Conservative upper bound on the caption's line count. Used only to compute a
# stable per-page thumbnail capacity so the grid never overflows even if a
# pagination suffix bumps the caption onto an extra wrapped line.
_TITLE_BLOCK_MAX_CAPTION_LINES = 4
_BBOX_PAD_PT = 3.0             # padding around each OCR bbox on page 1 so the
                               # outline visibly encloses (rather than covers)
                               # the digits beneath it
_LABEL_GAP_PT = 7.0            # gap between a bbox and the OCR-read text below it


# ---------------------------------------------------------------------------
# Title-block helpers
# ---------------------------------------------------------------------------


def _wrap_text(text: str, max_width_pt: float, font_name: str, font_size: float) -> list[str]:
    """Greedy word-wrap to fit ``max_width_pt`` at the given font.

    Returns a list of lines (no trailing newlines). Long single words that
    exceed the max width are kept on their own line rather than split.
    """
    from reportlab.pdfbase.pdfmetrics import stringWidth

    words = text.split()
    if not words:
        return [""]
    lines: list[str] = []
    current = words[0]
    for word in words[1:]:
        candidate = current + " " + word
        if stringWidth(candidate, font_name, font_size) <= max_width_pt:
            current = candidate
        else:
            lines.append(current)
            current = word
    lines.append(current)
    return lines


def _draw_title_block(c, page_w: float, page_h: float, title: str, caption: str) -> float:
    """Draw the page title + a word-wrapped caption.

    Returns the *total* vertical space consumed below the top page margin
    (including the title, every caption line, and the gap before content),
    so callers can fit the rest of the page beneath it.
    """
    c.setFillGray(0)
    avail_w = page_w - 2 * _MARGIN_PT
    title_baseline = page_h - _MARGIN_PT - _TITLE_FONT_SIZE_PT
    c.setFont("Helvetica-Bold", _TITLE_FONT_SIZE_PT)
    c.drawString(_MARGIN_PT, title_baseline, title)

    c.setFont("Helvetica", _CAPTION_FONT_SIZE_PT)
    caption_lines = _wrap_text(caption, avail_w, "Helvetica", _CAPTION_FONT_SIZE_PT)
    line_y = title_baseline - _TITLE_GAP_PT - _CAPTION_FONT_SIZE_PT
    for line in caption_lines:
        c.drawString(_MARGIN_PT, line_y, line)
        line_y -= _CAPTION_LEADING_PT

    # Total height = top margin -> baseline of last caption line + a gap.
    consumed = (
        _TITLE_FONT_SIZE_PT
        + _TITLE_GAP_PT
        + _CAPTION_FONT_SIZE_PT
        + max(0, len(caption_lines) - 1) * _CAPTION_LEADING_PT
        + _TITLE_BLOCK_GAP_BELOW_PT
    )
    return consumed


def _max_title_block_height() -> float:
    """Conservative upper bound on title-block height across all pages.

    Used to compute a stable per-page thumbnail capacity that holds even when
    a paginated caption picks up an extra wrapped line. The chunk size never
    needs to change between pages, so labels paginate predictably.
    """
    return (
        _TITLE_FONT_SIZE_PT
        + _TITLE_GAP_PT
        + _CAPTION_FONT_SIZE_PT
        + max(0, _TITLE_BLOCK_MAX_CAPTION_LINES - 1) * _CAPTION_LEADING_PT
        + _TITLE_BLOCK_GAP_BELOW_PT
    )


def _thumbnail_grid_layout(page_w: float, page_h: float) -> tuple[int, float, float, float, float, float]:
    """Return ``(capacity, grid_top, cell_w, thumb_w, thumb_h, cell_h)``.

    ``grid_top`` is the Y coordinate (PDF points, origin bottom-left) of the
    top edge of the thumbnail grid assuming a max-height title block.
    ``capacity`` is how many thumbnails fit on one such page.
    """
    title_block_h = _max_title_block_height()
    grid_top = page_h - _MARGIN_PT - title_block_h
    grid_bottom = _MARGIN_PT
    avail_w = page_w - 2 * _MARGIN_PT
    cell_w = avail_w / _THUMB_COLS
    thumb_w = cell_w - _THUMB_PADDING_PT
    thumb_h = thumb_w * 0.70  # 5:7 aspect leaves room for a caption line
    cell_h = thumb_h + 22
    rows_per_page = max(1, int((grid_top - grid_bottom) // cell_h))
    capacity = rows_per_page * _THUMB_COLS
    return capacity, grid_top, cell_w, thumb_w, thumb_h, cell_h


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

    title_block_h = _draw_title_block(c, page_w, page_h, title, caption)

    avail_w = page_w - 2 * _MARGIN_PT
    avail_h = page_h - 2 * _MARGIN_PT - title_block_h
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
            f"{len(cal.labels)} labels found by OCR. Each green box is where OCR "
            "found a label and the green text is what it read. The translucent "
            "blue fill marks the room polygon we associated with that label. "
            "Scan for boxes around things that aren't actually room numbers."
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
        _draw_room_fill(c, fit, room, _LABELED_ROOM_FILL_RGB, alpha=0.25)

    # 2) Green bboxes + OCR text.
    c.setStrokeColorRGB(0.0, 0.6, 0.0)
    c.setLineWidth(0.4)
    c.setFont("Helvetica", 6)
    c.setFillColorRGB(0.0, 0.4, 0.0)
    for label in cal.labels:
        _draw_label_box(c, fit, label, text=label.id)


def _render_page2_polygons(c, page_w, page_h, map_image, cal: Calibration) -> None:
    """Page 2: every CC polygon outlined in a distinct color + annotations."""
    fit = _embed_map(
        c, page_w, page_h, map_image,
        title="Page 2 — Connected-component room polygons",
        caption=(
            f"{len(cal.rooms)} rooms detected. Each polygon is outlined in a distinct color. "
            "Watch for two rooms merged into a single polygon (an open doorway "
            "that needs a wall_patches entry) or a single room split into two by "
            "a stray dark pixel."
        ),
    )

    c.setLineWidth(0.8)
    c.setFont("Helvetica", 5)
    for idx, room in enumerate(cal.rooms):
        rgb = _distinct_color(idx, len(cal.rooms))
        c.setStrokeColorRGB(*rgb)
        c.setFillColorRGB(*rgb)
        _draw_room_outline(c, fit, room)

        # Annotate with id + area at the room's bbox top-left.
        x_pdf, y_pdf = fit.to_pdf(room.bbox[0], room.bbox[1])
        c.drawString(x_pdf, y_pdf - 6, f"#{room.id} {room.area_px}px")


def _render_page3_confidence(c, page_w, page_h, map_image, cal: Calibration) -> None:
    """Page 3: thumbnail grid of labels sorted by ascending OCR confidence.

    Paginates across as many PDF pages as needed (one page per
    ``_thumbnail_grid_layout`` capacity chunk) so every label is visible.
    """
    sorted_labels = sorted(cal.labels, key=lambda lab: lab.ocr_confidence)
    _render_label_thumbnail_grid(
        c, page_w, page_h, map_image,
        labels=sorted_labels,
        title="Page 3 — Labels by ascending OCR confidence",
        caption=(
            f"{len(sorted_labels)} label(s), sorted lowest-confidence first. "
            "Misreads almost always live near the top — compare the green text "
            "(what OCR thought it read) to the original digits in each crop. "
            "To edit one, find it in calibration.json by its room number (second caption line)."
        ),
    )


def _render_page4_orphans(c, page_w, page_h, map_image, cal: Calibration) -> None:
    """Page 4: orphan labels (no room) + orphan rooms (no label).

    Paginates so every orphan is visible (used to be capped at 60, which hid
    every orphan room on real floor plans with hundreds of detected polygons).
    """
    orphan_labels = [lab for lab in cal.labels if lab.room_id is None]
    referenced_rooms = {lab.room_id for lab in cal.labels if lab.room_id is not None}
    orphan_rooms = [r for r in cal.rooms if r.id not in referenced_rooms]

    # Build pseudo-labels for orphan rooms so we can reuse the thumbnail grid.
    # These are visually distinguishable by their ``room#<id>`` id form.
    pseudo: list[Label] = []
    for room in orphan_rooms:
        pseudo.append(
            Label(
                id=f"room#{room.id}",
                bbox=room.bbox,
                room_id=room.id,
                fill_seed=(room.bbox[0] + room.bbox[2] // 2, room.bbox[1] + room.bbox[3] // 2),
                ocr_confidence=0.0,
            )
        )

    combined = orphan_labels + pseudo

    _render_label_thumbnail_grid(
        c, page_w, page_h, map_image,
        labels=combined,
        title="Page 4 — Orphans",
        caption=(
            f"{len(orphan_labels)} label(s) with no room (shown first) + "
            f"{len(orphan_rooms)} room(s) with no label (shown after, with id 'room#N'). "
            "Each tile is the relevant area cropped from the map. "
            "Resolve by editing calibration.json (set a label's room_id, "
            "add a new Label entry pointing at an orphan room, or delete "
            "the spurious entry)."
        ),
    )


# ---------------------------------------------------------------------------
# Drawing helpers
# ---------------------------------------------------------------------------


def _draw_label_box(c, fit: _MapFit, label: Label, *, text: str) -> None:
    """Draw a green outline rectangle around the OCR bbox + the OCR-read text below it.

    The rectangle is padded outward by ``_BBOX_PAD_PT`` PDF points so the outline
    stays clear of the actual digits in the map (otherwise the stroke would
    overlap the glyphs and the box would look filled at PDF scale, because the
    raw OCR bbox is only a few PDF points across).
    """
    x, y, w, h = label.bbox
    x_pdf_l, y_pdf_t = fit.to_pdf(x, y)              # upper-left in PDF
    x_pdf_r, y_pdf_b = fit.to_pdf(x + w, y + h)      # lower-right in PDF
    rect_x = x_pdf_l - _BBOX_PAD_PT
    rect_y = y_pdf_b - _BBOX_PAD_PT
    rect_w = (x_pdf_r - x_pdf_l) + 2 * _BBOX_PAD_PT
    rect_h = (y_pdf_t - y_pdf_b) + 2 * _BBOX_PAD_PT
    c.rect(rect_x, rect_y, rect_w, rect_h, stroke=1, fill=0)
    # OCR-read text drawn just below the rectangle so it doesn't obscure the
    # digits inside; baseline is _LABEL_GAP_PT below the rectangle bottom.
    c.drawString(rect_x, rect_y - _LABEL_GAP_PT, text)


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
    """Render label crops in a uniform grid, paginating across PDF pages.

    All ``labels`` are rendered — nothing is truncated. If the list exceeds
    one page's grid capacity (see ``_thumbnail_grid_layout``), additional PDF
    pages are emitted via ``c.showPage()`` BETWEEN chunks. The trailing
    ``showPage()`` is left to the caller (``build_calibration_review_pdf``).

    Paginated pages get a ``(page i of N)`` suffix appended to ``caption``.
    """
    from reportlab.lib.utils import ImageReader

    if not labels:
        _draw_title_block(c, page_w, page_h, title, caption)
        c.setFont("Helvetica-Oblique", 12)
        c.drawString(_MARGIN_PT, page_h / 2, "(none)")
        return

    capacity, grid_top, cell_w, thumb_w, thumb_h, cell_h = _thumbnail_grid_layout(
        page_w, page_h
    )

    total = len(labels)
    num_pages = (total + capacity - 1) // capacity  # ceil-div

    for page_i in range(num_pages):
        if page_i > 0:
            c.showPage()

        chunk = labels[page_i * capacity : (page_i + 1) * capacity]
        page_caption = caption
        if num_pages > 1:
            page_caption = f"{caption} (page {page_i + 1} of {num_pages})"
        _draw_title_block(c, page_w, page_h, title, page_caption)

        for i, label in enumerate(chunk):
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


# Uniform translucent blue for any room that has at least one label, drawn
# on page 1 as a faint backdrop so the user can confirm label↔room association.
_LABELED_ROOM_FILL_RGB: tuple[float, float, float] = (0.40, 0.70, 1.00)


__all__ = ["build_calibration_review_pdf"]
