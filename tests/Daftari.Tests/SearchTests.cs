using Xunit;

namespace Daftari.Tests;

/// <summary>ترجيح نتائج البحث، والبحث بالوسوم، والبحث بعدة كلمات.</summary>
public class SearchTests
{
    static List<string> Names(IEnumerable<SearchHit> hits) =>
        hits.Select(h => Path.GetFileNameWithoutExtension(h.FilePath)).Distinct().ToList();

    [Fact]
    public void تطابق_العنوان_يتصدر_النتائج()
    {
        using var t = new TempVault();
        t.Note("مشروع", "محتوى بلا كلمة البحث");
        t.Note("قليل", "ذُكر مشروع مرة واحدة");

        var order = Names(t.Vault.Search("مشروع"));

        Assert.Equal("مشروع", order[0]);
    }

    [Fact]
    public void الملاحظة_ذات_التطابقات_الأكثر_تسبق_الأقل()
    {
        using var t = new TempVault();
        t.Note("كثير", "مشروع\nمشروع\nمشروع");
        t.Note("قليل", "ذُكر مشروع مرة واحدة");

        var order = Names(t.Vault.Search("مشروع"));

        Assert.True(order.IndexOf("كثير") < order.IndexOf("قليل"));
    }

    [Fact]
    public void الوسم_يرجّح_الملاحظة_فوق_تطابق_نصي_أقل()
    {
        using var t = new TempVault();
        t.Note("وسم", "نص فيه #مشروع كوسم");
        t.Note("قليل", "ذُكر مشروع مرة واحدة");

        var order = Names(t.Vault.Search("مشروع"));

        Assert.True(order.IndexOf("وسم") < order.IndexOf("قليل"));
    }

    [Fact]
    public void البحث_بصيغة_وسم_يجد_الملاحظة()
    {
        using var t = new TempVault();
        t.Note("وسم", "نص فيه #مشروع كوسم");

        Assert.Contains("وسم", Names(t.Vault.Search("#مشروع")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void البحث_الفارغ_لا_يعيد_شيئاً_ولا_يرمي(string query)
    {
        using var t = new TempVault();
        t.Note("ملاحظة", "أي نص");

        Assert.Empty(t.Vault.Search(query));
    }

    [Fact]
    public void بحث_عدة_كلمات_يجدها_ولو_في_أسطر_متفرقة()
    {
        using var t = new TempVault();
        t.Note("متفرقة", "سطر فيه رفيق\n\nوسطر بعيد فيه مكفوفين");

        Assert.Contains("متفرقة", Names(t.Vault.Search("رفيق مكفوفين")));
    }

    [Fact]
    public void بحث_عدة_كلمات_يستبعد_ما_ينقصه_كلمة()
    {
        using var t = new TempVault();
        t.Note("ناقصة", "فيها رفيق فقط بلا الكلمة الثانية");
        t.Note("كاملة", "فيها رفيق و مكفوفين معاً");

        var names = Names(t.Vault.Search("رفيق مكفوفين"));

        Assert.Contains("كاملة", names);
        Assert.DoesNotContain("ناقصة", names);
    }

    [Fact]
    public void العنوان_الحاوي_لكل_الكلمات_يتصدر()
    {
        using var t = new TempVault();
        t.Note("رفيق مكفوفين", "العنوان يحوي الكلمتين");
        t.Note("متجاورة", "عبارة رفيق مكفوفين متجاورة");
        t.Note("متفرقة", "سطر فيه رفيق\n\nوسطر فيه مكفوفين");

        var order = Names(t.Vault.Search("رفيق مكفوفين"));

        Assert.Equal("رفيق مكفوفين", order[0]);
    }

    [Fact]
    public void العبارة_المتجاورة_ترجّح_فوق_الكلمات_المتفرقة()
    {
        using var t = new TempVault();
        t.Note("متجاورة", "عبارة رفيق مكفوفين متجاورة");
        t.Note("متفرقة", "سطر فيه رفيق\n\nوسطر بعيد فيه مكفوفين");

        var order = Names(t.Vault.Search("رفيق مكفوفين"));

        Assert.True(order.IndexOf("متجاورة") < order.IndexOf("متفرقة"));
    }

    [Fact]
    public void الملاحظات_المقفلة_خارج_البحث()
    {
        using var t = new TempVault();
        var blob = NoteCrypto.Encrypt("نص فيه كلمة حساسة", NoteCrypto.CreateKey("كلمة مرور طويلة"));
        File.WriteAllBytes(t.Path_("سرية" + NoteCrypto.Extension), blob);
        t.Note("عادية", "نص فيه كلمة حساسة أيضاً");

        var hits = t.Vault.Search("حساسة").ToList();

        Assert.All(hits, h => Assert.False(NoteCrypto.IsEncrypted(h.FilePath)));
        Assert.Single(t.Vault.EncryptedNotes());
    }
}
