"""Shared pytest fixtures for the OfficeMapMaker test suite."""

from __future__ import annotations

import pytest


@pytest.fixture(autouse=True, scope="session")
def _redirect_qsettings(tmp_path_factory):
    """Redirect Qt's QSettings to a tmp INI file for the whole session.

    Several wizard tests construct ``MainWindow`` instances; the new
    geometry save/restore wiring writes to ``QSettings`` on
    ``closeEvent``. Without this fixture, those writes would land in
    the developer's real registry / preferences file, leaking test
    state into their actual Office Map Maker configuration. Pointing
    ``QSettings`` at a tmp INI for the test session keeps the suite
    hermetic.

    Scoped session-wide because Qt caches the resolved settings path
    per QApplication; flipping it per-test would not consistently
    take effect.
    """
    from PySide6 import QtCore

    settings_dir = tmp_path_factory.mktemp("qsettings")
    QtCore.QSettings.setPath(
        QtCore.QSettings.Format.IniFormat,
        QtCore.QSettings.Scope.UserScope,
        str(settings_dir),
    )
    QtCore.QSettings.setDefaultFormat(QtCore.QSettings.Format.IniFormat)
    yield
