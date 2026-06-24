using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using CanfarDesktop.Models.Export;

namespace CanfarDesktop.Services.Export;

/// <summary>Thrown when a bundle export cannot be completed; the partial bundle is removed first.</summary>
public class ExportException : Exception
{
    public ExportException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>
/// Assembles one or more <see cref="IExportableModule"/>s into a timestamped, Claude-friendly bundle
/// (manifest.json + README.md at the root, one subdirectory per module), zips it, and optionally
/// uploads it to VOSpace. All-or-nothing: a partial/failed bundle is deleted rather than shipped.
/// 1-to-1 with the macOS ExportService.
/// </summary>
public class ExportService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Build a bundle under <paramref name="destinationDir"/>. Returns the bundle directory path.
    /// <paramref name="now"/>/<paramref name="appVersion"/>/<paramref name="hostName"/> are injected
    /// so the assembly is deterministic + unit-testable.
    /// </summary>
    public async Task<string> BuildBundleAsync(
        string destinationDir,
        IReadOnlyList<IExportableModule> modules,
        ExportOptions options,
        DateTimeOffset now,
        string appVersion,
        string hostName)
    {
        var bundleDir = Path.Combine(destinationDir, BundleName(now));
        Directory.CreateDirectory(bundleDir);

        try
        {
            var manifestModules = new List<ExportManifestModule>();
            var copyFailures = new List<string>();

            foreach (var module in modules)
            {
                var output = await module.ExportAsync(options);
                var moduleDir = Path.Combine(bundleDir, module.ModuleId);
                Directory.CreateDirectory(moduleDir);

                var files = new List<string>();

                foreach (var (filename, json) in output.JsonFiles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    await File.WriteAllTextAsync(Path.Combine(moduleDir, filename), json);
                    files.Add($"{module.ModuleId}/{filename}");
                }

                foreach (var (filename, md) in output.MarkdownFiles.OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    await File.WriteAllTextAsync(Path.Combine(moduleDir, filename), md);
                    files.Add($"{module.ModuleId}/{filename}");
                }

                if (options.IncludeFileCopies && output.AttachedFiles.Count > 0)
                {
                    var filesDir = Path.Combine(moduleDir, "files");
                    Directory.CreateDirectory(filesDir);
                    var copied = 0;
                    foreach (var src in output.AttachedFiles)
                    {
                        try
                        {
                            File.Copy(src, Path.Combine(filesDir, Path.GetFileName(src)), overwrite: true);
                            copied++;
                        }
                        catch
                        {
                            copyFailures.Add(Path.GetFileName(src));
                        }
                    }
                    if (copied > 0) files.Add($"{module.ModuleId}/files/");
                }

                files.Sort(StringComparer.Ordinal);
                manifestModules.Add(new ExportManifestModule
                {
                    Id = module.ModuleId,
                    DisplayName = module.DisplayName,
                    Files = files,
                    ItemCounts = output.ItemCounts,
                });
            }

            // A file-copy export that silently dropped files is a false success — fail loudly and
            // discard the partial bundle so what ships is always trustworthy.
            if (copyFailures.Count > 0)
            {
                TryDelete(bundleDir);
                var names = string.Join(", ", copyFailures.Take(5));
                var suffix = copyFailures.Count > 5 ? ", …" : string.Empty;
                throw new ExportException(
                    $"Export incomplete: {copyFailures.Count} attached file(s) could not be copied ({names}{suffix}). Re-export, or disable file copies.");
            }

            var allFiles = manifestModules.SelectMany(m => m.Files).ToList();
            var manifest = new ExportManifest
            {
                ExportVersion = "1.0",
                AppName = "Verbinal",
                AppVersion = appVersion,
                ExportedAt = now,
                HostName = hostName,
                Modules = manifestModules,
                ClaudeHints = new ExportClaudeHints
                {
                    PrimaryContext = allFiles.FirstOrDefault(f => f.EndsWith(".md", StringComparison.Ordinal)),
                    MetadataSchema = allFiles.FirstOrDefault(f => f.EndsWith(".json", StringComparison.Ordinal)),
                    ReadMeFirst = "README.md",
                },
            };

            await File.WriteAllTextAsync(
                Path.Combine(bundleDir, "manifest.json"),
                JsonSerializer.Serialize(manifest, ManifestJsonOptions));
            await File.WriteAllTextAsync(
                Path.Combine(bundleDir, "README.md"),
                RenderReadme(manifest));

            return bundleDir;
        }
        catch (ExportException)
        {
            throw;
        }
        catch (Exception ex)
        {
            TryDelete(bundleDir);
            throw new ExportException($"Export failed: {ex.Message}", ex);
        }
    }

