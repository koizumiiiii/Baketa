using System.IO;
using System.Security.Cryptography;
using System.Text;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.Translation.Cloud;

public sealed class GoogleTranslationSettingsService : IGoogleTranslationSettingsService, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _key;
    private long _revision;

    public GoogleTranslationSettingsService(string? baseDirectory = null)
    {
        _path = Path.Combine(baseDirectory ?? BaketaSettingsPaths.BaseDirectory, "byok", "google-translation.dpapi");
        try
        {
            if (!File.Exists(_path)) return;
            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
            try { _key = Encoding.UTF8.GetString(bytes); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Fail closed without exposing credential or cryptographic details.
        }
    }

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_key);
    public long ApiKeySaveRevision => Interlocked.Read(ref _revision);
    public event EventHandler? Changed;
    public string? GetApiKey() => _key;

    public async Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsControl))
            throw new ArgumentException("Invalid credential format.", nameof(apiKey));
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var key = apiKey.Trim();
            var bytes = Encoding.UTF8.GetBytes(key);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(bytes); }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllBytesAsync(_path + ".tmp", encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(_path + ".tmp", _path, true);
            _key = key;
            Interlocked.Increment(ref _revision);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task DeleteApiKeyAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path)) File.Delete(_path);
            _key = null;
            Interlocked.Increment(ref _revision);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _gate.Dispose();
}
