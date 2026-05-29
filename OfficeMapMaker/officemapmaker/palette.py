"""Team color palette generator with WCAG-AAA contrast guarantee.

This module owns:

  * Auto-assignment of distinct, contrast-safe colors to an arbitrary set
    of team names.
  * Hex / RGB color parsing for the optional ``teams.json`` override file.
  * Contrast-ratio computation against black text (must be >= 7.0 for
    WCAG AAA; the renderer warns if any user-supplied override fails).

Design choices:

  * Auto palette uses HSL L=0.82, S=0.45 with hues evenly spaced over the
    full circle. Those L/S values are documented in plan.md §9 — they
    guarantee >= 7:1 contrast against black for every hue, while still
    being chromatic enough to distinguish ~12+ teams at a glance.
  * Team ordering is deterministic: sorted by team name (case-insensitive).
    This makes the palette stable across runs even when assignments are
    reordered in the spreadsheet.
  * Hue assignment is interleaved (golden-ratio spacing) so the *first*
    N teams in alphabetical order land in visually-distinct hues, instead
    of e.g. all the A-team and B-team being nearly the same red.
"""

from __future__ import annotations

import colorsys
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable, Mapping, Optional


__all__ = [
    "RGB",
    "TeamPalette",
    "auto_palette",
    "build_palette",
    "contrast_ratio",
    "load_team_overrides",
    "parse_hex_color",
]


RGB = tuple[int, int, int]  # 0..255 per channel; same convention as PIL


# ---------------------------------------------------------------------------
# Contrast (WCAG)
# ---------------------------------------------------------------------------


def _srgb_to_linear(c: float) -> float:
    """sRGB component (0..1) -> linear-light (0..1). Per WCAG 2.x."""
    if c <= 0.03928:
        return c / 12.92
    return ((c + 0.055) / 1.055) ** 2.4


def _relative_luminance(rgb: RGB) -> float:
    r, g, b = (v / 255.0 for v in rgb)
    return (
        0.2126 * _srgb_to_linear(r)
        + 0.7152 * _srgb_to_linear(g)
        + 0.0722 * _srgb_to_linear(b)
    )


def contrast_ratio(fg: RGB, bg: RGB) -> float:
    """Compute the WCAG contrast ratio between two RGB colors.

    Returns a value in ``[1.0, 21.0]``. WCAG AAA for normal text requires
    >= 7.0; AA requires >= 4.5.
    """
    l1, l2 = _relative_luminance(fg), _relative_luminance(bg)
    lighter, darker = max(l1, l2), min(l1, l2)
    return (lighter + 0.05) / (darker + 0.05)


# ---------------------------------------------------------------------------
# Hex parsing
# ---------------------------------------------------------------------------


def parse_hex_color(text: str) -> RGB:
    """Parse ``#RRGGBB`` or ``#RGB`` (3 or 6 hex chars, optional ``#``).

    Raises ``ValueError`` for any other format.
    """
    if not isinstance(text, str):
        raise ValueError(f"hex color must be a string, got {type(text).__name__}")
    s = text.strip()
    if s.startswith("#"):
        s = s[1:]
    if len(s) == 3:
        s = "".join(c * 2 for c in s)
    if len(s) != 6:
        raise ValueError(f"hex color must be 3 or 6 hex digits, got {text!r}")
    try:
        r = int(s[0:2], 16)
        g = int(s[2:4], 16)
        b = int(s[4:6], 16)
    except ValueError as exc:
        raise ValueError(f"invalid hex color {text!r}: {exc}") from exc
    return (r, g, b)


def rgb_to_hex(rgb: RGB) -> str:
    r, g, b = rgb
    return f"#{r:02X}{g:02X}{b:02X}"


# ---------------------------------------------------------------------------
# Auto palette
# ---------------------------------------------------------------------------


_GOLDEN_RATIO_CONJUGATE = 0.61803398875


def _hsl_to_rgb(h: float, s: float, l: float) -> RGB:
    """``h``, ``s``, ``l`` in [0, 1]; returns RGB in [0, 255]."""
    # colorsys.hls_to_rgb takes (h, l, s) — order swap on purpose.
    r, g, b = colorsys.hls_to_rgb(h, l, s)
    return (int(round(r * 255)), int(round(g * 255)), int(round(b * 255)))


