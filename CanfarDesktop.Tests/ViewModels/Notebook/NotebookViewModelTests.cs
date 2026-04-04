using Xunit;
using NSubstitute;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Models.Notebook;
using CanfarDesktop.Services.Notebook;
using CanfarDesktop.ViewModels.Notebook;

namespace CanfarDesktop.Tests.ViewModels.Notebook;

public class NotebookViewModelTests : IDisposable
{
    private readonly IDirtyTracker _dirtyTracker = new DirtyTracker();
    private readonly IAutoSaveService _autoSave = Substitute.For<IAutoSaveService>();
    private readonly IKernelService _kernel = Substitute.For<IKernelService>();
    private readonly RecentNotebooksService _recent = new();
    private readonly NotebookViewModel _vm;

    public NotebookViewModelTests()
    {
        _vm = new NotebookViewModel(_dirtyTracker, _autoSave, _kernel, _recent);
    }

    public void Dispose()
    {
        _vm.Close();
    }

    #region Initial state

    [Fact]
    public void LoadNew_HasOneCodeCell()
    {
        _vm.LoadNew();

        Assert.Single(_vm.Cells);
        Assert.IsType<CodeCellViewModel>(_vm.Cells[0]);
        Assert.Equal("Untitled", _vm.Title);
        Assert.False(_vm.IsDirty);
    }

    [Fact]
    public void LoadFromFile_PopulatesCells()
    {
        var doc = NotebookParser.CreateEmpty();
        doc.Cells.Add(new NotebookCell { CellType = "markdown", Id = "md1", Source = ["# Title"] });

        _vm.LoadFromFile("test.ipynb", doc);

        Assert.Equal(2, _vm.Cells.Count);
        Assert.IsType<CodeCellViewModel>(_vm.Cells[0]);
        Assert.IsType<MarkdownCellViewModel>(_vm.Cells[1]);
        Assert.Equal("test.ipynb", _vm.Title);
    }

    #endregion

    #region Cell operations

    [Fact]
    public void AddCellBelow_InsertsAtCorrectIndex()
    {
        _vm.LoadNew();
        Assert.Single(_vm.Cells);

        _vm.AddCellBelowCommand.Execute("code");

        Assert.Equal(2, _vm.Cells.Count);
        Assert.Equal(1, _vm.SelectedCellIndex);
    }

    [Fact]
    public void AddCellAbove_InsertsAtCorrectIndex()
    {
        _vm.LoadNew();
        _vm.SelectCell(0);

        _vm.AddCellAboveCommand.Execute("markdown");

        Assert.Equal(2, _vm.Cells.Count);
        Assert.IsType<MarkdownCellViewModel>(_vm.Cells[0]);
        Assert.Equal(0, _vm.SelectedCellIndex);
    }

    [Fact]
    public void DeleteCell_RemovesAndReselectsNext()
    {
        _vm.LoadNew();
        _vm.AddCellBelowCommand.Execute("code");
        _vm.AddCellBelowCommand.Execute("code");
        Assert.Equal(3, _vm.Cells.Count);

        _vm.SelectCell(1);
        _vm.DeleteSelectedCellCommand.Execute(null);

        Assert.Equal(2, _vm.Cells.Count);
        Assert.Equal(1, _vm.SelectedCellIndex);
    }

    [Fact]
    public void DeleteCell_PreventsDeletingLastCell()
    {
        _vm.LoadNew();
        Assert.Single(_vm.Cells);

        _vm.SelectCell(0);
        _vm.DeleteSelectedCellCommand.Execute(null);

        Assert.Single(_vm.Cells); // still 1 cell
    }

    [Fact]
    public void MoveCellUp_SwapsCells()
    {
        _vm.LoadNew();
        _vm.Cells[0].Source = "first";
        _vm.AddCellBelowCommand.Execute("code");
        _vm.Cells[1].Source = "second";

        _vm.SelectCell(1);
        _vm.MoveCellUpCommand.Execute(null);

        Assert.Equal("second", _vm.Cells[0].Source);
        Assert.Equal("first", _vm.Cells[1].Source);
        Assert.Equal(0, _vm.SelectedCellIndex);
    }

    [Fact]
    public void MoveCellDown_SwapsCells()
    {
        _vm.LoadNew();
        _vm.Cells[0].Source = "first";
        _vm.AddCellBelowCommand.Execute("code");
        _vm.Cells[1].Source = "second";

        _vm.SelectCell(0);
        _vm.MoveCellDownCommand.Execute(null);

        Assert.Equal("second", _vm.Cells[0].Source);
        Assert.Equal("first", _vm.Cells[1].Source);
        Assert.Equal(1, _vm.SelectedCellIndex);
    }

