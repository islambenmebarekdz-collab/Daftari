using Daftari.Cli;
using Xunit;

namespace Daftari.Tests;

/// <summary>
/// أوامر الكتابة في أداة الطرفية. حارسان يحكمانها: الملاحظة المقفلة مرفوضة في كل
/// أمر، وكل كتابةٍ ناجحة تترك أثراً في السجلّ — فيكون «ما كتبته الأداة» معلوماً
/// لا مظنوناً. والحذف ليس منها: حذف الملاحظات يبقى بيد صاحب القبو وحده.
/// </summary>
public class CliWriteTests
{
    static (int Code, string Out) Run(TempVault t, string stdin, params string[] args)
    {
        var w = new StringWriter();
        int code = Commands.Run(t.Vault, args, w, new StringReader(stdin));
        return (code, w.ToString());
    }

    static string Locked(TempVault t, string name, string content = "نصّ سرّي")
    {
        var path = t.Path_(name + NoteCrypto.Extension);
        File.WriteAllBytes(path, NoteCrypto.Encrypt(content, NoteCrypto.CreateKey("كلمة-قوية-7")));
        return path;
    }

    // ---------- الإنشاء ----------

    [Fact]
    public void new_ينشئ_ملاحظة_بمحتوى_الدخل_القياسي()
    {
        using var t = new TempVault();

        var (code, output) = Run(t, "# عنوان\nمحتوى", "new", "فكرة جديدة");

        Assert.Equal(Commands.Found, code);
        var path = t.Vault.ResolveLink("فكرة جديدة");
        Assert.NotNull(path);
        Assert.Contains("محتوى", File.ReadAllText(path!));
        Assert.Contains("فكرة جديدة", output);      // يطبع المسار كي أتحقّق
    }

    [Fact]
    public void new_لا_يكتب_فوق_ملاحظة_قائمة_بل_يرقّم()
    {
        using var t = new TempVault();
        t.Note("موجودة.md", "المحتوى الأصلي");

        Run(t, "محتوى مختلف", "new", "موجودة");

        Assert.Equal("المحتوى الأصلي", File.ReadAllText(t.Path_("موجودة.md")));
        Assert.True(File.Exists(t.Path_("موجودة 2.md")));
    }

    [Fact]
    public void new_ينشئ_داخل_مجلد_معيّن()
    {
        using var t = new TempVault();

        Run(t, "محتوى", "new", "ملاحظة", "--folder", "مجلد/فرعي");

        Assert.True(File.Exists(t.Path_("مجلد", "فرعي", "ملاحظة.md")));
    }

    // ---------- الإلحاق ----------

