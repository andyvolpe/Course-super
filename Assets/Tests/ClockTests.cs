// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 1.1 — clock + resolve-pipeline skeleton + interruptible skip.</summary>
    [TestFixture]
    public class ClockTests
    {
        private static GameDirector NewDirector(int seed)
        {
            // These exercise the bare clock/skip mechanics, so isolate them from weather interrupts.
            return new GameDirector(CourseFactory.Build(CourseConfig.Mvp(), seed), seed)
            {
                WeatherInterruptsEnabled = false
            };
        }

        [Test]
        public void NinetyDays_AdvancesToDay90()
        {
            var d = NewDirector(1234);
            for (int i = 0; i < 90; i++) d.ResolveDay();
            Assert.AreEqual(90, d.Clock.DayIndex);
        }

        [Test]
        public void SameSeed_ProducesSameDaySequence()
        {
            var a = NewDirector(7);
            var b = NewDirector(7);
            for (int i = 0; i < 90; i++)
            {
                Assert.AreEqual(a.Clock.DayIndex, b.Clock.DayIndex, $"day index diverged at step {i}");
                a.ResolveDay();
                b.ResolveDay();
            }
            Assert.AreEqual(90, a.Clock.DayIndex);
            Assert.AreEqual(90, b.Clock.DayIndex);
        }

        [Test]
        public void Season_TracksDayIndex()
        {
            var d = NewDirector(1);
            Assert.AreEqual(Season.Spring, d.Clock.Season);
            d.Clock.JumpTo(GameClock.DaysPerSeason);       // 90
            Assert.AreEqual(Season.Summer, d.Clock.Season);
            d.Clock.JumpTo(GameClock.DaysPerSeason * 3);    // 270
            Assert.AreEqual(Season.Winter, d.Clock.Season);
        }

        [Test]
        public void SkipUntil_StopsOnForcedInterrupt()
        {
            var d = NewDirector(99);
            // Interrupt source fires after 5 days; the predicate never stops on its own.
            int resolved = d.SkipUntil(dir =>
            {
                if (dir.Clock.DayIndex >= 5) dir.RaiseInterrupt();
                return false;
            });
            Assert.IsTrue(d.InterruptRaised);
            Assert.AreEqual(5, resolved, "resolves days 0..4, then the interrupt raised at day 5 stops the loop");
        }

        [Test]
        public void SkipUntil_StopsOnPredicate()
        {
            var d = NewDirector(5);
            int resolved = d.SkipUntil(dir => dir.Clock.DayIndex >= 30);
            Assert.AreEqual(30, resolved);
            Assert.AreEqual(30, d.Clock.DayIndex);
            Assert.IsFalse(d.InterruptRaised, "interrupt flag stays false by default");
        }
    }
}
