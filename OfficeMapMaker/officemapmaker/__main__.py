"""CLI entry point: ``py -m officemapmaker``.

This module wires up argparse for every subcommand the README documents and
dispatches to pass-specific modules. Each pass module is intentionally a stub
right now — implementation is tracked in plan.md milestones 2 through 9.

Subcommand grammar (see README §"Quick reference"):

    officemapmaker calibrate          --map MAP.png  [--out calibration.json] [--edit]
    officemapmaker calibrate review   --calibration calibration.json
    officemapmaker calibrate confirm  --calibration calibration.json
    officemapmaker calibrate edit     --calibration calibration.json
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
    p_cal.add_argument(
        "--edit",
        action="store_true",
        help=(
            "After OCR + connected-components, immediately launch the "
            "interactive editor on the new calibration. Recommended one-shot "
            "flow for new maps: 'OfficeMapMaker calibrate --map MAP.png --edit'."
        ),
    )
    _add_common_flags(p_cal)
    cal_subs = p_cal.add_subparsers(dest="cal_action", required=False, metavar="ACTION")

    p_cal_review = cal_subs.add_parser(
        "review", help="Build (if missing or stale) and open calibration_review.pdf."
    )
    p_cal_review.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    p_cal_review.add_argument(
        "--map",
        type=Path,
        default=None,
        help=(
            "Optional. Path to the source map. Defaults to the 'map_image' "
            "field recorded in the calibration, resolved relative to the "
            "calibration file."
        ),
    )
    _add_common_flags(p_cal_review)

    p_cal_confirm = cal_subs.add_parser(
        "confirm", help="Write the .reviewed sentinel for calibration.json."
    )
    p_cal_confirm.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    _add_common_flags(p_cal_confirm)

    p_cal_edit = cal_subs.add_parser(
        "edit",
        help=(
            "Open the interactive calibration editor "
            "(PySide6 desktop app — primary way to fix OCR misreads, "
            "missing labels, misclassified rooms, etc.)."
        ),
    )
    p_cal_edit.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    p_cal_edit.add_argument(
        "--map",
        type=Path,
        default=None,
        help=(
            "Optional. Path to the source map. Defaults to the 'map_image' "
            "field recorded in the calibration, resolved relative to the "
            "calibration file."
        ),
    )
    _add_common_flags(p_cal_edit)

    p_cal_fix_seeds = cal_subs.add_parser(
        "fix-seeds",
        help=(
            "Heal calibration.json: re-pick fill_seed for any label whose "
            "current seed lands on a wall or yields a near-empty flood-fill. "
            "Use after a 'validate fill' run with many 'seed_on_wall' warnings."
        ),
    )
    p_cal_fix_seeds.add_argument("--calibration", type=Path, default=Path("calibration.json"))
    p_cal_fix_seeds.add_argument(
        "--map",
        type=Path,
        default=None,
        help=(
            "Optional. Path to the source map. Defaults to the 'map_image' "
            "field recorded in the calibration, resolved relative to the "
            "calibration file."
        ),
    )
    p_cal_fix_seeds.add_argument(
        "--dry-run",
        action="store_true",
        help="Print what would change, but don't modify calibration.json.",
    )
    p_cal_fix_seeds.add_argument(
        "--threshold",
        type=float,
        default=0.30,
        help=(
            "Consider a seed 'broken' if its flood-fill produces fewer pixels "
            "than this fraction of the room's polygon area (default: 0.30)."
        ),
    )
    _add_common_flags(p_cal_fix_seeds)

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
    if getattr(args, "edit", False):
        # One-shot flow: OCR → editor. The editor returns 0 on clean close.
        # We preserve exit-code 1 if the calibration itself had errors so
        # callers / CI can still detect that, but only after the editor closes.
        from .editor import launch as _launch_editor

        print(f"launching editor on {args.out}")
        editor_rc = _launch_editor(args.out, args.map)
        if editor_rc != 0:
            return editor_rc
        return 1 if errors else 0

    print(
        f"next: review {review_pdf_path} then run "
        f"'officemapmaker calibrate confirm --calibration {args.out}'"
    )
    return 1 if errors else 0


def _run_calibrate_review(args: argparse.Namespace) -> int:
    """Build calibration_review.pdf if missing or stale, then open it.

    Rebuilds when:
      - The PDF does not exist.
      - The PDF is older than the calibration file (the user hand-edited
        ``calibration.json`` to fix OCR misreads, room links, etc.).

    A rebuild also clears any prior ``.reviewed`` sentinel so the user has to
    re-confirm after eyeballing the new PDF — hand-edits invalidate prior
    confirmation by design.

    The source map is resolved from (in order):
      1. ``--map`` if provided.
      2. The ``map_image`` field inside ``calibration.json`` (relative to
         the calibration file's directory).
    """
    from .calibration import CalibrationFormatError, load_calibration
    from .manifest import clear_review
    from .review_pdf import build_calibration_review_pdf

    cal_path: Path = args.calibration
    if not cal_path.exists():
        print(
            f"error: calibration not found at {cal_path}; "
            f"run 'officemapmaker calibrate --map MAP.png --out {cal_path}' first",
            file=sys.stderr,
        )
        return 2

    review_pdf = cal_path.with_name(cal_path.stem + "_review.pdf")
    needs_rebuild = (
        not review_pdf.exists()
        or review_pdf.stat().st_mtime < cal_path.stat().st_mtime
    )

    if needs_rebuild:
        try:
            cal = load_calibration(cal_path)
        except CalibrationFormatError as exc:
            print(f"error: could not load calibration: {exc}", file=sys.stderr)
            return 2

        if args.map is not None:
            map_path = args.map
        else:
            map_path = (cal_path.parent / cal.map_image).resolve()

        if not map_path.exists():
            print(
                f"error: source map not found at {map_path}.\n"
                f"Pass --map to point at the map explicitly, or move the map "
                f"next to {cal_path.name} so the recorded 'map_image' "
                f"({cal.map_image!r}) resolves.",
                file=sys.stderr,
            )
            return 2

        try:
            build_calibration_review_pdf(map_path, cal, review_pdf)
        except Exception as exc:  # noqa: BLE001 — surface any build failure
            print(f"error: failed to build review PDF: {exc}", file=sys.stderr)
            return 2

        clear_review(cal_path)
        print(f"rebuilt {review_pdf} from {cal_path}")

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


def _run_calibrate_edit(args: argparse.Namespace) -> int:
    """Launch the interactive PySide6 calibration editor."""
    try:
        from .editor import launch
    except ImportError as exc:
        print(
            f"error: the editor requires PySide6 but it could not be imported ({exc}).\n"
            f"Install it with: py -m pip install -r requirements.txt",
            file=sys.stderr,
        )
        return 2
    return launch(args.calibration, args.map)


def _run_calibrate_fix_seeds(args: argparse.Namespace) -> int:
    """Heal fill_seed values that land on walls.

    Strategy:
      1. Load calibration + binarize map (same wall mask validate uses).
      2. For each label with a room_id, virtual-flood-fill from the current
         seed. If the fill covers less than ``--threshold`` (default 30%) of
         the room's polygon area, the seed is "broken".
      3. For each broken seed, recompute via
         :func:`pole_of_inaccessibility` on (room polygon mask AND interior).
         This guarantees the seed lands on a true interior pixel inside the
         room polygon — even for L-shaped rooms or rooms whose polygon was
         hand-drawn.
      4. Verify the new seed fills above the threshold; only then accept it.
      5. Write calibration.json with a ``.bak`` backup if anything changed.

    Returns 0 on clean success (including "nothing to heal"), 2 on I/O errors.
    """
    from .calibration import CalibrationFormatError, load_calibration, save_calibration
    from .geometry import pole_of_inaccessibility, rle_to_mask
    from .validate import build_fill_mask, virtual_flood_fill

    cal_path: Path = args.calibration

    if not cal_path.exists():
        print(f"error: calibration not found at {cal_path}", file=sys.stderr)
        return 2

    try:
        cal = load_calibration(cal_path)
    except CalibrationFormatError as exc:
        print(f"error: could not load calibration: {exc}", file=sys.stderr)
        return 2

    if args.map is not None:
        map_path = args.map
    else:
        map_path = (cal_path.parent / cal.map_image).resolve()

    if not map_path.exists():
        print(
            f"error: source map not found at {map_path}.\n"
            f"Pass --map to point at the map explicitly, or move the map "
            f"next to {cal_path.name}.",
            file=sys.stderr,
        )
        return 2

    import cv2  # noqa: WPS433 — lazy import keeps non-CV CLI paths cheap
    import numpy as np

    image = cv2.imread(str(map_path), cv2.IMREAD_COLOR)
    if image is None:
        print(f"error: could not decode map image: {map_path}", file=sys.stderr)
        return 2
    wall_mask = build_fill_mask(image, cal.wall_patches)

    threshold: float = float(args.threshold)
    dry_run: bool = bool(args.dry_run)
    rooms_by_id = {r.id: r for r in cal.rooms}

    healed = 0
    unchanged = 0
    skipped_no_room = 0
    skipped_no_polygon = 0
    still_broken = 0

    for label in cal.labels:
        if label.room_id is None:
            skipped_no_room += 1
            continue
        room = rooms_by_id.get(label.room_id)
        if room is None:
            skipped_no_room += 1
            continue

        filled = virtual_flood_fill(wall_mask, label.fill_seed)
        filled_area = int(filled.sum())
        if filled_area >= threshold * room.area_px:
            unchanged += 1
            continue

        try:
            room_mask = rle_to_mask(room.polygon_rle)
        except Exception as exc:  # noqa: BLE001
            print(
                f"warning: could not decode polygon for room {room.id} "
                f"(label {label.id!r}): {exc}",
                file=sys.stderr,
            )
            skipped_no_polygon += 1
            continue

        # Search for the deepest interior pixel that's BOTH inside the room's
        # recorded polygon AND on a non-wall pixel. Without the AND, the pole
        # could land in a glyph-shaped hole inside the polygon (the same bug
        # mask_centroid had).
        if room_mask.shape != wall_mask.shape:
            print(
                f"warning: polygon for room {room.id} (label {label.id!r}) "
                f"has shape {room_mask.shape}, but map is {wall_mask.shape}; "
                f"skipping (calibration is stale relative to the map)",
                file=sys.stderr,
            )
            skipped_no_polygon += 1
            continue

        polygon_interior = (
            ((room_mask > 0) & (wall_mask == 0)).astype(np.uint8) * 255
        )
        new_seed = pole_of_inaccessibility(polygon_interior)
        if new_seed is None:
            print(
                f"warning: room {room.id} (label {label.id!r}) has no interior "
                f"pixels left after masking with walls; calibration may need a "
                f"manual fix in the editor",
                file=sys.stderr,
            )
            still_broken += 1
            continue

        new_filled = virtual_flood_fill(wall_mask, new_seed)
        new_area = int(new_filled.sum())
        if new_area < threshold * room.area_px:
            print(
                f"warning: label {label.id!r}: new seed at {new_seed} still "
                f"fills only {new_area} px (< {threshold:.0%} of {room.area_px}); "
                f"leaving the old seed in place — fix manually in the editor",
                file=sys.stderr,
            )
            still_broken += 1
            continue

        action = "would heal" if dry_run else "healed"
        print(
            f"{action} {label.id!r}: "
            f"{tuple(label.fill_seed)} -> {tuple(new_seed)} "
            f"(fill {filled_area:,} -> {new_area:,} px / room {room.area_px:,} px)"
        )
        if not dry_run:
            label.fill_seed = new_seed
        healed += 1

    if healed > 0 and not dry_run:
        bak_path = cal_path.with_name(cal_path.name + ".bak")
        bak_path.write_bytes(cal_path.read_bytes())
        save_calibration(cal, cal_path)
        print(f"wrote {cal_path} (backup at {bak_path.name})")
    elif healed > 0 and dry_run:
        print(f"(dry-run: would write {cal_path} with {healed} updated seed(s))")
    else:
        print(f"no changes made")

    print(
        f"summary: {healed} healed, {unchanged} already-good, "
        f"{still_broken} still-broken, {skipped_no_room} orphan/missing-room, "
        f"{skipped_no_polygon} bad-polygon"
    )
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


def _run_validate_fill(args: argparse.Namespace) -> int:
    """Execute Pass 2 (virtual flood-fill leak detection)."""
    from .calibration import load_calibration
    from .validate import (
        render_leak_overlay_png,
        render_rooms_overview_png,
        validate_fill,
    )

    cal_path: Path = args.calibration
    map_path: Path = args.map

    if not cal_path.exists():
        print(f"error: calibration not found at {cal_path}", file=sys.stderr)
        return 2
    if not map_path.exists():
        print(f"error: map not found at {map_path}", file=sys.stderr)
        return 2

    try:
        cal = load_calibration(cal_path)
    except Exception as exc:  # noqa: BLE001
        print(f"error: could not load calibration: {exc}", file=sys.stderr)
        return 2

    try:
        leaks = validate_fill(map_path, cal)
    except (FileNotFoundError, ValueError) as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2

    errors = [l for l in leaks if l.severity == "error"]
    warnings = [l for l in leaks if l.severity == "warning"]

    # Per-leak overlay PNGs in <calibration-stem>_leaks\ next to the calibration.
    leaks_dir = cal_path.with_name(cal_path.stem + "_leaks")
    if leaks:
        leaks_dir.mkdir(parents=True, exist_ok=True)
        for leak in leaks:
            png = leaks_dir / f"room-{leak.office_id}-{leak.code}.png"
            try:
                render_leak_overlay_png(map_path, cal, leak, png)
            except Exception as exc:  # noqa: BLE001
                print(
                    f"warning: failed to render {png.name}: {exc}",
                    file=sys.stderr,
                )
        print(f"wrote {len(leaks)} leak overlay(s) to {leaks_dir}")

    # Rooms overview is always useful; render it whether or not leaks were found.
    overview_png = cal_path.with_name(cal_path.stem + "_rooms_overview.png")
    try:
        render_rooms_overview_png(map_path, cal, overview_png)
        print(f"wrote {overview_png}")
    except Exception as exc:  # noqa: BLE001
        print(f"warning: failed to render rooms overview: {exc}", file=sys.stderr)

    for leak in leaks:
        stream = sys.stderr if leak.severity == "error" else sys.stdout
        print(str(leak), file=stream)
        if leak.suggested_patch:
            x1, y1, x2, y2 = leak.suggested_patch
            print(
                f"  suggested wall_patches entry: [{x1}, {y1}, {x2}, {y2}]",
                file=stream,
            )

    labeled_count = sum(1 for lab in cal.labels if lab.room_id is not None)
    print(
        f"summary: {labeled_count} labeled room(s), "
        f"{len(errors)} error(s), {len(warnings)} warning(s)"
    )
    return 1 if errors else 0


def _run_layout(args: argparse.Namespace) -> int:
    """Execute Pass 3 (plan name layout)."""
    from .calibration import compute_map_hash, load_calibration
    from .io_assignments import AssignmentLoadError, load_assignments
    from .layout import (
        plan_layout,
        render_layout_problems_png,
        render_layout_review_png,
        save_layout,
    )
    from .manifest import clear_review, write_manifest

    cal_path: Path = args.calibration
    map_path: Path = args.map
    asn_path: Path = args.assignments
    out_path: Path = args.out

    for label, path in (("calibration", cal_path), ("map", map_path), ("assignments", asn_path)):
        if not path.exists():
            print(f"error: {label} not found at {path}", file=sys.stderr)
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

    layout, issues = plan_layout(
        cal, assignments, map_hash=compute_map_hash(map_path)
    )
    save_layout(layout, out_path)
    print(
        f"wrote {out_path} ({len(layout.entries)} office entr"
        f"{'y' if len(layout.entries) == 1 else 'ies'})"
    )

    # Regenerating layout invalidates any prior layout-review confirmation.
    clear_review(out_path)
    write_manifest(
        out_path,
        {"map": map_path, "calibration": cal_path, "assignments": asn_path},
    )

    review_png = out_path.with_name(out_path.stem + "_review.png")
    problems_png = out_path.with_name(out_path.stem + "_review_problems.png")
    try:
        render_layout_review_png(map_path, cal, layout, review_png)
        print(f"wrote {review_png}")
    except Exception as exc:  # noqa: BLE001
        print(f"warning: failed to render layout review PNG: {exc}", file=sys.stderr)

    try:
        render_layout_problems_png(map_path, cal, layout, problems_png)
        print(f"wrote {problems_png}")
    except Exception as exc:  # noqa: BLE001
        print(f"warning: failed to render layout problems PNG: {exc}", file=sys.stderr)

    errors = [i for i in issues if i.severity == "error"]
    warnings = [i for i in issues if i.severity == "warning"]
    for issue in issues:
        stream = sys.stderr if issue.severity == "error" else sys.stdout
        print(str(issue), file=stream)
    print(
        f"summary: {len(layout.entries)} office layout(s), "
        f"{len(errors)} error(s), {len(warnings)} warning(s)"
    )
    print(
        f"next: review {review_png} then run "
        f"'officemapmaker layout confirm --layout {out_path}'"
    )
    return 1 if errors else 0


def _run_layout_confirm(args: argparse.Namespace) -> int:
    """Write the .reviewed sentinel next to layout.json."""
    from .manifest import confirm_review

    layout_path: Path = args.layout
    if not layout_path.exists():
        print(f"error: layout not found at {layout_path}", file=sys.stderr)
        return 2
    sentinel = confirm_review(layout_path)
    print(f"wrote {sentinel}")
    return 0


def _run_build(args: argparse.Namespace) -> int:
    """Execute Pass 4 (render the colored composite)."""
    from .calibration import compute_map_hash, load_calibration
    from .io_assignments import AssignmentLoadError, load_assignments
    from .layout import load_layout
    from .manifest import clear_review, write_manifest
    from .palette import load_team_overrides
    from .render import render_composite

    cal_path: Path = args.calibration
    map_path: Path = args.map
    asn_path: Path = args.assignments
    layout_path: Path = args.layout
    teams_path = args.teams  # Optional[Path]
    out_path: Path = args.out

    for label, path in (
        ("calibration", cal_path),
        ("map", map_path),
        ("assignments", asn_path),
        ("layout", layout_path),
    ):
        if not path.exists():
            print(f"error: {label} not found at {path}", file=sys.stderr)
            return 2

    try:
        cal = load_calibration(cal_path)
    except Exception as exc:  # noqa: BLE001
        print(f"error: could not load calibration: {exc}", file=sys.stderr)
        return 2

    try:
        layout = load_layout(layout_path)
    except Exception as exc:  # noqa: BLE001
        print(f"error: could not load layout: {exc}", file=sys.stderr)
        return 2

    try:
        assignments = load_assignments(asn_path)
    except AssignmentLoadError as exc:
        print(f"error: could not load assignments: {exc}", file=sys.stderr)
        return 2

    overrides = None
    if teams_path is not None:
        if not teams_path.exists():
            print(f"error: --teams file not found at {teams_path}", file=sys.stderr)
            return 2
        try:
            overrides = load_team_overrides(teams_path)
        except Exception as exc:  # noqa: BLE001
            print(f"error: could not load team overrides: {exc}", file=sys.stderr)
            return 2

    # Stale-input check: layout map_hash must match the current map.
    current_hash = compute_map_hash(map_path)
    if layout.map_hash and layout.map_hash != current_hash and not args.force:
        print(
            f"error: layout was planned against a different map "
            f"(layout.map_hash={layout.map_hash}, current={current_hash}). "
            f"Re-run 'officemapmaker layout ...' (or pass --force to override).",
            file=sys.stderr,
        )
        return 2

    result = render_composite(
        map_path,
        cal,
        layout,
        assignments,
        output_png=out_path,
        team_overrides=overrides,
    )

    print(f"wrote {result.composite_path}")
    print(f"wrote {result.review_path}")
    print(
        f"diff: {result.changed_pixel_count} pixel(s) changed; "
        f"{result.unexpected_pixel_count} unexpected"
    )

    # Re-rendering invalidates any prior composite-review confirmation.
    clear_review(out_path)
    write_manifest(
        out_path,
        {
            "map": map_path,
            "calibration": cal_path,
            "assignments": asn_path,
            "layout": layout_path,
            **({"teams": teams_path} if teams_path else {}),
        },
    )

    for issue in result.issues:
        stream = sys.stderr if issue.severity == "error" else sys.stdout
        print(str(issue), file=stream)
    print(
        f"summary: {len(result.errors)} error(s), {len(result.warnings)} warning(s)"
    )
    print(
        f"next: review {result.review_path} then run "
        f"'officemapmaker build confirm --composite {out_path}'"
    )
    return 1 if result.errors else 0


def _run_build_confirm(args: argparse.Namespace) -> int:
    """Write the .reviewed sentinel next to composite.png."""
    from .manifest import confirm_review

    composite_path: Path = args.composite
    if not composite_path.exists():
        print(f"error: composite not found at {composite_path}", file=sys.stderr)
        return 2
    sentinel = confirm_review(composite_path)
    print(f"wrote {sentinel}")
    return 0


def _run_tile(args: argparse.Namespace) -> int:
    """Execute Pass 5 (tile composite into letter pages + multi-page PDF)."""
    from .manifest import write_manifest
    from .tile import tile_composite

    composite_path: Path = args.composite
    out_dir: Path = args.out
    if not composite_path.exists():
        print(f"error: composite not found at {composite_path}", file=sys.stderr)
        return 2

    # Build refuses to overwrite a reviewed composite; tile is allowed to
    # run on any composite (it's a downstream packaging step).  But if the
    # composite hasn't been reviewed, mention it as a warning.
    reviewed_sentinel = composite_path.with_name(composite_path.name + ".reviewed")
    if not reviewed_sentinel.exists() and not args.auto:
        print(
            f"warning: composite has not been confirmed yet — run "
            f"'officemapmaker build confirm --composite {composite_path}' first "
            f"(or pass --auto to skip this gate)",
            file=sys.stderr,
        )

    result = tile_composite(
        composite_path,
        out_dir=out_dir,
        dpi=args.dpi,
        paper=args.paper,
        overlap_in=args.overlap_in,
    )

    print(
        f"wrote {len(result.tile_paths)} tile(s) "
        f"({result.grid.rows}x{result.grid.cols} grid at {result.grid.dpi} DPI) "
        f"to {out_dir}"
    )
    print(f"wrote {result.contact_sheet_path}")
    print(f"wrote {result.pdf_path}")

    write_manifest(
        out_dir / "all.pdf",
        {
            "composite": composite_path,
        },
    )

    for issue in result.issues:
        stream = sys.stderr if issue.severity == "error" else sys.stdout
        print(f"[{issue.severity}] {issue.code}: {issue.message}", file=stream)
    print(
        f"summary: {len(result.errors)} error(s), {len(result.warnings)} warning(s)"
    )
    print(f"next: review {result.contact_sheet_path} and open {result.pdf_path}")
    return 1 if result.errors else 0


def dispatch(args: argparse.Namespace) -> int:
    cmd = args.cmd

    if cmd == "calibrate":
        action = getattr(args, "cal_action", None)
        if action == "review":
            return _run_calibrate_review(args)
        if action == "confirm":
            return _run_calibrate_confirm(args)
        if action == "edit":
            return _run_calibrate_edit(args)
        if action == "fix-seeds":
            return _run_calibrate_fix_seeds(args)
        if args.map is None:
            print("error: --map is required when running calibration", file=sys.stderr)
            return 2
        return _run_calibrate(args)

    if cmd == "validate":
        action = args.val_action  # required
        if action == "labels":
            return _run_validate_labels(args)
        if action == "fill":
            return _run_validate_fill(args)
        # argparse guarantees we don't reach here, but be defensive.
        return _not_implemented(f"validate {action}")

    if cmd == "layout":
        action = getattr(args, "layout_action", None)
        if action == "confirm":
            return _run_layout_confirm(args)
        if args.map is None or args.assignments is None:
            print("error: --map and --assignments are required when running layout", file=sys.stderr)
            return 2
        return _run_layout(args)

    if cmd == "build":
        action = getattr(args, "build_action", None)
        if action == "confirm":
            return _run_build_confirm(args)
        if args.map is None or args.assignments is None:
            print("error: --map and --assignments are required when running build", file=sys.stderr)
            return 2
        return _run_build(args)

    if cmd == "tile":
        return _run_tile(args)

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
