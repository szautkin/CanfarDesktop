using System.IO.Compression;
using System.Text.Json;
using Xunit;
using CanfarDesktop.Models;
using CanfarDesktop.Models.Export;
using CanfarDesktop.Services;
using CanfarDesktop.Services.Export;

namespace CanfarDesktop.Tests.Services;

public class ExportServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private sealed class FakeModule : IExportableModule
    {
        public string ModuleId { get; init; } = "research";
        public string DisplayName { get; init; } = "Research";
        public ExportModuleOutput Output { get; } = new();
        public Task<ExportModuleOutput> ExportAsync(ExportOptions options) => Task.FromResult(Output);
    }

    private sealed class FakeStorage : IStorageService
    {
        public List<(string Remote, string? ContentType)> Uploads { get; } = new();
        public List<(string Path, string Folder)> Folders { get; } = new();

        public Task UploadFileAsync(string remotePath, Stream content, string? contentType = null, CancellationToken cancellationToken = default)
        {
            Uploads.Add((remotePath, contentType));
            return Task.CompletedTask;
        }

        public Task CreateFolderAsync(string remotePath, string folderName, CancellationToken cancellationToken = default)
        {
            Folders.Add((remotePath, folderName));
            return Task.CompletedTask;
        }

        public Task<StorageQuota?> GetQuotaAsync(string username, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<List<VoSpaceNode>> ListNodesAsync(string path, int? limit = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Stream> DownloadFileAsync(string remotePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task DeleteNodeAsync(string remotePath, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "exporttest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public async Task BuildBundle_WritesModuleFiles_ManifestAndReadme()
    {
        var dir = TempDir();
        var module = new FakeModule();
        module.Output.JsonFiles["observations.json"] = "[]";
        module.Output.MarkdownFiles["notes.md"] = "# Notes";
        module.Output.ItemCounts["observations"] = 3;
        module.Output.ItemCounts["notes"] = 1;

        var bundle = await new ExportService().BuildBundleAsync(
            dir, new[] { module }, new ExportOptions(), Now, "1.1.0.0", "TESTHOST");

        Assert.True(File.Exists(Path.Combine(bundle, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(bundle, "README.md")));
        Assert.True(File.Exists(Path.Combine(bundle, "research", "observations.json")));
        Assert.True(File.Exists(Path.Combine(bundle, "research", "notes.md")));

        var manifest = JsonSerializer.Deserialize<ExportManifest>(
            await File.ReadAllTextAsync(Path.Combine(bundle, "manifest.json")), ReadOptions)!;
        Assert.Equal("1.0", manifest.ExportVersion);
        Assert.Equal("TESTHOST", manifest.HostName);
        var m = Assert.Single(manifest.Modules);
        Assert.Equal("research", m.Id);
        Assert.Equal(3, m.ItemCounts["observations"]);
        Assert.Equal("research/notes.md", manifest.ClaudeHints.PrimaryContext);     // first .md
        Assert.Equal("research/observations.json", manifest.ClaudeHints.MetadataSchema); // first .json

        var readme = await File.ReadAllTextAsync(Path.Combine(bundle, "README.md"));
        Assert.Contains("Verbinal Export", readme);
        Assert.Contains("3 observations", readme);
    }

    [Fact]
    public async Task BuildBundle_FailsLoudlyAndDeletesBundle_WhenAttachedFileMissing()
    {
        var dir = TempDir();
        var module = new FakeModule();
        module.Output.AttachedFiles.Add(Path.Combine(dir, "does-not-exist.fits"));

        await Assert.ThrowsAsync<ExportException>(() => new ExportService().BuildBundleAsync(
            dir, new[] { module }, new ExportOptions { IncludeFileCopies = true }, Now, "1", "h"));

        Assert.Empty(Directory.GetDirectories(dir)); // partial bundle removed
    }

    [Fact]
    public async Task ZipBundle_ProducesZipContainingManifest()
    {
        var dir = TempDir();
        var bundle = await new ExportService().BuildBundleAsync(
            dir, new[] { new FakeModule() }, new ExportOptions(), Now, "1", "h");

        var zip = new ExportService().ZipBundle(bundle);

        Assert.True(File.Exists(zip));
        Assert.EndsWith(".zip", zip);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName.EndsWith("manifest.json", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UploadBundle_EnsuresFolderAndUploadsZip()
    {
        var dir = TempDir();
        var svc = new ExportService();
        var bundle = await svc.BuildBundleAsync(dir, new[] { new FakeModule() }, new ExportOptions(), Now, "1", "h");
        var zip = svc.ZipBundle(bundle);
        var storage = new FakeStorage();

        var remote = await svc.UploadBundleToVoSpaceAsync(zip, storage, "alice");

        Assert.Equal($"alice/Verbinal-Exports/{Path.GetFileName(zip)}", remote);
        Assert.Contains(storage.Folders, f => f is { Path: "alice", Folder: "Verbinal-Exports" });
        Assert.Contains(storage.Uploads, u => u.Remote == remote && u.ContentType == "application/zip");
    }
}
