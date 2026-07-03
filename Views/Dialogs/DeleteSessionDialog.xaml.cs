using Microsoft.UI.Xaml.Controls;

namespace CanfarDesktop.Views.Dialogs;

public sealed partial class DeleteSessionDialog : ContentDialog
{
    public string SessionName { get; set; } = "";

    public DeleteSessionDialog()
    {
        InitializeComponent();
    }

    public void SetSessionName(string name)
    {
        SessionName = name;
        MessageText.Text = Helpers.Loc.F("Sessions_DeleteConfirm", name);
    }
}