    /// <summary>Zip a bundle folder (including the top folder) to a sibling .zip; returns the zip path.</summary>
    public string ZipBundle(string bundleDir)
    {
        var zipPath = bundleDir + ".zip";
        if (File.Exists(zipPath)) File.Delete(zipPath);
        ZipFile.CreateFromDirectory(bundleDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
        return zipPath;
    }

    /// <summary>Upload a zipped bundle to <c>{username}/Verbinal-Exports/&lt;name&gt;.zip</c> on VOSpace.</summary>
    public async Task<string> UploadBundleToVoSpaceAsync(string zipPath, IStorageService storage, string username)
    {
        try { await storage.CreateFolderAsync(username, "Verbinal-Exports"); }
        catch { /* 409 (already exists) is expected/idempotent */ }

        var remotePath = $"{username}/Verbinal-Exports/{Path.GetFileName(zipPath)}";
        await using var stream = File.OpenRead(zipPath);
        await storage.UploadFileAsync(remotePath, stream, "application/zip");
        return remotePath;
    }

    private static string BundleName(DateTimeOffset now)
        => $"Verbinal-Export-{now:yyyy-MM-dd_HHmmss}";

    private static void TryDelete(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); }
        catch { /* best effort */ }
    }

    private static string RenderReadme(ExportManifest m)
    {
        var date = m.ExportedAt.ToString("dddd, dd MMMM yyyy HH:mm", CultureInfo.InvariantCulture);
        var sb = new System.Text.StringBuilder();
        sb.Append($"# Verbinal Export — {date}\n\n");
        sb.Append($"This bundle was exported from Verbinal v{m.AppVersion} on `{m.HostName}`.\n");
        sb.Append("It is structured for consumption by Claude, other LLMs, and human collaborators.\n\n");

        sb.Append("## Contents\n\n");
        foreach (var module in m.Modules)
        {
            var counts = string.Join(", ", module.ItemCounts.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .Select(kv => $"{kv.Value} {kv.Key}"));
            sb.Append($"- **{module.DisplayName}** (`{module.Id}/`) — {(counts.Length == 0 ? "no items" : counts)}\n");
        }
        sb.Append('\n');

        sb.Append("## For Claude / LLM ingestion\n\n");
        if (m.ClaudeHints.PrimaryContext is { } primary)
        {
            sb.Append("1. Start with `manifest.json` to understand the bundle shape.\n");
            sb.Append($"2. Read `{primary}` for human-readable per-item content.\n");
            if (m.ClaudeHints.MetadataSchema is { } schema)
                sb.Append($"3. Cross-reference with `{schema}` for full metadata.\n\n");
            else
                sb.Append('\n');
        }

        sb.Append("### Suggested prompts\n\n");
        sb.Append("- *\"Summarize the data in this export, grouped by module.\"*\n");
        sb.Append("- *\"Which items stand out as needing further investigation?\"*\n");
        sb.Append("- *\"List everything tagged `calibration` across all modules.\"*\n\n");

        sb.Append("## Privacy note\n\n");
        sb.Append("This bundle excludes all authentication tokens, Keychain entries, session state, ");
        sb.Append("and cached credentials. Only user-authored data and public CADC metadata are exported.\n");

        return sb.ToString();
    }
}
