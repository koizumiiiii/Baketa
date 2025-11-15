using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;

namespace Baketa.Core.UI.Overlay.Positioning;

/// <summary>
/// オーバーレイ位置・サイズ情報（計算結果）
/// </summary>
/// <param name="position">計算された位置</param>
/// <param name="size">計算されたサイズ</param>
/// <param name="sourceTextRegion">元になったテキスト領域</param>
/// <param name="monitor">表示対象モニター</param>
/// <param name="calculationMethod">計算方法</param>
public readonly record struct OverlayPositionInfo(
    CorePoint Position,
    CoreSize Size,
    TextRegion? SourceTextRegion,
    MonitorInfo Monitor,
    PositionCalculationMethod CalculationMethod)
{
    /// <summary>
    /// 有効な位置情報かどうか
    /// </summary>
    public bool IsValid => Size.Width > 0 && Size.Height > 0;

    /// <summary>
    /// 境界矩形
    /// </summary>
    public CoreRect Bounds => new(Position.X, Position.Y, Size.Width, Size.Height);
}

/// <summary>
/// オーバーレイ位置更新イベント引数
/// </summary>
/// <param name="previousPosition">前回の位置</param>
/// <param name="newPosition">新しい位置</param>
/// <param name="previousSize">前回のサイズ</param>
/// <param name="newSize">新しいサイズ</param>
/// <param name="reason">更新理由</param>
/// <param name="timestamp">更新時刻</param>
public sealed class OverlayPositionUpdatedEventArgs(
    CorePoint previousPosition,
    CorePoint newPosition,
    CoreSize previousSize,
    CoreSize newSize,
    PositionUpdateReason reason,
    DateTimeOffset timestamp) : EventArgs
{
    /// <summary>
    /// 前回の位置
    /// </summary>
    public CorePoint PreviousPosition { get; } = previousPosition;

    /// <summary>
    /// 新しい位置
    /// </summary>
    public CorePoint NewPosition { get; } = newPosition;

    /// <summary>
    /// 前回のサイズ
    /// </summary>
    public CoreSize PreviousSize { get; } = previousSize;

    /// <summary>
    /// 新しいサイズ
    /// </summary>
    public CoreSize NewSize { get; } = newSize;

    /// <summary>
    /// 更新理由
    /// </summary>
    public PositionUpdateReason Reason { get; } = reason;

    /// <summary>
    /// 更新時刻
    /// </summary>
    public DateTimeOffset Timestamp { get; } = timestamp;

    /// <summary>
    /// 位置が変更されたかどうか
    /// </summary>
    public bool PositionChanged => PreviousPosition != NewPosition;

    /// <summary>
    /// サイズが変更されたかどうか
    /// </summary>
    public bool SizeChanged => PreviousSize != NewSize;
}

/// <summary>
/// ゲームウィンドウ情報（Baketa.UI.Monitors.MonitorInfoと整合）
/// </summary>
public sealed class GameWindowInfo
{
    /// <summary>
    /// ウィンドウハンドル
    /// </summary>
    public required nint WindowHandle { get; init; }

    /// <summary>
    /// ウィンドウタイトル
    /// </summary>
    public required string WindowTitle { get; init; } = string.Empty;

    /// <summary>
    /// ウィンドウ位置
    /// </summary>
    public required CorePoint Position { get; init; }

    /// <summary>
    /// ウィンドウサイズ
    /// </summary>
    public required CoreSize Size { get; init; }

    /// <summary>
    /// クライアント領域位置
    /// </summary>
    public required CorePoint ClientPosition { get; init; }

    /// <summary>
    /// クライアント領域サイズ
    /// </summary>
    public required CoreSize ClientSize { get; init; }

    /// <summary>
    /// フルスクリーンかどうか
    /// </summary>
    public required bool IsFullScreen { get; init; }

    /// <summary>
    /// 最大化されているかどうか
    /// </summary>
    public required bool IsMaximized { get; init; }

    /// <summary>
    /// 最小化されているかどうか
    /// </summary>
    public required bool IsMinimized { get; init; }

    /// <summary>
    /// アクティブかどうか
    /// </summary>
    public required bool IsActive { get; init; }

    /// <summary>
    /// ディスプレイモニター情報（Issue 71 MonitorInfoクラスとの整合性確保）
    /// </summary>
    public required MonitorInfo Monitor { get; init; }

    /// <summary>
    /// クライアント領域の境界矩形
    /// </summary>
    public CoreRect ClientBounds => new(ClientPosition.X, ClientPosition.Y, ClientSize.Width, ClientSize.Height);

    /// <summary>
    /// ウィンドウ境界矩形
    /// </summary>
    public CoreRect WindowBounds => new(Position.X, Position.Y, Size.Width, Size.Height);
}

/// <summary>
/// 翻訳情報（自動サイズ調整に必要な情報）
/// </summary>
public sealed class TranslationInfo
{
    /// <summary>
    /// 元テキスト
    /// </summary>
    public required string SourceText { get; init; } = string.Empty;

    /// <summary>
    /// 翻訳テキスト
    /// </summary>
    public required string TranslatedText { get; init; } = string.Empty;

    /// <summary>
    /// 関連するOCR検出領域
    /// </summary>
    public TextRegion? SourceRegion { get; init; }

