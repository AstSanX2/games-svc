using System.Security.Cryptography;
using System.Text;

namespace Helpers.Extensions
{
    public static class StringExtensions
    {
        public static string ToHash(this string input)
        {
            using SHA256 sha256 = SHA256.Create();

            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hashBytes = sha256.ComputeHash(inputBytes);
            string hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();

            return hash[..Math.Min(hash.Length, 60)];
        }
    }

}
