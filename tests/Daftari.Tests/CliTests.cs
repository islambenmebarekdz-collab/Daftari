using Daftari.Cli;
using Xunit;

namespace Daftari.Tests;

/// <summary>
/// أوامر الطرفية: سطحٌ نصّي للقراءة فقط فوق منطق القبو نفسه.
/// أهمّ ما تحرسه هذه الاختبارات: ألّا يسرّب أيّ أمرٍ محتوى ملاحظةٍ مقفلة،
/// وألّا يكتب أيّ أمرٍ في القبو حرفاً.
/// </summary>
public class CliTests
{
    static (int Code, string Out) Run(TempVault t, params string[] args)
    {
        var w = new StringWriter();
        int code = Commands.Run(t.Vault, args, w);
        return (code, w.ToString());
    }

    [Fact]
    public void search_يعطي_المسار_النسبي_ورقم_السطر_من_واحد()
    {
        using var t = new TempVault();
        t.Note("مذكرة.md", "سطر أول\nذكر الدافعية هنا\n");

        var (code, output) = Run(t, "search", "الدافعية");

        Assert.Equal(Commands.Found, code);
        Assert.Contains("مذكرة:2:", output);
        Assert.Contains("ذكر الدافعية هنا", output);
    }

    [Fact]
    public void search_بعدة_كلمات_متفرقة_يجد_الملاحظة()
    {
        using var t = new TempVault();
        t.Note("بحث.md", "الدافعية في السطر الأول\nوجودة الحياة في سطر آخر\n");
        t.Note("أخرى.md", "الدافعية وحدها\n");

        var (code, output) = Run(t, "search", "الدافعية", "جودة");

        Assert.Equal(Commands.Found, code);
        Assert.Contains("بحث:", output);
        Assert.DoesNotContain("أخرى:", output);
    }

    [Fact]
    public void search_بلا_نتائج_يعيد_رمز_لا_شيء()
    {
        using var t = new TempVault();
        t.Note("مذكرة.md", "نصّ عادي");

        var (code, output) = Run(t, "search", "كلمة-غير-موجودة-أبداً");

        Assert.Equal(Commands.NothingFound, code);
        Assert.Empty(output.Trim());
    }

    [Fact]
    public void links_يعرض_الواردة_والصادرة_معاً()
    {
        using var t = new TempVault();
        t.Note("الهدف.md", "# الهدف\nيشير إلى [[مرجع]]\n");
        t.Note("مصدر.md", "أذكر [[الهدف]] هنا\n");
        t.Note("مرجع.md", "محتوى المرجع\n");

        var (code, output) = Run(t, "links", "الهدف");

        Assert.Equal(Commands.Found, code);
        Assert.Contains("مصدر:1:", output);   // رابط وارد
        Assert.Contains("مرجع", output);      // رابط صادر
    }

    [Fact]
    public void links_يعلّم_الرابط_الصادر_المكسور()
    {
        using var t = new TempVault();
        t.Note("الهدف.md", "يشير إلى [[ملاحظة محذوفة]]\n");

        var (_, output) = Run(t, "links", "الهدف");

        Assert.Contains("ملاحظة محذوفة", output);
        Assert.Contains(Commands.BrokenLinkMark, output);
    }

    [Fact]
    public void resolve_يطبع_المسار_الكامل_للاسم()
    {
        using var t = new TempVault();
        var path = t.Note("مجلد/ملاحظة.md", "محتوى");

        var (code, output) = Run(t, "resolve", "ملاحظة");

        Assert.Equal(Commands.Found, code);
        // GetFullPath يوحّد الفاصل: TempVault يبني بـ/ والقرص يعيد بـ\
        Assert.Equal(Path.GetFullPath(path), Path.GetFullPath(output.Trim()));
    }

    [Fact]
    public void resolve_لاسم_غير_موجود_يعيد_رمز_لا_شيء()
    {
        using var t = new TempVault();
        t.Note("موجودة.md", "محتوى");

        var (code, _) = Run(t, "resolve", "اسم قديم بعد إعادة التسمية");

        Assert.Equal(Commands.NothingFound, code);
    }

    [Fact]
    public void index_يسرد_الملاحظات_بالأحدث_تعديلاً_أولاً()
    {
        using var t = new TempVault();
        var قديمة = t.Note("قديمة.md", "أ");
        var حديثة = t.Note("حديثة.md", "ب");
        File.SetLastWriteTimeUtc(قديمة, DateTime.UtcNow.AddDays(-3));
        File.SetLastWriteTimeUtc(حديثة, DateTime.UtcNow);

        var (code, output) = Run(t, "index");

        Assert.Equal(Commands.Found, code);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("حديثة", lines[0]);
        Assert.Contains("قديمة", lines[1]);
    }

