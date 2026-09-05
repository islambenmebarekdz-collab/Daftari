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

    // ---------- الكلمة من حرف واحد لا ترفع الترجيح ----------

    [Fact]
    public void حرف_الجر_المفرد_لا_يرفع_ملاحظة_لا_صلة_لها()
    {
        using var t = new TempVault();
        // لا مطابقة في العنوانين، فالترتيب بعدد الأسطر المطابقة وحده — وهنا يقع العيب:
        // «ب» حرفٌ يطابق كل سطر تقريباً، فيرفع ملاحظةً لا صلة لها فوق المقصودة
        // ولا تجاور بين الكلمتين في أيّهما، كي لا تحسمها مكافأةُ العبارة الكاملة
        t.Note("منشور لا صلة له.md", "ب ب\nنسبة التوصيل ب\nب\nب\nب\nمرحلة\n");
        t.Note("الوثيقة المقصودة.md", "مرحلة أولى\nمرحلة ثانية\nمرحلة ثالثة\nب\n");

        var first = t.Vault.Search("مرحلة ب").First();

        Assert.Equal("الوثيقة المقصودة", t.Vault.DisplayName(first.FilePath));
    }

    [Fact]
    public void الكلمة_المفردة_تبقى_شرطاً_للمطابقة_لا_للترجيح()
    {
        using var t = new TempVault();
        t.Note("فيها الاثنان.md", "مرحلة\nب\n");
        t.Note("فيها واحدة.md", "مرحلة وحدها\n");

        var names = t.Vault.Search("مرحلة ب").Select(h => t.Vault.DisplayName(h.FilePath)).Distinct().ToList();

        Assert.Contains("فيها الاثنان", names);
        Assert.DoesNotContain("فيها واحدة", names);   // «ب» غائبة فتُستبعد
    }

    [Fact]
    public void استعلام_كله_حروف_مفردة_يبقى_عاملاً()
    {
        using var t = new TempVault();
        t.Note("فيها الحرف.md", "ب هنا\n");
        t.Note("بلا الحرف.md", "لا شيء\n");

        var names = t.Vault.Search("ب").Select(h => t.Vault.DisplayName(h.FilePath)).Distinct().ToList();

        Assert.Contains("فيها الحرف", names);
    }
}
