"""Pipeline runner: runs long pipeline calls off the UI thread.

The wizard's per-step actions (calibrate, validate, layout, build, tile)
can each take seconds to minutes. Running them on the UI thread would
freeze the window. This package provides a small ``PipelineRunner``
that wraps a single callable in a worker ``QThread`` and exposes Qt
signals for progress / completion / failure / cancellation, plus
optional ``progress_cb`` and ``cancel_cb`` parameters that the wrapped
callable can poll.

See ``runner.py`` for the implementation and ``plan.md`` section 14.9
(W3) for the design notes.
"""

from .runner import PipelineCanceled, PipelineRunner  # noqa: F401

__all__ = ["PipelineCanceled", "PipelineRunner"]
