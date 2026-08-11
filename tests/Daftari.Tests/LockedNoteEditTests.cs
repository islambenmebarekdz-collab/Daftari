using Xunit;

namespace Daftari.Tests;

/// <summary>
/// تحرير الملاحظات المقفلة المفتوحة في الجلسة: مهامها وإشاراتها وروابطها تظهر في القوائم،
/// فيجب أن تكون قابلة للتعديل فعلاً — ولا يجوز بحال أن يُكتب نص واضح فوق ملف مشفّر.
/// </summary>
public class LockedNoteEditTests
{
    /// <summary>ينشئ ملاحظة مقفلة حقيقية بمحتوى معلوم ويفتحها في الجلسة. يعيد مسارها وبايتاتها الأصلية.</summary>
    static (string Path, byte[] Original) MakeLocked(TempVault t, string name, string content, string password)
    {
        var path = t.Path_(name + NoteCrypto.Extension);
        var blob = NoteCrypto.Encrypt(content, NoteCrypto.CreateKey(password));
        File.WriteAllBytes(path, blob);
        t.Vault.SetUnlockedContent(path, content);
        return (path, blob);
    }

    /// <summary>كاتب مشفّر مثل الذي تركّبه الواجهة: يعيد التشفير بمفتاح الجلسة.</summary>
    static Func<string, string, bool> WriterFor(string password) =>
        (path, text) =>
        {
            File.WriteAllBytes(path, NoteCrypto.Encrypt(text, NoteCrypto.CreateKey(password)));
            return true;
        };

    static string Decrypt(string path, string password)
    {
        var blob = File.ReadAllBytes(path);
        return NoteCrypto.Decrypt(blob, NoteCrypto.DeriveKeyFor(blob, password));
    }

    [Fact]
    public void SetTaskDone_ينجز_مهمة_داخل_ملاحظة_مقفلة_مفتوحة()
    {
        using var t = new TempVault();
        var (path, _) = MakeLocked(t, "سرية", "# سرية\n- [ ] مهمة مقفلة\n", "كلمة-قوية-1");
        t.Vault.EncryptedWriter = WriterFor("كلمة-قوية-1");

        var task = t.Vault.Tasks().Single(x => x.Text == "مهمة مقفلة");
        Assert.True(t.Vault.SetTaskDone(task.FilePath, task.LineNumber, done: true));

        // الملف ما زال مشفّراً، ومحتواه الجديد يُقرأ بفك التشفير لا كنص عادي
        Assert.Contains("- [x] مهمة مقفلة", Decrypt(path, "كلمة-قوية-1"));

        // ونسخة الجلسة تحدّثت، فلم تعد المهمة مفتوحة في القائمة
        Assert.DoesNotContain(t.Vault.Tasks(), x => x.Text == "مهمة مقفلة");
    }

    [Fact]
    public void SetTaskDone_لا_يكتب_نصاً_واضحاً_فوق_ملاحظة_مقفلة_بلا_كاتب()
    {
        using var t = new TempVault();
        var (path, original) = MakeLocked(t, "سرية", "- [ ] مهمة مقفلة\n", "كلمة-قوية-2");
        // لا EncryptedWriter: لا مفتاح في الجلسة

        Assert.False(t.Vault.SetTaskDone(path, 0, done: true));
        Assert.Equal(original, File.ReadAllBytes(path));   // الملف لم يُمَس إطلاقاً
    }

    [Fact]
    public void ConvertMentionToLink_يعمل_داخل_ملاحظة_مقفلة_مفتوحة()
    {
        using var t = new TempVault();
        t.Note("فكرة", "# فكرة");
        var (path, _) = MakeLocked(t, "سرية", "سطر يذكر فكرة بلا رابط\n", "كلمة-قوية-3");
        t.Vault.EncryptedWriter = WriterFor("كلمة-قوية-3");

        var mention = t.Vault.UnlinkedMentions(t.Path_("فكرة.md")).First();
        Assert.Equal(path, mention.FilePath);
        Assert.True(t.Vault.ConvertMentionToLink(mention.FilePath, mention.LineNumber, "فكرة"));
        Assert.Contains("[[فكرة]]", Decrypt(path, "كلمة-قوية-3"));
    }

    [Fact]
    public void ConvertMentionToLink_لا_يمس_ملاحظة_مقفلة_بلا_كاتب()
    {
        using var t = new TempVault();
        var (path, original) = MakeLocked(t, "سرية", "سطر يذكر فكرة بلا رابط\n", "كلمة-قوية-4");

        Assert.False(t.Vault.ConvertMentionToLink(path, 0, "فكرة"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void UpdateLinks_يحدّث_روابط_ملاحظة_مقفلة_مفتوحة_ويبقيها_مشفّرة()
    {
        using var t = new TempVault();
        var (path, _) = MakeLocked(t, "سرية", "أشير إلى [[هدف]] هنا\n", "كلمة-قوية-5");
        t.Vault.EncryptedWriter = WriterFor("كلمة-قوية-5");

        Assert.Equal(1, t.Vault.UpdateLinks("هدف", "هدف جديد"));
        Assert.Contains("[[هدف جديد]]", Decrypt(path, "كلمة-قوية-5"));
    }

    [Fact]
    public void UpdateLinks_لا_يكتب_نصاً_واضحاً_فوق_ملاحظة_مقفلة_بلا_كاتب()
    {
        using var t = new TempVault();
        var (path, original) = MakeLocked(t, "سرية", "أشير إلى [[هدف]] هنا\n", "كلمة-قوية-6");

        Assert.Equal(0, t.Vault.UpdateLinks("هدف", "هدف جديد"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void الملاحظة_المقفلة_غير_المفتوحة_لا_تُمس_ولو_وُجد_كاتب()
    {
        using var t = new TempVault();
        var path = t.Path_("مغلقة" + NoteCrypto.Extension);
        var original = NoteCrypto.Encrypt("- [ ] مهمة\n[[هدف]]\n", NoteCrypto.CreateKey("كلمة-قوية-7"));
        File.WriteAllBytes(path, original);
        t.Vault.EncryptedWriter = WriterFor("كلمة-قوية-7");   // كاتب موجود، لكن الملاحظة لم تُفتح

        Assert.False(t.Vault.SetTaskDone(path, 0, done: true));
        Assert.False(t.Vault.ConvertMentionToLink(path, 0, "هدف"));
        Assert.Equal(0, t.Vault.UpdateLinks("هدف", "هدف جديد"));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void فشل_الكاتب_لا_يفقد_نسخة_الجلسة_ولا_يغيّر_الملف()
    {
        using var t = new TempVault();
        var (path, original) = MakeLocked(t, "سرية", "- [ ] مهمة مقفلة\n", "كلمة-قوية-8");
        t.Vault.EncryptedWriter = (_, _) => false;   // مثلاً: مُحي مفتاح الجلسة قبل الكتابة

        Assert.False(t.Vault.SetTaskDone(path, 0, done: true));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Contains(t.Vault.Tasks(), x => x.Text == "مهمة مقفلة");   // ما زالت مفتوحة كما كانت
    }

    [Fact]
    public void الملاحظات_العادية_لم_يتغيّر_سلوكها_بوجود_كاتب_مشفّر()
    {
        using var t = new TempVault();
        var note = t.Note("عادية", "- [ ] مهمة عادية\n");
        t.Vault.EncryptedWriter = (_, _) => throw new InvalidOperationException("لا يُستدعى للملاحظات العادية");

        Assert.True(t.Vault.SetTaskDone(note, 0, done: true));
        Assert.Contains("- [x] مهمة عادية", File.ReadAllText(note));
    }
}
