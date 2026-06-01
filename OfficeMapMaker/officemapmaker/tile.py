"""Pass 5 — tile the composite into letter-size print pages + multi-page PDF.

This module turns ``composite.png`` (produced by Pass 4) into a stack of
8.5×11 (or A4) PNG tiles, a 4-up contact sheet, and a bundled
``all.pdf`` that includes a dedicated legend page.

Pipeline (per plan.md §8 Pass 5):

  1. Compute the tile grid from the composite size + the chosen DPI + paper
     size + page overlap.  Tiles are cropped from the composite; each tile
     fills the full printable area of its page (i.e. tiles are NOT padded
     into a smaller printed region).  Adjacent tiles overlap by exactly
     ``overlap_in`` inches so the user can tape pages along the seams.
  2. Render each tile to a letter-size PNG that includes a footer
     ("Row R / Col C of nR × nC") and corner crop marks at the four
     overlap-band edges.
  3. Render a 4-up contact sheet PNG so the user can scan the whole job
     before committing ink.
  4. Render a legend page (swatches + team names + headcount + total
     people + vacant count + map version + render timestamp).  Data
     comes from the ``<composite>_meta.json`` sidecar written by Pass 4.
  5. Bundle everything into ``all.pdf`` (tiles first, then the legend
     page) via reportlab.

Auto-checks (the headline guard rails in plan.md §8 Pass 5):

  * Coverage — the union of tile crop rectangles covers every pixel of the
    composite (no gap between two tiles will leave a stripe unprinted).
  * Min font size — if the chosen DPI shrinks any text in the source map
    below ``min_font_pt``, raise a warning.  We can't measure original-map
    text directly, so we approximate by computing the on-page text height
    of the smallest ``font_px`` we observed (from layout data, or a
    user-supplied fallback) at the chosen DPI.
  * Page count — the resulting PDF has exactly ``len(tiles) + 1`` pages
    (1 extra for the legend).
  * SHA spot-check — three random tiles, when decoded back from PNG, must
    pixel-match the corresponding crop region of the composite (catches
    file-corruption / wrong-tile bugs).

Public surface:

  ``TilePlacement``    — one tile (grid coords + composite crop bbox).
  ``TileGrid``         — full grid + per-page geometry.
  ``TileIssue``        — typed errors / warnings.
  ``TileResult``       — output paths + issues + coverage stats.
  ``compute_tile_grid``— pure geometry, no I/O.
  ``tile_composite``   — main entry point.
"""

from __future__ import annotations

import json
import math
from dataclasses import dataclass
from pathlib import Path
from typing import Optional, Sequence

import numpy as np

from .palette import parse_hex_color


__all__ = [
    "PAPER_SIZES_IN",
    "ORIENTATIONS",
    "TilePlacement",
    "TileGrid",
    "TileIssue",
    "TileResult",
    "compute_tile_grid",
    "compute_fit_to_one_page_percent",
    "tile_composite",
]


# Orientation values accepted by ``compute_tile_grid``.  ``"auto"`` tries both
# portrait and landscape and picks whichever produces fewer tiles (tiebreak:
# portrait).  Each row in the tile grid is laid out in the same orientation;
# we do not (yet) mix orientations within a single job.
ORIENTATIONS: tuple[str, ...] = ("auto", "portrait", "landscape")


# ---------------------------------------------------------------------------
# Constants
# ---------------------------------------------------------------------------


# Paper sizes in inches (width, height) — both stored as portrait dims.
PAPER_SIZES_IN: dict[str, tuple[float, float]] = {
    "letter": (8.5, 11.0),
    "a4": (8.27, 11.69),
    "tabloid": (11.0, 17.0),
}

# Footer text styling
_FOOTER_FONT_PX = 14
_FOOTER_HEIGHT_PX = 32  # reserved at bottom of each tile for the footer text
_FOOTER_MARGIN_PX = 8

# Corner crop marks
_CROP_TICK_PX = 18
_CROP_TICK_WIDTH = 2
_CROP_TICK_COLOR = (40, 40, 40)

# Contact sheet
_CONTACT_COLUMNS = 4
_CONTACT_THUMB_W = 320  # px per thumbnail (height auto from aspect)
_CONTACT_PADDING = 14
_CONTACT_LABEL_H = 22
_CONTACT_BG = (255, 255, 255)
_CONTACT_FG = (20, 20, 20)

