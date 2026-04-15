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

        return Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
    }

    public static string ResolveRepositoryPath(IConfiguration configuration, IHostEnvironment environment, string configuredPath)
    {
        return Path.GetFullPath(Path.Combine(GetRepositoryRoot(configuration, environment), configuredPath));
    }
}