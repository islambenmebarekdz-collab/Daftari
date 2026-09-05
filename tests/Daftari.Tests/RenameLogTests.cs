using Xunit;

namespace Daftari.Tests;

/// <summary>
/// سجلّ القبو: أثرٌ دائم للأحداث التي تُتلف معلومة — إعادة التسمية تُزيل الاسم القديم،
/// والحذف يُخرج الملاحظة من الفهرس، والنقل المتعارض يعيد التسمية بصمت.
/// النقل الذي يبقي الاسم لا يُسجَّل: لا يُتلف شيئاً، والملاحظة تبقى قابلة للحلّ باسمها.
/// </summary>
public class RenameLogTests
{
    /// <summary>يحاكي ما تفعله الواجهة: تنقل الملف ثم تُعلم القبو.</summary>
    static string RenameOnDisk(TempVault t, string path, string newName)
    {
        var dest = Path.Combine(Path.GetDirectoryName(path)!, newName + Path.GetExtension(path));
        if (Directory.Exists(path)) Directory.Move(path, dest); else File.Move(path, dest);
        t.Vault.RecordRename(path, dest);
        return dest;
    }

    [Fact]
    public void RecordRename_يسجّل_الاسم_القديم_والجديد_كمسارين_نسبيين()
    {
        using var t = new TempVault();
        var path = t.Note("مجلد/فكرة قديمة.md", "# فكرة قديمة");

        RenameOnDisk(t, path, "فكرة جديدة");

        var e = Assert.Single(t.Vault.Renames());
        Assert.Equal("rename", e.Kind);
        Assert.Equal("note", e.ItemType);
        Assert.Equal(Path.Combine("مجلد", "فكرة قديمة"), e.From);
        Assert.Equal(Path.Combine("مجلد", "فكرة جديدة"), e.To);
    }

    [Fact]
    public void RecordRename_يميّز_المجلد_عن_الملاحظة()
    {
        using var t = new TempVault();
        var folder = t.Folder("مجلد قديم");

        RenameOnDisk(t, folder, "مجلد جديد");

        Assert.Equal("folder", Assert.Single(t.Vault.Renames()).ItemType);
    }

