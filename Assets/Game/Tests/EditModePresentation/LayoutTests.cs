using NUnit.Framework;

namespace Game.Presentation.Tests
{
    public class LayoutTests
    {
        private static readonly LayoutSpec Spec = new LayoutSpec();

        /// <summary>A phone in dp, converted to pixels at the given dpi, with 24 dp status and 48 dp navigation bars.</summary>
        private static ScreenMetrics Phone(float widthDp, float heightDp, float dpi)
        {
            float d = dpi / 160f;
            return new ScreenMetrics(widthDp * d, heightDp * d, dpi, 0, 48 * d, widthDp * d, (heightDp - 72) * d);
        }

        // Matches the estimate table in Docs/VisionReference.md (same reserved space).
        [TestCase(360, 640, 160, 37)]
        [TestCase(360, 800, 160, 57)]
        [TestCase(393, 852, 440, 64)]
        [TestCase(412, 915, 420, 71)]
        [TestCase(600, 960, 240, 77)]
        public void CellSize_MatchesVisionReferenceEstimates(int widthDp, int heightDp, int dpi, int expectedDp)
        {
            var layout = new BoardLayout(Phone(widthDp, heightDp, dpi), Spec, 5, 8, 4);
            Assert.AreEqual(expectedDp, layout.CellDp, 1.0, "cell dp");
            Assert.IsTrue(layout.HeightLimited, "5x8 is height-limited on all these screens");
        }

        [Test]
        public void CompactPhone_IsFlaggedBelowComfort_OthersAreNot()
        {
            Assert.IsTrue(new BoardLayout(Phone(360, 640, 160), Spec, 5, 8, 4).BelowComfort);
            Assert.IsFalse(new BoardLayout(Phone(360, 800, 160), Spec, 5, 8, 4).BelowComfort);
        }

        [Test]
        public void EveryCellCentre_HitTestsToItself_AndOutsideIsMinusOne()
        {
            var layout = new BoardLayout(Phone(393, 852, 440), Spec, 5, 8, 4);
            for (int cell = 0; cell < 40; cell++)
            {
                Assert.AreEqual(cell, layout.CellAt(layout.CellCentreX(cell), layout.CellCentreY(cell)));
            }

            Assert.AreEqual(0, layout.CellAt(layout.BoardLeft + 1, layout.BoardTop - 1), "top-left is cell 0");
            Assert.AreEqual(-1, layout.CellAt(layout.BoardLeft - 1, layout.BoardTop - 1));
            Assert.AreEqual(-1, layout.CellAt(layout.BoardLeft + 1, layout.BoardTop + 1));
            Assert.AreEqual(-1, layout.CellAt(layout.BoardLeft + layout.BoardWidth + 1, layout.BoardTop - 1));
            Assert.AreEqual(-1, layout.CellAt(layout.BoardLeft + 1, layout.BoardTop - layout.BoardHeight - 1));
        }

        [Test]
        public void BoardSlotsAndSellZone_StayInsideTheSafeArea_AndDoNotOverlap()
        {
            ScreenMetrics s = Phone(360, 800, 160);
            var layout = new BoardLayout(s, Spec, 5, 8, 4);
            Assert.That(layout.BoardLeft, Is.GreaterThanOrEqualTo(s.SafeX));
            Assert.That(layout.BoardLeft + layout.BoardWidth, Is.LessThanOrEqualTo(s.SafeX + s.SafeWidth));
            Assert.That(layout.BoardTop, Is.LessThanOrEqualTo(s.SafeY + s.SafeHeight));
            float boardBottom = layout.BoardTop - layout.BoardHeight;
            Assert.That(boardBottom, Is.GreaterThan(layout.SlotY + layout.SlotPx * 0.5f), "board clears the bottom bar");
            Assert.That(layout.SlotY - layout.SlotPx * 0.5f, Is.GreaterThanOrEqualTo(s.SafeY));

            for (int slot = 0; slot < 4; slot++)
            {
                Assert.AreEqual(slot, layout.SlotAt(layout.SlotCentreX(slot), layout.SlotY));
                Assert.IsTrue(layout.IsOverInventory(layout.SlotCentreX(slot), layout.SlotY));
                Assert.IsFalse(layout.IsOverSell(layout.SlotCentreX(slot), layout.SlotY));
            }

            Assert.IsTrue(layout.IsOverSell(layout.SellX, layout.SlotY));
            Assert.AreEqual(-1, layout.SlotAt(layout.SellX, layout.SlotY));
            Assert.That(layout.SlotCentreX(3) + layout.SlotPx * 0.5f, Is.LessThan(layout.SellX - layout.SlotPx * 0.5f),
                "last slot does not overlap the sell zone");
        }

        [Test]
        public void NotchSafeArea_PushesTheBoardDown()
        {
            float d = 2.75f;
            var full = new ScreenMetrics(393 * d, 852 * d, 440, 0, 0, 393 * d, 852 * d);
            var notched = new ScreenMetrics(393 * d, 852 * d, 440, 0, 34 * d, 393 * d, (852 - 34 - 47) * d);
            var a = new BoardLayout(full, Spec, 5, 8, 4);
            var b = new BoardLayout(notched, Spec, 5, 8, 4);
            Assert.That(b.BoardTop, Is.LessThan(a.BoardTop));
            Assert.That(b.CellPx, Is.LessThanOrEqualTo(a.CellPx));
        }

        [Test]
        public void UnknownDpi_UsesFallback_AndIsReported()
        {
            var layout = new BoardLayout(ScreenMetrics.FullScreen(1080, 2400, 0), Spec, 5, 8, 4);
            Assert.IsTrue(layout.UsedFallbackDpi);
            Assert.AreEqual(1f, layout.Density);
        }

        [Test]
        public void AlternativeBoard7x9_AlsoLaysOut()
        {
            var layout = new BoardLayout(Phone(393, 852, 440), Spec, 7, 9, 4);
            Assert.AreEqual(62, layout.CellAt(layout.CellCentreX(62), layout.CellCentreY(62)));
            Assert.That(layout.CellDp, Is.LessThan(new BoardLayout(Phone(393, 852, 440), Spec, 5, 8, 4).CellDp));
        }
    }
}
