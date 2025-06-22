namespace Baketa.Core.Settings;

/// <summary>
/// キャプチャ設定クラス
/// 画面キャプチャとスクリーンショット機能の設定を管理
/// </summary>
public sealed class CaptureSettings
{
    /// <summary>
    /// キャプチャ機能の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Capture", "キャプチャ有効", 
        Description = "画面キャプチャ機能を有効にします")]
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// キャプチャ機能の有効化（別名）
    /// </summary>
    public bool EnableCapture
    {
        get => IsEnabled;
        set => IsEnabled = value;
    }
    
    /// <summary>
    /// キャプチャ間隔（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Capture", "キャプチャ間隔", 
        Description = "画面をキャプチャする間隔", 
        Unit = "ms", 
        MinValue = 100, 
        MaxValue = 5000)]
    public int CaptureIntervalMs { get; set; } = 500;
    
    /// <summary>
    /// キャプチャ品質（1-100）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Capture", "キャプチャ品質", 
        Description = "キャプチャ画像の品質（高いほど精度向上、低いほど高速）", 
        MinValue = 1, 
        MaxValue = 100)]
    public int CaptureQuality { get; set; } = 85;
    
    /// <summary>
    /// キャプチャ領域の自動検出
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Capture", "自動領域検出", 
        Description = "テキスト領域を自動的に検出してキャプチャします")]
    public bool AutoDetectCaptureArea { get; set; } = true;
    
    /// <summary>
    /// 固定キャプチャ領域のX座標
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "固定領域X", 
        Description = "固定キャプチャ領域の左上X座標", 
        Unit = "px", 
        MinValue = 0, 
        MaxValue = 3840)]
    public int FixedCaptureAreaX { get; set; } = 0;
    
    /// <summary>
    /// 固定キャプチャ領域のY座標
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "固定領域Y", 
        Description = "固定キャプチャ領域の左上Y座標", 
        Unit = "px", 
        MinValue = 0, 
        MaxValue = 2160)]
    public int FixedCaptureAreaY { get; set; } = 0;
    
    /// <summary>
    /// 固定キャプチャ領域の幅
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "固定領域幅", 
        Description = "固定キャプチャ領域の幅", 
        Unit = "px", 
        MinValue = 50, 
        MaxValue = 3840)]
    public int FixedCaptureAreaWidth { get; set; } = 800;
    
    /// <summary>
    /// 固定キャプチャ領域の高さ
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "固定領域高さ", 
        Description = "固定キャプチャ領域の高さ", 
        Unit = "px", 
        MinValue = 50, 
        MaxValue = 2160)]
    public int FixedCaptureAreaHeight { get; set; } = 600;
    
    /// <summary>
    /// モニター選択（マルチモニター環境）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "対象モニター", 
        Description = "キャプチャ対象のモニター（0=プライマリ、-1=自動選択）", 
        MinValue = -1, 
        MaxValue = 8)]
    public int TargetMonitor { get; set; } = -1;
    
    /// <summary>
    /// DPIスケーリング考慮
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "DPIスケーリング考慮", 
        Description = "高DPI環境でのスケーリングを考慮したキャプチャ")]
    public bool ConsiderDpiScaling { get; set; } = true;
    
    /// <summary>
    /// ハードウェアアクセラレーション使用
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "ハードウェア加速", 
        Description = "GPUを使用したハードウェアアクセラレーション")]
    public bool UseHardwareAcceleration { get; set; } = true;
    
    /// <summary>
    /// キャプチャ差分検出の感度
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "差分検出感度", 
        Description = "画面変更を検出する感度（高いほど小さな変更も検出）", 
        MinValue = 1, 
        MaxValue = 100)]
    public int DifferenceDetectionSensitivity { get; set; } = 30;
    
    /// <summary>
    /// 差分検出機能の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "差分検出有効", 
        Description = "画面変更の差分検出機能を有効にします")]
    public bool EnableDifferenceDetection { get; set; } = true;
    
    /// <summary>
    /// 差分検出闾値（0.0～1.0）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "差分検出闾値", 
        Description = "差分検出の闾値（小さいほど敏感）", 
        MinValue = 0.0, 
        MaxValue = 1.0)]
    public double DifferenceThreshold { get; set; } = 0.1;
    
    /// <summary>
    /// 差分検出領域の分割数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "差分検出分割数", 
        Description = "差分検出のために画面を分割する数", 
        MinValue = 1, 
        MaxValue = 100)]
    public int DifferenceDetectionGridSize { get; set; } = 16;
    
    /// <summary>
    /// キャプチャ履歴の保存
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "履歴保存", 
        Description = "キャプチャした画像の履歴を保存します")]
    public bool SaveCaptureHistory { get; set; } = false;
    
    /// <summary>
    /// キャプチャ履歴の最大保存数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "履歴最大保存数", 
        Description = "保存するキャプチャ履歴の最大数", 
        MinValue = 10, 
        MaxValue = 1000)]
    public int MaxCaptureHistoryCount { get; set; } = 100;
    
    /// <summary>
    /// フルスクリーンゲーム対応モード
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "フルスクリーン対応", 
        Description = "フルスクリーンゲームからのキャプチャを最適化します")]
    public bool FullscreenOptimization { get; set; } = true;
    
    /// <summary>
    /// ゲーム検出時の自動最適化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Capture", "ゲーム自動最適化", 
        Description = "ゲームを検出した時にキャプチャ設定を自動最適化します")]
    public bool AutoOptimizeForGames { get; set; } = true;
    
    /// <summary>
    /// デバッグ用キャプチャ保存
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Capture", "デバッグ保存", 
        Description = "デバッグ用にキャプチャ画像を保存します（開発者向け）")]
    public bool SaveDebugCaptures { get; set; } = false;
    
    /// <summary>
    /// デバッグ用キャプチャ保存パス
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Capture", "デバッグ保存パス", 
        Description = "デバッグ用キャプチャの保存先パス（開発者向け）")]
    public string DebugCaptureSavePath { get; set; } = string.Empty;
    
    /// <summary>
    /// 設定のクローンを作成します
    /// </summary>
    /// <returns>クローンされた設定</returns>
    public CaptureSettings Clone()
    {
        return new CaptureSettings
        {
            IsEnabled = IsEnabled,
            CaptureIntervalMs = CaptureIntervalMs,
            CaptureQuality = CaptureQuality,
            AutoDetectCaptureArea = AutoDetectCaptureArea,
            FixedCaptureAreaX = FixedCaptureAreaX,
            FixedCaptureAreaY = FixedCaptureAreaY,
            FixedCaptureAreaWidth = FixedCaptureAreaWidth,
            FixedCaptureAreaHeight = FixedCaptureAreaHeight,
            TargetMonitor = TargetMonitor,
            ConsiderDpiScaling = ConsiderDpiScaling,
            UseHardwareAcceleration = UseHardwareAcceleration,
            DifferenceDetectionSensitivity = DifferenceDetectionSensitivity,
            EnableDifferenceDetection = EnableDifferenceDetection,
            DifferenceThreshold = DifferenceThreshold,
            DifferenceDetectionGridSize = DifferenceDetectionGridSize,
            SaveCaptureHistory = SaveCaptureHistory,
            MaxCaptureHistoryCount = MaxCaptureHistoryCount,
            FullscreenOptimization = FullscreenOptimization,
            AutoOptimizeForGames = AutoOptimizeForGames,
            SaveDebugCaptures = SaveDebugCaptures,
            DebugCaptureSavePath = DebugCaptureSavePath
        };
    }
}
