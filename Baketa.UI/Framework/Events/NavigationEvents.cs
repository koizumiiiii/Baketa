using System;

namespace Baketa.UI.Framework.Events;

    /// <summary>
    /// キャプチャ設定画面を開くリクエストイベント
    /// </summary>
    internal sealed class OpenCaptureSettingsRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "OpenCaptureSettingsRequested";
        
        /// <inheritdoc/>
        public override string Category => "UI.Navigation";
    }
    
    /// <summary>
    /// 翻訳設定画面を開くリクエストイベント
    /// </summary>
    internal sealed class OpenTranslationSettingsRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "OpenTranslationSettingsRequested";
        
        /// <inheritdoc/>
        public override string Category => "UI.Navigation";
    }
    
    /// <summary>
    /// 履歴画面を開くリクエストイベント
    /// </summary>
    internal sealed class OpenHistoryViewRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "OpenHistoryViewRequested";
        
        /// <inheritdoc/>
        public override string Category => "UI.Navigation";
    }
    
    /// <summary>
    /// アクセシビリティ設定を開くリクエストイベント
    /// </summary>
    internal sealed class OpenAccessibilitySettingsRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "OpenAccessibilitySettingsRequested";
        
        /// <inheritdoc/>
        public override string Category => "UI.Navigation";
    }
    
    /// <summary>
    /// アプリケーション終了リクエストイベント
    /// </summary>
    internal sealed class ApplicationExitRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "ApplicationExitRequested";
        
        /// <inheritdoc/>
        public override string Category => "UI.App";
    }
    
    /// <summary>
    /// キャプチャ開始リクエストイベント
    /// </summary>
    public sealed class StartCaptureRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "StartCaptureRequested";
        
        /// <inheritdoc/>
        public override string Category => "Capture";
    }
    
    /// <summary>
    /// キャプチャ停止リクエストイベント
    /// </summary>
    public sealed class StopCaptureRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "StopCaptureRequested";
        
        /// <inheritdoc/>
        public override string Category => "Capture";
    }
    
    /// <summary>
    /// トレイに最小化リクエストイベント
    /// </summary>
    internal sealed class MinimizeToTrayRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "MinimizeToTrayRequested";
        
        /// <inheritdoc/>
        public override string Category => "UI.App";
    }

    /// <summary>
    /// 設定画面を閉じるリクエストイベント
    /// </summary>
    internal sealed class CloseSettingsRequestedEvent : UIEventBase
    {
        /// <inheritdoc/>
        public override string Name => "CloseSettingsRequested";
        
        /// <inheritdoc/>
        public override string Category => "UI.Navigation";
    }
