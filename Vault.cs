using System.Text;
using System.Text.RegularExpressions;

namespace Daftari;

public record SearchHit(string FilePath, int LineNumber, string LineText);

/// <summary>مهمة داخل ملاحظة: سطر بصيغة قائمة مهام في Markdown.</summary>
public record TaskItem(string FilePath, int LineNumber, string Text, bool Done);

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
    public const string TemplatesFolderName = "قوالب";
    public string TemplatesPath => Path.Combine(Root, TemplatesFolderName);

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

    /// <summary>
    /// محتوى الملاحظات المقفلة التي فُتحت في هذه الجلسة، في الذاكرة فقط ولا يُكتب على القرص.
    /// يجعلها تظهر في البحث والمهام والروابط ما دامت مفتوحة، وتختفي منها فور إقفال الجلسة.
    /// </summary>
    readonly Dictionary<string, string> unlockedContents = new(StringComparer.OrdinalIgnoreCase);

    public void SetUnlockedContent(string path, string text) => unlockedContents[path] = text;
    public void ForgetUnlockedContent(string path) => unlockedContents.Remove(path);
    public void ForgetAllUnlocked() => unlockedContents.Clear();

    /// <summary>يمر على الملاحظات محدّثاً الفهرس: يقرأ من القرص الملفات المتغيرة فقط.</summary>
    IEnumerable<(string Path, CachedNote Note)> Indexed()
    {
        // الملاحظات المقفلة المفتوحة في الجلسة تُفهرس من الذاكرة لا من القرص
        foreach (var kv in unlockedContents)
        {
            if (!File.Exists(kv.Key)) continue;
            var lines = kv.Value.Replace("\r\n", "\n").Split('\n');
            yield return (kv.Key, new CachedNote
            {
                Stamp = DateTime.MinValue,
                Lines = lines,
                LinkTargets = lines.SelectMany(l => LinkRegex.Matches(l).Select(m => m.Groups[1].Value.Trim()))
                                   .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                Tags = lines.SelectMany(l => TagRegex.Matches(l).Select(m => m.Groups[1].Value))
                            .Distinct().ToArray(),
            });
        }

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

    /// <summary>اسم العرض: يزيل الامتداد، ويزيل امتدادَي الملاحظة المقفلة (.md.enc) معاً.</summary>
    public string DisplayName(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(NoteCrypto.Extension, StringComparison.OrdinalIgnoreCase))
            return name[..^NoteCrypto.Extension.Length];
        return Path.GetFileNameWithoutExtension(name);
    }

    public string RelativeName(string path)
    {
        var rel = Path.GetRelativePath(Root, path);
        if (rel.EndsWith(NoteCrypto.Extension, StringComparison.OrdinalIgnoreCase))
            return rel[..^NoteCrypto.Extension.Length];
        return rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? rel[..^3] : rel;
    }

    /// <summary>الملاحظات المقفلة (ملفات مشفّرة) — تظهر في الشجرة لكنها خارج الفهرس والبحث.</summary>
    public IEnumerable<string> EncryptedNotes() =>
        Directory.EnumerateFiles(Root, "*" + NoteCrypto.Extension, SearchOption.AllDirectories)
            .Where(p => !IsExcluded(p));

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
    /// بحث مرجّح متعدد الكلمات: تُقسَّم العبارة إلى كلمات، وتُقبل الملاحظة إن احتوت **كل**
    /// الكلمات (في عنوانها أو وسومها أو نصها) ولو في أسطر متفرقة. الترجيح: العنوان أولاً،
    /// ثم العبارة الكاملة، ثم الوسوم، ثم عدد الأسطر المطابقة. النتائج مرتّبة، الأعلى أولاً.
    /// </summary>
    public IEnumerable<SearchHit> Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0) yield break;
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) yield break;

        const StringComparison Cmp = StringComparison.OrdinalIgnoreCase;
        bool MatchesTag(CachedNote n, string term)
        {
            var tag = term.TrimStart('#');
            return tag.Length > 0 && n.Tags.Any(x => x.Contains(tag, Cmp));
        }

        var scored = new List<(int Score, string Path, List<SearchHit> Hits)>();
        foreach (var (p, note) in Indexed())
        {
            var title = DisplayName(p);

            // شرط القبول: كل كلمة موجودة في مكان ما من الملاحظة
            bool allPresent = terms.All(t =>
                title.Contains(t, Cmp) || MatchesTag(note, t) || note.Lines.Any(l => l.Contains(t, Cmp)));
            if (!allPresent) continue;

            var hits = new List<SearchHit>();
            for (int i = 0; i < note.Lines.Length; i++)
                if (terms.Any(t => note.Lines[i].Contains(t, Cmp)))
                    hits.Add(new SearchHit(p, i, note.Lines[i].Trim()));

            int score = hits.Count;
            if (terms.All(t => title.Contains(t, Cmp))) score += 2000;        // العنوان يحوي كل الكلمات
            else if (terms.Any(t => title.Contains(t, Cmp))) score += 1000;   // العنوان يحوي بعضها
            if (terms.Any(t => MatchesTag(note, t))) score += 200;
            // العبارة كاملة متجاورة أقوى من كلمات متفرقة
            if (terms.Length > 1 &&
                (title.Contains(query, Cmp) || note.Lines.Any(l => l.Contains(query, Cmp))))
                score += 500;

            // نتيجة نائبة عند تطابق العنوان أو الوسم فقط بلا تطابق في الأسطر
            if (hits.Count == 0)
            {
                var firstLine = note.Lines.FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? title;
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

    /// <summary>
    /// الروابط الصادرة من ملاحظة: كل [[هدف]] بترتيب ظهوره، مع رقم سطره ومسار الملاحظة الهدف
    /// (null إن كانت غير موجودة بعد). تُعرض للتنقل والفتح المباشر.
    /// </summary>
    public IEnumerable<(string Target, int LineNumber, string? Path)> OutgoingLinks(string notePath)
    {
        string[] lines;
        try { lines = File.ReadAllText(notePath).Replace("\r\n", "\n").Split('\n'); }
        catch { yield break; }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < lines.Length; i++)
            foreach (Match m in LinkRegex.Matches(lines[i]))
            {
                var target = m.Groups[1].Value.Trim();
                if (target.Length == 0 || !seen.Add(target)) continue;
                yield return (target, i, ResolveLink(target));
            }
    }

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

    /// <summary>القوالب: ملاحظات في مجلد «قوالب» تُنشأ منها ملاحظات جديدة.</summary>
    public IEnumerable<string> Templates() =>
        Directory.Exists(TemplatesPath)
            ? Directory.EnumerateFiles(TemplatesPath, "*.md")
                .OrderBy(p => DisplayName(p), StringComparer.CurrentCultureIgnoreCase)
            : Enumerable.Empty<string>();

    public bool IsTemplate(string path) =>
        Path.GetFullPath(path).StartsWith(Path.GetFullPath(TemplatesPath) + Path.DirectorySeparatorChar,
                                          StringComparison.OrdinalIgnoreCase);

    /// <summary>يبحث عن قالب اليوميات بالاسم المتعارف عليه بالعربية أو الإنجليزية.</summary>
    public string? DailyTemplate() =>
        Templates().FirstOrDefault(p =>
            DisplayName(p).Equals("يومية", StringComparison.OrdinalIgnoreCase) ||
            DisplayName(p).Equals("Daily", StringComparison.OrdinalIgnoreCase));

    /// <summary>علامة داخلية لموضع المؤشر، تُزال من النص بعد تحديد موضعها.</summary>
    const string CursorMarker = "CURSOR";

    static readonly Regex TemplateVariable = new(@"\{\{\s*([^}\r\n]+?)\s*\}\}", RegexOptions.Compiled);

    /// <summary>
    /// يملأ متغيّرات القالب: العنوان والتاريخ والوقت، ويحدّد موضع المؤشر إن وُجدت علامته.
    /// المتغيّرات غير المعروفة تبقى كما هي فلا يضيع نص المستخدم.
    /// caretOffset يساوي -1 إن لم يحدد القالب موضعاً للمؤشر.
    /// </summary>
    public static string ApplyTemplate(string content, string title, DateTime now, string dateText, out int caretOffset)
    {
        var text = TemplateVariable.Replace(content, m => m.Groups[1].Value.Trim().ToLowerInvariant() switch
        {
            "العنوان" or "title" => title,
            "التاريخ" or "date" => dateText,
            "الوقت" or "time" => now.ToString("HH:mm"),
            "التاريخ_رقمي" or "isodate" => now.ToString("yyyy-MM-dd"),
            "المؤشر" or "cursor" => CursorMarker,
            _ => m.Value,
        });

        caretOffset = text.IndexOf(CursorMarker, StringComparison.Ordinal);
        if (caretOffset >= 0) text = text.Remove(caretOffset, CursorMarker.Length);
        return text;
    }

    // صيغة المهام في Markdown: "- [ ] نص" أو "- [x] نص" مع أي مسافة بادئة وأي علامة قائمة
    static readonly Regex TaskLine = new(@"^(\s*)([-*+])\s+\[([ xX])\]\s?(.*)$", RegexOptions.Compiled);
    static readonly Regex BulletLine = new(@"^(\s*)([-*+])\s+(.*)$", RegexOptions.Compiled);

    /// <summary>
    /// يقلب حالة سطر مهمة: مفتوحة تصير منجزة والعكس. والسطر العادي أو عنصر القائمة
    /// يتحوّل إلى مهمة مفتوحة، فتُنشأ المهام بالاختصار نفسه دون كتابة الأقواس يدوياً.
    /// </summary>
    public static string ToggleTaskLine(string line, out bool isDone)
    {
        var task = TaskLine.Match(line);
        if (task.Success)
        {
            isDone = task.Groups[3].Value is not ("x" or "X");
            return $"{task.Groups[1].Value}{task.Groups[2].Value} [{(isDone ? "x" : " ")}] {task.Groups[4].Value}";
        }

        isDone = false;
        var bullet = BulletLine.Match(line);
        if (bullet.Success)
            return $"{bullet.Groups[1].Value}{bullet.Groups[2].Value} [ ] {bullet.Groups[3].Value}";

        var indent = line.Length - line.TrimStart().Length;
        return line[..indent] + "- [ ] " + line.TrimStart();
    }

    /// <summary>كل المهام في القبو مرتّبةً بالملاحظة ثم السطر؛ المفتوحة فقط ما لم يُطلب غيرها.</summary>
    public IEnumerable<TaskItem> Tasks(bool includeDone = false)
    {
        var found = new List<TaskItem>();
        foreach (var (p, note) in Indexed())
        {
            if (IsTemplate(p)) continue;      // مهام القالب نموذج لا عمل حقيقي
            for (int i = 0; i < note.Lines.Length; i++)
            {
                var m = TaskLine.Match(note.Lines[i]);
                if (!m.Success) continue;
                bool done = m.Groups[3].Value is "x" or "X";
                if (done && !includeDone) continue;
                var text = m.Groups[4].Value.Trim();
                if (text.Length == 0) continue;      // مربّع فارغ بلا نص ليس مهمة
                found.Add(new TaskItem(p, i, text, done));
            }
        }
        return found
            .OrderBy(t => DisplayName(t.FilePath), StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.LineNumber);
    }

    /// <summary>يضبط حالة مهمة في ملف على القرص مباشرة. يعيد false إن لم يكن السطر مهمة.</summary>
    public bool SetTaskDone(string filePath, int lineNumber, bool done)
    {
        string[] lines;
        try { lines = File.ReadAllText(filePath).Replace("\r\n", "\n").Split('\n'); }
        catch { return false; }
        if (lineNumber < 0 || lineNumber >= lines.Length) return false;

        var m = TaskLine.Match(lines[lineNumber]);
        if (!m.Success) return false;
        lines[lineNumber] = $"{m.Groups[1].Value}{m.Groups[2].Value} [{(done ? "x" : " ")}] {m.Groups[4].Value}";
        try
        {
            File.WriteAllText(filePath, string.Join("\r\n", lines), new UTF8Encoding(false));
            return true;
        }
        catch { return false; }
    }

    /// <summary>
    /// استبدال كل تطابقات نص بآخر تجاهلاً لحالة الأحرف، مع إرجاع عدد التطابقات.
    /// دالة نقية منفصلة عن الواجهة كي تكون قابلة للاختبار.
    /// </summary>
    public static string ReplaceAllOccurrences(string text, string find, string replacement, out int count)
    {
        count = 0;
        if (string.IsNullOrEmpty(find)) return text;

        var sb = new StringBuilder();
        int at = 0;
        while (true)
        {
            int j = text.IndexOf(find, at, StringComparison.CurrentCultureIgnoreCase);
            if (j < 0) break;
            sb.Append(text, at, j - at).Append(replacement);
            at = j + find.Length;
            count++;
        }
        if (count == 0) return text;
        sb.Append(text, at, text.Length - at);
        return sb.ToString();
    }

    /// <summary>
    /// يحفظ نصاً كملف جديد بجوار ملاحظة، باسم مشتق منها بلا تعارض.
    /// يُستخدم لحفظ نسخة المستخدم عند تعارضها مع تعديل خارجي. يعيد المسار الجديد.
    /// </summary>
    public string SaveSideCopy(string notePath, string suffix, string content)
    {
        var dir = Path.GetDirectoryName(notePath)!;
        var fileName = $"{Path.GetFileNameWithoutExtension(notePath)} {suffix}.md";
        var dest = UniqueDestination(dir, Sanitize(fileName));
        File.WriteAllText(dest, content, new UTF8Encoding(false));
        return dest;
    }

    /// <summary>الملاحظات مرتّبةً بالأحدث تعديلاً — لاستئناف العمل حيث توقّف.</summary>
    public IEnumerable<string> RecentlyModified(int max = 30)
    {
        DateTime When(string p) { try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; } }
        return AllNotes().Concat(EncryptedNotes()).OrderByDescending(When).Take(max);
    }

    /// <summary>النسخ الاحتياطية المتاحة في مجلد النسخ، الأحدث أولاً.</summary>
    public static IEnumerable<(string Path, DateTime When, long Size)> BackupsIn(string folder)
    {
        if (!Directory.Exists(folder)) yield break;
        var items = new List<(string Path, DateTime When, long Size)>();
        foreach (var f in Directory.EnumerateFiles(folder, "Daftari-*.zip"))
        {
            FileInfo info;
            try { info = new FileInfo(f); } catch { continue; }
            items.Add((f, info.LastWriteTime, info.Length));
        }
        foreach (var i in items.OrderByDescending(i => i.When)) yield return i;
    }

    /// <summary>
    /// يفك ضغط نسخة احتياطية في مجلد جديد بجوار الوجهة المطلوبة — لا يكتب فوق قبو قائم أبداً.
    /// يعيد مسار المجلد المستعاد.
    /// </summary>
    public static string RestoreBackupTo(string zipPath, string parentFolder, string name)
    {
        Directory.CreateDirectory(parentFolder);
        var dest = Path.Combine(parentFolder, Sanitize(name));
        int i = 2;
        while (Directory.Exists(dest) || File.Exists(dest))
            dest = Path.Combine(parentFolder, Sanitize($"{name} {i++}"));
        Directory.CreateDirectory(dest);
        System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, dest);
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
