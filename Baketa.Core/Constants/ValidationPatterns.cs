namespace Baketa.Core.Constants;

/// <summary>
/// プロジェクト全体で使用する検証用の正規表現パターンを定義
/// </summary>
/// <remarks>
/// Issue #237 Phase 2: コードレビュー指摘対応
/// DRY原則に基づき、検証パターンを一元管理
/// </remarks>
public static class ValidationPatterns
{
    /// <summary>
    /// プロモーションコードの形式 (BAKETA-XXXXXXXX) を検証する正規表現
    /// Base32 Crockford形式: 0-9, A-H, J-K, M-N, P-T, V-Z（O/I/L/U除外）
    /// </summary>
    public const string PromotionCode = @"^BAKETA-[0-9A-HJKMNP-TV-Z]{8}$";
}
