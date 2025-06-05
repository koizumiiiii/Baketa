using System;

namespace Baketa.Core.Events.CaptureEvents;

    /// <summary>
    /// キャプチャ状態変更イベント
    /// </summary>
    public class CaptureStatusChangedEvent : EventBase
    {
        /// <summary>
        /// キャプチャがアクティブかどうか
        /// </summary>
        public bool IsActive { get; set; }
        
        /// <summary>
        /// キャプチャ領域幅
        /// </summary>
        public int RegionWidth { get; set; }
        
        /// <summary>
        /// キャプチャ領域高さ
        /// </summary>
        public int RegionHeight { get; set; }
        
        /// <summary>
        /// キャプチャ領域X座標
        /// </summary>
        public int RegionX { get; set; }
        
        /// <summary>
        /// キャプチャ領域Y座標
        /// </summary>
        public int RegionY { get; set; }
        
        /// <summary>
        /// キャプチャモード
        /// </summary>
        public string CaptureMode { get; set; } = string.Empty;
        
        /// <summary>
        /// キャプチャインターバル（ミリ秒）
        /// </summary>
        public int IntervalMs { get; set; }
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "CaptureStatusChanged";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Capture";
    }
