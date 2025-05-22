using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// 翻訳エンジンの基本機能を提供する抽象基底クラス
/// </summary>
public abstract class TranslationEngineBase : ITranslationEngine, IDisposable, IAsyncDisposable
{
    private bool _disposed;
    
    /// <summary>
    /// 翻訳エンジンの名称
    /// </summary>
    public abstract string Name { get; }
    
    /// <summary>
    /// 翻訳エンジンの説明
    /// </summary>
    public abstract string Description { get; }
    
    /// <summary>
    /// ネットワーク接続が必要かどうか
    /// </summary>
    public abstract bool RequiresNetwork { get; }

    /// <summary>
    /// 初期化状態
    /// </summary>
    protected bool IsInitialized { get; set; }

    /// <summary>
    /// 単一テキストを翻訳する
    /// </summary>
    /// <param name="request">翻訳リクエスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳結果</returns>
    public abstract Task<TranslationResponse> TranslateAsync(
        TranslationRequest request, 
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// 複数のテキストをバッチ翻訳する
    /// </summary>
    /// <param name="requests">翻訳リクエストリスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>翻訳結果リスト</returns>
    public abstract Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
        IReadOnlyList<TranslationRequest> requests, 
        CancellationToken cancellationToken = default);
        
    /// <summary>
    /// 指定された言語ペアがサポートされているかを確認する
    /// </summary>
    /// <param name="pair">言語ペア</param>
    /// <returns>サポート状況</returns>
    public abstract Task<bool> SupportsLanguagePairAsync(LanguagePair pair);
    
    /// <summary>
    /// 翻訳エンジンが利用可能かを確認する
    /// </summary>
    /// <returns>利用可能な場合はtrue</returns>
    public abstract Task<bool> IsReadyAsync();
    
    /// <summary>
    /// 翻訳エンジンを初期化する
    /// </summary>
    /// <returns>初期化に成功した場合はtrue</returns>
    public abstract Task<bool> InitializeAsync();
    
    /// <summary>
    /// サポートされている言語ペアを取得する
    /// </summary>
    /// <returns>サポートされている言語ペアのコレクション</returns>
    public abstract Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync();
    
    /// <summary>
    /// テキストの言語を検出する
    /// </summary>
    /// <param name="text">検出対象テキスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検出された言語</returns>
    public abstract Task<LanguageDetectionResult> DetectLanguageAsync(
        string text, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// リソースを解放する
    /// </summary>
    public void Dispose()
    {
        // 削除されたオブジェクトの複数回の呼び出しを防止
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    /// リソースを解放する
    /// </summary>
    /// <param name="disposing">マネージドリソースも解放するか</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // マネージドリソースの解放
                DisposeManagedResources();
            }
            
            // アンマネージドリソースの解放
            DisposeUnmanagedResources();
            
            _disposed = true;
        }
    }
    
    /// <summary>
    /// マネージドリソースを解放する
    /// </summary>
    protected virtual void DisposeManagedResources()
    {
        // 継承クラスでオーバーライドして実装
    }
    
    /// <summary>
    /// アンマネージドリソースを解放する
    /// </summary>
    protected virtual void DisposeUnmanagedResources()
    {
        // 継承クラスでオーバーライドして実装
    }
    
    /// <summary>
    /// 非同期にリソースを解放する
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await DisposeAsyncCore().ConfigureAwait(false);
        
        Dispose(false);
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    /// 非同期にリソースを解放する実装
    /// </summary>
    /// <returns>非同期操作のValueTask</returns>
    protected virtual async ValueTask DisposeAsyncCore()
    {
        // 継承クラスでオーバーライドして実装
        await Task.CompletedTask.ConfigureAwait(false);
    }
    
    /// <summary>
    /// エラー応答を作成するヘルパーメソッド
    /// </summary>
    /// <param name="request">元のリクエスト</param>
    /// <param name="errorType">エラータイプ</param>
    /// <param name="message">エラーメッセージ</param>
    /// <returns>エラー情報を含む翻訳応答</returns>
    protected TranslationResponse CreateErrorResponse(
        TranslationRequest request, 
        TranslationErrorType errorType, 
        string message)
    {
        ArgumentNullException.ThrowIfNull(request, nameof(request));
        
        return new TranslationResponse
        {
            RequestId = request.RequestId,
            SourceText = request.SourceText,
            SourceLanguage = request.SourceLanguage,
            TargetLanguage = request.TargetLanguage,
            EngineName = Name,
            IsSuccess = false,
            Error = new TranslationError
            {
                ErrorCode = errorType.ToString(),
                Message = message
            }
        };
    }
}