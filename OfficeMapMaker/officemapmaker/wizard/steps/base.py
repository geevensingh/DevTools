"""Base class for wizard step widgets.

Every step in the W4..W9 series subclasses ``StepBase``. The base
class is intentionally minimal: it provides the lifecycle hooks the
``MainWindow`` calls when the user navigates in and out of the step,
plus a back-reference to the main window so the step can reach the
session, ``run_pipeline_step``, and ``set_step_status``.

Lifecycle:

* ``on_activated()`` -- called by ``MainWindow._activate_step`` each
  time the step becomes the current one. Steps use this to kick off
  their pipeline call on first entry (if no cached result exists in
  the session) or to refresh their UI from the cached result on
  subsequent entries.
* ``on_deactivated()`` -- called by ``MainWindow._activate_step``
  just before the step is hidden. Steps with in-flight pipeline runs
  should NOT cancel them here (the user might be coming right back);
  the wizard serializes runs so the next activation just won't
  re-fire.

Both hooks have no-op defaults so a step that doesn't need them can
omit them entirely.
"""

from __future__ import annotations

from typing import TYPE_CHECKING

from PySide6 import QtWidgets

if TYPE_CHECKING:
    from ..main_window import MainWindow


class StepBase(QtWidgets.QWidget):
    """Common base class for wizard step content widgets."""

    def __init__(self, main_window: "MainWindow") -> None:
        super().__init__()
        self._main_window = main_window

    @property
    def main_window(self) -> "MainWindow":
        return self._main_window

    # ------------------------------------------------------------------
    # Lifecycle hooks (no-op defaults; subclasses override as needed).
    # ------------------------------------------------------------------

    def on_activated(self) -> None:
        """Called when this step becomes the current step.

        Override to kick off a pipeline run on first entry, refresh
        the UI from cached session state, or focus an input.
        """

    def on_deactivated(self) -> None:
        """Called just before this step is hidden by a navigation.

        Override to pause animations, persist transient UI state, or
        flush pending edits. Do NOT cancel in-flight pipeline runs --
        the wizard serializes runs and the user may navigate right
        back to this step.
        """
