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
    public bool NotifyWarnings { get; set; } = true;
    public bool NotifyResolved { get; set; } = true;

    /// <summary>
    /// Which defaults the file was saved with, so a default that changes can reach
    /// settings saved before it did. Missing from files saved before there was one.
    /// </summary>
    public int Defaults { get; set; }

    /// <summary>The defaults now: 1 turned warnings on.</summary>
    const int CurrentDefaults = 1;

    static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaxNines");

    static string File => Path.Combine(Folder, "settings.json");

    public static Settings Load()
    {
        Settings? saved;
        try
        {
            saved = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File));
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            saved = null;
        }
        if (saved is null)
        {
            return new Settings { Defaults = CurrentDefaults };
        }
        // Saved before warnings were on to begin with, which every file then said were off.
        if (saved.Defaults < 1)
        {
            saved.NotifyWarnings = true;
        }
        saved.Defaults = CurrentDefaults;
        return saved;
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

    /// <summary>Removes the settings, as uninstalling does: their folder holds nothing else.</summary>
    public static void Forget()
    {
        if (Directory.Exists(Folder))
        {
            Directory.Delete(Folder, recursive: true);
        }
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

    /// <summary>
    /// Whether it's on, for this copy of the program. The installer writes the same,
    /// though perhaps not in the same case, which Windows doesn't mind in a path.
    /// </summary>
    public static bool IsOn
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return string.Equals(key?.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>Turns it off if it's on for this copy, leaving any other copy's alone.</summary>
    public static void Forget()
    {
        if (IsOn)
        {
            Set(false);
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
