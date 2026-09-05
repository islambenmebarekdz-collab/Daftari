using System.Text;
namespace Daftari.Cli;

/// <summary>
/// تنفيذ أوامر الطرفية على قبوٍ مفتوح.
///
/// <para>أربعة قيود تحكم هذا الملف:</para>
/// <list type="bullet">
/// <item>لا منطق جديد — كل أمرٍ سطرُ ربطٍ فوق دالّةٍ مختبَرة في <see cref="Vault"/>،
/// فيبقى ترجيح البحث وحلّ الروابط واحداً في الواجهة والطرفية معاً.</item>
/// <item>الملاحظات المقفلة خارج المحتوى بحكم البنية: <c>AllNotes()</c> تعدّ ملفات
/// <c>.md</c> وحدها، وذاكرة الجلسة المفتوحة لا وجود لها في عملية الطرفية أصلاً.
/// فلا يُطبع من المقفلة إلا اسمُها في <c>index</c>، ولا تُكتب البتّة.</item>
/// <item>الكتابة لا تُتلف: إنشاءٌ يُرقَّم عند التعارض، وإلحاقٌ لا يمسّ ما قبله،
/// واستبدالُ نطاقٍ يعيد ما أزاله قبل إزالته. ولا حذف — حذف الملاحظات يبقى في
/// التطبيق بيد صاحب القبو. ولا كتابة خارج القبو مهما كان المسار الممرَّر.</item>
/// <item>كل كتابةٍ تُسجَّل في سجلّ القبو، فيكون ما فعلته الأداة معلوماً لا مظنوناً.</item>
/// </list>
///
/// <para>المخرَج يُكتب في <see cref="TextWriter"/> لا في الطرفية مباشرة، ليكون قابلاً للاختبار.</para>
/// </summary>
public static class Commands
{
    /// <summary>نجح الأمر ووجد ما يعرضه.</summary>
    public const int Found = 0;
    /// <summary>نجح الأمر ولم يجد شيئاً — يميّزه رمز الخروج فلا يحتاج تحليل النص.</summary>
    public const int NothingFound = 1;
    /// <summary>خطأ في الاستعمال: أمر مجهول أو وسائط ناقصة.</summary>
    public const int UsageError = 2;

    /// <summary>علامة الملاحظة المقفلة في <c>index</c> — اسمها يظهر ومحتواها لا.</summary>
    public const string LockedMark = "[مقفلة]";
    /// <summary>علامة الرابط الصادر الذي لا يشير إلى ملاحظة موجودة.</summary>
    public const string BrokenLinkMark = "[مكسور]";

    public const string Usage = """
        دفتري — واجهة نصّية على القبو: قراءةٌ وكتابةٌ محروسة

        الاستعمال: daftari [--vault <مسار>] <أمر> [وسائط]

          search <كلمات...>   بحثٌ مرجّح في المحتوى والعناوين والوسوم (كل الكلمات لازمة)
                              --files يعطي الملاحظات وعدد مطابقاتها بلا أسطرها
          links <اسم>         الروابط الواردة إلى الملاحظة والصادرة منها
          links --broken      كل الروابط المكسورة في القبو كله
          index               كل الملاحظات بوقت آخر تعديل، الأحدث أولاً
          resolve <اسم>       يحلّ اسم رابط [[...]] إلى مسار ملف كامل
          show <اسم>          يطبع محتوى الملاحظة باسم رابطها
          tasks [--all] [--folder <مجلد>]   المهام غير المنجزة (--all يشمل المنجزة)
          tags                الوسوم وملاحظات كل وسم
          renames [اسم]       سجلّ إعادة التسمية والحذف وما كتبته الأداة، الأحدث أولاً

        الكتابة (النصّ من الدخل القياسي، وكلّها تُسجَّل في السجلّ):

          new <اسم> [--folder <مجلد>]      ملاحظة جديدة (تُرقَّم إن تعارض الاسم)
          append <اسم>                     يُلحق نصّاً بآخرها
          edit <اسم> --lines n[-m]         يستبدل نطاق أسطر، ودخلٌ فارغ يشطبه
          task <اسم> --line n [--undo]     يعلّم مهمةً منجزة أو يعيدها
          move <اسم> --to <مجلد>           ينقلها إلى مجلد

        لا حذف: حذف الملاحظات يبقى في التطبيق بيد صاحب القبو.
        والملاحظات المقفلة محجوبةٌ عن القراءة والكتابة جميعاً.

        رموز الخروج: 0 وُجد، 1 لا شيء، 2 خطأ استعمال
        """;