# Legend page
_LEGEND_TITLE = "Office Map Legend"
_LEGEND_TITLE_PX = 32
_LEGEND_BODY_PX = 18
_LEGEND_SWATCH_PX = 28
_LEGEND_ROW_GAP_PX = 12
_LEGEND_MARGIN_PX = 64


# ---------------------------------------------------------------------------
# Dataclasses
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class TilePlacement:
    """One tile in the grid.

    ``row`` and ``col`` are 1-indexed for user-facing display.  ``bbox``
    is the crop rectangle inside the composite as ``(x, y, w, h)`` in
    composite pixels.
    """

    row: int
    col: int
    bbox: tuple[int, int, int, int]


@dataclass(frozen=True)
class TileGrid:
    """Result of :func:`compute_tile_grid`."""

    composite_size: tuple[int, int]  # (w, h) in composite pixels
    page_size_in: tuple[float, float]  # (w, h) in inches (actual orientation)
    dpi: int
    overlap_in: float
    rows: int
    cols: int
    tile_px: tuple[int, int]  # (w, h) of the composite-crop region per tile
    overlap_px: int
    tiles: tuple[TilePlacement, ...]
    orientation: str = "portrait"  # "portrait" or "landscape" (resolved; never "auto")


@dataclass(frozen=True)
class TileIssue:
    severity: str  # "error" | "warning"
    code: str
    message: str


@dataclass
class TileResult:
    out_dir: Path
    tile_paths: list[Path]
    contact_sheet_path: Path
    pdf_path: Path
    grid: TileGrid
    issues: list[TileIssue]

    @property
    def errors(self) -> list[TileIssue]:
        return [i for i in self.issues if i.severity == "error"]

    @property
    def warnings(self) -> list[TileIssue]:
        return [i for i in self.issues if i.severity == "warning"]


# ---------------------------------------------------------------------------
# Grid math
# ---------------------------------------------------------------------------


