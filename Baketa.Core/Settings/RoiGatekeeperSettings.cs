namespace Baketa.Core.Settings;

/// <summary>
/// ROI Gatekeeper設定
/// </summary>
/// <remarks>
/// テキスト変化検知後のCloud AI翻訳Gate機能の設定を管理します。
/// 相対閾値を使用して、短文・長文で異なる判定基準を適用し、
/// 不要なAPI呼び出しを削減してトークンを節約します。
/// </remarks>
public sealed record RoiGatekeeperSettings
{
    /// <summary>
    /// Gatekeeper機能を有効化
    /// </summary>
    /// <remarks>
    /// falseの場合、全てのテキスト変化で翻訳を実行します。
    /// デフォルト: false（既存動作との互換性を維持）
    /// </remarks>
    public bool Enabled { get; init; } = false;

    // ========================================
    // 短文設定
    // ========================================

    /// <summary>
    /// 短文と判定する文字数閾値
    /// </summary>
    /// <remarks>
    /// この文字数以下のテキストを短文として扱います。
    /// デフォルト: 20文字
    /// </remarks>
    public int ShortTextThreshold { get; init; } = 20;

    /// <summary>
    /// 短文に適用する変化率閾値
    /// </summary>
    /// <remarks>
    /// 短文はより高い変化率を要求（ノイズ除去）。
    /// デフォルト: 0.3（30%以上の変化で翻訳）
    /// 推奨範囲: 0.2-0.5
    /// </remarks>
    public float ShortTextChangeThreshold { get; init; } = 0.3f;

    // ========================================
    // 中文設定
    // ========================================

    /// <summary>
    /// 長文と判定する文字数閾値
    /// </summary>
    /// <remarks>
    /// この文字数以上のテキストを長文として扱います。
    /// ShortTextThreshold ～ LongTextThreshold の間は中文。
    /// デフォルト: 100文字
    /// </remarks>
    public int LongTextThreshold { get; init; } = 100;

    /// <summary>
    /// 中文に適用する変化率閾値
    /// </summary>
    /// <remarks>
    /// 標準的な変化率閾値。
    /// デフォルト: 0.15（15%以上の変化で翻訳）
    /// 推奨範囲: 0.1-0.3
    /// </remarks>
    public float MediumTextChangeThreshold { get; init; } = 0.15f;

    // ========================================
    // 長文設定
    // ========================================

    /// <summary>
    /// 長文に適用する変化率閾値
    /// </summary>
    /// <remarks>
    /// 長文はより低い変化率でも翻訳（部分変更を検知）。
    /// デフォルト: 0.08（8%以上の変化で翻訳）
    /// 推奨範囲: 0.05-0.15
    /// </remarks>
    public float LongTextChangeThreshold { get; init; } = 0.08f;

    // ========================================
    // 長さ変化設定
    // ========================================

    /// <summary>
    /// 長さ変化による強制翻訳を有効化
    /// </summary>
    /// <remarks>
    /// テキスト長が大幅に変化した場合、変化率に関係なく翻訳します。
    /// デフォルト: true
    /// </remarks>
    public bool EnableLengthChangeForceTranslate { get; init; } = true;

    /// <summary>
    /// 強制翻訳する長さ変化率閾値
    /// </summary>
    /// <remarks>
    /// テキスト長がこの割合以上変化した場合、強制翻訳。
    /// デフォルト: 0.5（50%以上の長さ変化）
    /// 推奨範囲: 0.3-0.7
    /// </remarks>
    public float LengthChangeForceThreshold { get; init; } = 0.5f;

    // ========================================
    // 除外設定
    // ========================================

    /// <summary>
    /// 除外ゾーンチェックを有効化
    /// </summary>
    /// <remarks>
    /// ROI Manager の除外ゾーン内のテキストは翻訳をスキップ。
    /// デフォルト: true
    /// </remarks>
    public bool EnableExclusionZoneCheck { get; init; } = true;

    /// <summary>
    /// 空テキストを除外
    /// </summary>
    /// <remarks>
    /// 空文字列や空白のみのテキストは翻訳をスキップ。
    /// デフォルト: true
    /// </remarks>
    public bool SkipEmptyText { get; init; } = true;

    /// <summary>
    /// 最小テキスト長
    /// </summary>
    /// <remarks>
    /// この文字数未満のテキストは翻訳をスキップ。
    /// デフォルト: 2文字
    /// </remarks>
    public int MinTextLength { get; init; } = 2;

