using System;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// パイプライン全体にわたってReferencedSafeImageの最低参照を保持するスコープ管理クラス
/// Phase 3.2B: 並行パイプライン実行時の参照競合問題修正
///
/// 問題解決:
/// - 段階間でのSafeImage早期破棄防止
/// - パイプライン完了まで確実な参照保持
/// - 並行パイプライン実行時の参照競合防止
/// - 例外安全性の確保
///
/// 使用方法:
/// using var pipelineScope = new PipelineScope(referencedSafeImage);
/// // パイプライン処理実行
/// // スコープ終了時に自動的にBaseline参照解放
/// </summary>
public sealed class PipelineScope : IDisposable
{
    private readonly ReferencedSafeImage _referencedSafeImage;
    private readonly object _disposeLock = new();
    private readonly string _pipelineId;
    private bool _disposed;
    private bool _baselineReferenceAcquired;
    private bool _isMainPipeline;

    /// <summary>
    /// PipelineScopeを作成し、Baseline Referenceを確保
    /// Phase 3.2B: 並行パイプライン実行対応
    /// </summary>
    /// <param name="referencedSafeImage">管理対象のReferencedSafeImage</param>
    /// <exception cref="ArgumentNullException">referencedSafeImageがnullの場合</exception>
    /// <exception cref="ObjectDisposedException">referencedSafeImageが既に破棄済みの場合</exception>
    public PipelineScope(ReferencedSafeImage referencedSafeImage)
    {
        _referencedSafeImage = referencedSafeImage ?? throw new ArgumentNullException(nameof(referencedSafeImage));
        _pipelineId = Guid.NewGuid().ToString("N")[..8]; // 短縮形式のユニークID

        lock (_disposeLock)
        {
            // Phase 3.2B: 強化されたBaseline Reference確保
            // 並行実行時の競合を考慮して、より強固な参照管理を実装
            try
            {
                // 追加の安全参照を確保（並行実行対策）
                _referencedSafeImage.AddReference();
                _referencedSafeImage.AddReference(); // Phase 3.2B: 二重参照で安全性向上
                _baselineReferenceAcquired = true;
                _isMainPipeline = true;

                // デバッグ情報：パイプラインID付きロギング
                System.Diagnostics.Debug.WriteLine($"🎯 [PHASE3.2B] PipelineScope作成: ID={_pipelineId}, 参照カウント={_referencedSafeImage.ReferenceCount}");
            }
            catch (ObjectDisposedException)
            {
                throw new ObjectDisposedException(nameof(referencedSafeImage),
                    $"ReferencedSafeImageが既に破棄されているため、PipelineScope({_pipelineId})を作成できません。");
            }
        }
    }

    /// <summary>
    /// 管理中のReferencedSafeImageにアクセス
    /// パイプライン実行中は常に有効な参照を保証
    /// </summary>
    /// <returns>管理中のReferencedSafeImage</returns>
    /// <exception cref="ObjectDisposedException">PipelineScopeが既に破棄済みの場合</exception>
    public ReferencedSafeImage SafeImage
    {
        get
        {
            lock (_disposeLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _referencedSafeImage;
            }
        }
    }

    /// <summary>
    /// PipelineScopeが有効かどうかを判定
    /// </summary>
    public bool IsValid
    {
        get
        {
            lock (_disposeLock)
            {
                return !_disposed && _baselineReferenceAcquired && !_referencedSafeImage.IsDisposed;
            }
        }
    }

    /// <summary>
    /// 一時的な参照を取得（段階処理用）
    /// Baseline Referenceとは独立して管理される
    ///
    /// 注意: 返されるスコープのDisposeを忘れずに呼び出すこと
    /// usingステートメントの使用を推奨
    /// </summary>
    /// <returns>一時参照スコープ</returns>
    /// <exception cref="ObjectDisposedException">PipelineScopeが既に破棄済みの場合</exception>
    public TemporaryReferenceScope AcquireTemporaryReference()
    {
        lock (_disposeLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new TemporaryReferenceScope(_referencedSafeImage);
        }
    }

    /// <summary>
    /// Baseline Referenceを解放してリソースを破棄
    /// Phase 3.2B: 並行実行対応の強化された破棄処理
    /// </summary>
    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return;

            if (_baselineReferenceAcquired)
            {
                try
                {
                    // Phase 3.2B: 二重参照の解放
                    _referencedSafeImage.ReleaseReference(); // 1つ目の参照解放
                    _referencedSafeImage.ReleaseReference(); // 2つ目の参照解放

                    System.Diagnostics.Debug.WriteLine($"🎯 [PHASE3.2B] PipelineScope破棄: ID={_pipelineId}, 残り参照カウント={_referencedSafeImage.ReferenceCount}");
                }
                catch (ObjectDisposedException)
                {
                    // 既に破棄済みの場合は無視
                    System.Diagnostics.Debug.WriteLine($"🚨 [PHASE3.2B] PipelineScope破棄時SafeImage既破棄: ID={_pipelineId}");
                }
                finally
                {
                    _baselineReferenceAcquired = false;
                    _isMainPipeline = false;
                }
            }

            _disposed = true;
        }
    }
}

/// <summary>
/// 一時的な参照を管理するスコープクラス
/// 段階処理での短期間の参照に使用
/// </summary>
public sealed class TemporaryReferenceScope : IDisposable
{
    private readonly ReferencedSafeImage _referencedSafeImage;
    private readonly object _disposeLock = new();
    private bool _disposed;
    private bool _referenceAcquired;

    /// <summary>
    /// 一時参照スコープを作成
    /// </summary>
    /// <param name="referencedSafeImage">参照対象のReferencedSafeImage</param>
    internal TemporaryReferenceScope(ReferencedSafeImage referencedSafeImage)
    {
        _referencedSafeImage = referencedSafeImage ?? throw new ArgumentNullException(nameof(referencedSafeImage));

        lock (_disposeLock)
        {
            try
            {
                _referencedSafeImage.AddReference();
                _referenceAcquired = true;
            }
            catch (ObjectDisposedException)
            {
                // 既に破棄済みの場合は参照取得失敗
                _referenceAcquired = false;
                throw;
            }
        }
    }

    /// <summary>
    /// 参照が正常に取得されたかどうかを判定
    /// </summary>
    public bool IsReferenceValid
    {
        get
        {
            lock (_disposeLock)
            {
                return !_disposed && _referenceAcquired && !_referencedSafeImage.IsDisposed;
            }
        }
    }

    /// <summary>
    /// 管理中のReferencedSafeImageにアクセス
    /// </summary>
    /// <returns>ReferencedSafeImage</returns>
    /// <exception cref="ObjectDisposedException">スコープが既に破棄済みの場合</exception>
    public ReferencedSafeImage SafeImage
    {
        get
        {
            lock (_disposeLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _referencedSafeImage;
            }
        }
    }

    /// <summary>
    /// 一時参照を解放
    /// </summary>
    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_disposed)
                return;

            if (_referenceAcquired)
            {
                try
                {
                    _referencedSafeImage.ReleaseReference();
                }
                catch (ObjectDisposedException)
                {
                    // 既に破棄済みの場合は無視
                }
                finally
                {
                    _referenceAcquired = false;
                }
            }

            _disposed = true;
        }
    }
}