    public static int Run(Vault vault, string[] args, TextWriter output, TextReader? input = null)
    {
        if (args.Length == 0) { output.WriteLine(Usage); return UsageError; }

        var rest = args.Skip(1).ToArray();
        return args[0] switch
        {
            "new" => New(vault, rest, output, input),
            "append" => AppendCmd(vault, rest, output, input),
            "edit" => Edit(vault, rest, output, input),
            "task" => Task(vault, rest, output),
            "move" => Move(vault, rest, output),
            "search" => Search(vault, rest, output),
            "links" => Links(vault, rest, output),
            "index" => Index(vault, output),
            "resolve" => Resolve(vault, rest, output),
            "show" => Show(vault, rest, output),
            "tasks" => Tasks(vault, rest, output),
            "tags" => Tags(vault, output),
            "renames" => Renames(vault, rest, output),
            "help" or "--help" or "-h" => Help(output),
            _ => Help(output, UsageError),
        };
    }

    static int Help(TextWriter output, int code = Found)
    {
        output.WriteLine(Usage);
        return code;
    }

    /// <summary>موضعٌ في القبو بصيغة يفهمها المحرر والعين معاً: <c>الاسم:السطر: النص</c>.</summary>
    static string At(Vault vault, string path, int zeroBasedLine, string text) =>
        $"{vault.RelativeName(path)}:{zeroBasedLine + 1}: {text}";

    static int Search(Vault vault, string[] args, TextWriter output)
    {
        bool filesOnly = args.Contains("--files");
        var terms = args.Where(a => a != "--files").ToArray();
        if (terms.Length == 0) return Help(output, UsageError);

        var hits = vault.Search(string.Join(' ', terms));

        // بحثٌ واسع في قبوٍ حقيقي يُخرج آلاف الأحرف حين يعود كل سطرٍ مطابق كاملاً.
        // ‎--files‎ يجيب أولاً عن «أيّ الملاحظات» فيُنزَل إلى واحدة بعدها — كما يفرّق
        // grep بين ‎-l‎ وبحثه المعتاد. الترتيب هو ترتيب الترجيح نفسه، لأنّ مطابقات
        // الملاحظة الواحدة تأتي متجاورة، فالظهور الأول يحفظ الرتبة.
        if (filesOnly)
        {
            var perNote = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();
            foreach (var hit in hits)
            {
                if (!perNote.ContainsKey(hit.FilePath)) { perNote[hit.FilePath] = 0; order.Add(hit.FilePath); }
                perNote[hit.FilePath]++;
            }
            foreach (var path in order)
                output.WriteLine($"{perNote[path]}\t{vault.RelativeName(path)}");
            return order.Count > 0 ? Found : NothingFound;
        }

        int count = 0;
        foreach (var hit in hits)
        {
            output.WriteLine(At(vault, hit.FilePath, hit.LineNumber, hit.LineText));
            count++;
        }
        return count > 0 ? Found : NothingFound;
    }

