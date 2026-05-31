"""Background pipeline runner built on ``QThread``.

The wizard's expensive operations (OCR + room detection in calibrate,
flood-fill in validate-fill, layout planner, full-res composite render,
tile + PDF generation) all need to run off the UI thread so the window
stays responsive and the user can cancel a runaway job.

``PipelineRunner`` is intentionally tiny: it takes one callable, spawns
one worker thread, and emits signals for progress / done / failed /
canceled. The wrapped callable receives ``progress_cb`` and
``cancel_cb`` keyword arguments so it can report progress and
cooperatively bail out when the user clicks Cancel.

Contract for the wrapped callable
---------------------------------
- Signature: ``func(*args, progress_cb=..., cancel_cb=..., **kwargs) -> (result, issues)``
- ``progress_cb(fraction: float, message: str)``: optional. The callable
  may call this any number of times. ``fraction`` is clamped to
  ``[0.0, 1.0]``. The runner forwards the value (debounced by Qt's
  queued-connection delivery) to its ``progress`` signal.
- ``cancel_cb() -> bool``: optional. Returns ``True`` once the user
  requested cancellation. The callable is expected to check this
  between phases and raise :class:`PipelineCanceled` to abort.
- Return value: a 2-tuple ``(result, issues)``. ``result`` is whatever
  the step produces (a ``Calibration`` for calibrate, a ``Layout`` for
  layout, etc). ``issues`` is a list of issue dataclasses (typically
  ``CalibrationIssue`` or similar -- the runner doesn't introspect it,
  it just forwards it to ``finished``).

Signals
-------
- ``progress(float, str)`` -- emitted from the runner thread (Qt routes
  to the UI thread automatically).
- ``finished(object, object)`` -- ``(result, issues)`` on normal exit.
- ``canceled()`` -- emitted instead of ``finished`` when the callable
  raises ``PipelineCanceled``.
- ``failed(object)`` -- the raised exception instance for any other
  uncaught error. The runner attaches a ``.traceback`` string
  attribute so callers can display a stack trace without needing
  ``sys.exc_info``.

Thread model
------------
- One ``PipelineRunner`` owns one ``QThread`` and one wrapped
  callable. The runner is single-shot: after ``start()`` and a
  terminal signal (``finished`` / ``canceled`` / ``failed``), the
  worker thread is finished and ``start()`` cannot be called again.
- Subsequent runs require a fresh ``PipelineRunner`` instance.
- The MainWindow is expected to serialize runs (one pipeline step
  active at a time) -- this matches the wizard's linear, one-step-at-
  a-time UX. Parallel runs are not supported.

Test helpers
------------
- ``PipelineRunner.run_blocking(func, *, args=(), kwargs=None)`` runs
  the wrapped callable on the calling thread without spinning a
  worker. Useful for unit tests that don't want to start a
  ``QApplication`` event loop.
"""

from __future__ import annotations

import threading
import traceback
from typing import Any, Callable, Optional, Tuple

from PySide6 import QtCore


class PipelineCanceled(Exception):
    """Raised by a pipeline callable when ``cancel_cb()`` returns True.

    Catching this is the runner's responsibility -- pipeline callables
    just raise it and unwind. The runner translates the exception into
    a ``canceled`` signal (not a ``failed`` signal).
    """


