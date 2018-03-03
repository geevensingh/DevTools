using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Utilities
{
    public class SingularAction : NotifyPropertyChanged
    {
        private Guid _actionId = Guid.Empty;
        private Dispatcher _dispatcher;
        private DispatcherOperation _operation = null;

        public Guid ActionId { get => _actionId; }

        public SingularAction(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
        }

        public void BeginInvoke(DispatcherPriority priority, Func<Guid, Task<bool>> func)
        {
            Guid actionId = Guid.NewGuid();
            _actionId = actionId;

            if (_operation != null)
            {
                Debug.Assert(this.IsRunning);

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
            _operation = _dispatcher.BeginInvoke(priority, func, actionId);
            Debug.Assert(this.IsRunning);
            this.FirePropertyChanged("IsRunning");
            _operation.Task.ContinueWith(new Action<Task>(
                (task) =>
                {
                    Debug.Assert(task.IsCompleted);
                    Task<bool> subTask = (Task<bool>)_operation.Result;
                    subTask.ContinueWith(new Action<Task<bool>>(
                        (boolTask) =>
                        {
                            Debug.Assert(boolTask.IsCompleted);
                            if (!this.IsRunning)
                            {
                                _actionId = Guid.Empty;
                                _operation = null;
                                _dispatcher.InvokeAsync(() => { this.FirePropertyChanged("IsRunning"); });
                            }
                        }));
                }));
        }

        public async Task<bool> YieldAndContinue(Guid actionId)
        {
            await Dispatcher.Yield();
            return this.ShouldContinue(actionId);
        }

        public bool IsRunning
        {
            get
            {
                if (_operation == null)
                {
                    return false;
                }

                if (_operation?.Status == DispatcherOperationStatus.Completed)
                {
                    Task<bool> task = (Task<bool>)_operation.Result;
                    if (task.IsCompleted)
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        public bool ShouldContinue(Guid actionId)
        {
            if (_operation?.Status == DispatcherOperationStatus.Completed)
            {
                Task<bool> task = (Task<bool>)_operation.Result;
                if (task.IsCompleted && task.Result)
                {
                    return true;
                }
            }

            return actionId == _actionId;
        }
    }
}
