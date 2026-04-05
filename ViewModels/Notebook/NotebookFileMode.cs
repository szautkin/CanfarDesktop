namespace CanfarDesktop.ViewModels.Notebook;

/// <summary>
/// Determines how the notebook saves: as .ipynb JSON or plain text.
/// </summary>
public enum NotebookFileMode
{
    /// <summary>Standard .ipynb notebook format.</summary>
    Notebook,

    /// <summary>.py file — single code cell, saves as plain text.</summary>
    PythonScript,

    /// <summary>.md file — single markdown cell, saves as plain text.</summary>
    Markdown,
}
