using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// テキストチャンクの時間軸集約を行うサービスのインターフェース
/// Phase 26-1: Clean Architecture修正 - Application層でのInfrastructure層抽象化
/// 翻訳品質向上のための文章ぶつ切り防止機能を提供します
/// </summary>
/// <remarks>
/// Issue #78 Phase 4: Cloud AI翻訳統合
/// - SetImageContext を追加（並列翻訳用画像データ設定）
/// </remarks>
public interface ITextChunkAggregatorService
{
    /// <summary>
    /// 単一のテキストチャンクをTimedAggregatorに送信して時間軸集約を試行します
    /// </summary>
    /// <param name="chunk">送信するTextChunk</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>チャンク送信成功時: true, 集約無効またはエラー時: false</returns>
    Task<bool> TryAddTextChunkAsync(TextChunk chunk, CancellationToken cancellationToken = default);

    /// <summary>
    /// [Issue #227] 複数のテキストチャンクをバッチで追加します（N+1ロック問題解消）
    /// </summary>
    /// <param name="chunks">送信するTextChunkのコレクション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>追加に成功したチャンク数</returns>
    Task<int> TryAddTextChunksBatchAsync(IReadOnlyList<TextChunk> chunks, CancellationToken cancellationToken = default);

    /// <summary>
    /// TimedAggregator機能が有効かどうかを示します
    /// </summary>
    bool IsFeatureEnabled { get; }

    /// <summary>
    /// 現在の集約待機チャンク数を取得します
    /// </summary>
    int PendingChunksCount { get; }

    /// <summary>
    /// [Issue #78 Phase 4] Cloud AI翻訳用の画像コンテキストを設定
    /// 次回のAggregatedChunksReadyEvent発行時に画像データが含まれます
    /// </summary>
    /// <param name="imageBase64">画像データ（Base64エンコード）</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    void SetImageContext(string imageBase64, int width, int height);

    /// <summary>
    /// [Issue #78 Phase 4] 画像コンテキストをクリア
    /// </summary>
    void ClearImageContext();
}
