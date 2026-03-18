using Baketa.Core.Abstractions.Settings;

namespace Baketa.Core.Settings;

/// <summary>
/// 翻訳設定クラス（UX改善対応版）
/// 自動翻訳と単発翻訳のエンジン設定を管理
/// </summary>
public sealed class TranslationSettings : ITranslationSettings
{
    /// <summary>
    /// 翻訳機能の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳機能",
        Description = "翻訳機能を有効にします")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// デフォルト翻訳エンジン
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳エンジン",
        Description = "使用する翻訳エンジン",
        ValidValues = [TranslationEngine.Gemini, TranslationEngine.NLLB200])]
    public TranslationEngine DefaultEngine { get; set; } = TranslationEngine.NLLB200;

    /// <summary>
    /// ソース言語の自動検出
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "言語自動検出",
        Description = "翻訳元言語を自動的に検出します")]
    public bool AutoDetectSourceLanguage { get; set; }

    /// <summary>
    /// デフォルトソース言語
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳元言語",
        Description = "自動検出無効時のデフォルト翻訳元言語",
        ValidValues = ["ja", "en"])]
    public string DefaultSourceLanguage { get; set; } = "en";

    /// <summary>
    /// デフォルトターゲット言語
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳先言語",
        Description = "翻訳先の言語",
        ValidValues = ["ja", "en"])]
    public string DefaultTargetLanguage { get; set; } = "ja";

    /// <summary>
    /// 翻訳遅延時間（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳遅延",
        Description = "OCR検出後、翻訳開始までの遅延時間",
        Unit = "ms",
        MinValue = 0,
        MaxValue = 5000)]
    public int TranslationDelayMs { get; set; } = 200;

    /// <summary>
    /// 翻訳キャッシュの有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳キャッシュ",
        Description = "同じテキストの翻訳結果をキャッシュして高速化します")]
    public bool EnableTranslationCache { get; set; } = true;

    /// <summary>
    /// 翻訳スタイル
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "翻訳スタイル",
        Description = "翻訳の文体・スタイル",
        ValidValues = [TranslationStyle.Natural, TranslationStyle.Literal, TranslationStyle.Formal, TranslationStyle.Casual])]
    public TranslationStyle Style { get; set; } = TranslationStyle.Natural;

    /// <summary>
    /// フォールバック翻訳エンジン
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "フォールバックエンジン",
        Description = "メインエンジンが失敗した時に使用するエンジン",
        ValidValues = [TranslationEngine.None, TranslationEngine.Gemini])]
    public TranslationEngine FallbackEngine { get; set; } = TranslationEngine.Gemini;

    /// <summary>
    /// 翻訳タイムアウト時間
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "タイムアウト時間",
        Description = "翻訳処理のタイムアウト時間（NLLB-200初回モデルロード考慮）",
        Unit = "秒",
        MinValue = 15,
        MaxValue = 300)]
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 最大文字数制限
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最大文字数",
        Description = "一度に翻訳する最大文字数",
        MinValue = 10,
        MaxValue = 10000)]
    public int MaxCharactersPerRequest { get; set; } = 1000;

    /// <summary>
    /// 最小文字数制限
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最小文字数",
        Description = "翻訳を実行する最小文字数",
        MinValue = 1,
        MaxValue = 100)]
    public int MinCharactersToTranslate { get; set; } = 2;

    /// <summary>
    /// 同一テキストの重複翻訳防止
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "重複翻訳防止",
        Description = "同じテキストの連続翻訳を防止します")]
    public bool PreventDuplicateTranslations { get; set; } = true;

    /// <summary>
    /// 重複判定の類似度閾値
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "重複判定閾値",
        Description = "テキストの類似度がこの値以上の場合重複とみなします",
        MinValue = 0.5,
        MaxValue = 1.0)]
    public double DuplicateSimilarityThreshold { get; set; } = 0.95;

    /// <summary>
    /// 並列翻訳の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "並列翻訳",
        Description = "複数のテキストを同時に翻訳して高速化します")]
    public bool EnableParallelTranslation { get; set; } = true;

    /// <summary>
    /// [Issue #78 Phase 5] Cloud AI翻訳の有効化（Pro/Premiaプラン専用）
    /// ローカルOCRとCloud AI翻訳を並列実行し、相互検証で高品質な翻訳を実現
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "Cloud AI翻訳",
        Description = "Cloud AI翻訳を使用して翻訳品質を向上させます（Pro/Premiaプラン専用）")]
    public bool EnableCloudAiTranslation { get; set; } = true;

    /// <summary>
    /// [Issue #280+#281] ローカル翻訳エンジンを使用するかどうか
    /// EnableCloudAiTranslation と連動して設定される
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "ローカル翻訳",
        Description = "ローカル翻訳エンジンを使用します")]
    public bool UseLocalEngine { get; set; } = true;

    /// <summary>
    /// [Issue #554] EXモード自動ON済みフラグ
    /// ウェルカムボーナス付与時に一度だけEXモードを自動ONにするためのフラグ。
    /// true の場合、自動ONは実行済みでありユーザーの手動設定が優先される。
    /// </summary>
    public bool ExModeAutoEnabled { get; set; }

    /// <summary>
    /// 最大並列翻訳数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最大並列数",
        Description = "同時に実行する翻訳の最大数",
        MinValue = 1,
        MaxValue = 10)]
    public int MaxParallelTranslations { get; set; } = 3;

    /// <summary>
    /// キャッシュ保持期間（時間）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "キャッシュ保持期間",
        Description = "翻訳キャッシュを保持する期間",
        Unit = "時間",
        MinValue = 1,
        MaxValue = 168)]
    public int CacheRetentionHours { get; set; } = 24;

    /// <summary>
    /// 最大キャッシュエントリ数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最大キャッシュ数",
        Description = "保持する翻訳キャッシュの最大数",
        MinValue = 100,
        MaxValue = 10000)]
    public int MaxCacheEntries { get; set; } = 1000;

    /// <summary>
    /// APIキーの暗号化保存
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "APIキー暗号化",
        Description = "翻訳サービスのAPIキーを暗号化して保存します")]
    public bool EncryptApiKeys { get; set; } = true;

    /// <summary>
    /// Google Gemini APIキー
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "Gemini APIキー",
        Description = "Google Gemini APIのキー（暗号化保存）")]
    public string GeminiApiKey { get; set; } = string.Empty;

    /// <summary>
    /// カスタム翻訳プロンプト（Gemini用）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "カスタムプロンプト",
        Description = "Gemini翻訳時のカスタムプロンプト")]
    public string CustomTranslationPrompt { get; set; } = string.Empty;

    /// <summary>
    /// ローカル翻訳モデルパス
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "ローカルモデルパス",
        Description = "ローカル翻訳モデルのファイルパス")]
    public string LocalModelPath { get; set; } = string.Empty;

    /// <summary>
    /// ローカル翻訳モデルの自動更新
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "モデル自動更新",
        Description = "ローカル翻訳モデルを自動的に更新します")]
    public bool AutoUpdateLocalModel { get; set; } = true;

    /// <summary>
    /// 翻訳品質推定の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "品質推定",
        Description = "翻訳結果の品質を推定して表示します")]
    public bool EnableQualityEstimation { get; set; }

    /// <summary>
    /// 品質推定の最小閾値
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "品質最小閾値",
        Description = "この値以下の品質の翻訳には警告を表示します",
        MinValue = 0.0,
        MaxValue = 1.0)]
    public double QualityThreshold { get; set; } = 0.7;

    /// <summary>
    /// 詳細ログ出力の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Translation", "詳細ログ",
        Description = "翻訳処理の詳細ログを出力します（開発者向け）")]
    public bool EnableVerboseLogging { get; set; }

    /// <summary>
    /// 翻訳結果の保存
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Translation", "結果保存",
        Description = "翻訳結果をファイルに保存します（開発者向け）")]
    public bool SaveTranslationResults { get; set; }

    /// <summary>
    /// API使用統計の記録
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Translation", "API統計記録",
        Description = "翻訳API使用統計を記録します（開発者向け）")]
    public bool RecordApiUsageStatistics { get; set; }

    /// <summary>
    /// 翻訳結果のフォントサイズ
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "フォントサイズ",
        Description = "翻訳結果オーバーレイのフォントサイズ（ポイント）",
        MinValue = 8,
        MaxValue = 48)]
    public int OverlayFontSize { get; set; } = 14;

    /// <summary>
    /// テキストグループ化機能を有効にする
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Translation", "文章グループ化",
        Description = "OCR結果を文章のまとまりごとにグループ化して翻訳表示します")]
    public bool EnableTextGrouping { get; set; } = true;

    /// <summary>
    /// 段落区切りを保持する
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "段落区切り保持",
        Description = "文章グループ化時に段落区切りを保持します")]
    public bool PreserveParagraphs { get; set; } = true;

    /// <summary>
    /// 同じ行と判定する閾値
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "行判定閾値",
        Description = "同じ行と判定する垂直距離の閾値（平均文字高に対する比率）",
        MinValue = 0.1,
        MaxValue = 1.0)]
    public double SameLineThreshold { get; set; } = 0.5;

    /// <summary>
    /// 段落区切り判定閾値
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "段落区切り閾値",
        Description = "段落区切りと判定する行間の閾値（平均行高に対する比率）",
        MinValue = 0.5,
        MaxValue = 3.0)]
    public double ParagraphSeparationThreshold { get; set; } = 1.5;

    /// <summary>
    /// 翻訳完了後のクールダウン時間（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "翻訳後クールダウン",
        Description = "翻訳完了後の一時停止時間（重複翻訳を防止）",
        Unit = "秒",
        MinValue = 0,
        MaxValue = 10)]
    public int PostTranslationCooldownSeconds { get; set; } = 3;

    /// <summary>
    /// 真のウィンドウキャプチャを使用する
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "真のウィンドウキャプチャ",
        Description = "PrintWindowを使用して他のウィンドウの重なりを除外したキャプチャを行います")]
    public bool UseTrueWindowCapture { get; set; } = true;

    /// <summary>
    /// 従来のキャプチャ方式を優先する（デバッグ用）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "従来キャプチャ優先",
        Description = "BitBltを優先して使用します（PrintWindowでテキスト検出に問題がある場合）")]
    public bool PreferLegacyCapture { get; set; }

    // ✨ Issue #147: 接続プール設定（Python翻訳エンジン最適化）

    /// <summary>
    /// 最大接続数。nullの場合は自動計算（CPU数/2）を使用します
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最大接続数",
        Description = "Python翻訳エンジンの最大接続数（null=自動、CPU数/2で最適化）",
        MinValue = 1,
        MaxValue = 20)]
    public int? MaxConnections { get; set; }

    /// <summary>
    /// 最小接続数。接続プールの下限を設定します
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最小接続数",
        Description = "Python翻訳エンジンの最小接続数",
        MinValue = 1,
        MaxValue = 5)]
    public int MinConnections { get; set; } = 1;

    /// <summary>
    /// 1接続あたりの最適なバッチサイズ
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最適バッチサイズ",
        Description = "1つの接続で処理する最適なテキストバッチサイズ",
        MinValue = 1,
        MaxValue = 10)]
    public int OptimalChunksPerConnection { get; set; } = 4;

    /// <summary>
    /// 接続タイムアウト時間（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "接続タイムアウト",
        Description = "Python翻訳エンジンへの接続タイムアウト時間",
        Unit = "ms",
        MinValue = 5000,
        MaxValue = 60000)]
    public int ConnectionTimeoutMs { get; set; } = 30000;

    /// <summary>
    /// 接続ヘルスチェック間隔（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "ヘルスチェック間隔",
        Description = "接続プールのヘルスチェック実行間隔",
        Unit = "ms",
        MinValue = 10000,
        MaxValue = 300000)]
    public int HealthCheckIntervalMs { get; set; } = 30000;

    /// <summary>
    /// ヘルスチェックタイムアウト（ミリ秒）
    /// Issue #189: 高負荷時の誤検出防止のため緩和（デフォルト15秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "ヘルスチェックタイムアウト",
        Description = "ヘルスチェック1回あたりのタイムアウト時間",
        Unit = "ms",
        MinValue = 5000,
        MaxValue = 60000)]
    public int HealthCheckTimeoutMs { get; set; } = 15000;

    /// <summary>
    /// 外部サーバー使用フラグ（trueの場合、独自プロセス起動を回避）
    /// Issue #147: 既存サーバー利用のための設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "外部サーバー使用",
        Description = "既に起動しているPythonサーバーを使用します（開発/テスト時用）")]
    public bool UseExternalServer { get; set; } = false;

    /// <summary>
    /// gRPCクライアント使用フラグ
    /// Phase 2.3: gRPC通信への切り替え設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "gRPCクライアント使用",
        Description = "gRPC通信を使用します（stdin/stdoutの代わり、Phase 2.3+）")]
    public bool UseGrpcClient { get; set; } = false;

    /// <summary>
    /// gRPCサーバーアドレス
    /// Phase 2.3: gRPC通信サーバーのURL
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "gRPCサーバーアドレス",
        Description = "gRPCサーバーのURL（例: http://localhost:50051）")]
    public string GrpcServerAddress { get; set; } = "http://localhost:50051";

    /// <summary>
    /// ONNX Runtime による直接推論を使用する（Python/gRPC サーバー不要）
    /// Issue #445: true の場合、NLLB-200 をC#から直接実行
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "ONNX直接推論",
        Description = "Python/gRPCサーバーなしでNLLB-200翻訳を直接実行します")]
    public bool UseOnnxInference { get; set; }

    /// <summary>
    /// ONNX モデルディレクトリパス（空の場合は既定パスを使用）
    /// Issue #445: Models/nllb-200-onnx/ が既定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "ONNXモデルパス",
        Description = "ONNX翻訳モデルのディレクトリパス（空=既定パス使用）")]
    public string OnnxModelDirectory { get; set; } = string.Empty;

    // 🚨 サーバー監視・自動再起動設定（安定化対応）

    /// <summary>
    /// サーバー自動再起動の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "自動再起動",
        Description = "翻訳サーバーの自動監視・再起動を有効にします")]
    public bool EnableServerAutoRestart { get; set; } = true;

    /// <summary>
    /// 最大連続失敗回数（この回数だけ連続で失敗すると再起動）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "最大連続失敗回数",
        Description = "サーバー再起動を実行する連続失敗回数",
        MinValue = 1,
        MaxValue = 10)]
    public int MaxConsecutiveFailures { get; set; } = 5;

    /// <summary>
    /// 再起動バックオフ時間（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "再起動バックオフ時間",
        Description = "サーバー再起動後の待機時間",
        Unit = "ms",
        MinValue = 1000,
        MaxValue = 60000)]
    public int RestartBackoffMs { get; set; } = 5000;

    /// <summary>
    /// サーバー起動タイムアウト（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Translation", "起動タイムアウト",
        Description = "サーバー起動時のタイムアウト時間",
        Unit = "ms",
        MinValue = 5000,
        MaxValue = 120000)]
    public int ServerStartupTimeoutMs { get; set; } = 120000;

    /// <summary>
    /// 設定のクローンを作成します
    /// </summary>
    /// <returns>クローンされた設定</returns>
    public TranslationSettings Clone()
    {
        return new TranslationSettings
        {
            IsEnabled = IsEnabled,
            DefaultEngine = DefaultEngine,
            AutoDetectSourceLanguage = AutoDetectSourceLanguage,
            DefaultSourceLanguage = DefaultSourceLanguage,
            DefaultTargetLanguage = DefaultTargetLanguage,
            TranslationDelayMs = TranslationDelayMs,
            EnableTranslationCache = EnableTranslationCache,
            Style = Style,
            FallbackEngine = FallbackEngine,
            TimeoutSeconds = TimeoutSeconds,
            MaxCharactersPerRequest = MaxCharactersPerRequest,
            MinCharactersToTranslate = MinCharactersToTranslate,
            PreventDuplicateTranslations = PreventDuplicateTranslations,
            DuplicateSimilarityThreshold = DuplicateSimilarityThreshold,
            EnableParallelTranslation = EnableParallelTranslation,
            MaxParallelTranslations = MaxParallelTranslations,
            // [Issue #78 Phase 5] Cloud AI翻訳設定
            EnableCloudAiTranslation = EnableCloudAiTranslation,
            // [Issue #280+#281] ローカル翻訳エンジン設定
            UseLocalEngine = UseLocalEngine,
            ExModeAutoEnabled = ExModeAutoEnabled,
            CacheRetentionHours = CacheRetentionHours,
            MaxCacheEntries = MaxCacheEntries,
            EncryptApiKeys = EncryptApiKeys,
            GeminiApiKey = GeminiApiKey,
            CustomTranslationPrompt = CustomTranslationPrompt,
            LocalModelPath = LocalModelPath,
            AutoUpdateLocalModel = AutoUpdateLocalModel,
            EnableQualityEstimation = EnableQualityEstimation,
            QualityThreshold = QualityThreshold,
            EnableVerboseLogging = EnableVerboseLogging,
            SaveTranslationResults = SaveTranslationResults,
            RecordApiUsageStatistics = RecordApiUsageStatistics,
            OverlayFontSize = OverlayFontSize,
            EnableTextGrouping = EnableTextGrouping,
            PreserveParagraphs = PreserveParagraphs,
            SameLineThreshold = SameLineThreshold,
            ParagraphSeparationThreshold = ParagraphSeparationThreshold,
            PostTranslationCooldownSeconds = PostTranslationCooldownSeconds,
            UseTrueWindowCapture = UseTrueWindowCapture,
            PreferLegacyCapture = PreferLegacyCapture,
            // Issue #147: 接続プール設定のクローン
            MaxConnections = MaxConnections,
            MinConnections = MinConnections,
            OptimalChunksPerConnection = OptimalChunksPerConnection,
            ConnectionTimeoutMs = ConnectionTimeoutMs,
            HealthCheckIntervalMs = HealthCheckIntervalMs,
            UseExternalServer = UseExternalServer,
            // Phase 2.3: gRPC設定のクローン
            UseGrpcClient = UseGrpcClient,
            GrpcServerAddress = GrpcServerAddress,
            // Issue #445: ONNX直接推論設定のクローン
            UseOnnxInference = UseOnnxInference,
            OnnxModelDirectory = OnnxModelDirectory,
            // サーバー監視・自動再起動設定のクローン
            EnableServerAutoRestart = EnableServerAutoRestart,
            MaxConsecutiveFailures = MaxConsecutiveFailures,
            RestartBackoffMs = RestartBackoffMs,
            ServerStartupTimeoutMs = ServerStartupTimeoutMs
        };
    }

    #region ITranslationSettings 明示的インターフェース実装

    /// <summary>
    /// ITranslationSettings.DefaultEngine - エンジン名を文字列で返す
    /// </summary>
    string ITranslationSettings.DefaultEngine => DefaultEngine.ToString();

    // [Issue #280+#281] UseLocalEngine は明示的パブリックプロパティとして定義済み
    // 明示的インターフェース実装は不要

    /// <summary>
    /// ITranslationSettings.ConfidenceThreshold - 翻訳信頼度閾値（デフォルト: 0.0）
    /// </summary>
    double ITranslationSettings.ConfidenceThreshold => 0.0;

    /// <summary>
    /// ITranslationSettings.TimeoutMs - タイムアウト（ミリ秒）
    /// TimeoutSecondsを変換
    /// </summary>
    int ITranslationSettings.TimeoutMs => TimeoutSeconds * 1000;

    #endregion
}

/// <summary>
/// 翻訳エンジンの種類
/// </summary>
public enum TranslationEngine
{
    /// <summary>
    /// なし（翻訳しない）
    /// </summary>
    None,

    /// <summary>
    /// Google Gemini AI翻訳（クラウド）
    /// </summary>
    Gemini,

    /// <summary>
    /// NLLB-200高品質多言語翻訳（ローカル）
    /// </summary>
    NLLB200,

    /// <summary>
    /// LLamaSharp ローカルLLM翻訳（無料プラン用、Gemma 2B）
    /// </summary>
    LlamaLocal,
}

/// <summary>
/// 翻訳スタイル
/// </summary>
public enum TranslationStyle
{
    /// <summary>
    /// 自然な翻訳（推奨）
    /// </summary>
    Natural,

    /// <summary>
    /// 直訳
    /// </summary>
    Literal,

    /// <summary>
    /// フォーマル（丁寧語）
    /// </summary>
    Formal,

    /// <summary>
    /// カジュアル（口語的）
    /// </summary>
    Casual
}
