using CanfarDesktop.Services.Database;

namespace CanfarDesktop.Services.Export;

/// <summary>
/// Exports downloaded observations + astronomer notes (JSON + markdown), keyed by publisherID so the
/// notes cross-reference the observations. Thin adapter over the stores; the rendering lives in the
/// unit-tested <see cref="ResearchExportBuilder"/>.
/// </summary>
public class ResearchExporter : IExportableModule
{
    private readonly ObservationStore _observations;
    private readonly ObservationNoteStore _notes;

    public ResearchExporter(ObservationStore observations, ObservationNoteStore notes)
    {
        _observations = observations;
        _notes = notes;
    }

    public string ModuleId => "research";
    public string DisplayName => "Research";

    public Task<ExportModuleOutput> ExportAsync(ExportOptions options)
        => Task.FromResult(ResearchExportBuilder.Build(_observations.Observations, _notes.All(), options, DateTimeOffset.UtcNow));
}
