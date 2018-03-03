namespace JsonViewer.Model
{
    using System;
    using System.Diagnostics;
    using System.Windows;
    using System.Windows.Interop;

    internal class ClipboardManager
    {
        private static readonly IntPtr WndProcSuccess = IntPtr.Zero;

        private static ClipboardManager _this;

        private static EventHandler _clipboardChanged;

        private ClipboardManager()
        {
            Window windowSource = App.Current.MainWindow;
            Debug.Assert(windowSource != null);

            HwndSource source = PresentationSource.FromVisual(windowSource) as HwndSource;
            if (source == null)
            {
                throw new ArgumentException(
                    "Window source MUST be initialized first, such as in the Window's OnSourceInitialized handler.",
                    nameof(windowSource));
            }

            source.AddHook(WndProc);

            // get window handle for interop
            IntPtr windowHandle = new WindowInteropHelper(windowSource).Handle;

            // register for clipboard events
            NativeMethods.AddClipboardFormatListener(windowHandle);
        }

        public static event EventHandler ClipboardChanged
        {
            add
            {
                if (_this == null)
                {
                    _this = new ClipboardManager();
                }

                _clipboardChanged += value;
            }

            remove
            {
                _clipboardChanged -= value;
            }
        }

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
                _clipboardChanged?.Invoke(null, EventArgs.Empty);
                handled = true;
            }

            return WndProcSuccess;
        }
    }
}
