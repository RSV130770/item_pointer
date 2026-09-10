using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace LedMatrixControl
{
    public class AppUser
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string PinHash { get; set; } = "";
    }

    /// <summary>
    /// Loads users from users.json and authenticates by PIN. PINs are never
    /// stored in plain text - only a salted SHA-256 hash. This is a simple
    /// convenience gate for logging who's operating the machine, not a
    /// high-security login system.
    /// </summary>
    public class UserStore
    {
        private const string Salt = "LedMatrixPickTaskSalt";

        private readonly List<AppUser> _users = new();

        public UserStore(string path)
        {
            Load(path);
        }

        private void Load(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var json = File.ReadAllText(path);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var users = JsonSerializer.Deserialize<List<AppUser>>(json, options);
                if (users != null)
                    _users.AddRange(users);
            }
            catch
            {
                // Corrupt/unreadable users file: nobody can log in, but the
                // app should not crash outright.
            }
        }

        public bool TryAuthenticate(string pin, out AppUser user)
        {
            var hash = HashPin(pin);
            foreach (var candidate in _users)
            {
                if (string.Equals(candidate.PinHash, hash, StringComparison.OrdinalIgnoreCase))
                {
                    user = candidate;
                    return true;
                }
            }
            user = null;
            return false;
        }

        public static string HashPin(string pin)
        {
            using var sha = SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(Salt + (pin ?? ""));
            var hash = sha.ComputeHash(bytes);
            var sb = new StringBuilder();
            foreach (var b in hash)
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
