using System.Text.RegularExpressions;

using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Synthesis;

using TrueUnleveledSkyrim.Config;

namespace TrueUnleveledSkyrim.Patch
{
    internal enum SpellTheme { None, Vampire, Holy }

    /// <summary>
    /// Gives NPCs that use magic extra spells from the load order, matching their element, magic school and level.
    /// Ported from the standalone spell distributor patcher. It runs inside the NPC stage right after the static level is set,
    /// so spell tiers follow the level TUS gives the NPC, and the class rebuild and perk distribution that come afterwards
    /// see the new spells.
    /// </summary>
    internal static class SpellDistributor
    {
        private enum MagicSchool { Alteration, Conjuration, Destruction, Illusion, Restoration, None }
        private enum PerkLevel { None = 0, Novice = 1, Apprentice = 2, Adept = 3, Expert = 4, Master = 5 }

        private sealed class CachedSpell
        {
            public ISpellGetter Spell { get; set; } = null!;
            public FormKey FormKey { get; set; }
            public float BaseCost { get; set; }
            public SpellElement Element { get; set; }
            public MagicSchool School { get; set; }
            public PerkLevel RequiredLevel { get; set; }
            public SpellTheme Theme { get; set; }

            // Not learnable from a book, only kept for NPCs covered by a plugin-only rule. Never part of the normal distribution.
            public bool ExclusiveOnly { get; set; }
        }

        private static readonly Dictionary<FormKey, (MagicSchool School, PerkLevel Level)> PerkMap = new()
        {
            { Skyrim.Perk.AlterationNovice00.FormKey, (MagicSchool.Alteration, PerkLevel.Novice) },
            { Skyrim.Perk.AlterationApprentice25.FormKey, (MagicSchool.Alteration, PerkLevel.Apprentice) },
            { Skyrim.Perk.AlterationAdept50.FormKey, (MagicSchool.Alteration, PerkLevel.Adept) },
            { Skyrim.Perk.AlterationExpert75.FormKey, (MagicSchool.Alteration, PerkLevel.Expert) },
            { Skyrim.Perk.AlterationMaster100.FormKey, (MagicSchool.Alteration, PerkLevel.Master) },

            { Skyrim.Perk.ConjurationNovice00.FormKey, (MagicSchool.Conjuration, PerkLevel.Novice) },
            { Skyrim.Perk.ConjurationApprentice25.FormKey, (MagicSchool.Conjuration, PerkLevel.Apprentice) },
            { Skyrim.Perk.ConjurationAdept50.FormKey, (MagicSchool.Conjuration, PerkLevel.Adept) },
            { Skyrim.Perk.ConjurationExpert75.FormKey, (MagicSchool.Conjuration, PerkLevel.Expert) },
            { Skyrim.Perk.ConjurationMaster100.FormKey, (MagicSchool.Conjuration, PerkLevel.Master) },

            { Skyrim.Perk.DestructionNovice00.FormKey, (MagicSchool.Destruction, PerkLevel.Novice) },
            { Skyrim.Perk.DestructionApprentice25.FormKey, (MagicSchool.Destruction, PerkLevel.Apprentice) },
            { Skyrim.Perk.DestructionAdept50.FormKey, (MagicSchool.Destruction, PerkLevel.Adept) },
            { Skyrim.Perk.DestructionExpert75.FormKey, (MagicSchool.Destruction, PerkLevel.Expert) },
            { Skyrim.Perk.DestructionMaster100.FormKey, (MagicSchool.Destruction, PerkLevel.Master) },

            { Skyrim.Perk.IllusionNovice00.FormKey, (MagicSchool.Illusion, PerkLevel.Novice) },
            { Skyrim.Perk.IllusionApprentice25.FormKey, (MagicSchool.Illusion, PerkLevel.Apprentice) },
            { Skyrim.Perk.IllusionAdept50.FormKey, (MagicSchool.Illusion, PerkLevel.Adept) },
            { Skyrim.Perk.IllusionExpert75.FormKey, (MagicSchool.Illusion, PerkLevel.Expert) },
            { Skyrim.Perk.IllusionMaster100.FormKey, (MagicSchool.Illusion, PerkLevel.Master) },

            { Skyrim.Perk.RestorationNovice00.FormKey, (MagicSchool.Restoration, PerkLevel.Novice) },
            { Skyrim.Perk.RestorationApprentice25.FormKey, (MagicSchool.Restoration, PerkLevel.Apprentice) },
            { Skyrim.Perk.RestorationAdept50.FormKey, (MagicSchool.Restoration, PerkLevel.Adept) },
            { Skyrim.Perk.RestorationExpert75.FormKey, (MagicSchool.Restoration, PerkLevel.Expert) },
            { Skyrim.Perk.RestorationMaster100.FormKey, (MagicSchool.Restoration, PerkLevel.Master) },
        };

        // ---- Rules specific to the mods in this load order ----------------------------------------------------

        // Spells from these plugins are only given to NPCs the rule accepts.
        private static readonly Dictionary<string, Func<INpcGetter, bool>> ExclusivePluginRules = new(StringComparer.OrdinalIgnoreCase)
        {
            {
                "Aqua.esl", npc =>
                {
                    string id = npc.EditorID?.ToLowerInvariant() ?? "";
                    return id.Contains("dlc2miraak") || (id.Contains("dlc2") && id.Contains("cultist"));
                }
            }
        };

        // Themed spells are identified by the plugin they come from. Plugin names are compared in lower case.
        private static SpellTheme GetSpellTheme(ISpellGetter spell)
        {
            string pluginName = spell.FormKey.ModKey.FileName.String.ToLowerInvariant();

            if (pluginName.Contains("ancientbloodii.esl") || pluginName.Contains("bloodmoon.esp"))
                return SpellTheme.Vampire;

            if (pluginName.Contains("inquisition.esp"))
                return SpellTheme.Holy;

            return SpellTheme.None;
        }

        private static readonly Dictionary<string, List<SpellTheme>> ThemeOverrides = new(StringComparer.OrdinalIgnoreCase)
        {
            { "DLC1Serana", new List<SpellTheme> { SpellTheme.Vampire } },
            { "DLC1Valerica", new List<SpellTheme> { SpellTheme.Vampire } },
        };

        private static readonly Dictionary<string, List<SpellTheme>> ThemeBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            { "thrall", new List<SpellTheme> { SpellTheme.Vampire } }
        };

        // ---- Spell rules (spellRules.json) ------------------------------------------------------------------------
        // A rule describes a kind of NPC and what it does with spells: which spells it must receive, whether it also takes part in the
        // regular distribution, and which spells it must never receive. If several rules match an NPC they are combined: the
        // must-receive spells add up, the must-not-receive spells add up and win over everything, and the regular distribution is
        // off if any of the rules turns it off.