    [Fact]
    public void MoveCellUp_AtTop_NoOp()
    {
        _vm.LoadNew();
        _vm.SelectCell(0);
        _vm.MoveCellUpCommand.Execute(null);
        Assert.Equal(0, _vm.SelectedCellIndex);
    }

    [Fact]
    public void MoveCellDown_AtBottom_NoOp()
    {
        _vm.LoadNew();
        _vm.SelectCell(0);
        _vm.MoveCellDownCommand.Execute(null);
        Assert.Equal(0, _vm.SelectedCellIndex);
    }

    [Fact]
    public void SplitCell_ProducesCorrectParts()
    {
        _vm.LoadNew();
        _vm.Cells[0].Source = "line1\nline2";
        _vm.SelectCell(0);

        _vm.SplitCellCommand.Execute(5); // split after "line1"

        Assert.Equal(2, _vm.Cells.Count);
        Assert.Equal("line1", _vm.Cells[0].Source);
        Assert.Equal("\nline2", _vm.Cells[1].Source);
    }

    [Fact]
    public void MergeCellBelow_CombinesSources()
    {
        _vm.LoadNew();
        _vm.Cells[0].Source = "top";
        _vm.AddCellBelowCommand.Execute("code");
        _vm.Cells[1].Source = "bottom";

        _vm.SelectCell(0);
        _vm.MergeCellBelowCommand.Execute(null);

        Assert.Single(_vm.Cells);
        Assert.Equal("top\nbottom", _vm.Cells[0].Source);
    }

    [Fact]
    public void MergeCellBelow_AtBottom_NoOp()
    {
        _vm.LoadNew();
        _vm.SelectCell(0);
        _vm.MergeCellBelowCommand.Execute(null);
        Assert.Single(_vm.Cells); // no crash, no change
    }

    [Fact]
    public void ChangeCellType_PreservesSource()
    {
        _vm.LoadNew();
        _vm.Cells[0].Source = "# Heading";
        _vm.SelectCell(0);

        _vm.ChangeCellTypeCommand.Execute("markdown");

        Assert.IsType<MarkdownCellViewModel>(_vm.Cells[0]);
        Assert.Equal("# Heading", _vm.Cells[0].Source);
    }

    [Fact]
    public void ChangeCellType_SameType_NoOp()
    {
        _vm.LoadNew();
        var original = _vm.Cells[0];
        _vm.SelectCell(0);

        _vm.ChangeCellTypeCommand.Execute("code");

        Assert.Same(original, _vm.Cells[0]);
    }

    [Fact]
    public void ClearAllOutputs_ClearsAllCodeCells()
    {
        var doc = NotebookParser.Parse("""
            {
              "nbformat": 4, "nbformat_minor": 5, "metadata": {},
              "cells": [
                { "cell_type": "code", "id": "c1", "source": ["1+1"], "metadata": {},
                  "outputs": [{ "output_type": "execute_result", "data": { "text/plain": "2" }, "metadata": {}, "execution_count": 1 }],
                  "execution_count": 1 }
              ]
            }
            """);
        _vm.LoadFromFile("test.ipynb", doc);

        var code = (CodeCellViewModel)_vm.Cells[0];
        Assert.Single(code.Outputs);

        _vm.ClearAllOutputsCommand.Execute(null);

        Assert.Empty(code.Outputs);
    }

    #endregion

    #region Dirty tracking

    [Fact]
    public void EditCell_MarksDirty()
    {
        _vm.LoadNew();
        Assert.False(_vm.IsDirty);

        _vm.Cells[0].Source = "changed";

        Assert.True(_vm.IsDirty);
    }

    [Fact]
    public void AddCell_MarksDirty()
    {
        _vm.LoadNew();
        _dirtyTracker.MarkClean();

        _vm.AddCellBelowCommand.Execute("code");

        Assert.True(_vm.IsDirty);
    }

    [Fact]
    public void Close_StopsAutoSave()
    {
        _vm.Close();
        _autoSave.Received(1).StopAndCleanup();
    }

    #endregion

    #region Selection

    [Fact]
    public void SelectCell_UpdatesSelectedCell()
    {
        _vm.LoadNew();
        _vm.AddCellBelowCommand.Execute("code");

        _vm.SelectCell(0);
        Assert.Equal(0, _vm.SelectedCellIndex);
        Assert.True(_vm.Cells[0].IsSelected);
        Assert.False(_vm.Cells[1].IsSelected);

        _vm.SelectCell(1);
        Assert.Equal(1, _vm.SelectedCellIndex);
        Assert.False(_vm.Cells[0].IsSelected);
        Assert.True(_vm.Cells[1].IsSelected);
    }

    #endregion
}
