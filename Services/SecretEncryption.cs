using System.Security.Cryptography;
using System.Text;

namespace ConnectBackend.Services;

public static class SecretEncryption
{
    public const string DefaultAlgorithm = "AES-256-CBC";
    public const string FallbackKey = "ConnectEV_Secret_AES256_Key_2027_#@!998877";

    public static byte[] DeriveKey(string passphrase)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(passphrase));
    }

    public static string Encrypt(string plainText, string? passphrase = null)
    {
        if (string.IsNullOrEmpty(plainText)) return string.Empty;
        var key = DeriveKey(string.IsNullOrWhiteSpace(passphrase) ? FallbackKey : passphrase);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        var iv = aes.IV;

        using var encryptor = aes.CreateEncryptor(aes.Key, iv);
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var combined = new byte[iv.Length + cipherBytes.Length];
        Buffer.BlockCopy(iv, 0, combined, 0, iv.Length);
        Buffer.BlockCopy(cipherBytes, 0, combined, iv.Length, cipherBytes.Length);

        return Convert.ToBase64String(combined);
    }

    public static string Decrypt(string cipherBase64, string? passphrase = null)
    {
        if (string.IsNullOrWhiteSpace(cipherBase64)) return string.Empty;
        var key = DeriveKey(string.IsNullOrWhiteSpace(passphrase) ? FallbackKey : passphrase);
        var combined = Convert.FromBase64String(cipherBase64.Trim());

        if (combined.Length < 16)
            throw new ArgumentException("Invalid cipher payload length.");

        var iv = new byte[16];
        var cipherBytes = new byte[combined.Length - 16];
        Buffer.BlockCopy(combined, 0, iv, 0, 16);
        Buffer.BlockCopy(combined, 16, cipherBytes, 0, cipherBytes.Length);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }
}
