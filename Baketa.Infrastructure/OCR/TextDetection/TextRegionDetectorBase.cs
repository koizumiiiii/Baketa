using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Microsoft.Extensions.Logging;
using DetectionMethodEnum = Baketa.Core.Abstractions.OCR.TextDetection.TextDetectionMethod;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Infrastructure.OCR.TextDetection;

/// <summary>
/// テキスト領域検出器の基底クラス
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="logger">ロガー</param>
public abstract class TextRegionDetectorBase(ILogger? logger = null) : ITextRegionDetector
{
    private readonly Dictionary<string, object> _parameters = [];

    /// <summary>
    /// ロガーを取得します
    /// </summary>
    protected ILogger? Logger { get; } = logger;

    /// <summary>
    /// 検出器の名前
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// 検出器の説明
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// 検出に使用するアルゴリズム
    /// </summary>
    public abstract DetectionMethodEnum Method { get; }

    /// <summary>
    /// デフォルトパラメータを初期化します
    /// </summary>
    protected virtual void InitializeDefaultParameters()
    {
        // 名前空間が異なるため、明示的に型変換
        var methodValue = (Baketa.Core.Abstractions.OCR.TextDetectionMethod)Method;
        var defaultParams = Baketa.Core.Abstractions.OCR.TextDetectionParams.CreateForMethod(methodValue);

        _parameters["MinWidth"] = defaultParams.MinWidth;
        _parameters["MinHeight"] = defaultParams.MinHeight;
        _parameters["MinAspectRatio"] = defaultParams.MinAspectRatio;
        _parameters["MaxAspectRatio"] = defaultParams.MaxAspectRatio;
        _parameters["MergeThreshold"] = defaultParams.MergeThreshold;

        if (Method == DetectionMethodEnum.Mser)
        {
            _parameters["MserDelta"] = defaultParams.MserDelta;
            _parameters["MserMinArea"] = defaultParams.MserMinArea;
            _parameters["MserMaxArea"] = defaultParams.MserMaxArea;
        }
    }

    /// <summary>
    /// 画像からテキスト領域を検出します
    /// </summary>
    /// <param name="image">検出対象の画像</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>検出されたテキスト領域のリスト</returns>
    public abstract Task<IReadOnlyList<OCRTextRegion>> DetectRegionsAsync(
        IAdvancedImage image,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 検出器のパラメータを設定します
    /// </summary>
    /// <param name="parameterName">パラメータ名</param>
    /// <param name="value">設定値</param>
    public virtual void SetParameter(string parameterName, object value)
    {
        _parameters[parameterName] = value;
    }

    /// <summary>
    /// 検出器のパラメータを取得します
    /// </summary>
    /// <param name="parameterName">パラメータ名</param>
    /// <returns>パラメータ値</returns>
    public virtual object GetParameter(string parameterName)
    {
        if (_parameters.TryGetValue(parameterName, out var value))
        {
            return value;
        }

        throw new KeyNotFoundException($"パラメータ'{parameterName}'が見つかりません");
    }

    /// <summary>
    /// 指定した型でパラメータを取得します
    /// </summary>
    /// <typeparam name="T">取得する型</typeparam>
    /// <param name="parameterName">パラメータ名</param>
    /// <returns>型変換されたパラメータ値</returns>
    public virtual T GetParameter<T>(string parameterName)
    {
        var value = GetParameter(parameterName);

        try
        {
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            throw new InvalidCastException($"パラメータ'{parameterName}'を型'{typeof(T).Name}'に変換できません", ex);
        }
    }

    /// <summary>
    /// すべてのパラメータを取得します
    /// </summary>
    /// <returns>パラメータディクショナリ</returns>
    public virtual IReadOnlyDictionary<string, object> GetParameters()
    {
        return new Dictionary<string, object>(_parameters);
    }

    /// <summary>
    /// 検出器の現在の設定をプロファイルとして保存します
    /// </summary>
    /// <param name="profileName">プロファイル名</param>
    /// <returns>非同期タスク</returns>
    public virtual Task SaveProfileAsync(string profileName)
    {
        // この実装はProfileManagerが必要
        throw new NotImplementedException("プロファイル機能は現在実装されていません");
    }

    /// <summary>
    /// プロファイルから検出器の設定を読み込みます
    /// </summary>
    /// <param name="profileName">プロファイル名</param>
    /// <returns>非同期タスク</returns>
    public virtual Task LoadProfileAsync(string profileName)
    {
        // この実装はProfileManagerが必要
        throw new NotImplementedException("プロファイル機能は現在実装されていません");
    }

    /// <summary>
    /// ロガーでログを記録します
    /// </summary>
    /// <param name="level">ログレベル</param>
    /// <param name="messageTemplate">メッセージテンプレート</param>
    /// <param name="args">引数</param>
    protected void Log(LogLevel level, string messageTemplate, params object[] args)
    {
        // 固定のメッセージテンプレートを使用
        if (Logger == null)
            return;

        // CA2254回避のため、フォーマット済みメッセージを使用
        var formattedMessage = args.Length > 0 ? string.Format(CultureInfo.InvariantCulture, messageTemplate, args) : messageTemplate;

        switch (level)
        {
            case LogLevel.Debug:
                Logger.LogDebug("{Message}", formattedMessage);
                break;
            case LogLevel.Information:
                Logger.LogInformation("{Message}", formattedMessage);
                break;
            case LogLevel.Warning:
                Logger.LogWarning("{Message}", formattedMessage);
                break;
            case LogLevel.Error:
                Logger.LogError("{Message}", formattedMessage);
                break;
            case LogLevel.Critical:
                Logger.LogCritical("{Message}", formattedMessage);
                break;
            default:
                Logger.LogInformation("{Message}", formattedMessage);
                break;
        }
    }

    /// <summary>
    /// フォーマット済みのメッセージでログを記録します
    /// </summary>
    /// <param name="level">ログレベル</param>
    /// <param name="formattedMessage">フォーマット済みメッセージ</param>
    protected void LogFormatted(LogLevel level, string formattedMessage)
    {
        // 事前フォーマット済みメッセージを一貫性のある形式で渡す
        Logger?.Log(level, "{Message}", formattedMessage);
    }
}
