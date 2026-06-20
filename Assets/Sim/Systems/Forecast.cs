// Greenkeeper.Sim — pure C#. MUST NOT reference UnityEngine.
using Greenkeeper.Sim.Config;
using Greenkeeper.Sim.Math;
using Greenkeeper.Sim.State;

namespace Greenkeeper.Sim.Systems
{
    /// <summary>A predicted day with its error band — what the player sees, not the truth (Phase 5.2).</summary>
    public struct ForecastDay
    {
        public int TargetDayIndex;
        public int DaysOut;
        public WeatherDay Predicted;   // best-guess values (may miss reality)
        public double TempBandF;       // +/- band on temperature (wider further out)
        public double RainBandMm;
        public double Confidence;      // 1 at "today", falling toward the horizon

        public bool PredictedHeatSpike;
        public bool PredictedStorm;
        public bool PredictedFrost;
    }

    /// <summary>
    /// A fallible view of the future (Phase 5.2 / GDD §7). The forecast is NOT the actual weather — the
    /// gap between them is the gameplay. Each value carries an error band that TIGHTENS as the day
    /// approaches (band = perDayBand * daysOut) and can still miss. Fully deterministic from the seed,
    /// so a run replays identically, and the error trajectory for a given day converges to reality as
    /// it nears (the prediction at day+1 is close; at day+5 it's loose).
    /// </summary>
    public sealed class Forecast
    {
        private readonly int _seed;
        private readonly WeatherSystem _weather;
        private readonly AgronomyTuning _t;

        public Forecast(int seed, AgronomyTuning tuning = null)
        {
            _seed = seed;
            _t = tuning ?? AgronomyTuning.Default;
            _weather = new WeatherSystem(seed);
        }

        public ForecastDay Predict(int currentDay, int targetDay)
        {
            int daysOut = targetDay - currentDay;
            if (daysOut < 0) daysOut = 0;

            WeatherDay actual = _weather.Generate(targetDay);

            // Fixed per-(target day) error directions in [-1,1]; band shrinks to 0 as the day arrives.
            double dTmax = ErrDir(targetDay, 1);
            double dTmin = ErrDir(targetDay, 2);
            double dRain = ErrDir(targetDay, 3);
            double dWet = ErrDir(targetDay, 4);
            double dHum = ErrDir(targetDay, 5);

            double tempBand = _t.ForecastTempBandPerDayF * daysOut;
            double rainBand = _t.ForecastRainBandPerDayMm * daysOut;
            double wetBand = _t.ForecastLeafWetBandPerDayHr * daysOut;
            double humBand = _t.ForecastHumidityBandPerDay * daysOut;

            var predicted = new WeatherDay
            {
                TmaxF = actual.TmaxF + dTmax * tempBand,
                TminF = actual.TminF + dTmin * tempBand,
                RainMm = Mathx.Max0(actual.RainMm + dRain * rainBand),
                LeafWetnessHrs = Mathx.Clamp(actual.LeafWetnessHrs + dWet * wetBand, 0, 24),
                Humidity = Mathx.Clamp(actual.Humidity + dHum * humBand, 0, 1),
                SolarRa = actual.SolarRa,
            };

            return new ForecastDay
            {
                TargetDayIndex = targetDay,
                DaysOut = daysOut,
                Predicted = predicted,
                TempBandF = tempBand,
                RainBandMm = rainBand,
                Confidence = 1.0 - Mathx.Clamp01((double)daysOut / System.Math.Max(1, _t.ForecastHorizonDays)),
                PredictedHeatSpike = WeatherEvents.IsHeatSpike(predicted, _t),
                PredictedStorm = WeatherEvents.IsStorm(predicted, _t),
                PredictedFrost = WeatherEvents.IsFrost(predicted, _t),
            };
        }

        /// <summary>The next ForecastHorizonDays of forecast, starting tomorrow.</summary>
        public ForecastDay[] Upcoming(int currentDay)
        {
            int n = _t.ForecastHorizonDays;
            var days = new ForecastDay[n];
            for (int i = 0; i < n; i++) days[i] = Predict(currentDay, currentDay + i + 1);
            return days;
        }

        // Deterministic error direction in [-1,1] for a given target day + field.
        private double ErrDir(int targetDay, ulong field)
        {
            var rng = new Rng((ulong)unchecked((long)_seed * 6364136223846793005L + targetDay)).Fork(field);
            return rng.NextDouble() * 2.0 - 1.0;
        }
    }
}
