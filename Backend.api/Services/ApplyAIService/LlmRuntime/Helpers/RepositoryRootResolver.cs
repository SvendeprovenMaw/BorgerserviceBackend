namespace Backend.api.Services.ApplyAIService.LlmRuntime.Helpers;

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

    private static bool LooksLikeRepositoryRoot(string candidateRoot)
    {
        return Directory.Exists(Path.Combine(candidateRoot, "LLM"))
            || Directory.Exists(Path.Combine(candidateRoot, "TestData"))
            || File.Exists(Path.Combine(candidateRoot, "Backend.sln"));
    }
}