    /// <summary>
    /// كل الروابط المكسورة في القبو. <c>links</c> على ملاحظةٍ واحدة لا يجيب سؤال
    /// «أين كلّ المكسور؟»، وهو السؤال الذي كان سيكشف أنّ الروابط داخل علامات
    /// الشيفرة تُعدّ روابط، بدل العثور عليه مصادفةً.
    /// </summary>
    static int BrokenLinks(Vault vault, TextWriter output)
    {
        int count = 0;
        foreach (var note in vault.AllNotes())
            foreach (var (target, line, path) in vault.OutgoingLinks(note))
                if (path == null)
                {
                    output.WriteLine(At(vault, note, line, $"[[{target}]] ← {BrokenLinkMark}"));
                    count++;
                }
        return count > 0 ? Found : NothingFound;
    }

    static int Links(Vault vault, string[] args, TextWriter output)
    {
        if (args.Contains("--broken")) return BrokenLinks(vault, output);
        if (args.Length == 0) return Help(output, UsageError);

        var name = string.Join(' ', args);
        var path = vault.ResolveLink(name);
        if (path == null)
        {
            output.WriteLine($"لا توجد ملاحظة بالاسم: {name}");
            return NothingFound;
        }

        var incoming = vault.Backlinks(path).ToList();
        output.WriteLine($"الروابط الواردة ({incoming.Count}):");
        foreach (var hit in incoming)
            output.WriteLine("  " + At(vault, hit.FilePath, hit.LineNumber, hit.LineText));

        var outgoing = vault.OutgoingLinks(path).ToList();
        output.WriteLine($"الروابط الصادرة ({outgoing.Count}):");
        foreach (var (target, line, targetPath) in outgoing)
            output.WriteLine("  " + At(vault, path, line,
                $"[[{target}]] ← " + (targetPath == null ? BrokenLinkMark : vault.RelativeName(targetPath))));

        return Found;
    }

    static int Index(Vault vault, TextWriter output)
    {
        DateTime When(string p) { try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; } }

        var notes = vault.AllNotes().Concat(vault.EncryptedNotes())
            .Select(p => (Path: p, When: When(p)))
            .OrderByDescending(x => x.When)
            .ToList();

        foreach (var (path, when) in notes)
            output.WriteLine($"{when.ToLocalTime():yyyy-MM-dd HH:mm}\t{vault.RelativeName(path)}" +
                (NoteCrypto.IsEncrypted(path) ? "\t" + LockedMark : ""));

