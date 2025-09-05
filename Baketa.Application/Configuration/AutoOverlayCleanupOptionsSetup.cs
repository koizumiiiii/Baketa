using Baketa.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Baketa.Application.Configuration;

/// <summary>
/// オーバーレイ自動削除システム設定のセットアップクラス
/// UltraThink Phase 1 + Gemini Review: IOptionsパターンによる設定外部化
/// </summary>
public sealed class AutoOverlayCleanupOptionsSetup : IConfigureOptions<AutoOverlayCleanupSettings>
{
    private readonly IConfiguration _configuration;

    public AutoOverlayCleanupOptionsSetup(IConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }

    public void Configure(AutoOverlayCleanupSettings options)
    {
        var section = _configuration.GetSection("AutoOverlayCleanup");
        if (!section.Exists())
        {
            throw new InvalidOperationException("AutoOverlayCleanup設定セクションがappsettings.jsonに見つかりません");
        }
        
        section.Bind(options);
        
        // 設定値バリデーション
        if (options.MinConfidenceScore < 0.0f || options.MinConfidenceScore > 1.0f)
        {
            throw new InvalidOperationException($"AutoOverlayCleanup.MinConfidenceScore は 0.0-1.0 の範囲で設定してください。現在値: {options.MinConfidenceScore}");
        }
        
        if (options.MaxCleanupPerSecond < 1 || options.MaxCleanupPerSecond > 100)
        {
            throw new InvalidOperationException($"AutoOverlayCleanup.MaxCleanupPerSecond は 1-100 の範囲で設定してください。現在値: {options.MaxCleanupPerSecond}");
        }
        
        if (options.TextDisappearanceChangeThreshold < 0.0f || options.TextDisappearanceChangeThreshold > 1.0f)
        {
            throw new InvalidOperationException($"AutoOverlayCleanup.TextDisappearanceChangeThreshold は 0.0-1.0 の範囲で設定してください。現在値: {options.TextDisappearanceChangeThreshold}");
        }
    }
}