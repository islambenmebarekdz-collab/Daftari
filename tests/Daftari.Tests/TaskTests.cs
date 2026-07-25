using Xunit;

namespace Daftari.Tests;

/// <summary>منطق المهام: تحويل الأسطر، وجمع مهام القبو، وتغيير حالتها على القرص.</summary>
public class TaskTests
{
    [Fact]
    public void السطر_العادي_يصير_مهمة_مفتوحة()
    {
        var result = Vault.ToggleTaskLine("مراجعة الفصل الثالث", out bool isDone);

        Assert.Equal("- [ ] مراجعة الفصل الثالث", result);
        Assert.False(isDone);
    }

    [Fact]
    public void عنصر_القائمة_يصير_مهمة_مع_الحفاظ_على_علامته()
    {
        Assert.Equal("- [ ] بند", Vault.ToggleTaskLine("- بند", out _));
        Assert.Equal("* [ ] بند", Vault.ToggleTaskLine("* بند", out _));
        Assert.Equal("+ [ ] بند", Vault.ToggleTaskLine("+ بند", out _));
    }

    [Fact]
    public void المهمة_المفتوحة_تصير_منجزة_والعكس()
    {
        var done = Vault.ToggleTaskLine("- [ ] مهمة", out bool isDone1);
        Assert.Equal("- [x] مهمة", done);
        Assert.True(isDone1);

        var open = Vault.ToggleTaskLine(done, out bool isDone2);
        Assert.Equal("- [ ] مهمة", open);
        Assert.False(isDone2);
    }

    [Fact]
    public void يقبل_علامة_الإنجاز_بحرف_كبير()
    {
        Assert.Equal("- [ ] مهمة", Vault.ToggleTaskLine("- [X] مهمة", out bool isDone));
        Assert.False(isDone);
    }

    [Fact]
    public void يحافظ_على_المسافة_البادئة_للمهام_المتداخلة()
    {
        Assert.Equal("    - [x] فرعية", Vault.ToggleTaskLine("    - [ ] فرعية", out _));
        Assert.Equal("  - [ ] نص مزاح", Vault.ToggleTaskLine("  نص مزاح", out _));
    }

    [Fact]
    public void السطر_الفارغ_يصير_مهمة_فارغة_جاهزة_للكتابة()
    {
        Assert.Equal("- [ ] ", Vault.ToggleTaskLine("", out _));
    }

    [Fact]
    public void Tasks_يجمع_المهام_المفتوحة_من_كل_الملاحظات()
    {
        using var t = new TempVault();
        t.Note("أولى", "# أولى\n- [ ] مهمة أولى\nنص عادي\n- [x] منجزة");
        t.Note("ثانية", "- [ ] مهمة ثانية");

        var open = t.Vault.Tasks().ToList();

        Assert.Equal(2, open.Count);
        Assert.Contains(open, x => x.Text == "مهمة أولى");
        Assert.Contains(open, x => x.Text == "مهمة ثانية");
        Assert.All(open, x => Assert.False(x.Done));
    }

    [Fact]
    public void Tasks_يمكن_أن_يشمل_المنجزة()
    {
        using var t = new TempVault();
        t.Note("ملاحظة", "- [ ] مفتوحة\n- [x] منجزة");

        var all = t.Vault.Tasks(includeDone: true).ToList();

        Assert.Equal(2, all.Count);
        Assert.Contains(all, x => x.Done && x.Text == "منجزة");
    }

    [Fact]
    public void Tasks_يتجاهل_المربع_الفارغ_بلا_نص()
    {
        using var t = new TempVault();
        t.Note("ملاحظة", "- [ ] \n- [ ] مهمة حقيقية");

        var open = t.Vault.Tasks().ToList();

        Assert.Single(open);
        Assert.Equal("مهمة حقيقية", open[0].Text);
    }

    [Fact]
    public void Tasks_يسجّل_رقم_السطر_الصحيح()
    {
        using var t = new TempVault();
        t.Note("ملاحظة", "# عنوان\n\n- [ ] مهمة في السطر الثالث");

        var task = t.Vault.Tasks().Single();

        Assert.Equal(2, task.LineNumber);
    }

    [Fact]
    public void SetTaskDone_يعدّل_الملف_على_القرص()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة", "- [ ] مهمة\nسطر آخر");

        var ok = t.Vault.SetTaskDone(note, 0, done: true);

        Assert.True(ok);
        Assert.Contains("- [x] مهمة", File.ReadAllText(note));
        Assert.Contains("سطر آخر", File.ReadAllText(note));
        Assert.Empty(t.Vault.Tasks());
    }

    [Fact]
    public void SetTaskDone_يعيد_المهمة_مفتوحة()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة", "- [x] مهمة");

        Assert.True(t.Vault.SetTaskDone(note, 0, done: false));
        Assert.Single(t.Vault.Tasks());
    }

    [Fact]
    public void SetTaskDone_يرفض_سطراً_ليس_مهمة()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة", "نص عادي فقط");

        Assert.False(t.Vault.SetTaskDone(note, 0, done: true));
        Assert.Equal("نص عادي فقط", File.ReadAllText(note));
    }

    [Fact]
    public void SetTaskDone_يرفض_رقم_سطر_خارج_المدى()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة", "- [ ] مهمة");

        Assert.False(t.Vault.SetTaskDone(note, 99, done: true));
    }
}
