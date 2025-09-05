using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events.Capture;

    /// <summary>
    /// テキスト消失イベント
    /// </summary>
    public class TextDisappearanceEvent : IEvent
    {
        // IEvent のプロパティを実装
        /// <summary>
        /// イベントID
        /// </summary>
        public Guid Id { get; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント名
        /// </summary>
        public string Name => "TextDisappearance";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public string Category => "Capture";
        
        /// <summary>
        /// イベント発生時刻
        /// </summary>
        public DateTime Timestamp { get; }
        
        // クラス固有のプロパティ
        /// <summary>
        /// 消失したテキスト領域
        /// </summary>
        public IReadOnlyList<Rectangle> DisappearedRegions { get; }
        
        /// <summary>
        /// ソースウィンドウハンドル
        /// </summary>
        public IntPtr SourceWindowHandle { get; }
        
        /// <summary>
        /// 領域ID（オーバーレイとの関連付け用）
        /// UltraThink Phase 1 拡張: オーバーレイ自動削除システム対応
        /// </summary>
        public string? RegionId { get; }
        
        /// <summary>
        /// 検知時刻（高精度タイムスタンプ）
        /// </summary>
        public DateTime DetectedAt { get; }
        
        /// <summary>
        /// 変化検知の信頼度 (0.0-1.0)
        /// Circuit Breaker パターンでの誤検知防止用
        /// </summary>
        public float ConfidenceScore { get; }
        
        /// <summary>
        /// コンストラクタ（既存互換）
        /// </summary>
        /// <param name="regions">消失したテキスト領域</param>
        /// <param name="sourceWindow">ソースウィンドウハンドル</param>
        public TextDisappearanceEvent(IReadOnlyList<Rectangle> regions, IntPtr sourceWindow = default)
            : this(regions, sourceWindow, regionId: null, confidenceScore: 1.0f)
        {
        }
        
        /// <summary>
        /// 拡張コンストラクタ（UltraThink Phase 1 対応）
        /// </summary>
        /// <param name="regions">消失したテキスト領域</param>
        /// <param name="sourceWindow">ソースウィンドウハンドル</param>
        /// <param name="regionId">領域ID（オーバーレイとの関連付け用）</param>
        /// <param name="confidenceScore">検知信頼度 (0.0-1.0)</param>
        public TextDisappearanceEvent(
            IReadOnlyList<Rectangle> regions, 
            IntPtr sourceWindow = default,
            string? regionId = null,
            float confidenceScore = 1.0f)
        {
            ArgumentNullException.ThrowIfNull(regions, nameof(regions));
            if (confidenceScore < 0.0f || confidenceScore > 1.0f)
                throw new ArgumentOutOfRangeException(nameof(confidenceScore), "信頼度は0.0-1.0の範囲で指定してください");
                
            DisappearedRegions = regions;
            SourceWindowHandle = sourceWindow;
            RegionId = regionId;
            ConfidenceScore = confidenceScore;
            Timestamp = DateTime.UtcNow;
            DetectedAt = DateTime.UtcNow;
        }
    }
