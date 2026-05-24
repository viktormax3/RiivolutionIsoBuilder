using System.Text.Json;
using System.Text.Json.Serialization;

namespace RiivolutionIsoBuilder;

public sealed class ModCatalog
{
    private readonly CatalogDefinition catalog;

    private ModCatalog(CatalogDefinition catalog)
    {
        this.catalog = catalog;
    }

    public static ModCatalog Load(string catalogFile)
    {
        if (!File.Exists(catalogFile))
        {
            throw new FileNotFoundException("No se encontro el catalogo de mods.", catalogFile);
        }

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        options.Converters.Add(new JsonStringEnumConverter());

        var catalog = JsonSerializer.Deserialize<CatalogDefinition>(File.ReadAllText(catalogFile), options)
            ?? throw new InvalidOperationException("El catalogo de mods esta vacio o es invalido.");

        Validate(catalog);
        return new ModCatalog(catalog);
    }

    public GameDefinition? GetGame(string gameId)
    {
        return catalog.Games.FirstOrDefault(game =>
            game.GameIds.Contains(gameId, StringComparer.OrdinalIgnoreCase));
    }

    public GameDefinition CreateGame(string gameId, string title)
    {
        return GetGame(gameId) ?? new GameDefinition
        {
            Key = gameId,
            DisplayName = string.IsNullOrWhiteSpace(title) ? gameId : title,
            GameIds = [gameId],
            RequiredFreeSpaceGb = 8
        };
    }

    public RegionDefinition GetRegion(string id)
    {
        var regionCharacter = id.Length >= 4 ? id[3].ToString() : "";
        return catalog.Regions.FirstOrDefault(region =>
            region.IdCharacter.Equals(regionCharacter, StringComparison.OrdinalIgnoreCase))
            ?? new RegionDefinition { Code = "UNK", Name = "Unknown", IdCharacter = regionCharacter };
    }

    public RegionDefinition GetRegionByNameOrId(string regionName, string id)
    {
        var region = catalog.Regions.FirstOrDefault(region =>
            region.Name.Equals(regionName, StringComparison.OrdinalIgnoreCase)
            || region.Code.Equals(regionName, StringComparison.OrdinalIgnoreCase));
        return region ?? GetRegion(id);
    }

    public IReadOnlyList<ModDefinition> GetModsForGame(string gameKey)
    {
        return catalog.Mods
            .Where(mod => mod.GameKey.Equals(gameKey, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public IEnumerable<string> GameIds()
    {
        return catalog.Games.SelectMany(game => game.GameIds).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static void Validate(CatalogDefinition catalog)
    {
        if (catalog.Games.Count == 0)
        {
            throw new InvalidOperationException("El catalogo debe declarar al menos un juego.");
        }

        if (catalog.Regions.Count == 0)
        {
            throw new InvalidOperationException("El catalogo debe declarar al menos una region.");
        }

        var gameKeys = catalog.Games.Select(game => game.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in catalog.Mods)
        {
            if (!gameKeys.Contains(mod.GameKey))
            {
                throw new InvalidOperationException($"El mod {mod.Id} apunta a un juego inexistente: {mod.GameKey}.");
            }
        }
    }
}

