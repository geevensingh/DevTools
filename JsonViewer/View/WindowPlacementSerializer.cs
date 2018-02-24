namespace JsonViewer
{
    using System;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Text;
    using System.Windows;
    using System.Windows.Interop;
    using System.Xml;
    using System.Xml.Serialization;
    using static JsonViewer.NativeMethods;

    public static class WindowPlacementSerializer
    {
        private static Encoding encoding = new UTF8Encoding();
        private static XmlSerializer serializer = new XmlSerializer(typeof(WINDOWPLACEMENT));

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
                placement.showCmd = placement.showCmd == SW_SHOWMINIMIZED ? SW_SHOWNORMAL : placement.showCmd;
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
            NativeMethods.GetWindowPlacement(new WindowInteropHelper(window).Handle, out WINDOWPLACEMENT placement);

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
