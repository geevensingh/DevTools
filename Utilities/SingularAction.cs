using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Utilities
{
    public class SingularAction
    {
        private Guid _actionId = Guid.NewGuid();
        private Dispatcher _dispatcher;
        private DispatcherOperation _operation = null;

        public Guid ActionId { get => _actionId; }

        public SingularAction(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void BeginInvoke(DispatcherPriority priority, Action<Guid> action)
        {
            Guid actionId = Guid.NewGuid();
            _actionId = actionId;

            if (_operation != null)
            {
                bool inProgress = !_operation.Abort();
                if (inProgress)
                {
                    _operation.Wait();
                    Debug.Assert(_operation.Status == DispatcherOperationStatus.Completed);
                }
                else
                {
                    Debug.Assert(_operation.Status == DispatcherOperationStatus.Aborted);
                }

                Debug.Assert(_operation.Status != DispatcherOperationStatus.Executing);
                Debug.Assert(_operation.Status != DispatcherOperationStatus.Pending);

                _operation = null;
            }

            Debug.Assert(_operation == null);
            _operation = _dispatcher.BeginInvoke(priority, action, actionId);
        }

        public async Task<bool> YieldAndContinue(Guid actionId)
        {
            await Dispatcher.Yield();
            return this.ShouldContinue(actionId);
        }

        public bool ShouldContinue(Guid actionId)
        {
            return actionId == _actionId;
        }
    }
}
