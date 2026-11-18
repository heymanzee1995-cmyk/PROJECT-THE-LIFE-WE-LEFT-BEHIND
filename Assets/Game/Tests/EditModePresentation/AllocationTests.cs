#if !UNITY_5_3_OR_NEWER
using System;
using Game.Data;
using Game.Domain;
using NUnit.Framework;

namespace Game.Presentation.Tests
{
    /// <summary>
    /// Harness-only check (needs .NET's per-thread allocation counter) that the per-frame drag path in pure C#
    /// allocates nothing: hit-testing, hover evaluation and the gesture. Inside Unity the equivalent check is
    /// BoardAllocationTests (PlayMode), which uses Unity's own GC recorder on the full MonoBehaviour path.
    /// </summary>
    public class AllocationTests
    {
        [Test]
        public void DragFramePath_AllocatesNothing()
        {
            MergeContent content = ContentFile.LoadValid();
            MergeEngine engine = MergeEngine.CreateNew(content.Config, 3, content.StartingLayout);
            var presenter = new BoardPresenter(engine, content.Catalog);
            float d = 2.75f;
            var layout = new BoardLayout(new ScreenMetrics(393 * d, 852 * d, 440, 0, 48 * d, 393 * d, 780 * d), new LayoutSpec(), 5, 8, 4);
            var gesture = new DragGesture(layout.DragThresholdPx);
            Location from = Location.Cell(15);
            ItemId id = engine.GetCell(15).Id;

            void Frame(int step)
            {
                float x = layout.BoardLeft + (step * 37 % (int)layout.BoardWidth);
                float y = layout.SlotY + (step * 53 % (int)(layout.BoardTop - layout.SlotY + 40));
                gesture.Move(x, y);
                PointerTarget target;
                int cell = layout.CellAt(x, y);
                if (cell >= 0)
                {
                    target = PointerTarget.AtCell(cell);
                }
                else if (layout.IsOverSell(x, y))
                {
                    target = PointerTarget.Sell;
                }
                else
                {
                    target = layout.IsOverInventory(x, y) ? PointerTarget.Inventory : PointerTarget.Nothing;
                }

                presenter.Hover(from, id, target);
            }

            gesture.Press(layout.CellCentreX(15), layout.CellCentreY(15));
            for (int i = 0; i < 200; i++)
            {
                Frame(i); // warm-up
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 5000; i++)
            {
                Frame(i);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0, allocated, "bytes allocated across 5000 drag frames");
        }

        [Test]
        public void GeneratorBadgeRefresh_AllocatesNothing()
        {
            MergeContent content = ContentFile.LoadValid();
            MergeEngine engine = MergeEngine.CreateNew(content.Config, 3, content.StartingLayout);
            Item gen = engine.GetCell(17);
            for (int i = 0; i < 10; i++)
            {
                engine.TryTapGenerator(17, gen.Id, 0);
            }

            // One minute of 4 Hz badge refreshes, as BoardScreen does for every generator.
            void RefreshForAMinute()
            {
                for (long t = 0; t < 60000; t += 250)
                {
                    engine.GetAvailableCharges(gen, t);
                    TimeText.Format(engine.GetNextChargeInMs(gen, t));
                }
            }

            RefreshForAMinute(); // warm-up: JIT and the countdown string cache fill once
            RefreshForAMinute();

            // The .NET runtime can make a single tiny one-time allocation (tiered JIT) on the first measured
            // round, so the first round is discarded and the steady state is checked over several rounds.
            var rounds = new long[5];
            for (int k = 0; k < rounds.Length; k++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                RefreshForAMinute();
                rounds[k] = GC.GetAllocatedBytesForCurrentThread() - before;
            }

            for (int k = 1; k < rounds.Length; k++)
            {
                Assert.AreEqual(0, rounds[k], "steady-state bytes in round " + k + " (all: " + string.Join(",", rounds) + ")");
            }
        }
    }
}
#endif
