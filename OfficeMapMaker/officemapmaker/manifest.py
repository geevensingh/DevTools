"""SHA-manifest + review-sentinel helpers.

Every pass writes a `<output>.manifest.json` recording the SHA-256 of each
input file used to produce that output. The next pass refuses to run if any
declared input's current SHA does not match the recorded value — preventing
stale renders from sneaking through.

Each pass also writes a `<output>.reviewed` sentinel only after the human
runs the corresponding `... confirm` subcommand.
"""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


SHA_PREFIX = "sha256:"


def sha256_file(path: Path) -> str:
    """Return `sha256:<hexdigest>` for the file at `path`."""
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return SHA_PREFIX + h.hexdigest()


def _manifest_path(output: Path) -> Path:
    return output.with_name(output.name + ".manifest.json")


def _sentinel_path(output: Path) -> Path:
    return output.with_name(output.name + ".reviewed")


def write_manifest(output: Path, inputs: dict[str, Path]) -> Path:
    """Record the SHAs of `inputs` alongside `output`."""
    manifest = {name: sha256_file(p) for name, p in sorted(inputs.items())}
    out = _manifest_path(output)
    out.write_text(json.dumps(manifest, indent=2, sort_keys=True))
    return out


@dataclass
class ManifestMismatch:
    """One difference between recorded and current SHAs."""

    name: str
    recorded: str | None
    current: str | None

    def describe(self) -> str:
        if self.recorded is None:
            return f"new input '{self.name}' was not in the manifest"
        if self.current is None:
            return f"input '{self.name}' is missing from disk (was {self.recorded[:18]}…)"
        return (
            f"input '{self.name}' changed: was {self.recorded[7:19]}…, now {self.current[7:19]}…"
        )


def check_manifest(output: Path, inputs: dict[str, Path]) -> list[ManifestMismatch]:
    """Return the list of mismatches between the recorded manifest and current inputs.

    Empty list means "all inputs match the recorded manifest".
    A missing manifest file produces one synthetic mismatch with name='<manifest>'.
    """
    m = _manifest_path(output)
    if not m.exists():
        return [ManifestMismatch(name=str(m.name), recorded=None, current=None)]

    recorded = json.loads(m.read_text())
    mismatches: list[ManifestMismatch] = []
    seen = set()
    for name, path in inputs.items():
        seen.add(name)
        rec = recorded.get(name)
        cur = sha256_file(path) if path.exists() else None
        if rec != cur:
            mismatches.append(ManifestMismatch(name=name, recorded=rec, current=cur))
    for name in recorded.keys() - seen:
        mismatches.append(ManifestMismatch(name=name, recorded=recorded[name], current=None))
    return mismatches


def confirm_review(output: Path) -> Path:
    """Write the `.reviewed` sentinel next to `output`."""
    s = _sentinel_path(output)
    s.write_text("")  # empty marker
    return s


def is_reviewed(output: Path) -> bool:
    """Return True if a `.reviewed` sentinel exists next to `output`."""
    return _sentinel_path(output).exists()


def clear_review(output: Path) -> None:
    """Remove the `.reviewed` sentinel if it exists (call this when output is regenerated)."""
    s = _sentinel_path(output)
    if s.exists():
        s.unlink()


def gate(output: Path, inputs: dict[str, Path], *, auto: bool, force: bool) -> list[str]:
    """Return a list of human-readable gate errors. Empty list means "OK to proceed".

    With `--auto`, skip the review-sentinel check.
    With `--force`, skip both review and SHA checks.
    """
    if force:
        return []

    errors: list[str] = []
    mismatches = check_manifest(output, inputs)
    if mismatches:
        errors.append(
            f"stale-input gate: {output} was produced from different inputs:\n  - "
            + "\n  - ".join(m.describe() for m in mismatches)
            + f"\nRegenerate {output} and retry."
        )

    if not auto and not is_reviewed(output):
        errors.append(
            f"review gate: {output} has no .reviewed sentinel; run the corresponding "
            f"'... confirm' subcommand after eyeballing the review artifact (or pass --auto)."
        )
    return errors


def required_inputs(**paths: Path | str) -> dict[str, Path]:
    """Convenience: build the inputs dict and assert every path exists."""
    out: dict[str, Path] = {}
    for name, p in paths.items():
        path = Path(p) if not isinstance(p, Path) else p
        if not path.exists():
            raise FileNotFoundError(f"required input '{name}' not found: {path}")
        out[name] = path
    return out
