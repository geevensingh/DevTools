"""Smoke tests for the CLI surface.

These tests verify that argparse accepts every documented command form and
dispatches to the right (stubbed) handler. They do not exercise the actual
pipeline — that lives in the pass-specific test modules added in later
milestones.
"""

from __future__ import annotations

import pytest

from officemapmaker.__main__ import build_parser, dispatch


@pytest.fixture(scope="module")
def parser():
    return build_parser()


@pytest.mark.parametrize(
    "argv",
    [
        ["calibrate", "--map", "map.png"],
        ["calibrate", "review", "--calibration", "cal.json"],
        ["calibrate", "confirm", "--calibration", "cal.json"],
        ["validate", "labels", "--calibration", "cal.json", "--assignments", "p.csv"],
        ["validate", "fill", "--map", "map.png", "--calibration", "cal.json"],
        ["layout", "--map", "map.png", "--calibration", "cal.json", "--assignments", "p.csv"],
        ["layout", "confirm", "--layout", "layout.json"],
        ["build", "--map", "map.png", "--calibration", "cal.json",
         "--assignments", "p.csv", "--layout", "layout.json"],
        ["build", "confirm", "--composite", "composite.png"],
        ["tile", "--composite", "composite.png", "--out", "tiles"],
        ["all", "--map", "map.png", "--assignments", "p.csv"],
    ],
)
def test_every_documented_form_parses(parser, argv):
    args = parser.parse_args(argv)
    assert args.cmd == argv[0]


def test_common_flags_present_on_every_subcommand(parser):
    forms = [
        ["calibrate", "--map", "m.png"],
        ["validate", "labels", "--assignments", "p.csv"],
        ["layout", "confirm"],
        ["tile"],
        ["all", "--map", "m.png", "--assignments", "p.csv"],
    ]
    for argv in forms:
        args = parser.parse_args([*argv, "--auto", "--force"])
        assert args.auto is True
        assert args.force is True


def test_dispatch_returns_not_implemented(parser):
    args = parser.parse_args(["calibrate", "--map", "m.png"])
    assert dispatch(args) == 2  # stubbed; non-zero
