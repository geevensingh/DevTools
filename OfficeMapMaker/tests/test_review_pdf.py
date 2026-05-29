"""Tests for ``officemapmaker.review_pdf`` + the calibrate review/confirm CLI."""

from __future__ import annotations

from pathlib import Path

import numpy as np
import pytest

from officemapmaker.calibration import (
    Calibration,
    Classification,
    Label,
    RenderDefaults,
    Room,
    save_calibration,
)
from officemapmaker.geometry import mask_to_rle
from officemapmaker.review_pdf import (
    _color_for_classification,
    _crop_label_region,
    _distinct_color,
    _wrap_text,
    build_calibration_review_pdf,
)


# ---------------------------------------------------------------------------
# Fixtures
# ---------------------------------------------------------------------------


def _make_map_png(path: Path, size: tuple[int, int] = (400, 300)) -> None:
    from PIL import Image, ImageDraw

    img = Image.new("RGB", size, "white")
    draw = ImageDraw.Draw(img)
    draw.rectangle((20, 20, size[0] - 20, size[1] - 20), outline="black", width=3)
    draw.rectangle((40, 40, 180, 200), outline="black", width=2)
    draw.rectangle((200, 40, 360, 200), outline="black", width=2)
    img.save(path)


def _square_room(rid: int, x: int, y: int, side: int, *, img_size: tuple[int, int]) -> Room:
    mask = np.zeros((img_size[1], img_size[0]), dtype=bool)
    mask[y : y + side, x : x + side] = True
    return Room(
        id=rid,
        polygon_rle=mask_to_rle(mask),
        area_px=int(mask.sum()),
        bbox=(x, y, side, side),
    )


def _make_minimal_calibration(img_size: tuple[int, int]) -> Calibration:
    return Calibration(
        map_image="map.png",
        map_hash="sha256:dummy",
        labels=[
            Label(
                id="1480",
                bbox=(60, 80, 30, 14),
                room_id=1,
                classification=Classification.OFFICE,
                fill_seed=(110, 120),
                ocr_confidence=0.92,
            ),
            Label(
                id="1481",
                bbox=(220, 80, 30, 14),
                room_id=2,
                classification=Classification.OFFICE,
                fill_seed=(280, 120),
                ocr_confidence=0.41,
            ),
        ],
        rooms=[
            _square_room(1, 40, 40, 140, img_size=img_size),
            _square_room(2, 200, 40, 160, img_size=img_size),
        ],
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )


# ---------------------------------------------------------------------------
# Pure-function helpers
# ---------------------------------------------------------------------------


def test_distinct_colors_are_in_unit_range():
    for i in range(20):
        r, g, b = _distinct_color(i, 20)
        for ch in (r, g, b):
            assert 0.0 <= ch <= 1.0


def test_distinct_color_handles_zero_total():
    # Should not crash; return a neutral gray.
    r, g, b = _distinct_color(0, 0)
    assert (r, g, b) == (0.5, 0.5, 0.5)


def test_color_for_classification_covers_all_values():
    for c in Classification:
        rgb = _color_for_classification(c)
        assert len(rgb) == 3
        for ch in rgb:
            assert 0.0 <= ch <= 1.0


def test_crop_label_region_clamps_to_image_bounds():
    from PIL import Image

    img = Image.new("RGB", (100, 100), "white")
    # Bbox spilling off the right edge.
    crop = _crop_label_region(img, bbox=(90, 50, 30, 20), expand=4.0)
    assert crop is not None
    assert crop.size[0] <= 100 and crop.size[1] <= 100


def test_crop_label_region_returns_none_for_zero_area():
    from PIL import Image

    img = Image.new("RGB", (100, 100), "white")
    # Bbox entirely outside the image.
    crop = _crop_label_region(img, bbox=(200, 200, 30, 20), expand=4.0)
    assert crop is None


# ---------------------------------------------------------------------------
# Caption word-wrap
# ---------------------------------------------------------------------------