    // ========================================
    // 統計設定
    // ========================================

    /// <summary>
    /// 統計収集を有効化
    /// </summary>
    /// <remarks>
    /// 判定統計（許可/拒否回数、節約トークン数など）を収集。
    /// デフォルト: true
    /// </remarks>
    public bool EnableStatistics { get; init; } = true;

    /// <summary>
    /// 推定トークン数の文字あたり係数
    /// </summary>
    /// <remarks>
    /// トークン節約量の推定に使用。
    /// 日本語: 約1.5-2.0、英語: 約0.25-0.5
    /// デフォルト: 1.5（日本語ベース）
    /// </remarks>
    public float TokensPerCharacterEstimate { get; init; } = 1.5f;

    // ========================================
    // 高度な設定
    // ========================================

    /// <summary>
    /// 初回テキストは常に翻訳
    /// </summary>
    /// <remarks>
    /// 前回テキストがない場合（初回キャプチャ）は常に翻訳。
    /// デフォルト: true
    /// </remarks>
    public bool AlwaysTranslateFirstText { get; init; } = true;

    /// <summary>
    /// 同一テキストの翻訳をスキップ
    /// </summary>
    /// <remarks>
    /// 完全一致するテキストの再翻訳をスキップ。
    /// デフォルト: true
    /// </remarks>
    public bool SkipIdenticalText { get; init; } = true;

    /// <summary>
    /// ROI信頼度による閾値調整を有効化
    /// </summary>
    /// <remarks>
    /// 高信頼度ROI領域では閾値を緩和します。
    /// デフォルト: false
    /// </remarks>
    public bool EnableConfidenceBasedThresholdAdjustment { get; init; } = false;

    /// <summary>
    /// 高信頼度ROIでの閾値調整係数
    /// </summary>
    /// <remarks>
    /// 高信頼度領域の閾値に乗算。
    /// デフォルト: 0.8（20%緩和）
    /// </remarks>
    public float HighConfidenceThresholdMultiplier { get; init; } = 0.8f;

    // ========================================
    // [Issue #293] ヒートマップベース閾値調整
    // ========================================

    /// <summary>
    /// ヒートマップベースの閾値調整を有効化
    /// </summary>
    /// <remarks>
    /// ROI学習ヒートマップ値に基づいて閾値を動的に調整します。
    /// 高ヒートマップ領域（テキストが頻繁に検出される）は閾値を下げて感度を上げ、
    /// 低ヒートマップ領域は閾値を上げてノイズを除去します。
    /// デフォルト: true
    /// </remarks>
    public bool EnableHeatmapBasedThresholdAdjustment { get; init; } = true;

    /// <summary>
    /// 高ヒートマップ領域での閾値調整係数
    /// </summary>
    /// <remarks>
    /// ヒートマップ値が高い（頻繁にテキストが検出される）領域の閾値に乗算。
    /// 1.0未満で感度が上がり、小さな変化でも翻訳をトリガーします。
    /// デフォルト: 0.8（20%感度アップ）
    /// 推奨範囲: 0.6-0.95
    /// </remarks>
    public float HighHeatmapThresholdMultiplier { get; init; } = 0.8f;

    /// <summary>
    /// 低ヒートマップ領域での閾値調整係数
    /// </summary>
    /// <remarks>
    /// ヒートマップ値が低い（テキストが稀にしか検出されない）領域の閾値に乗算。
    /// 1.0以上で感度が下がり、大きな変化のみで翻訳をトリガーします。
    /// デフォルト: 1.2（20%感度ダウン）
    /// 推奨範囲: 1.05-1.5
    /// </remarks>
    public float LowHeatmapThresholdMultiplier { get; init; } = 1.2f;

    /// <summary>
    /// 高ヒートマップと判定する閾値
    /// </summary>
    /// <remarks>
    /// ヒートマップ値がこの値以上の場合、高ヒートマップ領域として扱います。
    /// デフォルト: 0.7
    /// </remarks>
    public float HighHeatmapThreshold { get; init; } = 0.7f;

    /// <summary>
    /// 低ヒートマップと判定する閾値
    /// </summary>
    /// <remarks>
    /// ヒートマップ値がこの値以下の場合、低ヒートマップ領域として扱います。
    /// デフォルト: 0.3
    /// </remarks>
    public float LowHeatmapThreshold { get; init; } = 0.3f;

