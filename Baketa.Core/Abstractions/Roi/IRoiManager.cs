using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Models.Roi;

namespace Baketa.Core.Abstractions.Roi;

/// <summary>
/// ROI（Region of Interest）管理サービスのインターフェース
/// </summary>
/// <remarks>
/// ROI領域の管理、学習データの統合、動的閾値の提供を担当します。
/// </remarks>
public interface IRoiManager
{
    /// <summary>
    /// 現在アクティブなプロファイルを取得
    /// </summary>
    RoiProfile? CurrentProfile { get; }

    /// <summary>
    /// ROI管理が有効かどうか
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 指定したプロファイルをロード
    /// </summary>
    /// <param name="profileId">プロファイルID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ロードされたプロファイル、見つからない場合はnull</returns>
    Task<RoiProfile?> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のプロファイルを保存
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task SaveCurrentProfileAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 実行ファイルパスからプロファイルIDを生成
    /// </summary>
    /// <param name="executablePath">実行ファイルパス</param>
    /// <returns>プロファイルID（SHA256ハッシュ）</returns>
    string ComputeProfileId(string executablePath);

    /// <summary>
    /// 指定した実行ファイルのプロファイルを取得または作成
    /// </summary>
    /// <param name="executablePath">実行ファイルパス</param>
    /// <param name="windowTitle">ウィンドウタイトル</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>プロファイル</returns>
    Task<RoiProfile> GetOrCreateProfileAsync(
        string executablePath,
        string windowTitle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ROI領域を取得
    /// </summary>
    /// <param name="normalizedBounds">正規化矩形</param>
    /// <returns>該当するROI領域、なければnull</returns>
    RoiRegion? GetRegionAt(NormalizedRect normalizedBounds);

    /// <summary>
    /// 現在のプロファイルの全ROI領域を取得
    /// </summary>
    /// <returns>ROI領域のコレクション</returns>
    IReadOnlyList<RoiRegion> GetAllRegions();

    /// <summary>
    /// [Issue #324] 高信頼度のROI領域のみを取得
    /// ROI優先監視モードで使用
    /// </summary>
    /// <param name="minConfidence">最小信頼度スコア（デフォルト: 0.7）</param>
    /// <returns>高信頼度ROI領域のコレクション</returns>
    IReadOnlyList<RoiRegion> GetHighConfidenceRegions(float minConfidence = 0.7f);

    /// <summary>
    /// [Issue #324] 学習が完了しているかどうかを取得
    /// 高信頼度領域が一定数以上存在し、学習セッション数が閾値以上の場合にtrue
    /// </summary>
    bool IsLearningComplete { get; }

    /// <summary>
    /// 指定した座標に適用する閾値を取得
    /// </summary>
    /// <param name="normalizedX">正規化X座標（0.0-1.0）</param>
    /// <param name="normalizedY">正規化Y座標（0.0-1.0）</param>
    /// <param name="defaultThreshold">デフォルト閾値</param>
    /// <returns>適用する閾値</returns>
    float GetThresholdAt(float normalizedX, float normalizedY, float defaultThreshold);

    /// <summary>
    /// [Issue #293] 指定した座標のヒートマップ値を直接取得
    /// </summary>
    /// <param name="normalizedX">正規化X座標（0.0-1.0）</param>
    /// <param name="normalizedY">正規化Y座標（0.0-1.0）</param>
    /// <returns>ヒートマップ値（0.0-1.0）、無効な場合は0.0</returns>
    float GetHeatmapValueAt(float normalizedX, float normalizedY);

    /// <summary>
    /// テキスト検出結果を報告（学習用）
    /// </summary>
    /// <param name="normalizedBounds">検出されたテキストの正規化矩形</param>
    /// <param name="confidence">検出信頼度</param>
    void ReportTextDetection(NormalizedRect normalizedBounds, float confidence);

    /// <summary>
    /// 複数のテキスト検出結果を一括報告（学習用）
    /// </summary>
    /// <param name="detections">検出結果のコレクション</param>
    void ReportTextDetections(IEnumerable<(NormalizedRect bounds, float confidence)> detections);

    /// <summary>
    /// [Issue #293] ウィンドウ情報付きで複数のテキスト検出結果を一括報告（学習用）
    /// </summary>
    /// <param name="detections">検出結果のコレクション</param>
    /// <param name="windowHandle">対象ウィンドウのハンドル</param>
    /// <param name="windowTitle">ウィンドウタイトル</param>
    /// <param name="executablePath">実行ファイルのパス</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>Task</returns>
    Task ReportTextDetectionsAsync(
        IEnumerable<(NormalizedRect bounds, float confidence)> detections,
        IntPtr windowHandle,
        string windowTitle,
        string executablePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 指定した座標が除外ゾーン内かどうかを判定
    /// </summary>
    /// <param name="normalizedX">正規化X座標（0.0-1.0）</param>
    /// <param name="normalizedY">正規化Y座標（0.0-1.0）</param>
    /// <returns>除外ゾーン内ならtrue</returns>
    bool IsInExclusionZone(float normalizedX, float normalizedY);

    /// <summary>
    /// 除外ゾーンを追加
    /// </summary>
    /// <param name="zone">除外ゾーンの正規化矩形</param>
    void AddExclusionZone(NormalizedRect zone);

    /// <summary>
    /// 除外ゾーンを削除
    /// </summary>
    /// <param name="zone">削除する除外ゾーン</param>
    /// <returns>削除成功ならtrue</returns>
    bool RemoveExclusionZone(NormalizedRect zone);

    /// <summary>
    /// 学習データをリセット
    /// </summary>
    /// <param name="preserveExclusionZones">除外ゾーンを保持するかどうか</param>
    void ResetLearningData(bool preserveExclusionZones = true);

    /// <summary>
    /// プロファイルが変更されたときに発生するイベント
    /// </summary>
    event EventHandler<RoiProfileChangedEventArgs>? ProfileChanged;
}

/// <summary>
/// プロファイル変更イベントの引数
/// </summary>
public sealed class RoiProfileChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更前のプロファイル
    /// </summary>
    public RoiProfile? OldProfile { get; init; }

    /// <summary>
    /// 変更後のプロファイル
    /// </summary>
    public RoiProfile? NewProfile { get; init; }

    /// <summary>
    /// 変更の種類
    /// </summary>
    public RoiProfileChangeType ChangeType { get; init; }
}

/// <summary>
/// プロファイル変更の種類
/// </summary>
public enum RoiProfileChangeType
{
    /// <summary>
    /// 新しいプロファイルがロードされた
    /// </summary>
    Loaded,

    /// <summary>
    /// プロファイルが更新された
    /// </summary>
    Updated,

    /// <summary>
    /// プロファイルがアンロードされた
    /// </summary>
    Unloaded,

    /// <summary>
    /// 新しいプロファイルが作成された
    /// </summary>
    Created
}
