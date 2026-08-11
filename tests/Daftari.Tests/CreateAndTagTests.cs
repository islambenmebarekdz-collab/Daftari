using System.Text;
using Xunit;

namespace Daftari.Tests;

/// <summary>إنشاء الملاحظات وجمع الوسوم — أكثر مسارين استعمالاً في اليوم (Ctrl+N وCtrl+T).</summary>
public class CreateAndTagTests
{
    [Fact]
    public void CreateNote_ينشئ_ملفاً_بالاسم_والمحتوى()
    {
        using var t = new TempVault();
        var path = t.Vault.CreateNote(t.Root, "فكرة أولى", "# فكرة أولى\nنص");

        Assert.True(File.Exists(path));
        Assert.Equal("فكرة أولى.md", Path.GetFileName(path));
        Assert.Equal("# فكرة أولى\nنص", File.ReadAllText(path));
    }

    [Fact]
    public void CreateNote_يكتب_UTF8_بلا_علامة_ترتيب_البايتات()
    {
        using var t = new TempVault();
        var path = t.Vault.CreateNote(t.Root, "عربية", "مرحبا");

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("مرحبا", new UTF8Encoding(false).GetString(bytes));
    }

    [Fact]
    public void CreateNote_ينظّف_الأحرف_الممنوعة_في_أسماء_الملفات()
    {
        using var t = new TempVault();
        var path = t.Vault.CreateNote(t.Root, "تقرير: الفصل/الأول?");

        Assert.True(File.Exists(path));
        Assert.DoesNotContain(Path.GetInvalidFileNameChars(), c => Path.GetFileName(path).Contains(c));
        Assert.Equal(t.Root, Path.GetDirectoryName(path));   // لم يخرج الاسم من مجلده
    }

    [Fact]
    public void CreateNote_يسمّي_الفارغ_ملاحظة_جديدة()
    {
        using var t = new TempVault();
        var path = t.Vault.CreateNote(t.Root, "   ");

        Assert.Equal("ملاحظة جديدة.md", Path.GetFileName(path));
    }

    [Fact]
    public void CreateNote_لا_يكتب_فوق_ملاحظة_قائمة_بل_يرقّم()
    {
        using var t = new TempVault();
        var first = t.Vault.CreateNote(t.Root, "مكرّرة", "الأصل");
        var second = t.Vault.CreateNote(t.Root, "مكرّرة", "الثانية");
        var third = t.Vault.CreateNote(t.Root, "مكرّرة", "الثالثة");

        Assert.Equal("مكرّرة.md", Path.GetFileName(first));
        Assert.Equal("مكرّرة 2.md", Path.GetFileName(second));
        Assert.Equal("مكرّرة 3.md", Path.GetFileName(third));
        Assert.Equal("الأصل", File.ReadAllText(first));   // لم تُمَس الأولى
    }

    [Fact]
    public void AllTags_يجمع_الوسوم_مع_ملاحظاتها_مرتّبة()
    {
        using var t = new TempVault();
        var a = t.Note("أ", "# أ\n#مذكرة و #رفيق");
        var b = t.Note("ب", "# ب\n#مذكرة فقط");

        var tags = t.Vault.AllTags();

        Assert.Equal(new[] { "رفيق", "مذكرة" }, tags.Keys.ToArray());
        Assert.Equal(new[] { a, b }, tags["مذكرة"].OrderBy(x => x).ToArray());
        Assert.Equal(new[] { a }, tags["رفيق"].ToArray());
    }

    [Fact]
    public void AllTags_لا_يعد_علامة_ملتصقة_بكلمة_وسماً_فتسلم_لغات_مثل_C()
    {
        using var t = new TempVault();
        t.Note("أ", "أكتب بلغة C# منذ سنة، و#وسم_ملتصق ليس وسماً، بخلاف (#بين_قوسين)");

        var tags = t.Vault.AllTags();

        Assert.DoesNotContain(tags.Keys, k => k.StartsWith("منذ"));   // ‏C# لم تفتح وسماً
        Assert.DoesNotContain("وسم_ملتصق", tags.Keys);
        Assert.Contains("بين_قوسين", tags.Keys);
    }

    [Fact]
    public void AllTags_لا_يعد_العنوان_وسماً()
    {
        using var t = new TempVault();
        t.Note("أ", "# عنوان الملاحظة\n## عنوان فرعي");

        Assert.Empty(t.Vault.AllTags());
    }

    [Fact]
    public void AllTags_يستبعد_المحذوفات_ويشمل_المقفلة_المفتوحة()
    {
        using var t = new TempVault();
        Directory.CreateDirectory(t.Vault.TrashPath);
        File.WriteAllText(Path.Combine(t.Vault.TrashPath, "قديمة.md"), "#محذوف");

        var locked = t.Path_("سرية" + NoteCrypto.Extension);
        File.WriteAllBytes(locked, NoteCrypto.Encrypt("#سري", NoteCrypto.CreateKey("كلمة-قوية")));
        t.Vault.SetUnlockedContent(locked, "#سري");

        var tags = t.Vault.AllTags();

        Assert.DoesNotContain("محذوف", tags.Keys);
        Assert.Contains("سري", tags.Keys);

        t.Vault.ForgetAllUnlocked();
        Assert.DoesNotContain("سري", t.Vault.AllTags().Keys);   // تخرج فور إقفال الجلسة
    }
}
