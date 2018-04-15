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
        private TabContent _tab;

        public ClipboardManager(TabContent tab)
        {
            FileLogger.Assert(tab != null);
            _tab = tab;

            HwndSource source = PresentationSource.FromVisual(tab) as HwndSource;
            if (source == null)
            {
                throw new ArgumentException(
                    "Window source MUST be initialized first, such as in the Window's OnSourceInitialized handler.",
                    nameof(tab));
            }

            source.AddHook(WndProc);

            // get window handle for interop
            IntPtr windowHandle = new WindowInteropHelper(tab).Handle;

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
                _tab.RunWhenever(() => { ClipboardChanged?.Invoke(null, EventArgs.Empty); });
                handled = true;
            }

            return WndProcSuccess;
        }
    }
}
