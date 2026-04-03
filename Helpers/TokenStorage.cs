using Windows.Security.Credentials;

namespace CanfarDesktop.Helpers;

public class TokenStorage
{
    private const string ResourceName = "CanfarDesktop";
    private const string TokenKey = "AuthToken";
    private const string UsernameKey = "Username";
    private const string PasswordKey = "Password";

    public void SaveToken(string token, string username)
    {
        var vault = new PasswordVault();
        ClearToken();
        vault.Add(new PasswordCredential(ResourceName, UsernameKey, username));
        vault.Add(new PasswordCredential(ResourceName, TokenKey, token));
    }

    public void SaveCredentials(string token, string username, string password)
    {
        var vault = new PasswordVault();
        ClearToken();
        vault.Add(new PasswordCredential(ResourceName, UsernameKey, username));
        vault.Add(new PasswordCredential(ResourceName, TokenKey, token));
        vault.Add(new PasswordCredential(ResourceName, PasswordKey, password));
    }

    public (string? token, string? username) LoadToken()
    {
        var vault = new PasswordVault();
        try
        {
            var tokenCred = vault.Retrieve(ResourceName, TokenKey);
            var userCred = vault.Retrieve(ResourceName, UsernameKey);
            tokenCred.RetrievePassword();
            userCred.RetrievePassword();
            return (tokenCred.Password, userCred.Password);
        }
        catch
        {
            return (null, null);
        }
    }

    public (string? username, string? password) LoadCredentials()
    {
        var vault = new PasswordVault();
        try
        {
            var userCred = vault.Retrieve(ResourceName, UsernameKey);
            var passCred = vault.Retrieve(ResourceName, PasswordKey);
            userCred.RetrievePassword();
            passCred.RetrievePassword();
            return (userCred.Password, passCred.Password);
        }
        catch
        {
            return (null, null);
        }
    }

    public void ClearToken()
    {
        var vault = new PasswordVault();
        try
        {
            var creds = vault.FindAllByResource(ResourceName);
            foreach (var cred in creds)
                vault.Remove(cred);
        }
        catch
        {
            // No credentials found
        }
    }
}
