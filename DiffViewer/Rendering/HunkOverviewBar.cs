using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiffViewer.Models;
using DiffViewer.ViewModels;

namespace DiffViewer.Rendering;

/// <summary>
/// Thin vertical strip painted between (or alongside) the diff panes. Hunks
/// render as a pair of small rectangles — deletions on the left, insertions
/// on the right — at vertical positions proportional to their location in
/// the corresponding file. Mixed hunks (those that both delete and insert
/// content) get a horizontal gradient ribbon connecting the two rects.
///
/// <para>Implementation notes:</para>
/// <list type="bullet">
///   <item>Each column scales independently to its file's line count, so
///   the relative heights of the two columns also visualise the relative
///   sizes of the two files (mirrors JetBrains' diff bar).</item>
///   <item>Markers shorter than <see cref="HunkOverviewBarGeometry.MinHitHeight"/>
///   are inflated so they stay clickable on any DPI.</item>
///   <item>Hover tooltip shows the hunk's old- and new-side line ranges so
///   the user can scan without clicking. Clicking a marker (or the ribbon
///   between two markers) jumps the editors to that hunk via
///   <see cref="DiffPaneViewModel.JumpToHunk"/>.</item>
///   <item>Cluster markers, popups, and keyboard focus are listed in the
///   plan but deferred to a follow-up — the hit-test still falls through
///   to the nearest marker when two rects overlap.</item>
/// </list>
/// </summary>
public sealed class HunkOverviewBar : FrameworkElement
{
    /// <summary>
    /// Width of each colored column (deletions on the left, insertions on
    /// the right). The middle of the bar is reserved for the gradient
    /// ribbons that connect paired markers.
    /// </summary>
    private const double ColumnWidth = 10.0;

    /// <summary>Total bar width — two columns plus a ribbon gutter.</summary>
    private const double DefaultBarWidth = 32.0;

    private DiffPaneViewModel? _vm;

    public HunkOverviewBar()
    {
        Cursor = Cursors.Hand;
        ToolTipService.SetInitialShowDelay(this, 200);
        ToolTipService.SetShowDuration(this, 8000);
        Width = DefaultBarWidth;
        SnapsToDevicePixels = true;
        Focusable = false;

        DataContextChanged += OnDataContextChanged;
        Loaded += (_, _) => AttachToVm(DataContext as DiffPaneViewModel);
        Unloaded += (_, _) => DetachFromVm();
        MouseMove += OnMouseMove;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        DetachFromVm();
        AttachToVm(e.NewValue as DiffPaneViewModel);
    }

    private void AttachToVm(DiffPaneViewModel? vm)
    {
        _vm = vm;
        if (_vm is null) return;
        _vm.HighlightMapChanged += OnHighlightMapChanged;
        _vm.ColorSchemeChanged += OnColorSchemeChanged;
        _vm.PropertyChanged += OnVmPropertyChanged;
        InvalidateVisual();
    }

    private void DetachFromVm()
    {
        if (_vm is null) return;
        _vm.HighlightMapChanged -= OnHighlightMapChanged;
        _vm.ColorSchemeChanged -= OnColorSchemeChanged;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = null;
    }

