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
    private readonly ILogger<WindowsCredentialStorage> _logger;

    // Instance-level credential target names (allows test isolation)
    private readonly string _accessTokenTarget;
    private readonly string _refreshTokenTarget;

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

    /// <param name="logger">Logger instance</param>
    /// <param name="targetPrefix">
    /// Optional credential target prefix. When null (default), uses the production
    /// obfuscated prefix derived from machine/user identity.
    /// Pass a custom prefix in tests to isolate test credentials from production.
    /// </param>
    public WindowsCredentialStorage(
        ILogger<WindowsCredentialStorage> logger,
        string? targetPrefix = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var prefix = targetPrefix ?? GenerateObfuscatedPrefix();
        _accessTokenTarget = $"{prefix}_AT";
        _refreshTokenTarget = $"{prefix}_RT";
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
            // RefreshToken 最小長チェック (警告のみ、保存はブロックしない)
            // 正常なSupabase RefreshTokenは40-60文字。12文字等の異常な短さでも
            // SetSessionでは動作するため、保存して次回起動時のセッション復元に使用する
            const int MinRefreshTokenLength = 32;
            if (refreshToken.Length < MinRefreshTokenLength)
            {
                _logger.LogWarning(
                    "RefreshToken is suspiciously short ({Length} chars < {MinLength} chars). " +
                    "Storing anyway as short tokens may still be valid for session restore. " +
                    "This may indicate a Supabase SDK issue.",
                    refreshToken.Length, MinRefreshTokenLength);
            }

            // トークンサイズをログ出力（デバッグ用）
            // JWT tokens contain only ASCII characters, so UTF-8 encoding uses 1 byte per character
            // This allows storing tokens up to 2560 characters (within Windows Credential Manager limit)
            var accessBytes = Encoding.UTF8.GetBytes(accessToken);
            var refreshBytes = Encoding.UTF8.GetBytes(refreshToken);
            _logger.LogDebug("Storing tokens - AccessToken: {AccessChars} chars ({AccessBytes} bytes), RefreshToken: {RefreshChars} chars ({RefreshBytes} bytes)",
                accessToken.Length, accessBytes.Length, refreshToken.Length, refreshBytes.Length);

            // Windows Credential Manager の制限は 2560 bytes
            const int MaxCredentialBlobSize = 2560;
            if (accessBytes.Length > MaxCredentialBlobSize)
            {
                _logger.LogError("AccessToken exceeds Windows Credential Manager limit: {Size} bytes > {Limit} bytes",
                    accessBytes.Length, MaxCredentialBlobSize);
                return Task.FromResult(false);
            }
            if (refreshBytes.Length > MaxCredentialBlobSize)
            {
                _logger.LogError("RefreshToken exceeds Windows Credential Manager limit: {Size} bytes > {Limit} bytes",
                    refreshBytes.Length, MaxCredentialBlobSize);
                return Task.FromResult(false);
            }

            bool accessStored = WriteCredential(_accessTokenTarget, "BaketaAccessToken", accessToken, out int accessError);
            bool refreshStored = WriteCredential(_refreshTokenTarget, "BaketaRefreshToken", refreshToken, out int refreshError);

            if (accessStored && refreshStored)
            {
                _logger.LogInformation("Authentication tokens stored successfully");
                return Task.FromResult(true);
            }

            _logger.LogWarning("Failed to store authentication tokens - AccessToken: {AccessResult} (Error: {AccessError}), RefreshToken: {RefreshResult} (Error: {RefreshError})",
                accessStored, accessError, refreshStored, refreshError);
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

            string? accessToken = ReadCredential(_accessTokenTarget);
            string? refreshToken = ReadCredential(_refreshTokenTarget);

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
            // Issue #461: スタックトレースを記録して呼び出し元を特定可能にする
            // パフォーマンス考慮: StackTraceはコストが高いためWarning有効時のみ取得
            if (_logger.IsEnabled(LogLevel.Warning))
            {
                var stackTrace = Environment.StackTrace;
                _logger.LogWarning("[TOKEN_CLEAR] ClearTokensAsync called. StackTrace:\n{StackTrace}", stackTrace);
            }

            bool accessDeleted = DeleteCredential(_accessTokenTarget);
            bool refreshDeleted = DeleteCredential(_refreshTokenTarget);

            _logger.LogWarning("[TOKEN_CLEAR] Authentication tokens cleared from Windows Credential Manager (Access: {Access}, Refresh: {Refresh})",
                accessDeleted, refreshDeleted);

            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TOKEN_CLEAR] Error clearing authentication tokens");
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
            string? accessToken = ReadCredential(_accessTokenTarget);
            string? refreshToken = ReadCredential(_refreshTokenTarget);

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
    private static bool WriteCredential(string targetName, string userName, string secret, out int win32ErrorCode)
    {
        // Use UTF-8 encoding: JWT tokens are ASCII-only, so 1 byte per character
        // This halves the storage size compared to UTF-16/Unicode encoding
        var byteArray = Encoding.UTF8.GetBytes(secret);

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
            var result = NativeMethods.CredWrite(ref credential, 0);
            win32ErrorCode = result ? 0 : Marshal.GetLastWin32Error();
            return result;
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

            return Encoding.UTF8.GetString(byteArray);
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
