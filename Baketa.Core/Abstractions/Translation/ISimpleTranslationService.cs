using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Models.Primitives;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// シンプルな翻訳サービスインターフェース
/// 複雑な多層アダプターパターンを排除し、直接的な翻訳処理を提供
/// </summary>
public interface ISimpleTranslationService
{
    /// <summary>
    /// ウィンドウ情報を基に統合翻訳処理を実行
    /// キャプチャ→OCR→翻訳→結果返却の全処理を包含
    /// </summary>
    /// <param name="windowInfo">翻訳対象ウィンドウ情報</param>
    /// <param name="cancellationToken">キャンセル用トークン</param>
    /// <returns>翻訳処理結果</returns>
    Task<SimpleTranslationResult> ProcessTranslationAsync(
        WindowInfo windowInfo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在の処理状態を取得
    /// </summary>
    TranslationServiceStatus Status { get; }

    /// <summary>
    /// 状態変化を通知するReactiveプロパティ
    /// ReactiveUIとの連携に使用
    /// </summary>
    IObservable<TranslationServiceStatus> StatusChanges { get; }

    /// <summary>
    /// サービスの停止処理
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 翻訳処理結果
/// </summary>
public sealed record SimpleTranslationResult
{
    /// <summary>
    /// 処理が成功したか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 翻訳されたテキスト
    /// </summary>
    public string? TranslatedText { get; init; }

    /// <summary>
    /// 検出されたテキスト領域情報
    /// </summary>
    public TextRegionInfo[]? TextRegions { get; init; }

    /// <summary>
    /// エラー情報（失敗時）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 処理時間（診断用）
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
}

/// <summary>
/// ウィンドウ情報
/// </summary>
public sealed record WindowInfo
{
    /// <summary>
    /// ウィンドウハンドル
    /// </summary>
    public required IntPtr WindowHandle { get; init; }

    /// <summary>
    /// ウィンドウタイトル
    /// </summary>
    public required string WindowTitle { get; init; }

    /// <summary>
    /// キャプチャ領域（nullの場合はウィンドウ全体）
    /// </summary>
    public Rect? CaptureRegion { get; init; }
}

/// <summary>
/// テキスト領域情報
/// </summary>
public sealed record TextRegionInfo
{
    /// <summary>
    /// テキスト領域の座標
    /// </summary>
    public required Rect Bounds { get; init; }

    /// <summary>
    /// 元のテキスト
    /// </summary>
    public required string OriginalText { get; init; }

    /// <summary>
    /// 翻訳後テキスト
    /// </summary>
    public required string TranslatedText { get; init; }

    /// <summary>
    /// OCRの信頼度
    /// </summary>
    public float Confidence { get; init; }
}

/// <summary>
/// 翻訳サービスの状態
/// </summary>
public enum TranslationServiceStatus
{
    /// <summary>
    /// 停止中
    /// </summary>
    Stopped,

    /// <summary>
    /// 待機中
    /// </summary>
    Ready,

    /// <summary>
    /// 処理中
    /// </summary>
    Processing,

    /// <summary>
    /// エラー状態
    /// </summary>
    Error
}