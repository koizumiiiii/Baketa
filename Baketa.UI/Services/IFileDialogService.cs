using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.UI.Services;

/// <summary>
/// ファイルダイアログサービスのインターフェース
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// ファイル保存ダイアログを表示します
    /// </summary>
    /// <param name="title">ダイアログのタイトル</param>
    /// <param name="defaultFileName">デフォルトのファイル名</param>
    /// <param name="fileTypeFilters">ファイルタイプのフィルタ</param>
    /// <returns>選択されたファイルパス。キャンセルされた場合はnull</returns>
    Task<string?> ShowSaveFileDialogAsync(
        string title, 
        string? defaultFileName = null, 
        IReadOnlyList<FileTypeFilter>? fileTypeFilters = null);

    /// <summary>
    /// ファイル選択ダイアログを表示します
    /// </summary>
    /// <param name="title">ダイアログのタイトル</param>
    /// <param name="fileTypeFilters">ファイルタイプのフィルタ</param>
    /// <param name="allowMultiple">複数ファイルの選択を許可するか</param>
    /// <returns>選択されたファイルパス。キャンセルされた場合はnull</returns>
    Task<IReadOnlyList<string>?> ShowOpenFileDialogAsync(
        string title, 
        IReadOnlyList<FileTypeFilter>? fileTypeFilters = null, 
        bool allowMultiple = false);

    /// <summary>
    /// フォルダ選択ダイアログを表示します
    /// </summary>
    /// <param name="title">ダイアログのタイトル</param>
    /// <returns>選択されたフォルダパス。キャンセルされた場合はnull</returns>
    Task<string?> ShowOpenFolderDialogAsync(string title);
}

/// <summary>
/// ファイルタイプフィルタ
/// </summary>
/// <param name="Name">フィルタの表示名</param>
/// <param name="Extensions">ファイル拡張子のリスト（例：["json", "txt"]）</param>
public sealed record FileTypeFilter(string Name, IReadOnlyList<string> Extensions);
