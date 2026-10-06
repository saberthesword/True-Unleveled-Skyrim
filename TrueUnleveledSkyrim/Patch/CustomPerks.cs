using System.Text.RegularExpressions;

using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

using TrueUnleveledSkyrim.Config;

namespace TrueUnleveledSkyrim.Patch
{
    // The perks a specific NPC must not receive from the regular perk trees, built from the trees the NPC qualifies for.
    internal sealed class PerkBlockList
    {
        public List<string> PerkKeys { get; } = new();
        public List<string> Plugins { get; } = new();

        public bool Blocks(IPerkGetter perk)
        {
            // The FormKey's ModKey is the plugin that created the perk, no matter which plugin overrides it.
            if (Plugins.Count > 0 && Plugins.Contains(perk.FormKey.ModKey.ToString(), StringComparer.OrdinalIgnoreCase))
                return true;

            return perk.EditorID is not null && PerkKeys.Any(key => perk.EditorID.Contains(key, StringComparison.OrdinalIgnoreCase));
        }
    }

    // A perk of a custom tree. The duplicate is the copy created in the patch, which is what NPCs actually receive.
    internal sealed class CustomPerk
    {
        public CustomPerk(IPerkGetter duplicate, int requiredLevel)
        {
            Duplicate = duplicate;
            RequiredLevel = requiredLevel;
        }

        public IPerkGetter Duplicate { get; }
        public int RequiredLevel { get; }
    }

    internal sealed class CustomTree
    {
        public CustomTree(CustomPerkTree definition, Skill? proxySkill)
        {
            Definition = definition;
            ProxySkill = proxySkill;
        }

        public CustomPerkTree Definition { get; }
        public Skill? ProxySkill { get; }
        public List<Regex> SpellIDPatterns { get; } = new();
        public List<CustomPerk> Perks { get; } = new();
    }

    /// <summary>
    /// Distributes perks from custom perk trees (customPerkTrees.json), such as perks added by custom skill mods that vanilla NPCs never get.
    /// The perks are duplicated into the patch by <see cref="PatchCustomPerks"/>, and handed out by the regular NPC perk
    /// distribution, which asks <see cref="GetQualifyingTrees"/> and <see cref="SpendPerks"/> for them.
    /// </summary>
    internal static class CustomPerksPatcher
    {
        private const string DuplicatePrefix = "TUS_Custom_";

        private static readonly HashSet<Skill> MagicSchools = new() { Skill.Alteration, Skill.Conjuration, Skill.Destruction, Skill.Illusion, Skill.Restoration };

        private static readonly List<CustomTree> trees = new();
        private static readonly Dictionary<FormKey, SpellInfo> spellInfoCache = new();

        // Diagnostics, printed by PrintSummary at the end of the NPC stage.
        private static int npcsEvaluated;
        private static int npcsWithSpells;
        private static readonly Dictionary<string, int> qualifiedCounts = new();
        private static readonly Dictionary<string, int> grantedCounts = new();
        private static readonly Dictionary<string, int> failureCounts = new();

        public static void Reset()
        {
            trees.Clear();
            spellInfoCache.Clear();
            npcsEvaluated = 0;
            npcsWithSpells = 0;
            qualifiedCounts.Clear();
            grantedCounts.Clear();
            failureCounts.Clear();
        }

        private static void Count(Dictionary<string, int> counts, string key)
        {
            counts[key] = counts.TryGetValue(key, out int current) ? current + 1 : 1;
        }

        // Shows how many NPCs qualified for each tree, how many perks were handed out, and why the others didn't qualify.
        public static void PrintSummary()
        {
            if (trees.Count == 0)
                return;

            Console.WriteLine("Custom perk trees: " + npcsEvaluated + " NPCs evaluated, " + npcsWithSpells + " of them have spells.");
            foreach (CustomTree tree in trees)
            {
                string name = tree.Definition.TreeName;
                qualifiedCounts.TryGetValue(name, out int qualified);
                grantedCounts.TryGetValue(name, out int granted);
                Console.WriteLine("  " + name + ": " + qualified + " NPCs qualified, " + granted + " perks granted (" + tree.Perks.Count + " perks in tree).");

                foreach (var failure in failureCounts.Where(x => x.Key.StartsWith(name + "|")).OrderByDescending(x => x.Value))
                    Console.WriteLine("      not qualified, " + failure.Key.Substring(name.Length + 1) + ": " + failure.Value);
            }

            Console.WriteLine();
        }

