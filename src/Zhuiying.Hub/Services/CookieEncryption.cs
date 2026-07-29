using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Zhuiying.Hub.Services;

public static class CookieEncryption
{
    private static readonly byte[] Key;

    static CookieEncryption()
    {
        var keyStr = Environment.GetEnvironmentVariable("COOKIE_ENCRYPTION_KEY") 
            ?? "zhuiying-default-cookie-key-32byte!";
        // Ensure key is exactly 32 bytes for AES-256
        Key = Encoding.UTF8.GetBytes(keyStr.PadRight(32, '0')[..32]);
    }

    public static string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = Key;
        aes.GenerateIV();
        
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        
        // Prepend IV to encrypted data
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);
        
        return Convert.ToBase64String(result);
    }

    public static string Decrypt(string encryptedText)
    {
        var fullCipher = Convert.FromBase64String(encryptedText);
        
        using var aes = Aes.Create();
        aes.Key = Key;
        
        // Extract IV from the beginning
        var iv = new byte[aes.BlockSize / 8];
        var cipherText = new byte[fullCipher.Length - iv.Length];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipherText, 0, cipherText.Length);
        
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
        
        return Encoding.UTF8.GetString(plainBytes);
    }
}