class _Worker(QtCore.QObject):
    """Internal QObject that lives on the worker thread.

    The actual function call happens in :meth:`run`. We separate the
    worker from the ``PipelineRunner`` so the runner stays on the UI
    thread (where its signals can be safely connected) while only this
    small object is moved onto the worker thread.
    """

    # Re-emitted by the runner; using direct signals here means
    # auto-connection across threads turns into QueuedConnection
    # (which is what we want -- UI slots run on the UI thread).
    progress = QtCore.Signal(float, str)
    finished = QtCore.Signal(object, object)
    failed = QtCore.Signal(object)
    canceled = QtCore.Signal()

    def __init__(
        self,
        func: Callable[..., Tuple[Any, Any]],
        args: tuple,
        kwargs: dict,
        cancel_event: threading.Event,
    ) -> None:
        super().__init__()
        self._func = func
        self._args = args
        self._kwargs = kwargs
        self._cancel_event = cancel_event

    @QtCore.Slot()
    def run(self) -> None:
        """Invoke the wrapped callable; emit one terminal signal."""

        def progress_cb(fraction: float, message: str = "") -> None:
            # Clamp so misbehaving callables can't make the UI's
            # progress bar jump outside 0..1.
            if fraction < 0.0:
                fraction = 0.0
            elif fraction > 1.0:
                fraction = 1.0
            self.progress.emit(float(fraction), str(message))

        def cancel_cb() -> bool:
            return self._cancel_event.is_set()

        kwargs = dict(self._kwargs)
        kwargs["progress_cb"] = progress_cb
        kwargs["cancel_cb"] = cancel_cb

        try:
            result = self._func(*self._args, **kwargs)
        except PipelineCanceled:
            self.canceled.emit()
            return
        except BaseException as exc:  # noqa: BLE001 -- we forward it
            try:
                exc.traceback = traceback.format_exc()  # type: ignore[attr-defined]
            except Exception:
                pass
            self.failed.emit(exc)
            return

        if not (isinstance(result, tuple) and len(result) == 2):
            exc = TypeError(
                f"pipeline callable {self._func!r} returned "
                f"{type(result).__name__}; expected a (result, issues) tuple"
            )
            exc.traceback = "".join(  # type: ignore[attr-defined]
                traceback.format_stack()
            )
            self.failed.emit(exc)
            return

        self.finished.emit(result[0], result[1])


