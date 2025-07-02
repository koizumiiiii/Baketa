using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;

namespace Baketa.Core.UI.Overlay.Positioning;

/// <summary>
/// オーバーレイ位置とサイズの管理システムのインターフェース
/// OCR検出領域ベースの自動配置、翻訳テキスト量対応の自動サイズ調整、
/// ゲームウィンドウ状態変化への対応を実現します。
/// </summary>
public interface IOverlayPositionManager : IAsyncDisposable
{
    /// <summary>
    /// 配置モード（MVP: OCR領域ベースと固定位置のみ）
    /// </summary>
    OverlayPositionMode PositionMode { get; set; }
    
    /// <summary>
    /// サイズモード（MVP: コンテンツベースと固定サイズのみ）
    /// </summary>
    OverlaySizeMode SizeMode { get; set; }
    
    /// <summary>
    /// 固定位置（フォールバック用）
    /// </summary>
    CorePoint FixedPosition { get; set; }
    
    /// <summary>
    /// 固定サイズ（フォールバック用）
    /// </summary>
    CoreSize FixedSize { get; set; }
    
    /// <summary>
    /// 位置オフセット（OCR領域からの相対位置調整）
    /// </summary>
    CoreVector PositionOffset { get; set; }
    
    /// <summary>
    /// 最大サイズ制約
    /// </summary>
    CoreSize MaxSize { get; set; }
    
    /// <summary>
    /// 最小サイズ制約
    /// </summary>
    CoreSize MinSize { get; set; }
    
    /// <summary>
    /// 現在の位置（読み取り専用）
    /// </summary>
    CorePoint CurrentPosition { get; }
    
    /// <summary>
    /// 現在のサイズ（読み取り専用）
    /// </summary>
    CoreSize CurrentSize { get; }
    
    /// <summary>
    /// 位置・サイズが更新された時に発生するイベント
    /// 呼び出し頻度: OCR検出時、翻訳完了時、ゲームウィンドウ変更時
    /// スレッドセーフティ: UIスレッドから呼び出し
    /// </summary>
    event EventHandler<OverlayPositionUpdatedEventArgs> PositionUpdated;
    
    /// <summary>
    /// OCR検出テキスト領域の位置情報を更新します
    /// </summary>
    /// <param name="textRegions">テキスト領域のコレクション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新処理のタスク</returns>
    Task UpdateTextRegionsAsync(IReadOnlyList<TextRegion> textRegions, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 翻訳テキスト情報を更新します（自動サイズ調整のトリガー）
    /// </summary>
    /// <param name="translationInfo">翻訳情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新処理のタスク</returns>
    Task UpdateTranslationInfoAsync(TranslationInfo translationInfo, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// ゲームウィンドウ状態の更新を通知します
    /// </summary>
    /// <param name="gameWindowInfo">ゲームウィンドウ情報</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>更新処理のタスク</returns>
    Task NotifyGameWindowUpdateAsync(GameWindowInfo gameWindowInfo, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// オーバーレイの位置とサイズを計算します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>位置とサイズ情報</returns>
    Task<OverlayPositionInfo> CalculatePositionAndSizeAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// オーバーレイの位置とサイズを適用します
    /// </summary>
    /// <param name="overlayWindow">オーバーレイウィンドウ</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>適用処理のタスク</returns>
    Task ApplyPositionAndSizeAsync(IOverlayWindow overlayWindow, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 画面境界制約を適用します
    /// </summary>
    /// <param name="position">元の位置</param>
    /// <param name="size">サイズ</param>
    /// <param name="monitor">対象モニター情報</param>
    /// <returns>制約適用後の位置</returns>
    CorePoint ApplyBoundaryConstraints(CorePoint position, CoreSize size, MonitorInfo monitor);
    
    /// <summary>
    /// 位置の妥当性を検証します
    /// </summary>
    /// <param name="position">検証する位置</param>
    /// <param name="size">サイズ</param>
    /// <param name="monitor">対象モニター情報</param>
    /// <returns>妥当な位置かどうか</returns>
    bool IsPositionValid(CorePoint position, CoreSize size, MonitorInfo monitor);
}

/// <summary>
/// オーバーレイ配置モード（MVP版）
/// </summary>
public enum OverlayPositionMode
{
    /// <summary>
    /// OCR検出領域に基づく静的配置（主要機能）
    /// </summary>
    OcrRegionBased,
    
    /// <summary>
    /// 固定位置（フォールバック）
    /// </summary>
    Fixed
}

/// <summary>
/// オーバーレイサイズモード（MVP版）
/// </summary>
public enum OverlaySizeMode
{
    /// <summary>
    /// 翻訳テキスト内容に基づく自動サイズ（主要機能）
    /// </summary>
    ContentBased,
    
    /// <summary>
    /// 固定サイズ（フォールバック）
    /// </summary>
    Fixed
}