        private static bool TryParseSkill(string? name, out Skill skill)
        {
            skill = default;
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string normalized = name.Replace(" ", "").Replace("_", "");
            if (normalized.Equals("Marksman", StringComparison.OrdinalIgnoreCase))
                normalized = nameof(Skill.Archery);

            return Enum.TryParse(normalized, true, out skill) && Enum.IsDefined(skill);
        }

        // ------------------------------------------------------------------
        // Stage: duplicate the perks of every custom tree into the patch.
        // ------------------------------------------------------------------

        // Creates the copy of a perk that NPCs receive. Ported from the standalone custom perk patcher.
        private static Perk DuplicatePerk(IPerkGetter sourcePerk, string perkID, IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (!state.PatchMod.ModHeader.MasterReferences.Any(m => m.Master == sourcePerk.FormKey.ModKey))
            {
                state.PatchMod.ModHeader.MasterReferences.Add(new MasterReference { Master = sourcePerk.FormKey.ModKey });
            }

            var duplicatedPerk = state.PatchMod.Perks.DuplicateInAsNewRecord(sourcePerk);
            duplicatedPerk.EditorID = DuplicatePrefix + perkID;

            // Force Mutagen to map every internal dependency so it doesn't crash during output.
            foreach (var link in duplicatedPerk.EnumerateFormLinks())
            {
                if (link.FormKey != FormKey.Null)
                {
                    var requiredMaster = link.FormKey.ModKey;
                    if (requiredMaster != state.PatchMod.ModKey &&
                        !state.PatchMod.ModHeader.MasterReferences.Any(m => m.Master == requiredMaster))
                    {
                        state.PatchMod.ModHeader.MasterReferences.Add(new MasterReference { Master = requiredMaster });
                    }
                }
            }

            return duplicatedPerk;
        }

        public static void PatchCustomPerks(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Reset();

            if (!File.Exists(TUSConstants.CustomPerkTreesPath))
            {
                Console.WriteLine("No " + Path.GetFileName(TUSConstants.CustomPerkTreesPath) + " found, skipping custom perk trees.\n");
                return;
            }

            Console.WriteLine("Reading " + TUSConstants.CustomPerkTreesPath);
            CustomPerkTreeList definitions = JsonHelper.LoadConfig<CustomPerkTreeList>(TUSConstants.CustomPerkTreesPath);

            Dictionary<string, IPerkGetter> perkLookup = new(StringComparer.OrdinalIgnoreCase);
            foreach (IPerkGetter perk in state.LoadOrder.PriorityOrder.Perk().WinningOverrides())
            {
                if (!string.IsNullOrWhiteSpace(perk.EditorID))
                    perkLookup.TryAdd(perk.EditorID.Trim(), perk);
            }

            // Pass 1: duplicate every referenced perk once, even if several trees list it.
            Dictionary<FormKey, IPerkGetter> originalToDuplicate = new();
            List<Perk> createdPerks = new();
            foreach (CustomPerkTree definition in definitions.CustomTrees)
            {
                // A json file can contain explicit nulls, so make sure every list can be iterated.
                definition.Factions ??= new();
                definition.ActorTypeKeywords ??= new();
                definition.RequiredSpellKeywords ??= new();
                definition.RequiredSpellIDs ??= new();
                definition.NameKeys ??= new();
                definition.ForbiddenKeys ??= new();
                definition.BlockedPerkKeys ??= new();
                definition.BlockedPerkPlugins ??= new();
                definition.Perks ??= new();

                Skill? proxySkill = null;
                if (TryParseSkill(definition.ProxyVanillaSkill, out Skill parsedSkill))
                {
                    proxySkill = parsedSkill;
                }
                else if (definition.Perks.Count > 0)
                {
                    Console.WriteLine("[Warning] Tree '" + definition.TreeName + "' has no valid ProxyVanillaSkill ('" + definition.ProxyVanillaSkill + "'), so its perks can't be distributed. Skipping its perks.");
                    definition.Perks.Clear();
                }

                CustomTree tree = new(definition, proxySkill);
                foreach (string snippet in definition.RequiredSpellIDs)
                {
                    // "..." in an ID snippet stands for any number of characters.
                    string pattern = Regex.Escape(snippet).Replace("\\.\\.\\.", ".*");
                    tree.SpellIDPatterns.Add(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled));
                }

                foreach (CustomPerkEntry entry in definition.Perks)
                {
                    if (string.IsNullOrWhiteSpace(entry.EditorID))
                        continue;

                    string perkID = entry.EditorID.Trim();
                    if (!perkLookup.TryGetValue(perkID, out IPerkGetter? sourcePerk))
                    {
                        Console.WriteLine("[Warning] Skipping '" + perkID + "' in tree '" + definition.TreeName + "' - not found in the active load order.");
                        continue;
                    }

                    if (!originalToDuplicate.TryGetValue(sourcePerk.FormKey, out IPerkGetter? duplicate))
                    {
                        Perk createdPerk = DuplicatePerk(sourcePerk, perkID, state);
                        createdPerks.Add(createdPerk);
                        duplicate = createdPerk;
                        originalToDuplicate[sourcePerk.FormKey] = duplicate;
                    }

                    tree.Perks.Add(new CustomPerk(duplicate, entry.RequiredLevel));
                }

                trees.Add(tree);
            }

