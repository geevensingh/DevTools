"""CLI entry point: ``py -m officemapmaker``.

This module wires up argparse for every subcommand the README documents and
dispatches to pass-specific modules. Each pass module is intentionally a stub
right now — implementation is tracked in plan.md milestones 2 through 9.

Subcommand grammar (see README §"Quick reference"):

    officemapmaker calibrate          --map MAP.png  [--out calibration.json]
    officemapmaker calibrate review   --calibration calibration.json
    officemapmaker calibrate confirm  --calibration calibration.json
    officemapmaker validate labels    --calibration calibration.json --assignments PEOPLE
    officemapmaker validate fill      --map MAP.png  --calibration calibration.json
    officemapmaker layout             --map MAP.png  --calibration calibration.json --assignments PEOPLE
    officemapmaker layout confirm     --layout layout.json
    officemapmaker build              --map MAP.png  --calibration calibration.json
                                      --assignments PEOPLE --layout layout.json
                                      [--teams teams.json] [--out composite.png]
    officemapmaker build confirm      --composite composite.png
    officemapmaker tile               --composite composite.png [--out tiles\\]
                                      [--dpi 150] [--paper letter] [--overlap-in 0.25]
    officemapmaker all                --map MAP.png  --assignments PEOPLE  [--out-dir output\\]

Common flags on every subcommand: ``--auto`` (skip review gates), ``--force``
(ignore stale-input SHA mismatches).
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path


# ----------------------------------------------------------------------------
# Argparse construction
# ----------------------------------------------------------------------------

def _add_common_flags(p: argparse.ArgumentParser) -> None:
    p.add_argument(
        "--auto",
        action="store_true",
        help="Skip human-review gates (rebuild-only; trusts prior reviews).",
    )
    p.add_argument(
        "--force",
        action="store_true",
        help="Ignore stale-input SHA mismatches (dangerous; logged loudly).",
    )


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="officemapmaker",
        description="Build a colored, labelled office floor map from a map image and an assignments spreadsheet.",
    )
    subs = parser.add_subparsers(dest="cmd", required=True, metavar="COMMAND")

    # ---- calibrate (+ review / confirm) ------------------------------------
    p_cal = subs.add_parser(
        "calibrate",
        help="Run OCR + connected-components and write calibration.json (or open/confirm its review).",
    )
    # When no sub-sub is given, p_cal runs the calibration action.
    p_cal.add_argument("--map", type=Path, help="Path to map image (required when running calibration).")
    p_cal.add_argument(
        "--out", type=Path, default=Path("calibration.json"),
        help="Where to write calibration.json (default: ./calibration.json).",
    )
    _add_common_flags(p_cal)
    cal_subs = p_cal.add_subparsers(dest="cal_action", required=False, metavar="ACTION")

    p_cal_review = cal_subs.add_parser(
        "review", help="Open calibration_review.pdf in the default viewer."
    )
    p_cal_review.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    _add_common_flags(p_cal_review)

    p_cal_confirm = cal_subs.add_parser(
        "confirm", help="Write the .reviewed sentinel for calibration.json."
    )
    p_cal_confirm.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    _add_common_flags(p_cal_confirm)

    # ---- validate { labels | fill } ----------------------------------------
    p_val = subs.add_parser(
        "validate",
        help="Validate calibration against the spreadsheet (labels) or detect flood-fill leaks (fill).",
    )
    val_subs = p_val.add_subparsers(dest="val_action", required=True, metavar="ACTION")

    p_val_labels = val_subs.add_parser("labels", help="Cross-check spreadsheet IDs against calibration.")
    p_val_labels.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    p_val_labels.add_argument("--assignments", type=Path, required=True)
    _add_common_flags(p_val_labels)

    p_val_fill = val_subs.add_parser("fill", help="Virtual flood-fill every office and report leaks.")
    p_val_fill.add_argument("--map", type=Path, required=True)
    p_val_fill.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    _add_common_flags(p_val_fill)

    # ---- layout (+ confirm) -------------------------------------------------
    p_layout = subs.add_parser(
        "layout",
        help="Plan name placement per office and write layout.json (or confirm its review).",
    )
    p_layout.add_argument("--map", type=Path)
    p_layout.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    p_layout.add_argument("--assignments", type=Path)
    p_layout.add_argument("--out", type=Path, default=Path("layout.json"))
    _add_common_flags(p_layout)
    layout_subs = p_layout.add_subparsers(dest="layout_action", required=False, metavar="ACTION")

    p_layout_confirm = layout_subs.add_parser(
        "confirm", help="Write the .reviewed sentinel for layout.json."
    )
    p_layout_confirm.add_argument("--layout", type=Path, default=Path("layout.json"))
    _add_common_flags(p_layout_confirm)

    # ---- build (+ confirm) --------------------------------------------------
    p_build = subs.add_parser(
        "build",
        help="Render the colored composite.png (or confirm its review).",
    )
    p_build.add_argument("--map", type=Path)
    p_build.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    p_build.add_argument("--assignments", type=Path)
    p_build.add_argument("--layout", type=Path, default=Path("layout.json"))
    p_build.add_argument("--teams", type=Path, help="Optional team-color overrides (teams.json).")
    p_build.add_argument("--out", type=Path, default=Path("composite.png"))
    _add_common_flags(p_build)
    build_subs = p_build.add_subparsers(dest="build_action", required=False, metavar="ACTION")

    p_build_confirm = build_subs.add_parser(
        "confirm", help="Write the .reviewed sentinel for composite.png."
    )
    p_build_confirm.add_argument("--composite", type=Path, default=Path("composite.png"))
    _add_common_flags(p_build_confirm)

    # ---- tile ---------------------------------------------------------------
    p_tile = subs.add_parser(
        "tile",
        help="Tile composite.png into letter-size pages and bundle into all.pdf.",
    )
    p_tile.add_argument("--composite", type=Path, default=Path("composite.png"))
    p_tile.add_argument("--out", type=Path, default=Path("tiles"))
    p_tile.add_argument("--dpi", type=int, default=150)
    p_tile.add_argument("--paper", choices=["letter", "a4"], default="letter")
    p_tile.add_argument("--overlap-in", type=float, default=0.25)
    _add_common_flags(p_tile)

    # ---- all ----------------------------------------------------------------
    p_all = subs.add_parser(
        "all",
        help="Run every pass end-to-end (halts at each review gate unless --auto).",
    )
    p_all.add_argument("--map", type=Path, required=True)
    p_all.add_argument("--assignments", type=Path, required=True)
    p_all.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    p_all.add_argument("--layout", type=Path, default=Path("layout.json"))
    p_all.add_argument("--teams", type=Path)
    p_all.add_argument("--out-dir", type=Path, default=Path("output"))
    _add_common_flags(p_all)

    return parser


# ----------------------------------------------------------------------------
# Dispatch
# ----------------------------------------------------------------------------

def _not_implemented(name: str) -> int:
    print(
        f"[officemapmaker] '{name}' is not implemented yet — see plan.md milestones.",
        file=sys.stderr,
    )
    return 2


def _run_calibrate(args: argparse.Namespace) -> int:
    """Execute Pass 0 (calibrate) and write calibration.json + report issues."""
    from .calibrate import TesseractNotFoundError, calibrate_map
    from .calibration import save_calibration

    try:
        cal, issues = calibrate_map(args.map)
    except TesseractNotFoundError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 3
    except FileNotFoundError as exc:
        print(f"error: map not found: {exc}", file=sys.stderr)
        return 2

    save_calibration(cal, args.out)
    print(f"wrote {args.out} ({len(cal.labels)} labels, {len(cal.rooms)} rooms)")

    errors = [i for i in issues if i.severity == "error"]
    warnings = [i for i in issues if i.severity == "warning"]
    for issue in issues:
        stream = sys.stderr if issue.severity == "error" else sys.stdout
        print(str(issue), file=stream)

    print(f"summary: {len(errors)} error(s), {len(warnings)} warning(s)")
    return 1 if errors else 0


def dispatch(args: argparse.Namespace) -> int:
    cmd = args.cmd

    if cmd == "calibrate":
        action = getattr(args, "cal_action", None)
        if action == "review":
            return _not_implemented("calibrate review")
        if action == "confirm":
            return _not_implemented("calibrate confirm")
        if args.map is None:
            print("error: --map is required when running calibration", file=sys.stderr)
            return 2
        return _run_calibrate(args)

    if cmd == "validate":
        action = args.val_action  # required
        if action == "labels":
            return _not_implemented("validate labels")
        if action == "fill":
            return _not_implemented("validate fill")
        # argparse guarantees we don't reach here, but be defensive.
        return _not_implemented(f"validate {action}")

    if cmd == "layout":
        action = getattr(args, "layout_action", None)
        if action == "confirm":
            return _not_implemented("layout confirm")
        if args.map is None or args.assignments is None:
            print("error: --map and --assignments are required when running layout", file=sys.stderr)
            return 2
        return _not_implemented("layout")

    if cmd == "build":
        action = getattr(args, "build_action", None)
        if action == "confirm":
            return _not_implemented("build confirm")
        if args.map is None or args.assignments is None:
            print("error: --map and --assignments are required when running build", file=sys.stderr)
            return 2
        return _not_implemented("build")

    if cmd == "tile":
        return _not_implemented("tile")

    if cmd == "all":
        return _not_implemented("all")

    print(f"error: unknown command '{cmd}'", file=sys.stderr)
    return 2


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    return dispatch(args)


if __name__ == "__main__":
    sys.exit(main())
