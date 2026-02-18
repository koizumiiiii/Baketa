using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// WebAPI翻訳エンジン固有の機能を定義するインターフェース
/// </summary>
public interface IWebApiTranslationEngine : ITranslationEngine
{
    /// <summary>
    /// APIのベースURL
    /// </summary>
    Uri ApiBaseUrl { get; }

    /// <summary>
    /// APIキーが設定されているかどうか
    /// </summary>
    bool HasApiKey { get; }

    /// <summary>
    /// APIのリクエスト制限（リクエスト/分）
    /// </summary>
    int RateLimit { get; }

    /// <summary>
    /// APIの現在のクォータ残量（リクエスト数）
    /// </summary>
    int? QuotaRemaining { get; }

    /// <summary>
    /// APIのクォータリセット時刻
    /// </summary>
    DateTime? QuotaResetTime { get; }

    /// <summary>
    /// 自動検出言語をサポートしているかどうか
    /// </summary>
    bool SupportsAutoDetection { get; }

    /// <summary>
    /// APIのステータスを確認します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>APIのステータス情報</returns>
    Task<ApiStatusInfo> CheckApiStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// テキストの言語を自動検出します
    /// </summary>
    /// <param name="text">検出対象テキスト</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検出された言語と信頼度</returns>
    new Task<LanguageDetectionResult> DetectLanguageAsync(string text, CancellationToken cancellationToken = default);
}
