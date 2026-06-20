using System.Collections.Generic;
using UnityEngine;
using Greenkeeper.Sim.Config;

namespace Greenkeeper.Unity.Config
{
    /// <summary>
    /// ScriptableObject WRAPPER for authoring a course in the editor, converting to the plain-C#
    /// <see cref="CourseConfig"/> the sim consumes. Either generate the canonical MVP layout, or hand
    /// author zones. ScriptableObject (UnityEngine) never leaks into Greenkeeper.Sim.
    /// </summary>
    [CreateAssetMenu(menuName = "Greenkeeper/Course Config", fileName = "CourseConfig")]
    public sealed class CourseConfigSO : ScriptableObject
    {
        public string courseName = "MVP Cash-Cow Course";
        public int holes = 18;

        [Tooltip("When true, ignore the authored zone list and generate the canonical 18-hole MVP layout.")]
        public bool useMvpLayout = true;

        public GrassProfileSO grass;

        [System.Serializable]
        public struct ZoneEntry
        {
            public string id;
            public ZoneType type;
            public SoilType soil;
            public int holeNumber;
            public int gridSize;
        }

        public List<ZoneEntry> zones = new List<ZoneEntry>();

        public CourseConfig ToConfig()
        {
            if (useMvpLayout)
            {
                var mvp = CourseConfig.Mvp();
                mvp.Name = courseName;
                if (grass != null) mvp.Grass = grass.ToConfig();
                return mvp;
            }

            var cfg = new CourseConfig
            {
                Name = courseName,
                Holes = holes,
                Grass = grass != null ? grass.ToConfig() : GrassProfile.Mvp(),
                Zones = new List<ZoneSpec>(),
            };
            foreach (var z in zones)
                cfg.Zones.Add(new ZoneSpec(z.id, z.type, z.soil, z.holeNumber, Mathf.Max(1, z.gridSize)));
            return cfg;
        }
    }
}
