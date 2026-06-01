"""Pure-function geometry helpers used by the calibration / validation / layout passes.

Everything in this module is unit-testable without OpenCV's Tesseract dependency,
without an installed font, and without any disk I/O. The functions are kept small
and composable so that the higher-level passes (``calibrate``, ``validate``,
``layout``, ``render``) can build pipelines from them without reimplementing
common operations like "centroid of a polygon" or "is this point inside this
binary mask".

Conventions used throughout this codebase:

* Coordinates are 2-tuples ``(x, y)`` in image-space — x grows right, y grows down.
* Bounding boxes are 4-tuples ``(x, y, w, h)`` matching OpenCV's ``boundingRect``.
* Binary masks are ``numpy.uint8`` arrays of shape ``(H, W)`` with values 0 or 255.
  255 = "foreground / inside the region", 0 = "background / outside".
* When a function takes both a mask and a point, the point must be in image-space
  (not mask-relative); the mask is assumed to span the whole image.
"""

from __future__ import annotations

import base64
import zlib
from dataclasses import dataclass
from typing import Iterable, Optional

import numpy as np


BBox = tuple[int, int, int, int]   # (x, y, w, h)
Point = tuple[int, int]             # (x, y)


# ---------------------------------------------------------------------------
# Connected components
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class ConnectedComponent:
    """One connected component of a binary mask.

    Attributes:
        cc_id: Label index returned by ``cv2.connectedComponentsWithStats``
            (1-based; background has id 0).
        bbox: ``(x, y, w, h)`` of the component's axis-aligned bounding box.
        area_px: Number of foreground pixels in this component.
        centroid: ``(cx, cy)`` floating-point centroid, rounded to int pixels.
        mask: ``numpy.uint8`` array of shape ``(H, W)`` containing only this
            component (255 inside, 0 outside). Same dimensions as the source
            mask so it can be composed directly.
    """

    cc_id: int
    bbox: BBox
    area_px: int
    centroid: Point
    mask: np.ndarray


def find_connected_components(
    binary: np.ndarray,
    *,
    min_area: int = 1,
    discard_largest: bool = True,
) -> list[ConnectedComponent]:
    """Run connected-components on a binary mask and wrap each in a typed record.

    Args:
        binary: ``numpy.uint8`` mask of shape ``(H, W)`` with values 0 or 255.
            Foreground (the regions to label) must be 255.
        min_area: Components with fewer than this many foreground pixels are
            discarded as noise. Default 1 (keep everything).
        discard_largest: If True (default), the single largest component is
            dropped. For floor-plan use this is the giant "outside the
            building" background region.

    Returns:
        List of ``ConnectedComponent`` records, sorted by descending area.

    Raises:
        ImportError: if OpenCV is not installed.
        ValueError: if ``binary`` is not a 2-D uint8 array.
    """
    try:
        import cv2
    except ImportError as e:
        raise ImportError(
            "opencv-python is required for connected-components "
            "(pip install opencv-python)"
        ) from e

    if binary.ndim != 2 or binary.dtype != np.uint8:
        raise ValueError(
            f"binary must be a 2-D uint8 array, got shape={binary.shape} dtype={binary.dtype}"
        )

    n, labels, stats, centroids = cv2.connectedComponentsWithStats(
        binary, connectivity=8
    )

    out: list[ConnectedComponent] = []
    # Skip label 0 (background of the connectedComponents algorithm, not our
    # background — our "background" is everything that isn't foreground in the
    # input mask, which is already excluded from the labeled set).
    for cc_id in range(1, n):
        x, y, w, h, area = (int(v) for v in stats[cc_id])
        if area < min_area:
            continue
        cx, cy = centroids[cc_id]
        component_mask = np.where(labels == cc_id, np.uint8(255), np.uint8(0))
        out.append(
            ConnectedComponent(
                cc_id=cc_id,
                bbox=(x, y, w, h),
                area_px=area,
                centroid=(int(round(cx)), int(round(cy))),
                mask=component_mask,
            )
        )

    out.sort(key=lambda c: c.area_px, reverse=True)
    if discard_largest and out:
        out = out[1:]
    return out


# ---------------------------------------------------------------------------
# Bounding-box / point geometry
# ---------------------------------------------------------------------------


