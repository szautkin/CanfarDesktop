namespace CanfarDesktop.ViewModels.Notebook;

using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CanfarDesktop.Models.Notebook;

/// <summary>
/// ViewModel wrapping a single cell output. Provides display-ready properties.
/// Full rendering is Milestone 5; this provides the structure.
/// </summary>
public partial class CellOutputViewModel : ObservableObject
{
    private readonly CellOutput _model;

    public CellOutput Model => _model;
    public string OutputType => _model.OutputType;

    [ObservableProperty] private string _textContent = string.Empty;
    [ObservableProperty] private bool _hasImage;
    [ObservableProperty] private string _imageBase64 = string.Empty;
    [ObservableProperty] private bool _hasHtml;
    [ObservableProperty] private string _htmlContent = string.Empty;
    [ObservableProperty] private bool _hasSvg;
    [ObservableProperty] private string _svgContent = string.Empty;
    [ObservableProperty] private bool _hasMarkdown;
    [ObservableProperty] private string _markdownContent = string.Empty;
    [ObservableProperty] private bool _hasLatex;
    [ObservableProperty] private string _latexContent = string.Empty;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _errorName = string.Empty; // "Type: message" — UI error header
    [ObservableProperty] private string _errorType = string.Empty; // just the type, e.g. "ValueError" — MCP
    [ObservableProperty] private string _traceback = string.Empty;

    public CellOutputViewModel(CellOutput model)
    {
        _model = model;
        HydrateFromModel();
    }

    private void HydrateFromModel()
    {
        switch (_model.OutputType)
        {
            case "stream":
                TextContent = _model.Text is not null ? string.Join("", _model.Text) : string.Empty;
                break;

            case "execute_result":
            case "display_data":
                // Every MIME type the model can carry, not the three that happened to be wired. A
                // library that emits SVG (astropy quantities, graphviz, plotly's static export) or LaTeX
                // (sympy, astropy tables) had its output silently dropped: the cell ran, said nothing,
                // and looked like code that produced no result.
                if (_model.Data?.TryGetValue("text/html", out var htmlData) == true)
                {
                    HasHtml = true;
                    HtmlContent = ExtractTextFromJsonElement(htmlData);
                }
                if (_model.Data?.TryGetValue("image/svg+xml", out var svgData) == true)
                {
                    HasSvg = true;
                    SvgContent = ExtractTextFromJsonElement(svgData);
                }
                if (_model.Data?.TryGetValue("text/markdown", out var markdownData) == true)
                {
                    HasMarkdown = true;
                    MarkdownContent = ExtractTextFromJsonElement(markdownData);
                }
                if (_model.Data?.TryGetValue("text/latex", out var latexData) == true)
                {
                    HasLatex = true;
                    LatexContent = ExtractTextFromJsonElement(latexData);
                }
                if (_model.Data?.TryGetValue("text/plain", out var textPlain) == true)
                    TextContent = ExtractTextFromJsonElement(textPlain);
                if (_model.Data?.TryGetValue("image/png", out var imgPng) == true)
                {
                    HasImage = true;
                    ImageBase64 = ExtractTextFromJsonElement(imgPng);
                }
                break;

            case "error":
                IsError = true;
                ErrorType = _model.Ename ?? string.Empty;         // just the type, e.g. "ValueError" (for MCP)
                ErrorName = $"{_model.Ename}: {_model.Evalue}";    // type + message, for the UI error header
                Traceback = _model.Traceback is not null
                    ? string.Join("\n", _model.Traceback)
                    : string.Empty;
                break;
        }
    }


    /// <summary>
    /// Every MIME type this output actually carries, richest first — what <c>get_cell_output</c> reports
    /// as <c>richTypes</c>.
    ///
    /// A caller that only sees text/plain cannot tell a figure from a printed number, and the plain-text
    /// fallback for a plot is the string "&lt;Figure size 640x480&gt;", which says nothing about the plot.
    /// </summary>
    public IReadOnlyList<string> RichTypes
    {
        get
        {
            if (_model.Data is not { Count: > 0 } data) return Array.Empty<string>();

            // The order libraries themselves prefer: a picture, then a rendered document, then text.
            string[] known = ["image/png", "image/svg+xml", "text/html", "text/latex", "text/markdown", "text/plain"];

            var types = known.Where(data.ContainsKey).ToList();
            types.AddRange(data.Keys.Where(k => !known.Contains(k)).OrderBy(k => k, StringComparer.Ordinal));
            return types;
        }
    }

    private static string ExtractTextFromJsonElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
            return element.GetString() ?? string.Empty;

        if (element.ValueKind == JsonValueKind.Array)
        {
            var parts = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    parts.Add(item.GetString() ?? string.Empty);
            }
            return string.Join("", parts);
        }

        return element.ToString();
    }
}
