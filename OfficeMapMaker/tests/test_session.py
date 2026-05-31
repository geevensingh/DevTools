"""Tests for ``officemapmaker.session.Session`` (W2)."""

from __future__ import annotations

import json
from pathlib import Path

import pytest

from officemapmaker.session import (
    SCHEMA_VERSION,
    STEP_IDS,
    Issue,
    Session,
    StepState,
    StepStatus,
    compute_file_hash,
)


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _write_bytes(path: Path, data: bytes) -> Path:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(data)
    return path


@pytest.fixture
def map_file(tmp_path: Path) -> Path:
    # PNG magic so it looks like an image (the session code only ever
    # reads bytes for hashing — never decodes).
    return _write_bytes(tmp_path / "millennium_b.png", b"\x89PNG\r\n\x1a\n" + b"x" * 64)


@pytest.fixture
def assignments_file(tmp_path: Path) -> Path:
    return _write_bytes(tmp_path / "millennium_b.xlsx", b"PK\x03\x04" + b"y" * 64)


@pytest.fixture
def teams_file(tmp_path: Path) -> Path:
    return _write_bytes(tmp_path / "teams.json", b'{"Foo": "#FFAABB"}\n')


# ---------------------------------------------------------------------------
# Fresh load
# ---------------------------------------------------------------------------


def test_load_or_create_returns_fresh_when_no_session_file(map_file, assignments_file, tmp_path):
    result = Session.load_or_create(
        map_path=map_file,
        assignments_path=assignments_file,
        output_dir=tmp_path,
    )
    assert result.mode == "fresh"
    assert result.changed_inputs == ()
    sess = result.session
    assert sess.current_step == "calibrate"
    for step_id in STEP_IDS:
        assert sess.step_state[step_id].status == StepStatus.PENDING
        assert sess.step_state[step_id].issues == []
    assert sess.calibration is None
    assert sess.layout is None


def test_session_path_is_next_to_map_in_output_dir(map_file, assignments_file, tmp_path):
    out = tmp_path / "out"
    out.mkdir()
    expected = out / "millennium_b.session.json"
    assert Session.session_path_for(map_file, out) == expected
    res = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=out,
    )
    res.session.save()
    assert expected.is_file()


# ---------------------------------------------------------------------------
# Round-trip save/load
# ---------------------------------------------------------------------------


def test_save_and_reload_restores_step_state(map_file, assignments_file, tmp_path):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session

    issue = Issue(
        code="missing_assignment",
        severity="warning",
        message="Office 1480 has no people",
        room_id="1480",
    )
    sess.set_step("calibrate", status=StepStatus.OK, issues=[])
    sess.set_step("validate_labels", status=StepStatus.WARNING, issues=[issue])
    sess.current_step = "validate_labels"
    sess.save()

    # Mismatch-detection compares hashes of files on disk, so the
    # second load (with no input changes) must come back "restored".
    res2 = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    )
    assert res2.mode == "restored"
    assert res2.changed_inputs == ()
    sess2 = res2.session
    assert sess2.step_state["calibrate"].status == StepStatus.OK
    assert sess2.step_state["validate_labels"].status == StepStatus.WARNING
    assert len(sess2.step_state["validate_labels"].issues) == 1
    assert sess2.step_state["validate_labels"].issues[0].room_id == "1480"
    assert sess2.current_step == "validate_labels"


def test_save_is_atomic_no_partial_file_on_failure(map_file, assignments_file, tmp_path, monkeypatch):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session
    sess.save()

    # Corrupt the file by forcing a replace failure mid-save. The
    # original file must remain readable.
    original = sess._path.read_bytes()

    def boom(self, *a, **kw):
        raise OSError("simulated rename failure")

    monkeypatch.setattr(Path, "replace", boom)
    with pytest.raises(OSError):
        sess.save()
    # File on disk is the pre-failure version, not a half-written
    # mess.
    assert sess._path.read_bytes() == original


# ---------------------------------------------------------------------------
# Mismatch detection
# ---------------------------------------------------------------------------


def test_changed_assignments_reports_mismatch(map_file, assignments_file, tmp_path):
    Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session.save()
    assignments_file.write_bytes(b"PK\x03\x04" + b"DIFFERENT" * 16)
    res = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    )
    assert res.mode == "mismatched"
    assert res.changed_inputs == ("assignments",)


def test_changed_map_and_assignments_reports_both(map_file, assignments_file, tmp_path):
    Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session.save()
    map_file.write_bytes(b"\x89PNG\r\n\x1a\n" + b"NEW" * 32)
    assignments_file.write_bytes(b"PK\x03\x04" + b"NEW" * 32)
    res = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    )
    assert res.mode == "mismatched"
    assert set(res.changed_inputs) == {"map", "assignments"}


def test_adding_a_teams_file_reports_mismatch(map_file, assignments_file, teams_file, tmp_path):
    # Start without teams.
    Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session.save()
    res = Session.load_or_create(
        map_path=map_file,
        assignments_path=assignments_file,
        output_dir=tmp_path,
        teams_path=teams_file,
    )
    assert res.mode == "mismatched"
    assert res.changed_inputs == ("teams",)


