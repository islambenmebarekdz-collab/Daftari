using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Daftari.Tests;

/// <summary>
/// ضمانات تشفير الملاحظات: السرية، وكشف العبث، وعدم تكرار الناتج، ورفض المدخلات الفاسدة.
/// هذه أخطر وحدة في التطبيق، فأي تغيير فيها يجب أن يُبقي كل هذه الفحوص خضراء.
/// </summary>
public class NoteCryptoTests
{
    const string Secret = "# سري\n\nبيانات حساسة: تشخيص المريض ورقم الملف";
    const string Password = "عبارة مرور طويلة وقوية 123";

    [Fact]
    public void النص_الأصلي_لا_يظهر_في_الملف_المشفّر()
    {
        var blob = NoteCrypto.Encrypt(Secret, NoteCrypto.CreateKey(Password));

        var asText = Encoding.UTF8.GetString(blob);
        Assert.DoesNotContain("حساسة", asText);
        Assert.DoesNotContain("المريض", asText);
    }

    [Fact]
    public void الملف_يبدأ_بترويسة_معروفة()
    {
        var blob = NoteCrypto.Encrypt(Secret, NoteCrypto.CreateKey(Password));

        Assert.Equal(new byte[] { (byte)'D', (byte)'F', (byte)'T', (byte)'R' }, blob.Take(4).ToArray());
    }

    [Fact]
    public void دورة_كاملة_بكلمة_المرور_الصحيحة()
    {
        var blob = NoteCrypto.Encrypt(Secret, NoteCrypto.CreateKey(Password));

        var text = NoteCrypto.Decrypt(blob, NoteCrypto.DeriveKeyFor(blob, Password));

        Assert.Equal(Secret, text);
    }

    [Fact]
    public void كلمة_المرور_الخاطئة_تُرفض()
    {
        var blob = NoteCrypto.Encrypt(Secret, NoteCrypto.CreateKey(Password));

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            NoteCrypto.Decrypt(blob, NoteCrypto.DeriveKeyFor(blob, "كلمة مرور أخرى تماماً")));
    }

    [Fact]
    public void العبث_بمحتوى_الملف_يُكشف()
    {
        var blob = NoteCrypto.Encrypt(Secret, NoteCrypto.CreateKey(Password));
        blob[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() =>
            NoteCrypto.Decrypt(blob, NoteCrypto.DeriveKeyFor(blob, Password)));
    }

    [Fact]
    public void العبث_بمعاملات_الترويسة_يُكشف()
    {
        var blob = NoteCrypto.Encrypt(Secret, NoteCrypto.CreateKey(Password));
        // تخفيض عدد دورات الاشتقاق في الترويسة لإضعاف الحماية
        BitConverter.GetBytes(20_000).CopyTo(blob, 6);

        Assert.ThrowsAny<CryptographicException>(() =>
            NoteCrypto.Decrypt(blob, NoteCrypto.DeriveKeyFor(blob, Password)));
    }

    [Fact]
    public void تشفير_النص_نفسه_مرتين_ينتج_ملفين_مختلفين()
    {
        var key = NoteCrypto.CreateKey(Password);

        var a = NoteCrypto.Encrypt(Secret, key);
        var b = NoteCrypto.Encrypt(Secret, key);

        Assert.False(a.SequenceEqual(b));                    // رقم استخدام جديد كل مرة
        Assert.Equal(Secret, NoteCrypto.Decrypt(a, key));
        Assert.Equal(Secret, NoteCrypto.Decrypt(b, key));
    }

    [Fact]
    public void ملح_ومفتاح_مختلفان_لكل_ملاحظة_رغم_تطابق_كلمة_المرور()
    {
        var a = NoteCrypto.CreateKey(Password);
        var b = NoteCrypto.CreateKey(Password);

        Assert.False(a.Salt.SequenceEqual(b.Salt));
        Assert.False(a.Key.SequenceEqual(b.Key));
    }

    [Theory]
    [InlineData("")]
    [InlineData("نص فيه رموز 🔐 وحروف mixed content")]
    [InlineData("سطر\r\nوسطر آخر\tوجدولة")]
    public void يحافظ_على_النص_كما_هو(string text)
    {
        var key = NoteCrypto.CreateKey(Password);

        Assert.Equal(text, NoteCrypto.Decrypt(NoteCrypto.Encrypt(text, key), key));
    }

    [Fact]
    public void ملف_غير_صالح_يُرفض_بوضوح()
    {
        var garbage = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

        Assert.ThrowsAny<CryptographicException>(() => NoteCrypto.DeriveKeyFor(garbage, Password));
    }

    [Fact]
    public void كشف_امتداد_الملاحظة_المقفلة()
    {
        Assert.True(NoteCrypto.IsEncrypted("ملاحظة.md.enc"));
        Assert.False(NoteCrypto.IsEncrypted("ملاحظة.md"));
    }

    [Fact]
    public void Wipe_يمحو_المفتاح_من_الذاكرة()
    {
        var key = NoteCrypto.CreateKey(Password);
        Assert.Contains(key.Key, b => b != 0);

        key.Wipe();

        Assert.All(key.Key, b => Assert.Equal(0, b));
    }

    [Theory]
    [InlineData("12345678", NoteCrypto.Strength.Weak)]
    [InlineData("عبارة مرور طويلة وقوية 123!", NoteCrypto.Strength.Strong)]
    public void تقدير_قوة_كلمة_المرور(string password, NoteCrypto.Strength expected)
    {
        Assert.Equal(expected, NoteCrypto.Rate(password));
    }
}