def test_wrap_text_returns_single_line_when_text_fits():
    lines = _wrap_text("short caption", max_width_pt=500.0, font_name="Helvetica", font_size=9)
    assert lines == ["short caption"]


def test_wrap_text_breaks_into_multiple_lines_when_too_long():
    caption = (
        "230 labels found by OCR. Each green box is where OCR found a label and "
        "the green text is what it read. The translucent fill marks the room "
        "polygon we associated with that label."
    )
    # Letter page width minus 1" of margins ~= 540 pt.
    lines = _wrap_text(caption, max_width_pt=540.0, font_name="Helvetica", font_size=9)
    assert len(lines) >= 2, f"expected wrap, got {lines!r}"
    # Every individual line must fit the width.
    from reportlab.pdfbase.pdfmetrics import stringWidth

    for line in lines:
        assert stringWidth(line, "Helvetica", 9) <= 540.0, f"line still too wide: {line!r}"
    # Round-trip: joining lines must give back the original tokens.
    assert " ".join(lines).split() == caption.split()


def test_wrap_text_handles_empty_string():
    assert _wrap_text("", max_width_pt=200.0, font_name="Helvetica", font_size=9) == [""]


def test_wrap_text_keeps_long_unbreakable_word_on_its_own_line():
    word = "supercalifragilisticexpialidocious"
    lines = _wrap_text(f"a {word} z", max_width_pt=30.0, font_name="Helvetica", font_size=9)
    assert word in lines


# ---------------------------------------------------------------------------
# End-to-end PDF rendering
# ---------------------------------------------------------------------------


def test_build_calibration_review_pdf_writes_a_valid_pdf(tmp_path: Path):
    map_png = tmp_path / "map.png"
    _make_map_png(map_png, size=(400, 300))
    cal = _make_minimal_calibration(img_size=(400, 300))
    pdf_path = tmp_path / "calibration_review.pdf"

    build_calibration_review_pdf(map_png, cal, pdf_path)

    assert pdf_path.exists()
    data = pdf_path.read_bytes()
    assert data.startswith(b"%PDF-"), "output is not a PDF"
    # Sanity-check: should have at least 4 pages worth of content.
    assert len(data) > 5_000, "PDF unexpectedly small"


def test_build_calibration_review_pdf_handles_orphans(tmp_path: Path):
    map_png = tmp_path / "map.png"
    _make_map_png(map_png, size=(400, 300))

    # Calibration where one label is orphan (room_id=None) and one room is orphan.
    cal = Calibration(
        map_image="map.png",
        map_hash="sha256:dummy",
        labels=[
            Label(
                id="ORPH1",
                bbox=(300, 200, 30, 14),
                room_id=None,
                classification=Classification.SKIP,
                fill_seed=(315, 207),
                ocr_confidence=0.55,
            ),
        ],
        rooms=[_square_room(1, 40, 40, 140, img_size=(400, 300))],
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )
    pdf_path = tmp_path / "calibration_review.pdf"
    build_calibration_review_pdf(map_png, cal, pdf_path)
    assert pdf_path.exists()


def test_build_calibration_review_pdf_handles_empty_calibration(tmp_path: Path):
    map_png = tmp_path / "map.png"
    _make_map_png(map_png, size=(400, 300))
    cal = Calibration(
        map_image="map.png",
        map_hash="sha256:dummy",
        labels=[],
        rooms=[],
        wall_patches=[],
        render_defaults=RenderDefaults(),
    )
    pdf_path = tmp_path / "calibration_review.pdf"
    build_calibration_review_pdf(map_png, cal, pdf_path)
    assert pdf_path.exists()


def test_build_calibration_review_pdf_raises_when_map_missing(tmp_path: Path):
    cal = _make_minimal_calibration(img_size=(400, 300))
    with pytest.raises(FileNotFoundError):
        build_calibration_review_pdf(tmp_path / "no.png", cal, tmp_path / "out.pdf")


