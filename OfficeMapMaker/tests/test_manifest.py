"""Tests for the SHA-manifest + review-sentinel helpers."""

from __future__ import annotations

from pathlib import Path

from officemapmaker import manifest


def _touch(p: Path, content: str = "x") -> Path:
    p.write_text(content)
    return p


def test_write_and_check_manifest_clean(tmp_path: Path):
    inp = _touch(tmp_path / "in.txt", "alpha")
    out = _touch(tmp_path / "out.bin", "result")

    manifest.write_manifest(out, {"in": inp})
    mismatches = manifest.check_manifest(out, {"in": inp})

    assert mismatches == []


def test_check_manifest_detects_content_change(tmp_path: Path):
    inp = _touch(tmp_path / "in.txt", "alpha")
    out = _touch(tmp_path / "out.bin", "result")

    manifest.write_manifest(out, {"in": inp})
    _touch(inp, "BETA — different content")  # mutate the input

    mismatches = manifest.check_manifest(out, {"in": inp})
    assert len(mismatches) == 1
    assert mismatches[0].name == "in"
    assert mismatches[0].recorded != mismatches[0].current


def test_check_manifest_detects_missing_manifest(tmp_path: Path):
    inp = _touch(tmp_path / "in.txt")
    out = tmp_path / "out.bin"  # never written; manifest doesn't exist

    mismatches = manifest.check_manifest(out, {"in": inp})
    assert len(mismatches) == 1
    assert mismatches[0].recorded is None


def test_review_sentinel_round_trip(tmp_path: Path):
    out = _touch(tmp_path / "out.bin")
    assert manifest.is_reviewed(out) is False

    manifest.confirm_review(out)
    assert manifest.is_reviewed(out) is True

    manifest.clear_review(out)
    assert manifest.is_reviewed(out) is False


def test_gate_force_skips_everything(tmp_path: Path):
    inp = _touch(tmp_path / "in.txt", "v1")
    out = _touch(tmp_path / "out.bin")
    # No manifest, no sentinel — would normally fail.
    assert manifest.gate(out, {"in": inp}, auto=False, force=True) == []


def test_gate_auto_skips_review_only(tmp_path: Path):
    inp = _touch(tmp_path / "in.txt", "v1")
    out = _touch(tmp_path / "out.bin")
    manifest.write_manifest(out, {"in": inp})

    # SHA matches but no review sentinel — auto should pass.
    assert manifest.gate(out, {"in": inp}, auto=True, force=False) == []

    # Now mutate the input — even with auto, the stale-SHA gate should fail.
    _touch(inp, "v2")
    errors = manifest.gate(out, {"in": inp}, auto=True, force=False)
    assert len(errors) == 1
    assert "stale-input gate" in errors[0]


def test_gate_full_happy_path(tmp_path: Path):
    inp = _touch(tmp_path / "in.txt", "v1")
    out = _touch(tmp_path / "out.bin")
    manifest.write_manifest(out, {"in": inp})
    manifest.confirm_review(out)

    assert manifest.gate(out, {"in": inp}, auto=False, force=False) == []


def test_gate_complains_about_missing_review_and_stale_inputs(tmp_path: Path):
    inp = _touch(tmp_path / "in.txt", "v1")
    out = _touch(tmp_path / "out.bin")
    # No manifest, no sentinel.
    errors = manifest.gate(out, {"in": inp}, auto=False, force=False)
    assert any("stale-input gate" in e for e in errors)
    assert any("review gate" in e for e in errors)
