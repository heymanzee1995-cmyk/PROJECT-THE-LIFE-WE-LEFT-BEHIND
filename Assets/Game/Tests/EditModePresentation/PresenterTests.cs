using System.Collections.Generic;
using Game.Data;
using Game.Domain;
using NUnit.Framework;

namespace Game.Presentation.Tests
{
    /// <summary>Gesture outcomes against the real content file and the real engine.</summary>
    public class PresenterTests
    {
        private MergeContent _content;
        private MergeEngine _engine;
        private Ledger _ledger;
        private BoardPresenter _presenter;

        private static InitialPlacement At(string chain, int tier, int cell) => new InitialPlacement(new ItemType(chain, tier), cell);

        private void Start(params InitialPlacement[] layout)
        {
            _content = ContentFile.LoadValid();
            _ledger = new Ledger();
            _engine = MergeEngine.CreateNew(_content.Config, 7, layout, _ledger);
            _presenter = new BoardPresenter(_engine, _content.Catalog);
        }

        private ItemId Id(int cell) => _engine.GetCell(cell).Id;

        private string State()
        {
            EngineSnapshot s = _engine.CreateSnapshot();
            var parts = new List<string> { s.NextItemId.ToString(), s.RngState.ToString(), s.InventoryCapacity.ToString() };
            foreach (ItemRecord r in s.Items)
            {
                parts.Add(r.Id + ":" + r.ChainId + r.Tier + "@" + r.Area + "/" + r.Index + "g" + r.GeneratorCharges + "/" + r.GeneratorRechargeStartMs);
            }

            return string.Join(";", parts);
        }

        private void AssertBounceUnchanged(InteractionResult r, bool invalidFeedback, string before)
        {
            Assert.AreEqual(InteractionKind.Bounced, r.Kind);
            Assert.AreEqual(invalidFeedback, r.InvalidFeedback);
            Assert.AreEqual(before, State(), "a bounce changes nothing");
            Assert.That(InvariantChecker.Check(_engine, _ledger, true), Is.Empty);
        }

        [Test]
        public void Hover_ShowsTheRightHighlight()
        {
            Start(At("garden", 1, 0), At("garden", 1, 1), At("tea", 1, 2), At("toolbox", 1, 3));
            ItemId seed = Id(0);
            Location from = Location.Cell(0);
            Assert.AreEqual(HoverKind.Return, _presenter.Hover(from, seed, PointerTarget.AtCell(0)));
            Assert.AreEqual(HoverKind.Merge, _presenter.Hover(from, seed, PointerTarget.AtCell(1)));
            Assert.AreEqual(HoverKind.Invalid, _presenter.Hover(from, seed, PointerTarget.AtCell(2)));
            Assert.AreEqual(HoverKind.Invalid, _presenter.Hover(from, seed, PointerTarget.AtCell(3)));
            Assert.AreEqual(HoverKind.Move, _presenter.Hover(from, seed, PointerTarget.AtCell(10)));
            Assert.AreEqual(HoverKind.Invalid, _presenter.Hover(from, seed, PointerTarget.AtCell(36)), "locked");
            Assert.AreEqual(HoverKind.Store, _presenter.Hover(from, seed, PointerTarget.Inventory));
            Assert.AreEqual(HoverKind.Sell, _presenter.Hover(from, seed, PointerTarget.Sell));
            Assert.AreEqual(HoverKind.Invalid, _presenter.Hover(Location.Cell(3), Id(3), PointerTarget.Sell), "generators can't be sold");
            Assert.AreEqual(HoverKind.None, _presenter.Hover(from, seed, PointerTarget.Nothing));
            Assert.AreEqual(HoverKind.None, _presenter.Hover(from, new ItemId(999), PointerTarget.AtCell(1)), "stale pick-up");
        }

        [Test]
        public void DropOnMatchingItem_Merges()
        {
            Start(At("garden", 1, 0), At("garden", 1, 1));
            InteractionResult r = _presenter.Drop(Location.Cell(0), Id(0), PointerTarget.AtCell(1));
            Assert.AreEqual(InteractionKind.Merged, r.Kind);
            Assert.AreEqual(new ItemType("garden", 2), _engine.GetCell(1).Type);
            Assert.AreEqual(r.Item, _engine.GetCell(1).Id);
            Assert.IsNull(_engine.GetCell(0));
        }

