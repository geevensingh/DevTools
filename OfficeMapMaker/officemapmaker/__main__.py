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
import os
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
    from .manifest import clear_review, write_manifest
    from .review_pdf import build_calibration_review_pdf

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

    # Regenerating the calibration invalidates any prior human-review confirmation.
    clear_review(args.out)
    # Record the input SHA so the next pass can detect a stale calibration.
    write_manifest(args.out, {"map": args.map})

    # Build the human-review PDF next to the calibration so the user can eyeball it.
    review_pdf_path = args.out.with_name(args.out.stem + "_review.pdf")
    try:
        build_calibration_review_pdf(args.map, cal, review_pdf_path)
        print(f"wrote {review_pdf_path}")
    except Exception as exc:  # noqa: BLE001 — review PDF is best-effort
        print(f"warning: failed to write review PDF ({exc})", file=sys.stderr)

    errors = [i for i in issues if i.severity == "error"]
    warnings = [i for i in issues if i.severity == "warning"]
    for issue in issues:
        stream = sys.stderr if issue.severity == "error" else sys.stdout
        print(str(issue), file=stream)

    print(f"summary: {len(errors)} error(s), {len(warnings)} warning(s)")
    print(
        f"next: review {review_pdf_path} then run "
        f"'officemapmaker calibrate confirm --calibration {args.out}'"
    )
    return 1 if errors else 0


def _run_calibrate_review(args: argparse.Namespace) -> int:
    """Open calibration_review.pdf in the platform's default viewer."""
    cal_path: Path = args.calibration
    review_pdf = cal_path.with_name(cal_path.stem + "_review.pdf")
    if not review_pdf.exists():
        print(
            f"error: review PDF not found at {review_pdf}; "
            f"run 'officemapmaker calibrate --map MAP.png --out {cal_path}' first",
            file=sys.stderr,
        )
        return 2

    try:
        # os.startfile is Windows-only but launches the registered handler.
        os.startfile(str(review_pdf))  # type: ignore[attr-defined]
        print(f"opened {review_pdf}")
        return 0
    except AttributeError:
        # Non-Windows fallback: print the path so the user can open it manually.
        print(f"open this file in your PDF viewer: {review_pdf}")
        return 0
    except OSError as exc:
        print(f"error: could not open {review_pdf}: {exc}", file=sys.stderr)
        return 2


def _run_calibrate_confirm(args: argparse.Namespace) -> int:
    """Write the .reviewed sentinel next to calibration.json."""
    from .manifest import confirm_review

    cal_path: Path = args.calibration
    if not cal_path.exists():
        print(f"error: calibration not found at {cal_path}", file=sys.stderr)
        return 2
    sentinel = confirm_review(cal_path)
    print(f"wrote {sentinel}")
    return 0


def _run_validate_labels(args: argparse.Namespace) -> int:
    """Execute Pass 1 (validate labels against assignments)."""
    from .calibration import load_calibration
    from .io_assignments import AssignmentLoadError, load_assignments
    from .validate import render_validation_labels_review_png, validate_labels

    cal_path: Path = args.calibration
    asn_path: Path = args.assignments

    if not cal_path.exists():
        print(f"error: calibration not found at {cal_path}", file=sys.stderr)
        return 2
    if not asn_path.exists():
        print(f"error: assignments file not found at {asn_path}", file=sys.stderr)
        return 2

    try:
        cal = load_calibration(cal_path)
    except Exception as exc:  # noqa: BLE001
        print(f"error: could not load calibration: {exc}", file=sys.stderr)
        return 2

    try:
        assignments = load_assignments(asn_path)
    except AssignmentLoadError as exc:
        print(f"error: could not load assignments: {exc}", file=sys.stderr)
        return 2

    issues = validate_labels(cal, assignments)
    errors = [i for i in issues if i.severity == "error"]
    warnings = [i for i in issues if i.severity == "warning"]

    review_png = cal_path.with_name(cal_path.stem + "_validation_labels_review.png")
    map_path = cal_path.with_name(cal.map_image)
    if map_path.exists():
        try:
            render_validation_labels_review_png(map_path, cal, issues, review_png)
            print(f"wrote {review_png}")
        except Exception as exc:  # noqa: BLE001 — review PNG is best-effort
            print(f"warning: failed to write review PNG ({exc})", file=sys.stderr)
    else:
        print(
            f"warning: map image {map_path} not found next to calibration; "
            "skipping validation review PNG",
            file=sys.stderr,
        )

    for issue in issues:
        stream = sys.stderr if issue.severity == "error" else sys.stdout
        print(str(issue), file=stream)
    print(
        f"summary: {len(assignments)} assignment(s), "
        f"{len(errors)} error(s), {len(warnings)} warning(s)"
    )
    return 1 if errors else 0


def dispatch(args: argparse.Namespace) -> int:
    cmd = args.cmd

    if cmd == "calibrate":
        action = getattr(args, "cal_action", None)
        if action == "review":
            return _run_calibrate_review(args)
        if action == "confirm":
            return _run_calibrate_confirm(args)
        if args.map is None:
            print("error: --map is required when running calibration", file=sys.stderr)
            return 2
        return _run_calibrate(args)

    if cmd == "validate":
        action = args.val_action  # required
        if action == "labels":
            return _run_validate_labels(args)
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
