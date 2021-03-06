using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace FastCodeNavPlugin.Common
{
    public static class TaskExtensions
    {
        public static void FireAndForget(this Task task, ILogger logger)
        {
            var nowait = task.ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    if (t.IsCanceled)
                    {
                        logger.LogDebug(t.Exception, $"Cancellation of fire and forget task: {t.Exception?.Message}");
                    }
                    else
                    {
                        logger.LogError(t.Exception, $"Unhandled exception in fire and forget task: {t.Exception?.Message}");
                    }
                }
            },
            TaskScheduler.Default);
        }
    }
}