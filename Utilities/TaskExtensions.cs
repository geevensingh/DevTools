using System.Threading.Tasks;
using System.Diagnostics;

namespace Utilities
{
    public static class TaskExtensions
    {
        public static void Forget(this Task task)
        {
            task.ContinueWith(t => { Debug.WriteLine(t.Exception); }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}
