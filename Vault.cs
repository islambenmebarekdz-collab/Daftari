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

    /// <summary>
    /// مسار غير مستخدم داخل مجلد الوجهة: يعيد الاسم كما هو إن كان حراً،
    /// وإلا يضيف لاحقة رقمية متزايدة (2، 3...) حتى يجد اسماً حراً — مضمون التفرّد بلا اعتماد على الوقت.
    /// </summary>
    static string UniqueDestination(string destFolder, string fileName)
    {
        var dest = Path.Combine(destFolder, fileName);
        if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 2; ; i++)
        {
            dest = Path.Combine(destFolder, $"{stem} {i}{ext}");
            if (!File.Exists(dest) && !Directory.Exists(dest)) return dest;
        }
    }

    static void MovePath(string source, string dest)
    {
        if (Directory.Exists(source)) Directory.Move(source, dest);
        else File.Move(source, dest);
    }

    /// <summary>ينقل ملفاً أو مجلداً إلى مجلد المحذوفات داخل القبو (حذف قابل للاسترجاع).</summary>
    public string MoveToTrash(string path)
    {
        Directory.CreateDirectory(TrashPath);
        var dest = UniqueDestination(TrashPath, Path.GetFileName(path));
        MovePath(path, dest);
        return dest;
    }

    /// <summary>
    /// بحث مرجّح: تطابق عنوان الملاحظة (اسم الملف) يتصدّر، ثم الملاحظات ذات أكثر عدد تطابقات،
    /// مع دعم البحث بالوسوم (#tag) في نفس الصندوق. النتائج مرتّبة، الأعلى ترجيحاً أولاً.
    /// </summary>
    public IEnumerable<SearchHit> Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0) yield break;
        var tagQuery = query.TrimStart('#');

        var scored = new List<(int Score, string Path, List<SearchHit> Hits)>();
        foreach (var (p, note) in Indexed())
        {
            var hits = new List<SearchHit>();
            for (int i = 0; i < note.Lines.Length; i++)
                if (note.Lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                    hits.Add(new SearchHit(p, i, note.Lines[i].Trim()));

            bool titleMatch = DisplayName(p).Contains(query, StringComparison.OrdinalIgnoreCase);
            bool tagMatch = tagQuery.Length > 0 &&
                            note.Tags.Any(t => t.Contains(tagQuery, StringComparison.OrdinalIgnoreCase));

            int bodyHits = hits.Count;
            if (bodyHits == 0 && !titleMatch && !tagMatch) continue;

            // الترجيح: العنوان يتصدّر (+1000)، ثم الوسم (+200)، ثم عدد التطابقات
            int score = bodyHits + (titleMatch ? 1000 : 0) + (tagMatch ? 200 : 0);

            // نتيجة نائبة تشير إلى الملاحظة عند تطابق العنوان أو الوسم فقط بلا تطابق في الأسطر
            if (hits.Count == 0)
            {
                var firstLine = note.Lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? DisplayName(p);
                hits.Add(new SearchHit(p, 0, firstLine));
            }
            scored.Add((score, p, hits));
        }

        foreach (var s in scored
                     .OrderByDescending(x => x.Score)
                     .ThenBy(x => DisplayName(x.Path), StringComparer.CurrentCultureIgnoreCase))
            foreach (var h in s.Hits)
                yield return h;
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

    static readonly Regex AnyWikiLink = new(@"\[\[[^\]]*\]\]", RegexOptions.Compiled);

    static Regex StandaloneName(string name) =>
        new(@"(?<![\p{L}\p{N}_])" + Regex.Escape(name) + @"(?![\p{L}\p{N}_])", RegexOptions.IgnoreCase);

    /// <summary>
    /// إشارات غير مرتبطة: ملاحظات تذكر اسم الملاحظة الحالية كنص عادٍ (خارج أي رابط [[...]]).
    /// تُستخدم لاقتراح تحويلها إلى روابط صريحة.
    /// </summary>
    public IEnumerable<SearchHit> UnlinkedMentions(string notePath)
    {
        var name = DisplayName(notePath);
        if (name.Length == 0) yield break;
        var rx = StandaloneName(name);
        int count = 0;
        foreach (var (p, note) in Indexed())
        {
            if (string.Equals(p, notePath, StringComparison.OrdinalIgnoreCase)) continue;
            for (int i = 0; i < note.Lines.Length; i++)
            {
                // نزيل نطاقات الروابط [[...]] أولاً فيبقى فقط النص العادي للفحص
                var stripped = AnyWikiLink.Replace(note.Lines[i], " ");
                if (rx.IsMatch(stripped))
                {
                    yield return new SearchHit(p, i, note.Lines[i].Trim());
                    if (++count >= 300) yield break;
                }
            }
        }
    }

    /// <summary>
    /// يحوّل أول ذكرٍ عادٍ للاسم في سطر معيّن إلى رابط [[الاسم]]، متجاوزاً ما هو داخل روابط قائمة.
    /// يعيد true عند نجاح التحويل والحفظ.
    /// </summary>
    public bool ConvertMentionToLink(string filePath, int lineNumber, string name)
    {
        string[] lines;
        try { lines = File.ReadAllText(filePath).Replace("\r\n", "\n").Split('\n'); }
        catch { return false; }
        if (lineNumber < 0 || lineNumber >= lines.Length) return false;

        var line = lines[lineNumber];
        var linkSpans = AnyWikiLink.Matches(line).Select(m => (m.Index, End: m.Index + m.Length)).ToList();
        foreach (Match m in StandaloneName(name).Matches(line))
        {
            if (linkSpans.Any(s => m.Index >= s.Index && m.Index < s.End)) continue; // داخل رابط قائم
            lines[lineNumber] = line[..m.Index] + "[[" + name + "]]" + line[(m.Index + name.Length)..];
            try
            {
                File.WriteAllText(filePath, string.Join("\r\n", lines), new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }
        return false;
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

    /// <summary>كل المجلدات داخل القبو (يشمل الجذر، ويستبعد المحذوفات والمخفية) — لوجهات النقل.</summary>
    public IEnumerable<string> AllFolders()
    {
        yield return Root;
        foreach (var d in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Root, d);
            bool excluded = false;
            foreach (var part in rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                if (part.StartsWith('.') || string.Equals(part, TrashFolderName, StringComparison.OrdinalIgnoreCase))
                { excluded = true; break; }
            if (!excluded) yield return d;
        }
    }

    /// <summary>هل يجوز نقل المصدر إلى المجلد الوجهة؟ يمنع نقل مجلد إلى نفسه أو أحد أبنائه، أو نقلٍ بلا فائدة.</summary>
    public bool CanMoveInto(string source, string destFolder, out string reason)
    {
        reason = "";
        var src = Path.GetFullPath(source);
        var dest = Path.GetFullPath(destFolder);
        if (string.Equals(Path.GetDirectoryName(src), dest, StringComparison.OrdinalIgnoreCase))
        { reason = "same"; return false; }
        if (Directory.Exists(src))
        {
            if (string.Equals(src, dest, StringComparison.OrdinalIgnoreCase) ||
                dest.StartsWith(src + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { reason = "descendant"; return false; }
        }
        return true;
    }

    /// <summary>ينقل ملاحظة أو مجلداً إلى مجلد آخر، مع لاحقة رقمية عند تعارض الأسماء. يعيد المسار الجديد.</summary>
    public string MoveTo(string source, string destFolder)
    {
        Directory.CreateDirectory(destFolder);
        var dest = UniqueDestination(destFolder, Path.GetFileName(source));
        MovePath(source, dest);
        return dest;
    }

    /// <summary>عناصر مجلد المحذوفات (ملفات ومجلدات المستوى الأول).</summary>
    public IEnumerable<string> TrashItems()
    {
        if (!Directory.Exists(TrashPath)) yield break;
        foreach (var d in Directory.EnumerateDirectories(TrashPath)) yield return d;
        foreach (var f in Directory.EnumerateFiles(TrashPath)) yield return f;
    }

    /// <summary>يعيد عنصراً محذوفاً إلى جذر القبو، مع لاحقة رقمية عند تعارض الأسماء. يعيد المسار الجديد.</summary>
    public string RestoreFromTrash(string path)
    {
        var dest = UniqueDestination(Root, Path.GetFileName(path));
        MovePath(path, dest);
        return dest;
    }

    public void DeletePermanently(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        else if (File.Exists(path)) File.Delete(path);
    }

    public void EmptyTrash()
    {
        foreach (var p in TrashItems().ToList()) DeletePermanently(p);
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
