"""Tests for ``officemapmaker.pipeline.runner``.

Covers both the blocking test helper (run_blocking) and the real
QThread-backed runner. The QThread tests need a QApplication; they
process events via a local event loop and a watchdog timer rather
than waiting on QThread.wait() in isolation, so they remain
deterministic even when run headless.
"""

from __future__ import annotations

import threading
import time

import pytest

pytest.importorskip("PySide6")

from PySide6 import QtCore, QtWidgets  # noqa: E402

from officemapmaker.pipeline import PipelineCanceled, PipelineRunner  # noqa: E402


@pytest.fixture(scope="module")
def qapp():
    import os

    os.environ.setdefault("QT_QPA_PLATFORM", "offscreen")
    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication([])
    yield app


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------


def _drain_until(
    predicate, *, timeout_s: float = 5.0, tick_ms: int = 10
) -> bool:
    """Spin Qt's event loop until ``predicate()`` is true or timeout."""
    deadline = time.monotonic() + timeout_s
    app = QtWidgets.QApplication.instance()
    assert app is not None, "qapp fixture missing?"
    while time.monotonic() < deadline:
        app.processEvents(QtCore.QEventLoop.ProcessEventsFlag.AllEvents, tick_ms)
        if predicate():
            return True
    return False


# ---------------------------------------------------------------------------
# run_blocking (no QThread)
# ---------------------------------------------------------------------------


def test_run_blocking_returns_tuple_unchanged():
    def f(*, progress_cb, cancel_cb):
        return "result", ["issue1", "issue2"]

    out = PipelineRunner.run_blocking(f)
    assert out == ("result", ["issue1", "issue2"])


def test_run_blocking_passes_args_and_kwargs():
    def f(a, b, *, k, progress_cb, cancel_cb):
        return (a, b, k), []

    out = PipelineRunner.run_blocking(f, args=(1, 2), kwargs={"k": "v"})
    assert out == ((1, 2, "v"), [])


def test_run_blocking_records_progress_and_clamps():
    records: list = []

    def f(*, progress_cb, cancel_cb):
        progress_cb(-0.5, "below zero")
        progress_cb(0.5, "halfway")
        progress_cb(2.0, "over the top")
        return None, []

    PipelineRunner.run_blocking(f, progress_records=records)
    assert records == [(0.0, "below zero"), (0.5, "halfway"), (1.0, "over the top")]


def test_run_blocking_cancel_event_visible_to_callable():
    ev = threading.Event()

    def f(*, progress_cb, cancel_cb):
        assert cancel_cb() is False
        ev.set()
        assert cancel_cb() is True
        raise PipelineCanceled()

    with pytest.raises(PipelineCanceled):
        PipelineRunner.run_blocking(f, cancel_event=ev)


def test_run_blocking_rejects_non_tuple_return():
    def f(*, progress_cb, cancel_cb):
        return "just a string"

    with pytest.raises(TypeError, match="expected a .result, issues. tuple"):
        PipelineRunner.run_blocking(f)


def test_run_blocking_propagates_arbitrary_exceptions():
    def f(*, progress_cb, cancel_cb):
        raise ValueError("boom")

    with pytest.raises(ValueError, match="boom"):
        PipelineRunner.run_blocking(f)


# ---------------------------------------------------------------------------
# Real QThread-backed runner
# ---------------------------------------------------------------------------


def test_runner_emits_finished_with_result_and_issues(qapp):
    def f(*, progress_cb, cancel_cb):
        progress_cb(0.5, "halfway")
        return {"calibration": 1}, ["only-issue"]

    captured = {}

    runner = PipelineRunner(f)
    runner.finished.connect(
        lambda res, iss: captured.update(result=res, issues=iss)
    )
    progress_records: list = []
    runner.progress.connect(
        lambda frac, msg: progress_records.append((frac, msg))
    )
    runner.start()

    assert _drain_until(lambda: "result" in captured)
    runner.wait(2000)
    # is_running flips false in a queued slot on the main thread.
    _drain_until(lambda: not runner.is_running())

    assert captured["result"] == {"calibration": 1}
    assert captured["issues"] == ["only-issue"]
    assert (0.5, "halfway") in progress_records
    assert runner.is_running() is False


