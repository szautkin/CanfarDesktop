using System.Text.Json;
using Xunit;
using CanfarDesktop.Helpers.Notebook;
using CanfarDesktop.Models.Notebook;

namespace CanfarDesktop.Tests.Helpers.Notebook;

public class NotebookParserTests
{
    #region Test fixtures

    private const string SimpleNotebook = """
        {
          "nbformat": 4,
          "nbformat_minor": 5,
          "metadata": {
            "kernelspec": {
              "name": "python3",
              "display_name": "Python 3",
              "language": "python"
            },
            "language_info": {
              "name": "python",
              "version": "3.11.5"
            }
          },
          "cells": [
            {
              "cell_type": "code",
              "id": "abc12345",
              "source": ["import numpy as np\n", "x = np.array([1,2,3])"],
              "metadata": {},
              "outputs": [],
              "execution_count": null
            },
            {
              "cell_type": "markdown",
              "id": "def67890",
              "source": ["# Analysis\n", "This is a **test**."],
              "metadata": {}
            },
            {
              "cell_type": "code",
              "id": "ghi11111",
              "source": ["print(x)"],
              "metadata": {},
              "outputs": [
                {
                  "output_type": "stream",
                  "name": "stdout",
                  "text": ["[1 2 3]\n"]
                }
              ],
              "execution_count": 2
            }
          ]
        }
        """;

    private const string NotebookWithExtensionData = """
        {
          "nbformat": 4,
          "nbformat_minor": 5,
          "metadata": {
            "kernelspec": { "name": "python3", "display_name": "Python 3", "language": "python" },
            "papermill": { "duration": 12.5, "status": "completed" },
            "custom_tool_version": "1.2.3"
          },
          "cells": [
            {
              "cell_type": "code",
              "id": "a1b2c3d4",
              "source": ["1+1"],
              "metadata": { "tags": ["parameters"], "custom_flag": true },
              "outputs": [
                {
                  "output_type": "execute_result",
                  "data": { "text/plain": "2" },
                  "metadata": {},
                  "execution_count": 1
                }
              ],
              "execution_count": 1
            }
          ]
        }
        """;

    private const string NotebookWithAllOutputTypes = """
        {
          "nbformat": 4,
          "nbformat_minor": 5,
          "metadata": { "kernelspec": { "name": "python3", "display_name": "Python 3", "language": "python" } },
          "cells": [
            {
              "cell_type": "code",
              "id": "out1",
              "source": ["raise ValueError('bad')"],
              "metadata": {},
              "outputs": [
                {
                  "output_type": "error",
                  "ename": "ValueError",
                  "evalue": "bad",
                  "traceback": ["Traceback (most recent call last):", "  File ...", "ValueError: bad"]
                }
              ],
              "execution_count": 1
            },
            {
              "cell_type": "code",
              "id": "out2",
              "source": ["from IPython.display import Image"],
              "metadata": {},
              "outputs": [
                {
                  "output_type": "display_data",
                  "data": { "image/png": "iVBORw0KGgo=", "text/plain": "<Figure>" },
                  "metadata": { "image/png": { "width": 400, "height": 300 } }
                }
              ],
              "execution_count": 2
            }
          ]
        }
        """;

    private const string EmptyCellsNotebook = """
        {
          "nbformat": 4,
          "nbformat_minor": 5,
          "metadata": {},
          "cells": []
        }
        """;

    private const string NoCellIdNotebook = """
        {
          "nbformat": 4,
          "nbformat_minor": 4,
          "metadata": {},
          "cells": [
            {
              "cell_type": "code",
              "source": ["x = 1"],
              "metadata": {},
              "outputs": []
            }
          ]
        }
        """;

    private const string CodeCellWithoutOutputsKey = """
        {
          "nbformat": 4,
          "nbformat_minor": 5,
          "metadata": {},
          "cells": [
            {
              "cell_type": "code",
              "id": "noout1",
              "source": ["pass"],
              "metadata": {}
            }
          ]
        }
        """;

    #endregion

    #region Parse tests

