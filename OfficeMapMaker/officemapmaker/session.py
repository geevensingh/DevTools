"""Persisted wizard session: input hashes, per-step status, embedded artifacts.

A ``Session`` is the single source of truth for everything the wizard
produces for a given ``(map, assignments)`` pair. It lives next to the
map at ``<map_basename>.session.json`` and supersedes the previous mix
of ``calibration.json`` + ``layout.json`` + ``.reviewed`` sentinels.

Layout in the JSON file::

    {
      "schema_version": 1,
      "map_path":         "C:/.../Millennium_B_First_Floor_FULL.png",
      "map_hash":         "sha256:...",
      "assignments_path": "C:/.../Millennium_B_Move_May_2026.xlsx",
      "assignments_hash": "sha256:...",
      "teams_path":       null,
      "teams_hash":       null,
      "output_dir":       "C:/.../OfficeMapMaker",
      "current_step":     "calibrate",
      "calibration":      <Calibration.to_dict() | null>,
      "layout":           <Layout.to_dict() | null>,
      "step_state": {
        "calibrate":        {"status": "ok", "issues": [...], "last_run_input_hash": "..."},
        "validate_labels":  {"status": "pending", "issues": [], "last_run_input_hash": null},
        ...
      }
    }

Three load modes (see ``Session.load_or_create``):
  * **fresh** — no session file on disk, all steps ``pending``.
  * **restored** — file exists and every input hash still matches the
    file on disk; status/issues come back exactly as last saved.
  * **mismatched** — at least one input file has changed since the
    session was saved. The caller (``MainWindow``) is responsible for
    showing a prompt and either calling ``Session.start_over()`` or
    ``Session.invalidate_changed(...)`` (fine-grained per-step reset
    driven by ``_STEP_DEPENDENCIES``).
"""

from __future__ import annotations

import hashlib
import json
from dataclasses import dataclass, field
from enum import Enum
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from .calibration import Calibration, compute_map_hash
from .layout import Layout

SCHEMA_VERSION = 1

# Canonical step order. MUST match wizard.main_window._STEPS.
STEP_IDS: Tuple[str, ...] = (
    "calibrate",
    "validate_labels",
    "validate_fill",
    "layout",
    "build",
    "tile",
)


# Per-step input dependencies, used to compute ``last_run_input_hash``
# and to drive fine-grained invalidation when a single input file
# changes. Keys are step ids; values are the set of input "kinds" the
# step depends on.
#
# Kinds are abstract — ``"calibration"`` and ``"layout"`` are
# *artifact* dependencies (not raw input files), so they hash whatever
# is currently in ``Session.calibration`` / ``Session.layout``. This
# way, re-running ``calibrate`` automatically invalidates every
# downstream step, even if the raw map didn't change.
_STEP_DEPENDENCIES: Dict[str, Tuple[str, ...]] = {
    "calibrate":       ("map",),
    "validate_labels": ("calibration", "assignments"),
    "validate_fill":   ("map", "calibration"),
    "layout":          ("calibration", "assignments"),
    "build":           ("map", "calibration", "layout", "assignments", "teams"),
    "tile":            ("map", "calibration", "layout", "assignments", "teams"),
}


# ---------------------------------------------------------------------------
# Status enums + small dataclasses
# ---------------------------------------------------------------------------


class StepStatus(str, Enum):
    """Mirrors wizard.main_window.StepStatus but lives in the model layer.

    Kept as a ``str`` enum so JSON round-trips without a custom encoder.
    """

    PENDING = "pending"
    RUNNING = "running"
    OK = "ok"
    WARNING = "warning"
    ERROR = "error"
    INFO = "info"


