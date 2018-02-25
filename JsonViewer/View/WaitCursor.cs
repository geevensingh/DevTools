namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Input;

    internal class WaitCursor : IDisposable
    {
        private static Stack<Cursor> _previousCursors = new Stack<Cursor>();

        public WaitCursor()
        {
            _previousCursors.Push(Mouse.OverrideCursor);

            Mouse.OverrideCursor = Cursors.Wait;
        }

        public void Dispose()
        {
            Mouse.OverrideCursor = _previousCursors.Pop();
        }
    }
}