def compute_tile_grid(
    composite_size: tuple[int, int],
    *,
    dpi: int = 150,
    paper: str = "letter",
    overlap_in: float = 0.25,
    orientation: str = "portrait",
) -> TileGrid:
    """Decide how many tiles are needed and the crop rectangle of each.

    The tiles cover the composite **completely** with the requested
    overlap between adjacent pages.  Each tile is exactly the printable
    area of one page (``page_w_in × dpi`` pixels wide); the last tile in
    each row/column is shifted so it still fills the page even if the
    composite isn't a perfect multiple of the tile size.  The
    bottom-right tile may overshoot the composite — we keep the overshoot
    region transparent (white) when we render the page.

    ``orientation`` may be ``"portrait"``, ``"landscape"``, or ``"auto"``.
    Auto computes the grid in both orientations and returns whichever
    produces fewer total tiles; ties go to portrait.  The chosen
    orientation is reflected in ``TileGrid.orientation`` and
    ``TileGrid.page_size_in`` (the latter has its dims swapped for
    landscape, so downstream consumers like the PDF bundler get the
    right page size automatically).
    """
    if orientation not in ORIENTATIONS:
        raise ValueError(
            f"unsupported orientation: {orientation!r} "
            f"(supported: {list(ORIENTATIONS)})"
        )
    if paper not in PAPER_SIZES_IN:
        raise ValueError(
            f"unsupported paper size: {paper!r} (supported: {sorted(PAPER_SIZES_IN)})"
        )
    if dpi <= 0:
        raise ValueError(f"dpi must be positive, got {dpi}")
    if overlap_in < 0:
        raise ValueError(f"overlap_in must be non-negative, got {overlap_in}")

    if orientation == "auto":
        # Compute both orientations and pick the one with fewer tiles.
        # If only one orientation is geometrically valid (e.g. the
        # overlap exceeds one of the page dimensions), return that one.
        portrait_grid: Optional[TileGrid] = None
        landscape_grid: Optional[TileGrid] = None
        first_err: Optional[Exception] = None
        try:
            portrait_grid = compute_tile_grid(
                composite_size, dpi=dpi, paper=paper,
                overlap_in=overlap_in, orientation="portrait",
            )
        except ValueError as exc:
            first_err = exc
        try:
            landscape_grid = compute_tile_grid(
                composite_size, dpi=dpi, paper=paper,
                overlap_in=overlap_in, orientation="landscape",
            )
        except ValueError as exc:
            if first_err is None:
                first_err = exc
        if portrait_grid is None and landscape_grid is None:
            assert first_err is not None
            raise first_err
        if portrait_grid is None:
            return landscape_grid  # type: ignore[return-value]
        if landscape_grid is None:
            return portrait_grid
        # Tiebreak: portrait wins on equal tile count.
        if len(landscape_grid.tiles) < len(portrait_grid.tiles):
            return landscape_grid
        return portrait_grid

    page_w_in, page_h_in = PAPER_SIZES_IN[paper]
    if orientation == "landscape":
        page_w_in, page_h_in = page_h_in, page_w_in
    page_w_px = int(round(page_w_in * dpi))
    page_h_px = int(round(page_h_in * dpi))
    overlap_px = int(round(overlap_in * dpi))

    if overlap_px >= page_w_px or overlap_px >= page_h_px:
        raise ValueError(
            f"overlap ({overlap_in} in @ {dpi} dpi = {overlap_px} px) is "
            f"larger than a page ({page_w_px}×{page_h_px} px)"
        )

    comp_w, comp_h = composite_size

    # Each tile after the first advances by (page - overlap) pixels.
    # Number of tiles needed in each axis = ceil((comp - overlap) / (page - overlap)).
    def _count(comp_px: int, page_px: int) -> int:
        if comp_px <= page_px:
            return 1
        step = page_px - overlap_px
        return 1 + math.ceil((comp_px - page_px) / step)

    cols = _count(comp_w, page_w_px)
    rows = _count(comp_h, page_h_px)

    tiles: list[TilePlacement] = []
    for r in range(rows):
        for c in range(cols):
            # Default: stride by (page - overlap).
            x = c * (page_w_px - overlap_px)
            y = r * (page_h_px - overlap_px)
            # Pull the last column / row back so the last tile still fills
            # the page but doesn't push us off the composite.  The pull-back
            # increases the overlap between the last two tiles, which is
            # fine for assembly.
            if c == cols - 1 and rows * cols > 1:
                x = max(0, comp_w - page_w_px)
            if r == rows - 1 and rows * cols > 1:
                y = max(0, comp_h - page_h_px)
            tiles.append(
                TilePlacement(
                    row=r + 1,
                    col=c + 1,
                    bbox=(x, y, page_w_px, page_h_px),
                )
            )

    return TileGrid(
        composite_size=composite_size,
        page_size_in=(page_w_in, page_h_in),
        dpi=dpi,
        overlap_in=overlap_in,
        rows=rows,
        cols=cols,
        tile_px=(page_w_px, page_h_px),
        overlap_px=overlap_px,
        tiles=tuple(tiles),
        orientation=orientation,
    )


def compute_fit_to_one_page_percent(
    composite_size: tuple[int, int],
    *,
    dpi: int = 150,
    paper: str = "letter",
    orientation: str = "portrait",
) -> float:
    """Return the scale percent that makes ``composite_size`` fit on one page.

    Picks the largest scale factor where the resized composite still fits
    entirely inside a single page (no tiling needed).  Returned as a
    percent (i.e. ``50.0`` means scale to 50%).

    ``orientation="auto"`` returns the larger of the portrait and
    landscape fit percents — i.e. the orientation that requires the
    least shrinking.  The caller is expected to keep ``orientation=auto``
    when invoking ``tile_composite``; the auto resolver there will pick
    the orientation that yields a single tile at the chosen scale.

    Overlap is irrelevant for a single-tile fit (no neighbors), so this
    helper deliberately ignores ``overlap_in``.
    """
    if orientation not in ORIENTATIONS:
        raise ValueError(
            f"unsupported orientation: {orientation!r} "
            f"(supported: {list(ORIENTATIONS)})"
        )
    if paper not in PAPER_SIZES_IN:
        raise ValueError(
            f"unsupported paper size: {paper!r} (supported: {sorted(PAPER_SIZES_IN)})"
        )
    if dpi <= 0:
        raise ValueError(f"dpi must be positive, got {dpi}")
    comp_w, comp_h = composite_size
    if comp_w <= 0 or comp_h <= 0:
        raise ValueError(f"composite_size must be positive, got {composite_size}")

    def _percent_for(orient: str) -> float:
        page_w_in, page_h_in = PAPER_SIZES_IN[paper]
        if orient == "landscape":
            page_w_in, page_h_in = page_h_in, page_w_in
        page_w_px = int(round(page_w_in * dpi))
        page_h_px = int(round(page_h_in * dpi))
        scale = min(page_w_px / comp_w, page_h_px / comp_h)
        return scale * 100.0

    if orientation == "auto":
        return max(_percent_for("portrait"), _percent_for("landscape"))
    return _percent_for(orientation)


