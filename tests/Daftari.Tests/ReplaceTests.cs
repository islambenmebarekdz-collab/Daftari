using Xunit;

namespace Daftari.Tests;

/// <summary>منطق الاستبدال الشامل داخل الملاحظة.</summary>
public class ReplaceTests
{
    [Fact]
    public void يستبدل_كل_التطابقات_ويعيد_عددها()
    {
        var result = Vault.ReplaceAllOccurrences("المكفوفين هنا والمكفوفين هناك", "المكفوفين", "فاقدي البصر", out int n);

        Assert.Equal("فاقدي البصر هنا وفاقدي البصر هناك", result);
        Assert.Equal(2, n);
    }

    [Fact]
    public void بلا_تطابق_يعيد_النص_كما_هو()
    {
        var result = Vault.ReplaceAllOccurrences("نص بلا هدف", "مفقود", "بديل", out int n);

        Assert.Equal("نص بلا هدف", result);
        Assert.Equal(0, n);
    }

    [Fact]
    public void نص_بحث_فارغ_لا_يغيّر_شيئاً()
    {
        var result = Vault.ReplaceAllOccurrences("أي نص", "", "بديل", out int n);

        Assert.Equal("أي نص", result);
        Assert.Equal(0, n);
    }

    [Fact]
    public void الاستبدال_بنص_فارغ_يحذف_التطابقات()
    {
        var result = Vault.ReplaceAllOccurrences("أ ب أ ب", "ب ", "", out int n);

        Assert.Equal("أ أ ب", result);
        Assert.Equal(1, n);
    }

    [Fact]
    public void يتجاهل_حالة_الأحرف_اللاتينية()
    {
        var result = Vault.ReplaceAllOccurrences("Note note NOTE", "note", "ملاحظة", out int n);

        Assert.Equal("ملاحظة ملاحظة ملاحظة", result);
        Assert.Equal(3, n);
    }

    [Fact]
    public void نص_استبدال_يحوي_نص_البحث_لا_يدخل_حلقة_لا_نهائية()
    {
        // التنفيذ الساذج يعيد مسح النص الناتج فيستبدل إلى ما لا نهاية
        var result = Vault.ReplaceAllOccurrences("قط", "قط", "قطقط", out int n);

        Assert.Equal("قطقط", result);
        Assert.Equal(1, n);
    }

    [Fact]
    public void تطابقات_متلاصقة()
    {
        var result = Vault.ReplaceAllOccurrences("ااا", "ا", "ب", out int n);

        Assert.Equal("ببب", result);
        Assert.Equal(3, n);
    }
}