# ---------------------------------------------------------------------------
# CLI integration
# ---------------------------------------------------------------------------


def test_cli_calibrate_confirm_writes_sentinel(tmp_path: Path):
    from officemapmaker.__main__ import main

    cal_path = tmp_path / "calibration.json"
    cal = _make_minimal_calibration(img_size=(400, 300))
    save_calibration(cal, cal_path)

    rc = main(["calibrate", "confirm", "--calibration", str(cal_path)])

    assert rc == 0
    sentinel = cal_path.with_name(cal_path.name + ".reviewed")
    assert sentinel.exists()


def test_cli_calibrate_confirm_missing_calibration_returns_2(tmp_path: Path):
    from officemapmaker.__main__ import main

    rc = main(["calibrate", "confirm", "--calibration", str(tmp_path / "nope.json")])
    assert rc == 2


def test_cli_calibrate_review_missing_calibration_returns_2(tmp_path: Path, capsys):
    from officemapmaker.__main__ import main

    rc = main(["calibrate", "review", "--calibration", str(tmp_path / "nope.json")])
    assert rc == 2
    err = capsys.readouterr().err
    assert "calibration not found" in err


def test_cli_calibrate_review_invalid_calibration_returns_2(tmp_path: Path, capsys):
    """A calibration file that isn't a valid Calibration should fail cleanly."""
    from officemapmaker.__main__ import main

    cal_path = tmp_path / "calibration.json"
    cal_path.write_text("{}")  # missing required keys
    rc = main(["calibrate", "review", "--calibration", str(cal_path)])
    assert rc == 2
    err = capsys.readouterr().err
    assert "could not load calibration" in err


def test_cli_calibrate_review_missing_map_returns_2(tmp_path: Path, capsys):
    """If we need to rebuild but the source map is gone, fail with guidance."""
    from officemapmaker.__main__ import main

    cal_path = tmp_path / "calibration.json"
    cal = _make_minimal_calibration(img_size=(400, 300))
    save_calibration(cal, cal_path)
    # Note: no map.png is written next to the calibration.

    rc = main(["calibrate", "review", "--calibration", str(cal_path)])
    assert rc == 2
    err = capsys.readouterr().err
    assert "source map not found" in err
    assert "--map" in err  # nudge user toward the explicit flag


def test_cli_calibrate_review_rebuilds_when_pdf_missing(
    tmp_path: Path, monkeypatch, capsys
):
    """First-time review (no PDF yet) builds it from the calibration."""
    from officemapmaker.__main__ import main
    import officemapmaker.__main__ as cli_mod

    cal_path = tmp_path / "calibration.json"
    map_path = tmp_path / "map.png"
    _make_map_png(map_path, size=(400, 300))
    cal = _make_minimal_calibration(img_size=(400, 300))
    save_calibration(cal, cal_path)

    pdf_path = cal_path.with_name("calibration_review.pdf")
    assert not pdf_path.exists()

    # Don't actually launch a PDF viewer.
    opened: list[str] = []
    monkeypatch.setattr(cli_mod.os, "startfile", lambda p: opened.append(p), raising=False)

    rc = main(["calibrate", "review", "--calibration", str(cal_path)])

    assert rc == 0
    assert pdf_path.exists()
    assert opened == [str(pdf_path)]
    out = capsys.readouterr().out
    assert "rebuilt" in out