    [Fact]
    public void MoveToTrash_يسجّل_الحذف_مع_وجهته_في_المحذوفات()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "محتوى");

        t.Vault.MoveToTrash(path);

        var e = Assert.Single(t.Vault.Renames());
        Assert.Equal("trash", e.Kind);
        Assert.Equal("ملاحظة", e.From);
        Assert.Equal(Path.Combine(Vault.TrashFolderName, "ملاحظة"), e.To);
    }

    [Fact]
    public void MoveTo_بلا_تعارض_لا_يسجّل_شيئاً()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "محتوى");
        var dest = t.Folder("وجهة");

        t.Vault.MoveTo(path, dest);

        Assert.Empty(t.Vault.Renames());
    }

    [Fact]
    public void MoveTo_مع_تعارض_يسجّل_إعادة_التسمية_الصامتة()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "المنقولة");
        var dest = t.Folder("وجهة");
        t.Note("وجهة/ملاحظة.md", "الموجودة سلفاً");

        var moved = t.Vault.MoveTo(path, dest);

        Assert.Equal("ملاحظة 2.md", Path.GetFileName(moved));
        var e = Assert.Single(t.Vault.Renames());
        Assert.Equal("rename", e.Kind);
        Assert.Equal("ملاحظة", e.From);
        Assert.Equal(Path.Combine("وجهة", "ملاحظة 2"), e.To);
    }

    [Fact]
    public void Renames_يعيد_الأحدث_أولاً()
    {
        using var t = new TempVault();
        var first = RenameOnDisk(t, t.Note("أولى.md"), "أولى معدّلة");
        RenameOnDisk(t, t.Note("ثانية.md"), "ثانية معدّلة");

        var events = t.Vault.Renames();

        Assert.Equal(2, events.Count);
        Assert.Equal("ثانية", events[0].From);
        Assert.Equal("أولى", events[1].From);
    }

    [Fact]
    public void Renames_يرشّح_بالاسم_في_القديم_والجديد_معاً()
    {
        using var t = new TempVault();
        RenameOnDisk(t, t.Note("دافعية.md"), "دافعية الإنجاز");
        RenameOnDisk(t, t.Note("شيء آخر.md"), "شيء مختلف");

        Assert.Single(t.Vault.Renames("دافعية"));          // يطابق الطرفين
        Assert.Single(t.Vault.Renames("الإنجاز"));         // يطابق الجديد وحده
        Assert.Single(t.Vault.Renames("شيء آخر"));         // يطابق القديم وحده
        Assert.Empty(t.Vault.Renames("لا وجود له"));
    }

    [Fact]
    public void سؤال_عن_ملاحظة_داخل_مجلد_أُعيدت_تسميته_يجيبه_حدث_المجلد()
    {
        using var t = new TempVault();
        t.Note("مجلد قديم/ملاحظة.md", "محتوى");
        RenameOnDisk(t, Path.Combine(t.Root, "مجلد قديم"), "مجلد جديد");

        // الملاحظة نفسها لم تُسجَّل — سطرٌ واحد للمجلد يغطّي كل ما تحته
        var e = Assert.Single(t.Vault.Renames("مجلد قديم/ملاحظة"));
        Assert.Equal("folder", e.ItemType);
        Assert.Equal("مجلد قديم", e.From);
        Assert.Equal("مجلد جديد", e.To);

        Assert.Single(t.Vault.Renames(@"مجلد قديم\ملاحظة"));   // الفاصل الآخر يطابق أيضاً
    }

    [Fact]
    public void المطابقة_بالسابقة_لا_تتعدى_إلى_أحداث_الملاحظات()
    {
        using var t = new TempVault();
        RenameOnDisk(t, t.Note("ملاحظة.md"), "ملاحظة معدّلة");

        // «ملاحظة» ليست مجلداً، فلا يجوز أن يطابقها سؤالٌ عن مسارٍ يبدأ باسمها
        Assert.Empty(t.Vault.Renames("ملاحظة/شيء تحتها"));
    }

    [Fact]
    public void سطر_تالف_في_السجلّ_لا_يُسقط_قراءة_بقيته()
    {
        using var t = new TempVault();
        RenameOnDisk(t, t.Note("سليمة.md"), "سليمة معدّلة");
        var log = Path.Combine(t.Root, Vault.MetaFolderName, Vault.RenameLogName);
        File.AppendAllText(log, "سطر تالف بلا حقول\nتاريخ-غير-صالح\trename\tnote\tأ\tب\n");

        Assert.Equal("سليمة", Assert.Single(t.Vault.Renames()).From);
    }

    [Fact]
    public void تعذّر_الكتابة_في_السجلّ_لا_يمنع_الحذف()
    {
        using var t = new TempVault();
        var path = t.Note("ملاحظة.md", "محتوى");
        // ملفٌ مكان مجلد البيانات: كل محاولة إنشاء أو كتابة تفشل
        File.WriteAllText(Path.Combine(t.Root, Vault.MetaFolderName), "عائق");

        var dest = t.Vault.MoveToTrash(path);

        Assert.True(File.Exists(dest));
        Assert.False(File.Exists(path));
        Assert.Empty(t.Vault.Renames());
    }

    [Fact]
    public void مجلد_البيانات_مستبعد_من_الفهرس_والبحث()
    {
        using var t = new TempVault();
        t.Note("ملاحظة.md", "محتوى ظاهر");
        var meta = t.Folder(Vault.MetaFolderName);
        File.WriteAllText(Path.Combine(meta, "مخبوءة.md"), "محتوى مخبوء");

        Assert.DoesNotContain(t.Vault.AllNotes(), p => p.Contains("مخبوءة"));
        Assert.Empty(t.Vault.Search("مخبوء"));
    }
}
