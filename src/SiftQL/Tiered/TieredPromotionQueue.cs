using System.Collections.Concurrent;

namespace SiftQL.Tiered;

internal static class TieredPromotionQueue
{
    private const int MaxWorkers = 2;
    private static readonly ConcurrentQueue<Action> s_queue = new();
    private static int s_pendingJobs;
    private static int s_runningWorkers;

    public static bool TryQueue(Action action, int capacity)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (capacity <= 0)
            return false;

        while (true)
        {
            int pending = Volatile.Read(ref s_pendingJobs);
            if (pending >= capacity)
                return false;
            if (Interlocked.CompareExchange(ref s_pendingJobs, pending + 1, pending) == pending)
                break;
        }

        s_queue.Enqueue(action);
        TryStartWorker();
        return true;
    }

    private static void TryStartWorker()
    {
        bool started = false;
        while (Volatile.Read(ref s_runningWorkers) < MaxWorkers)
        {
            int running = Volatile.Read(ref s_runningWorkers);
            if (running >= MaxWorkers)
                return;
            if (Interlocked.CompareExchange(ref s_runningWorkers, running + 1, running) == running)
            {
                started = true;
                break;
            }
        }

        if (!started)
            return;
        ThreadPool.QueueUserWorkItem(static _ => RunWorker(), null);
    }

    private static void RunWorker()
    {
        try
        {
            while (s_queue.TryDequeue(out Action? action))
            {
                try
                {
                    action();
                }
                finally
                {
                    Interlocked.Decrement(ref s_pendingJobs);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref s_runningWorkers);
            if (!s_queue.IsEmpty)
                TryStartWorker();
        }
    }
}
