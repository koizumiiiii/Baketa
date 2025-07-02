using System;

namespace Baketa.Core.Abstractions.Capture;

    /// <summary>
    /// 差分検出設定を表すクラス
    /// </summary>
    public class DifferenceDetectionSettings
    {
        /// <summary>
        /// 差分検出の閾値（0.0～1.0）
        /// </summary>
        public double Threshold { get; set; } = 0.05;
        
        /// <summary>
        /// ブロックサイズ（ブロックベース検出用）
        /// </summary>
        public int BlockSize { get; set; } = 16;
        
        /// <summary>
        /// 最小変化領域サイズ（小さな変化を無視）
        /// </summary>
        public int MinimumChangedArea { get; set; } = 100;
        
        /// <summary>
        /// テキスト領域に重点を置く（true）か全体の変化を検出（false）か
        /// </summary>
        public bool FocusOnTextRegions { get; set; } = true;
        
        /// <summary>
        /// エッジ変化の重み（テキスト領域検出で使用）
        /// </summary>
        public double EdgeChangeWeight { get; set; } = 2.0;
        
        /// <summary>
        /// 照明変化を無視するか
        /// </summary>
        public bool IgnoreLightingChanges { get; set; } = true;
        
        /// <summary>
        /// 差分検出アルゴリズム
        /// </summary>
        public DifferenceDetectionAlgorithm Algorithm { get; set; } = DifferenceDetectionAlgorithm.Hybrid;
        
        /// <summary>
        /// マルチスケール数
        /// </summary>
        public int ScaleCount { get; set; } = 2;
        
        /// <summary>
        /// サンプリング密度
        /// </summary>
        public int SamplingDensity { get; set; } = 20;
        
        /// <summary>
        /// 設定のクローンを作成します
        /// </summary>
        /// <returns>クローンされた設定オブジェクト</returns>
        public DifferenceDetectionSettings Clone()
        {
            return new DifferenceDetectionSettings
            {
                Threshold = this.Threshold,
                BlockSize = this.BlockSize,
                MinimumChangedArea = this.MinimumChangedArea,
                FocusOnTextRegions = this.FocusOnTextRegions,
                EdgeChangeWeight = this.EdgeChangeWeight,
                IgnoreLightingChanges = this.IgnoreLightingChanges,
                Algorithm = this.Algorithm,
                ScaleCount = this.ScaleCount,
                SamplingDensity = this.SamplingDensity
            };
        }
    }
    
    /// <summary>
    /// 差分検出アルゴリズム
    /// </summary>
    public enum DifferenceDetectionAlgorithm
    {
        /// <summary>
        /// ピクセルベース（高精度・高負荷）
        /// </summary>
        PixelBased = 0,
        
        /// <summary>
        /// ブロックベース（バランス型）
        /// </summary>
        BlockBased = 1,
        
        /// <summary>
        /// ヒストグラムベース（照明変化に強い）
        /// </summary>
        HistogramBased = 2,
        
        /// <summary>
        /// エッジベース（テキスト検出特化）
        /// </summary>
        EdgeBased = 3,
        
        /// <summary>
        /// ハイブリッド（複合アルゴリズム）
        /// </summary>
        Hybrid = 4,
        
        /// <summary>
        /// サンプリングベース（最速・低精度）
        /// </summary>
        SamplingBased = 5
    }