    [Fact]
    public void index_يذكر_الملاحظة_المقفلة_باسمها_ولا_يكشف_محتواها()
    {
        using var t = new TempVault();
        var path = t.Path_("سرّية" + NoteCrypto.Extension);
        File.WriteAllBytes(path, NoteCrypto.Encrypt("نصّ سرّي جداً", NoteCrypto.CreateKey("كلمة-قوية-7")));

        var (_, output) = Run(t, "index");

        Assert.Contains("سرّية", output);
        Assert.Contains(Commands.LockedMark, output);
        Assert.DoesNotContain("نصّ سرّي جداً", output);
    }

    [Fact]
    public void search_لا_يسرّب_محتوى_ملاحظة_مقفلة()
    {
        using var t = new TempVault();
        var path = t.Path_("سرّية" + NoteCrypto.Extension);
        File.WriteAllBytes(path, NoteCrypto.Encrypt("كلمة السر هي الدافعية", NoteCrypto.CreateKey("كلمة-قوية-7")));

        var (code, output) = Run(t, "search", "الدافعية");

        Assert.Equal(Commands.NothingFound, code);
        Assert.DoesNotContain("كلمة السر", output);
    }

    [Fact]
    public void show_يطبع_محتوى_الملاحظة_باسم_الرابط()
    {
        using var t = new TempVault();
        t.Note("مجلد/ملاحظة.md", "# عنوان\nمحتوى الملاحظة\n");

        var (code, output) = Run(t, "show", "ملاحظة");

        Assert.Equal(Commands.Found, code);
        Assert.Contains("محتوى الملاحظة", output);
    }

    [Fact]
    public void show_يرفض_الملاحظة_المقفلة_ولا_يطبع_شيئاً_منها()
    {
        using var t = new TempVault();
        var path = t.Path_("سرّية" + NoteCrypto.Extension);
        File.WriteAllBytes(path, NoteCrypto.Encrypt("نصّ سرّي جداً", NoteCrypto.CreateKey("كلمة-قوية-7")));

        var (code, output) = Run(t, "show", "سرّية");

        Assert.Equal(Commands.NothingFound, code);
        Assert.DoesNotContain("نصّ سرّي جداً", output);
    }

    [Fact]
    public void tasks_يجمع_المهام_غير_المنجزة_من_كل_القبو()
    {
        using var t = new TempVault();
        t.Note("أ.md", "- [ ] مهمة معلّقة\n- [x] مهمة منجزة\n");
        t.Note("ب.md", "- [ ] مهمة ثانية\n");

        var (code, output) = Run(t, "tasks");

        Assert.Equal(Commands.Found, code);
        Assert.Contains("مهمة معلّقة", output);
        Assert.Contains("مهمة ثانية", output);
        Assert.DoesNotContain("مهمة منجزة", output);
    }

    [Fact]
    public void tags_يسرد_الوسوم_مع_ملاحظاتها()
    {
        using var t = new TempVault();
        t.Note("أ.md", "#مشروع نصّ\n");
        t.Note("ب.md", "#مشروع نصّ آخر\n");

        var (code, output) = Run(t, "tags");

        Assert.Equal(Commands.Found, code);
        Assert.Contains("مشروع", output);
        Assert.Contains("أ", output);
        Assert.Contains("ب", output);
    }

    [Fact]
    public void أمر_مجهول_يعيد_خطأ_استعمال_مع_قائمة_الأوامر()
    {
        using var t = new TempVault();

        var (code, output) = Run(t, "أمر-لا-وجود-له");

        Assert.Equal(Commands.UsageError, code);
        Assert.Contains("search", output);
    }

    [Fact]
    public void بلا_وسائط_يعرض_الاستعمال()
    {
        using var t = new TempVault();

        var (code, output) = Run(t);

        Assert.Equal(Commands.UsageError, code);
        Assert.Contains("search", output);
    }

    [Fact]
    public void لا_أمر_يكتب_في_القبو()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "# عنوان\n[[هدف]] و#وسم\n- [ ] مهمة\n");
        var before = Directory.GetFiles(t.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, p => File.GetLastWriteTimeUtc(p));

        foreach (var args in new[]
        {
            new[] { "search", "عنوان" }, new[] { "links", "ملاحظة" }, new[] { "index" },
            new[] { "resolve", "ملاحظة" }, new[] { "show", "ملاحظة" }, new[] { "tasks" }, new[] { "tags" },
        })
            Run(t, args);

        var after = Directory.GetFiles(t.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(p => p, p => File.GetLastWriteTimeUtc(p));
        Assert.Equal(before, after);
    }
}
