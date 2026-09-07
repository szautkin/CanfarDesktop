using Xunit;
using NSubstitute;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Tests.ViewModels.Notebook;

public class TextFileLoadTests : IDisposable
{
    private readonly IDirtyTracker _dirtyTracker = new DirtyTracker();
    private readonly IAutoSaveService _autoSave = Substitute.For<IAutoSaveService>();
    private readonly IKernelService _kernel = Substitute.For<IKernelService>();
    private readonly RecentNotebooksService _recent = new();
    private readonly NotebookViewModel _vm;

    public TextFileLoadTests()
    {
        _recent.Clear();
        _vm = new NotebookViewModel(_dirtyTracker, _autoSave, _kernel, _recent);
    }

    public void Dispose() => _vm.Close();

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "verbinal-nbfmt-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ── Loading ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>An ordinary script with no markers is one code cell — which is also the old behaviour.</summary>
    [Fact]
    public void LoadPythonFile_WithNoMarkers_IsOneCodeCell()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "print('hello')", NotebookFormat.PercentPython);

        Assert.Single(_vm.Cells);
        Assert.IsType<CodeCellViewModel>(_vm.Cells[0]);
        Assert.Equal("print('hello')", _vm.Cells[0].Source);
    }

    [Fact]
    public void LoadPythonFile_SetsTitle()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "x = 1", NotebookFormat.PercentPython);

        Assert.Equal("script.py", _vm.Title);
    }

    [Fact]
    public void LoadPythonFile_SetsFormat()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "x = 1", NotebookFormat.PercentPython);

        Assert.Equal(NotebookFormat.PercentPython, _vm.Format);
    }

    /// <summary>
    /// The bug this closes: a 500-line script arrived as ONE cell you could only run all at once. Every
    /// editor that runs Python interactively splits on the percent marker, and a scientist's script
    /// already contains them.
    /// </summary>
    [Fact]
    public void LoadPythonFile_SplitsOnPercentMarkers()
    {
        const string script = "# %%\nimport numpy\n\n# %% [markdown]\n# Some prose.\n\n# %%\nprint(1)\n";

        _vm.LoadFromTextFile("C:\\test\\script.py", script, NotebookFormat.PercentPython);

        Assert.Equal(3, _vm.Cells.Count);
        Assert.IsType<CodeCellViewModel>(_vm.Cells[0]);
        Assert.IsType<MarkdownCellViewModel>(_vm.Cells[1]);
        Assert.IsType<CodeCellViewModel>(_vm.Cells[2]);
    }

    [Fact]
    public void LoadMarkdownFile_WithNoFences_IsOneMarkdownCell()
    {
        _vm.LoadFromTextFile("C:\\test\\readme.md", "# Hello\nWorld", NotebookFormat.Markdown);

        Assert.Single(_vm.Cells);
        Assert.IsType<MarkdownCellViewModel>(_vm.Cells[0]);
        Assert.Equal("# Hello\nWorld", _vm.Cells[0].Source);
    }

    [Fact]
    public void LoadMarkdownFile_MakesFencedPythonACodeCell()
    {
        const string document = "# Title\n\nProse.\n\n```python\nx = 1\n```\n";

        _vm.LoadFromTextFile("C:\\test\\notes.md", document, NotebookFormat.Markdown);

        Assert.Equal(2, _vm.Cells.Count);
        Assert.IsType<MarkdownCellViewModel>(_vm.Cells[0]);
        Assert.IsType<CodeCellViewModel>(_vm.Cells[1]);
    }

    [Fact]
    public void LoadMarkdownFile_SetsFormat()
    {
        _vm.LoadFromTextFile("C:\\test\\readme.md", "# Title", NotebookFormat.Markdown);

        Assert.Equal(NotebookFormat.Markdown, _vm.Format);
    }

    /// <summary>A .txt has no cell convention to find, and inventing one would cut up someone's prose.</summary>
    [Fact]
    public void LoadTextFile_IsOneMarkdownCellAndIsNotCutUp()
    {
        _vm.LoadFromTextFile("C:\\test\\notes.txt", "Seeing was good.\n\nCloud after 03:00.\n", NotebookFormat.PlainText);

        Assert.Single(_vm.Cells);
        Assert.IsType<MarkdownCellViewModel>(_vm.Cells[0]);
    }

    [Fact]
    public void LoadTextFile_IsNotDirty()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "x = 1", NotebookFormat.PercentPython);

        Assert.False(_vm.IsDirty);
    }

    [Fact]
    public void LoadTextFile_EditMakesDirty()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "x = 1", NotebookFormat.PercentPython);

        _vm.Cells[0].Source = "x = 2";

        Assert.True(_vm.IsDirty);
    }

    [Fact]
    public void Format_DefaultIsNotebook() => Assert.Equal(NotebookFormat.Ipynb, _vm.Format);

    [Fact]
    public void LoadFromFile_SetsNotebookFormat()
    {
        var doc = NotebookParser.CreateEmpty();
        _vm.LoadFromFile("C:\\test\\nb.ipynb", doc);

        Assert.Equal(NotebookFormat.Ipynb, _vm.Format);
    }

    // ── Saving, which is where the file was being destroyed ─────────────────────────────────────

    /// <summary>
    /// Open analysis.py, press Ctrl+S, and it stays a script. It used to be replaced by nbformat JSON —
    /// a file someone may have opened only to read.
    /// </summary>
    [Fact]
    public async Task SavingAScriptKeepsItAScript()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "analysis.py");
            await File.WriteAllTextAsync(path, "# %%\nx = 1\n");

            _vm.LoadFromTextFile(path, await File.ReadAllTextAsync(path), NotebookFormat.PercentPython);
            await _vm.SaveAsync();

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("# %%", written);
            Assert.DoesNotContain("nbformat", written);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Every cell, not only the first. Writing Cells[0] alone silently dropped everything after it — a
    /// data loss you find out about the next time you open the file.
    /// </summary>
    [Fact]
    public async Task SavingAScriptWritesEveryCell()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "analysis.py");
            await File.WriteAllTextAsync(path, "# %%\nfirst = 1\n\n# %%\nsecond = 2\n");

            _vm.LoadFromTextFile(path, await File.ReadAllTextAsync(path), NotebookFormat.PercentPython);
            Assert.Equal(2, _vm.Cells.Count);

            await _vm.SaveAsync();

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("first = 1", written);
            Assert.Contains("second = 2", written);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>Save As to a .py makes it a script from here on: the format follows the name chosen.</summary>
    [Fact]
    public async Task SaveAs_TakesItsFormatFromTheChosenName()
    {
        var dir = TempDir();
        try
        {
            _vm.LoadNew();
            _vm.Cells[0].Source = "x = 1";

            var path = Path.Combine(dir, "script.py");
            await _vm.SaveAsAsync(path);

            Assert.Equal(NotebookFormat.PercentPython, _vm.Format);

            var written = await File.ReadAllTextAsync(path);
            Assert.Contains("# %%", written);
            Assert.DoesNotContain("nbformat", written);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    /// <summary>And the other way: Save As to .ipynb from a script writes a real notebook.</summary>
    [Fact]
    public async Task SaveAs_ToANotebookWritesJson()
    {
        var dir = TempDir();
        try
        {
            _vm.LoadFromTextFile("C:\\test\\script.py", "# %%\nx = 1\n", NotebookFormat.PercentPython);

            var path = Path.Combine(dir, "converted.ipynb");
            await _vm.SaveAsAsync(path);

            Assert.Equal(NotebookFormat.Ipynb, _vm.Format);
            Assert.Contains("nbformat", await File.ReadAllTextAsync(path));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
