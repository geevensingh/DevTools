namespace JsonViewer.View
{
    using System;
    using System.Collections.Generic;
    using System.Windows.Input;

    internal class WaitCursor : IDisposable
    {
        private static Stack<Cursor> _previousCursors = new Stack<Cursor>();

        private bool _isNoop = false;

        public WaitCursor()
        {
            _isNoop = _previousCursors.Count > 0 && _previousCursors.Peek() == Cursors.Wait;
            if (!_isNoop)
            {
                _previousCursors.Push(Mouse.OverrideCursor);

                Mouse.OverrideCursor = Cursors.Wait;
            }
        }

        public void Dispose()
        {
            if (!_isNoop)
            {
                Mouse.OverrideCursor = _previousCursors.Pop();
            }
        }
    }
}
