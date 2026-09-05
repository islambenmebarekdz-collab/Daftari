using Xunit;

namespace Daftari.Tests;

/// <summary>
/// مسارات الكتابة التي تستعملها أداة الطرفية. مبدؤها أن يبقى الخطأ مرئياً وقابلاً
/// للردّ: الإلحاق لا يمسّ ما قبله، والاستبدال يعيد ما أزاله قبل أن يزيله، وكلاهما
/// يفشل بصوتٍ عالٍ بدل أن يُتلف بصمت. والملاحظة المقفلة محجوبة عنهما بالبنية.
/// </summary>
public class VaultWriteTests
{
    static string Locked(TempVault t, string name, string content, string password = "كلمة-قوية-7")
    {
        var path = t.Path_(name + NoteCrypto.Extension);
        File.WriteAllBytes(path, NoteCrypto.Encrypt(content, NoteCrypto.CreateKey(password)));
        return path;
    }

    // ---------- الإلحاق ----------

    [Fact]
    public void AppendToNote_يضيف_في_الآخر_ويحفظ_ما_قبله()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "# عنوان\r\nالسطر الأصلي\r\n");

        Assert.True(t.Vault.AppendToNote(path, "سطرٌ مُلحق"));

        var after = File.ReadAllText(path);
        Assert.Contains("السطر الأصلي", after);
        Assert.Contains("سطرٌ مُلحق", after);
        Assert.True(after.IndexOf("السطر الأصلي") < after.IndexOf("سطرٌ مُلحق"));
    }

    [Fact]
    public void AppendToNote_يفصل_بسطر_جديد_إن_لم_ينته_الأصل_به()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "بلا سطر أخير");

        t.Vault.AppendToNote(path, "المُلحق");

        Assert.Equal(2, File.ReadAllLines(path).Length);
    }

    [Fact]
    public void AppendToNote_يرفض_ملاحظة_مقفلة_ولا_يكتب_فوقها()
    {
        using var t = new TempVault();
        var path = Locked(t, "سرّية", "نصّ سرّي");
        var before = File.ReadAllBytes(path);

        Assert.False(t.Vault.AppendToNote(path, "اقتحام"));

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // ---------- استبدال نطاق أسطر ----------

    [Fact]
    public void ReplaceLines_يعيد_النص_المُزال_ويضع_الجديد_مكانه()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "الأول\r\nالثاني\r\nالثالث\r\n");

        Assert.True(t.Vault.ReplaceLines(path, 2, 2, "البديل", out var removed));

        Assert.Equal("الثاني", removed);
        Assert.Equal(new[] { "الأول", "البديل", "الثالث" }, File.ReadAllLines(path));
    }

    [Fact]
    public void ReplaceLines_يستبدل_نطاقاً_كاملاً_بسطر_واحد()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "أ\r\nب\r\nج\r\nد\r\n");

        Assert.True(t.Vault.ReplaceLines(path, 2, 3, "بديل واحد", out var removed));

        Assert.Equal("ب\nج", removed);
        Assert.Equal(new[] { "أ", "بديل واحد", "د" }, File.ReadAllLines(path));
    }

    [Theory]
    [InlineData("")]        // فارغ حقيقي
    [InlineData("\n")]      // ما تمرّره الصدفة حين يُراد «لا شيء»
    [InlineData("\r\n")]
    public void ReplaceLines_ببديل_خالٍ_يشطب_ولا_يترك_سطراً_فارغاً(string replacement)
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "أ\r\nفقرة تُشطب\r\nوبقيتها\r\nد\r\n");

        Assert.True(t.Vault.ReplaceLines(path, 2, 3, replacement, out var removed));

        Assert.Equal("فقرة تُشطب\nوبقيتها", removed);
        Assert.Equal(new[] { "أ", "د" }, File.ReadAllLines(path));
    }

    [Theory]
    [InlineData(0, 1)]      // قبل الأول
    [InlineData(1, 9)]      // بعد الأخير
    [InlineData(3, 2)]      // نطاق مقلوب
    public void ReplaceLines_يرفض_مدى_خاطئاً_ولا_يمس_الملف(int from, int to)
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "أ\r\nب\r\nج\r\n");
        var before = File.ReadAllText(path);

        Assert.False(t.Vault.ReplaceLines(path, from, to, "بديل", out var removed));

        Assert.Equal("", removed);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void ReplaceLines_أرقام_أسطره_هي_أرقام_البحث_نفسها()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "أ\r\nالمطلوبة\r\nج\r\n");
        var hit = t.Vault.Search("المطلوبة").Single();

        // البحث يعدّ من صفر ويعرضه من واحد؛ فالسطر المعروض هو LineNumber + 1
        Assert.True(t.Vault.ReplaceLines(path, hit.LineNumber + 1, hit.LineNumber + 1, "بديل", out var removed));

        Assert.Equal("المطلوبة", removed);
    }

    [Fact]
    public void ReplaceLines_يرفض_ملاحظة_مقفلة_ولا_يكتب_فوقها()
    {
        using var t = new TempVault();
        var path = Locked(t, "سرّية", "سطر\nآخر");
        var before = File.ReadAllBytes(path);

        Assert.False(t.Vault.ReplaceLines(path, 1, 1, "اقتحام", out _));

        Assert.Equal(before, File.ReadAllBytes(path));
    }

    // ---------- أثر الكتابة ----------

    [Fact]
    public void RecordWrite_يترك_أثراً_يقرؤه_السجلّ()
    {
        using var t = new TempVault();
        var path = t.Note("مجلد/ملاحظة.md", "محتوى");

        t.Vault.RecordWrite("append", path, "+2 أسطر");

        var e = Assert.Single(t.Vault.Renames());
        Assert.Equal("append", e.Kind);
        Assert.Equal(Path.Combine("مجلد", "ملاحظة"), e.From);
        Assert.Equal("+2 أسطر", e.To);
    }

    [Fact]
    public void تعذّر_السجلّ_لا_يمنع_الكتابة()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "الأصل\r\n");
        File.WriteAllText(Path.Combine(t.Root, Vault.MetaFolderName), "عائق");   // ملف مكان المجلد

        Assert.True(t.Vault.AppendToNote(path, "المُلحق"));
        t.Vault.RecordWrite("append", path, "+1 سطر");

        Assert.Contains("المُلحق", File.ReadAllText(path));
        Assert.Empty(t.Vault.Renames());
    }
}
