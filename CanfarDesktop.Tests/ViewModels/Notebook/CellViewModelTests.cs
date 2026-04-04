using Xunit;
using CanfarDesktop.Models.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Tests.ViewModels.Notebook;

public class CellViewModelTests
{
    #region CodeCellViewModel

    [Fact]
    public void CodeCell_SourceChange_UpdatesModel()
    {
        var model = new NotebookCell { CellType = "code", Source = ["x = 1"] };
        var vm = new CodeCellViewModel(model);

        vm.Source = "x = 2";

        Assert.Equal("x = 2", model.SourceText);
    }

    [Fact]
    public void CodeCell_SourceChange_FiresContentChanged()
    {
        var model = new NotebookCell { CellType = "code", Source = [] };
        var vm = new CodeCellViewModel(model);
        var fired = false;
        vm.ContentChanged += () => fired = true;

        vm.Source = "hello";

        Assert.True(fired);
    }

    [Fact]
    public void CodeCell_ExecutionStatus_NotExecuted()
    {
        var model = new NotebookCell { CellType = "code", ExecutionCount = null };
        var vm = new CodeCellViewModel(model);

        Assert.Equal("[ ]", vm.ExecutionStatus);
    }

    [Fact]
    public void CodeCell_ExecutionStatus_Executing()
    {
        var model = new NotebookCell { CellType = "code" };
        var vm = new CodeCellViewModel(model);

        vm.IsExecuting = true;

        Assert.Equal("[*]", vm.ExecutionStatus);
    }

    [Fact]
    public void CodeCell_ExecutionStatus_Executed()
    {
        var model = new NotebookCell { CellType = "code", ExecutionCount = 3 };
        var vm = new CodeCellViewModel(model);

        Assert.Equal("[3]", vm.ExecutionStatus);
    }

    [Fact]
    public void CodeCell_ClearOutputs_EmptiesCollection()
    {
        var model = new NotebookCell
        {
            CellType = "code",
            ExecutionCount = 1,
            Outputs = [new CellOutput { OutputType = "stream", Name = "stdout", Text = ["hi"] }]
        };
        var vm = new CodeCellViewModel(model);
        Assert.Single(vm.Outputs);

        vm.ClearOutputs();

        Assert.Empty(vm.Outputs);
        Assert.Null(vm.ExecutionCount);
    }

    [Fact]
    public void CodeCell_SyncToModel_PersistsState()
    {
        var model = new NotebookCell { CellType = "code", Source = ["old"] };
        var vm = new CodeCellViewModel(model);
        vm.Source = "new content";
        vm.ExecutionCount = 5;

        vm.SyncToModel();

        Assert.Equal("new content", model.SourceText);
        Assert.Equal(5, model.ExecutionCount);
    }

    #endregion

    #region MarkdownCellViewModel

    [Fact]
    public void MarkdownCell_InitialState_IsRendered()
    {
        var model = new NotebookCell { CellType = "markdown", Source = ["# Hello"] };
        var vm = new MarkdownCellViewModel(model);

        Assert.True(vm.IsRendered);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void MarkdownCell_EnterEditMode_SetsFlags()
    {
        var model = new NotebookCell { CellType = "markdown" };
        var vm = new MarkdownCellViewModel(model);

        vm.EnterEditMode();

        Assert.False(vm.IsRendered);
        Assert.True(vm.IsEditing);
    }

    [Fact]
    public void MarkdownCell_ExitEditMode_SetsFlags()
    {
        var model = new NotebookCell { CellType = "markdown", Source = ["# Test"] };
        var vm = new MarkdownCellViewModel(model);

        vm.EnterEditMode();
        vm.ExitEditMode();

        Assert.True(vm.IsRendered);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void MarkdownCell_ToggleEditMode_Toggles()
    {
        var model = new NotebookCell { CellType = "markdown" };
        var vm = new MarkdownCellViewModel(model);

        vm.ToggleEditMode();
        Assert.False(vm.IsRendered);

        vm.ToggleEditMode();
        Assert.True(vm.IsRendered);
    }

    #endregion

    #region CellOutputViewModel

    [Fact]
    public void OutputVM_StreamOutput_HasTextContent()
    {
        var output = new CellOutput { OutputType = "stream", Name = "stdout", Text = ["hello\n", "world"] };
        var vm = new CellOutputViewModel(output);

        Assert.Equal("hello\nworld", vm.TextContent);
        Assert.False(vm.IsError);
        Assert.False(vm.HasImage);
    }

    [Fact]
    public void OutputVM_ErrorOutput_HasErrorFields()
    {
        var output = new CellOutput
        {
            OutputType = "error",
            Ename = "ValueError",
            Evalue = "bad input",
            Traceback = ["line 1", "line 2"]
        };
        var vm = new CellOutputViewModel(output);

        Assert.True(vm.IsError);
        Assert.Equal("ValueError: bad input", vm.ErrorName);
        Assert.Equal("line 1\nline 2", vm.Traceback);
    }

    #endregion
}
