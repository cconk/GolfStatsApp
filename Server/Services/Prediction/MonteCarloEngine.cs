using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GolfStatsApp.Shared.Models;

namespace GolfStatsApp.Server.Services.Prediction
{
    /// <summary>Per-golfer model input produced by <see cref="ModelInputService"/>.</summary>
    public class GolferInputs
    {
        public string PlayerId { get; set; } = string.Empty;
        public string PlayerName { get; set; } = string.Empty;

        /// <summary>Expected strokes better than the field per round (positive = better), after shrinkage.</summary>
        public double Skill { get; set; }

        /// <summary>Round-to-round standard deviation in strokes.</summary>
        public double Sigma { get; set; } = 2.8;

        /// <summary>Actual strokes-to-par for rounds already played in the target tournament (index 0 = R1); null = simulate.</summary>
        public int?[] FixedRounds { get; set; } = Array.Empty<int?>();

        public List<string> Warnings { get; set; } = new();
    }

    public class EngineConfig
    {
        public int Simulations { get; set; } = PredictionLimits.DefaultSimulations;
        public int Seed { get; set; }
        public int Rounds { get; set; } = 4;
        public int CutAfterRound { get; set; } = 2;

        /// <summary>Top N and ties after <see cref="CutAfterRound"/>; null = no cut.</summary>
        public int? CutTopN { get; set; } = 65;

        /// <summary>Expected field-average strokes to par per round.</summary>
        public double FieldMeanToPar { get; set; } = 0.5;

        /// <summary>SD of the shared per-round "conditions" shock applied to every golfer.</summary>
        public double ConditionsSd { get; set; } = 0.8;

        /// <summary>Simulations per parallel chunk; fixed so results do not depend on thread count.</summary>
        public int ChunkSize { get; set; } = 500;
    }

    public class EngineGolferResult
    {
        public double MeanScoreToPar { get; set; }
        public double P10 { get; set; }
        public double P50 { get; set; }
        public double P90 { get; set; }
        public double MeanFinish { get; set; }
        public double PMakeCut { get; set; }
        public double PWin { get; set; }
        public double PTop5 { get; set; }
        public double PTop10 { get; set; }
        public double PTop20 { get; set; }
        public Dictionary<string, double> FinishBuckets { get; set; } = new();
    }

    /// <summary>
    /// Pure, seedable Monte Carlo tournament simulator (no I/O). See docs/predictive-modeling-model.md.
    /// Simulations are split into fixed-size chunks, each with an RNG derived from the master seed, and merged in
    /// chunk order so a given seed always yields identical output regardless of thread scheduling.
    /// </summary>
    public class MonteCarloEngine
    {
        private const int BucketCount = 7;

