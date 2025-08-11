using System;
using System.Collections.Generic;
using System.Drawing;

namespace Baketa.Core.Logging;

/// <summary>
/// OCR処理結果の構造化ログエントリ
/// </summary>
public record OcrResultLogEntry
{
    /// <summary>
    /// 操作を一意に識別するID
    /// </summary>
    public required string OperationId { get; init; }
    
    /// <summary>
    /// ログエントリのタイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
    
    /// <summary>
    /// 処理ステージ（preprocessing, detection, recognition, postprocessing）
    /// </summary>
    public required string Stage { get; init; }
    
    /// <summary>
    /// 処理対象画像のサイズ
    /// </summary>
    public Size ImageSize { get; init; }
    
    /// <summary>
    /// 検出されたテキスト領域の数
    /// </summary>
    public int TextRegionsFound { get; init; }
    
    /// <summary>
    /// 平均信頼度スコア（0.0-1.0）
    /// </summary>
    public double AverageConfidence { get; init; }
    
    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public double ProcessingTimeMs { get; init; }
    
    /// <summary>
    /// パフォーマンス内訳（各ステップの処理時間）
    /// </summary>
    public Dictionary<string, double> PerformanceBreakdown { get; init; } = [];
    
    /// <summary>
    /// 認識されたテキスト内容のリスト
    /// </summary>
    public List<string> RecognizedTexts { get; init; } = [];
    
    /// <summary>
    /// 使用されたOCRエンジン名
    /// </summary>
    public string? Engine { get; init; }
    
    /// <summary>
    /// エラー情報（エラーが発生した場合）
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 翻訳処理結果の構造化ログエントリ
/// </summary>
public record TranslationResultLogEntry
{
    /// <summary>
    /// 操作を一意に識別するID
    /// </summary>
    public required string OperationId { get; init; }
    
    /// <summary>
    /// ログエントリのタイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
    
    /// <summary>
    /// 使用された翻訳エンジン名
    /// </summary>
    public required string Engine { get; init; }
    
    /// <summary>
    /// 翻訳言語ペア（例: "ja-en"）
    /// </summary>
    public required string LanguagePair { get; init; }
    
    /// <summary>
    /// 入力テキスト
    /// </summary>
    public required string InputText { get; init; }
    
    /// <summary>
    /// 出力テキスト
    /// </summary>
    public required string OutputText { get; init; }
    
    /// <summary>
    /// 翻訳品質の信頼度（0.0-1.0）
    /// </summary>
    public double Confidence { get; init; }
    
    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public double ProcessingTimeMs { get; init; }
    
    /// <summary>
    /// 入力トークン数
    /// </summary>
    public int InputTokenCount { get; init; }
    
    /// <summary>
    /// 出力トークン数
    /// </summary>
    public int OutputTokenCount { get; init; }
    
    /// <summary>
    /// バッチ処理の場合の総チャンク数
    /// </summary>
    public int? TotalChunks { get; init; }
    
    /// <summary>
    /// バッチ処理の場合の現在のチャンクインデックス
    /// </summary>
    public int? ChunkIndex { get; init; }
    
    /// <summary>
    /// キャッシュヒット情報
    /// </summary>
    public bool CacheHit { get; init; }
    
    /// <summary>
    /// エラー情報（エラーが発生した場合）
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// パフォーマンス分析の構造化ログエントリ
/// </summary>
public record PerformanceLogEntry
{
    /// <summary>
    /// 操作を一意に識別するID
    /// </summary>
    public required string OperationId { get; init; }
    
    /// <summary>
    /// ログエントリのタイムスタンプ
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.Now;
    
    /// <summary>
    /// パフォーマンス計測の対象操作名
    /// </summary>
    public required string OperationName { get; init; }
    
    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public double DurationMs { get; init; }
    
    /// <summary>
    /// CPU使用率（0.0-1.0）
    /// </summary>
    public double CpuUsage { get; init; }
    
    /// <summary>
    /// メモリ使用量（バイト）
    /// </summary>
    public long MemoryUsageBytes { get; init; }
    
    /// <summary>
    /// 処理開始時のメモリ使用量（バイト）
    /// </summary>
    public long MemoryUsageBeforeBytes { get; init; }
    
    /// <summary>
    /// 処理終了時のメモリ使用量（バイト）
    /// </summary>
    public long MemoryUsageAfterBytes { get; init; }
    
    /// <summary>
    /// スレッド数
    /// </summary>
    public int ThreadCount { get; init; }
    
    /// <summary>
    /// ボトルネック分析結果
    /// </summary>
    public Dictionary<string, object> BottleneckAnalysis { get; init; } = [];
    
    /// <summary>
    /// 追加のメタデータ
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = [];
    
    /// <summary>
    /// パフォーマンス警告レベル（Normal, Warning, Critical）
    /// </summary>
    public PerformanceLevel Level { get; init; } = PerformanceLevel.Normal;
}

/// <summary>
/// パフォーマンス警告レベル
/// </summary>
public enum PerformanceLevel
{
    /// <summary>
    /// 正常レベル
    /// </summary>
    Normal,
    
    /// <summary>
    /// 警告レベル
    /// </summary>
    Warning,
    
    /// <summary>
    /// 重要レベル（ボトルネック検出）
    /// </summary>
    Critical
}