namespace OpenAiResponses.Api.Helpers;

public static class RepositoryRootResolver
{
    public static string GetRepositoryRoot(IConfiguration configuration, IHostEnvironment environment)
    {
        var configuredRoot = configuration["RepositoryRoot"]
            ?? Environment.GetEnvironmentVariable("REPOSITORY_ROOT");

        if (!string.IsNullOrWhiteSpace(configuredRoot))
        {
            return Path.GetFullPath(configuredRoot);
        }

        var candidateRoots = new[]
        {
            environment.ContentRootPath,
            Path.Combine(environment.ContentRootPath, ".."),
            Path.Combine(environment.ContentRootPath, "..", "..")
        }
        .Select(Path.GetFullPath)
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidateRoot in candidateRoots)
        {
            if (LooksLikeRepositoryRoot(candidateRoot))
            {
                return candidateRoot;
            }
        }

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
    }

    public static string ResolveRepositoryPath(IConfiguration configuration, IHostEnvironment environment, string configuredPath)
    {
        return Path.GetFullPath(Path.Combine(GetRepositoryRoot(configuration, environment), configuredPath));
    }

    public static string ResolveApplyAiAssetPath(IConfiguration configuration, IHostEnvironment environment, params string[] pathSegments)
    {
        var combinedSegments = new string[4 + pathSegments.Length];
        combinedSegments[0] = "Backend.api";
        combinedSegments[1] = "Services";
        combinedSegments[2] = "ApplyAIService";
        combinedSegments[3] = "Assets";

        if (pathSegments.Length > 0)
        {
            Array.Copy(pathSegments, 0, combinedSegments, 4, pathSegments.Length);
        }

        return Path.GetFullPath(Path.Combine(GetRepositoryRoot(configuration, environment), Path.Combine(combinedSegments)));
    }

    private static bool LooksLikeRepositoryRoot(string candidateRoot)
    {
        return Directory.Exists(Path.Combine(candidateRoot, "Backend.api", "Services", "ApplyAIService"))
            || Directory.Exists(Path.Combine(candidateRoot, "TestData"))
            || File.Exists(Path.Combine(candidateRoot, "Backend.sln"));
    }
}