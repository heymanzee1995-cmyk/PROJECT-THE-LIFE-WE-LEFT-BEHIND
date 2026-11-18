using System;
using System.Collections.Generic;

namespace Game.Domain
{
    /// <summary>One weighted entry in a generator's output table.</summary>
    public readonly struct WeightedOutput
    {
        public readonly ItemType Type;
        public readonly int Weight;

        public WeightedOutput(ItemType type, int weight)
        {
            Type = type;
            Weight = weight;
        }
    }

    /// <summary>Probability of one generator outcome, for display / odds disclosure.</summary>
    public readonly struct OutputOdds
    {
        public readonly ItemType Type;
        public readonly double Probability;

        public OutputOdds(ItemType type, double probability)
        {
            Type = type;
            Probability = probability;
        }
    }

    /// <summary>
    /// Behaviour of a generator tier. A generator holds up to <see cref="MaxCharges"/> charges; each tap
    /// spends one and spawns one item rolled from <see cref="Outputs"/>. While below full it restores one
    /// charge every <see cref="RechargeIntervalMs"/> (approved incremental recharge); the timer stops at full.
    /// </summary>
    public sealed class GeneratorDefinition
    {
        private readonly WeightedOutput[] _outputs;

        public GeneratorDefinition(int maxCharges, long rechargeIntervalMs, IReadOnlyList<WeightedOutput> outputs)
        {
            MaxCharges = maxCharges;
            RechargeIntervalMs = rechargeIntervalMs;
            _outputs = outputs == null ? Array.Empty<WeightedOutput>() : CopyOf(outputs);
            long total = 0;
            for (int i = 0; i < _outputs.Length; i++)
            {
                total += Math.Max(0, _outputs[i].Weight);
            }

            TotalWeight = total;
        }

        public int MaxCharges { get; }
        /// <summary>Time to restore one charge. Must be &gt; 0.</summary>
        public long RechargeIntervalMs { get; }
        public IReadOnlyList<WeightedOutput> Outputs => _outputs;
        public long TotalWeight { get; }

        /// <summary>Exact odds of each outcome. Intended for inspection and player-facing disclosure.</summary>
        public List<OutputOdds> GetOdds()
        {
            var result = new List<OutputOdds>(_outputs.Length);
            for (int i = 0; i < _outputs.Length; i++)
            {
                double p = TotalWeight <= 0 ? 0.0 : (double)_outputs[i].Weight / TotalWeight;
                result.Add(new OutputOdds(_outputs[i].Type, p));
            }

            return result;
        }

        internal ItemType Roll(SeededRandom rng)
        {
            // TotalWeight is validated to fit in int and be > 0 before an engine accepts the config.
            int r = rng.NextInt((int)TotalWeight);
            for (int i = 0; i < _outputs.Length; i++)
            {
                r -= _outputs[i].Weight;
                if (r < 0)
                {
                    return _outputs[i].Type;
                }
            }

            return _outputs[_outputs.Length - 1].Type;
        }

        private static WeightedOutput[] CopyOf(IReadOnlyList<WeightedOutput> source)
        {
            var copy = new WeightedOutput[source.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }
    }
}
