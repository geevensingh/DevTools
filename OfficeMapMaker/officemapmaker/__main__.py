"""Launcher for the OfficeMapMaker GUI wizard.

Usage::

    OfficeMapMaker <map.png> <people.xlsx> [--output DIR] [--teams teams.json]

There are no subcommands: the wizard window is the only entry point.
See plan.md section 14 for the design rationale (single-app rewrite).

This module is intentionally tiny — it parses argv, validates that the
inputs exist, instantiates a ``QApplication`` + ``MainWindow``, and
runs the event loop. Everything else lives in ``officemapmaker.wizard``.
"""

from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path
from typing import List, Optional, Sequence


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(
        prog="OfficeMapMaker",
        description=(
            "Open the OfficeMapMaker wizard on the given map and assignments "
            "spreadsheet. The wizard walks you through calibration, "
            "validation, layout, render, and tiling in a single window."
        ),
    )
    p.add_argument(
        "map",
        type=Path,
        help="Path to the floor-plan map image (PNG).",
    )
    p.add_argument(
        "assignments",
        type=Path,
        help="Path to the assignments spreadsheet (.xlsx, .xls, or .csv).",
    )
    p.add_argument(
        "--output",
        type=Path,
        default=None,
        help=(
            "Directory for the session file and rendered outputs. "
            "Defaults to the directory containing the map."
        ),
    )
    p.add_argument(
        "--teams",
        type=Path,
        default=None,
        help="Optional teams.json mapping team names to explicit colors.",
    )
    return p


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)

    map_path: Path = args.map.resolve()
    assignments_path: Path = args.assignments.resolve()
    output_dir: Path = (args.output or map_path.parent).resolve()
    teams_path: Optional[Path] = args.teams.resolve() if args.teams else None

    # Argv validation. Don't bring up an empty window for missing
    # inputs — surface the problem at the shell so the user sees it.
    if not map_path.is_file():
        print(f"error: map not found: {map_path}", file=sys.stderr)
        return 2
    if not assignments_path.is_file():
        print(
            f"error: assignments file not found: {assignments_path}",
            file=sys.stderr,
        )
        return 2
    if teams_path is not None and not teams_path.is_file():
        print(f"error: teams file not found: {teams_path}", file=sys.stderr)
        return 2
    if not output_dir.exists():
        # Create it eagerly so the wizard can drop the session file
        # without an extra error path on first save.
        output_dir.mkdir(parents=True, exist_ok=True)

    # Import PySide6 + the wizard lazily so --help and argv validation
    # don't pay the Qt import cost.
    from PySide6 import QtWidgets

    from .session import Session
    from .wizard.main_window import MainWindow

    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication(sys.argv)

    # Resolve the session up front so we can prompt about input
    # changes *before* opening the window (any other order leads to
    # the awkward "the window appeared, then asked me a question").
    load = Session.load_or_create(
        map_path=map_path,
        assignments_path=assignments_path,
        output_dir=output_dir,
        teams_path=teams_path,
    )
    if load.mode == "mismatched":
        _prompt_for_input_mismatch(load.session, load.changed_inputs)
        # Whatever the user chose, persist immediately so a Cancel
        # below leaves the on-disk state consistent.
        load.session.save()

    window = MainWindow(session=load.session)
    window.show()
    return app.exec()


def _prompt_for_input_mismatch(session, changed_inputs):
    """Ask the user how to handle a changed input file.

    Three buttons:
      * **Start over** — drop calibration, layout, and every step's
        status. Use when the user wants a clean slate.
      * **Keep partial** — fine-grained per-step invalidation; only
        steps whose inputs actually changed (or whose artifact
        dependencies were invalidated) are reset to pending.
      * **Cancel** — leaves the session untouched in memory but the
        user can still click Cancel on the resulting window if they
        want to abort entirely. The on-disk save reflects whichever
        of the above two actions was applied.

    Imported lazily because PySide6 is heavy at import time.
    """
    from PySide6 import QtWidgets

    changed_label = ", ".join(changed_inputs)
    box = QtWidgets.QMessageBox()
    box.setWindowTitle("Inputs changed since last session")
    box.setIcon(QtWidgets.QMessageBox.Icon.Warning)
    box.setText(
        f"The following input file(s) changed since your last session: "
        f"<b>{changed_label}</b>."
    )
    box.setInformativeText(
        "Choose how to handle the change:\n\n"
        "  * Start over: drop all cached results.\n"
        "  * Keep partial: only reset steps that depend on the changed "
        "input(s); reuse calibration/layout where they're still valid."
    )
    start_over = box.addButton(
        "Start over", QtWidgets.QMessageBox.ButtonRole.DestructiveRole
    )
    keep_partial = box.addButton(
        "Keep partial", QtWidgets.QMessageBox.ButtonRole.AcceptRole
    )
    box.setDefaultButton(keep_partial)
    box.exec()
    clicked = box.clickedButton()
    if clicked is start_over:
        session.start_over()
    else:
        # Default to fine-grained invalidation. Safe even if the
        # changes only affected a single downstream step.
        session.invalidate_changed(changed_inputs)


if __name__ == "__main__":
    raise SystemExit(main())
