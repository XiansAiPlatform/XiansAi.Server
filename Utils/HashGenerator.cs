using System.Security.Cryptography;
using System.Text;

namespace XiansAi.Server.Utils;

public static class HashGenerator
{
    public static string GenerateContentHash(string content)
    {
        // Ensure content is not null
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        // Convert the string content to byte array
        byte[] contentBytes = Encoding.UTF8.GetBytes(content);

        // Create SHA1 hash
        using (SHA1 sha1 = SHA1.Create())
        {
            byte[] hashBytes = sha1.ComputeHash(contentBytes);

            // Convert byte array to hex string
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                builder.Append(hashBytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }

    // Overload for byte array input
    public static string GenerateContentHash(byte[] content)
    {
        if (content == null || content.Length == 0)
            return string.Empty;

        using (SHA1 sha1 = SHA1.Create())
        {
            byte[] hashBytes = sha1.ComputeHash(content);

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                builder.Append(hashBytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
