using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Unity.Save
{
    /// <summary>
    /// Versioned save container. <see cref="SchemaVersion"/> is present from day one (Decision Log §1)
    /// so migrations are possible, and <see cref="WeatherSeed"/> is stored for deterministic replay
    /// (Decision Log). The Sim stays serialization-agnostic: this Unity-layer DTO snapshots Sim state.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentSchemaVersion = 1;

        public int SchemaVersion = CurrentSchemaVersion;
        public int WeatherSeed;
        public int DayIndex;
        public List<ZoneSnapshot> Zones = new List<ZoneSnapshot>();

        [Serializable]
        public sealed class ZoneSnapshot
        {
            public string Id;
            public double SoilMoisturePct;
            public double DensityPct;
            public double OrganicMatterPct;
            public double TurfDebtPct;
            public double CarbReservesPct;
            public double NitrogenPct;
            public double SoilTempF;
            public double GddAccum;
            public double GrainPct;
            public double Stimp;
            public double FirmnessPct;
            public double[] CellPressure;
            public double[] CellInfection;
            public double[] CellExpression;
        }

        public static SaveData Capture(CourseState course, int weatherSeed, int dayIndex)
        {
            var data = new SaveData { WeatherSeed = weatherSeed, DayIndex = dayIndex };
            foreach (var z in course.Zones)
            {
                var snap = new ZoneSnapshot
                {
                    Id = z.Id,
                    SoilMoisturePct = z.SoilMoisturePct,
                    DensityPct = z.DensityPct,
                    OrganicMatterPct = z.OrganicMatterPct,
                    TurfDebtPct = z.TurfDebtPct,
                    CarbReservesPct = z.CarbReservesPct,
                    NitrogenPct = z.NitrogenPct,
                    SoilTempF = z.SoilTempF,
                    GddAccum = z.GddAccum,
                    GrainPct = z.GrainPct,
                    Stimp = z.Stimp,
                    FirmnessPct = z.FirmnessPct,
                    CellPressure = new double[z.Cells.Length],
                    CellInfection = new double[z.Cells.Length],
                    CellExpression = new double[z.Cells.Length],
                };
                for (int i = 0; i < z.Cells.Length; i++)
                {
                    snap.CellPressure[i] = z.Cells[i].Pressure;
                    snap.CellInfection[i] = z.Cells[i].Infection;
                    snap.CellExpression[i] = z.Cells[i].ExpressionSeverity;
                }
                data.Zones.Add(snap);
            }
            return data;
        }
    }

    /// <summary>
    /// JSON save/load to persistent storage via Unity's built-in JsonUtility (no external package).
    /// The SaveData shape is JsonUtility-friendly: [Serializable] classes, a List, and double[] arrays.
    /// </summary>
    public static class SaveSystem
    {
        public static string DefaultPath => Path.Combine(Application.persistentDataPath, "greenkeeper.save.json");

        public static void Save(SaveData data, string path = null)
        {
            path = path ?? DefaultPath;
            File.WriteAllText(path, Serialize(data));
        }

        public static SaveData Load(string path = null)
        {
            path = path ?? DefaultPath;
            if (!File.Exists(path)) return null;
            return Deserialize(File.ReadAllText(path));
        }

        // Exposed for round-trip testing without touching disk.
        public static string Serialize(SaveData data) => JsonUtility.ToJson(data, true);

        public static SaveData Deserialize(string json)
        {
            var data = JsonUtility.FromJson<SaveData>(json);
            if (data != null && data.SchemaVersion != SaveData.CurrentSchemaVersion)
                Debug.LogWarning($"[Greenkeeper] Save schemaVersion {data.SchemaVersion} != current {SaveData.CurrentSchemaVersion}; migration may be required.");
            return data;
        }
    }
}