        public IReadOnlyList<EngineGolferResult> Run(IReadOnlyList<GolferInputs> golfers, EngineConfig config, CancellationToken ct = default)
        {
            if (golfers.Count == 0) throw new ArgumentException("At least one golfer is required.", nameof(golfers));
            if (config.Simulations < PredictionLimits.MinSimulations || config.Simulations > PredictionLimits.MaxSimulations)
                throw new ArgumentOutOfRangeException(nameof(config), $"Simulations must be between {PredictionLimits.MinSimulations} and {PredictionLimits.MaxSimulations}.");
            if (config.Rounds < 1 || config.ChunkSize < 1) throw new ArgumentOutOfRangeException(nameof(config));

            var g = golfers.Count;
            var sims = config.Simulations;
            var chunkSize = config.ChunkSize;
            var chunkCount = (sims + chunkSize - 1) / chunkSize;
            var totals = new int[g * sims];
            var accs = new ChunkAccumulator[chunkCount];
            var skill = new double[g];
            var sigma = new double[g];
            for (var i = 0; i < g; i++)
            {
                skill[i] = golfers[i].Skill;
                sigma[i] = golfers[i].Sigma;
            }

            Parallel.For(0, chunkCount, new ParallelOptions { CancellationToken = ct }, chunk =>
            {
                var start = chunk * chunkSize;
                var end = Math.Min(sims, start + chunkSize);
                accs[chunk] = RunChunk(golfers, skill, sigma, config, DeriveSeed(config.Seed, chunk), start, end, totals, ct);
            });
            ct.ThrowIfCancellationRequested();

            var merged = new ChunkAccumulator(g);
            foreach (var acc in accs) merged.Add(acc);

            var results = new EngineGolferResult[g];
            var buffer = new int[sims];
            for (var i = 0; i < g; i++)
            {
                Array.Copy(totals, i * sims, buffer, 0, sims);
                Array.Sort(buffer);
                var row = new EngineGolferResult
                {
                    MeanScoreToPar = merged.ScoreSum[i] / sims,
                    P10 = Percentile(buffer, 0.10),
                    P50 = Percentile(buffer, 0.50),
                    P90 = Percentile(buffer, 0.90),
                    MeanFinish = (double)merged.FinishSum[i] / sims,
                    PMakeCut = (double)merged.MadeCut[i] / sims,
                    PWin = (double)merged.Buckets[i * BucketCount] / sims,
                    PTop5 = (double)merged.Top5[i] / sims,
                    PTop10 = (double)merged.Top10[i] / sims,
                    PTop20 = (double)merged.Top20[i] / sims,
                };
                for (var b = 0; b < BucketCount; b++)
                    row.FinishBuckets[FinishBucketNames.All[b]] = (double)merged.Buckets[i * BucketCount + b] / sims;
                results[i] = row;
            }
            return results;
        }

        private static ChunkAccumulator RunChunk(IReadOnlyList<GolferInputs> golfers, double[] skill, double[] sigma, EngineConfig config,
            int seed, int start, int end, int[] totals, CancellationToken ct)
        {
            var g = golfers.Count;
            var sims = config.Simulations;
            var rounds = config.Rounds;
            var cutRound = Math.Min(config.CutAfterRound, rounds);
            var hasCut = config.CutTopN.HasValue && config.CutTopN.Value < g;
            var rng = new Random(seed);
            var normal = new NormalSampler(rng);
            var acc = new ChunkAccumulator(g);

            var cutScore = new int[g];
            var total = new int[g];
            var made = new bool[g];
            var keys = new double[g];
            var order = new int[g];
            var cutSorted = new int[g];
            var shocks = new double[rounds];

            for (var s = start; s < end; s++)
            {
                if (((s - start) & 255) == 0) ct.ThrowIfCancellationRequested();

                for (var r = 0; r < rounds; r++) shocks[r] = normal.Next() * config.ConditionsSd;

                for (var i = 0; i < g; i++)
                {
                    var fixedRounds = golfers[i].FixedRounds;
                    int sum = 0, atCut = 0;
                    for (var r = 0; r < rounds; r++)
                    {
                        int score;
                        if (r < fixedRounds.Length && fixedRounds[r].HasValue)
                            score = fixedRounds[r]!.Value;
                        else
                            score = (int)Math.Round(config.FieldMeanToPar - skill[i] + sigma[i] * normal.Next() + shocks[r], MidpointRounding.AwayFromZero);
                        sum += score;
                        if (r == cutRound - 1) atCut = sum;
                    }
                    total[i] = sum;
                    cutScore[i] = atCut;
                    totals[i * sims + s] = sum;
                    acc.ScoreSum[i] += sum;
                }

                var madeCount = g;
                if (hasCut)
                {
                    Array.Copy(cutScore, cutSorted, g);
                    Array.Sort(cutSorted);
                    var line = cutSorted[config.CutTopN!.Value - 1];
                    madeCount = 0;
                    for (var i = 0; i < g; i++)
                    {
                        made[i] = cutScore[i] <= line;
                        if (made[i]) madeCount++;
                    }
                }
                else
                {
                    for (var i = 0; i < g; i++) made[i] = true;
                }

                // Made-cut golfers rank by 72-hole total; missed-cut golfers rank after them by cut-round total.
                // A uniform [0, 0.5) tiebreak keeps integer ordering and breaks ties at random.
                for (var i = 0; i < g; i++)
                {
                    order[i] = i;
                    keys[i] = made[i]
                        ? total[i] + rng.NextDouble() * 0.5
                        : 1_000_000 + cutScore[i] + rng.NextDouble() * 0.5;
                }
                Array.Sort(keys, order);

                for (var pos = 0; pos < g; pos++)
                {
                    var i = order[pos];
                    var finish = pos + 1;
                    acc.FinishSum[i] += finish;
                    int bucket;
                    if (!made[i]) bucket = 6;
                    else
                    {
                        acc.MadeCut[i]++;
                        bucket = finish == 1 ? 0 : finish <= 5 ? 1 : finish <= 10 ? 2 : finish <= 20 ? 3 : finish <= 40 ? 4 : 5;
                        if (finish <= 5) acc.Top5[i]++;
                        if (finish <= 10) acc.Top10[i]++;
                        if (finish <= 20) acc.Top20[i]++;
                    }
                    acc.Buckets[i * BucketCount + bucket]++;
                }
            }
            return acc;
        }

