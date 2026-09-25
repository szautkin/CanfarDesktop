using Xunit;
using CanfarDesktop.Helpers;

namespace CanfarDesktop.Tests.Helpers;

public class DiskPersistenceTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), "verbinal_test_" + Guid.NewGuid().ToString("N") + ".json");

    public void Dispose()
    {
        foreach (var p in new[] { _path, _path + ".corrupt", _path + ".tmp" })
            if (File.Exists(p)) File.Delete(p);
    }

    private static List<string> Empty() => new();

    [Fact]
    public void Read_MissingFile_ReturnsEmpty()
    {
        var r = DiskPersistence.Read(_path, 1, Empty);
        Assert.Empty(r.Value);
        Assert.False(r.WasCorrupt);
        Assert.False(r.WasNewerVersion);
        Assert.False(r.WasLegacy);
    }

    [Fact]
    public void WriteThenRead_RoundTrips()
    {
        var data = new List<string> { "a", "b" };
        Assert.True(DiskPersistence.Write(_path, data, 1));

        var r = DiskPersistence.Read(_path, 1, Empty);
        Assert.Equal(data, r.Value);
        Assert.False(r.WasLegacy);
        Assert.False(r.WasCorrupt);
    }

    [Fact]
    public void Read_LegacyBareArray_LoadsAndFlags()
    {
        File.WriteAllText(_path, "[\"x\",\"y\"]");
        var r = DiskPersistence.Read(_path, 1, Empty);
        Assert.Equal(new[] { "x", "y" }, r.Value);
        Assert.True(r.WasLegacy);
    }

    [Fact]
    public void Read_Corrupt_QuarantinesAndReturnsEmpty()
    {
        File.WriteAllText(_path, "{ this is not valid json");
        var r = DiskPersistence.Read(_path, 1, Empty);
        Assert.True(r.WasCorrupt);
        Assert.Empty(r.Value);
        Assert.False(File.Exists(_path));
        Assert.True(File.Exists(_path + ".corrupt"));
    }

    [Fact]
    public void Read_NewerVersion_RefusesAndFlags()
    {
        File.WriteAllText(_path, "{\"schemaVersion\":99,\"value\":[\"z\"]}");
        var r = DiskPersistence.Read(_path, 1, Empty);
        Assert.True(r.WasNewerVersion);
        Assert.Empty(r.Value);
    }

    [Fact]
    public void Write_RefusesToClobberNewerFile()
    {
        File.WriteAllText(_path, "{\"schemaVersion\":99,\"value\":[\"keep\"]}");
        var ok = DiskPersistence.Write(_path, new List<string> { "new" }, 1);
        Assert.False(ok);

        // File unchanged — still the version-99 payload.
        var r = DiskPersistence.Read(_path, 99, Empty);
        Assert.Equal(new[] { "keep" }, r.Value);
    }

    [Fact]
    public void Write_NullPath_ReturnsFalse()
        => Assert.False(DiskPersistence.Write<List<string>>(null, new(), 1));

    [Fact]
    public void Write_CreatesAMissingFolder_AndLeavesNoTempFile()
    {
        // Every store used to make its own folder first; the write owns that now.
        var folder = Path.Combine(Path.GetTempPath(), "verbinal_test_" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(folder, "nested", "store.json");
        try
        {
            Assert.True(DiskPersistence.Write(path, new List<string> { "a" }, 1));
            Assert.Equal(new[] { "a" }, DiskPersistence.Read(path, 1, Empty).Value);
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }
}