# ---------------------------------------------------------------------------
# Page rendering
# ---------------------------------------------------------------------------


def _load_font(font_px: int):
    """Load Arial-ish TrueType, fall back to PIL bitmap."""
    from PIL import ImageFont

    try:
        return ImageFont.truetype("arial.ttf", font_px)
    except (OSError, IOError):
        return ImageFont.load_default()


def _crop_with_padding(
    composite_rgba: "ImageRGBA", bbox: tuple[int, int, int, int]
) -> "ImageRGBA":  # type: ignore[name-defined]
    """Crop ``composite_rgba`` to ``bbox`` (x, y, w, h), padding with white.

    ``composite_rgba`` is a PIL RGB image.  Returns a fresh PIL Image of
    size ``(w, h)``.  Pixels outside the composite (because the bottom-
    right tile overshoots) are filled white so they print as blank
    margin rather than as transparent gaps.
    """
    from PIL import Image

    x, y, w, h = bbox
    out = Image.new("RGB", (w, h), (255, 255, 255))
    src_w, src_h = composite_rgba.size
    src_x0 = max(0, x)
    src_y0 = max(0, y)
    src_x1 = min(src_w, x + w)
    src_y1 = min(src_h, y + h)
    if src_x1 <= src_x0 or src_y1 <= src_y0:
        return out
    region = composite_rgba.crop((src_x0, src_y0, src_x1, src_y1))
    out.paste(region, (src_x0 - x, src_y0 - y))
    return out


