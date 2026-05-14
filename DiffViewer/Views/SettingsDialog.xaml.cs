using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DiffViewer.ViewModels;

namespace DiffViewer.Views;

/// <summary>
/// Modal Settings dialog. View concerns only: routes focus-loss /
/// Enter on numeric and text inputs to <see cref="SettingsViewModel
/// .CommitNumericFields"/>, blocks non-numeric characters on the
/// numeric fields, and flushes the color-scheme debounce on Close.
/// </summary>
public partial class SettingsDialog : Window
{
    private static readonly Regex IntegerPattern = new("^[0-9]+$", RegexOptions.Compiled);
    private static readonly Regex DecimalPattern = new("^[0-9]+(\\.[0-9]*)?$", RegexOptions.Compiled);

    public SettingsDialog(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void OnTextOrNumericLostFocus(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;

        // Push the TextBox's / ComboBox's current text into the bound VM
        // property FIRST. We use UpdateSourceTrigger=LostFocus on these
        // bindings, but the binding's internal LostFocus handler is only
        // attached when the binding activates (i.e. when DataContext is
        // set), which happens AFTER InitializeComponent registered this
        // XAML handler. Routed-event handlers fire in registration order,
        // so without this explicit UpdateSource() we'd run before the
        // binding -- CommitNumericFields would see the stale VM value,
        // and the UpdateTarget bounce below would then overwrite the
        // user's typed text with that stale value.
        var bindingExpr = sender switch
        {
            TextBox tb => tb.GetBindingExpression(TextBox.TextProperty),
            ComboBox cb => cb.GetBindingExpression(ComboBox.TextProperty),
            _ => null,
        };
        bindingExpr?.UpdateSource();

        vm.CommitNumericFields();

        // Bounce the binding so any clamped / coerced value (e.g. font
        // size pinned to 6 – 72, or an empty text box reverted to its
        // default) is reflected back into the visible field.
        bindingExpr?.UpdateTarget();
    }

    private void OnTextOrNumericKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        switch (sender)
        {
            case TextBox tb:
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                (DataContext as SettingsViewModel)?.CommitNumericFields();
                e.Handled = true;
                break;
            case ComboBox cb:
                cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                (DataContext as SettingsViewModel)?.CommitNumericFields();
                e.Handled = true;
                break;
        }
    }

    private void OnFontFamilySelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Picking a font from the dropdown is a commit-worthy event,
        // and the editable ComboBox's LostFocus doesn't fire while the
        // dropdown popup is open.
        if (e.AddedItems.Count == 0) return;
        if (DataContext is SettingsViewModel vm)
        {
            // Push the just-selected display name into the bound source.
            if (sender is ComboBox cb)
            {
                cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
            }
            vm.CommitNumericFields();
        }
    }

    private void OnIntegerTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!IsIntegerInsertion((TextBox)sender, e.Text))
        {
            e.Handled = true;
        }
    }

    private void OnDecimalTextInput(object sender, TextCompositionEventArgs e)
    {
        if (!IsDecimalInsertion((TextBox)sender, e.Text))
        {
            e.Handled = true;
        }
    }

    private void OnIntegerPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!TryGetPastedText(e, out var text) || !IsIntegerInsertion((TextBox)sender, text))
        {
            e.CancelCommand();
        }
    }

    private void OnDecimalPasting(object sender, DataObjectPastingEventArgs e)
    {
        if (!TryGetPastedText(e, out var text) || !IsDecimalInsertion((TextBox)sender, text))
        {
            e.CancelCommand();
        }
    }

    private static bool IsIntegerInsertion(TextBox tb, string inserted) =>
        IntegerPattern.IsMatch(PreviewAfterInsertion(tb, inserted));

    private static bool IsDecimalInsertion(TextBox tb, string inserted) =>
        DecimalPattern.IsMatch(PreviewAfterInsertion(tb, inserted));

    private static string PreviewAfterInsertion(TextBox tb, string inserted)
    {
        var text = tb.Text ?? string.Empty;
        var selStart = tb.SelectionStart;
        var selLength = tb.SelectionLength;
        return string.Concat(
            text.AsSpan(0, selStart),
            inserted.AsSpan(),
            text.AsSpan(selStart + selLength));
    }

    private static bool TryGetPastedText(DataObjectPastingEventArgs e, out string text)
    {
        if (e.SourceDataObject?.GetDataPresent(DataFormats.UnicodeText, true) == true)
        {
            text = (e.SourceDataObject.GetData(DataFormats.UnicodeText, true) as string) ?? string.Empty;
            return true;
        }
        text = string.Empty;
        return false;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        // If the user closes the dialog while a text input still has
        // focus (e.g. they pressed Esc -- IsCancel=True -- without
        // tabbing out first), the focused control's LostFocus event
        // hasn't fired yet, so its UpdateSourceTrigger=LostFocus
        // binding hasn't pushed the typed value to the VM. Force it
        // here so CommitNumericFields below picks up the latest.
        switch (Keyboard.FocusedElement)
        {
            case TextBox tb:
                tb.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                break;
            case ComboBox cb:
                cb.GetBindingExpression(ComboBox.TextProperty)?.UpdateSource();
                break;
        }

        if (DataContext is SettingsViewModel vm)
        {
            vm.CommitNumericFields();
            vm.FlushPendingWrites();
        }
        Close();
    }
}
