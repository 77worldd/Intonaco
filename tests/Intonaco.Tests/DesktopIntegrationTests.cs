using System;
using Intonaco.Core;
using Xunit;
using static Intonaco.Core.DesktopIntegration;

namespace Intonaco.Tests
{
    /// <summary>
    /// Pure-logic tests for WorkerW candidate ordering. The Win32 discovery
    /// itself needs a live Explorer desktop, but the *choice* among found
    /// candidates is deterministic and tested here.
    /// </summary>
    public class DesktopIntegrationTests
    {
        private static WorkerWCandidate C(long handle, WorkerWStrategy strategy,
            long area = 1920L * 1080, bool visible = true)
            => new WorkerWCandidate(new IntPtr(handle), strategy, area, visible);

        [Fact]
        public void ClassicStrategy_ComesBeforeFallbacks()
        {
            var ordered = OrderCandidates(new[]
            {
                C(0x30, WorkerWStrategy.ProgmanFallback),
                C(0x10, WorkerWStrategy.ShellHostNextSibling),
                C(0x20, WorkerWStrategy.ProgmanChild),
            });

            Assert.Equal(new IntPtr(0x10), ordered[0]);
            Assert.Equal(new IntPtr(0x20), ordered[1]);
            Assert.Equal(new IntPtr(0x30), ordered[2]);
        }

        [Fact]
        public void SameHandleFromTwoStrategies_IsDeduplicatedKeepingBestStrategy()
        {
            var ordered = OrderCandidates(new[]
            {
                C(0x10, WorkerWStrategy.AnyWorkerW),
                C(0x10, WorkerWStrategy.ProgmanChild),
                C(0x20, WorkerWStrategy.ProgmanNextSibling),
            });

            Assert.Equal(2, ordered.Count);
            // 0x10 keeps its best rank (ProgmanChild) and stays ahead of 0x20.
            Assert.Equal(new IntPtr(0x10), ordered[0]);
            Assert.Equal(new IntPtr(0x20), ordered[1]);
        }

        [Fact]
        public void ZeroHandles_AreFilteredOut()
        {
            var ordered = OrderCandidates(new[]
            {
                C(0, WorkerWStrategy.ShellHostNextSibling),
                C(0x10, WorkerWStrategy.AnyWorkerW),
            });

            Assert.Single(ordered);
            Assert.Equal(new IntPtr(0x10), ordered[0]);
        }

        [Fact]
        public void WithinSameStrategy_VisibleThenLargestWins()
        {
            var ordered = OrderCandidates(new[]
            {
                C(0x10, WorkerWStrategy.AnyWorkerW, area: 800L * 600, visible: true),
                C(0x20, WorkerWStrategy.AnyWorkerW, area: 1920L * 1080, visible: false),
                C(0x30, WorkerWStrategy.AnyWorkerW, area: 1920L * 1080, visible: true),
            });

            Assert.Equal(new IntPtr(0x30), ordered[0]); // visible + largest
            Assert.Equal(new IntPtr(0x10), ordered[1]); // visible
            Assert.Equal(new IntPtr(0x20), ordered[2]); // hidden
        }

        [Fact]
        public void EmptyInput_ReturnsEmpty()
        {
            Assert.Empty(OrderCandidates(Array.Empty<WorkerWCandidate>()));
        }

        [Fact]
        public void Classify_NoRedirectionBitmap_IsRaisedEvenWithClassicWorkerW()
        {
            // A stale top-level WorkerW can exist on a raised desktop, but
            // parenting there is invisible — raised must win.
            var layout = ClassifyLayout(
                progmanHasNoRedirectionBitmap: true,
                defViewIsProgmanChild: true,
                progmanHasFullScreenWorkerWChild: true,
                classicWorkerWFound: true);
            Assert.Equal(DesktopLayout.RaisedProgman, layout);
        }

        [Fact]
        public void Classify_DefViewAndFullScreenWorkerWUnderProgman_IsRaised()
        {
            var layout = ClassifyLayout(
                progmanHasNoRedirectionBitmap: false,
                defViewIsProgmanChild: true,
                progmanHasFullScreenWorkerWChild: true,
                classicWorkerWFound: false);
            Assert.Equal(DesktopLayout.RaisedProgman, layout);
        }

        [Fact]
        public void Classify_ClassicWorkerWOnly_IsClassic()
        {
            var layout = ClassifyLayout(
                progmanHasNoRedirectionBitmap: false,
                defViewIsProgmanChild: false,
                progmanHasFullScreenWorkerWChild: false,
                classicWorkerWFound: true);
            Assert.Equal(DesktopLayout.ClassicWorkerW, layout);
        }

        [Fact]
        public void Classify_UnsplitDesktop_IsUnknown()
        {
            var layout = ClassifyLayout(
                progmanHasNoRedirectionBitmap: false,
                defViewIsProgmanChild: true,
                progmanHasFullScreenWorkerWChild: false,
                classicWorkerWFound: false);
            Assert.Equal(DesktopLayout.Unknown, layout);
        }
    }
}
