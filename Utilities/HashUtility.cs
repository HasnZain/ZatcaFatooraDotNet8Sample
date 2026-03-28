using System.Security.Cryptography;
using System.Text;

namespace ZatcaFatooraDotNet8Sample.Utilities;

public static class HashUtility
{
    public static string Sha256Base64(string text)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToBase64String(sha.ComputeHash(bytes));
    }

    public static byte[] Sha256Bytes(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return sha.ComputeHash(bytes);
    }
}
