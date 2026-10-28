using System.Text;

using Mutagen.Bethesda.Plugins;
using Mutagen.Bethesda.Plugins.Cache;
using Mutagen.Bethesda.Skyrim;

namespace TrueUnleveledSkyrim.Patch
{
    /// <summary>
    /// Optional text report of what the NPC stage did: the spells, perks and custom perk trees each NPC got, why NPCs were skipped
    /// for spells, and a full trace for the NPCs on the watchlist setting. Does nothing unless the WriteReport setting is on.
    /// </summary>
    internal static class PatchReport
    {
        private sealed class NpcEntry
        {
            public NpcEntry(string header) { Header = header; }

            public string Header { get; }
            public List<string> Lines { get; } = new();
        }

        private static readonly Dictionary<FormKey, NpcEntry> entries = new();
        private static readonly Dictionary<string, int> spellSkipReasons = new();
        private static List<string> watchWords = new();

        public static bool Enabled { get; private set; }

        public static void Start()
        {
            entries.Clear();
            spellSkipReasons.Clear();

            var settings = Patcher.ModSettings.Value;
            watchWords = settings.ReportWatchlist.Where(word => !string.IsNullOrWhiteSpace(word)).Select(word => word.Trim()).ToList();

            // Adding words to the watchlist turns the report on, because a trace is useless without somewhere to write it.
            Enabled = settings.WriteReport || watchWords.Count > 0;
        }

        private static bool MatchesWatchlist(string? editorId, string? name)
        {
            return watchWords.Count > 0 && watchWords.Any(word =>
                (editorId?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false) ||
                (name?.Contains(word, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        // True if the report is on and the NPC matches a watchlist word.
        public static bool IsWatched(Npc npc)
        {
            return Enabled && MatchesWatchlist(npc.EditorID, npc.Name?.String);
        }

        private static NpcEntry GetEntry(Npc npc)
        {
            if (!entries.TryGetValue(npc.FormKey, out NpcEntry? entry))
            {
                string level = (npc.Configuration.Level as NpcLevel)?.Level.ToString() ?? "scaled";
                entry = new NpcEntry((npc.EditorID ?? "(no EditorID)") + "  \"" + (npc.Name?.String ?? "") + "\"  [" + npc.FormKey + "]  level " + level);
                entries[npc.FormKey] = entry;
            }

            return entry;
        }

        // Records something that happened to the NPC.
        public static void Add(Npc npc, string line)
        {
            if (Enabled)
                GetEntry(npc).Lines.Add(line);
        }

        // Records something that only matters for NPCs on the watchlist, such as why they did not get something.
        public static void Trace(Npc npc, string line)
        {
            if (IsWatched(npc))
                GetEntry(npc).Lines.Add(line);
        }

        // For NPCs that are dropped before the stages that explain themselves, such as by excludedNPCs.json. Only watchlist NPCs are listed.
        public static void TraceSkipped(INpcGetter npc, string reason)
        {
            if (!Enabled || !MatchesWatchlist(npc.EditorID, npc.Name?.String))
                return;

            if (!entries.TryGetValue(npc.FormKey, out NpcEntry? entry))
            {
                entry = new NpcEntry((npc.EditorID ?? "(no EditorID)") + "  \"" + (npc.Name?.String ?? "") + "\"  [" + npc.FormKey + "]");
                entries[npc.FormKey] = entry;
            }

            entry.Lines.Add("Skipped by the NPC stage before spells and perks: " + reason);
        }

        public static void SpellSkipped(Npc npc, string reason)
        {
            if (!Enabled)
                return;

            spellSkipReasons[reason] = spellSkipReasons.TryGetValue(reason, out int count) ? count + 1 : 1;
            Trace(npc, "Spells: none given - " + reason);
        }

        private static string PerkName(FormKey key, ILinkCache linkCache)
        {
            // Same form as the perk lookups in the NPC stage: the cache resolves a link.
            return linkCache.TryResolve(new FormLink<IPerkGetter>(key), out var perk) && perk.EditorID is not null ? perk.EditorID : key.ToString();
        }

        // Custom perks are reported separately, so only the other changes are listed here.
        public static void AddPerkChanges(Npc npc, List<FormKey> before, List<FormKey> after, ILinkCache linkCache)
        {
            if (!Enabled)
                return;

            List<string> added = after.Except(before).Select(key => PerkName(key, linkCache)).Where(name => !name.StartsWith("TUS_Custom_")).ToList();
            List<string> removed = before.Except(after).Select(key => PerkName(key, linkCache)).ToList();

            if (added.Count > 0)
                Add(npc, "Perks added (" + added.Count + "): " + string.Join(", ", added));

            if (removed.Count > 0)
                Add(npc, "Perks removed (" + removed.Count + "): " + string.Join(", ", removed));
        }

        public static void Write()
        {
            if (!Enabled)
                return;

            try
            {
                string configuredPath = Patcher.ModSettings.Value.ReportPath;
                string path = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath) ? Path.Combine(AppContext.BaseDirectory, "TUS_Report.txt") : configuredPath.Trim());

                StringBuilder report = new();
                report.AppendLine("True Unleveled Skyrim - NPC report, " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                report.AppendLine("NPCs listed: " + entries.Count);
                report.AppendLine();

                report.AppendLine("=== Why NPCs got no spells (counts) ===");
                if (spellSkipReasons.Count == 0)
                    report.AppendLine("(nothing recorded)");

                foreach (var reason in spellSkipReasons.OrderByDescending(x => x.Value))
                    report.AppendLine(reason.Value.ToString().PadLeft(7) + "  " + reason.Key);

                report.AppendLine();
                report.AppendLine("=== NPCs ===");
                foreach (NpcEntry entry in entries.Values.OrderBy(e => e.Header, StringComparer.OrdinalIgnoreCase))
                {
                    report.AppendLine(entry.Header);
                    foreach (string line in entry.Lines)
                        report.AppendLine("    " + line);

                    report.AppendLine();
                }

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, report.ToString());
                Console.WriteLine("Report written to " + path + "\n");
            }
            catch (Exception ex)
            {
                // A report problem must never fail the patch.
                Console.WriteLine("[Warning] Could not write the report: " + ex.Message + "\n");
            }
        }
    }
}
