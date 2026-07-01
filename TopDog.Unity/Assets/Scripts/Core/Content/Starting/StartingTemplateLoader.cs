using TopDog.Content.Ships;
using TopDog.Content.Traits;
using TopDog.Foundation.Io;
using TopDog.Lobby;
using TopDog.Sim.Economy;
using TopDog.Sim.Legion;
using TopDog.Sim.Member;
using TopDog.Sim.State;

/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/STARTING_TEMPLATES.md · CONTENT_FORMAT.md
 * 本文件: StartingTemplateLoader.cs — 开局团员 CSV 加载
 * 【机制要点】
 * · LoadMembers / ReadMemberCsvLines
 * · ApplyToState 写入 state.members
 * 【关联】TemplateCatalogEntry · CampaignBootstrap
 * ══
 */

namespace TopDog.Content.Starting;

// liketoc0de345

// liketoc0de345

public static class StartingTemplateLoader
// liketocoode3a5
{
    // liketocoode34e
    private static TraitCatalog? _traits;

// liketocoo3e345

    // liketocoode3a5
    // l1ketocoode345
    private static TraitCatalog Traits => _traits ??= TraitCatalog.LoadDefault();

// liketocoode3e5

    public static List<MemberState> LoadMembers(string? templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        // liketoco0de345
        {
            return new List<MemberState>();
        }
        var lines = ReadMemberCsvLines(templateId);
        // li3etocoode345
        if (lines.Count == 0)
        {
            return new List<MemberState>();
        }
        var keyRow = FindHeaderRow(lines, "identityCode");
        if (keyRow < 0 || keyRow + 1 >= lines.Count)
        // liketocoode345
        {
            return new List<MemberState>();
        // liketoco0de3e5
        }
        var cols = SplitCsv(lines[keyRow]);
        var idx = IndexColumns(cols);
        var outList = new List<MemberState>();
        for (var r = keyRow + 1; r < lines.Count; r++)
        {
            var line = lines[r].Trim();
            if (line.Length == 0)
            {
                continue;
            }
            var m = ParseRow(SplitCsv(line), idx);
            if (m != null)
            {
                outList.Add(m);
            }
        }
        return outList;
    }

    public static Dictionary<string, IdentityState> LoadIdentities(string? templateId)
    {
        var result = new Dictionary<string, IdentityState>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return result;
        }
        var lines = ReadIdentityCsvLines(templateId);
        if (lines.Count == 0)
        {
            return result;
        }
        var keyRow = FindHeaderRow(lines, "identityCode");
        if (keyRow < 0 || keyRow + 1 >= lines.Count)
        {
            return result;
        }
        var idx = IndexColumns(SplitCsv(lines[keyRow]));
        for (var r = keyRow + 1; r < lines.Count; r++)
        {
            var line = lines[r].Trim();
            if (line.Length == 0)
            {
                continue;
            }
            var row = SplitCsv(line);
            var code = Get(row, idx, "identityCode");
            if (string.IsNullOrWhiteSpace(code))
            {
                continue;
            }
            result[code.Trim()] = new IdentityState
            {
                identityCode = code.Trim(),
                energy = ParseInt(Get(row, idx, "energy"), 2),
                wisdom = ParseInt(Get(row, idx, "wisdom"), 2),
                legionBelonging = ParseInt(Get(row, idx, "legionBelonging"), 3),
            };
        }
        return result;
    }

    public static void ApplyToState(GameState state)
    {
        if (state.worldline.customMatch?.slots.Count > 0)
        {
            ApplyCustomMatchSlots(state);
            return;
        }
        var templateId = state.worldline.startingTemplateId;
        if (string.IsNullOrWhiteSpace(templateId))
        {
            return;
        }
        if (LobbyCatalogConstants.IsRandomMember(templateId))
        {
            LobbyRandomBootstrap.SpawnRandomMemberRoster(
                state, isPlayer: true, isAi: false, state.currentSolarSystemId);
            IdentityMigrationService.EnsureFromMembers(state);
            StarCoinService.SyncAllMemberFunds(state);
            IdentityAllocator.EnsureCounter(state);
            return;
        }
        MergeTemplateIntoState(state, templateId, state.currentSolarSystemId, isPlayer: true, isAi: false);
    }

    private static void ApplyCustomMatchSlots(GameState state)
    {
        var localPlayerId = state.flags.GetValueOrDefault("lobby.localPlayerId");
        var anyRandom = false;
        foreach (var slot in state.worldline.customMatch!.slots)
        {
            var templateId = slot.memberTemplateId;
            if (string.IsNullOrWhiteSpace(templateId))
            {
                continue;
            }
            if (LobbyCatalogConstants.IsRandomMember(templateId))
            {
                anyRandom = true;
                var isLocalRandom = slot.local
                    || (!string.IsNullOrWhiteSpace(localPlayerId)
                        && localPlayerId.Equals(slot.playerId, StringComparison.Ordinal));
                var isAiRandom = slot.kind == LobbyPlayerKind.AI;
                var spawnRandom = slot.spawnSolarSystemId ?? state.currentSolarSystemId;
                var legion = LegionRegistry.FindForSlot(state, slot);
                LobbyRandomBootstrap.SpawnRandomMemberRoster(
                    state, isLocalRandom, isAiRandom, spawnRandom, legionId: legion?.legionId);
                continue;
            }
            var isLocal = slot.local
                || (!string.IsNullOrWhiteSpace(localPlayerId)
                    && localPlayerId.Equals(slot.playerId, StringComparison.Ordinal));
            var isAi = slot.kind == LobbyPlayerKind.AI;
            var spawn = slot.spawnSolarSystemId ?? state.currentSolarSystemId;
            var slotLegion = LegionRegistry.FindForSlot(state, slot);
            MergeTemplateIntoState(state, templateId, spawn, isLocal, isAi, slotLegion?.legionId);
        }
        if (anyRandom)
        {
            state.flags["lobby.randomMembers"] = "1";
        }
        IdentityMigrationService.EnsureFromMembers(state);
        StarCoinService.SyncAllMemberFunds(state);
        IdentityAllocator.EnsureCounter(state);
    }

    private static void MergeTemplateIntoState(
        GameState state,
        string templateId,
        string? spawnSystemId,
        bool isPlayer,
        bool isAi,
        string? legionId = null)
    {
        var loaded = LoadMembers(templateId);
        if (loaded.Count == 0)
        {
            return;
        }
        foreach (var kv in LoadIdentities(templateId))
        {
            state.identities.TryAdd(kv.Key, kv.Value);
        }
        foreach (var m in loaded)
        {
            if (spawnSystemId != null)
            {
                m.currentSolarSystemId = spawnSystemId;
            }
            m.assignedTask ??= "待命";
            m.isPlayer = isPlayer;
            m.isAi = isAi;
            m.memberId = IdentityAllocator.AllocateMemberId(state, m.identityCode ?? "");
            m.accountSuffix = m.memberId.Length >= 2 ? m.memberId[^2..] : m.accountSuffix;
            m.homeLegionId = legionId;
            var resolvedLegion = legionId ?? LegionRegistry.Local(state)?.legionId ?? "";
            if (!string.IsNullOrWhiteSpace(resolvedLegion))
            {
                LegionPlayerRegistry.EnsureFromLegions(state);
                LegionPlayerRegistry.AddMemberToLegion(state, resolvedLegion, m);
            }
            else
            {
                state.members.Add(m);
            }
        }
        if (state.worldline.customMatch == null)
        {
            IdentityMigrationService.EnsureFromMembers(state);
            StarCoinService.SyncAllMemberFunds(state);
            IdentityAllocator.EnsureCounter(state);
        }
    }

    private static List<string> ReadIdentityCsvLines(string templateId)
    {
        var csv = ResolveTemplateCsvPath(templateId, ".identities.csv");
        return csv != null ? File.ReadAllLines(csv).ToList() : new List<string>();
    }

    private static List<string> ReadMemberCsvLines(string templateId)
    {
        var csv = ResolveTemplateCsvPath(templateId, ".members.csv");
        return csv != null ? File.ReadAllLines(csv).ToList() : new List<string>();
    }

    private static string? ResolveTemplateCsvPath(string templateId, string suffix)
    {
        var dir = AppRoot.StartingTemplatesDir();
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var fileName = templateId + suffix;
        string? best = null;
        foreach (var path in Directory.EnumerateFiles(dir, fileName, SearchOption.AllDirectories))
        {
            if (best == null)
            {
                best = path;
                continue;
            }

            if (PreferTemplateCsvPath(path, best))
            {
                best = path;
            }
        }

        return best;
    }

    /// <summary>嵌套 starting_templates/ 下的副本优先于根目录同名文件（与 meta 目录一致）。</summary>
    private static bool PreferTemplateCsvPath(string candidate, string current)
    {
        static int NestedScore(string p) =>
            p.Contains($"{Path.DirectorySeparatorChar}starting_templates{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

        var nestedDiff = NestedScore(candidate) - NestedScore(current);
        if (nestedDiff != 0)
        {
            return nestedDiff > 0;
        }

        try
        {
            return new FileInfo(candidate).Length > new FileInfo(current).Length;
        }
        catch
        {
            return false;
        }
    }

    private static MemberState? ParseRow(string[] row, Dictionary<string, int> idx)
    {
        var identity = Get(row, idx, "identityCode");
        var suffix = Get(row, idx, "accountSuffix");
        var name = Get(row, idx, "name");
        if (string.IsNullOrWhiteSpace(suffix) || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(identity))
        {
            return null;
        }
        var m = new MemberState
        {
            identityCode = identity.Trim(),
            accountSuffix = suffix.Trim(),
            memberId = identity.Trim() + suffix.Trim(),
            accountName = EmptyToNull(Get(row, idx, "accountName")),
            name = name.Trim(),
            source = "preset",
        };
        var rarity = Get(row, idx, "rarity");
        if (!string.IsNullOrWhiteSpace(rarity))
        {
            m.rarity = rarity.Trim().ToUpperInvariant();
            m.trueRarity = m.rarity;
            m.appraised = true;
        }
        var legionRaw = Get(row, idx, "legionBelonging");
        var fundsRaw = Get(row, idx, "funds");
        var energyRaw = Get(row, idx, "energy");
        var wisdomRaw = Get(row, idx, "wisdom");
        var buildRaw = Get(row, idx, "accountBuildScore");
        var statsFromRarity = string.IsNullOrWhiteSpace(legionRaw)
            && string.IsNullOrWhiteSpace(fundsRaw)
            && string.IsNullOrWhiteSpace(energyRaw)
            && string.IsNullOrWhiteSpace(wisdomRaw)
            && string.IsNullOrWhiteSpace(buildRaw);
        if (statsFromRarity && !string.IsNullOrWhiteSpace(m.rarity))
        {
            MemberStatGenerator.ApplyPresetTierMidpoints(m);
        }
        else
        {
            m.legionBelonging = ParseInt(legionRaw, -1);
            m.funds = ParseInt(fundsRaw, 0);
            m.energy = ParseInt(energyRaw, -1);
            m.wisdom = ParseInt(wisdomRaw, -1);
            if (m.legionBelonging < 0)
            {
                m.legionBelonging = 3;
            }
            if (m.energy < 0)
            {
                m.energy = 2;
            }
            if (m.wisdom < 0)
            {
                m.wisdom = 2;
            }
            m.accountBuildScore = ParseInt(buildRaw, 0);
        }
        ParseTonnageSpec(Get(row, idx, "tonnageSpecialties"), m);
        ParseTraits(Get(row, idx, "traitIds"), m);
        var multibox = Get(row, idx, "multiboxGroupId");
        if (!string.IsNullOrWhiteSpace(multibox))
        {
            m.multiboxGroupId = multibox.Trim();
        }
        else if (m.traitIds.Contains("trait_multibox"))
        {
            m.multiboxGroupId = "mb_" + m.identityCode;
        }
        var backdrop = Get(row, idx, "cardBackdrop");
        if (!string.IsNullOrWhiteSpace(backdrop))
        {
            m.cardBackdrop = backdrop.Trim();
        }
        m.bio = EmptyToNull(Get(row, idx, "bio"));
        ParseLabels(Get(row, idx, "labels"), m);
        var portrait = Get(row, idx, "portraitRef");
        m.portraitRef = !string.IsNullOrWhiteSpace(portrait) ? portrait.Trim() : m.identityCode;
        return m;
    }

    private static void ParseTonnageSpec(string? raw, MemberState m)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }
        foreach (var token in raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var level = 1;
            var namePart = token;
            var i = token.Length - 1;
            while (i >= 0 && char.IsDigit(token[i]))
            {
                i--;
            }
            if (i < token.Length - 1)
            {
                namePart = token[..(i + 1)];
                level = int.Parse(token[(i + 1)..]);
            }
            if (TonnageClassCatalog.TryResolve(namePart, out var tonnageClass))
            {
                m.tonnageSpec[tonnageClass] = Math.Max(m.tonnageSpec.GetValueOrDefault(tonnageClass, 0), level);
            }
        }
    }

    private static void ParseLabels(string? raw, MemberState m)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }
        foreach (var token in raw.Split('|', StringSplitOptions.RemoveEmptyEntries))
        {
            var label = token.Trim();
            if (label.Length > 0 && !m.labels.Contains(label))
            {
                m.labels.Add(label);
            }
        }
    }

    private static void ParseTraits(string? raw, MemberState m)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }
        foreach (var token in raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var traitId = Traits.ResolveTraitId(token);
            if (traitId != null && !m.traitIds.Contains(traitId))
            {
                m.traitIds.Add(traitId);
            }
        }
    }

    private static int FindHeaderRow(List<string> lines, string key)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (NormalizeColumn(lines[i]).Contains(key, StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    private static Dictionary<string, int> IndexColumns(string[] cols)
    {
        var m = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < cols.Length; i++)
        {
            m[NormalizeColumn(cols[i])] = i;
        }
        return m;
    }

    private static string NormalizeColumn(string? col)
    {
        if (col == null)
        {
            return "";
        }
        var t = col.Trim();
        if (t.Length > 0 && t[0] == '\uFEFF')
        {
            t = t[1..].Trim();
        }
        return t;
    }

    private static string? Get(string[] row, Dictionary<string, int> idx, string key)
    {
        if (!idx.TryGetValue(key, out var i) || i >= row.Length)
        {
            return null;
        }
        return row[i].Trim();
    }

    private static string[] SplitCsv(string line) => line.Split(',', StringSplitOptions.None);

    private static string? EmptyToNull(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static int ParseInt(string? s, int defaultValue)
    {
        if (string.IsNullOrWhiteSpace(s))
        {
            return defaultValue;
        }
        return int.TryParse(s.Trim(), out var v) ? v : defaultValue;
    }
}
