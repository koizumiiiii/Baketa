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
            IsRightToLeft = isRightToLeft;
        }

        /// <summary>
        /// 英語(English)
        /// </summary>
        public static Language English => new Language { Code = "en", DisplayName = "English" };

        /// <summary>
        /// 日本語(Japanese)
        /// </summary>
        public static Language Japanese => new Language { Code = "ja", DisplayName = "日本語" };

        /// <summary>
        /// 中国語(Chinese, Simplified)
        /// </summary>
        public static Language ChineseSimplified => new Language { Code = "zh-CN", DisplayName = "简体中文" };

        /// <summary>
        /// 中国語(Chinese, Traditional)
        /// </summary>
        public static Language ChineseTraditional => new Language { Code = "zh-TW", DisplayName = "繁體中文" };

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
            if (obj is Language other)
            {
                return string.Equals(Code, other.Code, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        /// <summary>
        /// ハッシュコードを生成
        /// </summary>
        public override int GetHashCode()
        {
            return Code?.GetHashCode(StringComparison.OrdinalIgnoreCase) ?? 0;
        }

        /// <summary>
        /// 文字列表現を返す
        /// </summary>
        public override string ToString()
        {
            return $"{DisplayName} ({Code})";
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
                _ => new Language { Code = code, DisplayName = code } // 未知の言語の場合はコードをそのまま表示名として使用
            };
        }
    }
}