        [Test]
        public void DropOnEmptyCell_Moves()
        {
            Start(At("garden", 1, 0));
            ItemId id = Id(0);
            Assert.AreEqual(InteractionKind.Moved, _presenter.Drop(Location.Cell(0), id, PointerTarget.AtCell(24)).Kind);
            Assert.AreEqual(id, _engine.GetCell(24).Id);
        }

        [Test]
        public void DropOnIncompatibleItem_BouncesWithInvalidFeedback_AndChangesNothing()
        {
            Start(At("garden", 1, 0), At("tea", 1, 1), At("garden", 2, 2), At("garden", 8, 3), At("garden", 8, 4));
            string before = State();
            InteractionResult r = _presenter.Drop(Location.Cell(0), Id(0), PointerTarget.AtCell(1));
            AssertBounceUnchanged(r, true, before);
            Assert.AreEqual(ActionError.IncompatibleTarget, r.Error);
            Assert.AreEqual(Messages.NoMatch, r.Message);
            AssertBounceUnchanged(_presenter.Drop(Location.Cell(0), Id(0), PointerTarget.AtCell(2)), true, before);
            AssertBounceUnchanged(_presenter.Drop(Location.Cell(3), Id(3), PointerTarget.AtCell(4)), true, before);
        }

        [Test]
        public void DropOnOwnCell_LockedCell_OrOutsideTheBoard_Bounces()
        {
            Start(At("garden", 1, 0));
            string before = State();
            AssertBounceUnchanged(_presenter.Drop(Location.Cell(0), Id(0), PointerTarget.AtCell(0)), false, before);
            InteractionResult locked = _presenter.Drop(Location.Cell(0), Id(0), PointerTarget.AtCell(37));
            AssertBounceUnchanged(locked, true, before);
            Assert.AreEqual(Messages.Locked, locked.Message);
            AssertBounceUnchanged(_presenter.Drop(Location.Cell(0), Id(0), PointerTarget.Nothing), false, before);
        }

        [Test]
        public void StoreAndRetrieve_ThroughTheInventory()
        {
            Start(At("tea", 3, 5));
            ItemId id = Id(5);
            Assert.AreEqual(InteractionKind.Stored, _presenter.Drop(Location.Cell(5), id, PointerTarget.Inventory).Kind);
            Assert.AreEqual(id, _engine.GetSlot(0).Id);
            Assert.AreEqual(HoverKind.Return, _presenter.Hover(Location.Slot(0), id, PointerTarget.Inventory));
            Assert.AreEqual(HoverKind.Move, _presenter.Hover(Location.Slot(0), id, PointerTarget.AtCell(9)));

            string before = State();
            AssertBounceUnchanged(_presenter.Drop(Location.Slot(0), id, PointerTarget.Inventory), false, before);

            Assert.AreEqual(InteractionKind.Retrieved, _presenter.Drop(Location.Slot(0), id, PointerTarget.AtCell(9)).Kind);
            Assert.AreEqual(id, _engine.GetCell(9).Id);
            Assert.IsNull(_engine.GetSlot(0));
        }

        [Test]
        public void StoredItemDroppedOnABoardItem_ReturnsToItsSlot_EvenIfItWouldMerge()
        {
            Start(At("garden", 1, 0), At("garden", 1, 1));
            ItemId stored = Id(0);
            _presenter.Drop(Location.Cell(0), stored, PointerTarget.Inventory);
            Assert.AreEqual(HoverKind.Invalid, _presenter.Hover(Location.Slot(0), stored, PointerTarget.AtCell(1)));
            string before = State();
            InteractionResult r = _presenter.Drop(Location.Slot(0), stored, PointerTarget.AtCell(1));
            AssertBounceUnchanged(r, true, before);
            Assert.AreEqual(ActionError.TargetOccupied, r.Error);
            Assert.AreEqual(stored, _engine.GetSlot(0).Id);
        }

