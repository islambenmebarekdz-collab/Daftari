using Xunit;

namespace Daftari.Tests;

/// <summary>القوالب: اكتشافها، وملء متغيّراتها، وموضع المؤشر، واستثناء مهامها.</summary>
public class TemplateTests
{
    static readonly DateTime Now = new(2026, 7, 25, 14, 30, 0);

    [Fact]
    public void Templates_تجد_ملاحظات_مجلد_القوالب_فقط()
    {
        using var t = new TempVault();
        t.Folder(Vault.TemplatesFolderName);
        t.Note(Path.Combine(Vault.TemplatesFolderName, "اجتماع.md"), "# {{العنوان}}");
        t.Note(Path.Combine(Vault.TemplatesFolderName, "دراسة.md"), "# {{العنوان}}");
        t.Note("ملاحظة عادية.md", "نص");

        var templates = t.Vault.Templates().Select(p => t.Vault.DisplayName(p)).ToList();

        Assert.Equal(2, templates.Count);
        Assert.Contains("اجتماع", templates);
        Assert.DoesNotContain("ملاحظة عادية", templates);
    }

    [Fact]
    public void Templates_فارغة_إن_لم_يوجد_المجلد()
    {
        using var t = new TempVault();

        Assert.Empty(t.Vault.Templates());
    }

    [Fact]
    public void DailyTemplate_يجد_القالب_المسمى_يومية()
    {
        using var t = new TempVault();
        t.Folder(Vault.TemplatesFolderName);
        t.Note(Path.Combine(Vault.TemplatesFolderName, "يومية.md"), "# {{التاريخ_رقمي}}");
        t.Note(Path.Combine(Vault.TemplatesFolderName, "أخرى.md"), "نص");

        var daily = t.Vault.DailyTemplate();

        Assert.NotNull(daily);
        Assert.Equal("يومية", t.Vault.DisplayName(daily!));
    }

    [Fact]
    public void DailyTemplate_يقبل_الاسم_الإنجليزي()
    {
        using var t = new TempVault();
        t.Folder(Vault.TemplatesFolderName);
        t.Note(Path.Combine(Vault.TemplatesFolderName, "Daily.md"), "# {{isodate}}");

        Assert.NotNull(t.Vault.DailyTemplate());
    }

    [Fact]
    public void DailyTemplate_فارغ_إن_لم_يوجد()
    {
        using var t = new TempVault();
        t.Folder(Vault.TemplatesFolderName);
        t.Note(Path.Combine(Vault.TemplatesFolderName, "اجتماع.md"), "نص");

        Assert.Null(t.Vault.DailyTemplate());
    }

    [Fact]
    public void ApplyTemplate_يملأ_العنوان_والتاريخ_والوقت()
    {
        var result = Vault.ApplyTemplate(
            "# {{العنوان}}\nالتاريخ: {{التاريخ}}\nالساعة: {{الوقت}}\nرقمي: {{التاريخ_رقمي}}",
            "مذكرة التخرج", Now, "الجمعة 25 يوليو 2026", out _);

        Assert.Contains("# مذكرة التخرج", result);
        Assert.Contains("التاريخ: الجمعة 25 يوليو 2026", result);
        Assert.Contains("الساعة: 14:30", result);
        Assert.Contains("رقمي: 2026-07-25", result);
    }

    [Fact]
    public void ApplyTemplate_يقبل_الأسماء_الإنجليزية_والمسافات_داخل_الأقواس()
    {
        var result = Vault.ApplyTemplate("{{ title }} — {{TITLE}} — {{ Time }}", "عنوان", Now, "تاريخ", out _);

        Assert.Equal("عنوان — عنوان — 14:30", result);
    }

    [Fact]
    public void ApplyTemplate_يحدد_موضع_المؤشر_ويزيل_علامته()
    {
        var result = Vault.ApplyTemplate("# {{العنوان}}\n\n{{المؤشر}}\n\n## التفاصيل",
                                         "فكرة", Now, "تاريخ", out int caret);

        Assert.DoesNotContain("{{المؤشر}}", result);
        Assert.True(caret > 0);
        Assert.Equal("# فكرة\n\n", result[..caret]);      // المؤشر يقع بعد العنوان مباشرة
    }

    [Fact]
    public void ApplyTemplate_بلا_علامة_مؤشر_يعيد_سالب_واحد()
    {
        Vault.ApplyTemplate("# {{العنوان}}", "عنوان", Now, "تاريخ", out int caret);

        Assert.Equal(-1, caret);
    }

    [Fact]
    public void ApplyTemplate_يترك_المتغيّرات_غير_المعروفة_كما_هي()
    {
        var result = Vault.ApplyTemplate("{{العنوان}} و {{متغيّر غير معروف}}", "عنوان", Now, "تاريخ", out _);

        Assert.Equal("عنوان و {{متغيّر غير معروف}}", result);
    }

    [Fact]
    public void ApplyTemplate_لا_يغيّر_نصاً_بلا_متغيّرات()
    {
        const string plain = "# عنوان ثابت\n\nنص عادي بلا متغيّرات";

        Assert.Equal(plain, Vault.ApplyTemplate(plain, "س", Now, "ت", out _));
    }

    [Fact]
    public void مهام_القوالب_لا_تظهر_في_قائمة_المهام()
    {
        using var t = new TempVault();
        t.Folder(Vault.TemplatesFolderName);
        t.Note(Path.Combine(Vault.TemplatesFolderName, "قالب.md"), "- [ ] مهمة نموذجية في القالب");
        t.Note("عمل.md", "- [ ] مهمة حقيقية");

        var tasks = t.Vault.Tasks().ToList();

        Assert.Single(tasks);
        Assert.Equal("مهمة حقيقية", tasks[0].Text);
    }
}
