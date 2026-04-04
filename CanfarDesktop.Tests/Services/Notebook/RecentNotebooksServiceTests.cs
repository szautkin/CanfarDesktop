using Xunit;
using CanfarDesktop.Services.Notebook;

namespace CanfarDesktop.Tests.Services.Notebook;

public class RecentNotebooksServiceTests
{
    private readonly RecentNotebooksService _service;

    public RecentNotebooksServiceTests()
    {
        _service = new RecentNotebooksService();
        _service.Clear(); // start clean — avoid cross-test pollution from shared disk file
    }

    [Fact]
    public void AddOrUpdate_AddsEntry()
    {
        _service.AddOrUpdate("C:\\test\\notebook1.ipynb");

        Assert.Single(_service.Entries);
        Assert.Equal("notebook1.ipynb", _service.Entries[0].Name);
    }

    [Fact]
    public void AddOrUpdate_DuplicatePath_MovesToTop()
    {
        _service.AddOrUpdate("C:\\test\\a.ipynb");
        _service.AddOrUpdate("C:\\test\\b.ipynb");
        _service.AddOrUpdate("C:\\test\\a.ipynb"); // re-add

        Assert.Equal(2, _service.Entries.Count);
        Assert.Equal("a.ipynb", _service.Entries[0].Name); // a is now first
    }

    [Fact]
    public void AddOrUpdate_Max15_EvictsOldest()
    {
        for (int i = 0; i < 20; i++)
            _service.AddOrUpdate($"C:\\test\\nb{i}.ipynb");

        Assert.Equal(15, _service.Entries.Count);
        Assert.Equal("nb19.ipynb", _service.Entries[0].Name); // most recent
    }

    [Fact]
    public void Remove_DeletesEntry()
    {
        _service.AddOrUpdate("C:\\test\\a.ipynb");
        _service.AddOrUpdate("C:\\test\\b.ipynb");

        _service.Remove("C:\\test\\a.ipynb");

        Assert.Single(_service.Entries);
        Assert.Equal("b.ipynb", _service.Entries[0].Name);
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        _service.AddOrUpdate("C:\\test\\a.ipynb");
        _service.AddOrUpdate("C:\\test\\b.ipynb");

        _service.Clear();

        Assert.Empty(_service.Entries);
    }

    [Fact]
    public void AddOrUpdate_CaseInsensitiveDedup()
    {
        _service.AddOrUpdate("C:\\Test\\A.ipynb");
        _service.AddOrUpdate("c:\\test\\a.ipynb");

        Assert.Single(_service.Entries);
    }

    [Fact]
    public void Changed_EventFires()
    {
        var fired = false;
        _service.Changed += () => fired = true;

        _service.AddOrUpdate("C:\\test\\a.ipynb");

        Assert.True(fired);
    }
}
