using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DiffViewer.Models;
using DiffViewer.ViewModels;

namespace DiffViewer.Rendering;

/// <summary>
/// Thin vertical strip painted between the two side-by-side diff panes (and
/// alongside the inline pane). Each diff hunk renders as a small colored
/// rectangle whose vertical position is proportional to the hunk's location
/// in the right-side file, and whose color matches the active
/// <see cref="DiffColorScheme"/>. Clicking a marker jumps the editors to
/// that hunk via <see cref="DiffPaneViewModel.JumpToHunk"/>.
///
/// <para>Implementation notes:</para>
/// <list type="bullet">
///   <item>Each hunk's natural rect is sized by the hunk's new-side line
///   count; markers shorter than the 4 px minimum hit-test height are
///   inflated to 4 px so they're clickable on any DPI.</item>
///   <item>Hover tooltip shows the hunk's line range (e.g.
///   <c>L42-45 (4 lines)</c>) so the user can scan without clicking.</item>
///   <item>This is the v1 implementation — cluster markers, popups, and
///   keyboard focus are listed in the plan but deferred to a follow-up
///   so the bar ships immediately. The hit-test still falls through to
///   the nearest marker when two markers overlap.</item>
/// </list>
/// </summary>
public sealed class HunkOverviewBar : FrameworkElement
{
    /// <summary>Side margin to keep markers from touching the bar's edges.</summary>
    private const double HorizontalPadding = 2.0;

    private DiffPaneViewModel? _vm;
    private bool _useInlineGeometry;

    /// <summary>
    /// When true, marker positions are computed against the inline document
    /// (which spans the full file with hunks woven in) rather than against
    /// the right-side blob. Set by the view depending on which pane the bar
    /// is decorating.
    /// </summary>
    public bool UseInlineGeometry
    {
        get => _useInlineGeometry;
        set { _useInlineGeometry = value; InvalidateVisual(); }
    }

    public HunkOverviewBar()
    {
        Cursor = Cursors.Hand;
        ToolTipService.SetInitialShowDelay(this, 200);
        ToolTipService.SetShowDuration(this, 8000);
        Width = 14.0;
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
        // Bar background — faint enough to disappear when no diff is loaded
        // but visible enough to give the user a column to aim at.
        var bg = new SolidColorBrush(Color.FromArgb(0x10, 0x80, 0x80, 0x80));
        if (bg.CanFreeze) bg.Freeze();
        dc.DrawRectangle(bg, null, new Rect(0, 0, ActualWidth, ActualHeight));

        if (_vm is null || ActualHeight <= 0 || ActualWidth <= 0) return;
        var hunks = _vm.CurrentHunks;
        if (hunks.Count == 0) return;

        int totalLines = ResolveTotalLines();
        if (totalLines <= 0) return;

        var scheme = _vm.CurrentColorScheme;

        for (int i = 0; i < hunks.Count; i++)
        {
            var (y, h) = HunkOverviewBarGeometry.ComputeMarkerRect(hunks[i], totalLines, ActualHeight);
            var brush = PickMarkerBrush(hunks[i], scheme);
            var rect = new Rect(HorizontalPadding, y, ActualWidth - 2 * HorizontalPadding, h);
            dc.DrawRectangle(brush, null, rect);

            // Highlight the active hunk with a darker outline so the user
            // can find the marker the keyboard navigation has selected.
            if (i == _vm.CurrentHunkIndex)
            {
                var pen = new Pen(Brushes.Black, 1.0);
                if (pen.CanFreeze) pen.Freeze();
                dc.DrawRectangle(null, pen, rect);
            }
        }
    }

    private int ResolveTotalLines()
    {
        if (_vm is null) return 0;
        return _useInlineGeometry
            ? Math.Max(1, _vm.InlineDocument.LineCount)
            : Math.Max(1, _vm.RightDocument.LineCount);
    }

    private static Brush PickMarkerBrush(DiffHunk hunk, DiffColorScheme scheme) =>
        HunkOverviewBarGeometry.ClassifyHunk(hunk) switch
        {
            HunkChangeShape.PureInsert => scheme.AddedIntraLineBackground,
            HunkChangeShape.PureDelete => scheme.RemovedIntraLineBackground,
            _ => scheme.ModifiedLineBackground,
        };

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_vm is null) return;
        int totalLines = ResolveTotalLines();
        var p = e.GetPosition(this);
        int idx = HunkOverviewBarGeometry.HitTest(_vm.CurrentHunks, totalLines, ActualHeight, p.Y);
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
        int totalLines = ResolveTotalLines();
        int idx = HunkOverviewBarGeometry.HitTest(hunks, totalLines, ActualHeight, e.GetPosition(this).Y);
        if (idx < 0)
        {
            ToolTip = null;
            return;
        }
        var h = hunks[idx];
        int startLine = h.NewStartLine > 0 ? h.NewStartLine : h.OldStartLine;
        int count = Math.Max(h.NewLineCount, h.OldLineCount);
        ToolTip = count <= 1
            ? $"Hunk {idx + 1} of {hunks.Count}: L{startLine}"
            : $"Hunk {idx + 1} of {hunks.Count}: L{startLine}-{startLine + count - 1} ({count} lines)";
    }
}
