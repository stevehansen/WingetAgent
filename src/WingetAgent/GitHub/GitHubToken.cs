using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.UserSecrets;

namespace WingetAgent.GitHub;

/// <summary>
/// Resolves and stores the optional GitHub token used to lift the winget-pkgs
/// enrichment rate limit. Preference order: per-user .NET user-secrets (stored under
/// the user profile, outside the repo and the process environment) over the
/// GITHUB_TOKEN environment variable. The token is never logged or written to any run
/// artifact. The recommended token is least-privilege: a classic token with NO scopes
/// is still authenticated for public reads (5000 req/hr) and can touch nothing else.
/// </summary>
public static class GitHubToken
{
    public const string SecretKey = "GitHub:Token";
    public const string EnvVar = "GITHUB_TOKEN";

    /// <summary>GitHub "new classic token" page, pre-filled with no scopes (public read only).</summary>
    public const string CreationUrl =
        "https://github.com/settings/tokens/new?description=WingetAgent%20(public%20read%2C%20no%20scopes)&scopes=";

    public static (string? token, string source) Resolve()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets(typeof(GitHubToken).Assembly, optional: true)
            .Build();

        var fromSecrets = config[SecretKey];
        if (!string.IsNullOrWhiteSpace(fromSecrets)) return (fromSecrets.Trim(), "user-secrets");

        var fromEnv = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return (fromEnv.Trim(), "GITHUB_TOKEN env");

        return (null, "none");
    }

    static string? SecretsId =>
        Assembly.GetEntryAssembly()?.GetCustomAttribute<UserSecretsIdAttribute>()?.UserSecretsId;

    public static string? SecretsPath
    {
        get
        {
            var id = SecretsId;
            if (string.IsNullOrEmpty(id)) return null;
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "Microsoft", "UserSecrets", id, "secrets.json");
        }
    }

    /// <summary>Writes the token into the per-user secrets store, preserving any other keys. Returns the file path.</summary>
    public static string Save(string token)
    {
        var path = SecretsPath ?? throw new InvalidOperationException("UserSecretsId is not configured.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) as JsonObject ?? new JsonObject()
            : new JsonObject();
        root[SecretKey] = token.Trim();

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }
}
