using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Windows;
using System.Windows.Interop;

namespace TextManipulator
{
    public static class WindowPlacementSerializer
    {
        // RECT structure required by WINDOWPLACEMENT structure
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public RECT(int left, int top, int right, int bottom)
            {
                this.Left = left;
                this.Top = top;
                this.Right = right;
                this.Bottom = bottom;
            }
        }

        // POINT structure required by WINDOWPLACEMENT structure
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;

            public POINT(int x, int y)
            {
                this.X = x;
                this.Y = y;
            }
        }

        // WINDOWPLACEMENT stores the position, size, and state of a window
        [Serializable]
        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT minPosition;
            public POINT maxPosition;
            public RECT normalPosition;
        }

        private static Encoding encoding = new UTF8Encoding();
        private static XmlSerializer serializer = new XmlSerializer(typeof(WINDOWPLACEMENT));

        private static class NativeMethods
        {
            [DllImport("user32.dll")]
            public static extern bool SetWindowPlacement(IntPtr hWnd, [In] ref WINDOWPLACEMENT lpwndpl);

            [DllImport("user32.dll")]
            public  static extern bool GetWindowPlacement(IntPtr hWnd, out WINDOWPLACEMENT lpwndpl);
        }

        private const int SW_SHOWNORMAL = 1;
        private const int SW_SHOWMINIMIZED = 2;

        public static void SetPlacement(Window window, string placementXml, Point? offset = null)
        {
            if (string.IsNullOrEmpty(placementXml))
            {
                return;
            }

            Point realOffset = new Point(0, 0);
            if (offset.HasValue)
            {
                realOffset = offset.Value;
            }

            WINDOWPLACEMENT placement;
            byte[] xmlBytes = encoding.GetBytes(placementXml);

            try
            {
                using (MemoryStream memoryStream = new MemoryStream(xmlBytes))
                {
                    placement = (WINDOWPLACEMENT)serializer.Deserialize(memoryStream);
                }

                placement.length = Marshal.SizeOf(typeof(WINDOWPLACEMENT));
                placement.flags = 0;
                placement.showCmd = (placement.showCmd == SW_SHOWMINIMIZED ? SW_SHOWNORMAL : placement.showCmd);
                placement.normalPosition.Left += (int)Math.Floor(realOffset.X);
                placement.normalPosition.Right += (int)Math.Floor(realOffset.X);
                placement.normalPosition.Top += (int)Math.Floor(realOffset.Y);
                placement.normalPosition.Bottom += (int)Math.Floor(realOffset.Y);
                NativeMethods.SetWindowPlacement(new WindowInteropHelper(window).Handle, ref placement);
            }
            catch (InvalidOperationException)
            {
                // Parsing placement XML failed. Fail silently.
            }
        }

        public static string GetPlacement(Window window)
        {
            WINDOWPLACEMENT placement = new WINDOWPLACEMENT();
            NativeMethods.GetWindowPlacement(new WindowInteropHelper(window).Handle, out placement);

            MemoryStream memoryStream = null;
            try
            {
                memoryStream = new MemoryStream();
                XmlTextWriter xmlTextWriter = new XmlTextWriter(memoryStream, Encoding.UTF8);
                serializer.Serialize(xmlTextWriter, placement);
                byte[] xmlBytes = memoryStream.ToArray();
                return encoding.GetString(xmlBytes);
            }
            finally
            {
                if (memoryStream != null)
                {
                    memoryStream.Dispose();
                }
            }
        }
    }
}