    /// <summary>
    /// 翻訳リクエストID
    /// </summary>
    public Guid TranslationId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// テキスト測定情報（自動計算される）
    /// </summary>
    public TextMeasurementInfo? MeasurementInfo { get; init; }

    /// <summary>
    /// 翻訳完了時刻
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>
    /// 有効な翻訳情報かどうか
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(TranslatedText);
}

/// <summary>
/// テキスト測定情報（固定フォント設定での測定結果）
/// </summary>
/// <param name="textSize">必要なテキスト表示サイズ</param>
/// <param name="lineCount">行数</param>
/// <param name="characterCount">文字数</param>
/// <param name="usedFontSize">使用されたフォントサイズ（固定値の確認用）</param>
/// <param name="fontFamily">使用されたフォントファミリー</param>
/// <param name="measuredAt">測定時刻</param>
public readonly record struct TextMeasurementInfo(
    CoreSize TextSize,
    int LineCount,
    int CharacterCount,
    double UsedFontSize,
    string FontFamily,
    DateTimeOffset MeasuredAt)
{
    /// <summary>
    /// 有効な測定結果かどうか
    /// </summary>
    public bool IsValid => TextSize.Width > 0 && TextSize.Height > 0 && LineCount > 0;

    /// <summary>
    /// 推奨オーバーレイサイズ（パディング考慮）
    /// </summary>
    public CoreSize RecommendedOverlaySize => new(
        TextSize.Width + 20,  // 左右パディング
        TextSize.Height + 20  // 上下パディング
    );
}

/// <summary>
/// OCR検出テキスト領域（PaddleOCR連携用）
/// </summary>
/// <param name="bounds">テキスト領域の境界矩形</param>
/// <param name="text">検出されたテキスト</param>
/// <param name="confidence">検出精度</param>
/// <param name="detectedAt">検出時刻</param>
public readonly record struct TextRegion(
    CoreRect Bounds,
    string Text,
    double Confidence,
    DateTimeOffset DetectedAt)
{
    /// <summary>
    /// 有効なテキスト領域かどうか
    /// </summary>
    public bool IsValid => Bounds.Width > 0 && Bounds.Height > 0 && !string.IsNullOrWhiteSpace(Text);

    /// <summary>
    /// 中心点
    /// </summary>
    public CorePoint Center => new(
        Bounds.X + Bounds.Width / 2,
        Bounds.Y + Bounds.Height / 2
    );
}

/// <summary>
/// 位置計算方法
/// </summary>
public enum PositionCalculationMethod
{
    /// <summary>
    /// OCR領域ベース - 直下配置
    /// </summary>
    OcrBelowText,

    /// <summary>
    /// OCR領域ベース - 上部配置
    /// </summary>
    OcrAboveText,

    /// <summary>
    /// OCR領域ベース - 右側配置
    /// </summary>
    OcrRightOfText,

    /// <summary>
    /// OCR領域ベース - 左側配置
    /// </summary>
    OcrLeftOfText,

    /// <summary>
    /// 固定位置
    /// </summary>
    FixedPosition,

    /// <summary>
    /// フォールバック（安全位置）
    /// </summary>
    FallbackPosition
}

/// <summary>
/// 位置更新理由
/// </summary>
public enum PositionUpdateReason
{
    /// <summary>
    /// OCR検出による更新
    /// </summary>
    OcrDetection,

    /// <summary>
    /// 翻訳完了による更新
    /// </summary>
    TranslationCompleted,

    /// <summary>
    /// ゲームウィンドウ変更による更新
    /// </summary>
    GameWindowChanged,

    /// <summary>
    /// モニター変更による更新
    /// </summary>
    MonitorChanged,

    /// <summary>
    /// 手動調整による更新
    /// </summary>
    ManualAdjustment,

    /// <summary>
    /// 設定変更による更新
    /// </summary>
    SettingsChanged
}

/// <summary>
/// テキスト測定オプション
/// </summary>
/// <param name="fontFamily">フォントファミリー</param>
/// <param name="fontSize">フォントサイズ</param>
/// <param name="fontWeight">フォント重み</param>
/// <param name="maxWidth">最大幅</param>
/// <param name="padding">パディング</param>
public readonly record struct TextMeasurementOptions(
    string FontFamily,
    double FontSize,
    string FontWeight,
    double MaxWidth,
    CoreThickness Padding)
{
    /// <summary>
    /// デフォルト設定
    /// </summary>
    public static TextMeasurementOptions Default => new(
        FontFamily: "Yu Gothic UI",
        FontSize: 16,
        FontWeight: "Normal",
        MaxWidth: 600,
        Padding: new CoreThickness(10)
    );
}

/// <summary>
/// テキスト測定結果
/// </summary>
/// <param name="size">測定されたサイズ</param>
/// <param name="lineCount">行数</param>
/// <param name="actualFontSize">実際に使用されたフォントサイズ</param>
/// <param name="measuredWith">測定に使用されたオプション</param>
public readonly record struct TextMeasurementResult(
    CoreSize Size,
    int LineCount,
    double ActualFontSize,
    TextMeasurementOptions MeasuredWith)
{
    /// <summary>
    /// 有効な測定結果かどうか
    /// </summary>
    public bool IsValid => Size.Width > 0 && Size.Height > 0;
}
