using System.Security.Cryptography;
using System.Text;

namespace Daftari;

/// <summary>مفتاح جلسة لملاحظة مقفلة: الملح مع المفتاح المشتق منه، يبقى في الذاكرة فقط.</summary>
public sealed class NoteKey
{
    public byte[] Salt { get; init; } = Array.Empty<byte>();
    public byte[] Key { get; init; } = Array.Empty<byte>();

    /// <summary>يمحو المفتاح من الذاكرة عند إقفال الجلسة.</summary>
    public void Wipe() => CryptographicOperations.ZeroMemory(Key);
}

/// <summary>
/// تشفير الملاحظات الحساسة بأدوات قياسية مُراجَعة فقط:
/// AES-256-GCM (تشفير موثّق يكشف أي عبث) مع اشتقاق مفتاح PBKDF2-HMAC-SHA256.
/// رأس الملف يحمل رقم إصدار ومعرّف خوارزمية الاشتقاق وعدد الدورات، كي تبقى الملفات
/// القديمة قابلة للفتح بعد أي ترقية مستقبلية (إلى Argon2id مثلاً).
/// الرأس نفسه يدخل في التحقق (associated data) فلا يمكن العبث بمعاملاته دون كشف.
/// </summary>
public static class NoteCrypto
{
    public const string Extension = ".md.enc";
    /// <summary>الحد الذي توصي به OWASP حالياً لـ PBKDF2-HMAC-SHA256.</summary>
    public const int Iterations = 600_000;
    public const int MinPasswordLength = 8;

    static readonly byte[] Magic = { (byte)'D', (byte)'F', (byte)'T', (byte)'R' };
    const byte FormatVersion = 1;
    const byte KdfPbkdf2Sha256 = 1;
    const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32;

    public static bool IsEncrypted(string path) =>
        path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, iterations,
                                  HashAlgorithmName.SHA256, KeySize);

    /// <summary>ينشئ مفتاحاً بملح جديد عشوائي — عند قفل ملاحظة لأول مرة.</summary>
    public static NoteKey CreateKey(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        return new NoteKey { Salt = salt, Key = DeriveKey(password, salt, Iterations) };
    }

    /// <summary>يشتق المفتاح بملح ملف موجود — عند فتح ملاحظة مقفلة.</summary>
    public static NoteKey DeriveKeyFor(byte[] file, string password)
    {
        var (salt, iterations, _, _, _) = ParseHeader(file);
        return new NoteKey { Salt = salt, Key = DeriveKey(password, salt, iterations) };
    }

    /// <summary>يشفّر نص الملاحظة بمفتاح الجلسة ورقم استخدام جديد في كل مرة.</summary>
    public static byte[] Encrypt(string plainText, NoteKey noteKey)
    {
        // رقم الاستخدام يجب أن يكون فريداً لكل عملية تشفير بنفس المفتاح
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var header = BuildHeader(noteKey.Salt, Iterations, nonce);
        var plain = Encoding.UTF8.GetBytes(plainText);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using (var aes = new AesGcm(noteKey.Key, TagSize))
            aes.Encrypt(nonce, plain, cipher, tag, header);
        CryptographicOperations.ZeroMemory(plain);

        var output = new byte[header.Length + TagSize + cipher.Length];
        header.CopyTo(output, 0);
        tag.CopyTo(output, header.Length);
        cipher.CopyTo(output, header.Length + TagSize);
        return output;
    }

    /// <summary>
    /// يفك التشفير ويتحقق من السلامة. يرمي CryptographicException عند كلمة مرور خاطئة
    /// أو ملف معبوث به — لا نفرّق بينهما عمداً كي لا نمنح مهاجماً أي إشارة.
    /// </summary>
    public static string Decrypt(byte[] file, NoteKey noteKey)
    {
        var (_, _, nonce, headerLength, _) = ParseHeader(file);
        if (file.Length < headerLength + TagSize) throw new CryptographicException("ملف تالف");

        var header = file.AsSpan(0, headerLength).ToArray();
        var tag = file.AsSpan(headerLength, TagSize).ToArray();
        var cipher = file.AsSpan(headerLength + TagSize).ToArray();
        var plain = new byte[cipher.Length];

        using (var aes = new AesGcm(noteKey.Key, TagSize))
            aes.Decrypt(nonce, cipher, tag, plain, header);   // يرمي إن فشل التحقق

        var text = Encoding.UTF8.GetString(plain);
        CryptographicOperations.ZeroMemory(plain);
        return text;
    }

    static byte[] BuildHeader(byte[] salt, int iterations, byte[] nonce)
    {
        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.WriteByte(FormatVersion);
        ms.WriteByte(KdfPbkdf2Sha256);
        ms.Write(BitConverter.GetBytes(iterations));
        ms.WriteByte((byte)salt.Length);
        ms.Write(salt);
        ms.Write(nonce);
        return ms.ToArray();
    }

    static (byte[] Salt, int Iterations, byte[] Nonce, int HeaderLength, byte Version) ParseHeader(byte[] file)
    {
        if (file.Length < Magic.Length + 2 + 4 + 1)
            throw new CryptographicException("ملف غير صالح");
        for (int i = 0; i < Magic.Length; i++)
            if (file[i] != Magic[i]) throw new CryptographicException("ليس ملف ملاحظة مقفلة");

        int at = Magic.Length;
        byte version = file[at++];
        if (version != FormatVersion) throw new CryptographicException($"إصدار صيغة غير مدعوم: {version}");
        byte kdf = file[at++];
        if (kdf != KdfPbkdf2Sha256) throw new CryptographicException("خوارزمية اشتقاق غير مدعومة");

        int iterations = BitConverter.ToInt32(file, at); at += 4;
        if (iterations < 10_000) throw new CryptographicException("عدد دورات غير مقبول");
        int saltLen = file[at++];
        if (saltLen is < 8 or > 64 || file.Length < at + saltLen + NonceSize)
            throw new CryptographicException("ملف تالف");

        var salt = file.AsSpan(at, saltLen).ToArray(); at += saltLen;
        var nonce = file.AsSpan(at, NonceSize).ToArray(); at += NonceSize;
        return (salt, iterations, nonce, at, version);
    }

    public enum Strength { Weak, Medium, Strong }

    /// <summary>تقدير بسيط لقوة كلمة المرور يُعرض للمستخدم (ليس ضماناً أمنياً).</summary>
    public static Strength Rate(string password)
    {
        int score = 0;
        if (password.Length >= 12) score++;
        if (password.Length >= 16) score++;
        if (password.Trim().Contains(' ')) score++;                // عبارة مرور من عدة كلمات
        if (password.Any(char.IsDigit)) score++;
        if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
        return score switch { <= 1 => Strength.Weak, 2 or 3 => Strength.Medium, _ => Strength.Strong };
    }
}
