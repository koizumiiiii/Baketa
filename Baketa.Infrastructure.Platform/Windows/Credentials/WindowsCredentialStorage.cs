using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Credentials;

/// <summary>
/// Windows Credential Manager implementation for secure token storage
/// Uses Windows Credential Manager API to securely store authentication tokens
/// Target names are obfuscated to reduce attack surface
/// </summary>
public sealed class WindowsCredentialStorage : ITokenStorage
{
    // Obfuscated credential target names (not easily searchable)
    // Format: "{AppHash}_{UserHash}_{Purpose}"
    private static readonly string CredentialTargetPrefix = GenerateObfuscatedPrefix();
    private static readonly string AccessTokenTarget = $"{CredentialTargetPrefix}_AT";
    private static readonly string RefreshTokenTarget = $"{CredentialTargetPrefix}_RT";

    private readonly ILogger<WindowsCredentialStorage> _logger;

    /// <summary>
    /// Generate an obfuscated credential target prefix unique to this machine/user
    /// This makes it harder for malware to bulk-search for Baketa credentials
    /// </summary>
    private static string GenerateObfuscatedPrefix()
    {
        // Combine app identifier with machine/user specific data
        var uniqueData = $"Baketa_{Environment.MachineName}_{Environment.UserName}";
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(uniqueData));
        // Use first 8 bytes as hex string (16 chars)
        return Convert.ToHexString(hashBytes[..8]);
    }

    public WindowsCredentialStorage(ILogger<WindowsCredentialStorage> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Store authentication tokens securely in Windows Credential Manager
    /// </summary>
    public Task<bool> StoreTokensAsync(string accessToken, string refreshToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        try
        {
            _logger.LogDebug("Storing authentication tokens in Windows Credential Manager");

            bool accessStored = WriteCredential(AccessTokenTarget, "BaketaAccessToken", accessToken);
            bool refreshStored = WriteCredential(RefreshTokenTarget, "BaketaRefreshToken", refreshToken);

            if (accessStored && refreshStored)
            {
                _logger.LogInformation("Authentication tokens stored successfully");
                return Task.FromResult(true);
            }

            _logger.LogWarning("Failed to store some authentication tokens");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing authentication tokens");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Retrieve stored authentication tokens from Windows Credential Manager
    /// </summary>
    public Task<(string AccessToken, string RefreshToken)?> RetrieveTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Retrieving authentication tokens from Windows Credential Manager");

            string? accessToken = ReadCredential(AccessTokenTarget);
            string? refreshToken = ReadCredential(RefreshTokenTarget);

            if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken))
            {
                _logger.LogInformation("Authentication tokens retrieved successfully");
                return Task.FromResult<(string, string)?>((accessToken, refreshToken));
            }

            _logger.LogDebug("No stored authentication tokens found");
            return Task.FromResult<(string, string)?>(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving authentication tokens");
            return Task.FromResult<(string, string)?>(null);
        }
    }

    /// <summary>
    /// Clear all stored tokens from Windows Credential Manager
    /// </summary>
    public Task<bool> ClearTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Clearing authentication tokens from Windows Credential Manager");

            bool accessDeleted = DeleteCredential(AccessTokenTarget);
            bool refreshDeleted = DeleteCredential(RefreshTokenTarget);

            _logger.LogInformation("Authentication tokens cleared (Access: {Access}, Refresh: {Refresh})",
                accessDeleted, refreshDeleted);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error clearing authentication tokens");
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Check if tokens are currently stored
    /// </summary>
    public Task<bool> HasStoredTokensAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            string? accessToken = ReadCredential(AccessTokenTarget);
            string? refreshToken = ReadCredential(RefreshTokenTarget);

            return Task.FromResult(!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(refreshToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking for stored tokens");
            return Task.FromResult(false);
        }
    }

    #region Windows Credential Manager P/Invoke

    /// <summary>
    /// Write a credential to Windows Credential Manager
    /// </summary>
    private static bool WriteCredential(string targetName, string userName, string secret)
    {
        var byteArray = Encoding.Unicode.GetBytes(secret);

        var credential = new NativeMethods.CREDENTIAL
        {
            Type = NativeMethods.CRED_TYPE_GENERIC,
            TargetName = targetName,
            CredentialBlobSize = (uint)byteArray.Length,
            CredentialBlob = Marshal.AllocHGlobal(byteArray.Length),
            Persist = NativeMethods.CRED_PERSIST_LOCAL_MACHINE,
            UserName = userName
        };

        try
        {
            Marshal.Copy(byteArray, 0, credential.CredentialBlob, byteArray.Length);
            return NativeMethods.CredWrite(ref credential, 0);
        }
        finally
        {
            Marshal.FreeHGlobal(credential.CredentialBlob);
        }
    }

    /// <summary>
    /// Read a credential from Windows Credential Manager
    /// </summary>
    private static string? ReadCredential(string targetName)
    {
        if (!NativeMethods.CredRead(targetName, NativeMethods.CRED_TYPE_GENERIC, 0, out IntPtr credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<NativeMethods.CREDENTIAL>(credentialPtr);

            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }

            var byteArray = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, byteArray, 0, (int)credential.CredentialBlobSize);

            return Encoding.Unicode.GetString(byteArray);
        }
        finally
        {
            NativeMethods.CredFree(credentialPtr);
        }
    }

    /// <summary>
    /// Delete a credential from Windows Credential Manager
    /// </summary>
    private static bool DeleteCredential(string targetName)
    {
        return NativeMethods.CredDelete(targetName, NativeMethods.CRED_TYPE_GENERIC, 0);
    }

    #endregion

    /// <summary>
    /// Native methods for Windows Credential Manager API
    /// </summary>
    private static class NativeMethods
    {
        public const int CRED_TYPE_GENERIC = 1;
        public const int CRED_PERSIST_LOCAL_MACHINE = 2;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct CREDENTIAL
        {
            public uint Flags;
            public int Type;
            public string TargetName;
            public string? Comment;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
            public uint CredentialBlobSize;
            public IntPtr CredentialBlob;
            public int Persist;
            public uint AttributeCount;
            public IntPtr Attributes;
            public string? TargetAlias;
            public string? UserName;
        }

        [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredWrite([In] ref CREDENTIAL credential, [In] uint flags);

        [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredRead(string targetName, int type, int reservedFlag, out IntPtr credentialPtr);

        [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool CredDelete(string targetName, int type, int flags);

        [DllImport("advapi32.dll", EntryPoint = "CredFree", SetLastError = true)]
        public static extern void CredFree(IntPtr credential);
    }
}
