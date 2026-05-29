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


def test_cli_calibrate_review_missing_pdf_returns_2(tmp_path: Path, capsys):
    from officemapmaker.__main__ import main

    cal_path = tmp_path / "calibration.json"
    cal_path.write_text("{}")
    rc = main(["calibrate", "review", "--calibration", str(cal_path)])
    assert rc == 2
    err = capsys.readouterr().err
    assert "review PDF not found" in err
