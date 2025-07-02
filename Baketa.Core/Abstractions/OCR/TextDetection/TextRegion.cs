using System;
using System.Collections.Generic;
using System.Drawing;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.OCR.TextDetection;

    /// <summary>
    /// 検出されたテキスト領域を表すクラス
    /// </summary>
    public class TextRegion
    {
        /// <summary>
        /// 領域のバウンディングボックス
        /// </summary>
        public Rectangle Bounds { get; set; }
        
        /// <summary>
        /// 領域の詳細な輪郭（存在する場合）
        /// </summary>
        private Point[]? _contour;
        
        /// <summary>
        /// 領域の輪郭を取得または設定します
        /// </summary>
        public IReadOnlyList<Point>? Contour 
        { 
            get => _contour; 
            set => _contour = value?.ToArray(); 
        }
        
        /// <summary>
        /// 領域の検出信頼度スコア（0.0～1.0）
        /// </summary>
        public float ConfidenceScore { get; set; }
        
        /// <summary>
        /// 領域のタイプ（タイトル、本文、UI要素など）
        /// </summary>
        public TextRegionType RegionType { get; set; }
        
        /// <summary>
        /// 領域に対する前処理されたイメージ
        /// </summary>
        public IAdvancedImage? ProcessedImage { get; set; }
        
        /// <summary>
        /// 領域の識別ID（フレーム間追跡用）
        /// </summary>
        public Guid RegionId { get; } = Guid.NewGuid();
        
        /// <summary>
        /// 追加のメタデータ
        /// </summary>
        public Dictionary<string, object> Metadata { get; } = [];
        
        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public TextRegion()
        {
            Bounds = Rectangle.Empty;
            ConfidenceScore = 0.0f;
            RegionType = TextRegionType.Unknown;
        }
        
        /// <summary>
        /// バウンディングボックスと信頼度スコアを指定して初期化
        /// </summary>
        /// <param name="bounds">バウンディングボックス</param>
        /// <param name="confidenceScore">信頼度スコア</param>
        public TextRegion(Rectangle bounds, float confidenceScore)
        {
            Bounds = bounds;
            ConfidenceScore = confidenceScore;
            RegionType = TextRegionType.Unknown;
        }
        
        /// <summary>
        /// バウンディングボックス、信頼度スコア、タイプを指定して初期化
        /// </summary>
        /// <param name="bounds">バウンディングボックス</param>
        /// <param name="confidenceScore">信頼度スコア</param>
        /// <param name="regionType">領域タイプ</param>
        public TextRegion(Rectangle bounds, float confidenceScore, TextRegionType regionType)
        {
            Bounds = bounds;
            ConfidenceScore = confidenceScore;
            RegionType = regionType;
        }
        
        /// <summary>
        /// この領域と他の領域の重なり率を計算します
        /// </summary>
        /// <param name="other">比較する他の領域</param>
        /// <returns>重なり率（0.0～1.0）</returns>
        public float CalculateOverlapRatio(TextRegion other)
        {
            ArgumentNullException.ThrowIfNull(other);
                
            var intersection = Rectangle.Intersect(Bounds, other.Bounds);
            if (intersection.IsEmpty)
            {
                return 0.0f;
            }
            
            float intersectionArea = intersection.Width * intersection.Height;
            float thisArea = Bounds.Width * Bounds.Height;
            float otherArea = other.Bounds.Width * other.Bounds.Height;
            float unionArea = thisArea + otherArea - intersectionArea;
            
            return intersectionArea / unionArea;
        }
        
        /// <summary>
        /// この領域が他の領域と重なっているかどうかを判定します
        /// </summary>
        /// <param name="other">比較する他の領域</param>
        /// <param name="overlapThreshold">重なりと判定する閾値（0.0～1.0）</param>
        /// <returns>重なりがある場合はtrue、そうでない場合はfalse</returns>
        public bool Overlaps(TextRegion other, float overlapThreshold = 0.1f)
        {
            return CalculateOverlapRatio(other) >= overlapThreshold;
        }
        
        /// <summary>
        /// 文字列表現を取得します
        /// </summary>
        /// <returns>領域の文字列表現</returns>
        public override string ToString()
        {
            return $"TextRegion [ID={RegionId.ToString()[..8]}..., Bounds={{{Bounds.X}, {Bounds.Y}, {Bounds.Width}, {Bounds.Height}}}, Type={RegionType}, Confidence={ConfidenceScore:F2}]";
        }
    }
