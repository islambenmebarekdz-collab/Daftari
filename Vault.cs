using System.Text;
using System.Text.RegularExpressions;

namespace Daftari;

public record SearchHit(string FilePath, int LineNumber, string LineText);

/// <summary>
/// القبو: مجلد على القرص يحتوي ملفات Markdown. متوافق مع قبو Obsidian.
/// يحتفظ بفهرس في الذاكرة (أسطر، روابط، وسوم) مصحوباً ببصمة وقت التعديل،
/// فلا يُعاد قراءة إلا الملفات المتغيرة — البحث والروابط الواردة والوسوم شبه فورية.
/// </summary>
public class Vault
{
    public string Root { get; }
    public const string TrashFolderName = "المحذوفات";
    public string TrashPath => Path.Combine(Root, TrashFolderName);

    static readonly Regex LinkRegex = new(@"\[\[([^\]\|#]+)([#|][^\]]*)?\]\]", RegexOptions.Compiled);
    static readonly Regex TagRegex = new(@"(?<=^|[\s(])#([\p{L}\p{N}_\-/]+)", RegexOptions.Compiled);

    sealed class CachedNote
    {
        public DateTime Stamp;
        public string[] Lines = Array.Empty<string>();
        public string[] LinkTargets = Array.Empty<string>();
        public string[] Tags = Array.Empty<string>();
    }

    readonly Dictionary<string, CachedNote> cache = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>يمر على الملاحظات محدّثاً الفهرس: يقرأ من القرص الملفات المتغيرة فقط.</summary>
    IEnumerable<(string Path, CachedNote Note)> Indexed()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in AllNotes())
        {
            seen.Add(p);
            DateTime stamp;
            try { stamp = File.GetLastWriteTimeUtc(p); } catch { continue; }
            if (!cache.TryGetValue(p, out var note) || note.Stamp != stamp)
            {
                string[] lines;
                try { lines = File.ReadAllLines(p); } catch { continue; }
                note = new CachedNote
                {
                    Stamp = stamp,
                    Lines = lines,
                    LinkTargets = lines
                        .SelectMany(l => LinkRegex.Matches(l).Select(m => m.Groups[1].Value.Trim()))
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                    Tags = lines
                        .SelectMany(l => TagRegex.Matches(l).Select(m => m.Groups[1].Value))
                        .Distinct().ToArray()
                };
                cache[p] = note;
            }
            yield return (p, note);
        }
        // إسقاط ما حُذف من القرص
        foreach (var stale in cache.Keys.Where(k => !seen.Contains(k)).ToList())
            cache.Remove(stale);
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
        foreach (var (p, note) in Indexed())
            for (int i = 0; i < note.Lines.Length; i++)
                if (note.Lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    yield return new SearchHit(p, i, note.Lines[i].Trim());
    }

    /// <summary>كل الملاحظات التي تحتوي رابطاً [[...]] يشير إلى الملاحظة المعطاة.</summary>
    public IEnumerable<SearchHit> Backlinks(string notePath)
    {
        var name = DisplayName(notePath);
        foreach (var (p, note) in Indexed())
        {
            if (string.Equals(p, notePath, StringComparison.OrdinalIgnoreCase)) continue;
            // الفهرس يستبعد فوراً الملفات التي لا تشير إلى هذا الاسم أصلاً
            if (!note.LinkTargets.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            for (int i = 0; i < note.Lines.Length; i++)
            {
                foreach (Match m in LinkRegex.Matches(note.Lines[i]))
                {
                    if (string.Equals(m.Groups[1].Value.Trim(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        yield return new SearchHit(p, i, note.Lines[i].Trim());
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
        foreach (var (p, note) in Indexed())
        {
            foreach (var tag in note.Tags)
            {
                if (!result.TryGetValue(tag, out var list)) result[tag] = list = new List<string>();
                list.Add(p);
            }
        }
        return result;
    }

    /// <summary>
    /// بعد إعادة تسمية ملاحظة: يحدّث كل روابط [[الاسم القديم]] في القبو إلى الاسم الجديد،
    /// محافظاً على الأجزاء الإضافية مثل [[الاسم|نص بديل]] و[[الاسم#قسم]].
    /// يعيد عدد الروابط المحدّثة.
    /// </summary>
    public int UpdateLinks(string oldName, string newName)
    {
        var rx = new Regex(@"\[\[\s*" + Regex.Escape(oldName) + @"\s*([#|][^\]]*)?\]\]", RegexOptions.IgnoreCase);
        int total = 0;
        foreach (var (p, note) in Indexed().ToList())
        {
            // الفهرس يحصر القراءة والكتابة في الملفات التي تشير إلى الاسم القديم فقط
            if (!note.LinkTargets.Contains(oldName, StringComparer.OrdinalIgnoreCase)) continue;
            string text;
            try { text = File.ReadAllText(p); } catch { continue; }
            int count = 0;
            var newText = rx.Replace(text, m => { count++; return $"[[{newName}{m.Groups[1].Value}]]"; });
            if (count == 0) continue;
            try
            {
                File.WriteAllText(p, newText, new UTF8Encoding(false));
                total += count;
            }
            catch { }
        }
        return total;
    }
}