        private sealed class CompiledRule
        {
            public CompiledRule(SpellRule definition) { Definition = definition; }

            public SpellRule Definition { get; }
            public List<ISpellGetter> ExplicitSpells { get; } = new();
        }

        // The combined effect of every rule that matches one NPC.
        private sealed class NpcSpellRules
        {
            public List<CompiledRule> Rules { get; } = new();
            public bool NaturalDistribution { get; set; } = true;
            public HashSet<string> BlockedIDs { get; } = new(StringComparer.OrdinalIgnoreCase);
            public List<string> BlockedPlugins { get; } = new();
            public HashSet<string> BlockedElements { get; } = new(StringComparer.OrdinalIgnoreCase);
            public HashSet<string> BlockedSchools { get; } = new(StringComparer.OrdinalIgnoreCase);

            public bool Blocks(ISpellGetter spell, SpellElement element, string? school)
            {
                if (spell.EditorID is not null && BlockedIDs.Contains(spell.EditorID)) return true;
                if (BlockedPlugins.Any(snippet => IsFromPlugin(spell, snippet))) return true;
                if (BlockedElements.Contains(element.ToString())) return true;
                return school is not null && BlockedSchools.Contains(school);
            }
        }

        // Keywords and factions of an NPC, only collected if a rule asks for them.
        private sealed class RuleTraits
        {
            private readonly Npc npc;
            private readonly IRaceGetter race;
            private readonly ILinkCache linkCache;
            private HashSet<string>? keywords;
            private HashSet<string>? factions;

            public RuleTraits(Npc npc, IRaceGetter race, ILinkCache linkCache)
            {
                this.npc = npc;
                this.race = race;
                this.linkCache = linkCache;
            }

            public HashSet<string> Keywords => keywords ??= CollectKeywords();
            public HashSet<string> Factions => factions ??= CollectFactions();

            private HashSet<string> CollectKeywords()
            {
                HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
                foreach (var keywordLink in npc.Keywords.EmptyIfNull())
                {
                    if (keywordLink.TryResolve(linkCache, out var keyword) && keyword.EditorID is not null)
                        result.Add(keyword.EditorID.Trim());
                }

                foreach (var keywordLink in race.Keywords.EmptyIfNull())
                {
                    if (keywordLink.TryResolve(linkCache, out var keyword) && keyword.EditorID is not null)
                        result.Add(keyword.EditorID.Trim());
                }

                return result;
            }

            private HashSet<string> CollectFactions()
            {
                HashSet<string> result = new(StringComparer.OrdinalIgnoreCase);
                foreach (RankPlacement? rankEntry in npc.Factions)
                {
                    if (rankEntry is not null && rankEntry.Faction.TryResolve(linkCache, out var faction) && faction.EditorID is not null)
                        result.Add(faction.EditorID.Trim());
                }

                return result;
            }
        }

        private static readonly List<CompiledRule> compiledRules = new();

        private static bool IsFromPlugin(ISpellGetter spell, string pluginSnippet)
        {
            return spell.FormKey.ModKey.FileName.String.Contains(pluginSnippet, StringComparison.OrdinalIgnoreCase);
        }

        // Spells of these plugins are kept even if they can't be learned from a book, so rules can hand them out. They are
        // marked ExclusiveOnly and never take part in the regular distribution.
        private static bool IsRulePluginSpell(ISpellGetter spell)
        {
            return compiledRules.Any(rule => rule.Definition.MustReceive.Plugins.Any(snippet => IsFromPlugin(spell, snippet)));
        }

        private static string? GetSchoolName(ISpellGetter spell)
        {
            return spell.HalfCostPerk != null && PerkMap.TryGetValue(spell.HalfCostPerk.FormKey, out var perkInfo) ? perkInfo.School.ToString() : null;
        }

        private static List<string> CleanList(List<string>? list)
        {
            return (list ?? new List<string>()).Where(entry => !string.IsNullOrWhiteSpace(entry)).Select(entry => entry.Trim()).ToList();
        }

        private static SpellSelector CleanSelector(SpellSelector? selector)
        {
            selector ??= new SpellSelector();
            selector.SpellIDs = CleanList(selector.SpellIDs);
            selector.Plugins = CleanList(selector.Plugins);
            selector.Elements = CleanList(selector.Elements);
            selector.Schools = CleanList(selector.Schools);
            return selector;
        }

        // Loads spellRules.json (optional) and finds the spells its rules name.
        private static void LoadRules(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            compiledRules.Clear();
            if (!File.Exists(TUSConstants.SpellRulesPath))
                return;

            SpellRuleList ruleList = JsonHelper.LoadConfig<SpellRuleList>(TUSConstants.SpellRulesPath);
            HashSet<string> neededIDs = new(StringComparer.OrdinalIgnoreCase);

            foreach (SpellRule rule in ruleList.Rules)
            {
                if (!rule.Enabled)
                    continue;

                rule.NameKeys = CleanList(rule.NameKeys);
                rule.ForbiddenKeys = CleanList(rule.ForbiddenKeys);
                rule.Factions = CleanList(rule.Factions);
                rule.ActorTypeKeywords = CleanList(rule.ActorTypeKeywords);
                rule.MustReceive = CleanSelector(rule.MustReceive);
                rule.MustNotReceive = CleanSelector(rule.MustNotReceive);

                if (rule.NameKeys.Count == 0 && rule.Factions.Count == 0 && rule.ActorTypeKeywords.Count == 0)
                {
                    Console.WriteLine("[Warning] Spell rule '" + rule.Name + "' has no NPC criteria (NameKeys, Factions or ActorTypeKeywords), skipping it so it doesn't apply to every NPC.");
                    continue;
                }

                compiledRules.Add(new CompiledRule(rule));
                neededIDs.UnionWith(rule.MustReceive.SpellIDs);
            }

            if (neededIDs.Count > 0)
            {
                HashSet<string> foundIDs = new(StringComparer.OrdinalIgnoreCase);
                foreach (var spell in state.LoadOrder.PriorityOrder.Spell().WinningOverrides())
                {
                    if (spell.EditorID is null || !neededIDs.Contains(spell.EditorID))
                        continue;

                    foundIDs.Add(spell.EditorID);
                    foreach (CompiledRule rule in compiledRules)
                    {
                        if (rule.Definition.MustReceive.SpellIDs.Any(id => id.Equals(spell.EditorID, StringComparison.OrdinalIgnoreCase)))
                            rule.ExplicitSpells.Add(spell);
                    }
                }

                foreach (string id in neededIDs.Where(id => !foundIDs.Contains(id)))
                    Console.WriteLine("[Warning] Spell rule spell '" + id + "' was not found in the load order.");
            }

            Console.WriteLine("Spell rules: " + compiledRules.Count + " active.");
        }

