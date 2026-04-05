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
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private string _errorName = string.Empty;
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
                // Prefer HTML for rich display (pandas DataFrames, styled output)
                if (_model.Data?.TryGetValue("text/html", out var htmlData) == true)
                {
                    HasHtml = true;
                    HtmlContent = ExtractTextFromJsonElement(htmlData);
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
                ErrorName = $"{_model.Ename}: {_model.Evalue}";
                Traceback = _model.Traceback is not null
                    ? string.Join("\n", _model.Traceback)
                    : string.Empty;
                break;
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
