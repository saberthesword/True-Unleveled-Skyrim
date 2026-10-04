using Noggog;
using Mutagen.Bethesda;
using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Synthesis;
using Mutagen.Bethesda.Skyrim;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Plugins.Exceptions;
using Mutagen.Bethesda.Plugins.Records;
using Mutagen.Bethesda.FormKeys.SkyrimSE;
using Mutagen.Bethesda.Plugins.Order;

using TrueUnleveledSkyrim.Config;

namespace TrueUnleveledSkyrim.Patch
{
    class ZonesPatcher
    {
        private static ZoneList? ZonesByKeyword;
        private static ZoneList? ZonesByID;

        private static void UnlevelZone(EncounterZone encZone, ZoneEntry zoneDefinition)
        {
            // Seeded per record so the result doesn't depend on processing order.
            Random random = Patcher.RandomFor(encZone.FormKey, "zone");

            encZone.Flags.SetFlag(EncounterZone.Flag.MatchPcBelowMinimumLevel, false);
            if(zoneDefinition.EnableCombatBoundary is not null)
                encZone.Flags.SetFlag(EncounterZone.Flag.DisableCombatBoundary, !(bool)zoneDefinition.EnableCombatBoundary);

            if (Patcher.ModSettings.Value.Zones.StaticZoneLevels)
            {
                encZone.MinLevel = (byte)random.Next(zoneDefinition.MinLevel, zoneDefinition.MaxLevel);
                encZone.MaxLevel = encZone.MinLevel;
            }
            else
            {
                if (zoneDefinition.MaxLevel == 0)
                {
                    encZone.MinLevel = (byte)random.Next(zoneDefinition.MinLevel, zoneDefinition.MinLevel + zoneDefinition.Range);
                    encZone.MaxLevel = 0;
                }
                else
                {
                    // Random.Next throws when the upper bound is below the lower one, which a misconfigured range could cause.
                    int upperBound = Math.Max(zoneDefinition.MinLevel, zoneDefinition.MaxLevel - zoneDefinition.Range + 1);
                    encZone.MinLevel = (byte)random.Next(zoneDefinition.MinLevel, upperBound);
                    encZone.MaxLevel = (byte)(encZone.MinLevel + zoneDefinition.Range);
                }
            }
        }

        private static bool MatchesDefinition(ZoneEntry zoneDefinition, string id)
        {
            return zoneDefinition.Keys.Any(key => id.Equals(key, StringComparison.OrdinalIgnoreCase)) &&
                   !zoneDefinition.ForbiddenKeys.Any(key => id.Equals(key, StringComparison.OrdinalIgnoreCase));
        }

        // Finds the first matching definition (searching from the end of the list) based on the keywords of the zone's location.
        private static ZoneEntry? FindZoneByKeyword(IEncounterZoneGetter encZone, ILinkCache linkCache)
        {
            if (!encZone.Location.TryResolve<ILocationGetter>(linkCache, out ILocationGetter? resolvedLocation))
                return null;

            // Resolve the location's keywords once instead of once per definition.
            List<string> keywordIDs = new();
            foreach (var keywordEntry in resolvedLocation.Keywords.EmptyIfNull())
            {
                if (keywordEntry.TryResolve<IKeywordGetter>(linkCache, out IKeywordGetter? resolvedKeyword) && resolvedKeyword.EditorID is not null)
                    keywordIDs.Add(resolvedKeyword.EditorID);
            }

            if (keywordIDs.Count == 0)
                return null;

            for (int i = ZonesByKeyword!.Zones.Count - 1; i >= 0; i--)
            {
                ZoneEntry zoneDefinition = ZonesByKeyword.Zones[i];
                if (keywordIDs.Any(id => MatchesDefinition(zoneDefinition, id)))
                    return zoneDefinition;
            }

            return null;
        }

