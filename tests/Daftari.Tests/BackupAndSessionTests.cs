using System.IO.Compression;
using Xunit;

namespace Daftari.Tests;

/// <summary>النسخ الاحتياطية واستعادتها، والملاحظات المعدّلة حديثاً، ومحتوى الجلسة المفتوح.</summary>
public class BackupAndSessionTests
{
    [Fact]
    public void BackupsIn_تعيد_النسخ_بالأحدث_أولاً()
    {
        using var t = new TempVault();
        var folder = t.Folder("نسخ");
        foreach (var (name, ago) in new[] { ("Daftari-a-2026-01-01-0000.zip", 3), ("Daftari-b-2026-02-01-0000.zip", 1) })
        {
            var p = Path.Combine(folder, name);
            File.WriteAllText(p, "x");
            File.SetLastWriteTime(p, DateTime.Now.AddDays(-ago));
        }
        File.WriteAllText(Path.Combine(folder, "ملف آخر.txt"), "x");   // ليس نسخة

        var backups = Vault.BackupsIn(folder).ToList();

        Assert.Equal(2, backups.Count);
        Assert.EndsWith("Daftari-b-2026-02-01-0000.zip", backups[0].Path);
        Assert.True(backups[0].When > backups[1].When);
    }

    [Fact]
    public void BackupsIn_مجلد_غير_موجود_يعيد_فارغاً()
    {
        Assert.Empty(Vault.BackupsIn(Path.Combine(Path.GetTempPath(), "لا-يوجد-" + Guid.NewGuid())));
    }

    [Fact]
    public void RestoreBackupTo_يفك_الضغط_في_مجلد_جديد_ولا_يمس_القبو()
    {
        using var t = new TempVault();
        t.Note("ملاحظة حالية.md", "المحتوى الحالي");
        var zipDir = t.Folder("مصدر");
        File.WriteAllText(Path.Combine(zipDir, "قديمة.md"), "محتوى النسخة");
        var zip = Path.Combine(t.Root, "نسخة.zip");
        ZipFile.CreateFromDirectory(zipDir, zip);

        var restored = Vault.RestoreBackupTo(zip, t.Root, "مستعادة");

        Assert.True(Directory.Exists(restored));
        Assert.Equal("محتوى النسخة", File.ReadAllText(Path.Combine(restored, "قديمة.md")));
        Assert.Equal("المحتوى الحالي", File.ReadAllText(t.Path_("ملاحظة حالية.md")));   // لم تُمس
    }

    [Fact]
    public void RestoreBackupTo_لا_يطمس_مجلداً_بنفس_الاسم()
    {
        using var t = new TempVault();
        var zipDir = t.Folder("مصدر");
        File.WriteAllText(Path.Combine(zipDir, "ملف.md"), "نص");
        var zip = Path.Combine(t.Root, "نسخة.zip");
        ZipFile.CreateFromDirectory(zipDir, zip);

        var first = Vault.RestoreBackupTo(zip, t.Root, "مستعادة");
        var second = Vault.RestoreBackupTo(zip, t.Root, "مستعادة");

        Assert.NotEqual(first, second);
        Assert.True(Directory.Exists(first) && Directory.Exists(second));
    }

    [Fact]
    public void RecentlyModified_ترتّب_بالأحدث_وتشمل_المقفلة()
    {
        using var t = new TempVault();
        var older = t.Note("قديمة.md", "نص");
        File.SetLastWriteTime(older, DateTime.Now.AddDays(-2));
        var newer = t.Note("حديثة.md", "نص");
        File.SetLastWriteTime(newer, DateTime.Now);
        var locked = t.Path_("مقفلة" + NoteCrypto.Extension);
        File.WriteAllBytes(locked, NoteCrypto.Encrypt("سر", NoteCrypto.CreateKey("كلمة مرور طويلة")));
        File.SetLastWriteTime(locked, DateTime.Now.AddDays(-1));

        var order = t.Vault.RecentlyModified().Select(p => t.Vault.DisplayName(p)).ToList();

        Assert.Equal(new[] { "حديثة", "مقفلة", "قديمة" }, order);
    }

    [Fact]
    public void RecentlyModified_تحترم_الحد_الأقصى()
    {
        using var t = new TempVault();
        for (int i = 0; i < 8; i++) t.Note($"ملاحظة{i}.md", "نص");

        Assert.Equal(3, t.Vault.RecentlyModified(3).Count());
    }

    [Fact]
    public void الملاحظة_المقفلة_المفتوحة_في_الجلسة_تدخل_البحث()
    {
        using var t = new TempVault();
        var locked = t.Path_("سرية" + NoteCrypto.Extension);
        File.WriteAllBytes(locked, NoteCrypto.Encrypt("# سرية\nكلمة نادرة جداً", NoteCrypto.CreateKey("كلمة مرور طويلة")));

        Assert.Empty(t.Vault.Search("نادرة"));                       // مقفلة: خارج البحث

        t.Vault.SetUnlockedContent(locked, "# سرية\nكلمة نادرة جداً");
        Assert.Contains(t.Vault.Search("نادرة"), h => h.FilePath == locked);

        t.Vault.ForgetAllUnlocked();
        Assert.Empty(t.Vault.Search("نادرة"));                       // تخرج فور الإقفال
    }

    [Fact]
    public void مهام_الملاحظة_المقفلة_المفتوحة_تظهر_ثم_تختفي_عند_الإقفال()
    {
        using var t = new TempVault();
        var locked = t.Path_("سرية" + NoteCrypto.Extension);
        File.WriteAllBytes(locked, NoteCrypto.Encrypt("- [ ] مهمة سرية", NoteCrypto.CreateKey("كلمة مرور طويلة")));

        Assert.Empty(t.Vault.Tasks());

        t.Vault.SetUnlockedContent(locked, "- [ ] مهمة سرية");
        Assert.Single(t.Vault.Tasks());

        t.Vault.ForgetUnlockedContent(locked);
        Assert.Empty(t.Vault.Tasks());
    }
}
