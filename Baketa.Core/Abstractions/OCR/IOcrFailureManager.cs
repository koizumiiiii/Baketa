using System;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// OCR失敗状態管理インターフェース
/// Stop→Start後のOCR状態リセット機能を提供し、Clean Architecture原則に準拠したOCR失敗管理を行う
/// </summary>
public interface IOcrFailureManager
{
    /// <summary>
    /// OCR失敗カウンターをリセットします
    /// Stop→Start操作時にOCR状態を初期化し、継続的な失敗状態を解消します
    /// </summary>
    /// <remarks>
    /// このメソッドは同期的に実行され、失敗カウンターを0にリセットします。
    /// リセット後、OCRエンジンは再び利用可能な状態になります。
    /// </remarks>
    void ResetFailureCounter();

    /// <summary>
    /// 現在のOCR失敗回数を取得します
    /// </summary>
    /// <returns>現在の連続失敗回数</returns>
    /// <remarks>
    /// 0以上の整数値を返します。失敗カウンターがMaxFailureThresholdに達した場合、
    /// OCRエンジンは一時的に無効化されます。
    /// </remarks>
    int GetFailureCount();

    /// <summary>
    /// OCRエンジンが現在利用可能かどうかを取得します
    /// </summary>
    /// <value>
    /// OCRエンジンが利用可能な場合はtrue、失敗回数がしきい値に達して無効化されている場合はfalse
    /// </value>
    /// <remarks>
    /// この値は GetFailureCount() &lt; MaxFailureThreshold の結果に基づいて決定されます。
    /// falseの場合、ResetFailureCounter()を呼び出すことで再度利用可能にできます。
    /// </remarks>
    bool IsOcrAvailable { get; }

    /// <summary>
    /// OCRエンジンを無効化する失敗回数のしきい値を取得します
    /// </summary>
    /// <value>OCRエンジンが無効化される失敗回数のしきい値（通常は3）</value>
    /// <remarks>
    /// この値は実装によって異なる場合がありますが、通常は3回の連続失敗で無効化されます。
    /// 設定によって変更可能な場合もあります。
    /// </remarks>
    int MaxFailureThreshold { get; }

    /// <summary>
    /// OCR失敗カウンターが最後にリセットされた日時を取得します（オプション）
    /// </summary>
    /// <value>最後にリセットされた日時、またはまだリセットされていない場合はnull</value>
    /// <remarks>
    /// デバッグ情報として使用されます。実装によってはサポートされない場合があります。
    /// </remarks>
    DateTime? LastResetTime { get; }
}
