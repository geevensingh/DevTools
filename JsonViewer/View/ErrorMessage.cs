namespace JsonViewer.View
{
    using System.Windows;

    public static class ErrorMessage
    {
        public static MessageBoxResult Show(string message)
        {
            return MessageBox.Show(
                messageBoxText: message,
                caption: "Error!",
                button: MessageBoxButton.OK,
                icon: MessageBoxImage.Error);
        }
    }
}