        return notes.Count > 0 ? Found : NothingFound;
    }

    static int Resolve(Vault vault, string[] args, TextWriter output)
    {
        if (args.Length == 0) return Help(output, UsageError);

        var path = vault.ResolveLink(string.Join(' ', args));
        if (path == null) return NothingFound;
        output.WriteLine(path);
        return Found;
    }

    static int Show(Vault vault, string[] args, TextWriter output)
    {
        if (args.Length == 0) return Help(output, UsageError);

        var name = string.Join(' ', args);
        var path = vault.ResolveLink(name);

        // ResolveLink يمرّ على AllNotes وحدها، لكن الحارس صريحٌ هنا لأنّ show
        // هو الأمر الوحيد الذي يطبع محتوى ملفٍ كاملاً: لا يُقرأ ملفٌ مشفّر أبداً.
        if (path == null || NoteCrypto.IsEncrypted(path))
        {
            output.WriteLine(path == null
                ? $"لا توجد ملاحظة بالاسم: {name}"
                : $"الملاحظة مقفلة، وتُفتح من التطبيق وحده: {name}");
            return NothingFound;
        }

        try { output.Write(File.ReadAllText(path)); }
        catch (Exception ex) { output.WriteLine($"تعذّرت القراءة: {ex.Message}"); return NothingFound; }
        return Found;
    }

    static int Tasks(Vault vault, string[] args, TextWriter output)
    {
        bool includeDone = args.Contains("--all");
        // ترشيحٌ بالمجلد: قبوٌ فيه مشاريع كثيرة يُخرج مهامّ لا تخصّ ما أعمل عليه
        var folder = Option(args, "--folder");
        var prefix = folder == null ? null
            : Path.GetFullPath(Path.Combine(vault.Root, folder)) + Path.DirectorySeparatorChar;

        int count = 0;
        foreach (var task in vault.Tasks(includeDone))
        {
            if (prefix != null &&
                !Path.GetFullPath(task.FilePath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            output.WriteLine(At(vault, task.FilePath, task.LineNumber, (task.Done ? "[x] " : "[ ] ") + task.Text));
            count++;
        }
        return count > 0 ? Found : NothingFound;
    }

    /// <summary>
    /// سجلّ الأحداث التي أتلفت معلومة: إعادة تسمية وحذف. حين يفشل <c>resolve</c> على
    /// اسمٍ أعرفه، هذا الأمر يقول أين ذهب بدل الاستدلال عليه بالبحث.
    /// </summary>
    static int Renames(Vault vault, string[] args, TextWriter output)
    {
        var events = vault.Renames(args.Length > 0 ? string.Join(' ', args) : null);
        foreach (var e in events)
            output.WriteLine($"{e.When.ToLocalTime():yyyy-MM-dd HH:mm}\t{e.Kind}\t{e.ItemType}\t{e.From} ← {e.To}");
        return events.Count > 0 ? Found : NothingFound;
    }

    // ---------- الكتابة ----------
    //
    // النصّ يأتي من الدخل القياسي لا من وسيطٍ في السطر: نصٌّ عربيّ متعدد الأسطر عبر
    // اقتباس الصدفة حقلُ ألغام، والدخل القياسي يتجاوزه كلّه.
    //
    // ولا أمرَ يحذف ملاحظة: ذلك يبقى في التطبيق بيد صاحب القبو.

    /// <summary>
    /// نصّ الدخل القياسي بعد تنظيفه. الصدفة تكتب بادئة ترتيب بايتات (U+FEFF) في
    /// أول ما تمرّره — ورأيتُها تدخل نصَّ ملاحظةٍ فعلاً — وهي أثرُ ترميزٍ لا محتوى،
    /// فتُزال أينما وقعت.
    /// </summary>
    /// <summary>
    /// قارئُ الدخل القياسي. يفكّ بـUTF-8 صراحةً ولا يتّكل على ترميز الطرفية:
    /// <c>Console.In</c> يفكّ بترميز صفحة الرموز النشطة، وهي على جهازٍ عربي CP720،
    /// فالنصّ العربي المُمرَّر يُفكّ خطأً ثم يُكتب مشوّشاً. كتب ذلك ملاحظةً كاملةً
    /// بحروفٍ معطوبة في قبوٍ حقيقي — لا يُقرأ الدخل إلا من هنا.
    /// </summary>
    public static TextReader Utf8Reader(Stream stream) =>
        new StreamReader(stream, new UTF8Encoding(false));

    /// <remarks>يُكتب المحرف بترميزه الصريح لا حرفياً: محرفٌ غير مرئيّ في المصدر يسقط
    /// عند الحفظ فيصير الاستبدال بلا أثر — وقع ذلك في فحصٍ هنا فعلاً.</remarks>
    static string ReadInput(TextReader? input) =>
        (input?.ReadToEnd() ?? "").Replace("﻿", "");

    /// <summary>عدد الأسطر الحقيقية في نصّ، بلا عدّ الفاصل الأخير سطراً.</summary>
    static int LineCount(string text) =>
        text.Length == 0 ? 0 : text.TrimEnd('\r', '\n').Split('\n').Length;

    /// <summary>يقرأ الوسيط الذي يلي علماً معيّناً، أو null إن غاب.</summary>
    static string? Option(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    /// <summary>الوسائط الحرّة: ما ليس علماً ولا قيمةَ علم — أي اسم الملاحظة.</summary>
    static string Positional(string[] args, params string[] options)
    {
        var free = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].StartsWith("--")) { if (options.Contains(args[i])) i++; continue; }
            free.Add(args[i]);
        }
        return string.Join(' ', free);
    }

    /// <summary>
    /// يحلّ اسماً إلى ملاحظةٍ قابلة للكتابة. الملاحظة المقفلة لا تصل هنا أصلاً — فـ
    /// <c>ResolveLink</c> يمرّ بـ<c>AllNotes</c> وهي تعدّ ملفات md وحدها — لكنّ الحارس
    /// صريحٌ كي يكون الرفض معلَناً لا صمتاً يُفسَّر بأن الملاحظة غير موجودة.
    /// </summary>
    static string? ResolveForWrite(Vault vault, string name, TextWriter output)
    {
        var path = vault.ResolveLink(name);
        if (path == null) { output.WriteLine($"لا توجد ملاحظة بالاسم: {name}"); return null; }
        if (NoteCrypto.IsEncrypted(path))
        {
            output.WriteLine($"الملاحظة مقفلة، ولا تُكتب إلا من التطبيق: {name}");
            return null;
        }
        return path;
    }

    /// <summary>
    /// مجلدُ وجهةٍ داخل القبو، مُنشأً إن لزم. الفحص على المسار المطلق بعد الحلّ،
    /// فلا يخرج ‎--folder ../..‎ بالكتابة خارج القبو إلى مكانٍ لم يأذن به صاحبه.
    /// </summary>
    static string? FolderInVault(Vault vault, string relative, TextWriter output)
    {
        var root = Path.GetFullPath(vault.Root).TrimEnd(Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase) &&
            !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            output.WriteLine($"المجلد خارج القبو، مرفوض: {relative}");
            return null;
        }
        try { Directory.CreateDirectory(full); }
        catch (Exception ex) { output.WriteLine($"تعذّر إنشاء المجلد: {ex.Message}"); return null; }
        return full;
    }

    /// <summary>
    /// ينبّه إن كان السطر الأول عنواناً «# …» يخالف اسم الملف. الواجهة تزامن اسم
    /// الملف مع العنوان، فتعيد تسمية الملف أوّلَ ما يُفتح — أثرٌ لا يراه من كتب
    /// ولا يتوقّعه صاحب القبو. وقع ذلك مرّتين في يومٍ واحد.
    /// <para>ويطبع السطر الأول على كل حال: عطبُ ترميزٍ يظهر هنا في ثانية بدل أن
    /// يمرّ صامتاً حتى يفتح أحدٌ الملف.</para>
    /// </summary>
    static void ReportFirstLine(Vault vault, string path, TextWriter output)
    {
        string first;
        try { first = File.ReadAllLines(path).FirstOrDefault() ?? ""; } catch { return; }
        output.WriteLine($"السطر الأول: {first}");

        if (!first.StartsWith("# ")) return;
        var title = Vault.Sanitize(first[2..].Trim());
        if (title.Length == 0 || string.Equals(title, vault.DisplayName(path), StringComparison.Ordinal)) return;
        output.WriteLine($"تنبيه: العنوان يخالف اسم الملف، فسيعيد التطبيق تسميته إلى «{title}» عند فتحه.");
    }

    static int New(Vault vault, string[] args, TextWriter output, TextReader? input)
    {
        var name = Positional(args, "--folder");
        if (name.Length == 0) return Help(output, UsageError);

        var target = FolderInVault(vault, Option(args, "--folder") ?? "", output);
        if (target == null) return NothingFound;

        var path = vault.CreateNote(target, name, ReadInput(input));
        vault.RecordWrite("create", path, vault.RelativeName(path));
        output.WriteLine(path);
        ReportFirstLine(vault, path, output);
        return Found;
    }

    static int AppendCmd(Vault vault, string[] args, TextWriter output, TextReader? input)
    {
        var name = Positional(args);
        if (name.Length == 0) return Help(output, UsageError);
        var path = ResolveForWrite(vault, name, output);
        if (path == null) return NothingFound;

        var text = ReadInput(input);
        if (!vault.AppendToNote(path, text)) { output.WriteLine("تعذّرت الكتابة"); return NothingFound; }

        int added = LineCount(text);
        vault.RecordWrite("append", path, $"+{added} أسطر");
        output.WriteLine($"أُلحق {added} سطراً بـ{vault.RelativeName(path)}");
        return Found;
    }

    /// <summary>يفسّر "n" أو "n-m" إلى نطاقٍ شاملٍ للطرفين يبدأ من واحد.</summary>
    static bool TryRange(string? spec, out int from, out int to)
    {
        from = to = 0;
        if (spec == null) return false;
        var parts = spec.Split('-', 2);
        if (!int.TryParse(parts[0], out from)) return false;
        to = parts.Length == 1 ? from : (int.TryParse(parts[1], out int end) ? end : 0);
        return to >= from;
    }

    static int Edit(Vault vault, string[] args, TextWriter output, TextReader? input)
    {
        var name = Positional(args, "--lines");
        if (name.Length == 0 || !TryRange(Option(args, "--lines"), out int from, out int to))
            return Help(output, UsageError);
        var path = ResolveForWrite(vault, name, output);
        if (path == null) return NothingFound;

        if (!vault.ReplaceLines(path, from, to, ReadInput(input), out var removed))
        {
            output.WriteLine($"تعذّر التعديل: لا نطاق {from}-{to} في {vault.RelativeName(path)}");
            return NothingFound;
        }

        var span = from == to ? $"سطر {from}" : $"أسطر {from}-{to}";
        vault.RecordWrite("edit", path, span);
        // المُزال يُعرض كي يُرى الخطأ في حينه لا بعد أسبوع
        output.WriteLine($"استُبدل {span} في {vault.RelativeName(path)}. المُزال:");
        output.WriteLine(removed);
        if (from == 1) ReportFirstLine(vault, path, output);   // العنوان تغيّر: قد تتبعه إعادة تسمية
        return Found;
    }

    static int Task(Vault vault, string[] args, TextWriter output)
    {
        var name = Positional(args, "--line");
        if (name.Length == 0 || !int.TryParse(Option(args, "--line"), out int line) || line < 1)
            return Help(output, UsageError);
        var path = ResolveForWrite(vault, name, output);
        if (path == null) return NothingFound;

        bool done = !args.Contains("--undo");
        // أرقام السطور المعروضة تبدأ من واحد، وSetTaskDone يعدّ من صفر
        if (!vault.SetTaskDone(path, line - 1, done))
        {
            output.WriteLine($"السطر {line} ليس مهمة في {vault.RelativeName(path)}");
            return NothingFound;
        }

        var state = done ? "منجزة" : "مفتوحة";
        vault.RecordWrite("task", path, $"سطر {line} {state}");
        output.WriteLine($"صارت مهمة السطر {line} {state} في {vault.RelativeName(path)}");
        return Found;
    }

    static int Move(Vault vault, string[] args, TextWriter output)
    {
        var name = Positional(args, "--to");
        var to = Option(args, "--to");
        if (name.Length == 0 || to == null) return Help(output, UsageError);
        var path = ResolveForWrite(vault, name, output);
        if (path == null) return NothingFound;
        var target = FolderInVault(vault, to, output);
        if (target == null) return NothingFound;

        string moved;
        try { moved = vault.MoveTo(path, target); }
        catch (Exception ex) { output.WriteLine($"تعذّر النقل: {ex.Message}"); return NothingFound; }

        vault.RecordWrite("move", path, vault.RelativeName(moved));
        output.WriteLine(moved);
        return Found;
    }

    static int Tags(Vault vault, TextWriter output)
    {
        var tags = vault.AllTags();
        foreach (var (tag, notes) in tags)
            output.WriteLine($"#{tag}\t{string.Join("، ", notes.Select(vault.RelativeName))}");
        return tags.Count > 0 ? Found : NothingFound;
    }
}