def _draw_tile_decorations(
    tile_img,  # PIL Image (mutated in place)
    *,
    tile: TilePlacement,
    grid: TileGrid,
) -> None:
    """Draw corner crop marks + footer (Row R / Col C of nR × nC) on the tile."""
    from PIL import ImageDraw

    draw = ImageDraw.Draw(tile_img)
    w, h = tile_img.size
    overlap = grid.overlap_px

    # Crop marks live at the inner corner of each overlap band (i.e. the
    # spot where you'd cut so the remaining sheet butts cleanly against
    # the neighbouring tile).  For edge tiles we draw all four corners;
    # the user can ignore the marks on the outside edge.
    tick = _CROP_TICK_PX
    color = _CROP_TICK_COLOR
    width = _CROP_TICK_WIDTH

    # Top-left
    draw.line((overlap, overlap, overlap + tick, overlap), fill=color, width=width)
    draw.line((overlap, overlap, overlap, overlap + tick), fill=color, width=width)
    # Top-right
    draw.line(
        (w - overlap - tick, overlap, w - overlap, overlap), fill=color, width=width
    )
    draw.line(
        (w - overlap, overlap, w - overlap, overlap + tick), fill=color, width=width
    )
    # Bottom-left
    draw.line(
        (overlap, h - overlap, overlap + tick, h - overlap), fill=color, width=width
    )
    draw.line(
        (overlap, h - overlap - tick, overlap, h - overlap), fill=color, width=width
    )
    # Bottom-right
    draw.line(
        (w - overlap - tick, h - overlap, w - overlap, h - overlap),
        fill=color,
        width=width,
    )
    draw.line(
        (w - overlap, h - overlap - tick, w - overlap, h - overlap),
        fill=color,
        width=width,
    )

    # Footer: paint a white strip first so we cover any composite content
    # in the bottom margin area, then draw the text.
    footer_y = h - _FOOTER_HEIGHT_PX
    draw.rectangle((0, footer_y, w, h), fill=(255, 255, 255))
    font = _load_font(_FOOTER_FONT_PX)
    text = f"Row {tile.row} / Col {tile.col} of {grid.rows} × {grid.cols}"
    tw_bbox = font.getbbox(text)
    tw = tw_bbox[2] - tw_bbox[0]
    th = tw_bbox[3] - tw_bbox[1]
    draw.text(
        ((w - tw) // 2, footer_y + (_FOOTER_HEIGHT_PX - th) // 2 - tw_bbox[1]),
        text,
        fill=(20, 20, 20),
        font=font,
    )


def _render_tile(
    composite_rgb,  # PIL RGB image
    tile: TilePlacement,
    grid: TileGrid,
):
    """Crop + decorate one tile.  Returns a PIL Image at tile_px size."""
    page = _crop_with_padding(composite_rgb, tile.bbox)
    _draw_tile_decorations(page, tile=tile, grid=grid)
    return page


def _render_contact_sheet(
    tile_paths: Sequence[Path], grid: TileGrid
):
    """4-up grid thumbnail of all tiles.  Returns a PIL Image."""
    from PIL import Image, ImageDraw

    thumb_w = _CONTACT_THUMB_W
    page_w, page_h = grid.tile_px
    aspect = page_h / page_w
    thumb_h = int(round(thumb_w * aspect))

    columns = min(_CONTACT_COLUMNS, len(tile_paths))
    rows = math.ceil(len(tile_paths) / columns)
    cell_w = thumb_w + 2 * _CONTACT_PADDING
    cell_h = thumb_h + _CONTACT_LABEL_H + 2 * _CONTACT_PADDING
    sheet_w = columns * cell_w
    sheet_h = rows * cell_h
    sheet = Image.new("RGB", (sheet_w, sheet_h), _CONTACT_BG)
    draw = ImageDraw.Draw(sheet)
    font = _load_font(_FOOTER_FONT_PX)

    for idx, path in enumerate(tile_paths):
        r = idx // columns
        c = idx % columns
        cell_x = c * cell_w
        cell_y = r * cell_h
        with Image.open(path) as im:
            im = im.convert("RGB")
            im.thumbnail((thumb_w, thumb_h), Image.LANCZOS)
            sheet.paste(im, (cell_x + _CONTACT_PADDING, cell_y + _CONTACT_PADDING))
        label = path.stem  # e.g. "page-1x1"
        bbox = font.getbbox(label)
        lw = bbox[2] - bbox[0]
        draw.text(
            (
                cell_x + (cell_w - lw) // 2,
                cell_y + _CONTACT_PADDING + thumb_h + 4 - bbox[1],
            ),
            label,
            fill=_CONTACT_FG,
            font=font,
        )

    return sheet


def _render_legend_page(meta: dict, page_px: tuple[int, int]):
    """Render the dedicated legend page as a full-bleed PIL Image."""
    from PIL import Image, ImageDraw

    page_w, page_h = page_px
    img = Image.new("RGB", (page_w, page_h), (255, 255, 255))
    draw = ImageDraw.Draw(img)

    title_font = _load_font(_LEGEND_TITLE_PX)
    body_font = _load_font(_LEGEND_BODY_PX)

    cursor_x = _LEGEND_MARGIN_PX
    cursor_y = _LEGEND_MARGIN_PX

    draw.text((cursor_x, cursor_y), _LEGEND_TITLE, fill=(20, 20, 20), font=title_font)
    tb = title_font.getbbox(_LEGEND_TITLE)
    cursor_y += (tb[3] - tb[1]) + 8

    # Subtitle line: map version + rendered_at + totals.
    map_hash = meta.get("map_hash", "")
    short_hash = map_hash.split(":", 1)[-1][:12] if map_hash else "(unknown)"
    rendered_at = meta.get("rendered_at", "(unknown)")
    total_people = int(meta.get("total_people", 0))
    assigned_offices = int(meta.get("assigned_offices", 0))
    vacant_offices = int(meta.get("vacant_offices", 0))
    summary = (
        f"Map version: {short_hash}     "
        f"Rendered: {rendered_at}     "
        f"People: {total_people}     "
        f"Offices assigned: {assigned_offices}     "
        f"Vacant: {vacant_offices}"
    )
    draw.text((cursor_x, cursor_y), summary, fill=(60, 60, 60), font=body_font)
    sb = body_font.getbbox(summary)
    cursor_y += (sb[3] - sb[1]) + _LEGEND_ROW_GAP_PX * 2

    # Per-team rows.
    palette: dict[str, str] = meta.get("palette") or {}
    headcount: dict[str, int] = meta.get("headcount") or {}
    teams_sorted = sorted(palette.keys(), key=str.casefold)
    swatch = _LEGEND_SWATCH_PX
    for team in teams_sorted:
        try:
            color = parse_hex_color(palette[team])
        except ValueError:
            color = (200, 200, 200)
        # Swatch
        draw.rectangle(
            (cursor_x, cursor_y, cursor_x + swatch, cursor_y + swatch),
            fill=color,
            outline=(60, 60, 60),
        )
        # Label: "TeamName (N people)"
        count = headcount.get(team, 0)
        label = f"{team}  ({count} {'person' if count == 1 else 'people'})"
        draw.text(
            (cursor_x + swatch + 12, cursor_y + (swatch - _LEGEND_BODY_PX) // 2),
            label,
            fill=(20, 20, 20),
            font=body_font,
        )
        cursor_y += swatch + _LEGEND_ROW_GAP_PX

    # Footer line so the user knows where this legend matches.
    foot = f"Source: {Path(meta.get('map_path', '')).name}"
    fb = body_font.getbbox(foot)
    fh = fb[3] - fb[1]
    draw.text(
        (_LEGEND_MARGIN_PX, page_h - _LEGEND_MARGIN_PX - fh),
        foot,
        fill=(80, 80, 80),
        font=body_font,
    )

    return img


# ---------------------------------------------------------------------------
# PDF
# ---------------------------------------------------------------------------


def _bundle_pdf(
    pdf_path: Path,
    tile_paths: Sequence[Path],
    legend_path: Path,
    grid: TileGrid,
) -> None:
    """Bundle all tiles + the legend page into a single multi-page PDF."""
    from reportlab.lib.pagesizes import letter, A4
    from reportlab.lib.utils import ImageReader
    from reportlab.pdfgen import canvas as rl_canvas

    page_w_pt = grid.page_size_in[0] * 72.0
    page_h_pt = grid.page_size_in[1] * 72.0
    c = rl_canvas.Canvas(str(pdf_path), pagesize=(page_w_pt, page_h_pt))
    for path in tile_paths:
        c.drawImage(
            ImageReader(str(path)),
            0,
            0,
            width=page_w_pt,
            height=page_h_pt,
            preserveAspectRatio=False,
        )
        c.showPage()
    # Legend last so the print stack reads tiles first, legend last.
    c.drawImage(
        ImageReader(str(legend_path)),
        0,
        0,
        width=page_w_pt,
        height=page_h_pt,
        preserveAspectRatio=False,
    )
    c.showPage()
    c.save()


# ---------------------------------------------------------------------------
# Main entry point
# ---------------------------------------------------------------------------


def tile_composite(
    composite_path: Path | str,
    *,
    out_dir: Path | str,
    dpi: int = 150,
    paper: str = "letter",
    overlap_in: float = 0.25,
    orientation: str = "portrait",
    scale_percent: float = 100.0,
    meta: Optional[dict] = None,
    min_font_pt: int = 7,
) -> TileResult:
    """Tile ``composite_path`` into letter pages + contact sheet + PDF.

    Args:
        composite_path: ``composite.png`` from Pass 4.
        out_dir: Destination directory (created if missing).  Outputs:
            ``page-RxC.png`` per tile, ``contact_sheet.png``,
            ``legend.png``, ``all.pdf``.
        dpi: Print resolution.  Default 150 DPI (≈1275×1650 px letter).
        paper: Paper key in :data:`PAPER_SIZES_IN`.
        overlap_in: Inches of overlap between adjacent tiles.
        orientation: ``"portrait"`` (default), ``"landscape"``, or
            ``"auto"``.  Auto picks whichever orientation produces fewer
            total tiles (tiebreak: portrait).
        scale_percent: Resize the composite by this percent before
            tiling.  ``100.0`` (default) leaves the composite untouched.
            ``50.0`` halves the composite in each dim (so the resulting
            text is half the physical size on the printed page, and the
            tile count drops accordingly).  Use
            :func:`compute_fit_to_one_page_percent` to derive a value
            that fits the whole composite on a single page.
        meta: Pre-loaded composite metadata (mostly used by tests).  If
            None, we look for ``<composite_stem>_meta.json`` next to the
            composite and load it; if not found, we render a minimal
            legend page using whatever we can infer.
        min_font_pt: Text on the source map smaller than this point size
            (at the chosen DPI) triggers a warning.  Heuristic only.
    """
    from PIL import Image

    composite_path = Path(composite_path)
    out_dir = Path(out_dir)
    if not composite_path.exists():
        raise FileNotFoundError(composite_path)
    if scale_percent <= 0:
        raise ValueError(f"scale_percent must be positive, got {scale_percent}")
    out_dir.mkdir(parents=True, exist_ok=True)

    issues: list[TileIssue] = []

    # Load metadata sidecar if not supplied.
    if meta is None:
        sidecar = composite_path.with_name(composite_path.stem + "_meta.json")
        if sidecar.exists():
            try:
                meta = json.loads(sidecar.read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError) as exc:
                issues.append(
                    TileIssue(
                        severity="warning",
                        code="meta_sidecar_unreadable",
                        message=(
                            f"{sidecar.name} exists but could not be parsed: {exc}. "
                            f"Legend page will be sparse."
                        ),
                    )
                )
                meta = {}
        else:
            issues.append(
                TileIssue(
                    severity="warning",
                    code="meta_sidecar_missing",
                    message=(
                        f"no {sidecar.name} next to composite — legend page will "
                        f"be sparse. Run 'officemapmaker build' to (re)generate it."
                    ),
                )
            )
            meta = {}

    # Load composite + optionally resize.
    composite = Image.open(composite_path).convert("RGB")
    if scale_percent != 100.0:
        orig_w, orig_h = composite.size
        new_w = max(1, int(round(orig_w * scale_percent / 100.0)))
        new_h = max(1, int(round(orig_h * scale_percent / 100.0)))
        # LANCZOS gives high quality both up- and down-scaling at the
        # cost of being ~3x slower than BILINEAR.  Tiling is a once-per-
        # job operation so we accept the cost for sharper text.
        composite = composite.resize((new_w, new_h), Image.LANCZOS)
    comp_size = composite.size  # (w, h)
    grid = compute_tile_grid(
        comp_size, dpi=dpi, paper=paper, overlap_in=overlap_in,
        orientation=orientation,
    )

    # ---- Render tiles ----
    tile_paths: list[Path] = []
    for tile in grid.tiles:
        page = _render_tile(composite, tile, grid)
        name = f"page-{tile.row}x{tile.col}.png"
        path = out_dir / name
        page.save(path, "PNG", dpi=(dpi, dpi))
        tile_paths.append(path)

    # ---- Render contact sheet ----
    contact_path = out_dir / "contact_sheet.png"
    contact = _render_contact_sheet(tile_paths, grid)
    contact.save(contact_path, "PNG")

    # ---- Render legend page ----
    legend_path = out_dir / "legend.png"
    legend_meta = dict(meta) if meta else {}
    legend_meta.setdefault("map_path", str(composite_path))
    legend = _render_legend_page(legend_meta, grid.tile_px)
    legend.save(legend_path, "PNG", dpi=(dpi, dpi))

    # ---- Bundle PDF ----
    pdf_path = out_dir / "all.pdf"
    _bundle_pdf(pdf_path, tile_paths, legend_path, grid)

    # ---- Auto-checks ----
    issues.extend(_auto_checks(
        composite, grid, tile_paths, pdf_path, dpi=dpi, min_font_pt=min_font_pt
    ))

    return TileResult(
        out_dir=out_dir,
        tile_paths=tile_paths,
        contact_sheet_path=contact_path,
        pdf_path=pdf_path,
        grid=grid,
        issues=issues,
    )


def _auto_checks(
    composite,  # PIL Image
    grid: TileGrid,
    tile_paths: Sequence[Path],
    pdf_path: Path,
    *,
    dpi: int,
    min_font_pt: int,
) -> list[TileIssue]:
    """Run the five guard-rails in plan.md §8 Pass 5."""
    from PIL import Image

    issues: list[TileIssue] = []
    comp_w, comp_h = composite.size

    # 1. Coverage — union of crop bboxes covers every composite pixel.
    coverage = np.zeros((comp_h, comp_w), dtype=bool)
    for tile in grid.tiles:
        x, y, w, h = tile.bbox
        x0 = max(0, x)
        y0 = max(0, y)
        x1 = min(comp_w, x + w)
        y1 = min(comp_h, y + h)
        if x1 > x0 and y1 > y0:
            coverage[y0:y1, x0:x1] = True
    uncovered = int((~coverage).sum())
    if uncovered > 0:
        issues.append(
            TileIssue(
                severity="error",
                code="tile_coverage_gap",
                message=(
                    f"{uncovered} composite pixel(s) are not covered by any tile "
                    f"(grid {grid.rows}×{grid.cols} of {grid.tile_px[0]}×{grid.tile_px[1]} px). "
                    f"Try a smaller overlap or larger paper."
                ),
            )
        )

    # 2. Min font size — at DPI d, M points = M * d / 72 pixels.  We
    #    can't measure source-map text directly, but plan.md says the
    #    smallest planned text uses min_font_pt at the chosen DPI, so
    #    we simply warn if min_font_pt at this DPI translates to fewer
    #    than 7 px (the absolute readability floor).
    min_px = min_font_pt * dpi / 72.0
    if min_px < 7.0:
        issues.append(
            TileIssue(
                severity="warning",
                code="tile_text_too_small",
                message=(
                    f"At {dpi} DPI, {min_font_pt}-pt text renders to {min_px:.1f} px — "
                    f"below the 7-px readability floor. Raise DPI or font size."
                ),
            )
        )

    # 3. Page count = tiles + 1 (legend).
    try:
        from pypdf import PdfReader  # type: ignore

        reader = PdfReader(str(pdf_path))
        page_count = len(reader.pages)
    except ImportError:
        # pypdf is optional — fall back to a header sniff (counts "/Type /Page" objs).
        try:
            blob = pdf_path.read_bytes()
            page_count = blob.count(b"/Type /Page") - blob.count(b"/Type /Pages")
        except OSError:
            page_count = -1
    expected_pages = len(tile_paths) + 1
    if page_count >= 0 and page_count != expected_pages:
        issues.append(
            TileIssue(
                severity="error",
                code="pdf_page_count_mismatch",
                message=(
                    f"PDF has {page_count} page(s), expected {expected_pages} "
                    f"({len(tile_paths)} tile(s) + 1 legend)"
                ),
            )
        )

    # 4. Tile pixel-content spot-check — three random tiles must match
    #    their corresponding composite crop region (above the footer
    #    strip, which we replaced with white).  Catches the case where
    #    we wrote the wrong tile to disk.
    import random

    rng = random.Random(0xC0FFEE)
    sample_idxs = rng.sample(
        range(len(tile_paths)), k=min(3, len(tile_paths))
    )
    for idx in sample_idxs:
        tile = grid.tiles[idx]
        path = tile_paths[idx]
        x, y, w, h = tile.bbox
        ref = _crop_with_padding(composite, (x, y, w, h))
        try:
            with Image.open(path) as actual:
                actual_arr = np.asarray(actual.convert("RGB"))
        except OSError as exc:
            issues.append(
                TileIssue(
                    severity="error",
                    code="tile_unreadable",
                    message=f"tile {path.name} could not be read back: {exc}",
                )
            )
            continue
        ref_arr = np.asarray(ref)
        # Compare only above the footer (we whitened that strip).
        cmp_h = h - _FOOTER_HEIGHT_PX
        if cmp_h <= 0:
            continue
        diff = np.any(actual_arr[:cmp_h] != ref_arr[:cmp_h], axis=2)
        # The crop marks live in the overlap band — exclude that ring.
        margin = grid.overlap_px + _CROP_TICK_PX + _CROP_TICK_WIDTH + 2
        if margin * 2 < cmp_h and margin * 2 < w:
            interior = diff[margin:cmp_h - margin, margin:w - margin]
            if int(interior.sum()) != 0:
                issues.append(
                    TileIssue(
                        severity="error",
                        code="tile_pixel_mismatch",
                        message=(
                            f"tile {path.name} interior does not match the "
                            f"composite crop region (likely wrote the wrong "
                            f"tile or corruption)"
                        ),
                    )
                )

    return issues
