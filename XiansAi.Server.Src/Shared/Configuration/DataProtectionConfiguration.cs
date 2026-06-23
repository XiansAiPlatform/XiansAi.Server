using Microsoft.AspNetCore.DataProtection;

namespace Features.Shared.Configuration;

/// <summary>
/// Configuration settings for ASP.NET Core Data Protection.
/// Data Protection keys are used for antiforgery tokens, OAuth correlation/state during
/// login handshakes, and any <c>IDataProtector</c> usage. In containerized deployments these
/// keys must be persisted outside the (ephemeral) container filesystem so they survive
/// restarts/redeploys and can be shared across replicas.
/// </summary>
public class DataProtectionSettings
{
    /// <summary>
    /// Directory where Data Protection keys are persisted. In containers this should point to a
    /// mounted volume (shared across replicas) so keys are not lost on restart.
    /// When empty, persistence is only configured outside Development, defaulting to
    /// <see cref="DefaultKeysDirectory"/>.
    /// </summary>
    public string? KeysDirectory { get; set; }

    /// <summary>
    /// Stable application name used to isolate keys. Must remain constant across deployments
    /// for previously protected payloads to remain valid.
    /// </summary>
    public string ApplicationName { get; set; } = "XiansAi.Server";

    /// <summary>
    /// Default directory used in non-Development environments when no directory is configured.
    /// Matches the working directory used in the production Dockerfile.
    /// </summary>
    public const string DefaultKeysDirectory = "/app/keys";
}

/// <summary>
/// Extension methods for configuring ASP.NET Core Data Protection key persistence.
/// </summary>
public static class DataProtectionConfiguration
{
    /// <summary>
    /// Configures Data Protection to persist keys to a stable, durable location so that keys
    /// survive container restarts and can be shared across replicas.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder instance.</param>
    /// <returns>The WebApplicationBuilder for method chaining.</returns>
    public static WebApplicationBuilder AddDataProtectionConfiguration(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration
            .GetSection("DataProtection")
            .Get<DataProtectionSettings>() ?? new DataProtectionSettings();

        using var loggerFactory = LoggerFactory.Create(logBuilder => logBuilder.AddConsole());
        var logger = loggerFactory.CreateLogger(typeof(DataProtectionConfiguration));

        // Resolve the directory to persist keys to. In Development we keep the framework default
        // (local per-user storage) unless a directory is explicitly configured, so local runs
        // don't need a writable /app/keys path.
        var keysDirectory = ResolveKeysDirectory(settings, builder.Environment.IsDevelopment());

        var dataProtectionBuilder = builder.Services
            .AddDataProtection()
            .SetApplicationName(settings.ApplicationName);

        if (string.IsNullOrWhiteSpace(keysDirectory))
        {
            logger.LogInformation(
                "Data Protection persistence not configured (Environment: {Environment}). " +
                "Using default key storage. Set DataProtection:KeysDirectory to persist keys.",
                builder.Environment.EnvironmentName);
            return builder;
        }

        if (!TryEnsureDirectory(keysDirectory, logger))
        {
            // Directory could not be created/accessed. Fall back to default storage rather than
            // failing startup, but warn loudly because keys will not survive restarts.
            logger.LogWarning(
                "Data Protection keys directory '{KeysDirectory}' is not usable. Falling back to " +
                "default key storage. Keys may be lost on restart.", keysDirectory);
            return builder;
        }

        dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));

        logger.LogInformation(
            "Data Protection keys will be persisted to '{KeysDirectory}' for application '{ApplicationName}'.",
            keysDirectory, settings.ApplicationName);

        return builder;
    }

    /// <summary>
    /// Determines the directory to persist keys to based on configuration and environment.
    /// </summary>
    private static string? ResolveKeysDirectory(DataProtectionSettings settings, bool isDevelopment)
    {
        if (!string.IsNullOrWhiteSpace(settings.KeysDirectory))
        {
            return settings.KeysDirectory.Trim();
        }

        // No explicit directory: only enforce persistence outside Development.
        return isDevelopment ? null : DataProtectionSettings.DefaultKeysDirectory;
    }

    /// <summary>
    /// Ensures the target directory exists and is writable. Returns false on any failure.
    /// </summary>
    private static bool TryEnsureDirectory(string directory, ILogger logger)
    {
        try
        {
            Directory.CreateDirectory(directory);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to create Data Protection keys directory '{KeysDirectory}'.", directory);
            return false;
        }
    }
}