            // Pass 2: perks that require other perks must require the duplicates instead of the originals.
            void RemapCondition(Condition? cond)
            {
                if (cond is ConditionFloat floatCond && floatCond.Data is HasPerkConditionData perkData)
                {
                    var currentFormKey = perkData.Perk.Link.FormKey;
                    if (currentFormKey != FormKey.Null && originalToDuplicate.TryGetValue(currentFormKey, out var linkedCustomPerk))
                    {
                        perkData.Perk.Link.FormKey = linkedCustomPerk.FormKey;
                    }
                }
            }

            foreach (Perk createdPerk in createdPerks)
            {
                if (createdPerk.Conditions != null)
                {
                    foreach (var cond in createdPerk.Conditions) RemapCondition(cond);
                }

                if (createdPerk.Effects != null)
                {
                    foreach (var effect in createdPerk.Effects)
                    {
                        if (effect?.Conditions != null)
                        {
                            foreach (var perkCond in effect.Conditions)
                            {
                                if (perkCond?.Conditions != null)
                                {
                                    foreach (var subCond in perkCond.Conditions) RemapCondition(subCond);
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine("Loaded " + trees.Count + " custom perk trees, duplicated " + createdPerks.Count + " perks.\n");
        }

        // ------------------------------------------------------------------
        // Per-NPC evaluation, used by the NPC perk distribution.
        // ------------------------------------------------------------------

        private sealed class SpellInfo
        {
            public SpellInfo(string? editorID) { EditorID = editorID; }

            public string? EditorID { get; }
            public HashSet<string> Keywords { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Schools { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class SpellData
        {
            public HashSet<string> Keywords { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> IDs { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> Schools { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        // Keywords, schools of a spell's effects. Cached because the same spells are shared by many NPCs.
        private static SpellInfo GetSpellInfo(ISpellGetter spell, ILinkCache linkCache)
        {
            if (spellInfoCache.TryGetValue(spell.FormKey, out SpellInfo? cached))
                return cached;

            SpellInfo info = new(spell.EditorID);
            foreach (var effect in spell.Effects)
            {
                if (effect.BaseEffect.IsNull || !effect.BaseEffect.TryResolve(linkCache, out var mgefGetter))
                    continue;

                if (mgefGetter.MagicSkill != ActorValue.None)
                    info.Schools.Add(mgefGetter.MagicSkill.ToString());

                foreach (var keywordLink in mgefGetter.Keywords.EmptyIfNull())
                {
                    if (keywordLink.TryResolve(linkCache, out var keyword) && keyword.EditorID is not null)
                        info.Keywords.Add(keyword.EditorID.Trim());
                }
            }

            spellInfoCache[spell.FormKey] = info;
            return info;
        }

        // Everything a tree can ask about an NPC. Each part is only computed if some tree needs it.
        private sealed class NpcTraits
        {
            private readonly Npc npc;
            private readonly IRaceGetter race;
            private readonly ILinkCache linkCache;
            private HashSet<string>? actorKeywords;
            private HashSet<string>? factionIDs;
            private SpellData? spells;

            public NpcTraits(Npc npc, IRaceGetter race, ILinkCache linkCache)
            {
                this.npc = npc;
                this.race = race;
                this.linkCache = linkCache;
            }

            public HashSet<string> ActorKeywords => actorKeywords ??= CollectActorKeywords();
            public HashSet<string> FactionIDs => factionIDs ??= CollectFactionIDs();
            public SpellData Spells => spells ??= CollectSpells();
            public bool HasSpells => spells is not null && (spells.IDs.Count > 0 || spells.Keywords.Count > 0 || spells.Schools.Count > 0);

            private HashSet<string> CollectActorKeywords()
            {
                HashSet<string> keywords = new(StringComparer.OrdinalIgnoreCase);
                foreach (var keywordLink in npc.Keywords.EmptyIfNull())
                {
                    if (keywordLink.TryResolve(linkCache, out var keyword) && keyword.EditorID is not null)
                        keywords.Add(keyword.EditorID.Trim());
                }

                foreach (var keywordLink in race.Keywords.EmptyIfNull())
                {
                    if (keywordLink.TryResolve(linkCache, out var keyword) && keyword.EditorID is not null)
                        keywords.Add(keyword.EditorID.Trim());
                }

                return keywords;
            }

            private HashSet<string> CollectFactionIDs()
            {
                HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
                foreach (RankPlacement? rankEntry in npc.Factions)
                {
                    if (rankEntry is not null && rankEntry.Faction.TryResolve(linkCache, out var faction) && faction.EditorID is not null)
                        ids.Add(faction.EditorID.Trim());
                }

                return ids;
            }

            private SpellData CollectSpells()
            {
                SpellData data = new();
                foreach (var spellEntry in npc.ActorEffect.EmptyIfNull())
                {
                    if (!spellEntry.TryResolve(linkCache, out var spellRecord))
                        continue;

                    // Spell lists can contain other lists, so walk them, guarding against lists that contain themselves.
                    List<ISpellRecordGetter> nodes = new() { spellRecord };
                    HashSet<FormKey> visited = new();
                    while (nodes.Count > 0)
                    {
                        ISpellRecordGetter node = nodes[nodes.Count - 1];
                        nodes.RemoveAt(nodes.Count - 1);
                        if (!visited.Add(node.FormKey))
                            continue;

                        if (node is ISpellGetter spell)
                        {
                            SpellInfo info = GetSpellInfo(spell, linkCache);
                            if (info.EditorID is not null)
                                data.IDs.Add(info.EditorID.Trim());

                            data.Keywords.UnionWith(info.Keywords);
                            data.Schools.UnionWith(info.Schools);
                        }
                        else if (node is ILeveledSpellGetter leveledSpell)
                        {
                            foreach (var entry in leveledSpell.Entries.EmptyIfNull())
                            {
                                if (entry.Data is not null && entry.Data.Reference.TryResolve(linkCache, out var child))
                                    nodes.Add(child);
                            }
                        }
                    }
                }

                return data;
            }
        }

        private static bool NameOrIDContainsAny(Npc npc, IEnumerable<string> keys)
        {
            string? name = npc.Name?.String;
            return keys.Any(key => !string.IsNullOrEmpty(key) &&
                ((name?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false) || (npc.EditorID?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false)));
        }

        // Every criterion a tree defines has to hold for the NPC. Returns the first criterion that failed, or null if the NPC qualifies.
        private static string? GetFailedCriterion(CustomTree tree, Npc npc, NpcTraits traits)
        {
            CustomPerkTree definition = tree.Definition;

            if (definition.NameKeys.Count > 0 && !NameOrIDContainsAny(npc, definition.NameKeys))
                return "name keys";

            if (definition.ForbiddenKeys.Count > 0 && NameOrIDContainsAny(npc, definition.ForbiddenKeys))
                return "forbidden name keys";

            if (definition.ActorTypeKeywords.Count > 0 && !definition.ActorTypeKeywords.Any(keyword => traits.ActorKeywords.Contains(keyword)))
                return "actor type keywords";

            if (definition.RequiredSpellKeywords.Count > 0 && !definition.RequiredSpellKeywords.Any(keyword => traits.Spells.Keywords.Contains(keyword)))
                return "spell keywords";

            if (tree.SpellIDPatterns.Count > 0 && !tree.SpellIDPatterns.Any(regex => traits.Spells.IDs.Any(id => regex.IsMatch(id))))
                return "spell IDs";

            if (definition.Factions.Count > 0 && !definition.Factions.Any(faction => traits.FactionIDs.Contains(faction)))
                return "factions";

            // Non-caster guard: a tree proxied by a magic school only applies to NPCs that really have a spell of that school.
            if (tree.ProxySkill is Skill proxySkill && MagicSchools.Contains(proxySkill) && !traits.Spells.Schools.Contains(proxySkill.ToString()))
                return "no " + proxySkill + " spell";

            return null;
        }

        public static List<CustomTree> GetQualifyingTrees(Npc npc, IRaceGetter race, ILinkCache linkCache)
        {
            List<CustomTree> qualifying = new();
            if (trees.Count == 0)
                return qualifying;

            ++npcsEvaluated;
            NpcTraits traits = new(npc, race, linkCache);
            foreach (CustomTree tree in trees)
            {
                string? failedCriterion = GetFailedCriterion(tree, npc, traits);
                if (failedCriterion is null)
                {
                    qualifying.Add(tree);
                    Count(qualifiedCounts, tree.Definition.TreeName);
                }
                else
                {
                    Count(failureCounts, tree.Definition.TreeName + "|" + failedCriterion);
                }
            }

            if (traits.HasSpells)
                ++npcsWithSpells;

            return qualifying;
        }

        // Combines the block lists of every tree the NPC qualifies for. They apply to the regular perk trees only,
        // the perks of a tree the NPC qualifies for are never blocked.
        public static PerkBlockList? CreateBlockList(List<CustomTree> qualifyingTrees)
        {
            PerkBlockList? blockList = null;
            foreach (CustomTree tree in qualifyingTrees)
            {
                if (tree.Definition.BlockedPerkKeys.Count == 0 && tree.Definition.BlockedPerkPlugins.Count == 0)
                    continue;

                blockList ??= new();
                blockList.PerkKeys.AddRange(tree.Definition.BlockedPerkKeys);
                blockList.Plugins.AddRange(tree.Definition.BlockedPerkPlugins);
            }

            return blockList;
        }

        // Spends perk points of the given skill on the perks of the qualifying custom trees that use it as their proxy skill.
        // Only a share of the points goes to custom perks (the rest stays for the regular tree), and perks are taken lowest
        // requirement first. Returns the points that are left.
        public static byte SpendPerks(Npc npc, Skill skill, byte points, List<CustomTree> qualifyingTrees)
        {
            if (points == 0 || qualifyingTrees.Count == 0)
                return points;

            List<CustomTree> skillTrees = qualifyingTrees.Where(tree => tree.ProxySkill == skill && tree.Perks.Count > 0).ToList();
            if (skillTrees.Count == 0)
                return points;

            float share = Math.Clamp(skillTrees.Max(tree => tree.Definition.PointShare), 0f, 1f);
            int customBudget = Math.Min(points, (int)Math.Ceiling(points * share));
            if (customBudget <= 0)
                return points;

            byte skillValue = npc.PlayerSkills!.SkillValues[skill];
            foreach (var (owner, perk) in skillTrees.SelectMany(t => t.Perks.Select(p => (Owner: t, Perk: p))).OrderBy(x => x.Perk.RequiredLevel))
            {
                if (customBudget <= 0)
                    break;

                // Sorted by requirement, so nothing after this one can be afforded either.
                if (perk.RequiredLevel > skillValue)
                    break;

                if (npc.Perks!.Any(x => x.Perk.FormKey == perk.Duplicate.FormKey))
                    continue;

                npc.Perks!.Add(new PerkPlacement() { Perk = perk.Duplicate.ToLink(), Rank = 1 });
                Count(grantedCounts, owner.Definition.TreeName);
                --customBudget;
                --points;
            }

            return points;
        }
    }
}
