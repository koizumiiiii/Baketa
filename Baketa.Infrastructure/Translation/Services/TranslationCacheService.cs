using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// 翻訳キャッシュサービス
/// </summary>
public interface ITranslationCacheService
{
    /// <summary>
    /// キャッシュから翻訳結果を取得
    /// </summary>
    /// <param name="sourceText">元テキスト</param>
    /// <param name="sourceLang">元言語</param>
    /// <param name="targetLang">対象言語</param>
    /// <param name="engineName">エンジン名</param>
    /// <returns>キャッシュされた翻訳結果</returns>
    Task<string?> GetCachedTranslationAsync(string sourceText, string sourceLang, string targetLang, string engineName);
    
    /// <summary>
    /// 翻訳結果をキャッシュに保存
    /// </summary>
    /// <param name="sourceText">元テキスト</param>
    /// <param name="sourceLang">元言語</param>
    /// <param name="targetLang">対象言語</param>
    /// <param name="engineName">エンジン名</param>
    /// <param name="translatedText">翻訳結果</param>
    /// <param name="expirationMinutes">有効期限（分）</param>
    /// <returns>保存完了を示すタスク</returns>
    Task SetCachedTranslationAsync(string sourceText, string sourceLang, string targetLang, string engineName, string translatedText, int expirationMinutes);
    
    /// <summary>
    /// キャッシュをクリア
    /// </summary>
    /// <returns>クリア完了を示すタスク</returns>
    Task ClearCacheAsync();
}

/// <summary>
/// メモリベース翻訳キャッシュサービス
/// </summary>
public class MemoryTranslationCacheService : ITranslationCacheService
{
    private readonly Dictionary<string, (string Translation, DateTimeOffset Expiration)> _cache = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    
    /// <inheritdoc/>
    public Task<string?> GetCachedTranslationAsync(string sourceText, string sourceLang, string targetLang, string engineName)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        ArgumentNullException.ThrowIfNull(engineName);
        var key = GenerateKey(sourceText, sourceLang, targetLang, engineName);
        
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var cached))
            {
                if (cached.Expiration > DateTimeOffset.UtcNow)
                {
                    return Task.FromResult<string?>(cached.Translation);
                }
                else
                {
                    _cache.Remove(key);
                }
            }
        }
        
        return Task.FromResult<string?>(null);
    }
    
    /// <inheritdoc/>
    public Task SetCachedTranslationAsync(string sourceText, string sourceLang, string targetLang, string engineName, string translatedText, int expirationMinutes)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        ArgumentNullException.ThrowIfNull(sourceLang);
        ArgumentNullException.ThrowIfNull(targetLang);
        ArgumentNullException.ThrowIfNull(engineName);
        ArgumentNullException.ThrowIfNull(translatedText);
        var key = GenerateKey(sourceText, sourceLang, targetLang, engineName);
        var expiration = DateTimeOffset.UtcNow.AddMinutes(expirationMinutes);
        
        lock (_lock)
        {
            _cache[key] = (translatedText, expiration);
        }
        
        return Task.CompletedTask;
    }
    
    /// <inheritdoc/>
    public Task ClearCacheAsync()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
        
        return Task.CompletedTask;
    }
    
    /// <summary>
    /// キャッシュキーを生成
    /// </summary>
    private static string GenerateKey(string sourceText, string sourceLang, string targetLang, string engineName)
    {
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{engineName}:{sourceLang}->{targetLang}:{sourceText.GetHashCode(StringComparison.Ordinal):X8}");
    }
}