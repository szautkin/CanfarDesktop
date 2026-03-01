using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using CanfarDesktop.ViewModels;

namespace CanfarDesktop.Views.Dialogs;

public sealed partial class LoginDialog : ContentDialog
{
    public LoginViewModel ViewModel { get; }

    public LoginDialog(LoginViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            LoginProgress.Visibility = ViewModel.IsLoggingIn ? Visibility.Visible : Visibility.Collapsed;
            ErrorBar.IsOpen = ViewModel.HasError;
            ErrorBar.Message = ViewModel.ErrorMessage;
            IsPrimaryButtonEnabled = !ViewModel.IsLoggingIn;
        });
    }

    private async void OnLoginClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();

        // Sync UI values to ViewModel
        ViewModel.Username = UsernameBox.Text;
        ViewModel.Password = PasswordBox.Password;
        ViewModel.RememberMe = RememberMeBox.IsChecked == true;

        await ViewModel.LoginCommand.ExecuteAsync(null);

        // Only close dialog on success
        if (ViewModel.HasError)
            args.Cancel = true;

        deferral.Complete();
    }
}
