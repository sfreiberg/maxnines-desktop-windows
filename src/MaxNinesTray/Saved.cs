using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using Windows.Security.Credentials;

namespace MaxNines.Tray;

/// <summary>The settings, apart from the key, in a file under the user's local app data.</summary>
sealed class Settings
{
    public string Address { get; set; } = "";
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

/// <summary>Keeps the API key in Windows Credential Manager, never in a file.</summary>
static class KeyStore
{
    const string Resource = "MaxNines";
    const string Account = "api-key";

    public static string? ApiKey()
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
    /// Saves the key, or forgets it when it's empty. If the new one can't be saved, the
    /// old one is kept.
    /// </summary>
    public static void SetApiKey(string key)
    {
        var vault = new PasswordVault();
        var old = ApiKey();
        try
        {
            vault.Remove(vault.Retrieve(Resource, Account));
        }
        catch (COMException)
        {
            // There wasn't one.
        }
        if (key.Length > 0)
        {
            try
            {
                vault.Add(new PasswordCredential(Resource, Account, key));
            }
            catch (COMException) when (old is not null)
            {
                vault.Add(new PasswordCredential(Resource, Account, old));
                throw;
            }
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
