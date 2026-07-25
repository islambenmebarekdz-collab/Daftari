namespace Daftari.Tests;

/// <summary>
/// قبو مؤقت على القرص لكل اختبار، يُحذف تلقائياً في النهاية.
/// كل اختبار يحصل على قبو مستقل فلا تتداخل الاختبارات ولو عملت بالتوازي.
/// </summary>
public sealed class TempVault : IDisposable
{
    public string Root { get; }
    public Vault Vault { get; }

    public TempVault()
    {
        Root = Path.Combine(Path.GetTempPath(), "DaftariTests_" + Guid.NewGuid().ToString("N")[..10]);
        Vault = new Vault(Root);
    }

    /// <summary>ينشئ ملاحظة بمحتوى معيّن ويعيد مسارها.</summary>
    public string Note(string name, string content = "")
    {
        var path = Path.Combine(Root, name.EndsWith(".md") || name.EndsWith(NoteCrypto.Extension) ? name : name + ".md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    public string Folder(string name)
    {
        var path = Path.Combine(Root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    public string Path_(params string[] parts) => System.IO.Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
