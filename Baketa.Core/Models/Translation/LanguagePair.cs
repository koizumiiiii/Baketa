using System;

namespace Baketa.Core.Models.Translation
{
    /// <summary>
    /// 言語ペアを表すクラス
    /// </summary>
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
    }
}
