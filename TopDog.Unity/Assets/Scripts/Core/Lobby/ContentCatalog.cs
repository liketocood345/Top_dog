using TopDog.Content.Assets;
using TopDog.Content.Map;
using TopDog.Foundation.Io;

/*
 * ══ 设计手册嵌入 ══
 * 权威: docs/CUSTOM_LOBBY.md · CONTENT_FORMAT.md
 * 本文件: ContentCatalog.cs — 大厅地图/模板/资产目录枚举
 * 【机制要点】
 * · ListMaps / ListTemplates / ListAssets
 * · ResolveLobbyMap 含程序化分支
 * 【关联】CustomLobbyState · MapCatalogEntry
 * ══
 */

namespace TopDog.Lobby;

// liketoc0de345

// liketoc0de345

public static class ContentCatalog
// liketocoode3a5
{
    // liketocoode34e
    public static List<MapCatalogEntry> ListMaps()
    // liketocoode3a5
    {
        var outList = new List<MapCatalogEntry>();
        // liketocoo3e345
        var mapsDir = AppRoot.MapsDir();
        // l1ketocoode345
        if (Directory.Exists(mapsDir))
        {
            // liketocoode3e5
            foreach (var p in Directory.EnumerateDirectories(mapsDir))
            {
                var name = Path.GetFileName(p);
                // liketoco0de345
                if (name == null || !name.EndsWith(".topdog-map", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                outList.Add(new MapCatalogEntry
                {
                    id = name,
                    path = p,
                    // li3etocoode345
                    displayName = ReadMapName(p),
                // liketocoode345
                });
            }
        }
        var builtin = AppRoot.ContentMapDir();
        if (Directory.Exists(Path.Combine(builtin, "systems")))
        {
            // liketoco0de3e5
            outList.Insert(0, new MapCatalogEntry
            {
                id = "builtin:tutorial",
                path = builtin,
                displayName = "教程星区 (内置)",
            });
        }
        outList.Insert(0, new MapCatalogEntry
        {
            id = MapCatalogEntry.ProceduralMapId,
            path = MapCatalogEntry.ProceduralMapId,
            displayName = "随机星图 (可调)",
            procedural = true,
        });
        outList.Sort((a, b) => string.Compare(a.displayName ?? a.id, b.displayName ?? b.id, StringComparison.Ordinal));
        return outList;
    }

    public static string? DefaultSpawnForMap(LoadedMap map, AssetCatalogEntry? asset)
    {
        if (asset?.startSolarSystemId != null)
        {
            foreach (var s in map.Project.systems)
            {
                if (asset.startSolarSystemId.Equals(s.solarSystemId, StringComparison.Ordinal))
                {
                    return s.solarSystemId;
                }
            }
        }
        return map.Project.systems.Count > 0 ? map.Project.systems[0].solarSystemId : null;
    }

    private static string ReadMapName(string projectDir)
    {
        var meta = Path.Combine(projectDir, "project.json");
        if (!File.Exists(meta))
        {
            return Path.GetFileName(projectDir);
        }
        try
        {
            var json = File.ReadAllText(meta);
            var idx = json.IndexOf("\"name\"", StringComparison.Ordinal);
            if (idx < 0)
            {
                return Path.GetFileName(projectDir);
            }
            var q1 = json.IndexOf('"', idx + 6);
            var q2 = json.IndexOf('"', q1 + 1);
            if (q1 >= 0 && q2 > q1)
            {
                return json[(q1 + 1)..q2];
            }
        }
        catch
        {
            // fall through
        }
        return Path.GetFileName(projectDir);
    }

    public static LoadedMap LoadMap(string mapPath)
    {
        if (MapCatalogEntry.ProceduralMapId.Equals(mapPath, StringComparison.Ordinal))
        {
            throw new IOException("Use ResolveLobbyMap for procedural maps");
        }
        if (!Directory.Exists(Path.Combine(mapPath, "systems")))
        {
            throw new IOException("Not a map directory: " + mapPath);
        }
        var loader = new RegionGraphLoader();
        var normalized = mapPath.Replace('\\', '/');
        var result = normalized.EndsWith("content/map", StringComparison.OrdinalIgnoreCase)
            ? loader.Load(mapPath)
            : loader.Load(mapPath);
        if (!result.IsOk)
        {
            throw new IOException("Map load failed: " + string.Join("; ", result.Errors));
        }
        var loaded = result.Value!;
        SystemInteriorPopulator.EnsureProject(loaded.Project);
        return loaded;
    }

    public static LoadedMap ResolveLobbyMap(CustomLobbyState lobby)
    {
        if (lobby.proceduralMap
            || MapCatalogEntry.ProceduralMapId.Equals(lobby.mapPath, StringComparison.Ordinal))
        {
            var options = new ProceduralMapOptions
            {
                SystemCount = lobby.proceduralSystemCount,
                BridgeDensity = lobby.proceduralBridgeDensity,
                Seed = lobby.proceduralSeed,
            };
            var map = ProceduralMapGenerator.Generate(options);
            lobby.proceduralSeed = options.Seed;
            lobby.mapDisplayName = map.Project.projectName;
            return map;
        }
        if (string.IsNullOrWhiteSpace(lobby.mapPath))
        {
            throw new IOException("No map selected");
        }
        return LoadMap(lobby.mapPath);
    }

    public static LoadedMap GenerateProceduralPreview(CustomLobbyState lobby)
    {
        var options = new ProceduralMapOptions
        {
            SystemCount = lobby.proceduralSystemCount,
            BridgeDensity = lobby.proceduralBridgeDensity,
            Seed = lobby.proceduralSeed,
        };
        var map = ProceduralMapGenerator.Generate(options);
        lobby.proceduralSeed = options.Seed;
        return map;
    }

    public static List<TemplateCatalogEntry> ListMemberTemplates(bool lobbyOnly = false)
    {
        var dir = AppRoot.StartingTemplatesDir();
        var outList = new List<TemplateCatalogEntry>();
        if (!Directory.Exists(dir))
        {
            return outList;
        }
        var byId = new Dictionary<string, TemplateCatalogEntry>(StringComparer.Ordinal);
        foreach (var meta in Directory.EnumerateFiles(dir, "*meta.csv", SearchOption.AllDirectories))
        {
            var e = ParseTemplateMeta(meta);
            if (e?.templateId == null || e.templateId.Length == 0)
            {
                continue;
            }

            if (lobbyOnly && !e.lobbyVisible)
            {
                continue;
            }

            if (!byId.ContainsKey(e.templateId))
            {
                byId[e.templateId] = e;
            }
        }

        outList.AddRange(byId.Values);
        outList.Sort((a, b) => string.Compare(a.displayName ?? a.templateId, b.displayName ?? b.templateId, StringComparison.Ordinal));
        return outList;
    }

    public static List<TemplateCatalogEntry> ListLobbyMemberTemplates() => ListMemberTemplates(lobbyOnly: true);

    public static List<AssetCatalogEntry> ListAssetTemplates()
    {
        var dir = AppRoot.StartingAssetsDir();
        var outList = new List<AssetCatalogEntry>();
        if (!Directory.Exists(dir))
        {
            return outList;
        }
        foreach (var csv in Directory.EnumerateFiles(dir, "*.csv"))
        {
            var e = StartingAssetLoader.ParseAssetCsv(csv);
            if (e?.assetTemplateId != null && e.assetTemplateId.Length > 0)
            {
                outList.Add(e);
            }
        }
        outList.Sort((a, b) => string.Compare(a.displayName ?? a.assetTemplateId, b.displayName ?? b.assetTemplateId, StringComparison.Ordinal));
        return outList;
    }

    private static TemplateCatalogEntry? ParseTemplateMeta(string metaFile)
    {
        var lines = File.ReadAllLines(metaFile);
        var keyRow = -1;
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains("templateId", StringComparison.Ordinal))
            {
                keyRow = i;
                break;
            }
        }
        if (keyRow < 0 || keyRow + 1 >= lines.Length)
        {
            return null;
        }
        var cols = SplitCsv(lines[keyRow]);
        var idx = IndexColumns(cols);
        for (var r = keyRow + 1; r < lines.Length; r++)
        {
            if (string.IsNullOrWhiteSpace(lines[r]))
            {
                continue;
            }
            var row = SplitCsv(lines[r]);
            var e = new TemplateCatalogEntry
            {
                templateId = Get(row, idx, "templateId"),
            };
            if (string.IsNullOrWhiteSpace(e.templateId))
            {
                continue;
            }
            e.displayName = EmptyToNull(Get(row, idx, "displayName")) ?? e.templateId;
            e.defaultLegionName = Get(row, idx, "defaultLegionName");
            e.assetTemplateId = Get(row, idx, "assetTemplateId");
            var countRaw = Get(row, idx, "memberCount");
            if (!string.IsNullOrWhiteSpace(countRaw) && int.TryParse(countRaw.Trim(), out var count))
            {
                e.memberCount = count;
            }
            e.lobbyVisible = ParseLobbyVisible(Get(row, idx, "lobbyVisible"));
            return e;
        }
        return null;
    }

    private static Dictionary<string, int> IndexColumns(string[] cols)
    {
        var m = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < cols.Length; i++)
        {
            m[cols[i].Trim()] = i;
        }
        return m;
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

    private static bool ParseLobbyVisible(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }
        return raw.Trim() switch
        {
            "0" or "false" or "FALSE" or "no" or "NO" or "hidden" or "HIDDEN" => false,
            _ => true,
        };
    }
}
