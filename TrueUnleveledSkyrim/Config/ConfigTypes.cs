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

        // Every non-empty criterion below has to be met for an NPC to qualify for the tree.
        [JsonProperty] public List<string> Factions { get; set; } = new();
        [JsonProperty] public List<string> ActorTypeKeywords { get; set; } = new();
        [JsonProperty] public List<string> RequiredSpellKeywords { get; set; } = new();
        [JsonProperty] public List<string> RequiredSpellIDs { get; set; } = new();
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
