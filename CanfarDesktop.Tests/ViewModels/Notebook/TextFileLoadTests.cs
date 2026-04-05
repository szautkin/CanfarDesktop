using Xunit;
using NSubstitute;
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

    [Fact]
    public void LoadPythonFile_CreatesSingleCodeCell()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "print('hello')", NotebookFileMode.PythonScript);

        Assert.Single(_vm.Cells);
        Assert.IsType<CodeCellViewModel>(_vm.Cells[0]);
        Assert.Equal("print('hello')", _vm.Cells[0].Source);
    }

    [Fact]
    public void LoadPythonFile_SetsTitle()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "x = 1", NotebookFileMode.PythonScript);

        Assert.Equal("script.py", _vm.Title);
    }

    [Fact]
    public void LoadPythonFile_SetsFileMode()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "x = 1", NotebookFileMode.PythonScript);

        Assert.Equal(NotebookFileMode.PythonScript, _vm.FileMode);
    }

    [Fact]
    public void LoadMarkdownFile_CreatesSingleMarkdownCell()
    {
        _vm.LoadFromTextFile("C:\\test\\readme.md", "# Hello\nWorld", NotebookFileMode.Markdown);

        Assert.Single(_vm.Cells);
        Assert.IsType<MarkdownCellViewModel>(_vm.Cells[0]);
        Assert.Equal("# Hello\nWorld", _vm.Cells[0].Source);
    }

    [Fact]
    public void LoadMarkdownFile_SetsFileMode()
    {
        _vm.LoadFromTextFile("C:\\test\\readme.md", "# Title", NotebookFileMode.Markdown);

        Assert.Equal(NotebookFileMode.Markdown, _vm.FileMode);
    }

    [Fact]
    public void LoadTextFile_IsNotDirty()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "x = 1", NotebookFileMode.PythonScript);

        Assert.False(_vm.IsDirty);
    }

    [Fact]
    public void LoadTextFile_EditMakesDirty()
    {
        _vm.LoadFromTextFile("C:\\test\\script.py", "x = 1", NotebookFileMode.PythonScript);

        _vm.Cells[0].Source = "x = 2";

        Assert.True(_vm.IsDirty);
    }

    [Fact]
    public void FileMode_DefaultIsNotebook()
    {
        Assert.Equal(NotebookFileMode.Notebook, _vm.FileMode);
    }

    [Fact]
    public void LoadFromFile_SetsNotebookMode()
    {
        var doc = CanfarDesktop.Helpers.Notebook.NotebookParser.CreateEmpty();
        _vm.LoadFromFile("C:\\test\\nb.ipynb", doc);

        Assert.Equal(NotebookFileMode.Notebook, _vm.FileMode);
    }
}
