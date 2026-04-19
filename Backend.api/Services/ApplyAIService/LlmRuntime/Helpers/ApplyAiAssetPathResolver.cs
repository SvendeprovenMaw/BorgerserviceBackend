namespace Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;

public static class ApplyAiAssetPathResolver
{
    private const string LocalAssetRoot = "Services/ApplyAIService/Assets";
    private const string LegacySchemaRoot = "AI Schemas/";
    private const string LocalSchemaRoot = "Schemas/";

    public static string ResolveCatalogPath(IConfiguration configuration, IHostEnvironment environment, string catalogPath)
    {
        var normalizedCatalogPath = NormalizeCatalogPath(catalogPath);
        var localRelativePath = MapCatalogPathToLocalRelative(normalizedCatalogPath);
        var localPath = Path.GetFullPath(Path.Combine(environment.ContentRootPath, LocalAssetRoot, localRelativePath));
        if (File.Exists(localPath))
        {
            return localPath;
        }

        throw new FileNotFoundException(
            $"ApplyAI asset was not found in the local service assets: {catalogPath}",
            localPath);
    }

    public static string ToLocalAssetReference(string? catalogPath)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            return string.Empty;
        }

        var normalizedCatalogPath = NormalizeCatalogPath(catalogPath);
        var localRelativePath = MapCatalogPathToLocalRelative(normalizedCatalogPath).Replace('\\', '/');
        return $"{LocalAssetRoot}/{localRelativePath}";
    }

    private static string NormalizeCatalogPath(string catalogPath)
    {
        return catalogPath.Replace('\\', '/').Trim().TrimStart('/');
    }

    private static string MapCatalogPathToLocalRelative(string normalizedCatalogPath)
    {
        if (normalizedCatalogPath.StartsWith($"{LocalAssetRoot}/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCatalogPath[(LocalAssetRoot.Length + 1)..];
        }

        if (normalizedCatalogPath.StartsWith(LegacySchemaRoot, StringComparison.OrdinalIgnoreCase))
        {
            return $"{LocalSchemaRoot}{normalizedCatalogPath[LegacySchemaRoot.Length..]}";
        }

        if (normalizedCatalogPath.StartsWith(LocalSchemaRoot, StringComparison.OrdinalIgnoreCase)
            || normalizedCatalogPath.StartsWith("Prompts/", StringComparison.OrdinalIgnoreCase)
            || normalizedCatalogPath.StartsWith("Templates/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedCatalogPath;
        }

        throw new InvalidOperationException($"Unsupported ApplyAI asset path: {normalizedCatalogPath}");
    }
}