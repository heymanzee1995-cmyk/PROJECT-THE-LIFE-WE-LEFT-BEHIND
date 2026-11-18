using System.Collections;
using Game.Domain;
using Game.Presentation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.PlayMode.Tests
{
    /// <summary>
    /// Smoke tests for the M2 board in play mode: it loads, views mirror the engine, and taps/drops go
    /// through the same code path as real touches. Visual quality and feel are checked by hand on a device.
    /// </summary>
    public class BoardSmokeTests
    {
        private GameObject _go;
        private BoardScreen _screen;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _go = new GameObject("Board under test");
            _screen = _go.AddComponent<BoardScreen>();
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_go);
            Camera cam = Camera.main;
            if (cam != null)
            {
                Object.Destroy(cam.gameObject);
            }

            yield return null;
        }

        private static IEnumerator Settle()
        {
            yield return new WaitForSecondsRealtime(0.6f);
        }

        [UnityTest]
        public IEnumerator Board_Loads_With5x8Layout_AndOneViewPerItem()
        {
            Assert.IsNull(_screen.LoadError, _screen.LoadError);
            Assert.AreEqual(5, _screen.Engine.Columns);
            Assert.AreEqual(8, _screen.Engine.Rows);
            Assert.IsNotNull(_screen.Layout);
            Assert.That(_screen.Layout.CellPx, Is.GreaterThan(0f));
            Assert.IsTrue(_screen.ViewsMatchEngine());
            yield return null;
        }

        [UnityTest]
        public IEnumerator Merge_Bounce_Spawn_Store_And_Sell_KeepViewsInSync()
        {
            MergeEngine engine = _screen.Engine;

            // Starting layout has two Seed Packets on cells 15 and 16.
            InteractionResult merge = _screen.SimulateDrop(Location.Cell(15), PointerTarget.AtCell(16));
            Assert.AreEqual(InteractionKind.Merged, merge.Kind);
            yield return Settle();
            Assert.IsTrue(_screen.ViewsMatchEngine(), "after merge");

            // Sprout (16) onto the toolbox (17): incompatible, bounces, nothing changes.
            int before = engine.ItemCount;
            InteractionResult bounce = _screen.SimulateDrop(Location.Cell(16), PointerTarget.AtCell(17));
            Assert.AreEqual(InteractionKind.Bounced, bounce.Kind);
            Assert.IsTrue(bounce.InvalidFeedback);
            Assert.AreEqual(before, engine.ItemCount);
            yield return Settle();
            Assert.IsTrue(_screen.ViewsMatchEngine(), "after bounce");

            InteractionResult spawn = _screen.SimulateTap(Location.Cell(17));
            Assert.AreEqual(InteractionKind.Spawned, spawn.Kind);
            yield return Settle();
            Assert.IsTrue(_screen.ViewsMatchEngine(), "after spawn");

            Assert.AreEqual(InteractionKind.Stored, _screen.SimulateDrop(Location.Cell(16), PointerTarget.Inventory).Kind);
            yield return Settle();
            Assert.IsTrue(_screen.ViewsMatchEngine(), "after store");

            Assert.AreEqual(InteractionKind.Sold, _screen.SimulateDrop(Location.Slot(0), PointerTarget.Sell).Kind);
            yield return Settle();
            Assert.IsTrue(_screen.ViewsMatchEngine(), "after sell");

            Assert.That(InvariantChecker.Check(engine), Is.Empty);
        }
    }
}

namespace Game.PlayMode.Tests
{
    using UnityEngine.TestTools.Constraints;
    using Is = UnityEngine.TestTools.Constraints.Is;

    /// <summary>M2 acceptance: dragging allocates no managed memory per frame. Uses Unity's GC allocation recorder.</summary>
    public class BoardAllocationTests
    {
        private GameObject _go;
        private BoardScreen _screen;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _go = new GameObject("Board under test");
            _screen = _go.AddComponent<BoardScreen>();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_go);
            if (Camera.main != null)
            {
                Object.Destroy(Camera.main.gameObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DragFrames_DoNotAllocate()
        {
            BoardLayout layout = _screen.Layout;
            Assert.IsTrue(_screen.SimulateDragStart(Location.Cell(15)));

            // Sweep across every board cell, the storage row and the sell zone.
            var path = new Vector2[_screen.Engine.CellCount + 2];
            for (int i = 0; i < _screen.Engine.CellCount; i++)
            {
                path[i] = new Vector2(layout.CellCentreX(i), layout.CellCentreY(i));
            }

            path[path.Length - 2] = new Vector2(layout.SlotCentreX(0), layout.SlotY);
            path[path.Length - 1] = new Vector2(layout.SellX, layout.SlotY);

            for (int i = 0; i < path.Length; i++)
            {
                _screen.SimulateDragFrame(path[i]); // warm-up (first-time caches)
            }

            Assert.That(() =>
            {
                for (int i = 0; i < path.Length; i++)
                {
                    _screen.SimulateDragFrame(path[i]);
                }
            }, Is.Not.AllocatingGCMemory());

            _screen.SimulateInterruption();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.IsTrue(_screen.ViewsMatchEngine(), "interrupted drag returns the item; nothing changed");
        }
    }
}