# ---------------------------------------------------------------------------
# Fine-grained invalidation
# ---------------------------------------------------------------------------


def test_invalidate_changed_assignments_resets_only_dependent_steps(
    map_file, assignments_file, tmp_path
):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session

    # Pretend every step has run successfully.
    for sid in STEP_IDS:
        sess.set_step(sid, status=StepStatus.OK, issues=[])

    reset = sess.invalidate_changed(("assignments",))

    # ``calibrate`` and ``validate_fill`` don't depend on assignments;
    # everything else does.
    assert "calibrate" not in reset
    assert "validate_fill" not in reset
    assert set(reset) == {"validate_labels", "layout", "build", "tile"}
    assert sess.step_state["calibrate"].status == StepStatus.OK
    assert sess.step_state["validate_fill"].status == StepStatus.OK
    assert sess.step_state["validate_labels"].status == StepStatus.PENDING


def test_invalidate_changed_map_resets_every_step(map_file, assignments_file, tmp_path):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session
    for sid in STEP_IDS:
        sess.set_step(sid, status=StepStatus.OK, issues=[])
    reset = sess.invalidate_changed(("map",))
    assert set(reset) == set(STEP_IDS)
    for sid in STEP_IDS:
        assert sess.step_state[sid].status == StepStatus.PENDING


def test_invalidate_changed_teams_resets_only_render_steps(
    map_file, assignments_file, tmp_path
):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session
    for sid in STEP_IDS:
        sess.set_step(sid, status=StepStatus.OK, issues=[])
    reset = sess.invalidate_changed(("teams",))
    assert set(reset) == {"build", "tile"}


def test_invalidate_from_resets_step_and_downstream(map_file, assignments_file, tmp_path):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session
    for sid in STEP_IDS:
        sess.set_step(sid, status=StepStatus.OK, issues=[])
    sess.invalidate_from("layout")
    assert sess.step_state["calibrate"].status == StepStatus.OK
    assert sess.step_state["validate_fill"].status == StepStatus.OK
    for sid in ("layout", "build", "tile"):
        assert sess.step_state[sid].status == StepStatus.PENDING


def test_start_over_drops_everything(map_file, assignments_file, tmp_path):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session
    for sid in STEP_IDS:
        sess.set_step(sid, status=StepStatus.OK, issues=[])
    sess.current_step = "build"
    sess.start_over()
    for sid in STEP_IDS:
        assert sess.step_state[sid].status == StepStatus.PENDING
    assert sess.current_step == STEP_IDS[0]
    assert sess.calibration is None
    assert sess.layout is None


# ---------------------------------------------------------------------------
# Error handling
# ---------------------------------------------------------------------------


def test_corrupt_session_file_falls_back_to_fresh(map_file, assignments_file, tmp_path):
    path = Session.session_path_for(map_file, tmp_path)
    path.write_text("{not json at all", encoding="utf-8")
    res = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    )
    assert res.mode == "fresh"


def test_future_schema_version_falls_back_to_fresh(map_file, assignments_file, tmp_path):
    path = Session.session_path_for(map_file, tmp_path)
    path.write_text(
        json.dumps({"schema_version": SCHEMA_VERSION + 1}),
        encoding="utf-8",
    )
    res = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    )
    # The catch-all ``ValueError`` in load_or_create swallows the
    # schema mismatch and starts fresh; in W10 polish we may want to
    # surface a "session was from a newer version" warning instead.
    assert res.mode == "fresh"


def test_set_step_with_unknown_id_raises(map_file, assignments_file, tmp_path):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session
    with pytest.raises(ValueError):
        sess.set_step("not_a_real_step", status=StepStatus.OK)


# ---------------------------------------------------------------------------
# Input-hash stamping
# ---------------------------------------------------------------------------


def test_set_step_to_ok_stamps_input_hash(map_file, assignments_file, tmp_path):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session
    assert sess.step_state["validate_labels"].last_run_input_hash is None
    sess.set_step("validate_labels", status=StepStatus.OK, issues=[])
    h = sess.step_state["validate_labels"].last_run_input_hash
    assert h is not None and h.startswith("sha256:")


def test_running_status_does_not_stamp_input_hash(map_file, assignments_file, tmp_path):
    sess = Session.load_or_create(
        map_path=map_file, assignments_path=assignments_file, output_dir=tmp_path,
    ).session
    sess.set_step("calibrate", status=StepStatus.RUNNING)
    assert sess.step_state["calibrate"].last_run_input_hash is None


def test_compute_file_hash_is_deterministic_and_changes_with_content(tmp_path):
    f = tmp_path / "x.bin"
    f.write_bytes(b"hello")
    h1 = compute_file_hash(f)
    h2 = compute_file_hash(f)
    assert h1 == h2 and h1.startswith("sha256:")
    f.write_bytes(b"world")
    assert compute_file_hash(f) != h1
