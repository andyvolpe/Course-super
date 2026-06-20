// Greenkeeper.Tests — EditMode (Unity) / headless (dotnet). Pure NUnit.
using System.Linq;
using NUnit.Framework;
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Crew;
using Greenkeeper.Sim.State;
using Greenkeeper.Sim.Systems;

namespace Greenkeeper.Tests
{
    /// <summary>Phase 4.1 — crew, tasks, and the hour budget that forces triage.</summary>
    [TestFixture]
    public class CrewWindowTests
    {
        [Test]
        public void DefaultCrew_BudgetIsAboutThirtyHours()
        {
            var w = new MaintenanceWindow(CrewMember.DefaultCrew());
            Assert.AreEqual(30.0, w.BudgetHours, 1e-9, "5 hands x 6 hrs = 30 crew-hours/window");
        }

        [Test]
        public void CannotDoEverything_OverAssignmentIsRejected_ForcingTriage()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 1);
            var w = new MaintenanceWindow(CrewMember.DefaultCrew());

            int attempted = 0, accepted = 0;
            // Walk-mow every green (~10.8h) + rake all bunkers (4h) + spray every green (9h)
            // + fertilize every green (7.2h) = ~31h > 30h budget.
            foreach (var g in course.Greens) { attempted++; if (w.TryAssign(TaskCatalog.WalkMow(g.Id))) accepted++; }
            attempted++; if (w.TryAssign(TaskCatalog.RakeBunkers())) accepted++;
            foreach (var g in course.Greens) { attempted++; if (w.TryAssign(TaskCatalog.Spray(g.Id))) accepted++; }
            foreach (var g in course.Greens) { attempted++; if (w.TryAssign(TaskCatalog.Fertilize(g.Id))) accepted++; }

            Assert.IsTrue(w.CouldNotFitEverything, "the plan must not fit — that's the squeeze");
            Assert.Greater(w.Rejected.Count, 0, "over-budget tasks are rejected/flagged");
            Assert.Less(accepted, attempted, "you physically cannot do everything");
            Assert.LessOrEqual(w.UsedHours, w.BudgetHours + 1e-9, "you can never exceed the budget");
        }

        [Test]
        public void UnderBudget_PlanFitsAndAppliesEffects()
        {
            var course = CourseFactory.Build(CourseConfig.Mvp(), 1);
            var w = new MaintenanceWindow(CrewMember.DefaultCrew());

            Assert.IsTrue(w.TryAssign(TaskCatalog.WalkMow("green-01")));
            Assert.IsTrue(w.TryAssign(TaskCatalog.Spray("green-01")));
            Assert.IsTrue(w.TryAssign(TaskCatalog.Fertilize("green-01")));
            Assert.IsFalse(w.CouldNotFitEverything);

            var plan = w.ToDayPlan(course);
            var a = plan.For("green-01");
            Assert.IsTrue(a.Mow, "walk-mow applied");
            Assert.IsTrue(a.Spray, "spray applied");
            Assert.Greater(a.FertilizerN, 0.0, "fertilizer applied");
        }

        [Test]
        public void ResolveWindow_AdvancesTheDay_AndRunsTheSim()
        {
            var cfg = CourseConfig.Mvp();
            var course = CourseFactory.Build(cfg, 7);
            var dir = new GameDirector(course, 7, cfg.Tuning, cfg.Grass);
            var w = new MaintenanceWindow(CrewMember.DefaultCrew());
            w.TryAssign(TaskCatalog.Triplex());        // course-wide mow of all greens
            w.TryAssign(TaskCatalog.Water("green-01", 10));

            dir.ResolveWindow(w);
            Assert.AreEqual(1, dir.Clock.DayIndex, "the window resolved one day");
        }
    }
}
