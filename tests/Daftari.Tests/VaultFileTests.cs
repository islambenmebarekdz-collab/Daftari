using Xunit;

namespace Daftari.Tests;

/// <summary>عمليات الملفات: النقل، سلة المحذوفات، تعارض الأسماء، والنسخ الجانبية.</summary>
public class VaultFileTests
{
    [Fact]
    public void AllFolders_يشمل_الجذر_والفرعية_ويستبعد_المحذوفات()
    {
        using var t = new TempVault();
        t.Folder("أ");
        t.Folder(Path.Combine("أ", "ب"));
        Directory.CreateDirectory(t.Vault.TrashPath);

        var folders = t.Vault.AllFolders().ToList();

        Assert.Contains(t.Root, folders);
        Assert.Contains(folders, f => f.EndsWith("أ"));
        Assert.Contains(folders, f => f.EndsWith("ب"));
        Assert.DoesNotContain(folders, f => f.Contains(Vault.TrashFolderName));
    }

    [Fact]
    public void CanMoveInto_يمنع_نقل_مجلد_إلى_أحد_أبنائه()
    {
        using var t = new TempVault();
        var parent = t.Folder("أ");
        var child = t.Folder(Path.Combine("أ", "ب"));

        Assert.False(t.Vault.CanMoveInto(parent, child, out var reason));
        Assert.Equal("descendant", reason);
    }

    [Fact]
    public void CanMoveInto_يمنع_نقل_مجلد_إلى_نفسه()
    {
        using var t = new TempVault();
        var folder = t.Folder("أ");

        Assert.False(t.Vault.CanMoveInto(folder, folder, out var reason));
        Assert.Equal("descendant", reason);
    }

    [Fact]
    public void CanMoveInto_يمنع_النقل_إلى_المجلد_الحالي_نفسه()
    {
        using var t = new TempVault();
        t.Folder("أ");
        var note = t.Note(Path.Combine("أ", "ملاحظة.md"), "# ملاحظة");

        Assert.False(t.Vault.CanMoveInto(note, t.Path_("أ"), out var reason));
        Assert.Equal("same", reason);
    }

    [Fact]
    public void CanMoveInto_يسمح_بنقل_ملاحظة_إلى_مجلد_آخر()
    {
        using var t = new TempVault();
        t.Folder("أ");
        var note = t.Note(Path.Combine("أ", "ملاحظة.md"), "# ملاحظة");

        Assert.True(t.Vault.CanMoveInto(note, t.Root, out _));
    }

    [Fact]
    public void MoveTo_يتفادى_تعارض_الأسماء_بلاحقة_رقمية()
    {
        using var t = new TempVault();
        t.Folder("أ");
        t.Note("ملاحظة.md", "الأصلية في الجذر");
        var duplicate = t.Note(Path.Combine("أ", "ملاحظة.md"), "المنقولة");

        var moved = t.Vault.MoveTo(duplicate, t.Root);

        Assert.True(File.Exists(moved));
        Assert.NotEqual("ملاحظة.md", Path.GetFileName(moved));
        Assert.Equal("الأصلية في الجذر", File.ReadAllText(t.Path_("ملاحظة.md")));
        Assert.Equal("المنقولة", File.ReadAllText(moved));
    }

    [Fact]
    public void MoveToTrash_ينقل_الملف_ولا_يحذفه()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة.md", "محتوى");

        var trashed = t.Vault.MoveToTrash(note);

        Assert.False(File.Exists(note));
        Assert.True(File.Exists(trashed));
        Assert.Single(t.Vault.TrashItems());
        Assert.Equal("محتوى", File.ReadAllText(trashed));
    }

    [Fact]
    public void RestoreFromTrash_يتفادى_طمس_ملف_بنفس_الاسم()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة.md", "القديمة");
        var trashed = t.Vault.MoveToTrash(note);
        t.Note("ملاحظة.md", "الجديدة");   // اسم مستعمل من جديد

        var restored = t.Vault.RestoreFromTrash(trashed);

        Assert.True(File.Exists(restored));
        Assert.Equal("الجديدة", File.ReadAllText(t.Path_("ملاحظة.md")));
        Assert.Equal("القديمة", File.ReadAllText(restored));
        Assert.Empty(t.Vault.TrashItems());
    }

    [Fact]
    public void EmptyTrash_يمحو_الملفات_والمجلدات()
    {
        using var t = new TempVault();
        t.Folder("مجلد");
        t.Note(Path.Combine("مجلد", "بداخله.md"), "نص");
        t.Vault.MoveToTrash(t.Path_("مجلد"));
        t.Vault.MoveToTrash(t.Note("مفردة.md", "نص"));
        Assert.Equal(2, t.Vault.TrashItems().Count());

        t.Vault.EmptyTrash();

        Assert.Empty(t.Vault.TrashItems());
    }

    [Fact]
    public void SaveSideCopy_ينشئ_ملفاً_منفصلاً_ولا_يمس_الأصل()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة.md", "الأصل على القرص");

        var copy = t.Vault.SaveSideCopy(note, "نسختي", "نسخة المستخدم");

        Assert.True(File.Exists(copy));
        Assert.Equal("نسخة المستخدم", File.ReadAllText(copy));
        Assert.Equal("الأصل على القرص", File.ReadAllText(note));
    }

    [Fact]
    public void SaveSideCopy_لا_يطمس_نسخة_سابقة_بنفس_اللاحقة()
    {
        using var t = new TempVault();
        var note = t.Note("ملاحظة.md", "الأصل");

        var first = t.Vault.SaveSideCopy(note, "نسختي", "الأولى");
        var second = t.Vault.SaveSideCopy(note, "نسختي", "الثانية");

        Assert.NotEqual(first, second);
        Assert.Equal("الأولى", File.ReadAllText(first));
        Assert.Equal("الثانية", File.ReadAllText(second));
    }

    [Fact]
    public void DisplayName_يزيل_امتداد_الملاحظة_العادية_والمقفلة()
    {
        using var t = new TempVault();

        Assert.Equal("عادية", t.Vault.DisplayName(t.Path_("عادية.md")));
        Assert.Equal("سرية", t.Vault.DisplayName(t.Path_("سرية" + NoteCrypto.Extension)));
    }
}
