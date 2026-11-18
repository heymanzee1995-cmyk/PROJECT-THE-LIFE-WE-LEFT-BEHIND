using System;
using System.Collections.Generic;
using Game.Domain;

namespace Game.Data
{
    /// <summary>
    /// Turns <see cref="MergeContentData"/> into validated domain config. Every problem is reported with a
    /// readable message (never a crash), so a bad content file fails loudly at load time.
    /// </summary>
    public static class MergeContentMapper
    {
        public static bool TryBuild(MergeContentData data, out MergeContent content, out List<string> errors)
        {
            content = null;
            errors = new List<string>();
            if (data == null)
            {
                errors.Add("Content is missing or could not be parsed.");
                return false;
            }

            if (data.version != MergeContentData.SupportedVersion)
            {
                errors.Add(data.version == 0
                    ? "Content has no 'version' field (a missing version is never assumed)."
                    : "Unsupported content version " + data.version + " (expected " + MergeContentData.SupportedVersion + ").");
                return false;
            }

            if (data.board == null)
            {
                errors.Add("Content has no 'board' section.");
                return false;
            }

            if (data.chains == null || data.chains.Length == 0)
            {
                errors.Add("Content has no 'chains'.");
                return false;
            }

            var chains = new List<ChainDefinition>(data.chains.Length);
            var catalog = new ItemCatalog();
            for (int c = 0; c < data.chains.Length; c++)
            {
                ChainData chain = data.chains[c];
                if (chain == null)
                {
                    errors.Add("Chain at index " + c + " is empty.");
                    continue;
                }

                string label = "Chain '" + chain.id + "'";
                GeneratorDefinition[] generatorTiers = null;
                if (chain.generators != null && chain.generators.Length > 0)
                {
                    generatorTiers = new GeneratorDefinition[Math.Max(0, chain.maxTier)];
                    for (int g = 0; g < chain.generators.Length; g++)
                    {
                        GeneratorTierData gen = chain.generators[g];
                        if (gen == null)
                        {
                            errors.Add(label + ": generator entry " + g + " is empty.");
                            continue;
                        }

                        if (gen.tier < 1 || gen.tier > chain.maxTier)
                        {
                            errors.Add(label + ": generator tier " + gen.tier + " is outside 1.." + chain.maxTier + ".");
                            continue;
                        }

                        if (generatorTiers[gen.tier - 1] != null)
                        {
                            errors.Add(label + ": generator tier " + gen.tier + " is defined twice.");
                            continue;
                        }

                        var outputs = new List<WeightedOutput>();
                        if (gen.outputs != null)
                        {
                            for (int o = 0; o < gen.outputs.Length; o++)
                            {
                                OutputData output = gen.outputs[o];
                                if (output == null)
                                {
                                    errors.Add(label + " tier " + gen.tier + ": output " + o + " is empty.");
                                    continue;
                                }

                                outputs.Add(new WeightedOutput(new ItemType(output.chainId, output.tier), output.weight));
                            }
                        }

                        generatorTiers[gen.tier - 1] = new GeneratorDefinition(gen.maxCharges, gen.rechargeIntervalMs, outputs);
                    }
                }

                if (chain.tierNames != null && chain.tierNames.Length != 0 && chain.tierNames.Length != chain.maxTier)
                {
                    errors.Add(label + ": needs exactly " + chain.maxTier + " tier names (has " + chain.tierNames.Length + ").");
                }

                if (chain.color != null && !IsHexColor(chain.color))
                {
                    errors.Add(label + ": colour '" + chain.color + "' is not in #RRGGBB form.");
                }

                chains.Add(new ChainDefinition(chain.id, chain.maxTier, chain.sellValues ?? Array.Empty<int>(), generatorTiers,
                    chain.strictlyDoubling));
                if (!string.IsNullOrEmpty(chain.id))
                {
                    catalog.Add(chain.id, chain.color, chain.tierNames);
                }
            }

            BoardData b = data.board;
            var config = new EngineConfig(new BoardConfig(b.columns, b.rows, b.inventoryCapacity, b.maxInventoryCapacity, b.lockedCells),
                chains);
            errors.AddRange(ConfigValidator.Validate(config));
            if (errors.Count > 0)
            {
                return false;
            }

            var layout = new List<InitialPlacement>();
            var usedCells = new HashSet<int>();
            var locked = new HashSet<int>(config.Board.InitiallyLockedCells);
            LayoutEntryData[] entries = data.startingLayout ?? Array.Empty<LayoutEntryData>();
            for (int i = 0; i < entries.Length; i++)
            {
                LayoutEntryData e = entries[i];
                if (e == null)
                {
                    errors.Add("Starting layout entry " + i + " is empty.");
                    continue;
                }

                var type = new ItemType(e.chainId, e.tier);
                if (!config.IsValidType(type))
                {
                    errors.Add("Starting layout entry " + i + ": " + type + " is not a valid item.");
                }
                else if (e.cell < 0 || e.cell >= config.Board.CellCount)
                {
                    errors.Add("Starting layout entry " + i + ": cell " + e.cell + " is outside the board.");
                }
                else if (locked.Contains(e.cell))
                {
                    errors.Add("Starting layout entry " + i + ": cell " + e.cell + " is locked.");
                }
                else if (!usedCells.Add(e.cell))
                {
                    errors.Add("Starting layout entry " + i + ": cell " + e.cell + " is used twice.");
                }
                else
                {
                    layout.Add(new InitialPlacement(type, e.cell));
                }
            }

            if (errors.Count > 0)
            {
                return false;
            }

            content = new MergeContent(config, layout, catalog);
            return true;
        }

        private static bool IsHexColor(string s)
        {
            if (s.Length != 7 || s[0] != '#')
            {
                return false;
            }

            for (int i = 1; i < 7; i++)
            {
                char ch = s[i];
                bool hex = (ch >= '0' && ch <= '9') || (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F');
                if (!hex)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
