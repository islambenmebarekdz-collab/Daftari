namespace Daftari.Cli;

/// <summary>
/// تنفيذ أوامر الطرفية على قبوٍ مفتوح.
///
/// <para>ثلاثة قيود تحكم هذا الملف:</para>
/// <list type="bullet">
/// <item>للقراءة فقط — لا يكتب في القبو حرفاً، فلا يصطدم بنسخة الواجهة المفتوحة
/// على الملفات نفسها، ولا يحتاج كاشف التعديلات الخارجية ولا كاتب الملاحظات المقفلة.</item>
/// <item>لا منطق جديد — كل أمرٍ سطرُ ربطٍ فوق دالّةٍ مختبَرة في <see cref="Vault"/>،
/// فيبقى ترجيح البحث وحلّ الروابط واحداً في الواجهة والطرفية معاً.</item>
/// <item>الملاحظات المقفلة خارج المحتوى بحكم البنية: <c>AllNotes()</c> تعدّ ملفات
/// <c>.md</c> وحدها، وذاكرة الجلسة المفتوحة لا وجود لها في عملية الطرفية أصلاً.
/// فلا يُطبع من المقفلة إلا اسمُها في <c>index</c>.</item>
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
        دفتري — واجهة نصّية للقراءة فقط على القبو

        الاستعمال: daftari [--vault <مسار>] <أمر> [وسائط]

          search <كلمات...>   بحثٌ مرجّح في المحتوى والعناوين والوسوم (كل الكلمات لازمة)
                              --files يعطي الملاحظات وعدد مطابقاتها بلا أسطرها
          links <اسم>         الروابط الواردة إلى الملاحظة والصادرة منها
          index               كل الملاحظات بوقت آخر تعديل، الأحدث أولاً
          resolve <اسم>       يحلّ اسم رابط [[...]] إلى مسار ملف كامل
          show <اسم>          يطبع محتوى الملاحظة باسم رابطها
          tasks [--all]       المهام غير المنجزة في القبو كله (--all يشمل المنجزة)
          tags                الوسوم وملاحظات كل وسم
          renames [اسم]       سجلّ إعادة التسمية والحذف، الأحدث أولاً

        رموز الخروج: 0 وُجد، 1 لا شيء، 2 خطأ استعمال
        """;

    public static int Run(Vault vault, string[] args, TextWriter output)
    {
        if (args.Length == 0) { output.WriteLine(Usage); return UsageError; }

        var rest = args.Skip(1).ToArray();
        return args[0] switch
        {
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

    static int Links(Vault vault, string[] args, TextWriter output)
    {
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
        int count = 0;
        foreach (var task in vault.Tasks(includeDone))
        {
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

    static int Tags(Vault vault, TextWriter output)
    {
        var tags = vault.AllTags();
        foreach (var (tag, notes) in tags)
            output.WriteLine($"#{tag}\t{string.Join("، ", notes.Select(vault.RelativeName))}");
        return tags.Count > 0 ? Found : NothingFound;
    }
}
