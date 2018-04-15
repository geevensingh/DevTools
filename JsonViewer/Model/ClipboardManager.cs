namespace JsonViewer.Model
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Interop;
    using JsonViewer.View;

    internal class ClipboardManager
    {
        private static readonly IntPtr WndProcSuccess = IntPtr.Zero;
        private System.Windows.Window _window;

        public ClipboardManager(System.Windows.Window window)
        {
            FileLogger.Assert(window != null);
            _window = window;

            HwndSource source = PresentationSource.FromVisual(window) as HwndSource;
            if (source == null)
            {
                throw new ArgumentException(
                    "Window source MUST be initialized first, such as in the Window's OnSourceInitialized handler.",
                    nameof(window));
            }

            source.AddHook(WndProc);

            // get window handle for interop
            IntPtr windowHandle = new WindowInteropHelper(window).Handle;

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
                _window.Dispatcher.BeginInvoke(new Action(() => { ClipboardChanged?.Invoke(null, EventArgs.Empty); }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                handled = true;
            }

            return WndProcSuccess;
        }
    }
}
