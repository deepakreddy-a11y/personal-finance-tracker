using System.Data.Common;

namespace FinanceTracker.Api;

public sealed class DatabaseSafetyOptions
{
    public const string SectionName = "DatabaseSafety";

    public bool EnforceLocalOnlyInDevelopment { get; init; } = true;
    public string ConnectionStringName { get; init; } = "FinanceDb";
    public string[] AllowedHosts { get; init; } = ["localhost", "127.0.0.1"];
    public string[] BlockedDatabaseNameTerms { get; init; } = ["openlane", "prod", "production", "staging"];
}

public static class DatabaseSafetyGuard
{
    public static void Validate(IConfiguration configuration, IWebHostEnvironment environment, DatabaseSafetyOptions options)
    {
        if (!environment.IsDevelopment() || !options.EnforceLocalOnlyInDevelopment)
        {
            return;
        }

        var connectionString = configuration.GetConnectionString(options.ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var values = ParseConnectionString(connectionString);
        var hostRaw = Find(values, "Host", "Server", "Data Source", "DataSource");
        var databaseName = Find(values, "Database", "Initial Catalog");

        if (string.IsNullOrWhiteSpace(hostRaw))
        {
            throw new InvalidOperationException(
                $"Database safety guard: could not find host in connection string '{options.ConnectionStringName}'.");
        }

        var allowedHosts = options.AllowedHosts.Select(NormalizeHost).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hosts = hostRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeHost)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

        if (hosts.Count == 0)
        {
            throw new InvalidOperationException("Database safety guard: host value is empty.");
        }

        var disallowed = hosts.Where(host => !allowedHosts.Contains(host)).ToList();
        if (disallowed.Count > 0)
        {
            throw new InvalidOperationException(
                $"Database safety guard: blocked non-local DB host(s): {string.Join(", ", disallowed)}. " +
                $"Allowed hosts: {string.Join(", ", allowedHosts)}.");
        }

        if (!string.IsNullOrWhiteSpace(databaseName))
        {
            var lower = databaseName.ToLowerInvariant();
            var blockedTerm = options.BlockedDatabaseNameTerms
                .FirstOrDefault(term => !string.IsNullOrWhiteSpace(term) && lower.Contains(term, StringComparison.Ordinal));
            if (blockedTerm is not null)
            {
                throw new InvalidOperationException(
                    $"Database safety guard: database '{databaseName}' is blocked by term '{blockedTerm}'.");
            }
        }
    }

    private static Dictionary<string, string> ParseConnectionString(string connectionString)
    {
        var builder = new DbConnectionStringBuilder
        {
            ConnectionString = connectionString
        };

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in builder.Keys)
        {
            values[key] = builder[key]?.ToString() ?? string.Empty;
        }

        return values;
    }

    private static string? Find(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string NormalizeHost(string value)
    {
        var host = value.Trim();
        if (host.StartsWith("[", StringComparison.Ordinal))
        {
            var closing = host.IndexOf(']');
            if (closing > 0)
            {
                host = host[1..closing];
            }
        }
        else if (host.Count(c => c == ':') == 1)
        {
            host = host.Split(':', 2, StringSplitOptions.TrimEntries)[0];
        }

        return host.ToLowerInvariant();
    }
}