class PipelineRunner(QtCore.QObject):
    """Single-shot pipeline runner.

    Typical use::

        runner = PipelineRunner(calibrate_map, args=(map_path,))
        runner.progress.connect(self._on_progress)
        runner.finished.connect(self._on_finished)
        runner.canceled.connect(self._on_canceled)
        runner.failed.connect(self._on_failed)
        runner.start()
        # ... later, when the user clicks Cancel:
        runner.cancel()

    The runner is single-shot. After a terminal signal fires, the
    worker thread is shut down; create a fresh instance for another
    run.
    """

    progress = QtCore.Signal(float, str)
    finished = QtCore.Signal(object, object)
    failed = QtCore.Signal(object)
    canceled = QtCore.Signal()

    def __init__(
        self,
        func: Callable[..., Tuple[Any, Any]],
        *,
        args: tuple = (),
        kwargs: Optional[dict] = None,
        parent: Optional[QtCore.QObject] = None,
    ) -> None:
        super().__init__(parent)
        self._func = func
        self._args = args
        self._kwargs = kwargs or {}
        self._cancel_event = threading.Event()
        self._thread: Optional[QtCore.QThread] = None
        self._worker: Optional[_Worker] = None
        self._started = False
        self._terminated = False

    # ------------------------------------------------------------------
    # Lifecycle
    # ------------------------------------------------------------------

    def start(self) -> None:
        """Spawn the worker thread and begin running the callable.

        Raises ``RuntimeError`` if called more than once.
        """
        if self._started:
            raise RuntimeError("PipelineRunner.start() called more than once")
        self._started = True

        self._thread = QtCore.QThread()
        self._worker = _Worker(
            func=self._func,
            args=self._args,
            kwargs=self._kwargs,
            cancel_event=self._cancel_event,
        )
        self._worker.moveToThread(self._thread)

        # Re-emit worker signals from this QObject so callers can
        # connect once to a stable object that outlives the worker.
        self._worker.progress.connect(self.progress)
        self._worker.finished.connect(self._on_worker_finished)
        self._worker.canceled.connect(self._on_worker_canceled)
        self._worker.failed.connect(self._on_worker_failed)

        # Kick off run() once the thread's event loop is up.
        self._thread.started.connect(self._worker.run)
        # Once any terminal signal fires we quit the thread's event
        # loop; cleanup happens in _on_thread_finished.
        self._thread.finished.connect(self._on_thread_finished)
        self._thread.start()

    def cancel(self) -> None:
        """Request cancellation.

        The callable is expected to check ``cancel_cb()`` periodically
        and raise :class:`PipelineCanceled`. Setting the event has no
        effect on a callable that ignores ``cancel_cb``.

        Safe to call even if the runner already finished -- subsequent
        calls are no-ops.
        """
        self._cancel_event.set()

    def is_cancel_requested(self) -> bool:
        return self._cancel_event.is_set()

    def is_running(self) -> bool:
        return self._started and not self._terminated

    def wait(self, timeout_ms: int = 10_000) -> bool:
        """Block until the worker thread exits.

        Returns True if the thread finished, False on timeout. Intended
        mostly for tests -- production code should drive the
        ``finished`` / ``canceled`` / ``failed`` signals instead.
        """
        if self._thread is None:
            return True
        return self._thread.wait(timeout_ms)

    # ------------------------------------------------------------------
    # Worker callbacks
    # ------------------------------------------------------------------

    def _on_worker_finished(self, result: Any, issues: Any) -> None:
        self.finished.emit(result, issues)
        if self._thread is not None:
            self._thread.quit()

    def _on_worker_canceled(self) -> None:
        self.canceled.emit()
        if self._thread is not None:
            self._thread.quit()

    def _on_worker_failed(self, exc: Any) -> None:
        self.failed.emit(exc)
        if self._thread is not None:
            self._thread.quit()

    def _on_thread_finished(self) -> None:
        # Mark the runner as terminated. We deliberately do NOT call
        # deleteLater() here: the runner is single-shot and owns its
        # thread + worker for its own lifetime. Calling deleteLater
        # makes wait() and other introspection racy (the C++ object
        # can be torn down by the next event-loop spin before the
        # caller has a chance to inspect it).
        self._terminated = True

    # ------------------------------------------------------------------
    # Test helper
    # ------------------------------------------------------------------

    @staticmethod
    def run_blocking(
        func: Callable[..., Tuple[Any, Any]],
        *,
        args: tuple = (),
        kwargs: Optional[dict] = None,
        cancel_event: Optional[threading.Event] = None,
        progress_records: Optional[list] = None,
    ) -> Tuple[Any, Any]:
        """Run ``func`` on the calling thread, mimicking the worker.

        Intended for unit tests that don't want to start a Qt event
        loop. Behaves like the worker:

        - injects ``progress_cb`` / ``cancel_cb`` kwargs;
        - clamps progress fractions to ``[0, 1]``;
        - lets ``PipelineCanceled`` bubble out unchanged (caller can
          assert with ``pytest.raises``);
        - lets any other exception bubble out unchanged.

        Args:
            func: the callable to invoke.
            args / kwargs: passed through to ``func``.
            cancel_event: optional ``threading.Event``; if set before
                or during the call, ``cancel_cb()`` returns True.
            progress_records: optional list -- every progress callback
                appends a ``(fraction, message)`` tuple to it.

        Returns:
            The 2-tuple returned by ``func``.

        Raises:
            TypeError: if ``func`` returns a non-2-tuple value.
        """
        ev = cancel_event or threading.Event()
        kwargs = dict(kwargs or {})

        def progress_cb(fraction: float, message: str = "") -> None:
            if fraction < 0.0:
                fraction = 0.0
            elif fraction > 1.0:
                fraction = 1.0
            if progress_records is not None:
                progress_records.append((float(fraction), str(message)))

        def cancel_cb() -> bool:
            return ev.is_set()

        kwargs["progress_cb"] = progress_cb
        kwargs["cancel_cb"] = cancel_cb

        result = func(*args, **kwargs)
        if not (isinstance(result, tuple) and len(result) == 2):
            raise TypeError(
                f"pipeline callable {func!r} returned "
                f"{type(result).__name__}; expected a (result, issues) tuple"
            )
        return result
