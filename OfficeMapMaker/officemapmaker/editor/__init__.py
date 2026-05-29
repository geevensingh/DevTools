"""Interactive calibration editor (PySide6).

The editor is the primary fix-up surface for ``calibration.json``. The CLI
``review`` flow (PDF + hand-editing JSON) remains available for sharing
with teammates, but the interactive editor is what the README points users
at when something needs to change.

The package is intentionally split:

* ``app``        — ``QApplication`` bootstrap and the ``launch()`` entry point
                   used by ``__main__._run_calibrate_edit``.
* ``canvas``     — ``MapCanvas(QGraphicsView)``, pan/zoom, overlay management.
* ``items``      — ``LabelItem`` / ``RoomItem`` ``QGraphicsItem`` subclasses
                   (added in milestone ed2).
* ``sidebar``    — selection-aware inspector dock (added in milestone ed3).
* ``commands``   — ``QUndoCommand`` subclasses (added in milestone ed3).
* ``controller`` — wiring user actions to undoable commands (added in
                   milestone ed3).

Importing this package does **not** import PySide6 — only the submodules do.
That keeps ``import officemapmaker`` cheap for the rest of the CLI, which
must not pay the Qt cost on every invocation.
"""

from __future__ import annotations

__all__ = ["launch"]


def launch(*args, **kwargs):  # type: ignore[no-untyped-def]
    """Thin shim that defers the PySide6 import to call-time.

    Forwards to :func:`officemapmaker.editor.app.launch`. Defined here so
    callers can ``from officemapmaker.editor import launch`` without
    paying the Qt import cost just to read the module-level symbol.
    """
    from .app import launch as _launch

    return _launch(*args, **kwargs)
