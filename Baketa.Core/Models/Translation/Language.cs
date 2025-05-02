using System;

namespace Baketa.Core.Models.Translation
{
    /// <summary>
    /// 言語を表すクラス
    /// 初期サポート言語は英語、日本語、中国語のみです
    /// 他の言語は将来的なアップデートで対応予定です
    /// </summary>
    public class Language
    {
        /// <summary>
        /// 言語コード（ISO 639-1）
        /// 例: "en", "ja", "zh"など
        /// </summary>
        public required string Code { get; set; }

        /// <summary>
        /// 言語名（英語）
        /// 例: "English", "Japanese", "Chinese"など
        /// </summary>
        public required string Name { get; set; }

        /// <summary>
        /// 言語名（現地語）
        /// 例: "English", "日本語", "中文"など
        /// </summary>
        public string? NativeName { get; set; }

        /// <summary>
        /// 言語の地域バリエーション（ISO 3166-1）
        /// 例: "US", "JP", "CN", "TW"など
        /// 完全な言語コードは '{Code}-{RegionCode}' 形式になります
        /// 例: "en-US", "zh-CN", "zh-TW"など
        /// </summary>
        public string? RegionCode { get; set; }

        /// <summary>
        /// 言語が自動検出であるかどうか
        /// </summary>
        public bool IsAutoDetect { get; set; }

        /// <summary>
        /// 自動検出言語用の静的インスタンス
        /// </summary>
        public static Language AutoDetect { get; } = new Language
        {
            Code = "auto",
            Name = "Auto Detect",
            IsAutoDetect = true
        };

        /// <summary>
        /// 英語（アメリカ）の静的インスタンス
        /// </summary>
        public static Language English { get; } = new Language
        {
            Code = "en",
            Name = "English",
            NativeName = "English",
            RegionCode = "US"
        };

        /// <summary>
        /// 日本語の静的インスタンス
        /// </summary>
        public static Language Japanese { get; } = new Language
        {
            Code = "ja",
            Name = "Japanese",
            NativeName = "日本語",
            RegionCode = "JP"
        };

        /// <summary>
        /// 中国語（簡体字）の静的インスタンス
        /// </summary>
        public static Language ChineseSimplified { get; } = new Language
        {
            Code = "zh",
            Name = "Chinese (Simplified)",
            NativeName = "中文（简体）",
            RegionCode = "CN"
        };

        /// <summary>
        /// 中国語（繁体字）の静的インスタンス
        /// </summary>
        public static Language ChineseTraditional { get; } = new Language
        {
            Code = "zh",
            Name = "Chinese (Traditional)",
            NativeName = "中文（繁體）",
            RegionCode = "TW"
        };

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            if (obj is not Language other)
                return false;

            if (string.IsNullOrEmpty(RegionCode) && string.IsNullOrEmpty(other.RegionCode))
                return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);

            return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(RegionCode, other.RegionCode, StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            // StringComparisonに準拠したハッシュコード計算
            if (string.IsNullOrEmpty(RegionCode))
                return StringComparer.OrdinalIgnoreCase.GetHashCode(Code);

            // NullチェックはIsNullOrEmptyで済んでいるのでnull非許容参照型として扱う
            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(Code), 
                StringComparer.OrdinalIgnoreCase.GetHashCode(RegionCode!));
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            if (string.IsNullOrEmpty(RegionCode))
                return $"{Name} ({Code})";

            return $"{Name} ({Code}-{RegionCode})";
        }
    }
}
