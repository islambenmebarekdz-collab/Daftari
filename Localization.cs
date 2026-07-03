namespace Daftari;

/// <summary>نصوص الواجهة بالعربية والإنجليزية؛ العربية هي الأصل والافتراضي.</summary>
public static class L
{
    public static bool En;

    public static string T(string ar, string en) => En ? en : ar;

    public static RightToLeft Rtl => En ? RightToLeft.No : RightToLeft.Yes;
    public static bool RtlLayout => !En;

    public static MessageBoxOptions MsgOptions =>
        En ? 0 : MessageBoxOptions.RightAlign | MessageBoxOptions.RtlReading;
}