    [Fact]
    public void Parse_ValidNotebook_ReturnsDocument()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);

        Assert.Equal(4, doc.NbFormat);
        Assert.Equal(5, doc.NbFormatMinor);
        Assert.Equal(3, doc.Cells.Count);
        Assert.Equal("python3", doc.Metadata.KernelSpec!.Name);
        Assert.Equal("3.11.5", doc.Metadata.LanguageInfo!.Version);
    }

    [Fact]
    public void Parse_CodeCell_HasSourceAndOutputs()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);
        var cell = doc.Cells[2]; // third cell has output

        Assert.Equal("code", cell.CellType);
        Assert.Equal("print(x)", cell.SourceText);
        Assert.Single(cell.Outputs!);
        Assert.Equal("stream", cell.Outputs![0].OutputType);
        Assert.Equal("stdout", cell.Outputs[0].Name);
        Assert.Equal("[1 2 3]\n", cell.Outputs[0].Text![0]);
        Assert.Equal(2, cell.ExecutionCount);
    }

    [Fact]
    public void Parse_MarkdownCell_HasSourceNoOutputs()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);
        var cell = doc.Cells[1];

        Assert.Equal("markdown", cell.CellType);
        Assert.Contains("# Analysis", cell.SourceText);
        Assert.Null(cell.Outputs);
        Assert.Null(cell.ExecutionCount);
    }

    [Fact]
    public void Parse_EmptyCells_ReturnsEmptyList()
    {
        var doc = NotebookParser.Parse(EmptyCellsNotebook);
        Assert.Empty(doc.Cells);
    }

    [Fact]
    public void Parse_CodeCellWithoutOutputsKey_GetsEmptyList()
    {
        var doc = NotebookParser.Parse(CodeCellWithoutOutputsKey);
        Assert.NotNull(doc.Cells[0].Outputs);
        Assert.Empty(doc.Cells[0].Outputs!);
    }

    [Fact]
    public void Parse_GeneratesMissingCellIds()
    {
        var doc = NotebookParser.Parse(NoCellIdNotebook);
        Assert.NotNull(doc.Cells[0].Id);
        Assert.Equal(8, doc.Cells[0].Id!.Length);
    }

    [Fact]
    public void Parse_PreservesCellIds()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);
        Assert.Equal("abc12345", doc.Cells[0].Id);
        Assert.Equal("def67890", doc.Cells[1].Id);
    }

    [Fact]
    public void Parse_ErrorOutput_HasTracebackFields()
    {
        var doc = NotebookParser.Parse(NotebookWithAllOutputTypes);
        var errorOutput = doc.Cells[0].Outputs![0];

        Assert.Equal("error", errorOutput.OutputType);
        Assert.Equal("ValueError", errorOutput.Ename);
        Assert.Equal("bad", errorOutput.Evalue);
        Assert.Equal(3, errorOutput.Traceback!.Count);
    }

    [Fact]
    public void Parse_DisplayDataOutput_HasImageData()
    {
        var doc = NotebookParser.Parse(NotebookWithAllOutputTypes);
        var displayOutput = doc.Cells[1].Outputs![0];

        Assert.Equal("display_data", displayOutput.OutputType);
        Assert.NotNull(displayOutput.Data);
        Assert.True(displayOutput.Data!.ContainsKey("image/png"));
        Assert.True(displayOutput.Data.ContainsKey("text/plain"));
    }

    [Fact]
    public void Parse_NullJsonThrows()
    {
        Assert.ThrowsAny<ArgumentException>(() => NotebookParser.Parse(null!));
        Assert.ThrowsAny<ArgumentException>(() => NotebookParser.Parse(""));
        Assert.ThrowsAny<ArgumentException>(() => NotebookParser.Parse("   "));
    }

    [Fact]
    public void Parse_MalformedJsonThrows()
    {
        Assert.Throws<JsonException>(() => NotebookParser.Parse("{not valid json"));
    }

    #endregion

    #region Serialize tests

    [Fact]
    public void Serialize_ProducesValidJson()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);
        var json = NotebookParser.Serialize(doc);

        Assert.False(string.IsNullOrWhiteSpace(json));

        // Must be re-parseable
        var reparsed = NotebookParser.Parse(json);
        Assert.Equal(doc.Cells.Count, reparsed.Cells.Count);
    }

    [Fact]
    public void Serialize_NonCodeCellsOmitOutputs()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);
        var json = NotebookParser.Serialize(doc);

        // Parse the raw JSON to verify markdown cell has no outputs key
        using var jsonDoc = JsonDocument.Parse(json);
        var cells = jsonDoc.RootElement.GetProperty("cells");
        var markdownCell = cells[1]; // second cell is markdown

        Assert.False(markdownCell.TryGetProperty("outputs", out _));
        Assert.False(markdownCell.TryGetProperty("execution_count", out _));
    }

    [Fact]
    public void Serialize_PreservesSourceLineFormat()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);
        var json = NotebookParser.Serialize(doc);

        using var jsonDoc = JsonDocument.Parse(json);
        var firstCell = jsonDoc.RootElement.GetProperty("cells")[0];
        var source = firstCell.GetProperty("source");

        Assert.Equal(JsonValueKind.Array, source.ValueKind);
        Assert.Equal(2, source.GetArrayLength());
        Assert.Equal("import numpy as np\n", source[0].GetString());
    }

    #endregion

    #region Round-trip tests

    [Fact]
    public void RoundTrip_PreservesExtensionData()
    {
        var doc = NotebookParser.Parse(NotebookWithExtensionData);
        var json = NotebookParser.Serialize(doc);
        var reparsed = NotebookParser.Parse(json);

        // Notebook-level extension data preserved
        Assert.NotNull(reparsed.Metadata.ExtensionData);
        Assert.True(reparsed.Metadata.ExtensionData!.ContainsKey("papermill"));
        Assert.True(reparsed.Metadata.ExtensionData.ContainsKey("custom_tool_version"));

        // Cell metadata extension data preserved
        var cellMeta = reparsed.Cells[0].Metadata;
        Assert.NotNull(cellMeta.Tags);
        Assert.Contains("parameters", cellMeta.Tags!);
        Assert.NotNull(cellMeta.ExtensionData);
        Assert.True(cellMeta.ExtensionData!.ContainsKey("custom_flag"));
    }

    [Fact]
    public void RoundTrip_AllOutputTypes_Preserved()
    {
        var doc = NotebookParser.Parse(NotebookWithAllOutputTypes);
        var json = NotebookParser.Serialize(doc);
        var reparsed = NotebookParser.Parse(json);

        // Error output
        var errorCell = reparsed.Cells[0];
        Assert.Equal("error", errorCell.Outputs![0].OutputType);
        Assert.Equal("ValueError", errorCell.Outputs[0].Ename);
        Assert.Equal(3, errorCell.Outputs[0].Traceback!.Count);

        // Display data with image
        var displayCell = reparsed.Cells[1];
        Assert.Equal("display_data", displayCell.Outputs![0].OutputType);
        Assert.True(displayCell.Outputs[0].Data!.ContainsKey("image/png"));
    }

    [Fact]
    public void RoundTrip_CellCount_Preserved()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);
        var json = NotebookParser.Serialize(doc);
        var reparsed = NotebookParser.Parse(json);

        Assert.Equal(doc.Cells.Count, reparsed.Cells.Count);
        for (int i = 0; i < doc.Cells.Count; i++)
        {
            Assert.Equal(doc.Cells[i].CellType, reparsed.Cells[i].CellType);
            Assert.Equal(doc.Cells[i].SourceText, reparsed.Cells[i].SourceText);
        }
    }

    [Fact]
    public async Task RoundTrip_StreamOverloads_MatchStringOverloads()
    {
        var doc = NotebookParser.Parse(SimpleNotebook);

        // Serialize via string
        var jsonString = NotebookParser.Serialize(doc);

        // Serialize via stream
        using var ms = new MemoryStream();
        await NotebookParser.SerializeAsync(doc, ms);
        ms.Position = 0;
        using var reader = new StreamReader(ms);
        var jsonStream = await reader.ReadToEndAsync();

        // Parse via stream
        ms.Position = 0;
        var docFromStream = await NotebookParser.ParseAsync(ms);

        Assert.Equal(doc.Cells.Count, docFromStream.Cells.Count);
        Assert.Equal(jsonString, jsonStream);
    }

    #endregion

    #region CreateEmpty tests

    [Fact]
    public void CreateEmpty_HasOneCodeCell()
    {
        var doc = NotebookParser.CreateEmpty();

        Assert.Equal(4, doc.NbFormat);
        Assert.Equal(5, doc.NbFormatMinor);
        Assert.Single(doc.Cells);
        Assert.Equal("code", doc.Cells[0].CellType);
        Assert.NotNull(doc.Cells[0].Id);
        Assert.NotNull(doc.Cells[0].Outputs);
        Assert.Empty(doc.Cells[0].Outputs!);
        Assert.Null(doc.Cells[0].ExecutionCount);
    }

    [Fact]
    public void CreateEmpty_HasKernelSpec()
    {
        var doc = NotebookParser.CreateEmpty();

        Assert.NotNull(doc.Metadata.KernelSpec);
        Assert.Equal("python3", doc.Metadata.KernelSpec!.Name);
        Assert.Equal("Python 3", doc.Metadata.KernelSpec.DisplayName);
        Assert.NotNull(doc.Metadata.LanguageInfo);
        Assert.Equal("python", doc.Metadata.LanguageInfo!.Name);
    }

    [Fact]
    public void CreateEmpty_CustomKernel()
    {
        var doc = NotebookParser.CreateEmpty("ir", "R (IRkernel)");

        Assert.Equal("ir", doc.Metadata.KernelSpec!.Name);
        Assert.Equal("R (IRkernel)", doc.Metadata.KernelSpec.DisplayName);
    }

    [Fact]
    public void CreateEmpty_IsValidRoundTrip()
    {
        var doc = NotebookParser.CreateEmpty();
        var json = NotebookParser.Serialize(doc);
        var reparsed = NotebookParser.Parse(json);

        Assert.Single(reparsed.Cells);
        Assert.Equal("code", reparsed.Cells[0].CellType);
    }

    #endregion

    #region SourceText tests

    [Fact]
    public void SourceText_Get_JoinsLines()
    {
        var cell = new NotebookCell
        {
            Source = ["line1\n", "line2\n", "line3"]
        };

        Assert.Equal("line1\nline2\nline3", cell.SourceText);
    }

    [Fact]
    public void SourceText_Set_SplitsLines()
    {
        var cell = new NotebookCell();
        cell.SourceText = "line1\nline2\nline3";

        Assert.Equal(3, cell.Source.Count);
        Assert.Equal("line1\n", cell.Source[0]);
        Assert.Equal("line2\n", cell.Source[1]);
        Assert.Equal("line3", cell.Source[2]);
    }

    [Fact]
    public void SourceText_RoundTrip_EmptyString()
    {
        var cell = new NotebookCell();
        cell.SourceText = "";
        Assert.Empty(cell.Source);
        Assert.Equal("", cell.SourceText);
    }

    [Fact]
    public void SourceText_RoundTrip_SingleLine()
    {
        var cell = new NotebookCell();
        cell.SourceText = "hello";
        Assert.Single(cell.Source);
        Assert.Equal("hello", cell.Source[0]);
        Assert.Equal("hello", cell.SourceText);
    }

    [Fact]
    public void SourceText_RoundTrip_TrailingNewline()
    {
        var cell = new NotebookCell();
        cell.SourceText = "hello\n";
        Assert.Single(cell.Source);
        Assert.Equal("hello\n", cell.Source[0]);
        Assert.Equal("hello\n", cell.SourceText);
    }

    [Fact]
    public void SourceText_Unicode_Preserved()
    {
        var cell = new NotebookCell();
        cell.SourceText = "# 分析\nα = 0.05\n🎯 target";

        Assert.Equal(3, cell.Source.Count);
        Assert.Equal("# 分析\n", cell.Source[0]);
        Assert.Equal("α = 0.05\n", cell.Source[1]);
        Assert.Equal("🎯 target", cell.Source[2]);
        Assert.Equal("# 分析\nα = 0.05\n🎯 target", cell.SourceText);
    }

    #endregion
}