@dataclass
class Issue:
    """A single warning/error/info surfaced by a pipeline step."""

    code: str
    severity: str  # "error" | "warning" | "info"
    message: str
    room_id: Optional[str] = None
    suggestion: Optional[Dict[str, Any]] = None  # e.g. {"wall_patches": [...]}
    acked: bool = False  # user explicitly ignored this one

    def to_dict(self) -> Dict[str, Any]:
        return {
            "code": self.code,
            "severity": self.severity,
            "message": self.message,
            "room_id": self.room_id,
            "suggestion": self.suggestion,
            "acked": self.acked,
        }

    @classmethod
    def from_dict(cls, d: Dict[str, Any]) -> "Issue":
        return cls(
            code=str(d.get("code", "")),
            severity=str(d.get("severity", "warning")),
            message=str(d.get("message", "")),
            room_id=d.get("room_id"),
            suggestion=d.get("suggestion"),
            acked=bool(d.get("acked", False)),
        )


@dataclass
class StepState:
    """Per-step run state.

    ``last_run_input_hash`` records the hash of all inputs the step
    consumed last time it produced a result. If the current input hash
    no longer matches, the step's status is reset to ``PENDING`` and
    its issues cleared (see ``Session.invalidate_changed``).
    """

    status: StepStatus = StepStatus.PENDING
    issues: List[Issue] = field(default_factory=list)
    last_run_input_hash: Optional[str] = None

    def to_dict(self) -> Dict[str, Any]:
        return {
            "status": self.status.value,
            "issues": [i.to_dict() for i in self.issues],
            "last_run_input_hash": self.last_run_input_hash,
        }

    @classmethod
    def from_dict(cls, d: Dict[str, Any]) -> "StepState":
        raw_status = str(d.get("status", "pending"))
        try:
            status = StepStatus(raw_status)
        except ValueError:
            status = StepStatus.PENDING
        return cls(
            status=status,
            issues=[Issue.from_dict(x) for x in d.get("issues", [])],
            last_run_input_hash=d.get("last_run_input_hash"),
        )


@dataclass
class LoadResult:
    """Return value of ``Session.load_or_create``.

    ``mode`` is one of:
      * ``"fresh"``     — no session file existed.
      * ``"restored"``  — file existed; every input hash matched.
      * ``"mismatched"``— file existed; ``changed_inputs`` lists which
        raw input files (``"map"``, ``"assignments"``, ``"teams"``)
        differ from the saved hashes. ``MainWindow`` must prompt and
        then call ``session.start_over()`` or
        ``session.invalidate_changed(changed_inputs)``.
    """

    session: "Session"
    mode: str
    changed_inputs: Tuple[str, ...] = ()


# ---------------------------------------------------------------------------
# Hash helpers
# ---------------------------------------------------------------------------


def compute_file_hash(path: Path) -> str:
    """Return ``"sha256:<hex>"`` for any file (used for assignments/teams)."""
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(65536), b""):
            h.update(chunk)
    return f"sha256:{h.hexdigest()}"


def _hash_artifact(obj: Optional[Any]) -> str:
    """Stable hash of a calibration/layout dataclass via its JSON form."""
    if obj is None:
        return "none"
    payload = json.dumps(obj.to_dict(), sort_keys=True).encode("utf-8")
    return "sha256:" + hashlib.sha256(payload).hexdigest()


# ---------------------------------------------------------------------------
# The session
# ---------------------------------------------------------------------------