def auto_palette(
    n: int,
    *,
    lightness: float = 0.82,
    saturation: float = 0.45,
    hue_offset: float = 0.0,
) -> list[RGB]:
    """Generate ``n`` visually-distinct, contrast-safe colors.

    Hues are interleaved by the golden-ratio conjugate so the *first* N
    colors are well-separated for any N (not just N = a fixed grid).

    All returned colors are guaranteed >= 7:1 contrast vs black at the
    default L=0.82 S=0.45. Users who override these values should call
    ``contrast_ratio(color, (0, 0, 0)) >= 7`` to verify their choices.
    """
    if n < 0:
        raise ValueError(f"n must be >= 0, got {n}")
    if not (0.0 < lightness < 1.0):
        raise ValueError(f"lightness must be in (0, 1), got {lightness}")
    if not (0.0 <= saturation <= 1.0):
        raise ValueError(f"saturation must be in [0, 1], got {saturation}")

    colors: list[RGB] = []
    h = hue_offset % 1.0
    for _ in range(n):
        colors.append(_hsl_to_rgb(h, saturation, lightness))
        h = (h + _GOLDEN_RATIO_CONJUGATE) % 1.0
    return colors


# ---------------------------------------------------------------------------
# Palette dataclass
# ---------------------------------------------------------------------------


@dataclass(frozen=True)
class TeamPalette:
    """Mapping from team name -> RGB color.

    Attributes:
        colors: Dict keyed by exact team name as it appears in assignments.
        overrides_used: Subset of ``colors`` whose value came from a user
            override file (used by the renderer to warn about poor-contrast
            overrides without nagging about auto-generated ones).
        low_contrast: Team names whose color has <7:1 contrast vs black.
            Always empty for auto-only palettes; only populated when
            overrides include dark colors.
    """

    colors: dict[str, RGB]
    overrides_used: frozenset[str] = frozenset()
    low_contrast: frozenset[str] = frozenset()

    def color_for(self, team: str) -> Optional[RGB]:
        return self.colors.get(team)

    def __len__(self) -> int:  # pragma: no cover - trivial
        return len(self.colors)


def load_team_overrides(path: Path | str) -> dict[str, RGB]:
    """Load ``teams.json`` overrides as ``{team_name: RGB}``.

    The file's JSON values may be ``#RRGGBB`` strings or 3-element lists
    of integers; either is accepted. Missing file -> ``{}`` (a no-op).
    """
    p = Path(path)
    if not p.exists():
        return {}
    with p.open("r", encoding="utf-8") as f:
        raw = json.load(f)
    if not isinstance(raw, dict):
        raise ValueError(
            f"{p}: expected an object mapping team names to colors, "
            f"got {type(raw).__name__}"
        )
    out: dict[str, RGB] = {}
    for team, value in raw.items():
        if isinstance(value, str):
            out[str(team)] = parse_hex_color(value)
        elif isinstance(value, (list, tuple)) and len(value) == 3:
            r, g, b = (int(v) for v in value)
            if not all(0 <= c <= 255 for c in (r, g, b)):
                raise ValueError(
                    f"{p}: color for team {team!r} has components out of 0..255"
                )
            out[str(team)] = (r, g, b)
        else:
            raise ValueError(
                f"{p}: color for team {team!r} must be a hex string or "
                f"[r, g, b] list; got {value!r}"
            )
    return out


def build_palette(
    teams: Iterable[str],
    overrides: Optional[Mapping[str, RGB]] = None,
    *,
    lightness: float = 0.82,
    saturation: float = 0.45,
) -> TeamPalette:
    """Produce a final ``TeamPalette`` for the given set of team names.

    Args:
        teams: Iterable of team names (duplicates collapsed).
        overrides: Optional mapping from team name -> RGB. Takes precedence
            over auto-assigned colors. Matching is case-sensitive (matches
            the spreadsheet's team strings exactly).
        lightness, saturation: Forwarded to ``auto_palette`` for the
            non-overridden teams.

    Returns:
        ``TeamPalette`` with deterministic auto-color assignment for all
        teams not present in ``overrides``.
    """
    unique = sorted({t for t in teams if t}, key=str.casefold)
    overrides_map: dict[str, RGB] = dict(overrides or {})

    auto_targets = [t for t in unique if t not in overrides_map]
    auto_colors = auto_palette(
        len(auto_targets), lightness=lightness, saturation=saturation
    )
    final: dict[str, RGB] = {}
    for t, c in zip(auto_targets, auto_colors):
        final[t] = c
    for t, c in overrides_map.items():
        final[t] = c

    low = frozenset(
        t for t, c in final.items() if contrast_ratio(c, (0, 0, 0)) < 7.0
    )
    return TeamPalette(
        colors=final,
        overrides_used=frozenset(t for t in unique if t in overrides_map),
        low_contrast=low,
    )
