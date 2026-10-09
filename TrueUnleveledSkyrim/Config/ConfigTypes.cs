using System.Collections.Generic;

using Newtonsoft.Json;


namespace TrueUnleveledSkyrim.Config
{
    public abstract class ConfigType {}

    // artifactKeys.json
    public class ArtifactKeys : ConfigType
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
    }

    // customFollowers.json
    public class FollowerEntry
    {
        [JsonProperty] public string Key { get; set; } = string.Empty;
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
    }

    public class FollowerList : ConfigType
    {
        [JsonProperty] public List<FollowerEntry> Followers { get; set; } = new();
    }

    // excludedLVLI.json
    public class ExcludedLVLI : ConfigType
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
    }

    // excludedNPCs.json
    public class ExcludedNPCs : ConfigType
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
    }

    // excludedPerks.json
    public class ExcludedPerks : ConfigType
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
    }

    // NPCsByEDID.json
    public class NPCEDIDEntry
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
        [JsonProperty] public short Level { get; set; }
    }

    public class NPCEDIDs : ConfigType
    {
        [JsonProperty] public List<NPCEDIDEntry> NPCs { get; set; } = new();
    }

    // NPCsByFaction.json
    public class NPCFactionEntry
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
        [JsonProperty] public short? MinLevel { get; set; }
        [JsonProperty] public short? MaxLevel { get; set; }
        [JsonProperty] public short? Level { get; set; }
    }

    public class NPCFactions : ConfigType
    {
        [JsonProperty] public List<NPCFactionEntry> NPCs { get; set; } = new();
    }

    // customPerkTrees.json
    public class CustomPerkEntry
    {
        [JsonProperty] public string? EditorID { get; set; }

        // Skill level (0-100) in the tree's ProxyVanillaSkill an NPC needs before it can receive the perk.
        [JsonProperty] public int RequiredLevel { get; set; }
    }

    public class CustomPerkTree
    {
        [JsonProperty] public string TreeName { get; set; } = string.Empty;

        // The vanilla skill whose perk points are spent on this tree, e.g. "Destruction".
        [JsonProperty] public string ProxyVanillaSkill { get; set; } = string.Empty;

        // Optional. Trees with the same group (e.g. two fire trees) share their perks. When an NPC qualifies for several groups, they take
        // turns being handed perks. Defaults to the tree's own name.
        [JsonProperty] public string Group { get; set; } = string.Empty;

        // Every non-empty criterion below has to be met for an NPC to qualify for the tree.
        [JsonProperty] public List<string> Factions { get; set; } = new();
        [JsonProperty] public List<string> ActorTypeKeywords { get; set; } = new();
        [JsonProperty] public List<string> RequiredSpellKeywords { get; set; } = new();
        [JsonProperty] public List<string> RequiredSpellIDs { get; set; } = new();

        // Spell elements: Fire, Frost, Shock, Poison, Summoning, Necromancy. Classified the same way as in the spell distributor patcher.
        [JsonProperty] public List<string> RequiredSpellElements { get; set; } = new();

        [JsonProperty] public List<string> NameKeys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();

        // Perks of the regular skill trees that qualifying NPCs never receive: by perk EditorID snippet, or by the plugin that created them.
        [JsonProperty] public List<string> BlockedPerkKeys { get; set; } = new();
        [JsonProperty] public List<string> BlockedPerkPlugins { get; set; } = new();

        // The share (0-1) of the proxy skill's perk points spent on this tree. The rest goes to the regular tree.
        [JsonProperty] public float PointShare { get; set; } = 0.5f;

        [JsonProperty] public List<CustomPerkEntry> Perks { get; set; } = new();
    }

    public class CustomPerkTreeList : ConfigType
    {
        [JsonProperty] public List<CustomPerkTree> CustomTrees { get; set; } = new();
    }

    // spellRules.json
    public class SpellSelector
    {
        // Exact spell EditorIDs.
        [JsonProperty] public List<string> SpellIDs { get; set; } = new();

        // Parts of plugin file names, e.g. "Venomancy". Compared ignoring case.
        [JsonProperty] public List<string> Plugins { get; set; } = new();

        // Only used by MustNotReceive: Fire, Frost, Shock, Poison, Summoning, Necromancy, Mixed.
        [JsonProperty] public List<string> Elements { get; set; } = new();

        // Only used by MustNotReceive: Alteration, Conjuration, Destruction, Illusion, Restoration.
        [JsonProperty] public List<string> Schools { get; set; } = new();
    }

    public class SpellRule
    {
        [JsonProperty] public string Name { get; set; } = string.Empty;
        [JsonProperty] public bool Enabled { get; set; } = true;

        // Who the rule applies to. Every non-empty criterion has to match, and at least one has to be set.
        // Keys are matched against the NPC's name and EditorID, and forbidden keys veto the rule.
        [JsonProperty] public List<string> NameKeys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
        [JsonProperty] public List<string> Factions { get; set; } = new();
        [JsonProperty] public List<string> ActorTypeKeywords { get; set; } = new();

        // Spells the NPC always gets. SpellIDs are given as they are. Spells selected by Plugins still respect the NPC's level and magicka.
        [JsonProperty] public SpellSelector MustReceive { get; set; } = new();

        // If false, the NPC gets none of the regular distribution, only the MustReceive spells.
        [JsonProperty] public bool NaturalDistribution { get; set; } = true;

        // Spells the NPC never gets, from any source. Wins over MustReceive.
        [JsonProperty] public SpellSelector MustNotReceive { get; set; } = new();
    }

    // Describes a kind of NPC. Every non-empty criterion has to match, and at least one has to be set.
    public class SpellNpcMatcher
    {
        // Any of these in the NPC's EditorID.
        [JsonProperty] public List<string> EditorIDKeys { get; set; } = new();

        // All of these in the NPC's EditorID.
        [JsonProperty] public List<string> RequiredEditorIDKeys { get; set; } = new();

        // Any of these in the NPC's name or EditorID.
        [JsonProperty] public List<string> NameKeys { get; set; } = new();

        // None of these in the NPC's name or EditorID.
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();

        // Any of these factions (EditorIDs) the NPC is in.
        [JsonProperty] public List<string> Factions { get; set; } = new();

        // Any of these keywords (EditorIDs) on the NPC or its race.
        [JsonProperty] public List<string> ActorTypeKeywords { get; set; } = new();
    }

    // A set of spells (by plugin or EditorID) that only certain NPCs may receive, such as the spells of a vampire magic mod.
    public class SpellGroup
    {
        [JsonProperty] public string Name { get; set; } = string.Empty;
        [JsonProperty] public bool Enabled { get; set; } = true;

        // Which spells belong to the group: any spell from a plugin whose file name contains one of these, or with one of these EditorIDs.
        [JsonProperty] public List<string> Plugins { get; set; } = new();
        [JsonProperty] public List<string> SpellIDs { get; set; } = new();

        // NPCs allowed to receive the group's spells. Nobody else ever gets them through the regular distribution.
        [JsonProperty] public List<SpellNpcMatcher> Members { get; set; } = new();

        // NPCs that always receive the group's spells (if they fit them), whether or not the regular distribution would pick them. They count as members.
        [JsonProperty] public List<SpellNpcMatcher> AlwaysGiven { get; set; } = new();

        // NPCs that never receive the group's spells, even if they match Members or AlwaysGiven.
        [JsonProperty] public List<SpellNpcMatcher> ExcludedNpcs { get; set; } = new();
    }

    public class SpellRuleList : ConfigType
    {
        [JsonProperty] public List<SpellRule> Rules { get; set; } = new();
        [JsonProperty] public List<SpellGroup> SpellGroups { get; set; } = new();
    }

    // raceLevelModifiers.json
    public class RaceEntries
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
        [JsonProperty] public short? LevelModifierAdd { get; set; }
        [JsonProperty] public float? LevelModifierMult { get; set; }
    }

    public class RaceModifiers : ConfigType
    {
        [JsonProperty] public List<RaceEntries> Data { get; set; } = new();
    }

    // zoneTypesBy****.json
    public class ZoneEntry
    {
        [JsonProperty] public List<string> Keys { get; set; } = new();
        [JsonProperty] public List<string> ForbiddenKeys { get; set; } = new();
        [JsonProperty] public short MinLevel { get; set; }
        [JsonProperty] public short MaxLevel { get; set; }
        [JsonProperty] public short Range { get; set; }
        [JsonProperty] public bool? EnableCombatBoundary { get; set; }
    }

    public class ZoneList : ConfigType
    {
        [JsonProperty] public List<ZoneEntry> Zones { get; set; } = new();
    }
}