def bbox_center(bbox: BBox) -> Point:
    """Pixel-center of an ``(x, y, w, h)`` bounding box."""
    x, y, w, h = bbox
    return (x + w // 2, y + h // 2)


def bbox_contains_point(bbox: BBox, point: Point) -> bool:
    """True if ``point`` lies within ``bbox`` (inclusive of left/top edges)."""
    x, y, w, h = bbox
    px, py = point
    return x <= px < x + w and y <= py < y + h


def bbox_contains_bbox(outer: BBox, inner: BBox) -> bool:
    """True if ``inner`` is fully contained within ``outer``."""
    ox, oy, ow, oh = outer
    ix, iy, iw, ih = inner
    return ix >= ox and iy >= oy and ix + iw <= ox + ow and iy + ih <= oy + oh


def bbox_intersects(a: BBox, b: BBox) -> bool:
    """True if two bounding boxes share at least one pixel."""
    ax, ay, aw, ah = a
    bx, by, bw, bh = b
    return not (ax + aw <= bx or bx + bw <= ax or ay + ah <= by or by + bh <= ay)


def bbox_area(bbox: BBox) -> int:
    """Area of an ``(x, y, w, h)`` bounding box, in pixels."""
    _, _, w, h = bbox
    return w * h


def expand_bbox(bbox: BBox, *, margin: int) -> BBox:
    """Return a bbox grown by ``margin`` pixels on every side."""
    x, y, w, h = bbox
    return (x - margin, y - margin, w + 2 * margin, h + 2 * margin)


# ---------------------------------------------------------------------------
# Mask / point operations
# ---------------------------------------------------------------------------


def mask_contains_point(mask: np.ndarray, point: Point) -> bool:
    """True if ``point`` is inside the image and the mask pixel there is foreground."""
    px, py = point
    h, w = mask.shape[:2]
    if not (0 <= px < w and 0 <= py < h):
        return False
    return bool(mask[py, px])


def mask_area(mask: np.ndarray) -> int:
    """Count of nonzero pixels in ``mask``."""
    return int(np.count_nonzero(mask))


def mask_centroid(mask: np.ndarray) -> Optional[Point]:
    """Centroid (rounded to int pixels) of the nonzero pixels in ``mask``.

    Returns ``None`` if the mask is entirely zero.

    NOTE: For picking flood-fill seeds, prefer :func:`pole_of_inaccessibility`.
    The geometric centroid is the mean of all foreground pixels, which can land
    on a "hole" within the mask — e.g. a room interior whose connected component
    has letter-shaped holes around an OCR'd label will frequently produce a
    centroid that lies right ON a glyph (i.e. a wall pixel), making it useless
    as a flood-fill seed.
    """
    ys, xs = np.nonzero(mask)
    if xs.size == 0:
        return None
    return (int(round(xs.mean())), int(round(ys.mean())))


def pole_of_inaccessibility(mask: np.ndarray) -> Optional[Point]:
    """Pick the interior pixel farthest from any boundary of ``mask``.

    Uses an L2 distance transform and returns the argmax — i.e. the pixel
    that has the most "clearance" from the nearest zero pixel in the mask.
    This is the right point to use as a **flood-fill seed**: it's guaranteed
    to be a foreground pixel (so the fill starts), and being deep in the
    interior it sits as far as possible from text glyphs, door arcs, and
    other near-centroid hazards that would corrupt a naive centroid pick.

    Returns ``None`` if the mask is entirely zero. OpenCV is loaded lazily
    so unit tests that mock out OpenCV continue to work.
    """
    if not mask.any():
        return None
    import cv2

    # distanceTransform expects 8-bit single-channel with foreground != 0,
    # boundary == 0. Our masks are uint8/0-or-255, which already satisfies
    # that contract.
    binary = mask if mask.dtype == np.uint8 else (mask > 0).astype(np.uint8) * 255
    # OpenCV's distanceTransform does NOT treat the image edges as zero
    # pixels — so a foreground pixel sitting in the image corner would
    # report an arbitrarily large distance because no zero pixel is "above"
    # or "left of" it. Pad with a 1-pixel zero border so an image-edge
    # foreground pixel behaves like a true boundary pixel, then shift the
    # result back into the original coordinate space.
    padded = np.pad(binary, 1, mode="constant", constant_values=0)
    dist = cv2.distanceTransform(padded, cv2.DIST_L2, 3)
    py, px = np.unravel_index(int(np.argmax(dist)), dist.shape)
    return (int(px) - 1, int(py) - 1)


def mask_bbox(mask: np.ndarray) -> Optional[BBox]:
    """Tight bounding box of the nonzero pixels in ``mask``, or None if empty."""
    ys, xs = np.nonzero(mask)
    if xs.size == 0:
        return None
    x0, x1 = int(xs.min()), int(xs.max())
    y0, y1 = int(ys.min()), int(ys.max())
    return (x0, y0, x1 - x0 + 1, y1 - y0 + 1)


# ---------------------------------------------------------------------------
# RLE-style mask serialization for ``calibration.json``
# ---------------------------------------------------------------------------
#
# We need to persist per-room binary masks across runs of the tool. PNG would
# work but adds complexity. Instead we serialize the raw mask bytes using:
#
#     1. Booleanize to a single bit per pixel
#     2. Pack 8 bits per byte
#     3. zlib-compress (typical 5-10x savings on floor-plan masks)
#     4. base64-encode for JSON-safety
#
# Plus the (H, W) shape recorded alongside. This is fast (no per-pixel Python
# loop, all numpy + zlib) and round-trips bit-exact.


def mask_to_rle(mask: np.ndarray) -> str:
    """Serialize a binary mask to a base64-zlib-bitpacked string for JSON storage."""
    if mask.ndim != 2:
        raise ValueError(f"mask must be 2-D, got shape={mask.shape}")
    h, w = mask.shape
    bits = (mask > 0).astype(np.uint8)
    packed = np.packbits(bits.flatten())
    compressed = zlib.compress(packed.tobytes(), level=9)
    b64 = base64.b64encode(compressed).decode("ascii")
    return f"{h}x{w}:{b64}"


def rle_to_mask(rle: str) -> np.ndarray:
    """Inverse of ``mask_to_rle``. Returns a uint8 mask with values 0 or 255."""
    if ":" not in rle:
        raise ValueError(f"invalid RLE string: missing ':' delimiter")
    shape_part, b64 = rle.split(":", 1)
    if "x" not in shape_part:
        raise ValueError(f"invalid RLE shape header: {shape_part!r}")
    h_str, w_str = shape_part.split("x", 1)
    h, w = int(h_str), int(w_str)
    compressed = base64.b64decode(b64.encode("ascii"))
    packed = np.frombuffer(zlib.decompress(compressed), dtype=np.uint8)
    bits = np.unpackbits(packed)[: h * w]
    return (bits.reshape(h, w) * 255).astype(np.uint8)


# ---------------------------------------------------------------------------
# Largest inscribed rectangle
# ---------------------------------------------------------------------------


def largest_inscribed_rectangle(
    mask: np.ndarray, *, height_cap: Optional[int] = None
) -> BBox:
    """Return the largest axis-aligned rectangle entirely inside ``mask``.

    Uses the classic O(H*W) algorithm: for each row, maintain per-column
    "consecutive True pixels ending at this row" heights, then compute the
    largest rectangle in that histogram via a monotonic stack. The best
    histogram-rectangle across all rows is the largest inscribed rectangle.

    Args:
        mask: ``numpy`` array of shape ``(H, W)``. Any nonzero value counts
            as "inside". Both ``bool`` and ``uint8`` are accepted.
        height_cap: optional integer. If given, rectangles taller than the
            cap are scored as ``width * height_cap`` (instead of
            ``width * height``), and the returned rectangle's height is
            clipped to the cap. This makes wider-but-shorter rectangles
            competitive against tall narrow strips — useful when the
            rectangle will be used to lay out a fixed amount of text,
            since extra height beyond what the text needs is wasted.
            For rectangles already ≤ the cap, scoring is identical to
            the uncapped variant; ``height_cap=None`` (default) preserves
            the classical "largest area" behavior.

    Returns:
        ``(x, y, w, h)`` of the largest inscribed rectangle. ``(0, 0, 0, 0)``
        if the mask is empty.

    Raises:
        ValueError: if ``mask`` isn't 2-D.
    """
    if mask.ndim != 2:
        raise ValueError(f"mask must be 2-D, got shape={mask.shape}")
    if not mask.any():
        return (0, 0, 0, 0)

    inside = (mask != 0)
    h, w = inside.shape
    heights = np.zeros(w, dtype=np.int32)
    best_area = 0
    best: BBox = (0, 0, 0, 0)

    for y in range(h):
        row = inside[y]
        # Vectorized "consecutive True ending here": increment where inside,
        # reset to 0 where not.
        heights = np.where(row, heights + 1, 0)
        if height_cap is not None:
            scored = np.minimum(heights, height_cap)
        else:
            scored = heights
        area, left, right, height = _largest_rect_in_histogram(scored)
        if area > best_area:
            best_area = area
            # Bottom of the rectangle is row y; top is (y - height + 1).
            # ``height`` here is the capped value (== uncapped when no cap
            # is set), so the returned rect's height is naturally clipped.
            best = (
                int(left),
                int(y - height + 1),
                int(right - left + 1),
                int(height),
            )
    return best


def _largest_rect_in_histogram(
    heights: np.ndarray,
) -> tuple[int, int, int, int]:
    """Largest rectangle in a histogram via a monotonic-increasing stack.

    Returns ``(area, left_col, right_col, height)`` for the best rectangle.
    ``left_col`` and ``right_col`` are inclusive column indices.
    """
    stack: list[tuple[int, int]] = []  # (index, height)
    best_area = 0
    best_left = 0
    best_right = -1
    best_height = 0

    n = len(heights)
    for i in range(n + 1):
        cur_h = 0 if i == n else int(heights[i])
        while stack and stack[-1][1] > cur_h:
            top_i, top_h = stack.pop()
            left = stack[-1][0] + 1 if stack else 0
            right = i - 1
            width = right - left + 1
            area = width * top_h
            if area > best_area:
                best_area = area
                best_left = left
                best_right = right
                best_height = top_h
        stack.append((i, cur_h))

    return best_area, best_left, best_right, best_height


__all__ = [
    "BBox",
    "Point",
    "ConnectedComponent",
    "find_connected_components",
    "bbox_center",
    "bbox_contains_point",
    "bbox_contains_bbox",
    "bbox_intersects",
    "bbox_area",
    "expand_bbox",
    "mask_contains_point",
    "mask_area",
    "mask_centroid",
    "pole_of_inaccessibility",
    "mask_bbox",
    "mask_to_rle",
    "rle_to_mask",
    "largest_inscribed_rectangle",
]
