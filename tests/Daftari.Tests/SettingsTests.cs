using Xunit;

namespace Daftari.Tests;

/// <summary>
/// حلّ مسار القبو: منطقٌ تشترك فيه الواجهة وأداة الطرفية،
/// فلا بدّ أن يعطيا القبو نفسه دائماً وإلا بحثت الأداة في مكانٍ آخر.
/// </summary>
public class SettingsTests
{
    [Fact]
    public void ResolveVaultPath_يعيد_المسار_المحفوظ_إن_كان_موجوداً()
    {
        using var t = new TempVault();
        var settings = new Settings { VaultPath = t.Root };

        Assert.Equal(t.Root, settings.ResolveVaultPath());
    }

    [Fact]
    public void ResolveVaultPath_يسقط_إلى_الافتراضي_إن_لم_يكن_محفوظاً()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Settings.DefaultVaultFolderName);

        Assert.Equal(expected, new Settings().ResolveVaultPath());
    }

    [Fact]
    public void ResolveVaultPath_يسقط_إلى_الافتراضي_إن_اختفى_القبو_المحفوظ()
    {
        var settings = new Settings
        {
            VaultPath = Path.Combine(Path.GetTempPath(), "قبو-محذوف-" + Guid.NewGuid().ToString("N")[..8])
        };

        Assert.EndsWith(Settings.DefaultVaultFolderName, settings.ResolveVaultPath());
    }
}