        [Test]
        public void StoringIntoFullInventory_Bounces()
        {
            Start(At("tea", 1, 0), At("tea", 2, 1), At("tea", 3, 2), At("tea", 4, 3), At("tea", 5, 4));
            for (int cell = 0; cell < 4; cell++)
            {
                _presenter.Drop(Location.Cell(cell), Id(cell), PointerTarget.Inventory);
            }

            Assert.AreEqual(HoverKind.Invalid, _presenter.Hover(Location.Cell(4), Id(4), PointerTarget.Inventory));
            string before = State();
            InteractionResult r = _presenter.Drop(Location.Cell(4), Id(4), PointerTarget.Inventory);
            AssertBounceUnchanged(r, true, before);
            Assert.AreEqual(Messages.StorageFull, r.Message);
        }

        [Test]
        public void Selling_RemovesTheItemAndReportsTheValue_ButNeverAGenerator()
        {
            Start(At("tea", 4, 0), At("toolbox", 1, 1), At("memento", 2, 2));
            InteractionResult sold = _presenter.Drop(Location.Cell(0), Id(0), PointerTarget.Sell);
            Assert.AreEqual(InteractionKind.Sold, sold.Kind);
            Assert.AreEqual(7, sold.Value, "tea level 4 sells for 7 in the content file");
            StringAssert.Contains("Teapot", sold.Message);
            Assert.IsNull(_engine.GetCell(0));

            string before = State();
            InteractionResult gen = _presenter.Drop(Location.Cell(1), Id(1), PointerTarget.Sell);
            AssertBounceUnchanged(gen, true, before);
            Assert.AreEqual(Messages.CannotSellGenerator, gen.Message);

            ItemId memento = Id(2);
            _presenter.Drop(Location.Cell(2), memento, PointerTarget.Inventory);
            Assert.AreEqual(InteractionKind.Sold, _presenter.Drop(Location.Slot(0), memento, PointerTarget.Sell).Kind);
            Assert.AreEqual(1, _engine.ItemCount, "only the generator is left");
        }

        [Test]
        public void TappingAGenerator_Spawns_ThenReportsEmpty_WithTime()
        {
            Start(At("toolbox", 1, 17));
            ItemId gen = Id(17);
            for (int i = 0; i < 10; i++)
            {
                InteractionResult r = _presenter.Tap(Location.Cell(17), gen, 0);
                Assert.AreEqual(InteractionKind.Spawned, r.Kind);
                Assert.IsNotNull(_engine.GetCell(r.Cell));
            }

            InteractionResult empty = _presenter.Tap(Location.Cell(17), gen, 5000);
            Assert.AreEqual(InteractionKind.GeneratorEmpty, empty.Kind);
            Assert.AreEqual(25000, empty.WaitMs, "30 s interval, 5 s elapsed");
            Assert.AreEqual(Messages.NextChargePrefix + "0:25", empty.Message);
            Assert.IsTrue(empty.InvalidFeedback);
        }

        [Test]
        public void TappingAGeneratorOnAFullBoard_ReportsBoardFull_AndChangesNothing()
        {
            var layout = new List<InitialPlacement> { At("toolbox", 1, 17) };
            for (int cell = 0; cell < 35; cell++)
            {
                if (cell != 17)
                {
                    layout.Add(cell % 2 == 0 ? At("garden", 8, cell) : At("tea", 7, cell));
                }
            }

            Start(layout.ToArray());
            string before = State();
            InteractionResult r = _presenter.Tap(Location.Cell(17), Id(17), 0);
            Assert.AreEqual(InteractionKind.BoardFull, r.Kind);
            Assert.AreEqual(Messages.BoardFull, r.Message);
            Assert.IsTrue(r.InvalidFeedback);
            Assert.AreEqual(before, State());
        }

        [Test]
        public void TappingAnItem_ShowsItsDetails()
        {
            Start(At("tea", 4, 0), At("toolbox", 1, 1));
            InteractionResult info = _presenter.Tap(Location.Cell(0), Id(0), 0);
            Assert.AreEqual(InteractionKind.Info, info.Kind);
            Assert.AreEqual("Teapot  ·  level 4 of 7  ·  sells for 7", info.Message);
            Assert.AreEqual("Old Toolbox  ·  level 1 of 3  ·  10 charges", _presenter.Describe(_engine.GetCell(1), 0));
            Assert.AreEqual(InteractionKind.None, _presenter.Tap(Location.Cell(9), new ItemId(5), 0).Kind, "stale tap does nothing");
        }
    }
}
