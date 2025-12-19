using System;
using System.Security.Cryptography;
using System.Text;

namespace Banking_Application.Services
{
   
    // Application-Level Encryption (ALE) for PII using AES-GCM (authenticated encryption).
  
    public sealed class CryptoService
    {
        private readonly byte[] key;

        public CryptoService(byte[] key)
        {
            if (key == null || key.Length != 32)
                throw new ArgumentException("AES-256 key must be 32 bytes.", nameof(key));

            this.key = key;
        }

        
        // Encrypts plaintext and returns a versioned token suitable for TEXT columns.
        
        public string EncryptToToken(string plaintext)
        {
            plaintext ??= string.Empty;

            byte[] pt = Encoding.UTF8.GetBytes(plaintext);
            byte[] nonce = RandomNumberGenerator.GetBytes(12);   
            byte[] ct = new byte[pt.Length];
            byte[] tag = new byte[16];                           

            using (var aes = new AesGcm(key))
            {
                
                aes.Encrypt(nonce, pt, ct, tag);
            }

            return $"v1:{Convert.ToBase64String(nonce)}:{Convert.ToBase64String(ct)}:{Convert.ToBase64String(tag)}";
        }

        
        // Decrypts a token created by EncryptToToken().
        //If the value is not encrypted (legacy plaintext), it is returned unchanged.
        
        public string DecryptToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return string.Empty;

           
            if (!token.StartsWith("v1:", StringComparison.Ordinal))
                return token;

            string[] parts = token.Split(':');
            if (parts.Length != 4)
                throw new CryptographicException("Invalid encrypted token format.");

            byte[] nonce = Convert.FromBase64String(parts[1]);
            byte[] ct = Convert.FromBase64String(parts[2]);
            byte[] tag = Convert.FromBase64String(parts[3]);

            byte[] pt = new byte[ct.Length];

            using (var aes = new AesGcm(key))
            {
                aes.Decrypt(nonce, ct, tag, pt);
            }

            return Encoding.UTF8.GetString(pt);
        }
    }
}