def test_cli_calibrate_review_rebuilds_when_calibration_newer(
    tmp_path: Path, monkeypatch, capsys
):
    """Hand-editing the calibration (newer mtime) triggers a PDF rebuild."""
    import os
    import time
    from officemapmaker.__main__ import main
    import officemapmaker.__main__ as cli_mod
    from officemapmaker.manifest import confirm_review, is_reviewed

    cal_path = tmp_path / "calibration.json"
    map_path = tmp_path / "map.png"
    _make_map_png(map_path, size=(400, 300))
    cal = _make_minimal_calibration(img_size=(400, 300))
    save_calibration(cal, cal_path)

    pdf_path = cal_path.with_name("calibration_review.pdf")

    monkeypatch.setattr(cli_mod.os, "startfile", lambda p: None, raising=False)

    # First call builds the PDF.
    assert main(["calibrate", "review", "--calibration", str(cal_path)]) == 0
    first_mtime = pdf_path.stat().st_mtime

    # Pretend the user reviewed it.
    confirm_review(cal_path)
    assert is_reviewed(cal_path)

    # Simulate the user hand-editing calibration.json AFTER the PDF was built.
    # Bump the calibration's mtime forward to guarantee strict newer-than even
    # on filesystems with coarse timestamps.
    future = first_mtime + 5
    os.utime(cal_path, (future, future))

    # Capture stdout so the rebuild banner can be asserted.
    capsys.readouterr()  # drain previous output
    assert main(["calibrate", "review", "--calibration", str(cal_path)]) == 0

    second_mtime = pdf_path.stat().st_mtime
    assert second_mtime > first_mtime, "PDF should have been regenerated"
    out = capsys.readouterr().out
    assert "rebuilt" in out
    # Hand-edits invalidate prior review.
    assert not is_reviewed(cal_path), ".reviewed sentinel should be cleared on rebuild"


def test_cli_calibrate_review_skips_rebuild_when_pdf_newer(
    tmp_path: Path, monkeypatch, capsys
):
    """If the PDF is already up to date, just open it — don't rebuild."""
    import os
    from officemapmaker.__main__ import main
    import officemapmaker.__main__ as cli_mod

    cal_path = tmp_path / "calibration.json"
    map_path = tmp_path / "map.png"
    _make_map_png(map_path, size=(400, 300))
    cal = _make_minimal_calibration(img_size=(400, 300))
    save_calibration(cal, cal_path)

    pdf_path = cal_path.with_name("calibration_review.pdf")
    pdf_path.write_bytes(b"%PDF-1.4 sentinel\n")  # not a real PDF — never opened-for-read

    # Make the PDF strictly newer than the calibration.
    cal_mtime = cal_path.stat().st_mtime
    future = cal_mtime + 5
    os.utime(pdf_path, (future, future))
    expected_mtime = pdf_path.stat().st_mtime

    monkeypatch.setattr(cli_mod.os, "startfile", lambda p: None, raising=False)

    capsys.readouterr()
    rc = main(["calibrate", "review", "--calibration", str(cal_path)])

    assert rc == 0
    assert pdf_path.stat().st_mtime == expected_mtime, "PDF should NOT have been rebuilt"
    out = capsys.readouterr().out
    assert "rebuilt" not in out
    assert pdf_path.read_bytes() == b"%PDF-1.4 sentinel\n"


def test_cli_calibrate_review_explicit_map_flag(
    tmp_path: Path, monkeypatch
):
    """--map overrides the recorded map_image (useful when the map moved)."""
    from officemapmaker.__main__ import main
    import officemapmaker.__main__ as cli_mod

    cal_path = tmp_path / "calibration.json"
    cal = _make_minimal_calibration(img_size=(400, 300))
    save_calibration(cal, cal_path)
    # Recorded map_image points at ./map.png which we DON'T create.
    # Instead the map lives in a different folder.
    elsewhere = tmp_path / "moved"
    elsewhere.mkdir()
    moved_map = elsewhere / "map.png"
    _make_map_png(moved_map, size=(400, 300))

    monkeypatch.setattr(cli_mod.os, "startfile", lambda p: None, raising=False)

    rc = main([
        "calibrate", "review",
        "--calibration", str(cal_path),
        "--map", str(moved_map),
    ])

    assert rc == 0
    assert cal_path.with_name("calibration_review.pdf").exists()
