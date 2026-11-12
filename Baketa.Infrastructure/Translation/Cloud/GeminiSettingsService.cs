using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.Translation.Cloud;

/// <summary>CurrentUser DPAPI storage outside the retired license directory and token targets.</summary>
public sealed class GeminiSettingsService : IGeminiSettingsService, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private State _state = new(null, "gemini-flash-lite-latest", true);
    private long _revision;
    private long _apiKeySaveRevision;
    private sealed record State(string? ApiKey, string Model, bool PreferGemini);

    public GeminiSettingsService(string? baseDirectory = null)
    {
        _path = Path.Combine(baseDirectory ?? BaketaSettingsPaths.BaseDirectory, "byok", "gemini.dpapi");
        try
        {
            if (File.Exists(_path))
            {
                var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser);
                try { _state = JsonSerializer.Deserialize<State>(bytes) ?? _state; }
                finally { CryptographicOperations.ZeroMemory(bytes); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Fail closed. Never include credential contents or crypto exception details in logs.
        }
    }

    public void Dispose() => _gate.Dispose();

    public bool HasApiKey => !string.IsNullOrWhiteSpace(_state.ApiKey);
    public bool PreferGemini => _state.PreferGemini;
    public string Model => _state.Model;
    public long Revision => Interlocked.Read(ref _revision);
    public long ApiKeySaveRevision => Interlocked.Read(ref _apiKeySaveRevision);
    public event EventHandler? Changed;
    public string? GetApiKey() => _state.ApiKey;
    public async Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.Any(char.IsControl))
            throw new ArgumentException("Invalid credential format.", nameof(apiKey));
        await UpdateAsync(s => s with { ApiKey = apiKey.Trim(), PreferGemini = true }, cancellationToken).ConfigureAwait(false);
        Interlocked.Increment(ref _apiKeySaveRevision);
    }
    public Task DeleteApiKeyAsync(CancellationToken cancellationToken = default) =>
        UpdateAsync(s => s with { ApiKey = null }, cancellationToken);
    public Task SetOptionsAsync(string model, bool preferGemini, CancellationToken cancellationToken = default)
    {
        if (model is not ("gemini-flash-lite-latest" or "gemini-flash-latest"))
            throw new ArgumentException("Unsupported model.", nameof(model));
        return UpdateAsync(s => s with { Model = model, PreferGemini = preferGemini }, cancellationToken);
    }
    private async Task UpdateAsync(Func<State, State> update, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var next = update(_state);
            if (next == _state) return;
            var plaintext = JsonSerializer.SerializeToUtf8Bytes(next);
            byte[] encrypted;
            try { encrypted = ProtectedData.Protect(plaintext, null, DataProtectionScope.CurrentUser); }
            finally { CryptographicOperations.ZeroMemory(plaintext); }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            await File.WriteAllBytesAsync(_path + ".tmp", encrypted, cancellationToken).ConfigureAwait(false);
            File.Move(_path + ".tmp", _path, true);
            _state = next;
            Interlocked.Increment(ref _revision);
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
