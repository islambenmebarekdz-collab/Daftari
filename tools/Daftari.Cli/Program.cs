using System.Text;

namespace Daftari.Cli;

/// <summary>
/// مدخل أداة الطرفية. مشروعٌ منفصل عن التطبيق عمداً: التطبيق <c>WinExe</c> بلا طرفية،
/// ويحرسه قفل نسخةٍ واحدة يُحضِر النافذة بدل تنفيذ الأمر. فأداةٌ مستقلّة أنظف
/// من محاولة إلحاق طرفيةٍ بنافذة، وتعمل والتطبيق مفتوحٌ في الوقت نفسه.
/// </summary>
static class Program
{
    static int Main(string[] args)
    {
        // القبو عربي بالكامل: بلا UTF-8 صريح تخرج الأسماء والنصوص علامات استفهام.
        try { Console.OutputEncoding = new UTF8Encoding(false); } catch { }

        string? root = null;
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] is "--vault" or "-v" && i + 1 < args.Length) { root = args[++i]; continue; }
            rest.Add(args[i]);
        }

        root ??= Settings.Load().ResolveVaultPath();

        // الفحص قبل بناء Vault: مُنشِئ القبو ينشئ المجلد، وأداةُ قراءةٍ لا تنشئ شيئاً.
        if (!Directory.Exists(root))
        {
            Console.Error.WriteLine($"القبو غير موجود: {root}");
            return Commands.UsageError;
        }

        // لا Console.In: هو يفكّ بترميز صفحة الرموز النشطة، وهي على جهازٍ عربي CP720،
        // فيصير النصّ المُمرَّر حروفاً معطوبة تُكتب في القبو. القراءة من المجرى الخام بـUTF-8.
        return Commands.Run(new Vault(root), rest.ToArray(), Console.Out,
                            Commands.Utf8Reader(Console.OpenStandardInput()));
    }
}