    private void OnHighlightMapChanged(object? sender, EventArgs e) => InvalidateVisual();
    private void OnColorSchemeChanged(object? sender, EventArgs e) => InvalidateVisual();

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffPaneViewModel.CurrentHunkIndex))
            InvalidateVisual();
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        // Faint background so the strip is visible even before any diff is
        // loaded — gives the user a column to aim at and reinforces the
        // gutter between the two text panes.
        var bg = new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x80, 0x80));
        if (bg.CanFreeze) bg.Freeze();
        dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_vm is null || ActualHeight <= 0 || ActualWidth <= 0) return;
        var hunks = _vm.CurrentHunks;
        if (hunks.Count == 0) return;

        int leftTotal = Math.Max(1, _vm.LeftDocument.LineCount);
        int rightTotal = Math.Max(1, _vm.RightDocument.LineCount);

        var scheme = _vm.CurrentColorScheme;
        var removedBrush = scheme.RemovedIntraLineBackground;
        var addedBrush = scheme.AddedIntraLineBackground;
        Color removedColor = ((SolidColorBrush)removedBrush).Color;
        Color addedColor = ((SolidColorBrush)addedBrush).Color;

        var layouts = HunkOverviewBarGeometry.ComputeLayouts(
            hunks, leftTotal, rightTotal, ActualWidth, ActualHeight, ColumnWidth);

        // Outline pen for the active hunk. Reused per-layout below.
        var activePen = new Pen(Brushes.Black, 1.0);
        if (activePen.CanFreeze) activePen.Freeze();

        for (int i = 0; i < layouts.Count; i++)
        {
            var layout = layouts[i];

            // Mixed hunk: paint the trapezoid first so the column rects
            // sit on top (column rects own the click target's left/right
            // ends; the ribbon owns the middle).
            if (layout.LeftRect is Rect L && layout.RightRect is Rect R)
            {
                var ribbon = BuildTrapezoid(L, R);
                var gradient = new LinearGradientBrush
                {
                    MappingMode = BrushMappingMode.Absolute,
                    StartPoint = new Point(L.Right, 0),
                    EndPoint = new Point(R.Left, 0),
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(removedColor, 0),
                        new GradientStop(addedColor, 1),
                    },
                };
                if (gradient.CanFreeze) gradient.Freeze();
                dc.DrawGeometry(gradient, null, ribbon);
            }

            if (layout.LeftRect is Rect lr)
                dc.DrawRectangle(removedBrush, null, lr);
            if (layout.RightRect is Rect rr)
                dc.DrawRectangle(addedBrush, null, rr);

            // Highlight the active hunk by outlining whichever column
            // rect(s) are present so the user can find the marker the
            // keyboard / mouse navigation has selected.
            if (i == _vm.CurrentHunkIndex)
            {
                if (layout.LeftRect is Rect lh)
                    dc.DrawRectangle(null, activePen, lh);
                if (layout.RightRect is Rect rh)
                    dc.DrawRectangle(null, activePen, rh);
            }
        }
    }

    /// <summary>
    /// Build the trapezoid path that bridges <paramref name="left"/> and
    /// <paramref name="right"/> for a mixed hunk: top edge connects the two
    /// rects' top corners, bottom edge connects their bottom corners.
    /// </summary>
    private static StreamGeometry BuildTrapezoid(Rect left, Rect right)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(left.Right, left.Top), isFilled: true, isClosed: true);
            ctx.LineTo(new Point(right.Left, right.Top), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(right.Left, right.Bottom), isStroked: false, isSmoothJoin: false);
            ctx.LineTo(new Point(left.Right, left.Bottom), isStroked: false, isSmoothJoin: false);
        }
        if (g.CanFreeze) g.Freeze();
        return g;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_vm is null) return;
        var layouts = ComputeLayouts();
        int idx = HunkOverviewBarGeometry.HitTest(layouts, e.GetPosition(this));
        if (idx >= 0)
        {
            _vm.JumpToHunk(idx);
            e.Handled = true;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_vm is null) return;
        var hunks = _vm.CurrentHunks;
        if (hunks.Count == 0)
        {
            ToolTip = null;
            return;
        }
        var layouts = ComputeLayouts();
        int idx = HunkOverviewBarGeometry.HitTest(layouts, e.GetPosition(this));
        if (idx < 0)
        {
            ToolTip = null;
            return;
        }
        var h = hunks[idx];
        ToolTip = FormatTooltip(idx, hunks.Count, h);
    }

    private IReadOnlyList<HunkOverviewBarGeometry.HunkBarLayout> ComputeLayouts()
    {
        if (_vm is null) return Array.Empty<HunkOverviewBarGeometry.HunkBarLayout>();
        int leftTotal = Math.Max(1, _vm.LeftDocument.LineCount);
        int rightTotal = Math.Max(1, _vm.RightDocument.LineCount);
        return HunkOverviewBarGeometry.ComputeLayouts(
            _vm.CurrentHunks, leftTotal, rightTotal, ActualWidth, ActualHeight, ColumnWidth);
    }

    /// <summary>
    /// Builds a tooltip showing the hunk's index plus the per-side line
    /// ranges. Pure-add and pure-delete hunks only show the side that has
    /// content; mixed hunks show both ranges so the user can correlate
    /// the bar with the diff text.
    /// </summary>
    private static string FormatTooltip(int idx, int total, DiffHunk h)
    {
        string? oldRange = h.OldLineCount > 0
            ? FormatRange(h.OldStartLine, h.OldLineCount)
            : null;
        string? newRange = h.NewLineCount > 0
            ? FormatRange(h.NewStartLine, h.NewLineCount)
            : null;
        if (oldRange is not null && newRange is not null)
            return $"Hunk {idx + 1} of {total}: old {oldRange} → new {newRange}";
        if (newRange is not null)
            return $"Hunk {idx + 1} of {total}: added {newRange}";
        if (oldRange is not null)
            return $"Hunk {idx + 1} of {total}: removed {oldRange}";
        return $"Hunk {idx + 1} of {total}";
    }

    private static string FormatRange(int start, int count) =>
        count <= 1 ? $"L{start}" : $"L{start}-{start + count - 1} ({count} lines)";
}
