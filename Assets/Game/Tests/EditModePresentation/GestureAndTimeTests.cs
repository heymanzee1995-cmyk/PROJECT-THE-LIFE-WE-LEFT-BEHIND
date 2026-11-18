using NUnit.Framework;

namespace Game.Presentation.Tests
{
    public class GestureAndTimeTests
    {
        [Test]
        public void PressAndRelease_WithoutMoving_IsATap()
        {
            var g = new DragGesture(10);
            g.Press(100, 100);
            Assert.AreEqual(GestureEvent.None, g.Move(105, 103), "below threshold");
            Assert.AreEqual(GestureEvent.Tap, g.Release());
            Assert.IsFalse(g.IsPressed);
        }

        [Test]
        public void MovingPastThreshold_StartsDrag_ThenReleaseDrops()
        {
            var g = new DragGesture(10);
            g.Press(100, 100);
            Assert.AreEqual(GestureEvent.DragStarted, g.Move(110, 100));
            Assert.IsTrue(g.IsDragging);
            Assert.AreEqual(GestureEvent.None, g.Move(200, 200), "DragStarted fires once");
            Assert.AreEqual(GestureEvent.Drop, g.Release());
        }

        [Test]
        public void Cancel_EndsTheGesture_AndIgnoresTheLaterRelease()
        {
            var g = new DragGesture(10);
            g.Press(0, 0);
            g.Move(50, 0);
            Assert.AreEqual(GestureEvent.Cancelled, g.Cancel());
            Assert.AreEqual(GestureEvent.None, g.Release());
            Assert.AreEqual(GestureEvent.None, g.Cancel());
        }

        [Test]
        public void ReleaseOrMove_WithoutPress_DoNothing()
        {
            var g = new DragGesture(10);
            Assert.AreEqual(GestureEvent.None, g.Move(100, 100));
            Assert.AreEqual(GestureEvent.None, g.Release());
        }

        [TestCase(0, "0:00")]
        [TestCase(1, "0:01")]
        [TestCase(1000, "0:01")]
        [TestCase(1001, "0:02")]
        [TestCase(59000, "0:59")]
        [TestCase(60000, "1:00")]
        [TestCase(605000, "10:05")]
        [TestCase(-5, "0:00")]
        [TestCase(7200000, "120:00")]
        public void TimeText_FormatsRoundingUp(long ms, string expected)
        {
            Assert.AreEqual(expected, TimeText.Format(ms));
        }

        [Test]
        public void TimeText_ReturnsTheSameCachedString()
        {
            Assert.AreSame(TimeText.Format(42000), TimeText.Format(41500));
        }
    }
}

namespace Game.Presentation.Tests
{
    public class TextWrapTests
    {
        [Test]
        public void ShortText_IsUnchanged() => Assert.AreEqual("Those don't match.", TextWrap.Wrap("Those don't match.", 30));

        [Test]
        public void LongText_WrapsAtWordBoundaries_WithinTheLimit()
        {
            string wrapped = TextWrap.Wrap(Messages.BoardFull, 24);
            foreach (string line in wrapped.Split('\n'))
            {
                Assert.That(line.Length, Is.LessThanOrEqualTo(24), line);
                Assert.IsFalse(line.StartsWith(" ") || line.EndsWith(" "));
            }

            Assert.AreEqual(Messages.BoardFull, wrapped.Replace('\n', ' '));
        }

        [Test]
        public void AWordLongerThanTheLimit_GetsItsOwnLine()
        {
            Assert.AreEqual("a\nextraordinarily\nb", TextWrap.Wrap("a extraordinarily b", 6));
        }
    }
}
