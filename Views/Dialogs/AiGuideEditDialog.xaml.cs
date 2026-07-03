using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.Services.AiGuide;

namespace CanfarDesktop.Views.Dialogs;

/// <summary>
/// Add/edit a user guide tool. Validation happens on Save by calling the service; a validation failure
/// cancels the close and shows the message inline (so the user can fix the name/description and retry).
/// </summary>
public sealed partial class AiGuideEditDialog : ContentDialog
{
    private readonly AiGuideService _service;
    private readonly AiGuideToolEntry? _existing;

    public AiGuideEditDialog(AiGuideService service, AiGuideToolEntry? existing = null)
    {
        _service = service;
        _existing = existing;
        InitializeComponent();

        Title = existing is null ? Helpers.Loc.T("Guide_NewToolTitle") : Helpers.Loc.T("Guide_EditToolTitle");
        if (existing is not null)
        {
            NameBox.Text = existing.Name;
            DescriptionBox.Text = existing.Description;
            BodyBox.Text = existing.Body ?? string.Empty;
        }

        DescriptionBox.TextChanged += (_, _) => UpdateCounters();
        BodyBox.TextChanged += (_, _) => UpdateCounters();

        UpdateSlug();
        UpdateCounters();
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e) => UpdateSlug();

    private void UpdateSlug()
    {
        var slug = AiGuideService.Slug(NameBox.Text ?? string.Empty);
        SlugText.Text = slug.Length > 0
            ? Helpers.Loc.F("Guide_SlugLabel", slug)
            : Helpers.Loc.T("Guide_SlugEmpty");
    }

    private void UpdateCounters()
    {
        DescCounter.Text = $"{(DescriptionBox.Text ?? string.Empty).Trim().Length}/{AiGuideService.MaxDescriptionChars}";
        BodyCounter.Text = $"{(BodyBox.Text ?? string.Empty).Trim().Length}/{AiGuideService.MaxBodyChars}";
    }

    private void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        try
        {
            var name = NameBox.Text ?? string.Empty;
            var description = DescriptionBox.Text ?? string.Empty;
            var body = BodyBox.Text;

            if (_existing is null) _service.AddGuide(name, description, body);
            else _service.UpdateGuide(_existing.Id, name, description, body);
        }
        catch (AiGuideValidationException ex)
        {
            args.Cancel = true; // keep the dialog open so the user can fix it
            StatusBar.Message = ex.Message;
            StatusBar.IsOpen = true;
        }
        finally
        {
            deferral.Complete();
        }
    }
}