    // ========================================
    // [Issue #432] タイプライター演出検知設定
    // ========================================

    /// <summary>
    /// タイプライター演出検知を有効化
    /// </summary>
    /// <remarks>
    /// テキストが前方一致で成長中の場合、翻訳を遅延します。
    /// デフォルト: true
    /// </remarks>
    public bool EnableTypewriterDetection { get; init; } = true;

    /// <summary>
    /// タイプライター安定化に必要な連続同一回数
    /// </summary>
    /// <remarks>
    /// テキストが変化しなくなってからこの回数連続で同一だった場合、
    /// テキストが完成したと判定して翻訳を実行します。
    /// デフォルト: 1
    /// </remarks>
    public int TypewriterStabilizationCycles { get; init; } = 1;

    /// <summary>
    /// タイプライター最大遅延サイクル
    /// </summary>
    /// <remarks>
    /// テキストが成長し続けた場合、このサイクル数を超えたら強制翻訳します。
    /// 長いテキストが際限なく遅延されることを防止します。
    /// デフォルト: 10
    /// </remarks>
    public int TypewriterMaxDelayCycles { get; init; } = 10;

    /// <summary>
    /// 設定値の妥当性を検証
    /// </summary>
    public bool IsValid()
    {
        return ShortTextThreshold > 0
            && LongTextThreshold > ShortTextThreshold
            && ShortTextChangeThreshold is > 0.0f and <= 1.0f
            && MediumTextChangeThreshold is > 0.0f and <= 1.0f
            && LongTextChangeThreshold is > 0.0f and <= 1.0f
            && LengthChangeForceThreshold is > 0.0f and <= 1.0f
            && MinTextLength >= 0
            && TokensPerCharacterEstimate > 0.0f
            && HighConfidenceThresholdMultiplier > 0.0f
            // [Issue #293] ヒートマップベース閾値調整の検証
            && HighHeatmapThresholdMultiplier > 0.0f
            && LowHeatmapThresholdMultiplier > 0.0f
            && HighHeatmapThreshold is >= 0.0f and <= 1.0f
            && LowHeatmapThreshold is >= 0.0f and <= 1.0f
            && LowHeatmapThreshold < HighHeatmapThreshold
            // [Issue #432] タイプライター演出検知の検証
            && TypewriterStabilizationCycles >= 1
            && TypewriterMaxDelayCycles >= 1;
    }

    /// <summary>
    /// デフォルト設定を作成
    /// </summary>
    public static RoiGatekeeperSettings CreateDefault()
    {
        return new RoiGatekeeperSettings();
    }

    /// <summary>
    /// トークン節約重視設定を作成
    /// </summary>
    public static RoiGatekeeperSettings CreateTokenSaving()
    {
        return new RoiGatekeeperSettings
        {
            Enabled = true,
            ShortTextChangeThreshold = 0.4f,
            MediumTextChangeThreshold = 0.2f,
            LongTextChangeThreshold = 0.12f,
            LengthChangeForceThreshold = 0.6f,
            MinTextLength = 3,
            EnableTypewriterDetection = true,
            TypewriterStabilizationCycles = 1,
            TypewriterMaxDelayCycles = 8
        };
    }

    /// <summary>
    /// 高感度設定を作成（変化を見逃さない）
    /// </summary>
    public static RoiGatekeeperSettings CreateHighSensitivity()
    {
        return new RoiGatekeeperSettings
        {
            Enabled = true,
            ShortTextChangeThreshold = 0.2f,
            MediumTextChangeThreshold = 0.1f,
            LongTextChangeThreshold = 0.05f,
            LengthChangeForceThreshold = 0.3f,
            MinTextLength = 1
        };
    }

    /// <summary>
    /// テキスト長に基づいて適用する変化率閾値を取得
    /// </summary>
    /// <param name="textLength">テキスト長</param>
    /// <returns>適用する変化率閾値</returns>
    public float GetThresholdForTextLength(int textLength)
    {
        if (textLength <= ShortTextThreshold)
        {
            return ShortTextChangeThreshold;
        }

        if (textLength >= LongTextThreshold)
        {
            return LongTextChangeThreshold;
        }

        // 中間領域は線形補間
        var ratio = (float)(textLength - ShortTextThreshold) / (LongTextThreshold - ShortTextThreshold);
        return ShortTextChangeThreshold + ratio * (LongTextChangeThreshold - ShortTextChangeThreshold);
    }
}
