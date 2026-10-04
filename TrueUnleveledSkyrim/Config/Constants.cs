using System.IO;

using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;


namespace TrueUnleveledSkyrim.Config
{
    public static class TUSConstants
    {
        // List variant postfixes.
        public static string PostfixPart { get; } = "_TUS_";
        public static string WeakPostfix { get; } = "_TUS_Weak";
        public static string StrongPostfix { get; } = "_TUS_Strong";

        // Json config paths.
        public static string ArtifactKeysPath { get; set; } = "artifactKeys.json";
        public static string FollowersPath { get; set; } = "customFollowers.json";
        public static string ExcludedLVLIPath { get; set; } = "excludedLVLI.json";
        public static string ExcludedNPCsPath { get; set; } = "excludedNPCs.json";
        public static string ExcludedPerksPath { get; set; } = "excludedPerks.json";
        public static string NPCEDIDPath { get; set; } = "NPCsByEDID.json";
        public static string NPCFactionPath { get; set; } = "NPCsByFaction.json";
        public static string RaceModifiersPath { get; set; } = "raceLevelModifiers.json";
        public static string ZoneTyesEDIDPath { get; set; } = "zoneTypesByEDID.json";
        public static string ZoneTyesEDIDMLUPath { get; set; } = "zoneTypesByEDIDMLU.json";
        public static string ZoneTyesKeywordPath { get; set; } = "zoneTypesByKeyword.json";
        public static string ZoneTyesKeywordMLUPath { get; set; } = "zoneTypesByKeywordMLU.json";

        public static void GetPaths(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            string settingsPath = state.ExtraSettingsDataPath ?? throw new InvalidOperationException("The patcher's extra settings data path is not set, so the json config files can't be located.");

            ArtifactKeysPath = Path.Combine(settingsPath, ArtifactKeysPath);
            FollowersPath = Path.Combine(settingsPath, FollowersPath);
            ExcludedLVLIPath = Path.Combine(settingsPath, ExcludedLVLIPath);
            ExcludedNPCsPath = Path.Combine(settingsPath, ExcludedNPCsPath);
            ExcludedPerksPath = Path.Combine(settingsPath, ExcludedPerksPath);
            NPCEDIDPath = Path.Combine(settingsPath, NPCEDIDPath);
            NPCFactionPath = Path.Combine(settingsPath, NPCFactionPath);
            RaceModifiersPath = Path.Combine(settingsPath, RaceModifiersPath);
            ZoneTyesEDIDPath = Path.Combine(settingsPath, ZoneTyesEDIDPath);
            ZoneTyesEDIDMLUPath = Path.Combine(settingsPath, ZoneTyesEDIDMLUPath);
            ZoneTyesKeywordPath = Path.Combine(settingsPath, ZoneTyesKeywordPath);
            ZoneTyesKeywordMLUPath = Path.Combine(settingsPath, ZoneTyesKeywordMLUPath);
        }

    }
}
