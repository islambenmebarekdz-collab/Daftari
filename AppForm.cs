namespace Daftari;

/// <summary>
/// نافذة أساس لكل نوافذ التطبيق: تبتلع تفعيل شريط القوائم/قائمة النظام عبر لوحة المفاتيح
/// عندما يكون Shift مضغوطاً، فلا يتضارب مع اختصار تبديل لغة الإدخال (Alt+Shift) في ويندوز.
/// كل نافذة (رئيسية أو حوار) ترث منها لتحصل على الإصلاح موحّداً.
/// </summary>
public class AppForm : Form
{
    protected override void WndProc(ref Message m)
    {
        const int WM_SYSCOMMAND = 0x0112;
        const int SC_KEYMENU = 0xF100;
        if (m.Msg == WM_SYSCOMMAND
            && ((int)(m.WParam.ToInt64() & 0xFFF0)) == SC_KEYMENU
            && (ModifierKeys & Keys.Shift) == Keys.Shift)
        {
            return;
        }
        base.WndProc(ref m);
    }
}
