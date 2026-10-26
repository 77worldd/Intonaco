using System;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Windows.Threading;

namespace Intonaco.Core
{
    /// <summary>
    /// A point-in-time process footprint used by diagnostics and tests.
    /// Working-set values are intentionally reported alongside private bytes:
    /// the former can remain resident after native buffers are freed, while
    /// the latter shows whether the process actually released its commit.
    /// </summary>
    internal sealed class PerformanceSnapshot
    {
        public long WorkingSetBytes { get; init; }
        public long PrivateBytes { get; init; }
        public long ManagedBytes { get; init; }
        public int ThreadCount { get; init; }
        public int HandleCount { get; init; }
        public TimeSpan TotalProcessorTime { get; init; }

        public static PerformanceSnapshot Capture()
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();

            return new PerformanceSnapshot
            {
                WorkingSetBytes = process.WorkingSet64,
                PrivateBytes = process.PrivateMemorySize64,
                ManagedBytes = GC.GetTotalMemory(forceFullCollection: false),
                ThreadCount = process.Threads.Count,
                HandleCount = process.HandleCount,
                TotalProcessorTime = process.TotalProcessorTime
            };
        }

        public override string ToString()
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"working-set={WorkingSetBytes / 1048576.0:F1} MB, " +
                $"private={PrivateBytes / 1048576.0:F1} MB, " +
                $"managed={ManagedBytes / 1048576.0:F1} MB, " +
                $"threads={ThreadCount}, handles={HandleCount}, " +
                $"cpu-total={TotalProcessorTime.TotalSeconds:F2}s");
        }
    }

    /// <summary>
    /// Low-frequency, transition-only performance diagnostics. Nothing here
    /// runs while a wallpaper is animating except an explicit snapshot log;
    /// collection is delayed until playback has been fully suspended and is
    /// cancelled by a quick resume.
    /// </summary>
    internal static class PerformanceMonitor
    {
        private static DateTime _lastCollectionUtc = DateTime.MinValue;

        public static void Log(string reason)
        {
            try
            {
                DebugLog.Write($"Performance[{reason}]: {PerformanceSnapshot.Capture()}");
            }
            catch
            {
                // Diagnostics must never affect playback.
            }
        }

        public static void SchedulePausedCollection(
            Dispatcher dispatcher,
            Func<bool> shouldCollect)
        {
            if (dispatcher == null || shouldCollect == null)
                return;

            var timer = new DispatcherTimer(DispatcherPriority.ApplicationIdle, dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(750)
            };

            timer.Tick += (_, _) =>
            {
                timer.Stop();

                try
                {
                    if (!shouldCollect())
                        return;

                    DateTime now = DateTime.UtcNow;
                    if (now - _lastCollectionUtc < TimeSpan.FromSeconds(10))
                    {
                        Log("paused");
                        return;
                    }

                    _lastCollectionUtc = now;
                    Log("paused-before-collection");

                    // At this point both media pipelines have already been
                    // closed. A one-off compacting collection returns the
                    // temporary capture buffers and now-empty managed heaps to
                    // the OS instead of retaining them for a future allocation.
                    GCSettings.LargeObjectHeapCompactionMode =
                        GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(
                        GC.MaxGeneration,
                        GCCollectionMode.Aggressive,
                        blocking: true,
                        compacting: true);

                    Log("paused-after-collection");
                }
                catch
                {
                    // Best-effort diagnostics only.
                }
            };

            timer.Start();
        }
    }
}
