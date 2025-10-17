using System.Windows;

namespace CommandEditor.App.Views;

public partial class TwitchSecretsWindow : Window
{
    public TwitchSecretsWindow()
    {
        InitializeComponent();
    }

    public string ClientId { get; private set; } = string.Empty;

    public string AccessToken { get; private set; } = string.Empty;

    public string RefreshToken { get; private set; } = string.Empty;

    public void SetSecrets(string clientId, string accessToken, string refreshToken)
    {
        ClientId = clientId ?? string.Empty;
        AccessToken = accessToken ?? string.Empty;
        RefreshToken = refreshToken ?? string.Empty;

        ClientIdBox.Text = ClientId;
        AccessTokenBox.Password = AccessToken;
        RefreshTokenBox.Password = RefreshToken;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ClientId = ClientIdBox.Text.Trim();
        AccessToken = AccessTokenBox.Password.Trim();
        RefreshToken = RefreshTokenBox.Password.Trim();

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            System.Windows.MessageBox.Show("Client ID is required!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(AccessToken))
        {
            System.Windows.MessageBox.Show("Access Token is required!", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }

    private void OnGetToken(object sender, RoutedEventArgs e)
    {
        var url = "https://twitchtokengenerator.com/quick/FWj06omw7e";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            System.Windows.MessageBox.Show($"Could not open browser. Please visit:\n{url}", "Token Generator", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
