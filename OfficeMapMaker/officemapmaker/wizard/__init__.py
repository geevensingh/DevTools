"""GUI wizard for OfficeMapMaker.

The wizard is the only entry point users see; the CLI just launches it.
See plan.md section 14 for the design.

Module layout:
- main_window.py: top-level QMainWindow shell (sidebar + stacked content +
  issues panel + Back/Next footer). Drives navigation between steps.
- steps/*: one widget per pipeline pass (calibrate, validate labels,
  validate fill, layout, build, tile). Each is a QWidget subclass of
  ``StepBase``. Added in milestones W4..W9.
- (later) ``session.py`` provides the persisted state model.
"""
