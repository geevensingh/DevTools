"""Tests for ``officemapmaker.io_assignments``.

Covers all three supported formats (xlsx, xls, csv), header normalization edge
cases, Excel-numeric vs alphanumeric office IDs, BOM handling, extension lying,
and error conditions.
"""

from __future__ import annotations

import csv
from pathlib import Path

import pytest

from officemapmaker.io_assignments import (
    Assignment,
    AssignmentLoadError,
    detect_format,
    load_assignments,
)


# ---------------------------------------------------------------------------
# Fixture helpers — generate each format on the fly so no binary blobs are
# committed to the repo.
# ---------------------------------------------------------------------------


def _write_xlsx(path: Path, rows: list[list[object]]) -> None:
    import openpyxl

    wb = openpyxl.Workbook()
    ws = wb.active
    for row in rows:
        ws.append(row)
    wb.save(path)


def _write_xls(path: Path, rows: list[list[object]]) -> None:
    import xlwt

    book = xlwt.Workbook()
    sheet = book.add_sheet("Sheet1")
    for r, row in enumerate(rows):
        for c, val in enumerate(row):
            sheet.write(r, c, val)
    book.save(str(path))


def _write_csv(path: Path, rows: list[list[object]], *, bom: bool = False) -> None:
    encoding = "utf-8-sig" if bom else "utf-8"
    with path.open("w", encoding=encoding, newline="") as f:
        writer = csv.writer(f)
        for row in rows:
            writer.writerow(row)


# A canonical happy-path row set used by multiple tests.
HAPPY_ROWS = [
    ["Full Name", "Office number", "Team"],
    ["Alice Anderson", 1480, "BITS"],
    ["Bob Brown", "1479A", "FPAA"],
    ["Carol Chen", 1481, "BITS"],
]


# ---------------------------------------------------------------------------
# detect_format
# ---------------------------------------------------------------------------


def test_detect_format_xlsx(tmp_path: Path) -> None:
    p = tmp_path / "a.xlsx"
    _write_xlsx(p, HAPPY_ROWS)
    assert detect_format(p) == "xlsx"


def test_detect_format_xls(tmp_path: Path) -> None:
    p = tmp_path / "a.xls"
    _write_xls(p, HAPPY_ROWS)
    assert detect_format(p) == "xls"


def test_detect_format_csv(tmp_path: Path) -> None:
    p = tmp_path / "a.csv"
    _write_csv(p, HAPPY_ROWS)
    assert detect_format(p) == "csv"


def test_detect_format_ignores_extension(tmp_path: Path) -> None:
    """Real-world: the sample file is named .xlsx but is actually BIFF .xls."""
    p = tmp_path / "lies.xlsx"
    _write_xls(p, HAPPY_ROWS)
    assert detect_format(p) == "xls"


# ---------------------------------------------------------------------------
# load_assignments — happy path, all three formats produce identical output
# ---------------------------------------------------------------------------


@pytest.mark.parametrize("writer,suffix", [(_write_xlsx, ".xlsx"), (_write_xls, ".xls"), (_write_csv, ".csv")])
def test_load_happy_path_all_formats(
    tmp_path: Path, writer, suffix: str
) -> None:
    p = tmp_path / f"happy{suffix}"
    writer(p, HAPPY_ROWS)
    out = load_assignments(p)
    assert out == [
        Assignment(name="Alice Anderson", office_id="1480", team="BITS", source_row=2),
        Assignment(name="Bob Brown",      office_id="1479A", team="FPAA", source_row=3),
        Assignment(name="Carol Chen",     office_id="1481", team="BITS", source_row=4),
    ]


# ---------------------------------------------------------------------------
# Office-id normalization
# ---------------------------------------------------------------------------


def test_office_id_numeric_excel_loses_trailing_zero(tmp_path: Path) -> None:
    """Excel stores 1480 as float 1480.0 — we must emit '1480', not '1480.0'."""
    p = tmp_path / "n.xlsx"
    _write_xlsx(p, [["Full Name", "Office number", "Team"], ["X Y", 1480, "T"]])
    out = load_assignments(p)
    assert out[0].office_id == "1480"


def test_office_id_alphanumeric_passes_through(tmp_path: Path) -> None:
    p = tmp_path / "a.csv"
    _write_csv(p, [["Full Name", "Office number", "Team"], ["X Y", "MER101", "T"]])
    assert load_assignments(p)[0].office_id == "MER101"


def test_office_id_decimal_keeps_decimal(tmp_path: Path) -> None:
    """Non-integer numerics keep their decimal — caller decides what's an error."""
    p = tmp_path / "d.xlsx"
    _write_xlsx(p, [["Full Name", "Office number", "Team"], ["X Y", 12.5, "T"]])
    assert load_assignments(p)[0].office_id == "12.5"


# ---------------------------------------------------------------------------
# Whitespace / empty / BOM
# ---------------------------------------------------------------------------