        private static bool RuleMatches(SpellRule rule, Npc npc, RuleTraits traits)
        {
            string? name = npc.Name?.String;

            bool ContainsKey(string key) =>
                (name?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (npc.EditorID?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false);

            if (rule.NameKeys.Count > 0 && !rule.NameKeys.Any(ContainsKey)) return false;
            if (rule.ForbiddenKeys.Count > 0 && rule.ForbiddenKeys.Any(ContainsKey)) return false;
            if (rule.ActorTypeKeywords.Count > 0 && !rule.ActorTypeKeywords.Any(keyword => traits.Keywords.Contains(keyword))) return false;
            if (rule.Factions.Count > 0 && !rule.Factions.Any(faction => traits.Factions.Contains(faction))) return false;

            return true;
        }

        // Combines every rule that matches the NPC, or null if none does.
        private static NpcSpellRules? ResolveRules(Npc npc, IRaceGetter race, ILinkCache linkCache)
        {
            if (compiledRules.Count == 0)
                return null;

            RuleTraits traits = new(npc, race, linkCache);
            NpcSpellRules? result = null;
            foreach (CompiledRule rule in compiledRules)
            {
                if (!RuleMatches(rule.Definition, npc, traits))
                    continue;

                result ??= new NpcSpellRules();
                result.Rules.Add(rule);
                if (!rule.Definition.NaturalDistribution)
                    result.NaturalDistribution = false;

                result.BlockedIDs.UnionWith(rule.Definition.MustNotReceive.SpellIDs);
                result.BlockedPlugins.AddRange(rule.Definition.MustNotReceive.Plugins);
                result.BlockedElements.UnionWith(rule.Definition.MustNotReceive.Elements);
                result.BlockedSchools.UnionWith(rule.Definition.MustNotReceive.Schools);

                PatchReport.Add(npc, "Spell rule applies: " + rule.Definition.Name);
            }

            return result;
        }

        // ---- General rules --------------------------------------------------------------------------------------

        // Races with their own casting animations. They can only cast spells that cast exactly like the vanilla spells they already have
        // (same cast type, target type, equip slot and cast duration), so they never receive anything else, and are never widened
        // to concentration or ground-targeted spells.
        private static readonly HashSet<FormKey> RestrictedAnimationRaces = new()
        {
            Skyrim.Race.DragonPriestRace.FormKey,
            Skyrim.Race.HagravenRace.FormKey
        };

        private static (CastType Cast, TargetType Target, FormKey Equipment, double Duration) GetCastSignature(ISpellGetter spell)
        {
            return (spell.CastType, spell.TargetType, spell.EquipmentType.FormKey, Math.Round(spell.CastDuration, 2));
        }

        // NPCs with any of these in their EditorID or name are never touched.
        private static readonly string[] NpcSkipKeywords = { "summon", "ghost", "spirit", "fx", "test" };

        private static readonly HashSet<string> VanillaMasters = new(StringComparer.OrdinalIgnoreCase)
        {
            "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm"
        };

        // Names are matched as whole words, so "Stormcloak" is not a storm mage and "Voice" is not ice.
        private static readonly Regex FireNameWords = new(@"\b(pyromancer|flame)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex FrostNameWords = new(@"\b(cryomancer|frost|ice)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ShockNameWords = new(@"\b(electromancer|storm|shock)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex PoisonNameWords = new(@"\b(poisoner|afflicted|venom|toxin)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex NecromancyNameWords = new(@"\b(necromancer|reanimator)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SummoningNameWords = new(@"\b(conjurer|summoner)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly List<CachedSpell> cachedSpells = new();
        private static readonly HashSet<FormKey> encounterableNpcs = new();
        private static readonly HashSet<FormKey> givenNpcs = new();
        private static int npcsGivenSpells;
        private static int spellsGiven;

        public static void Reset()
        {
            cachedSpells.Clear();
            encounterableNpcs.Clear();
            compiledRules.Clear();
            givenNpcs.Clear();
            npcsGivenSpells = 0;
            spellsGiven = 0;
        }

        public static void PrintSummary()
        {
            if (cachedSpells.Count == 0)
                return;

            Console.WriteLine("Spell distribution: " + spellsGiven + " spells given to " + npcsGivenSpells + " NPCs (" + cachedSpells.Count + " spells available).\n");
        }

        // Collects the spells that can be handed out and the NPCs that actually appear in the world. Call once before the NPC loop.
        public static void Prepare(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            Reset();
            var settings = Patcher.ModSettings.Value.NPCs;
            LoadRules(state);

            foreach (var placedNpc in state.LoadOrder.PriorityOrder.PlacedNpc().WinningOverrides())
            {
                if (placedNpc.Base != null && !placedNpc.Base.IsNull)
                    encounterableNpcs.Add(placedNpc.Base.FormKey);
            }

            foreach (var leveledList in state.LoadOrder.PriorityOrder.LeveledNpc().WinningOverrides())
            {
                if (leveledList.Entries == null)
                    continue;

                foreach (var entry in leveledList.Entries)
                {
                    if (entry.Data != null && !entry.Data.Reference.IsNull)
                        encounterableNpcs.Add(entry.Data.Reference.FormKey);
                }
            }

            HashSet<FormKey> blacklistedSpellKeys = settings.BlacklistedSpells.Select(link => link.FormKey).ToHashSet();

            // Only spells the player can learn from a book are handed out.
            HashSet<FormKey> learnableSpellKeys = new();
            foreach (var book in state.LoadOrder.PriorityOrder.Book().WinningOverrides())
            {
                if (book.Teaches is IBookSpellGetter bookSpell && !bookSpell.Spell.IsNull)
                    learnableSpellKeys.Add(bookSpell.Spell.FormKey);
            }

            var rawSpells = state.LoadOrder.PriorityOrder.Spell().WinningOverrides()
                .Where(s =>
                    s.Type == SpellType.Spell &&
                    s.BaseCost > 0 &&
                    s.HalfCostPerk != null &&
                    PerkMap.ContainsKey(s.HalfCostPerk.FormKey) &&
                    s.EquipmentType != null &&
                    (s.EquipmentType.FormKey == Skyrim.EquipType.EitherHand.FormKey ||
                     s.EquipmentType.FormKey == Skyrim.EquipType.LeftHand.FormKey ||
                     s.EquipmentType.FormKey == Skyrim.EquipType.RightHand.FormKey) &&
                    (learnableSpellKeys.Contains(s.FormKey) || IsRulePluginSpell(s)) &&
                    !settings.BlacklistedSpellPlugins.Contains(s.FormKey.ModKey) &&
                    !blacklistedSpellKeys.Contains(s.FormKey) &&
                    !IsTiedToUniqueEntityOrLocation(s, state.LinkCache));

            foreach (var spell in rawSpells)
            {
                var perkData = PerkMap[spell.HalfCostPerk!.FormKey];

                cachedSpells.Add(new CachedSpell
                {
                    Spell = spell,
                    FormKey = spell.FormKey,
                    BaseCost = spell.BaseCost,
                    Element = CustomPerksPatcher.GetSpellElement(spell, Patcher.LinkCache),
                    School = perkData.School,
                    RequiredLevel = perkData.Level,
                    Theme = GetSpellTheme(spell),
                    ExclusiveOnly = !learnableSpellKeys.Contains(spell.FormKey)
                });
            }

            Console.WriteLine("Spell distribution: " + cachedSpells.Count + " spells available, " + encounterableNpcs.Count + " NPCs appear in the world.");
        }

        // ---- Spell classification helpers (ported) ------------------------------------------------------------

        // Teleports, portals, quest-bound and follower-bound spells can't be given to arbitrary NPCs.
        private static bool IsTiedToUniqueEntityOrLocation(ISpellGetter spell, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            string edid = spell.EditorID?.ToLowerInvariant() ?? "";
            string name = spell.Name?.String?.ToLowerInvariant() ?? "";

            if (name.Contains("teleport") || edid.Contains("teleport") ||
                name.Contains("recall") || edid.Contains("recall") ||
                name.Contains("portal") || edid.Contains("portal") ||
                name.Contains("dimension") || edid.Contains("dimension") ||
                name.Contains("milestone") || edid.Contains("playerhome") || edid.Contains("planewalk"))
            {
                return true;
            }

            if (spell.Effects == null) return false;

            foreach (var effect in spell.Effects)
            {
                if (HasUniqueConditions(effect.Conditions)) return true;

                if (effect.BaseEffect.TryResolve(linkCache, out var mgef))
                {
                    if (HasUniqueConditions(mgef.Conditions)) return true;
                    if (CheckEffectForLocationsAndFollowers(mgef, linkCache)) return true;
                }
            }

            return false;
        }

        private static bool HasUniqueConditions(IReadOnlyList<IConditionGetter>? conditions)
        {
            if (conditions == null) return false;

            foreach (var condition in conditions)
            {
                if (condition?.Data == null) continue;

                var functionProperty = condition.Data.GetType().GetProperty("Function");
                if (functionProperty != null)
                {
                    var functionValue = functionProperty.GetValue(condition.Data);
                    if (functionValue != null)
                    {
                        string funcName = functionValue.ToString() ?? "";

                        if (funcName == "GetIsID" ||
                            funcName == "GetInCell" ||
                            funcName == "GetInWorldspace" ||
                            funcName == "GetStage" ||
                            funcName == "GetStageDone" ||
                            funcName == "GetQuestCompleted" ||
                            funcName == "GetQuestRunning")
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool CheckEffectForLocationsAndFollowers(IMagicEffectGetter mgef, ILinkCache<ISkyrimMod, ISkyrimModGetter> linkCache)
        {
            string[] potentialPropertyNames = { "AssociatedItem", "AssociatedDisplayItem", "AssocItem" };

            foreach (var propName in potentialPropertyNames)
            {
                var property = mgef.GetType().GetProperty(propName);
                if (property != null)
                {
                    var value = property.GetValue(mgef);

                    if (value is IFormLinkGetter formLink)
                    {
                        if (formLink.TryResolve<INpcGetter>(linkCache, out var summonedNpc))
                        {
                            if (summonedNpc.Configuration?.Flags.HasFlag(NpcConfiguration.Flag.Unique) == true)
                                return true;

                            if (summonedNpc.Factions != null)
                            {
                                foreach (var factionEntry in summonedNpc.Factions)
                                {
                                    uint fid = factionEntry.Faction.FormKey.ID;
                                    if (fid == 0x05C84D || fid == 0x05C84E || fid == 0x0BD732 || fid == 0x0E0CD9)
                                        return true;
                                }
                            }
                        }
                        else if (formLink.TryResolve<ICellGetter>(linkCache, out _)) return true;
                        else if (formLink.TryResolve<IWorldspaceGetter>(linkCache, out _)) return true;
                        else if (formLink.TryResolve<ILocationGetter>(linkCache, out _)) return true;
                        else if (formLink.TryResolve<IDoorGetter>(linkCache, out _)) return true;
                        else if (formLink.TryResolve<IQuestGetter>(linkCache, out _)) return true;
                    }
                }
            }

            return false;
        }

        // The element an NPC is built around: from its name or EditorID if it says so, otherwise from the spells it already knows.
        private static SpellElement GetNpcArchetype(string editorID, string name, List<ISpellGetter> knownSpells, ILinkCache linkCache)
        {
            if (FireNameWords.IsMatch(name) || editorID.Contains("pyromancer")) return SpellElement.Fire;
            if (FrostNameWords.IsMatch(name) || editorID.Contains("cryomancer")) return SpellElement.Frost;
            if (ShockNameWords.IsMatch(name) || editorID.Contains("electromancer")) return SpellElement.Shock;
            if (PoisonNameWords.IsMatch(name) || editorID.Contains("poison")) return SpellElement.Poison;
            if (NecromancyNameWords.IsMatch(name) || editorID.Contains("necromancer")) return SpellElement.Necromancy;
            if (SummoningNameWords.IsMatch(name) || editorID.Contains("conjurer") || editorID.Contains("summoner")) return SpellElement.Summoning;

            int fireCount = 0, frostCount = 0, shockCount = 0, poisonCount = 0, summoningCount = 0, necromancyCount = 0;
            foreach (ISpellGetter spell in knownSpells)
            {
                switch (CustomPerksPatcher.GetSpellElement(spell, linkCache))
                {
                    case SpellElement.Fire: fireCount++; break;
                    case SpellElement.Frost: frostCount++; break;
                    case SpellElement.Shock: shockCount++; break;
                    case SpellElement.Poison: poisonCount++; break;
                    case SpellElement.Summoning: summoningCount++; break;
                    case SpellElement.Necromancy: necromancyCount++; break;
                }
            }

            int activeElements = (fireCount > 0 ? 1 : 0) + (frostCount > 0 ? 1 : 0) + (shockCount > 0 ? 1 : 0) +
                                 (poisonCount > 0 ? 1 : 0) + (summoningCount > 0 ? 1 : 0) + (necromancyCount > 0 ? 1 : 0);
            if (activeElements > 1) return SpellElement.Mixed;

            if (fireCount > 0) return SpellElement.Fire;
            if (frostCount > 0) return SpellElement.Frost;
            if (shockCount > 0) return SpellElement.Shock;
            if (poisonCount > 0) return SpellElement.Poison;
            if (summoningCount > 0) return SpellElement.Summoning;
            if (necromancyCount > 0) return SpellElement.Necromancy;

            return SpellElement.None;
        }

        // All real spells behind a spell record, looking through leveled spell lists.
        private static IEnumerable<ISpellGetter> GetAllBaseSpells(IFormLinkGetter<ISpellRecordGetter> effectLink, ILinkCache linkCache, HashSet<FormKey> visited)
        {
            if (!effectLink.TryResolve(linkCache, out var resolvedEffect) || !visited.Add(resolvedEffect.FormKey))
                yield break;

            if (resolvedEffect is ISpellGetter spell)
            {
                yield return spell;
            }
            else if (resolvedEffect is ILeveledSpellGetter leveledSpell && leveledSpell.Entries != null)
            {
                foreach (var entry in leveledSpell.Entries)
                {
                    if (entry.Data != null && entry.Data.Reference != null)
                    {
                        foreach (var nestedSpell in GetAllBaseSpells(entry.Data.Reference, linkCache, visited))
                            yield return nestedSpell;
                    }
                }
            }
        }

        // Some spells only work for certain races or keywords, check that the NPC can actually cast them.
        private static bool CasterSatisfiesSpellConditions(ISpellGetter spell, INpcGetter npc, IRaceGetter race)
        {
            if (spell.Effects == null) return true;

            var spellEffectConditions = spell.Effects.SelectMany(e => e.Conditions ?? Enumerable.Empty<IConditionGetter>());

            foreach (var condition in spellEffectConditions)
            {
                if (condition == null) continue;

                try
                {
                    dynamic dynamicCondition = condition;
                    var runOn = dynamicCondition.RunOn;
                    if ((int)runOn != 0 && runOn.ToString() != "Subject") continue;
                }
                catch
                {
                    // Fallback for variation in Mutagen versions
                }

                try
                {
                    dynamic funcData = condition.Data;
                    if (funcData == null) continue;

                    Condition.Function function = (Condition.Function)funcData.Function;
                    object paramOne = funcData.ParameterOne;

                    if (function == Condition.Function.GetIsRace && paramOne is IFormLinkGetter raceLink)
                    {
                        bool matchesRace = npc.Race.FormKey == raceLink.FormKey;
                        if (condition.CompareOperator == CompareOperator.EqualTo && !matchesRace) return false;
                        if (condition.CompareOperator == CompareOperator.NotEqualTo && matchesRace) return false;
                    }

                    if (function == Condition.Function.HasKeyword && paramOne is IFormLinkGetter keywordLink)
                    {
                        var combinedKeywords = new HashSet<FormKey>();
                        if (npc.Keywords != null) combinedKeywords.UnionWith(npc.Keywords.Select(k => k.FormKey));
                        if (race.Keywords != null) combinedKeywords.UnionWith(race.Keywords.Select(k => k.FormKey));

                        bool hasTargetKeyword = combinedKeywords.Contains(keywordLink.FormKey);
                        if (condition.CompareOperator == CompareOperator.EqualTo && !hasTargetKeyword) return false;
                        if (condition.CompareOperator == CompareOperator.NotEqualTo && hasTargetKeyword) return false;
                    }
                }
                catch
                {
                    continue;
                }
            }

            return true;
        }

        // The highest rank of each school's spell perks the NPC already has.
        private static Dictionary<MagicSchool, PerkLevel> DetermineHighestPerks(Npc npc)
        {
            var highestPerks = new Dictionary<MagicSchool, PerkLevel>();
            if (npc.Perks == null) return highestPerks;

            foreach (var perkRef in npc.Perks)
            {
                if (PerkMap.TryGetValue(perkRef.Perk.FormKey, out var perkInfo))
                {
                    highestPerks.TryGetValue(perkInfo.School, out var currentHighest);
                    if (perkInfo.Level > currentHighest)
                        highestPerks[perkInfo.School] = perkInfo.Level;
                }
            }

            return highestPerks;
        }

        private static float CalculateEstimatedMagicka(Npc npc, int level)
        {
            float magicka = 100f + npc.Configuration.MagickaOffset;

            if (npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique))
                magicka += (level - 1) * 10f;
            else
                magicka += (level - 1) * 4f;

            return magicka;
        }

        // ---- The distribution -------------------------------------------------------------------------------------

        private const int ArchmageLevel = 40;

        // Unique NPCs count as archmages from a lower level, but not below it: a low level unique mage must not be given
        // every element, concentration or ground-targeted spells.
        private const int UniqueArchmageLevel = 30;

        private static bool IsArchmage(Npc npc, int level)
        {
            return level >= ArchmageLevel || (level >= UniqueArchmageLevel && npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique));
        }

        private static bool Skip(Npc npc, string reason)
        {
            PatchReport.SpellSkipped(npc, reason);
            return false;
        }

        // The level TUS gave the NPC, so spell tiers match what the NPC will actually be.
        private static int GetNpcLevel(Npc npc)
        {
            int level = (npc.Configuration.Level as NpcLevel)?.Level ?? 0;
            if (level <= 0)
                level = npc.Configuration.CalcMinLevel > 0 ? npc.Configuration.CalcMinLevel : 1;

            return level;
        }

        // The highest spell tier a level allows.
        private static PerkLevel GetLevelTier(int level)
        {
            if (level >= 50) return PerkLevel.Master;
            if (level >= 40) return PerkLevel.Expert;
            if (level >= 25) return PerkLevel.Adept;
            if (level >= 10) return PerkLevel.Apprentice;
            if (level >= 1) return PerkLevel.Novice;
            return PerkLevel.None;
        }

        private static bool AddSpells(Npc npc, List<ISpellGetter> spellsToAdd)
        {
            if (spellsToAdd.Count == 0) return false;

            npc.ActorEffect ??= new ExtendedList<IFormLinkGetter<ISpellRecordGetter>>();
            foreach (var spellToAdd in spellsToAdd)
                npc.ActorEffect.Add(spellToAdd.ToLink());

            if (givenNpcs.Add(npc.FormKey))
                ++npcsGivenSpells;

            spellsGiven += spellsToAdd.Count;
            PatchReport.Add(npc, "Spells added (" + spellsToAdd.Count + "): " + string.Join(", ", spellsToAdd.Select(spell => spell.EditorID ?? spell.FormKey.ToString())));
            return true;
        }

        // Gives the NPC the spells its rules say it must receive. Explicit spells are given as they are. Spells selected by plugin
        // are limited by the NPC's level, magicka and race, like regular spells. Races with their own casting animations only get
        // spells that cast like their vanilla ones. Anything the rules forbid is never given, even if another rule asks for it.
        private static bool AddRuleSpells(Npc npc, NpcSpellRules rules, ILinkCache linkCache, IRaceGetter race)
        {
            int npcLevel = GetNpcLevel(npc);
            PerkLevel tierCap = GetLevelTier(npcLevel);
            float npcMagicka = CalculateEstimatedMagicka(npc, npcLevel);
            bool isArchmage = IsArchmage(npc, npcLevel);
            bool isRestrictedRace = RestrictedAnimationRaces.Contains(race.FormKey);

            HashSet<FormKey> knownSpellKeys = new();
            var vanillaCastSignatures = new HashSet<(CastType Cast, TargetType Target, FormKey Equipment, double Duration)>();
            if (npc.ActorEffect != null)
            {
                HashSet<FormKey> visited = new();
                foreach (var effectLink in npc.ActorEffect)
                {
                    knownSpellKeys.Add(effectLink.FormKey);
                    foreach (ISpellGetter knownSpell in GetAllBaseSpells(effectLink, linkCache, visited))
                    {
                        knownSpellKeys.Add(knownSpell.FormKey);
                        if (VanillaMasters.Contains(knownSpell.FormKey.ModKey.FileName.String) && knownSpell.Type == SpellType.Spell && knownSpell.BaseCost > 0)
                            vanillaCastSignatures.Add(GetCastSignature(knownSpell));
                    }
                }
            }

            List<ISpellGetter> spellsToAdd = new();
            HashSet<FormKey> chosen = new();

            // Spells named by EditorID.
            foreach (CompiledRule rule in rules.Rules)
            {
                foreach (ISpellGetter spell in rule.ExplicitSpells)
                {
                    if (knownSpellKeys.Contains(spell.FormKey) || chosen.Contains(spell.FormKey)) continue;

                    if (rules.Blocks(spell, CustomPerksPatcher.GetSpellElement(spell, linkCache), GetSchoolName(spell)))
                    {
                        PatchReport.Trace(npc, "Spell rule '" + rule.Definition.Name + "': " + spell.EditorID + " not given, a rule forbids it");
                        continue;
                    }

                    if (isRestrictedRace && !vanillaCastSignatures.Contains(GetCastSignature(spell)))
                    {
                        PatchReport.Trace(npc, "Spell rule '" + rule.Definition.Name + "': " + spell.EditorID + " not given, it doesn't cast like this race's vanilla spells");
                        continue;
                    }

                    chosen.Add(spell.FormKey);
                    spellsToAdd.Add(spell);
                }
            }

            // Spells selected by plugin.
            List<string> mustPlugins = rules.Rules.SelectMany(rule => rule.Definition.MustReceive.Plugins).ToList();
            if (mustPlugins.Count > 0)
            {
                foreach (CachedSpell cachedSpell in cachedSpells)
                {
                    if (!mustPlugins.Any(snippet => IsFromPlugin(cachedSpell.Spell, snippet))) continue;
                    if (knownSpellKeys.Contains(cachedSpell.FormKey) || chosen.Contains(cachedSpell.FormKey)) continue;
                    if (rules.Blocks(cachedSpell.Spell, cachedSpell.Element, cachedSpell.School.ToString())) continue;
                    if (cachedSpell.RequiredLevel > tierCap) continue;
                    if (cachedSpell.BaseCost > npcMagicka) continue;

                    // Concentration and ground-targeted spells are reserved for high level casters, like in the regular distribution.
                    if (!isArchmage && (cachedSpell.Spell.CastType == CastType.Concentration || cachedSpell.Spell.TargetType == TargetType.TargetLocation)) continue;

                    if (isRestrictedRace && !vanillaCastSignatures.Contains(GetCastSignature(cachedSpell.Spell))) continue;
                    if (!CasterSatisfiesSpellConditions(cachedSpell.Spell, npc, race)) continue;

                    chosen.Add(cachedSpell.FormKey);
                    spellsToAdd.Add(cachedSpell.Spell);
                }
            }

            if (spellsToAdd.Count == 0)
            {
                PatchReport.Trace(npc, "Spell rules: no must-receive spell was added (none fit, or all were already known or forbidden)");
                return false;
            }

            return AddSpells(npc, spellsToAdd);
        }

        // Gives the NPC fitting spells. Call after the NPC's static level has been set. Returns true if spells were added.
        public static bool DistributeSpells(Npc npc, ILinkCache linkCache)
        {
            if (cachedSpells.Count == 0 && compiledRules.Count == 0) return false;

            if (npc.IsDeleted) return Skip(npc, "deleted record");
            if (npc.FormKey == Skyrim.Npc.Player.FormKey) return false;
            if (npc.Configuration.TemplateFlags.HasFlag(NpcConfiguration.TemplateFlag.SpellList)) return Skip(npc, "inherits its template's spell list");

            if (!npc.Race.TryResolve(linkCache, out var race)) return Skip(npc, "race not found");
            if (race.Flags.HasFlag(Race.Flag.Child)) return Skip(npc, "child race");

            bool isAllowedActorType = race.FormKey == Skyrim.Race.DragonPriestRace.FormKey ||
                                      race.FormKey == Skyrim.Race.HagravenRace.FormKey ||
                                      (race.Keywords != null && race.Keywords.Any(k => k.FormKey == Skyrim.Keyword.ActorTypeNPC.FormKey));
            if (!isAllowedActorType) return Skip(npc, "race is not a humanoid, dragon priest or hagraven");

            bool isRestrictedRace = RestrictedAnimationRaces.Contains(race.FormKey);

            string editorId = npc.EditorID?.ToLowerInvariant() ?? "";
            string npcNameLower = npc.Name?.String?.ToLowerInvariant() ?? "";

            if (NpcSkipKeywords.Any(keyword => editorId.Contains(keyword) || npcNameLower.Contains(keyword))) return Skip(npc, "name or EditorID contains a skip word (summon, ghost, spirit, fx, test)");
            if (editorId.Contains("template") || editorId.Contains("dummy") || editorId.Contains("test") || editorId.Contains("preset") || npcNameLower.Contains("preset")) return Skip(npc, "EditorID or name marks it as a template, dummy, test or preset");

            NpcSpellRules? rules = ResolveRules(npc, race, linkCache);

            // The regular distribution first, so the spells a rule adds don't change which element or school the NPC counts as.
            bool changed = false;
            if (rules is null || rules.NaturalDistribution)
                changed |= DistributeRegularSpells(npc, linkCache, race, rules, isRestrictedRace, editorId, npcNameLower);
            else
                PatchReport.Trace(npc, "Regular spell distribution is turned off by a spell rule");

            if (rules is not null)
                changed |= AddRuleSpells(npc, rules, linkCache, race);

            return changed;
        }

        // The regular distribution: spells that fit the NPC's schools, element, level tier, cast style and magicka.
        private static bool DistributeRegularSpells(Npc npc, ILinkCache linkCache, IRaceGetter race, NpcSpellRules? rules, bool isRestrictedRace, string editorId, string npcNameLower)
        {
            bool isUnique = npc.Configuration.Flags.HasFlag(NpcConfiguration.Flag.Unique);
            if (!encounterableNpcs.Contains(npc.FormKey) && !isUnique)
                return Skip(npc, "not placed in the world, not in a leveled list and not unique");

            int npcLevel = GetNpcLevel(npc);

            // Spells the NPC already knows.
            HashSet<FormKey> npcKnownSpellKeys = new();
            List<ISpellGetter> flattenedSpells = new();
            if (npc.ActorEffect != null)
            {
                HashSet<FormKey> visited = new();
                foreach (var effectLink in npc.ActorEffect)
                {
                    npcKnownSpellKeys.Add(effectLink.FormKey);
                    flattenedSpells.AddRange(GetAllBaseSpells(effectLink, linkCache, visited));
                }
            }

            SpellElement npcArchetype = GetNpcArchetype(editorId, npcNameLower, flattenedSpells, linkCache);
            float npcMagicka = CalculateEstimatedMagicka(npc, npcLevel);

            bool isMagicUser = false;
            Dictionary<MagicSchool, int> schoolTallies = new();
            Dictionary<MagicSchool, PerkLevel> highestKnownSpellLevels = new();

            // Track highest known tier per unique element combo
            Dictionary<(MagicSchool School, SpellElement Element), PerkLevel> highestKnownSpellLevelsElement = new();

            var allowedCastTypes = new HashSet<CastType>();
            var allowedTargetTypes = new HashSet<TargetType>();
            float maxAllowedCastTime = 1.0f;
            var vanillaCastSignatures = new HashSet<(CastType Cast, TargetType Target, FormKey Equipment, double Duration)>();

            // Necromancers and summoners are magic users even before they know any spell.
            if (npcArchetype == SpellElement.Necromancy || npcArchetype == SpellElement.Summoning)
            {
                isMagicUser = true;
                allowedCastTypes.Add(CastType.FireAndForget);
                allowedTargetTypes.Add(TargetType.Aimed);
            }

            foreach (var existingSpell in flattenedSpells)
            {
                npcKnownSpellKeys.Add(existingSpell.FormKey);

                if (existingSpell.Type != SpellType.Spell || existingSpell.BaseCost <= 0)
                    continue;

                isMagicUser = true;

                if (VanillaMasters.Contains(existingSpell.FormKey.ModKey.FileName.String))
                {
                    allowedCastTypes.Add(existingSpell.CastType);
                    allowedTargetTypes.Add(existingSpell.TargetType);
                    vanillaCastSignatures.Add(GetCastSignature(existingSpell));
                    if (existingSpell.CastDuration > maxAllowedCastTime)
                        maxAllowedCastTime = existingSpell.CastDuration;
                }

                if (existingSpell.HalfCostPerk != null && PerkMap.TryGetValue(existingSpell.HalfCostPerk.FormKey, out var existingPerk))
                {
                    schoolTallies.TryGetValue(existingPerk.School, out int tally);
                    schoolTallies[existingPerk.School] = tally + 1;

                    highestKnownSpellLevels.TryGetValue(existingPerk.School, out var currentHighest);
                    if (existingPerk.Level > currentHighest) highestKnownSpellLevels[existingPerk.School] = existingPerk.Level;

                    var spellElement = CustomPerksPatcher.GetSpellElement(existingSpell, linkCache);
                    var elementKey = (existingPerk.School, spellElement);
                    highestKnownSpellLevelsElement.TryGetValue(elementKey, out var currentElementHighest);
                    if (existingPerk.Level > currentElementHighest) highestKnownSpellLevelsElement[elementKey] = existingPerk.Level;
                }
            }

            // --- The NPC's magical identity: the schools it favours (ties included) ---
            HashSet<MagicSchool> allowedSchools = new();
            var highestPerks = DetermineHighestPerks(npc);

            if (npcArchetype == SpellElement.Necromancy || npcArchetype == SpellElement.Summoning)
                allowedSchools.Add(MagicSchool.Conjuration);

            if (schoolTallies.Count > 0)
            {
                var topTallies = schoolTallies.Values.Distinct().OrderByDescending(v => v).ToList();
                int threshold = topTallies.Count > 1 ? topTallies[1] : topTallies[0];

                foreach (var kvp in schoolTallies)
                {
                    if (kvp.Value >= threshold)
                        allowedSchools.Add(kvp.Key);
                }
            }

            HashSet<SpellTheme> blockedThemesForThisNpc = new();
            foreach (var kvp in ThemeBlacklist)
            {
                if (editorId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) || npcNameLower.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var theme in kvp.Value)
                        blockedThemesForThisNpc.Add(theme);
                }
            }

            List<ISpellGetter> spellsToAdd = new();
            bool receivedForcedSpells = false;

            List<SpellTheme> activeForcedThemes = new();
            if (ThemeOverrides.TryGetValue(editorId, out var directOverrides))
                activeForcedThemes.AddRange(directOverrides);

            if (editorId.Contains("vampire") && !blockedThemesForThisNpc.Contains(SpellTheme.Vampire) && !activeForcedThemes.Contains(SpellTheme.Vampire))
                activeForcedThemes.Add(SpellTheme.Vampire);

            if (editorId.Contains("vigilant") && !blockedThemesForThisNpc.Contains(SpellTheme.Holy) && !activeForcedThemes.Contains(SpellTheme.Holy))
                activeForcedThemes.Add(SpellTheme.Holy);

            // Themed spells for NPCs that are forced into a theme.
            if (activeForcedThemes.Count > 0)
            {
                foreach (var cachedSpell in cachedSpells)
                {
                    if (cachedSpell.ExclusiveOnly) continue;
                    if (rules is not null && rules.Blocks(cachedSpell.Spell, cachedSpell.Element, cachedSpell.School.ToString())) continue;
                    if (npcKnownSpellKeys.Contains(cachedSpell.FormKey)) continue;
                    if (blockedThemesForThisNpc.Contains(cachedSpell.Theme)) continue;
                    if (cachedSpell.BaseCost > npcMagicka) continue;

                    if (!allowedCastTypes.Contains(cachedSpell.Spell.CastType)) continue;
                    if (!allowedTargetTypes.Contains(cachedSpell.Spell.TargetType)) continue;
                    if (cachedSpell.Spell.CastDuration > maxAllowedCastTime) continue;
                    if (isRestrictedRace && !vanillaCastSignatures.Contains(GetCastSignature(cachedSpell.Spell))) continue;

                    if (!CasterSatisfiesSpellConditions(cachedSpell.Spell, npc, race)) continue;

                    if (ExclusivePluginRules.TryGetValue(cachedSpell.FormKey.ModKey.FileName.String, out var isNpcEligibleForForced))
                    {
                        if (!isNpcEligibleForForced(npc)) continue;
                    }

                    if (activeForcedThemes.Contains(cachedSpell.Theme))
                    {
                        spellsToAdd.Add(cachedSpell.Spell);
                        npcKnownSpellKeys.Add(cachedSpell.FormKey);
                        receivedForcedSpells = true;
                    }
                }
            }

            // Pure melee and ranged NPCs don't get spells.
            if (npc.Class.TryResolve(linkCache, out var npcClass))
            {
                string classId = npcClass.EditorID?.ToLowerInvariant() ?? "";
                if (classId.Contains("archer") || classId.Contains("warrior") || classId.Contains("melee") ||
                    classId.Contains("berserker") || classId.Contains("thief") || classId.Contains("rogue") ||
                    classId.Contains("missile") || classId.Contains("tank") || classId.Contains("2h") ||
                    classId.Contains("1h") || classId.Contains("assassin") || classId.Contains("banditbow"))
                {
                    isMagicUser = false;
                }
            }

            // Resolved through the read-only view, the same way the original spell patcher did it.
            INpcGetter npcView = npc;
            if (npcView.CombatStyle.TryResolve(linkCache, out var combatStyle))
            {
                string styleId = combatStyle.EditorID?.ToLowerInvariant() ?? "";
                if (styleId.Contains("archer") || styleId.Contains("warrior") || styleId.Contains("melee") ||
                    styleId.Contains("berserker") || styleId.Contains("missile") || styleId.Contains("ranged") ||
                    styleId.Contains("dualwield") || styleId.Contains("2hm") || styleId.Contains("1hm"))
                {
                    isMagicUser = false;
                }
            }

            if (!isMagicUser && !receivedForcedSpells) return Skip(npc, "not a magic user (no spell with a cost, no summoning or necromancy archetype, or excluded by class or combat style)");

            // Standard distribution
            if (isMagicUser)
            {
                bool isArchmage = IsArchmage(npc, npcLevel);
                if (isArchmage)
                {
                    // Races with their own casting animations keep to the cast styles of their vanilla spells.
                    if (!isRestrictedRace)
                    {
                        allowedCastTypes.Add(CastType.Concentration);
                        allowedTargetTypes.Add(TargetType.TargetLocation);
                    }

                    npcArchetype = SpellElement.Mixed;
                }

                PerkLevel levelBasedFloor = GetLevelTier(npcLevel);

                // The highest spell tier the NPC may receive per school and element.
                Dictionary<(MagicSchool School, SpellElement Element), PerkLevel> effectiveElementLevels = new();
                var elementsToEvaluate = new[]
                {
                    SpellElement.Fire, SpellElement.Frost, SpellElement.Shock,
                    SpellElement.Poison, SpellElement.Necromancy, SpellElement.Summoning
                };

                foreach (var school in allowedSchools)
                {
                    highestPerks.TryGetValue(school, out var perkLevel);
                    highestKnownSpellLevels.TryGetValue(school, out var generalSpellLevel);

                    int schoolBaseFloor = Math.Max((int)perkLevel, (int)levelBasedFloor);
                    int generalMax = Math.Max(schoolBaseFloor, (int)generalSpellLevel);

                    effectiveElementLevels[(school, SpellElement.None)] = (PerkLevel)generalMax;
                    effectiveElementLevels[(school, SpellElement.Mixed)] = (PerkLevel)generalMax;

                    // An element is limited by the NPC's native spell expertise, unless the NPC is built around that element.
                    foreach (SpellElement element in elementsToEvaluate)
                    {
                        highestKnownSpellLevelsElement.TryGetValue((school, element), out var elementSpellLevel);
                        effectiveElementLevels[(school, element)] = npcArchetype == element ? (PerkLevel)generalMax : elementSpellLevel;
                    }
                }

                bool isVampireNpc =
                    (npc.Keywords != null && npc.Keywords.Any(k => k.FormKey == Skyrim.Keyword.Vampire.FormKey)) ||
                    (npc.Factions != null && npc.Factions.Any(f => f.Faction.FormKey == Skyrim.Faction.VampireFaction.FormKey)) ||
                    editorId.Contains("vampire");

                bool isHolyNpc =
                    (npc.Factions != null && npc.Factions.Any(f => f.Faction.FormKey == Skyrim.Faction.VigilantOfStendarrFaction.FormKey)) ||
                    editorId.Contains("stendarr");

                foreach (var cachedSpell in cachedSpells)
                {
                    if (cachedSpell.ExclusiveOnly) continue;
                    if (rules is not null && rules.Blocks(cachedSpell.Spell, cachedSpell.Element, cachedSpell.School.ToString())) continue;
                    if (npcKnownSpellKeys.Contains(cachedSpell.FormKey)) continue;

                    if (!allowedCastTypes.Contains(cachedSpell.Spell.CastType)) continue;
                    if (!allowedTargetTypes.Contains(cachedSpell.Spell.TargetType)) continue;
                    if (cachedSpell.Spell.CastDuration > maxAllowedCastTime) continue;
                    if (isRestrictedRace && !vanillaCastSignatures.Contains(GetCastSignature(cachedSpell.Spell))) continue;

                    if (!CasterSatisfiesSpellConditions(cachedSpell.Spell, npc, race)) continue;

                    if (ExclusivePluginRules.TryGetValue(cachedSpell.FormKey.ModKey.FileName.String, out var isNpcEligibleForStandard))
                    {
                        if (!isNpcEligibleForStandard(npc)) continue;
                    }

                    if (cachedSpell.BaseCost > npcMagicka) continue;
                    if (blockedThemesForThisNpc.Contains(cachedSpell.Theme)) continue;

                    if (cachedSpell.Theme == SpellTheme.Vampire && !isVampireNpc) continue;
                    if (cachedSpell.Theme == SpellTheme.Holy && !isHolyNpc) continue;

                    if (cachedSpell.Element != SpellElement.None && cachedSpell.Element != SpellElement.Mixed)
                    {
                        if (npcArchetype != SpellElement.Mixed && npcArchetype != cachedSpell.Element) continue;
                    }

                    if (!allowedSchools.Contains(cachedSpell.School)) continue;

                    effectiveElementLevels.TryGetValue((cachedSpell.School, cachedSpell.Element), out var npcHighestLevel);
                    if (npcHighestLevel < cachedSpell.RequiredLevel) continue;

                    spellsToAdd.Add(cachedSpell.Spell);
                }
            }

            if (spellsToAdd.Count == 0)
                return Skip(npc, "magic user, but no available spell fit its schools, element, spell tier, cast style or magicka");

            return AddSpells(npc, spellsToAdd);
        }
    }
}
