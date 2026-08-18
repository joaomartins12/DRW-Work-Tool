using Microsoft.Data.SqlClient;
using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DRW_Work_Tool.Core
{
    public enum DatabaseConnectionState
    {
        NotConfigured,
        Checking,
        Connected,
        Failed
    }

    /// <summary>
    /// Stores the SQL connection string encrypted with Windows DPAPI.
    ///
    /// DataProtectionScope.CurrentUser means the encrypted blob is tied to
    /// the current Windows user profile. Copying the file to another Windows
    /// account / machine does not reveal the plaintext connection string.
    ///
    /// NOTE:
    /// No local-secret mechanism can protect a credential from malicious code
    /// already running as the SAME Windows user. DPAPI is intended to protect
    /// the secret at rest and from other user contexts.
    /// </summary>
    public static class DatabaseConnectionStore
    {
        private static readonly byte[] Entropy =
            Encoding.UTF8.GetBytes(
                "DRW.Work.Tool.Database.Connection.v1");

        public static string SettingsFolder =>
            Path.Combine(
                AppPaths.Root,
                "Settings");

        public static string EncryptedFile =>
            Path.Combine(
                SettingsFolder,
                "database.connection");

        public static bool Exists =>
            File.Exists(EncryptedFile);

        public static void Save(
            string connectionString)
        {
            if (string.IsNullOrWhiteSpace(
                connectionString))
            {
                throw new InvalidDataException(
                    "A connection string está vazia.");
            }

            // Validate syntax before storing the secret.
            _ =
                new SqlConnectionStringBuilder(
                    connectionString);

            Directory.CreateDirectory(
                SettingsFolder);

            byte[] plain =
                Encoding.UTF8.GetBytes(
                    connectionString.Trim());

            byte[] protectedBytes =
                ProtectedData.Protect(
                    plain,
                    Entropy,
                    DataProtectionScope.CurrentUser);

            string temporary =
                EncryptedFile + ".tmp";

            File.WriteAllBytes(
                temporary,
                protectedBytes);

            File.Move(
                temporary,
                EncryptedFile,
                overwrite: true);

            CryptographicOperations.ZeroMemory(
                plain);
        }

        public static string Load()
        {
            if (!File.Exists(
                EncryptedFile))
            {
                return string.Empty;
            }

            byte[] encrypted =
                File.ReadAllBytes(
                    EncryptedFile);

            byte[] plain =
                ProtectedData.Unprotect(
                    encrypted,
                    Entropy,
                    DataProtectionScope.CurrentUser);

            try
            {
                return Encoding.UTF8.GetString(
                    plain);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(
                    plain);
            }
        }

        public static void Delete()
        {
            if (File.Exists(
                EncryptedFile))
            {
                File.Delete(
                    EncryptedFile);
            }
        }

        public static string GetSafeDescription(
            string connectionString)
        {
            if (string.IsNullOrWhiteSpace(
                connectionString))
            {
                return "Not configured";
            }

            try
            {
                var builder =
                    new SqlConnectionStringBuilder(
                        connectionString);

                string server =
                    string.IsNullOrWhiteSpace(
                        builder.DataSource)
                        ? "?"
                        : builder.DataSource;

                string database =
                    string.IsNullOrWhiteSpace(
                        builder.InitialCatalog)
                        ? "?"
                        : builder.InitialCatalog;

                string auth =
                    builder.IntegratedSecurity
                        ? "Windows Authentication"
                        : string.IsNullOrWhiteSpace(
                            builder.UserID)
                            ? "SQL Authentication"
                            : $"SQL User {builder.UserID}";

                return
                    $"{server} / {database} / {auth}";
            }
            catch
            {
                return "Configured connection";
            }
        }
    }
}
