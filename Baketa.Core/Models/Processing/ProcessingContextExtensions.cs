using System;
using Baketa.Core.Abstractions.Memory;

namespace Baketa.Core.Models.Processing;

/// <summary>
/// ProcessingContext拡張メソッド
/// Phase 3.11: ReferencedSafeImage管理機能を追加
/// SmartProcessingPipelineServiceでの段階的処理におけるSafeImage早期破棄問題解決
/// </summary>
public static class ProcessingContextExtensions
{
    /// <summary>
    /// ProcessingPipelineInputからReferencedSafeImageを取得
    /// IImageがReferencedSafeImageの場合のみ成功
    /// 注意: 現在の実装では、ProcessingPipelineInputでReferencedSafeImageを直接使用することを前提としています
    /// 将来的にはIImageからReferencedSafeImageへの変換ファクトリが必要になる可能性があります
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <returns>ReferencedSafeImage、取得できない場合はnull</returns>
    public static ReferencedSafeImage? GetReferencedSafeImage(this ProcessingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // 🎯 Phase 3.11: 型安全なキャスト実装
        // ReferencedSafeImageがIImageを実装していないため、
        // object経由で安全にキャストを実行
        if (context.Input.CapturedImage?.GetType() == typeof(ReferencedSafeImage))
        {
            return (ReferencedSafeImage)(object)context.Input.CapturedImage;
        }

        return null;
    }

    /// <summary>
    /// ReferencedSafeImageが利用可能かどうかを判定
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <returns>ReferencedSafeImageが利用可能な場合true</returns>
    public static bool HasReferencedSafeImage(this ProcessingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var referencedSafeImage = context.GetReferencedSafeImage();
        return referencedSafeImage != null && !referencedSafeImage.IsDisposed;
    }

    /// <summary>
    /// 段階開始時の参照カウント増加
    /// ReferencedSafeImageが利用可能な場合のみ実行
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <param name="stageType">実行する段階タイプ</param>
    /// <returns>参照が追加された場合true</returns>
    public static bool AcquireStageReference(this ProcessingContext context, ProcessingStageType stageType)
    {
        ArgumentNullException.ThrowIfNull(context);

        var referencedSafeImage = context.GetReferencedSafeImage();
        if (referencedSafeImage == null || referencedSafeImage.IsDisposed)
        {
            return false;
        }

        try
        {
            referencedSafeImage.AddReference();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄されている場合は失敗
            return false;
        }
    }

    /// <summary>
    /// 段階完了時の参照カウント減少
    /// ReferencedSafeImageが利用可能な場合のみ実行
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <param name="stageType">完了した段階タイプ</param>
    public static void ReleaseStageReference(this ProcessingContext context, ProcessingStageType stageType)
    {
        ArgumentNullException.ThrowIfNull(context);

        var referencedSafeImage = context.GetReferencedSafeImage();
        if (referencedSafeImage == null)
        {
            return;
        }

        try
        {
            referencedSafeImage.ReleaseReference();
        }
        catch (ObjectDisposedException)
        {
            // 既に破棄されている場合は無視
        }
    }

    /// <summary>
    /// 安全なSafeImageアクセス
    /// ReferencedSafeImageから内部のSafeImageを取得
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <returns>SafeImage、取得できない場合はnull</returns>
    public static SafeImage? GetSafeImageSafely(this ProcessingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var referencedSafeImage = context.GetReferencedSafeImage();
        if (referencedSafeImage == null || referencedSafeImage.IsDisposed)
        {
            return null;
        }

        try
        {
            return referencedSafeImage.GetUnderlyingSafeImage();
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>
    /// 参照カウント情報取得（デバッグ用）
    /// </summary>
    /// <param name="context">処理コンテキスト</param>
    /// <returns>参照カウント、取得できない場合は-1</returns>
    public static int GetReferenceCount(this ProcessingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var referencedSafeImage = context.GetReferencedSafeImage();
        return referencedSafeImage?.ReferenceCount ?? -1;
    }
}