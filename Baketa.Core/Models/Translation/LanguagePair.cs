using System;

namespace Baketa.Core.Models.Translation
{
    /// <summary>
    /// 言語ペアを表すクラス
    /// </summary>
    [Obsolete("代わりに Baketa.Core.Translation.Models.LanguagePair を使用してください。", false)]
    public class LanguagePair : IEquatable<LanguagePair>
    {
        /// <summary>
        /// 元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }

        /// <summary>
        /// 対象言語
        /// </summary>
        public required Language TargetLanguage { get; set; }

        /// <summary>
        /// 言語ペアを作成します
        /// </summary>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <returns>言語ペアインスタンス</returns>
        public static LanguagePair Create(Language sourceLanguage, Language targetLanguage)
        {
            return new LanguagePair
            {
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage
            };
        }

        /// <inheritdoc/>
        public bool Equals(LanguagePair? other)
        {
            if (other is null)
                return false;

            return SourceLanguage.Equals(other.SourceLanguage) &&
                   TargetLanguage.Equals(other.TargetLanguage);
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj) => Equals(obj as LanguagePair);

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(SourceLanguage, TargetLanguage);
        }

        /// <inheritdoc/>
        public override string ToString() =>
            $"{SourceLanguage.Code}{(SourceLanguage.RegionCode != null ? $"-{SourceLanguage.RegionCode}" : "")} -> " +
            $"{TargetLanguage.Code}{(TargetLanguage.RegionCode != null ? $"-{TargetLanguage.RegionCode}" : "")}";
            
        /// <summary>
        /// 暗黙的変換演算子 - 元の言語ペアオブジェクトを新しい名前空間のオブジェクトに変換
        /// </summary>
        /// <param name="languagePair">変換する言語ペアオブジェクト</param>
        public static implicit operator Baketa.Core.Translation.Models.LanguagePair(LanguagePair languagePair)
        {
            if (languagePair == null) return null!;
            
            return new Baketa.Core.Translation.Models.LanguagePair
            {
                SourceLanguage = languagePair.SourceLanguage,
                TargetLanguage = languagePair.TargetLanguage
            };
        }
        
        /// <summary>
        /// 新しい名前空間の言語ペアオブジェクトに変換するメソッド
        /// （暗黙的変換演算子と同等の機能）
        /// </summary>
        /// <returns>新しい名前空間の言語ペアオブジェクト</returns>
        public Baketa.Core.Translation.Models.LanguagePair ToLanguagePair()
        {
            return (Baketa.Core.Translation.Models.LanguagePair)this;
        }
        
        /// <summary>
        /// 明示的変換演算子 - 新しい名前空間の言語ペアオブジェクトを元のオブジェクトに変換
        /// </summary>
        /// <param name="languagePair">変換する言語ペアオブジェクト</param>
        public static explicit operator LanguagePair(Baketa.Core.Translation.Models.LanguagePair languagePair)
        {
            if (languagePair == null) return null!;
            
            return new LanguagePair
            {
                SourceLanguage = (Language)(object)languagePair.SourceLanguage,
                TargetLanguage = (Language)(object)languagePair.TargetLanguage
            };
        }
        
        /// <summary>
        /// 新しい名前空間の言語ペアオブジェクトから変換するメソッド
        /// （明示的変換演算子と同等の機能）
        /// </summary>
        /// <param name="languagePair">新しい名前空間の言語ペアオブジェクト</param>
        /// <returns>元の言語ペアオブジェクト</returns>
        public static LanguagePair FromLanguagePair(Baketa.Core.Translation.Models.LanguagePair languagePair)
        {
            return (LanguagePair)languagePair;
        }
    }
}