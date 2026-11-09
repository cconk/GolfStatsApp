using System;
using System.Collections.Generic;
using System.Linq;

namespace GolfStatsApp.Server.Services.Prediction
{
    /// <summary>
    /// Generates generic field players that fill the tournament field around the user-selected golfers, so finish
    /// positions, win odds and the cut are measured against a realistic full field rather than only the modeled group.
    /// Each player gets a random true skill and a set of random recent rounds; the model inputs (skill, sigma) are then
    /// estimated from those rounds with the same shrinkage used for real golfers. Deterministic for a given seed.
    /// </summary>
    public static class FieldFiller
    {
        public const string IdPrefix = "field-";

        public static List<GolferInputs> Generate(int count, int seed, PredictiveModelingOptions options)
        {
            var result = new List<GolferInputs>(Math.Max(0, count));
            if (count <= 0) return result;

            var rng = new Random(MonteCarloEngine.DeriveSeed(seed, -2));
            var rounds = Math.Max(1, options.FieldRecentRounds);
            var samples = new double[rounds];
            for (var i = 0; i < count; i++)
            {
                var trueSkill = Normal(rng) * options.FieldSkillSd;
                var trueSigma = Math.Max(2.0, options.FieldSigma + Normal(rng) * 0.3);
                for (var r = 0; r < rounds; r++)
                    samples[r] = trueSkill + trueSigma * Normal(rng); // strokes better than field in a recent round

                var mean = samples.Average();
                var skill = mean * rounds / (rounds + options.ShrinkageRounds);
                var sigma = rounds >= 10
                    ? Math.Sqrt(samples.Sum(x => (x - mean) * (x - mean)) / (rounds - 1))
                    : options.FieldSigma;

                result.Add(new GolferInputs
                {
                    PlayerId = $"{IdPrefix}{i + 1}",
                    PlayerName = $"Field Player {i + 1}",
                    Skill = skill,
                    Sigma = sigma,
                });
            }
            return result;
        }

        private static double Normal(Random rng)
        {
            double u1;
            do u1 = rng.NextDouble(); while (u1 <= double.Epsilon);
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * rng.NextDouble());
        }
    }
}
