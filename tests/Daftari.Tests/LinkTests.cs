using Xunit;

namespace Daftari.Tests;

/// <summary>الروابط الواردة والصادرة، والإشارات غير المرتبطة، وتحديث الروابط عند إعادة التسمية.</summary>
public class LinkTests
{
    [Fact]
    public void Backlinks_يجد_الملاحظات_المشيرة_ويستبعد_غيرها()
    {
        using var t = new TempVault();
        var target = t.Note("هدف", "# هدف");
        t.Note("مشيرة", "يشير إلى [[هدف]] هنا");
        t.Note("بعيدة", "لا علاقة لها");

        var names = t.Vault.Backlinks(target).Select(h => Path.GetFileNameWithoutExtension(h.FilePath)).ToList();

        Assert.Contains("مشيرة", names);
        Assert.DoesNotContain("بعيدة", names);
    }

    [Fact]
    public void UnlinkedMentions_يكتشف_الذكر_العادي()
    {
        using var t = new TempVault();
        var note = t.Note("فكرة", "# فكرة");
        t.Note("أخرى", "أعجبتني فكرة اليوم كثيراً");

        var names = t.Vault.UnlinkedMentions(note).Select(h => Path.GetFileNameWithoutExtension(h.FilePath)).ToList();

        Assert.Contains("أخرى", names);
    }

    [Fact]
    public void UnlinkedMentions_يتجاهل_ما_هو_داخل_رابط_صريح()
    {
        using var t = new TempVault();
        var note = t.Note("فكرة", "# فكرة");
        t.Note("مربوطة", "هذه [[فكرة]] مرتبطة");

        var names = t.Vault.UnlinkedMentions(note).Select(h => Path.GetFileNameWithoutExtension(h.FilePath)).ToList();

        Assert.DoesNotContain("مربوطة", names);
    }

    [Fact]
    public void UnlinkedMentions_يتجاهل_الاسم_حين_يكون_جزء_كلمة_أكبر()
    {
        using var t = new TempVault();
        var note = t.Note("فكرة", "# فكرة");
        t.Note("لاحقة", "الفكرة مهمة جداً");

        var names = t.Vault.UnlinkedMentions(note).Select(h => Path.GetFileNameWithoutExtension(h.FilePath)).ToList();

        Assert.DoesNotContain("لاحقة", names);
    }

    [Fact]
    public void ConvertMentionToLink_يحوّل_الإشارة_إلى_رابط_وارد()
    {
        using var t = new TempVault();
        var note = t.Note("فكرة", "# فكرة");
        t.Note("أخرى", "أعجبتني فكرة اليوم");
        var mention = t.Vault.UnlinkedMentions(note).First();

        var ok = t.Vault.ConvertMentionToLink(mention.FilePath, mention.LineNumber, "فكرة");

        Assert.True(ok);
        Assert.Contains("[[فكرة]]", File.ReadAllText(mention.FilePath));
        Assert.Empty(t.Vault.UnlinkedMentions(note));
        Assert.Contains(t.Vault.Backlinks(note), h => Path.GetFileNameWithoutExtension(h.FilePath) == "أخرى");
    }

    [Fact]
    public void OutgoingLinks_بلا_تكرار_ومع_تمييز_الهدف_المفقود()
    {
        using var t = new TempVault();
        var src = t.Note("مصدر", "# مصدر\nإلى [[هدف]] و [[مفقود]]\nومرة أخرى [[هدف]]");
        t.Note("هدف", "# هدف");

        var links = t.Vault.OutgoingLinks(src).ToList();

        Assert.Equal(2, links.Count);
        Assert.Contains(links, l => l.Target == "هدف" && l.Path != null);
        Assert.Contains(links, l => l.Target == "مفقود" && l.Path == null);
    }

    [Fact]
    public void OutgoingLinks_يسجّل_رقم_السطر_الصحيح()
    {
        using var t = new TempVault();
        var src = t.Note("مصدر", "# مصدر\nسطر فيه [[هدف]]");
        t.Note("هدف", "# هدف");

        var link = t.Vault.OutgoingLinks(src).First(l => l.Target == "هدف");

        Assert.Equal(1, link.LineNumber);
    }

    [Fact]
    public void OutgoingLinks_لملاحظة_بلا_روابط_فارغة()
    {
        using var t = new TempVault();
        var note = t.Note("بلا روابط", "# بلا روابط\nنص عادي");

        Assert.Empty(t.Vault.OutgoingLinks(note));
    }

    [Fact]
    public void UpdateLinks_يحدّث_الروابط_ويحافظ_على_اللقب_والقسم()
    {
        using var t = new TempVault();
        t.Note("هدف", "# هدف");
        var a = t.Note("أ", "رابط بسيط [[هدف]]");
        var b = t.Note("ب", "رابط بلقب [[هدف|نص بديل]] وبقسم [[هدف#مقدمة]]");

        int updated = t.Vault.UpdateLinks("هدف", "هدف جديد");

        Assert.Equal(3, updated);
        Assert.Contains("[[هدف جديد]]", File.ReadAllText(a));
        Assert.Contains("[[هدف جديد|نص بديل]]", File.ReadAllText(b));
        Assert.Contains("[[هدف جديد#مقدمة]]", File.ReadAllText(b));
    }

    [Fact]
    public void ResolveLink_يجد_الملاحظة_بالاسم()
    {
        using var t = new TempVault();
        var target = t.Note("هدف", "# هدف");

        Assert.Equal(target, t.Vault.ResolveLink("هدف"));
        Assert.Null(t.Vault.ResolveLink("غير موجودة"));
    }
}
