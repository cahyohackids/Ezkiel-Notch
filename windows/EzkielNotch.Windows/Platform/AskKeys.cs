using System.IO;
using System.Security.Cryptography;
using System.Text;
using EzkielNotch.Core;

namespace EzkielNotch.Platform;

/// <summary>Where Ask's API key lives. Never data.json, never the log.</summary>
public interface IAskKeys
{
    string? Read();
    bool Save(string key);
    void Remove();
}

/// <summary>The user's own key for api.ezkielyna.store, sealed with DPAPI for this Windows account
/// (%APPDATA%\EzkielNotch\ask-key.bin): another account, or the file copied to another PC, can't read it.</summary>
public sealed class DpapiAskKeys(string? path = null) : IAskKeys
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("EzkielNotch.Ask");
    private readonly string _path = path ?? System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EzkielNotch", "ask-key.bin");

    public string? Read()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception)
        {
            return null;   // another account's file or a damaged one: as if there were no key
        }
    }

    public bool Save(string key)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
            var protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), Entropy, DataProtectionScope.CurrentUser);
            var temp = _path + ".tmp";
            File.WriteAllBytes(temp, protectedBytes);
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Remove()
    {
        try { File.Delete(_path); }
        catch (Exception) { }
    }
}

/// <summary>The self-test's key store: memory only, so it never reads or writes the user's key.</summary>
public sealed class MemoryAskKeys : IAskKeys
{
    private string? _key;
    public string? Read() => _key;
    public bool Save(string key) { _key = key; return true; }
    public void Remove() => _key = null;
}

/// <summary>Ask for the whole app: the key (read once, when Ask is first used) and this session's questions.
/// Changed fires after every change of either.</summary>
public static class AskHub
{
    public static IAskKeys Keys { get; private set; } = new DpapiAskKeys();
    public static AskSession Session { get; private set; } = new(new AskClient());
    public static event Action? Changed;
    /// <summary>What's typed in the ask box but not asked yet (it survives closing the box).</summary>
    public static string Draft { get; set; } = "";

    private static bool _loaded;
    private static string? _key;

    static AskHub() => Session.Changed += Raise;

    private static void Raise() => Changed?.Invoke();

    public static string? Key
    {
        get
        {
            if (!_loaded)
            {
                _key = Keys.Read();
                _loaded = true;
            }
            return _key;
        }
    }

    public static bool HasKey => !string.IsNullOrEmpty(Key);

    /// <summary>The key with only its last 4 characters showing.</summary>
    public static string Masked => Key is { Length: > 0 } k ? new string('•', 8) + k[^Math.Min(4, k.Length)..] : "";

    public static bool SaveKey(string raw)
    {
        var key = Ask.CleanKey(raw);
        if (key.Length == 0 || !Keys.Save(key)) return false;
        _key = key;
        _loaded = true;
        // "Add your key" (or "That key didn't work") with nothing answered yet: the question goes back in the box.
        if (Session is { Busy: false, Answer.Length: 0, Error.NeedsSettings: true })
        {
            if (Draft.Length == 0) Draft = Session.Question;
            Session.Clear();
        }
        Session.ForgetKeyCheck();
        return true;
    }

    public static void RemoveKey()
    {
        Keys.Remove();
        _key = null;
        _loaded = true;
        Session.ForgetKeyCheck();
    }

    public static Task TestKey() => Session.TestKey(Key);

    /// <summary>The self-test's fresh start: another key store and transport, nothing remembered.</summary>
    public static void Reset(IAskKeys keys, AskClient client)
    {
        Session.Stop();
        Session.Changed -= Raise;
        Keys = keys;
        _key = null;
        _loaded = false;
        Draft = "";
        Session = new AskSession(client);
        Session.Changed += Raise;
        Raise();
    }
}