def test_whitespace_is_stripped_from_all_cells(tmp_path: Path) -> None:
    p = tmp_path / "ws.csv"
    _write_csv(p, [["Full Name", "Office number", "Team"], ["  Alice  ", " 1480 ", "  BITS  "]])
    out = load_assignments(p)
    assert out == [Assignment(name="Alice", office_id="1480", team="BITS", source_row=2)]


def test_blank_rows_in_middle_are_skipped(tmp_path: Path) -> None:
    p = tmp_path / "gaps.csv"
    _write_csv(p, [
        ["Full Name", "Office number", "Team"],
        ["Alice", "1480", "BITS"],
        ["", "", ""],
        [],
        ["Bob", "1481", "FPAA"],
    ])
    out = load_assignments(p)
    assert [a.name for a in out] == ["Alice", "Bob"]
    # Source rows still reflect the original file layout.
    assert [a.source_row for a in out] == [2, 5]


def test_csv_with_bom_is_handled(tmp_path: Path) -> None:
    """Excel's 'Save As CSV (UTF-8)' adds a BOM to the file."""
    p = tmp_path / "bom.csv"
    _write_csv(p, HAPPY_ROWS, bom=True)
    out = load_assignments(p)
    assert len(out) == 3
    assert out[0].name == "Alice Anderson"


# ---------------------------------------------------------------------------
# Header detection
# ---------------------------------------------------------------------------


def test_header_is_case_insensitive(tmp_path: Path) -> None:
    p = tmp_path / "case.csv"
    _write_csv(p, [
        ["FULL NAME", "office NUMBER", "Team"],
        ["Alice", "1480", "BITS"],
    ])
    assert load_assignments(p)[0].name == "Alice"


def test_header_column_order_can_vary(tmp_path: Path) -> None:
    p = tmp_path / "order.csv"
    _write_csv(p, [
        ["Team", "Full Name", "Office number"],
        ["BITS", "Alice", "1480"],
    ])
    out = load_assignments(p)
    assert out == [Assignment(name="Alice", office_id="1480", team="BITS", source_row=2)]


def test_header_can_have_title_rows_above(tmp_path: Path) -> None:
    """Real-world Excel files often have a title or banner above the header."""
    p = tmp_path / "title.xlsx"
    _write_xlsx(p, [
        ["Building B Move May 2026"],
        [""],
        ["Full Name", "Office number", "Team"],
        ["Alice", 1480, "BITS"],
    ])
    out = load_assignments(p)
    assert out == [Assignment(name="Alice", office_id="1480", team="BITS", source_row=4)]


def test_extra_columns_are_ignored(tmp_path: Path) -> None:
    p = tmp_path / "extra.csv"
    _write_csv(p, [
        ["Full Name", "Email", "Office number", "Team", "Notes"],
        ["Alice", "alice@x", "1480", "BITS", "n/a"],
    ])
    out = load_assignments(p)
    assert out == [Assignment(name="Alice", office_id="1480", team="BITS", source_row=2)]


# ---------------------------------------------------------------------------
# Error cases
# ---------------------------------------------------------------------------


def test_missing_required_column_raises(tmp_path: Path) -> None:
    p = tmp_path / "missing.csv"
    _write_csv(p, [
        ["Full Name", "Team"],   # no 'Office number'
        ["Alice", "BITS"],
    ])
    with pytest.raises(AssignmentLoadError, match="header"):
        load_assignments(p)


def test_only_header_no_data_raises(tmp_path: Path) -> None:
    p = tmp_path / "empty.csv"
    _write_csv(p, [["Full Name", "Office number", "Team"]])
    with pytest.raises(AssignmentLoadError, match="no data rows"):
        load_assignments(p)


def test_completely_empty_file_raises(tmp_path: Path) -> None:
    p = tmp_path / "void.csv"
    p.write_text("", encoding="utf-8")
    with pytest.raises(AssignmentLoadError):
        load_assignments(p)


def test_missing_file_raises(tmp_path: Path) -> None:
    p = tmp_path / "does-not-exist.csv"
    with pytest.raises(AssignmentLoadError, match="file not found"):
        load_assignments(p)


def test_directory_path_raises(tmp_path: Path) -> None:
    with pytest.raises(AssignmentLoadError, match="not a file"):
        load_assignments(tmp_path)


def test_encrypted_ole2_doc_gives_actionable_error(tmp_path: Path) -> None:
    """OLE2 files containing an EncryptedPackage stream (Azure IRM, password
    protection) should be detected and produce a workflow-actionable error.
    """
    # Hand-craft the minimum: OLE2 magic header + the UTF-16-LE bytes for
    # 'EncryptedPackage' somewhere in the first 64 KB. xlrd will fail to find
    # a BIFF workbook stream, and the loader should detect the encryption
    # signature and raise a clearer message.
    p = tmp_path / "rights_managed.xlsx"  # extension deliberately misleading
    header = bytes.fromhex("D0CF11E0A1B11AE1") + b"\x00" * 56
    encrypted_name = "EncryptedPackage".encode("utf-16-le")
    p.write_bytes(header + b"\x00" * 200 + encrypted_name + b"\x00" * 200)

    with pytest.raises(AssignmentLoadError, match="password-protected or rights-managed"):
        load_assignments(p)
