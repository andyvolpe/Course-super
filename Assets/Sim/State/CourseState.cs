// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using System.Collections.Generic;
using Greenkeeper.Sim.Config;

namespace Greenkeeper.Sim.State
{
    /// <summary>Live state for the whole course: the zones plus a fast lookup by id (TDD §2).</summary>
    public sealed class CourseState
    {
        public List<ZoneState> Zones = new List<ZoneState>();
        private readonly Dictionary<string, ZoneState> _byId = new Dictionary<string, ZoneState>();

        public void Add(ZoneState z)
        {
            Zones.Add(z);
            _byId[z.Id] = z;
        }

        public ZoneState Get(string id) => _byId.TryGetValue(id, out var z) ? z : null;

        public IEnumerable<ZoneState> Greens
        {
            get { foreach (var z in Zones) if (z.Type == ZoneType.Green) yield return z; }
        }

        public CourseState Clone()
        {
            var c = new CourseState();
            foreach (var z in Zones) c.Add(z.Clone());
            return c;
        }

        public void CollectStateValues(List<double> into)
        {
            foreach (var z in Zones) z.CollectStateValues(into);
        }
    }
}
