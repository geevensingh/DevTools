namespace JsonViewer.Commands.PerItem
{
    using System.Windows;
    using JsonViewer;

    internal class CopyEscapedValueCommand : BaseTreeViewDataCommand
    {
        public CopyEscapedValueCommand(TreeViewData data)
            : base(data, "Copy escaped value", true)
        {
        }

        public override void Execute(object parameter)
        {
            Clipboard.SetText(CSEscape.Escape(this.Data.Value));
        }
    }
}
