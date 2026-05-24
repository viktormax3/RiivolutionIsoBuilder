namespace RiivolutionIsoBuilder;

public static class OutputIdSuggester
{
    public static string ForCatalogMod(ModDefinition mod, GameImage game)
    {
        return $"{mod.OutputIdPrefix ?? mod.Id}{RegionMakerSuffix(game)}".ToUpperInvariant();
    }

    public static string ForNativeRiivolutionMod(NativeRiivolutionMod mod, GameImage game)
    {
        var patchId = mod.Plan.ActivePatches.FirstOrDefault()?.Id ?? Path.GetFileNameWithoutExtension(mod.XmlFile);
        var prefix = CreatePrefix(patchId, 'X');
        return $"{prefix}{RegionMakerSuffix(game)}";
    }

    public static string ForManualPatch(string name, GameImage game)
    {
        var prefix = CreatePrefix(name, 'G');
        return $"{prefix}{RegionMakerSuffix(game)}";
    }

    public static string Normalize(string outputId)
    {
        outputId = new string(outputId.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (outputId.Length != 6)
        {
            throw new InvalidOperationException("El ID6 de salida debe tener exactamente 6 caracteres alfanumericos.");
        }

        return outputId;
    }

    private static string RegionMakerSuffix(GameImage game)
    {
        return game.GameId.Length >= 6 ? game.GameId[3..6] : game.GameId.PadRight(6, 'X')[3..6];
    }

    private static string CreatePrefix(string value, char padding)
    {
        return new string(value.Where(char.IsLetterOrDigit).Take(3).ToArray()).ToUpperInvariant().PadRight(3, padding);
    }
}
