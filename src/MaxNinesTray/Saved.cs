using System.Runtime.InteropServices;
using System.Text.Json;
using MaxNines.Kit;
using Microsoft.Win32;
using Windows.Security.Credentials;

namespace MaxNines.Tray;

/// <summary>The settings, apart from the tokens, in a file under the user's local app data.</summary>
sealed class Settings
{
    public string Address { get; set; } = "";

    /// <summary>Who the app was signed in as, shown until the server says again.</summary>
    public string User { get; set; } = "";

    /// <summary>The role it was signed in with, shown until the server says again.</summary>
    public string Role { get; set; } = "";

    public int IntervalSecs { get; set; } = Store.Intervals[0];
    public bool NotifyCritical { get; set; } = true;
    public bool NotifyWarnings { get; set; }
    public bool NotifyResolved { get; set; } = true;

    static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaxNines");

    static string File => Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File)) ?? new Settings();
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public Settings Copy() => (Settings)MemberwiseClone();

    public void Save()
    {
        Directory.CreateDirectory(Folder);
        // Written whole and then moved into place, so a crash never leaves half a file.
        var partial = File + ".new";
        System.IO.File.WriteAllText(partial, JsonSerializer.Serialize(this));
        System.IO.File.Move(partial, File, overwrite: true);
    }
}

/// <summary>Keeps the sign-in's refresh token in Windows Credential Manager, never in a file.</summary>
sealed class KeyStore : IRefreshTokenStore
{
    const string Resource = "MaxNines";
    const string Account = "refresh-token";

    /// <summary>Where the API key was kept before the app signed in through the browser.</summary>
    const string OldApiKey = "api-key";

    public string? Load()
    {
        try
        {
            var credential = new PasswordVault().Retrieve(Resource, Account);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch (COMException)
        {
            // There isn't one.
            return null;
        }
    }

    /// <summary>
    /// Keeps the newest refresh token, or forgets it when it's null. The one before is
    /// never put back if this fails, as the server no longer takes it.
    /// </summary>
    public void Save(string? refreshToken)
    {
        var vault = new PasswordVault();
        Remove(vault, Account);
        if (refreshToken is not null)
        {
            vault.Add(new PasswordCredential(Resource, Account, refreshToken));
        }
    }

    /// <summary>Forgets the API key from before the app signed in through the browser.</summary>
    public static void ForgetApiKey() => Remove(new PasswordVault(), OldApiKey);

    static void Remove(PasswordVault vault, string account)
    {
        try
        {
            vault.Remove(vault.Retrieve(Resource, account));
        }
        catch (COMException)
        {
            // There wasn't one.
        }
    }
}

/// <summary>Starting when the user signs in, through their Run key.</summary>
static class OpenAtLogin
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "MaxNines";

    static string Command => $"\"{Environment.ProcessPath}\"";

    /// <summary>Whether it's on, for this copy of the program.</summary>
    public static bool IsOn
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) as string == Command;
        }
    }

    public static void Set(bool on)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on)
        {
            key.SetValue(Name, Command);
        }
        else
        {
            key.DeleteValue(Name, throwOnMissingValue: false);
        }
    }
}
