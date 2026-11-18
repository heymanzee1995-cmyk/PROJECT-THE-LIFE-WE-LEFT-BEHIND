using System;
using System.Collections.Generic;

namespace Game.Domain
{
    public sealed class InvalidConfigException : Exception
    {
        public InvalidConfigException(IReadOnlyList<string> errors)
            : base("Invalid merge engine config:\n - " + string.Join("\n - ", errors))
        {
            Errors = errors;
        }

        public IReadOnlyList<string> Errors { get; }
    }

    /// <summary>
    /// Validates an <see cref="EngineConfig"/> and returns human-readable errors (empty when valid).
    /// The engine refuses to start on an invalid config, so content mistakes fail loudly at load time.
    /// </summary>
    public static class ConfigValidator
    {
        public const int MaxBoardDimension = 32;
        public const int MaxDoublingTier = 30;

        public static List<string> Validate(EngineConfig config)
        {
            var errors = new List<string>();
            if (config == null)
            {
                errors.Add("Config is null.");
                return errors;
            }

            ValidateBoard(config.Board, errors);
            ValidateChains(config, errors);
            return errors;
        }

        public static void ThrowIfInvalid(EngineConfig config)
        {
            List<string> errors = Validate(config);
            if (errors.Count > 0)
            {
                throw new InvalidConfigException(errors);
            }
        }

        private static void ValidateBoard(BoardConfig board, List<string> errors)
        {
            if (board == null)
            {
                errors.Add("Board config is missing.");
                return;
            }

            if (board.Columns < 1 || board.Columns > MaxBoardDimension)
            {
                errors.Add("Board columns must be between 1 and " + MaxBoardDimension + " (was " + board.Columns + ").");
            }

            if (board.Rows < 1 || board.Rows > MaxBoardDimension)
            {
                errors.Add("Board rows must be between 1 and " + MaxBoardDimension + " (was " + board.Rows + ").");
            }

            if (board.InventoryCapacity < 0)
            {
                errors.Add("Inventory capacity must be >= 0 (was " + board.InventoryCapacity + ").");
            }

            if (board.MaxInventoryCapacity < board.InventoryCapacity)
            {
                errors.Add("Max inventory capacity (" + board.MaxInventoryCapacity + ") must be >= starting capacity (" + board.InventoryCapacity + ").");
            }

            int cellCount = board.Columns * board.Rows;
            var seen = new HashSet<int>();
            for (int i = 0; i < board.InitiallyLockedCells.Count; i++)
            {
                int cell = board.InitiallyLockedCells[i];
                if (cell < 0 || cell >= cellCount)
                {
                    errors.Add("Locked cell index " + cell + " is outside the board (0.." + (cellCount - 1) + ").");
                }
                else if (!seen.Add(cell))
                {
                    errors.Add("Locked cell index " + cell + " is listed twice.");
                }
            }

            if (cellCount > 0 && seen.Count >= cellCount)
            {
                errors.Add("At least one board cell must start unlocked.");
            }
        }

        private static void ValidateChains(EngineConfig config, List<string> errors)
        {
            if (config.Chains.Count == 0)
            {
                errors.Add("At least one item chain is required.");
                return;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int c = 0; c < config.Chains.Count; c++)
            {
                ChainDefinition chain = config.Chains[c];
                if (chain == null)
                {
                    errors.Add("Chain at index " + c + " is null.");
                    continue;
                }

                string label = "Chain '" + chain.Id + "'";
                if (string.IsNullOrEmpty(chain.Id))
                {
                    errors.Add("Chain at index " + c + " has an empty id.");
                    label = "Chain at index " + c;
                }
                else if (!ids.Add(chain.Id))
                {
                    errors.Add(label + " is defined more than once.");
                }

                if (chain.MaxTier < 1)
                {
                    errors.Add(label + ": max tier must be >= 1 (was " + chain.MaxTier + ").");
                    continue;
                }

                if (chain.StrictlyDoubling && chain.MaxTier > MaxDoublingTier)
                {
                    errors.Add(label + ": strictly doubling chains support at most " + MaxDoublingTier + " tiers.");
                }

                if (chain.SellValues.Count != chain.MaxTier)
                {
                    errors.Add(label + ": needs exactly " + chain.MaxTier + " sell values (has " + chain.SellValues.Count + ").");
                }

                for (int t = 0; t < chain.SellValues.Count; t++)
                {
                    if (chain.SellValues[t] < 0)
                    {
                        errors.Add(label + ": sell value for tier " + (t + 1) + " must be >= 0.");
                    }
                }

                if (chain.GeneratorTiers.Count != 0 && chain.GeneratorTiers.Count != chain.MaxTier)
                {
                    errors.Add(label + ": generator tier table must be empty or have exactly " + chain.MaxTier + " entries (has " + chain.GeneratorTiers.Count + ").");
                }

                for (int t = 0; t < chain.GeneratorTiers.Count; t++)
                {
                    GeneratorDefinition gen = chain.GeneratorTiers[t];
                    if (gen != null)
                    {
                        ValidateGenerator(config, label + " tier " + (t + 1) + " generator", gen, errors);
                    }
                }
            }
        }

        private static void ValidateGenerator(EngineConfig config, string label, GeneratorDefinition gen, List<string> errors)
        {
            if (gen.MaxCharges < 1)
            {
                errors.Add(label + ": max charges must be >= 1 (was " + gen.MaxCharges + ").");
            }

            if (gen.CooldownMs < 0)
            {
                errors.Add(label + ": cooldown must be >= 0 ms (was " + gen.CooldownMs + ").");
            }

            if (gen.Outputs.Count == 0)
            {
                errors.Add(label + ": output table is empty.");
            }

            for (int i = 0; i < gen.Outputs.Count; i++)
            {
                WeightedOutput output = gen.Outputs[i];
                if (output.Weight <= 0)
                {
                    errors.Add(label + ": output " + output.Type + " must have a positive weight (was " + output.Weight + ").");
                }

                if (!config.IsValidType(output.Type))
                {
                    errors.Add(label + ": output " + output.Type + " is not a valid chain/tier.");
                }
            }

            if (gen.TotalWeight > int.MaxValue)
            {
                errors.Add(label + ": total output weight is too large.");
            }
        }
    }
}