    [Fact]
    public void append_يضيف_ولا_يمس_ما_قبله()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "الأصل\r\n");

        var (code, _) = Run(t, "المُلحق", "append", "ملاحظة");

        Assert.Equal(Commands.Found, code);
        Assert.Equal(new[] { "الأصل", "المُلحق" }, File.ReadAllLines(t.Path_("ملاحظة.md")));
    }

    // ---------- التعديل ----------

    [Fact]
    public void edit_يستبدل_سطراً_ويطبع_ما_أزاله()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "أ\r\nالقديم\r\nج\r\n");

        var (code, output) = Run(t, "الجديد", "edit", "ملاحظة", "--lines", "2");

        Assert.Equal(Commands.Found, code);
        Assert.Contains("القديم", output);          // المُزال يُعرض قبل أن يضيع
        Assert.Equal(new[] { "أ", "الجديد", "ج" }, File.ReadAllLines(t.Path_("ملاحظة.md")));
    }

    [Fact]
    public void edit_بدخل_فارغ_يشطب_النطاق()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "أ\r\nتُشطب\r\nوهذه\r\nد\r\n");

        Run(t, "", "edit", "ملاحظة", "--lines", "2-3");

        Assert.Equal(new[] { "أ", "د" }, File.ReadAllLines(t.Path_("ملاحظة.md")));
    }

    [Fact]
    public void edit_بمدى_خاطئ_يفشل_ولا_يمس_الملف()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "أ\r\nب\r\n");

        var (code, _) = Run(t, "بديل", "edit", "ملاحظة", "--lines", "9");

        Assert.Equal(Commands.NothingFound, code);
        Assert.Equal(new[] { "أ", "ب" }, File.ReadAllLines(t.Path_("ملاحظة.md")));
    }

    // ---------- المهام ----------

    [Fact]
    public void task_يعلّم_المهمة_منجزة_ويعيدها()
    {
        using var t = new TempVault();
        t.Note("مهام.md", "- [ ] مهمة\r\n");

        Assert.Equal(Commands.Found, Run(t, "", "task", "مهام", "--line", "1").Code);
        Assert.Contains("[x]", File.ReadAllText(t.Path_("مهام.md")));

        Assert.Equal(Commands.Found, Run(t, "", "task", "مهام", "--line", "1", "--undo").Code);
        Assert.Contains("[ ]", File.ReadAllText(t.Path_("مهام.md")));
    }

    // ---------- النقل ----------

    [Fact]
    public void move_ينقل_ملاحظة_إلى_مجلد()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "محتوى");

        var (code, _) = Run(t, "", "move", "ملاحظة", "--to", "وجهة");

        Assert.Equal(Commands.Found, code);
        Assert.True(File.Exists(t.Path_("وجهة", "ملاحظة.md")));
        Assert.False(File.Exists(t.Path_("ملاحظة.md")));
    }

    // ---------- الحارسان ----------

    [Theory]
    [InlineData("append")]
    [InlineData("edit")]
    [InlineData("task")]
    public void كل_أمر_كتابة_يرفض_الملاحظة_المقفلة(string command)
    {
        using var t = new TempVault();
        var path = Locked(t, "سرّية", "- [ ] مهمة سرّية");
        var before = File.ReadAllBytes(path);

        var args = command switch
        {
            "edit" => new[] { "edit", "سرّية", "--lines", "1" },
            "task" => new[] { "task", "سرّية", "--line", "1" },
            _ => new[] { "append", "سرّية" },
        };
        var (code, output) = Run(t, "اقتحام", args);

        Assert.NotEqual(Commands.Found, code);
        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.DoesNotContain("مهمة سرّية", output);
    }

    [Fact]
    public void كل_كتابة_ناجحة_تترك_أثراً_في_السجلّ()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "عنوان\r\n- [ ] مهمة\r\n");

        Run(t, "محتوى", "new", "جديدة");
        Run(t, "مُلحق", "append", "ملاحظة");
        Run(t, "بديل", "edit", "ملاحظة", "--lines", "1");   // يبقى سطر المهمة في الثاني
        Run(t, "", "task", "ملاحظة", "--line", "2");
        Run(t, "", "move", "ملاحظة", "--to", "وجهة");

        var kinds = t.Vault.Renames().Select(e => e.Kind).ToList();
        Assert.Equal(5, kinds.Count);
        foreach (var kind in new[] { "create", "append", "edit", "task", "move" })
            Assert.Contains(kind, kinds);
    }

    [Fact]
    public void بادئة_الترميز_من_الصدفة_لا_تدخل_نص_الملاحظة()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "الأصل\r\n");

        // الصدفة تكتب U+FEFF في أول ما تمرّره عبر الدخل القياسي — رُئي يدخل ملاحظةً فعلاً
        var (code, output) = Run(t, "﻿نصّ نظيف\r\n", "append", "ملاحظة");

        Assert.Equal(Commands.Found, code);
        // مقارنة ترتيبية لا ثقافية: U+FEFF عديم الوزن في المقارنة الثقافية،
        // فـDoesNotContain يجده في أي نصّ ولو خلا منه
        Assert.False(File.ReadAllText(t.Path_("ملاحظة.md")).Contains('﻿'));
        Assert.Equal(new[] { "الأصل", "نصّ نظيف" }, File.ReadAllLines(t.Path_("ملاحظة.md")));
        Assert.Contains("1 سطر", output);   // الفاصل الأخير ليس سطراً يُعدّ
    }

    [Fact]
    public void قارئ_الدخل_يفكّ_UTF8_لا_ترميز_الطرفية_العربي()
    {
        const string arabic = "# ملاحظات على «دفتري» — تنتظر جلسة تحسين";
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(arabic);

        using var stream = new MemoryStream(bytes);
        var read = Commands.Utf8Reader(stream).ReadToEnd();

        // فكُّ البايتات نفسها بترميز الطرفية العربي (CP720) يعطي نصّاً مشوّشاً؛
        // كتب ذلك ملاحظةً كاملةً بحروفٍ معطوبة في قبوٍ حقيقي، فالفحص يحرس الفرق
        Assert.Equal(arabic, read);
        Assert.DoesNotContain("╪", read, StringComparison.Ordinal);
    }

    [Fact]
    public void لا_كتابة_خارج_القبو_مهما_كان_المسار()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "محتوى");
        var outside = Path.GetFullPath(Path.Combine(t.Root, "..", "خارج-القبو"));
        try { Directory.Delete(outside, true); } catch { }

        var created = Run(t, "محتوى", "new", "متسللة", "--folder", @"..\خارج-القبو");
        var moved = Run(t, "", "move", "ملاحظة", "--to", @"..\خارج-القبو");

        Assert.Equal(Commands.NothingFound, created.Code);
        Assert.Equal(Commands.NothingFound, moved.Code);
        Assert.False(Directory.Exists(outside));
        Assert.True(File.Exists(t.Path_("ملاحظة.md")));   // بقيت مكانها
    }

    [Fact]
    public void لا_أمر_يحذف_ملاحظة()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "محتوى");

        var (code, output) = Run(t, "", "delete", "ملاحظة");

        Assert.Equal(Commands.UsageError, code);
        Assert.True(File.Exists(t.Path_("ملاحظة.md")));
        Assert.DoesNotContain("delete", output);   // ليس في قائمة الأوامر أصلاً
    }
}
