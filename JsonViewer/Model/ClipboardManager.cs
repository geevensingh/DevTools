namespace JsonViewer.Model
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Interop;

    internal class ClipboardManager
    {
        private static readonly IntPtr WndProcSuccess = IntPtr.Zero;
        private MainWindow _mainWindow;

        public ClipboardManager(MainWindow mainWindow)
        {
            Debug.Assert(mainWindow != null);
            _mainWindow = mainWindow;

            HwndSource source = PresentationSource.FromVisual(mainWindow) as HwndSource;
            if (source == null)
            {
                throw new ArgumentException(
                    "Window source MUST be initialized first, such as in the Window's OnSourceInitialized handler.",
                    nameof(mainWindow));
            }

            source.AddHook(WndProc);

            // get window handle for interop
            IntPtr windowHandle = new WindowInteropHelper(mainWindow).Handle;

            // register for clipboard events
            NativeMethods.AddClipboardFormatListener(windowHandle);
        }

        public event EventHandler ClipboardChanged;

        public static string TryGetText()
        {
            try
            {
                return Clipboard.GetText();
            }
            catch
            {
            }

            return string.Empty;
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
            {
                _mainWindow.RunWhenever(() => { ClipboardChanged?.Invoke(null, EventArgs.Empty); });
                handled = true;
            }

            return WndProcSuccess;
        }
    }
}
