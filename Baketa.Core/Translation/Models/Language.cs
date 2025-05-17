using System;
using System.Globalization;

namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳で使用する言語情報を表すクラス
    /// </summary>
    public class Language
    {
        /// <summary>
        /// 言語コード (例: "en", "ja", "zh-CN")
        /// </summary>
        public required string Code { get; set; }

        /// <summary>
        /// 言語の表示名 (例: "English", "日本語", "简体中文")
        /// </summary>
        public required string DisplayName { get; set; }

        /// <summary>
        /// 言語名（英語）
        /// 例: "English", "Japanese", "Chinese"など
        /// 旧名前空間 Baketa.Core.Translation.Models.Language との互換性のため
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// 言語名（現地語）
        /// 例: "English", "日本語", "中文"など
        /// 旧名前空間 Baketa.Core.Translation.Models.Language から移植
        /// </summary>
        public string? NativeName { get; set; }

        /// <summary>
        /// 言語の地域バリエーション（ISO 3166-1）
        /// 例: "US", "JP", "CN", "TW"など
        /// 完全な言語コードは '{Code}-{RegionCode}' 形式になります
        /// 例: "en-US", "zh-CN", "zh-TW"など
        /// 旧名前空間 Baketa.Core.Translation.Models.Language から移植
        /// </summary>
        public string? RegionCode { get; set; }

        /// <summary>
        /// 言語が自動検出であるかどうか
        /// 旧名前空間 Baketa.Core.Translation.Models.Language から移植
        /// </summary>
        public bool IsAutoDetect { get; set; }

        /// <summary>
        /// この言語がRTL(右から左)かどうか
        /// </summary>
        public bool IsRightToLeft { get; set; }

        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public Language()
        {
        }

        /// <summary>
        /// コードと表示名を指定して言語を初期化します
        /// </summary>
        /// <param name="code">言語コード</param>
        /// <param name="displayName">表示名</param>
        /// <param name="isRightToLeft">RTLかどうか</param>
        public Language(string code, string displayName, bool isRightToLeft = false)
        {
            Code = code;
            DisplayName = displayName;
            Name = displayName; // 互換性のため
            IsRightToLeft = isRightToLeft;
        }

        /// <summary>
        /// 自動検出言語用の静的インスタンス
        /// </summary>
        public static Language Auto => new Language
        {
            Code = "auto",
            Name = "Auto Detect",
            DisplayName = "Auto Detect",
            IsAutoDetect = true
        };

        /// <summary>
        /// 自動検出言語用の静的インスタンス（旧名前空間との互換性のため）
        /// </summary>
        public static Language AutoDetect => Auto;

        /// <summary>
        /// 英語(English)
        /// </summary>
        public static Language English => new Language
        {
            Code = "en",
            Name = "English",
            DisplayName = "English",
            NativeName = "English",
            RegionCode = "US"
        };

        /// <summary>
        /// 日本語(Japanese)
        /// </summary>
        public static Language Japanese => new Language
        {
            Code = "ja",
            Name = "Japanese",
            DisplayName = "日本語",
            NativeName = "日本語",
            RegionCode = "JP"
        };

        /// <summary>
        /// 中国語(Chinese, Simplified)
        /// </summary>
        public static Language ChineseSimplified => new Language
        {
            Code = "zh-CN",
            Name = "Chinese (Simplified)",
            DisplayName = "简体中文",
            NativeName = "中文（简体）",
            RegionCode = "CN"
        };

        /// <summary>
        /// 中国語(Chinese, Traditional)
        /// </summary>
        public static Language ChineseTraditional => new Language
        {
            Code = "zh-TW",
            Name = "Chinese (Traditional)",
            DisplayName = "繁體中文",
            NativeName = "中文（繁體）",
            RegionCode = "TW"
        };

        /// <summary>
        /// 韓国語(Korean)
        /// </summary>
        public static Language Korean => new Language { Code = "ko", DisplayName = "한국어" };

        /// <summary>
        /// スペイン語(Spanish)
        /// </summary>
        public static Language Spanish => new Language { Code = "es", DisplayName = "Español" };

        /// <summary>
        /// フランス語(French)
        /// </summary>
        public static Language French => new Language { Code = "fr", DisplayName = "Français" };

        /// <summary>
        /// ドイツ語(German)
        /// </summary>
        public static Language German => new Language { Code = "de", DisplayName = "Deutsch" };

        /// <summary>
        /// ロシア語(Russian)
        /// </summary>
        public static Language Russian => new Language { Code = "ru", DisplayName = "Русский" };

        /// <summary>
        /// アラビア語(Arabic)
        /// </summary>
        public static Language Arabic => new Language { Code = "ar", DisplayName = "العربية", IsRightToLeft = true };

        /// <summary>
        /// 等価比較をオーバーライド
        /// </summary>
        public override bool Equals(object? obj)
        {
            if (obj is not Language other)
                return false;

            if (string.IsNullOrEmpty(RegionCode) && string.IsNullOrEmpty(other.RegionCode))
                return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);

            return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(RegionCode, other.RegionCode, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// ハッシュコードを生成
        /// </summary>
        public override int GetHashCode()
        {
            // StringComparisonに準拠したハッシュコード計算
            if (string.IsNullOrEmpty(RegionCode))
                return StringComparer.OrdinalIgnoreCase.GetHashCode(Code ?? string.Empty);

            return HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(Code ?? string.Empty),
                StringComparer.OrdinalIgnoreCase.GetHashCode(RegionCode));
        }

        /// <summary>
        /// 文字列表現を返す
        /// </summary>
        public override string ToString()
        {
            if (string.IsNullOrEmpty(RegionCode))
                return $"{DisplayName} ({Code})";

            return $"{DisplayName} ({Code}-{RegionCode})";
        }

        /// <summary>
        /// 言語コードから言語オブジェクトを作成
        /// </summary>
        /// <param name="code">言語コード</param>
        /// <returns>言語オブジェクト</returns>
        public static Language FromCode(string code)
        {
            ArgumentNullException.ThrowIfNull(code);
            
            // 言語コードは標準的には小文字で表記されますが、言語コードの比較には大文字変換を使用します
            // CA1308警告への対応: 小文字化では、いくつかの文字はラウンドトリップできなくなる場合があります
            string normalizedCode = code.ToUpperInvariant();
            
            return normalizedCode switch
            {
                "AUTO" => Auto,
                "EN" => English,
                "JA" => Japanese,
                "ZH-CN" => ChineseSimplified,
                "ZH-TW" => ChineseTraditional,
                "KO" => Korean,
                "ES" => Spanish,
                "FR" => French,
                "DE" => German,
                "RU" => Russian,
                "AR" => Arabic,
                _ => new Language { Code = code, Name = code, DisplayName = code } // 未知の言語の場合はコードをそのまま表示名として使用
            };
        }

        /// <summary>
        /// 言語コードと地域コードから言語オブジェクトを作成
        /// </summary>
        /// <param name="code">言語コード</param>
        /// <param name="regionCode">地域コード</param>
        /// <returns>言語オブジェクト</returns>
        public static Language FromCodeAndRegion(string code, string regionCode)
        {
            ArgumentNullException.ThrowIfNull(code);
            ArgumentNullException.ThrowIfNull(regionCode);

            // 言語コードと地域コードを組み合わせた形での標準検索
            string fullCode = $"{code.ToUpperInvariant()}-{regionCode.ToUpperInvariant()}";

            return fullCode switch
            {
                "EN-US" => English,
                "JA-JP" => Japanese,
                "ZH-CN" => ChineseSimplified,
                "ZH-TW" => ChineseTraditional,
                _ => new Language
                {
                    Code = code,
                    Name = $"{code}-{regionCode}",
                    DisplayName = $"{code}-{regionCode}",
                    RegionCode = regionCode
                }
            };
        }

        /// <summary>
        /// この言語のクローンを作成
        /// </summary>
        /// <returns>クローンされた言語オブジェクト</returns>
        public Language Clone()
        {
            return new Language
            {
                Code = Code,
                Name = Name,
                DisplayName = DisplayName,
                NativeName = NativeName,
                RegionCode = RegionCode,
                IsAutoDetect = IsAutoDetect,
                IsRightToLeft = IsRightToLeft
            };
        }
    }
}
