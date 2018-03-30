namespace JsonViewer.View
{
    using System.Windows.Controls;

    public class CustomVirtualizingStackPanel : VirtualizingStackPanel
    {
        public void BringIntoView(int index)
        {
            this.BringIndexIntoView(index);
        }
    }
}