        // Finds the first matching definition (searching from the end of the list) based on the zone's EditorID.
        private static ZoneEntry? FindZoneByID(IEncounterZoneGetter encZone)
        {
            if (encZone.EditorID is null)
                return null;

            for (int i = ZonesByID!.Zones.Count - 1; i >= 0; i--)
            {
                ZoneEntry zoneDefinition = ZonesByID.Zones[i];
                if (MatchesDefinition(zoneDefinition, encZone.EditorID))
                    return zoneDefinition;
            }

            return null;
        }

        // Overrides a leveled actor multiplier game setting with the given value.
        private static void SetLeveledActorMult(IFormLinkGetter<IGameSettingGetter> settingLink, float value, IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            IGameSettingGetter? original = settingLink.TryResolve(Patcher.LinkCache);
            if (original is null)
            {
                Console.WriteLine("Warning: could not find game setting " + settingLink.FormKey + ", leaving it unchanged.");
                return;
            }

            if (original.DeepCopy() is not GameSettingFloat copy)
            {
                Console.WriteLine("Warning: game setting " + settingLink.FormKey + " is not a float, leaving it unchanged.");
                return;
            }

            copy.Data = value;
            state.PatchMod.GameSettings.Set(copy);
        }

        public static void PatchZones(IPatcherState<ISkyrimMod, ISkyrimModGetter> state)
        {
            if (Patcher.ModSettings.Value.Zones.UseMorrowlootZoneBalance)
            {
                ZonesByKeyword = JsonHelper.LoadConfig<ZoneList>(TUSConstants.ZoneTyesKeywordMLUPath);
                ZonesByID = JsonHelper.LoadConfig<ZoneList>(TUSConstants.ZoneTyesEDIDMLUPath);
            }
            else
            {
                ZonesByKeyword = JsonHelper.LoadConfig<ZoneList>(TUSConstants.ZoneTyesKeywordPath);
                ZonesByID = JsonHelper.LoadConfig<ZoneList>(TUSConstants.ZoneTyesEDIDPath);
            }

            uint processedRecords = 0;
            uint changedRecords = 0;
            var forbiddenCache = LoadOrder.Import<ISkyrimModGetter>(state.DataFolderPath, Patcher.ModSettings.Value.Zones.PluginFilter, GameRelease.SkyrimSE).PriorityOrder.ToImmutableLinkCache();
            foreach (var zoneGetter in state.LoadOrder.PriorityOrder.EncounterZone().WinningOverrides())
            {
                try
                {
                    ++processedRecords;
                    if (processedRecords % 100 == 0)
                        Console.WriteLine("Processed " + processedRecords + " encounter zones.");

                    // Skip encounter zones that can be found in the cache defined by the plugin filter list.
                    if (forbiddenCache.TryResolve(zoneGetter.ToLink(), out var forbiddenZone))
                        continue;

                    // Decide on the read-only record first so only zones that actually change get copied.
                    ZoneEntry? zoneDefinition = FindZoneByID(zoneGetter) ?? FindZoneByKeyword(zoneGetter, Patcher.LinkCache);
                    if (zoneDefinition is null)
                        continue;

                    EncounterZone zoneCopy = zoneGetter.DeepCopy();
                    UnlevelZone(zoneCopy, zoneDefinition);
                    state.PatchMod.EncounterZones.Set(zoneCopy);
                    ++changedRecords;
                }
                catch (Exception ex)
                {
                    throw RecordException.Enrich(ex, zoneGetter);
                }
            }

            var zoneSettings = Patcher.ModSettings.Value.Zones;
            SetLeveledActorMult(Skyrim.GameSetting.fLeveledActorMultEasy, zoneSettings.EasySpawnLevelMult, state);
            SetLeveledActorMult(Skyrim.GameSetting.fLeveledActorMultMedium, zoneSettings.NormalSpawnLevelMult, state);
            SetLeveledActorMult(Skyrim.GameSetting.fLeveledActorMultHard, zoneSettings.HardSpawnLevelMult, state);
            SetLeveledActorMult(Skyrim.GameSetting.fLeveledActorMultVeryHard, zoneSettings.VeryHardSpawnLevelMult, state);

            Console.WriteLine("Processed " + processedRecords + " encounter zones in total, changed " + changedRecords + ".\n");
        }
    }
}