        private static double Percentile(int[] sorted, double p)
        {
            if (sorted.Length == 1) return sorted[0];
            var pos = p * (sorted.Length - 1);
            var lo = (int)Math.Floor(pos);
            var hi = Math.Min(lo + 1, sorted.Length - 1);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        /// <summary>SplitMix64-based derivation of a per-chunk seed from the master seed.</summary>
        internal static int DeriveSeed(int master, int chunk)
        {
            unchecked
            {
                var z = (ulong)(uint)master * 0x9E3779B97F4A7C15UL + (ulong)(chunk + 1) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                z ^= z >> 31;
                return (int)(z & 0x7FFFFFFF);
            }
        }

        private sealed class ChunkAccumulator
        {
            public readonly double[] ScoreSum;
            public readonly long[] FinishSum;
            public readonly int[] MadeCut;
            public readonly int[] Top5;
            public readonly int[] Top10;
            public readonly int[] Top20;
            public readonly int[] Buckets;

            public ChunkAccumulator(int golfers)
            {
                ScoreSum = new double[golfers];
                FinishSum = new long[golfers];
                MadeCut = new int[golfers];
                Top5 = new int[golfers];
                Top10 = new int[golfers];
                Top20 = new int[golfers];
                Buckets = new int[golfers * BucketCount];
            }

            public void Add(ChunkAccumulator other)
            {
                for (var i = 0; i < ScoreSum.Length; i++)
                {
                    ScoreSum[i] += other.ScoreSum[i];
                    FinishSum[i] += other.FinishSum[i];
                    MadeCut[i] += other.MadeCut[i];
                    Top5[i] += other.Top5[i];
                    Top10[i] += other.Top10[i];
                    Top20[i] += other.Top20[i];
                }
                for (var i = 0; i < Buckets.Length; i++) Buckets[i] += other.Buckets[i];
            }
        }

        /// <summary>Box-Muller standard normal sampler that caches the second variate.</summary>
        private sealed class NormalSampler
        {
            private readonly Random _rng;
            private double _spare;
            private bool _hasSpare;

            public NormalSampler(Random rng) => _rng = rng;

            public double Next()
            {
                if (_hasSpare)
                {
                    _hasSpare = false;
                    return _spare;
                }
                double u1;
                do u1 = _rng.NextDouble(); while (u1 <= double.Epsilon);
                var u2 = _rng.NextDouble();
                var mag = Math.Sqrt(-2.0 * Math.Log(u1));
                _spare = mag * Math.Sin(2.0 * Math.PI * u2);
                _hasSpare = true;
                return mag * Math.Cos(2.0 * Math.PI * u2);
            }
        }
    }
}
