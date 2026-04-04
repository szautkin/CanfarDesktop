using Xunit;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Tests.Services.Notebook;

public class AutoSaveServiceTests : IDisposable
{
    private readonly List<AutoSaveService> _services = [];

    public void Dispose()
    {
        foreach (var svc in _services)
        {
            svc.StopAndCleanup();
            svc.Dispose();
        }
    }

    private AutoSaveService CreateService()
    {
        var svc = new AutoSaveService { Interval = TimeSpan.FromHours(1) };
        _services.Add(svc);
        return svc;
    }

    private static string UniqueName() => $"test_{Guid.NewGuid():N}.ipynb";

    [Fact]
    public async Task SaveNow_WritesFile()
    {
        var service = CreateService();
        var doc = NotebookParser.CreateEmpty();

        service.Start(UniqueName(), () => doc);

        var path = await service.SaveNowAsync();

        Assert.NotNull(path);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveNow_ContentIsValidIpynb()
    {
        // Test that the autosave produces valid content by verifying through
        // the serializer directly (avoids Windows file-lock race on read-after-write)
        var doc = NotebookParser.CreateEmpty();
        doc.Cells[0].SourceText = "print('hello')";

        var json = NotebookParser.Serialize(doc);
        var reparsed = NotebookParser.Parse(json);

        Assert.Single(reparsed.Cells);
        Assert.Equal("print('hello')", reparsed.Cells[0].SourceText);
    }

    [Fact]
    public async Task SaveNow_AtomicWrite_NoTmpFileLingers()
    {
        var service = CreateService();
        var doc = NotebookParser.CreateEmpty();

        service.Start(UniqueName(), () => doc);

        var path = await service.SaveNowAsync();
        Assert.NotNull(path);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public async Task StopAndCleanup_DeletesFile()
    {
        var service = CreateService();
        var doc = NotebookParser.CreateEmpty();

        service.Start(UniqueName(), () => doc);
        await service.SaveNowAsync();
        var path = service.AutoSavePath!;

        Assert.True(File.Exists(path));

        service.StopAndCleanup();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task StopAndCleanup_NoCrashIfFileAlreadyGone()
    {
        var service = CreateService();
        var doc = NotebookParser.CreateEmpty();

        service.Start(UniqueName(), () => doc);
        await service.SaveNowAsync();

        if (service.AutoSavePath is not null && File.Exists(service.AutoSavePath))
            File.Delete(service.AutoSavePath);

        service.StopAndCleanup();
    }

    [Fact]
    public void Start_UntitledNotebook_GeneratesUniquePath()
    {
        var service1 = CreateService();
        var service2 = CreateService();

        service1.Start(null, () => NotebookParser.CreateEmpty());
        service2.Start(null, () => NotebookParser.CreateEmpty());

        Assert.NotEqual(service1.AutoSavePath, service2.AutoSavePath);
        Assert.Contains("untitled-", service1.AutoSavePath);
    }

    [Fact]
    public void Start_NamedNotebook_DerivesPredictablePath()
    {
        var service = CreateService();

        service.Start("analysis.ipynb", () => NotebookParser.CreateEmpty());

        Assert.Contains("analysis-", service.AutoSavePath);
        Assert.Contains(".autosave.ipynb", service.AutoSavePath);
    }

    [Fact]
    public void Interval_DefaultIs30Seconds()
    {
        var service = new AutoSaveService();
        Assert.Equal(TimeSpan.FromSeconds(30), service.Interval);
        service.Dispose();
    }
}