def test_runner_emits_canceled_when_callable_raises_pipeline_canceled(qapp):
    # Use a barrier so the callable is definitely running before cancel().
    started = threading.Event()
    released = threading.Event()

    def f(*, progress_cb, cancel_cb):
        started.set()
        # Wait until the test fires cancel() so we exercise the
        # mid-flight cancel path, not the "canceled before start" path.
        released.wait(timeout=5)
        if cancel_cb():
            raise PipelineCanceled()
        return None, []

    flags = {"canceled": False, "finished": False, "failed": False}
    runner = PipelineRunner(f)
    runner.canceled.connect(lambda: flags.update(canceled=True))
    runner.finished.connect(lambda *_: flags.update(finished=True))
    runner.failed.connect(lambda *_: flags.update(failed=True))
    runner.start()

    # Wait until the worker is inside the callable, then cancel.
    assert started.wait(timeout=2), "worker never entered the callable"
    runner.cancel()
    released.set()

    assert _drain_until(lambda: flags["canceled"] or flags["failed"])
    runner.wait(2000)
    assert flags == {"canceled": True, "finished": False, "failed": False}


def test_runner_emits_failed_on_exception_and_attaches_traceback(qapp):
    def f(*, progress_cb, cancel_cb):
        raise RuntimeError("kaboom")

    captured = {}
    runner = PipelineRunner(f)
    runner.failed.connect(lambda exc: captured.update(exc=exc))
    runner.start()

    assert _drain_until(lambda: "exc" in captured)
    runner.wait(2000)

    exc = captured["exc"]
    assert isinstance(exc, RuntimeError)
    assert "kaboom" in str(exc)
    assert hasattr(exc, "traceback")
    assert "kaboom" in exc.traceback


def test_runner_emits_failed_when_callable_returns_non_tuple(qapp):
    def f(*, progress_cb, cancel_cb):
        return "not a tuple"

    captured = {}
    runner = PipelineRunner(f)
    runner.failed.connect(lambda exc: captured.update(exc=exc))
    runner.start()

    assert _drain_until(lambda: "exc" in captured)
    runner.wait(2000)
    assert isinstance(captured["exc"], TypeError)


def test_runner_start_is_single_shot(qapp):
    captured = {}

    def f(*, progress_cb, cancel_cb):
        return None, []

    runner = PipelineRunner(f)
    runner.finished.connect(lambda *_: captured.setdefault("done", True))
    runner.start()
    with pytest.raises(RuntimeError, match="start.* more than once"):
        runner.start()
    # Drain so the first run completes cleanly before the runner goes
    # out of scope (otherwise the still-running QThread can outlive
    # its Python wrapper and crash on teardown).
    assert _drain_until(lambda: "done" in captured)
    runner.wait(2000)
    _drain_until(lambda: not runner.is_running())


def test_runner_cancel_before_start_still_aborts(qapp):
    def f(*, progress_cb, cancel_cb):
        if cancel_cb():
            raise PipelineCanceled()
        return None, []

    flags = {"canceled": False, "finished": False}
    runner = PipelineRunner(f)
    runner.cancel()  # set the event before start
    runner.canceled.connect(lambda: flags.update(canceled=True))
    runner.finished.connect(lambda *_: flags.update(finished=True))
    runner.start()

    assert _drain_until(lambda: flags["canceled"] or flags["finished"])
    runner.wait(2000)
    assert flags == {"canceled": True, "finished": False}


def test_runner_passes_args_and_kwargs(qapp):
    def f(a, b, *, k, progress_cb, cancel_cb):
        return (a, b, k), []

    captured = {}
    runner = PipelineRunner(f, args=(1, 2), kwargs={"k": "v"})
    runner.finished.connect(
        lambda res, iss: captured.update(result=res)
    )
    runner.start()
    assert _drain_until(lambda: "result" in captured)
    runner.wait(2000)
    assert captured["result"] == (1, 2, "v")
