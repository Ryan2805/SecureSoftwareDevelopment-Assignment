using System;
using System.IO;
using System.Security.Cryptography;

namespace Banking_Application.Services
{
   
    public sealed class KeyStore
    {
        private readonly string keyFilePath;

        public KeyStore(string keyFilePath = "encryptionKey.bin")
        {
            this.keyFilePath = keyFilePath ?? throw new ArgumentNullException(nameof(keyFilePath));
        }

        
        /// Loads a DPAPI-protected AES-256 key from disk or creates a new one if missing.
        
        public byte[] GetOrCreateAes256Key()
        {
            if (File.Exists(keyFilePath))
            {
                byte[] protectedKey = File.ReadAllBytes(keyFilePath);
                return ProtectedData.Unprotect(protectedKey, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);
            }

            // Generate a random 256-bit key (32 bytes).
            byte[] key = RandomNumberGenerator.GetBytes(32);

            // Protect the key at rest using DPAPI for the current Windows user.
            byte[] protectedBlob = ProtectedData.Protect(key, optionalEntropy: null, scope: DataProtectionScope.CurrentUser);

            File.WriteAllBytes(keyFilePath, protectedBlob);

            return key;
        }
    }
}
