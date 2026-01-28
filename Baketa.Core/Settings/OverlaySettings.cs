namespace Baketa.Core.Settings;

/// <summary>
/// オーバーレイ設定クラス（UX改善対応版）
/// 自動翻訳と単発翻訳の両方のオーバーレイ表示設定を管理
/// </summary>
public sealed class OverlaySettings
{
    /// <summary>
    /// オーバーレイ表示を有効にするか
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Overlay", "オーバーレイ表示",
        Description = "翻訳結果をゲーム画面上にオーバーレイ表示します")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// オーバーレイ表示の有効化（別名）
    /// </summary>
    public bool EnableOverlay
    {
        get => IsEnabled;
        set => IsEnabled = value;
    }

    /// <summary>
    /// オーバーレイの透明度（0.0-1.0）
    /// AR風UIでは自動調整されるため非推奨
    /// </summary>
    [Obsolete("AR風UIでは透明度は自動調整されます。この設定は将来削除される予定です。")]
    [SettingMetadata(SettingLevel.Basic, "Overlay", "透明度",
        Description = "オーバーレイの透明度（0.0=完全透明、1.0=完全不透明）",
        MinValue = 0.1,
        MaxValue = 1.0)]
    public double Opacity { get; set; } = 0.9;

    /// <summary>
    /// オーバーレイのフォントサイズ
    /// AR風UIでは自動計算されるため非推奨
    /// </summary>
    [Obsolete("AR風UIではフォントサイズは自動計算されます。この設定は将来削除される予定です。")]
    [SettingMetadata(SettingLevel.Basic, "Overlay", "フォントサイズ",
        Description = "オーバーレイに表示されるテキストのフォントサイズ",
        Unit = "pt",
        MinValue = 8,
        MaxValue = 48)]
    public int FontSize { get; set; } = 14;

    /// <summary>
    /// オーバーレイの背景色（ARGB形式）
    /// AR風UIでは自動計算されるため非推奨
    /// </summary>
    [Obsolete("AR風UIでは背景色は自動計算されます。この設定は将来削除される予定です。")]
    [SettingMetadata(SettingLevel.Basic, "Overlay", "背景色",
        Description = "オーバーレイの背景色（ARGB形式）")]
    public uint BackgroundColor { get; set; } = 0xFF000000; // 黒

    /// <summary>
    /// オーバーレイのテキスト色（ARGB形式）
    /// AR風UIでは自動計算されるため非推奨
    /// </summary>
    [Obsolete("AR風UIではテキスト色は自動計算されます。この設定は将来削除される予定です。")]
    [SettingMetadata(SettingLevel.Basic, "Overlay", "テキスト色",
        Description = "オーバーレイのテキスト色（ARGB形式）")]
    public uint TextColor { get; set; } = 0xFFFFFFFF; // 白

    /// <summary>
    /// 自動翻訳での自動非表示を有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Overlay", "自動翻訳の自動非表示",
        Description = "自動翻訳の結果を指定時間後に自動的に非表示にします")]
    public bool EnableAutoHideForAutoTranslation { get; set; } = false;

    /// <summary>
    /// 自動翻訳での自動非表示までの時間（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Overlay", "自動翻訳の自動非表示時間",
        Description = "自動翻訳結果が自動的に消えるまでの時間",
        Unit = "秒",
        MinValue = 2,
        MaxValue = 30)]
    public int AutoHideDelayForAutoTranslation { get; set; } = 5;

    /// <summary>
    /// 単発翻訳での自動非表示を有効化（常にtrue）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Overlay", "単発翻訳の自動非表示",
        Description = "単発翻訳の結果は常に自動的に非表示になります")]
    public bool EnableAutoHideForSingleShot { get; set; } = true;

    /// <summary>
    /// 単発翻訳での自動非表示までの時間（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Overlay", "単発翻訳の自動非表示時間",
        Description = "単発翻訳結果が自動的に消えるまでの時間",
        Unit = "秒",
        MinValue = 3,
        MaxValue = 60)]
    public int AutoHideDelayForSingleShot { get; set; } = 10;

    /// <summary>
    /// 表示時間（秒）（別名）
    /// </summary>
    public int DisplayDurationSeconds
    {
        get => AutoHideDelayForSingleShot;
        set => AutoHideDelayForSingleShot = value;
    }

    /// <summary>
    /// オーバーレイの最大幅（ピクセル、0で制限なし）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "最大幅",
        Description = "オーバーレイの最大幅（0で制限なし）",
        Unit = "px",
        MinValue = 0,
        MaxValue = 1920)]
    public int MaxWidth { get; set; } = 400;

    /// <summary>
    /// オーバーレイの最大高さ（ピクセル、0で制限なし）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "最大高さ",
        Description = "オーバーレイの最大高さ（0で制限なし）",
        Unit = "px",
        MinValue = 0,
        MaxValue = 1080)]
    public int MaxHeight { get; set; } = 200;

    /// <summary>
    /// テキストが長い場合の省略表示
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "テキスト省略",
        Description = "長いテキストを省略記号付きで表示します")]
    public bool EnableTextTruncation { get; set; } = true;

    /// <summary>
    /// マウスクリックでオーバーレイを手動で閉じることを許可
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Overlay", "クリックで閉じる",
        Description = "オーバーレイをクリックして手動で非表示にできます")]
    public bool AllowManualClose { get; set; } = true;

    /// <summary>
    /// クリックスルー機能の有効化
    /// </summary>
    /// <remarks>
    /// ⚠️ [WINDOWS_API_CONSTRAINT] クリックスルーとブラー効果は共存不可能
    /// - true: クリックスルー有効、ブラー効果無効（LayeredOverlayWindow使用）
    /// - false: クリックスルー無効、ブラー効果有効（CompositionOverlayWindow使用）
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "クリックスルー",
        Description = "オーバーレイをクリックしても底のアプリにクリックが通るようにします（有効時はブラー効果が無効になります）")]
    public bool EnableClickThrough { get; set; } = true;

    /// <summary>
    /// 翻訳結果のフェードアウトアニメーション時間（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "フェードアウト時間",
        Description = "オーバーレイが消える時のアニメーション時間",
        Unit = "ms",
        MinValue = 0,
        MaxValue = 2000)]
    public int FadeOutDurationMs { get; set; } = 500;

    /// <summary>
    /// オーバーレイの表示位置モード
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "表示位置モード",
        Description = "オーバーレイの表示位置の決定方法",
        ValidValues = [OverlayPositionMode.Fixed, OverlayPositionMode.NearText, OverlayPositionMode.MouseCursor])]
    public OverlayPositionMode PositionMode { get; set; } = OverlayPositionMode.NearText;

    /// <summary>
    /// 固定位置表示時のX座標
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "固定位置X",
        Description = "固定位置モード時のX座標",
        Unit = "px",
        MinValue = 0,
        MaxValue = 3840)]
    public int FixedPositionX { get; set; } = 100;

    /// <summary>
    /// 固定位置表示時のY座標
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "固定位置Y",
        Description = "固定位置モード時のY座標",
        Unit = "px",
        MinValue = 0,
        MaxValue = 2160)]
    public int FixedPositionY { get; set; } = 100;

    /// <summary>
    /// オーバーレイの境界線を表示するか
    /// AR風UIでは境界線は使用されないため非推奨
    /// </summary>
    [Obsolete("AR風UIでは境界線は使用されません。この設定は将来削除される予定です。")]
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "境界線表示",
        Description = "オーバーレイの周囲に境界線を表示します")]
    public bool ShowBorder { get; set; } = true;

    /// <summary>
    /// 境界線の色（ARGB形式）
    /// AR風UIでは境界線は使用されないため非推奨
    /// </summary>
    [Obsolete("AR風UIでは境界線は使用されません。この設定は将来削除される予定です。")]
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "境界線色",
        Description = "オーバーレイの境界線色（ARGB形式）")]
    public uint BorderColor { get; set; } = 0xFF808080; // グレー

    /// <summary>
    /// 境界線の太さ（ピクセル）
    /// AR風UIでは境界線は使用されないため非推奨
    /// </summary>
    [Obsolete("AR風UIでは境界線は使用されません。この設定は将来削除される予定です。")]
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "境界線太さ",
        Description = "オーバーレイの境界線の太さ",
        Unit = "px",
        MinValue = 1,
        MaxValue = 10)]
    public int BorderThickness { get; set; } = 1;

    /// <summary>
    /// オーバーレイの角丸半径（ピクセル）
    /// AR風UIでは角丸は使用されないため非推奨
    /// </summary>
    [Obsolete("AR風UIでは角丸は使用されません。この設定は将来削除される予定です。")]
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "角丸半径",
        Description = "オーバーレイの角の丸み",
        Unit = "px",
        MinValue = 0,
        MaxValue = 20)]
    public int CornerRadius { get; set; } = 5;

    /// <summary>
    /// デバッグ用のオーバーレイ境界表示
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Overlay", "デバッグ境界表示",
        Description = "オーバーレイの境界をデバッグ用に表示します（開発者向け）")]
    public bool ShowDebugBounds { get; set; } = false;

    /// <summary>
    /// 🔥 [DWM_BLUR_IMPLEMENTATION] DWM Composition モードを使用するか
    /// </summary>
    /// <remarks>
    /// Windows Vista以降で利用可能なDesktop Window Manager (DWM) Compositionを使用します。
    /// - true: DWM Compositionモード（ブラー効果が利用可能、Windows Vista+）
    /// - false: Layeredウィンドウモード（従来方式、Windows XP+）
    /// DWMがサポートされていない環境では自動的にLayeredモードにフォールバックします。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "DWM Compositionモード",
        Description = "Windows Vista以降のブラー効果を使用します（非サポート環境では自動的に従来方式にフォールバック）")]
    public bool UseComposition { get; set; } = true;

    /// <summary>
    /// 🔥 [DWM_BLUR_IMPLEMENTATION] ブラー効果を有効にするか
    /// </summary>
    /// <remarks>
    /// UseComposition が true の場合のみ有効です。
    /// Windows Vista/7: 標準的なブラー効果
    /// Windows 10/11: より洗練されたブラー効果
    /// パフォーマンスへの影響はわずかです（GPU側で処理）。
    /// </remarks>
    [SettingMetadata(SettingLevel.Basic, "Overlay", "ブラー効果",
        Description = "オーバーレイの背景にブラー効果を適用します（DWM Compositionモード時のみ）")]
    public bool EnableBlur { get; set; } = true;

    /// <summary>
    /// 🔥 [DWM_BLUR_IMPLEMENTATION] ブラー効果の不透明度 (0-255)
    /// </summary>
    /// <remarks>
    /// ブラー効果の強度を調整します。
    /// - 0: 完全透明（ブラーのみ）
    /// - 128: 半透明（推奨）
    /// - 200: やや不透明（デフォルト）
    /// - 255: 完全不透明
    /// 値が高いほど背景が見えにくくなりますが、テキストの可読性は向上します。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Overlay", "ブラー不透明度",
        Description = "ブラー効果の不透明度（0=完全透明、255=完全不透明）",
        MinValue = 0,
        MaxValue = 255)]
    public byte BlurOpacity { get; set; } = 200;
}

/// <summary>
/// オーバーレイの表示位置モード
/// </summary>
public enum OverlayPositionMode
{
    /// <summary>
    /// 固定位置に表示
    /// </summary>
    Fixed,

    /// <summary>
    /// 認識されたテキストの近くに表示
    /// </summary>
    NearText,

    /// <summary>
    /// マウスカーソルの近くに表示
    /// </summary>
    MouseCursor
}