@dataclass
class Session:
    """Persisted wizard state for one ``(map, assignments)`` pair."""

    # Inputs (paths are absolute; hashes are recomputed on load).
    map_path: Path
    map_hash: str
    assignments_path: Path
    assignments_hash: str
    output_dir: Path
    teams_path: Optional[Path] = None
    teams_hash: Optional[str] = None

    # Wizard state.
    current_step: str = "calibrate"
    step_state: Dict[str, StepState] = field(default_factory=dict)

    # Embedded artifacts (full Calibration / Layout, not paths).
    calibration: Optional[Calibration] = None
    layout: Optional[Layout] = None

    # Bookkeeping.
    schema_version: int = SCHEMA_VERSION

    # Where on disk this lives (set by load_or_create / save).
    _path: Optional[Path] = field(default=None, repr=False, compare=False)

    # ------------------------------------------------------------------
    # Construction
    # ------------------------------------------------------------------

    def __post_init__(self) -> None:
        # Ensure every step has a StepState entry, even if the on-disk
        # file was authored against an older STEP_IDS list.
        for step_id in STEP_IDS:
            self.step_state.setdefault(step_id, StepState())
        if self.current_step not in STEP_IDS:
            self.current_step = STEP_IDS[0]

    @classmethod
    def session_path_for(cls, map_path: Path, output_dir: Path) -> Path:
        """Where the session file lives for a given map.

        Always inside ``output_dir`` (which defaults to the map's
        directory in the launcher), named after the map. Decision Q2:
        next to the map.
        """
        return output_dir / f"{map_path.stem}.session.json"

    @classmethod
    def load_or_create(
        cls,
        *,
        map_path: Path,
        assignments_path: Path,
        output_dir: Path,
        teams_path: Optional[Path] = None,
    ) -> LoadResult:
        """Load the session from disk if present, else create a fresh one.

        Detects input-file changes and returns them in ``LoadResult``;
        the caller decides how to react (full reset vs per-step reset).
        Always recomputes input hashes from the on-disk file — the
        stored hash is only used for change detection, never as the
        authoritative value.
        """
        map_path = map_path.resolve()
        assignments_path = assignments_path.resolve()
        output_dir = output_dir.resolve()
        teams_path = teams_path.resolve() if teams_path else None

        current_hashes = {
            "map": compute_map_hash(map_path),
            "assignments": compute_file_hash(assignments_path),
            "teams": compute_file_hash(teams_path) if teams_path else None,
        }

        session_path = cls.session_path_for(map_path, output_dir)
        if not session_path.exists():
            sess = cls(
                map_path=map_path,
                map_hash=current_hashes["map"],
                assignments_path=assignments_path,
                assignments_hash=current_hashes["assignments"],
                teams_path=teams_path,
                teams_hash=current_hashes["teams"],
                output_dir=output_dir,
            )
            sess._path = session_path
            return LoadResult(session=sess, mode="fresh")

        # File exists — try to parse it. On any parse error we fall
        # back to a fresh session rather than crashing.
        try:
            raw = json.loads(session_path.read_text(encoding="utf-8"))
            sess = cls._from_dict(raw)
        except (json.JSONDecodeError, KeyError, ValueError, TypeError):
            sess = cls(
                map_path=map_path,
                map_hash=current_hashes["map"],
                assignments_path=assignments_path,
                assignments_hash=current_hashes["assignments"],
                teams_path=teams_path,
                teams_hash=current_hashes["teams"],
                output_dir=output_dir,
            )
            sess._path = session_path
            return LoadResult(session=sess, mode="fresh")

        sess._path = session_path

        # Detect which raw inputs changed. We update the in-memory
        # ``Session`` with the *current* paths and hashes either way —
        # the caller decides whether to reset step state.
        changed: List[str] = []
        if sess.map_hash != current_hashes["map"]:
            changed.append("map")
        if sess.assignments_hash != current_hashes["assignments"]:
            changed.append("assignments")
        if sess.teams_hash != current_hashes["teams"]:
            changed.append("teams")

        # Caller might have re-pointed to a different file path entirely
        # (e.g. moved the spreadsheet). Treat that as a change too.
        if sess.map_path != map_path:
            if "map" not in changed:
                changed.append("map")
        if sess.assignments_path != assignments_path:
            if "assignments" not in changed:
                changed.append("assignments")
        if sess.teams_path != teams_path:
            if "teams" not in changed:
                changed.append("teams")

        sess.map_path = map_path
        sess.map_hash = current_hashes["map"]
        sess.assignments_path = assignments_path
        sess.assignments_hash = current_hashes["assignments"]
        sess.teams_path = teams_path
        sess.teams_hash = current_hashes["teams"]
        sess.output_dir = output_dir

        mode = "restored" if not changed else "mismatched"
        return LoadResult(session=sess, mode=mode, changed_inputs=tuple(changed))

    # ------------------------------------------------------------------
    # Per-step mutation
    # ------------------------------------------------------------------

    def set_step(
        self,
        step_id: str,
        *,
        status: Optional[StepStatus] = None,
        issues: Optional[List[Issue]] = None,
    ) -> None:
        """Update one step's status/issues; stamps ``last_run_input_hash``
        whenever the step transitions out of ``PENDING``/``RUNNING``."""
        if step_id not in STEP_IDS:
            raise ValueError(f"unknown step id: {step_id!r}")
        st = self.step_state[step_id]
        if status is not None:
            st.status = status
        if issues is not None:
            st.issues = list(issues)
        if status in (StepStatus.OK, StepStatus.WARNING, StepStatus.ERROR, StepStatus.INFO):
            st.last_run_input_hash = self.compute_inputs_hash(step_id)

    def invalidate_from(self, step_id: str) -> None:
        """Reset everything from ``step_id`` (inclusive) forward to PENDING.

        Used when the user jumps back to a previous step. The Calibration
        and Layout objects are *not* dropped here — they're kept so the
        user can keep editing them. Re-running the step will overwrite.
        """
        idx = STEP_IDS.index(step_id)
        for sid in STEP_IDS[idx:]:
            self.step_state[sid] = StepState()

    def invalidate_changed(self, changed_inputs: Tuple[str, ...]) -> List[str]:
        """Mark every step whose deps include one of ``changed_inputs``
        as ``PENDING``; drop their issues. Returns the list of step ids
        that were reset, in canonical order.

        Cascades: a step that depends on the *artifact* of another step
        (calibration/layout) is invalidated when that producing step is
        invalidated. We compute the fixed point so a single map change
        cascades cleanly to every downstream step that depends on the
        now-dropped calibration.

        Also drops the embedded ``calibration`` / ``layout`` artifacts
        when their producing step is invalidated (the artifact is now
        stale and would otherwise mislead downstream steps).
        """
        invalidated: set = set()
        # Map from "kind" → producing step id.
        artifact_producer = {"calibration": "calibrate", "layout": "layout"}

        # Phase 1: direct raw-input invalidation.
        changed_set = set(changed_inputs)
        for step_id in STEP_IDS:
            raw_deps = {
                k for k in _STEP_DEPENDENCIES[step_id] if k not in artifact_producer
            }
            if raw_deps & changed_set:
                invalidated.add(step_id)

        # Phase 2: fixed-point cascade through artifact deps. A step
        # gets invalidated if any of its artifact deps' producers are
        # already invalidated.
        changed = True
        while changed:
            changed = False
            for step_id in STEP_IDS:
                if step_id in invalidated:
                    continue
                artifact_deps = [
                    k for k in _STEP_DEPENDENCIES[step_id] if k in artifact_producer
                ]
                for k in artifact_deps:
                    if artifact_producer[k] in invalidated:
                        invalidated.add(step_id)
                        changed = True
                        break

        # Apply the resets in canonical order so the returned list is
        # stable.
        reset: List[str] = []
        for step_id in STEP_IDS:
            if step_id in invalidated:
                self.step_state[step_id] = StepState()
                reset.append(step_id)
        if "calibrate" in reset:
            self.calibration = None
        if "layout" in reset:
            self.layout = None
        return reset

    def start_over(self) -> None:
        """Drop every cached result + step status — used when the user
        chooses "Start over" after a mismatch prompt."""
        self.calibration = None
        self.layout = None
        for sid in STEP_IDS:
            self.step_state[sid] = StepState()
        self.current_step = STEP_IDS[0]

    # ------------------------------------------------------------------
    # Hashing
    # ------------------------------------------------------------------

    def compute_inputs_hash(self, step_id: str) -> str:
        """Hash of every input that ``step_id`` depends on.

        Used as ``last_run_input_hash`` — letting later code (W3+) tell
        "is this step's result still good?" without re-running it.
        """
        parts: List[str] = []
        for kind in _STEP_DEPENDENCIES[step_id]:
            if kind == "map":
                parts.append(f"map={self.map_hash}")
            elif kind == "assignments":
                parts.append(f"assignments={self.assignments_hash}")
            elif kind == "teams":
                parts.append(f"teams={self.teams_hash or 'none'}")
            elif kind == "calibration":
                parts.append(f"calibration={_hash_artifact(self.calibration)}")
            elif kind == "layout":
                parts.append(f"layout={_hash_artifact(self.layout)}")
        joined = "|".join(parts).encode("utf-8")
        return "sha256:" + hashlib.sha256(joined).hexdigest()

    # ------------------------------------------------------------------
    # Persistence
    # ------------------------------------------------------------------

    def save(self, *, path: Optional[Path] = None) -> Path:
        """Write to disk atomically (temp file + rename).

        If ``path`` is omitted, uses the path the session was loaded
        from / created at.
        """
        target = path or self._path
        if target is None:
            raise RuntimeError("Session has no path; pass path= to save().")
        target = Path(target)
        target.parent.mkdir(parents=True, exist_ok=True)
        tmp = target.with_suffix(target.suffix + ".tmp")
        tmp.write_text(
            json.dumps(self._to_dict(), indent=2, sort_keys=False),
            encoding="utf-8",
        )
        # ``Path.replace`` is atomic on Windows and POSIX.
        tmp.replace(target)
        self._path = target
        return target

    def delete(self) -> None:
        """Remove the on-disk session file (used by tests + "start over"
        if we ever want to free disk space). No-op if missing."""
        if self._path and self._path.exists():
            self._path.unlink()

    # ------------------------------------------------------------------
    # Serialization
    # ------------------------------------------------------------------

    def _to_dict(self) -> Dict[str, Any]:
        return {
            "schema_version": self.schema_version,
            "map_path": str(self.map_path),
            "map_hash": self.map_hash,
            "assignments_path": str(self.assignments_path),
            "assignments_hash": self.assignments_hash,
            "teams_path": str(self.teams_path) if self.teams_path else None,
            "teams_hash": self.teams_hash,
            "output_dir": str(self.output_dir),
            "current_step": self.current_step,
            "calibration": self.calibration.to_dict() if self.calibration else None,
            "layout": self.layout.to_dict() if self.layout else None,
            "step_state": {sid: self.step_state[sid].to_dict() for sid in STEP_IDS},
        }

    @classmethod
    def _from_dict(cls, d: Dict[str, Any]) -> "Session":
        schema = int(d.get("schema_version", 1))
        if schema != SCHEMA_VERSION:
            # Future-proofing: if we bump the schema version, future
            # code can migrate here. For now any mismatch is an error.
            raise ValueError(
                f"unsupported session schema_version={schema} "
                f"(this build expects {SCHEMA_VERSION})"
            )
        teams_raw = d.get("teams_path")
        cal_raw = d.get("calibration")
        layout_raw = d.get("layout")
        step_state_raw = d.get("step_state", {})
        sess = cls(
            map_path=Path(d["map_path"]),
            map_hash=str(d["map_hash"]),
            assignments_path=Path(d["assignments_path"]),
            assignments_hash=str(d["assignments_hash"]),
            teams_path=Path(teams_raw) if teams_raw else None,
            teams_hash=d.get("teams_hash"),
            output_dir=Path(d["output_dir"]),
            current_step=str(d.get("current_step", STEP_IDS[0])),
            step_state={
                sid: StepState.from_dict(step_state_raw[sid])
                for sid in step_state_raw
                if sid in STEP_IDS
            },
            calibration=Calibration.from_dict(cal_raw) if cal_raw else None,
            layout=Layout.from_dict(layout_raw) if layout_raw else None,
        )
        return sess


__all__ = [
    "Issue",
    "LoadResult",
    "SCHEMA_VERSION",
    "STEP_IDS",
    "Session",
    "StepState",
    "StepStatus",
    "compute_file_hash",
]
