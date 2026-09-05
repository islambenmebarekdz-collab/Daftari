using Xunit;

namespace Daftari.Tests;

/// <summary>
/// إبطال فهرس القبو. الفهرس يوفّر إعادة قراءة الملفات غير المتغيّرة، لكنّ مفتاحه
/// كان طابع وقت التعديل وحده — وهو مفتاح غير كافٍ: كتابتان متتاليتان سريعتان قد
/// تقعان داخل تكّة الطابع نفسها على نظام الملفات، فيبقى الفهرس على الأسطر القديمة.
/// أثرُه في التطبيق: تعديلُ ملاحظة ثم بحثٌ فوريّ يُرجع نتائج قديمة.
/// </summary>
public class IndexCacheTests
{
    [Fact]
    public void تعديل_عبر_القبو_يبطل_الفهرس_ولو_لم_يتغيّر_طابع_الوقت()
    {
        using var t = new TempVault();
        var note = t.Note("فكرة", "# فكرة");
        var other = t.Note("أخرى", "أعجبتني فكرة اليوم");

        var mention = t.Vault.UnlinkedMentions(note).First();   // يملأ الفهرس
        var stamp = File.GetLastWriteTimeUtc(other);

        Assert.True(t.Vault.ConvertMentionToLink(mention.FilePath, mention.LineNumber, "فكرة"));
        File.SetLastWriteTimeUtc(other, stamp);   // يحاكي كتابةً وقعت داخل التكّة نفسها

        Assert.Contains("[[فكرة]]", File.ReadAllText(other));
        Assert.Empty(t.Vault.UnlinkedMentions(note));
    }

    [Fact]
    public void تعديل_خارجي_يغيّر_الحجم_يُكشف_ولو_لم_يتغيّر_طابع_الوقت()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة", "السطر الأول");

        Assert.Single(t.Vault.Search("الأول"));   // يملأ الفهرس
        var stamp = File.GetLastWriteTimeUtc(note);

        // محرّر خارجي (Obsidian مثلاً) يكتب فوق الملف ثم يعيد الطابع كما كان
        File.WriteAllText(note, "السطر الأول\nسطر أضافه محرّر خارجي");
        File.SetLastWriteTimeUtc(note, stamp);

        Assert.Single(t.Vault.Search("خارجي"));
    }

    [Fact]
    public void حفظ_المهمة_منجزة_ينعكس_فوراً_في_قائمة_المهام()
    {
        using var t = new TempVault();
        var note = t.Note("مهام", "- [ ] مهمة واحدة");

        var task = t.Vault.Tasks().Single();
        var stamp = File.GetLastWriteTimeUtc(note);

        Assert.True(t.Vault.SetTaskDone(task.FilePath, task.LineNumber, true));
        File.SetLastWriteTimeUtc(note, stamp);

        Assert.Empty(t.Vault.Tasks());
    }
}
