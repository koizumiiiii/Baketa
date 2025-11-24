using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// エラー通知サービスのインターフェース
/// アプリケーション全体のエラーメッセージを画面に表示する責務を担当
/// </summary>
public interface IErrorNotificationService
{
    /// <summary>
    /// エラーメッセージを表示
    /// 画面中央最下部に5秒間表示され、その後自動で消える
    /// </summary>
    /// <param name="message">
    /// エラーメッセージ（推奨フォーマット）:
    /// "[エラー内容]\n原因: [具体的な原因]\n対処: [推奨されるアクション]"
    ///
    /// 例:
    /// - "翻訳エンジンの初期化に失敗しました。\n原因: モデルファイルが見つかりません。\n対処: 設定画面でモデルパスを確認してください。"
    /// - "OCR処理に失敗しました。\n原因: 画像の取得に失敗しました。\n対処: ウィンドウが最小化されていないか確認してください。"
    ///
    /// 推奨事項:
    /// - エラー内容: 何が失敗したかを具体的に記述
    /// - 原因: 可能であれば技術的な原因を記述
    /// - 対処: ユーザーが取るべきアクションを明確に記述
    /// </param>
    Task ShowErrorAsync(string message);

    /// <summary>
    /// エラーメッセージを即座に非表示にする
    /// </summary>
    void HideError();
}
