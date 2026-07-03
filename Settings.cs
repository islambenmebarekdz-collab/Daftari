using System.Globalization;
using System.Text.Json;

namespace Daftari;

public class Settings
{
    public string? VaultPath { get; set; }
    public bool EditorRightToLeft { get; set; } = true;
    public float FontSize { get; set; } = 13f;
    public List<string> RecentNotes { get; set; } = new();
    /// <summary>"ar" أو "en"</summary>
    public string Language { get; set; } = "ar";
    /// <summary>"arabic" أو "algerian" أو "english" أو "numeric"</summary>
    public string DateFormat { get; set; } = "arabic";
    public string? BackupFolder { get; set; }

    static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Daftari");
    static string SettingsFile => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings();
        }
        catch { }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(SettingsFile,
                JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>ينسّق التاريخ والوقت حسب التنسيق المختار، بما فيها أسماء الأشهر الجزائرية.</summary>
    public static string FormatTimestamp(string key, DateTime now)
    {
        var ar = CultureInfo.GetCultureInfo("ar");
        switch (key)
        {
            case "algerian":
                string[] alg = { "جانفي", "فيفري", "مارس", "أفريل", "ماي", "جوان",
                                 "جويلية", "أوت", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر" };
                return $"{now.ToString("dddd", ar)} {now.Day} {alg[now.Month - 1]} {now.Year}، {now:HH:mm}";
            case "english":
                return now.ToString("dddd, MMMM d yyyy, HH:mm", CultureInfo.GetCultureInfo("en"));
            case "numeric":
                return now.ToString("yyyy-MM-dd HH:mm");
            default: // arabic
                return now.ToString("dddd d MMMM yyyy، HH:mm", ar);
        }
    }
}
