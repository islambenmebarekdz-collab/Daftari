using System.Text;
using System.Text.RegularExpressions;

namespace Daftari;

public record SearchHit(string FilePath, int LineNumber, string LineText);

/// <summary>
/// القبو: مجلد على القرص يحتوي ملفات Markdown. متوافق مع قبو Obsidian.
/// </summary>
public class Vault
{
    public string Root { get; }
    public const string TrashFolderName = "المحذوفات";
    public string TrashPath => Path.Combine(Root, TrashFolderName);

    static readonly Regex LinkRegex = new(@"\[\[([^\]\|#]+)([#|][^\]]*)?\]\]", RegexOptions.Compiled);
    static readonly Regex TagRegex = new(@"(?<=^|[\s(])#([\p{L}\p{N}_\-/]+)", RegexOptions.Compiled);

    public Vault(string root)
    {
        Root = root;
        Directory.CreateDirectory(root);
    }

    public IEnumerable<string> AllNotes() =>
        Directory.EnumerateFiles(Root, "*.md", SearchOption.AllDirectories)
            .Where(p => !IsExcluded(p));

    bool IsExcluded(string path)
    {
        var rel = Path.GetRelativePath(Root, path);
        foreach (var part in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            if (part.StartsWith('.')) return true;
            if (string.Equals(part, TrashFolderName, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public string DisplayName(string path) => Path.GetFileNameWithoutExtension(path);

    public string RelativeName(string path)
    {
        var rel = Path.GetRelativePath(Root, path);
        return rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? rel[..^3] : rel;
    }

    /// <summary>حل رابط ويكي إلى مسار ملف: بالاسم أولاً ثم بالمسار النسبي.</summary>
    public string? ResolveLink(string target)
    {
        target = target.Trim();
        foreach (var p in AllNotes())
            if (string.Equals(DisplayName(p), target, StringComparison.OrdinalIgnoreCase))
                return p;
        var normalized = target.Replace('\\', '/');
        foreach (var p in AllNotes())
            if (string.Equals(RelativeName(p).Replace('\\', '/'), normalized, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }

    public static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '-');
        return name.Trim();
    }

    public string CreateNote(string folder, string name, string content = "")
    {
        var safe = Sanitize(name);
        if (safe.Length == 0) safe = "ملاحظة جديدة";
        var path = Path.Combine(folder, safe + ".md");
        int i = 2;
        while (File.Exists(path)) { path = Path.Combine(folder, $"{safe} {i}.md"); i++; }
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    /// <summary>ينقل ملفاً أو مجلداً إلى مجلد المحذوفات داخل القبو (حذف قابل للاسترجاع).</summary>
    public string MoveToTrash(string path)
    {
        Directory.CreateDirectory(TrashPath);
        var dest = Path.Combine(TrashPath, Path.GetFileName(path));
        if (File.Exists(dest) || Directory.Exists(dest))
            dest = Path.Combine(TrashPath,
                $"{Path.GetFileNameWithoutExtension(path)} {DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
        if (Directory.Exists(path)) Directory.Move(path, dest);
        else File.Move(path, dest);
        return dest;
    }

    public IEnumerable<SearchHit> Search(string query)
    {
        foreach (var p in AllNotes())
        {
            string[] lines;
            try { lines = File.ReadAllLines(p); } catch { continue; }
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    yield return new SearchHit(p, i, lines[i].Trim());
        }
    }

    /// <summary>كل الملاحظات التي تحتوي رابطاً [[...]] يشير إلى الملاحظة المعطاة.</summary>
    public IEnumerable<SearchHit> Backlinks(string notePath)
    {
        var name = DisplayName(notePath);
        foreach (var p in AllNotes())
        {
            if (string.Equals(p, notePath, StringComparison.OrdinalIgnoreCase)) continue;
            string[] lines;
            try { lines = File.ReadAllLines(p); } catch { continue; }
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match m in LinkRegex.Matches(lines[i]))
                {
                    if (string.Equals(m.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new SearchHit(p, i, lines[i].Trim());
                        goto nextLine;
                    }
                }
            nextLine:;
            }
        }
    }

    /// <summary>كل الوسوم (#وسم) في القبو مع الملاحظات الحاوية لكل وسم.</summary>
    public SortedDictionary<string, List<string>> AllTags()
    {
        var result = new SortedDictionary<string, List<string>>(StringComparer.CurrentCultureIgnoreCase);
        foreach (var p in AllNotes())
        {
            string text;
            try { text = File.ReadAllText(p); } catch { continue; }
            foreach (Match m in TagRegex.Matches(text))
            {
                var tag = m.Groups[1].Value;
                if (!result.TryGetValue(tag, out var list)) result[tag] = list = new List<string>();
                if (!list.Contains(p)) list.Add(p);
            }
        }
        return result;
    }
}
