using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using System;

namespace Baketa.UI.ViewModels;

/// <summary>
/// メインビューモデル
/// </summary>
internal sealed class MainViewModel : Framework.ViewModelBase
{
    private readonly ILogger<MainViewModel> _logger;
    
    // LoggerMessageデリゲートの定義
    private static readonly Action<ILogger, Exception?> _logInitialized =
        LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(MainViewModel)),
            "MainViewModelが初期化されました");

    /// <summary>
    /// 表示用のグリーティングメッセージ
    /// </summary>
    public string Greeting { get; } = "Baketa - ゲーム内テキスト翻訳ツール";

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="eventAggregator">イベント集約</param>
    public MainViewModel(
        ILogger<MainViewModel> logger,
        IEventAggregator eventAggregator) : base(eventAggregator)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logInitialized(_logger, null);
    }
}
