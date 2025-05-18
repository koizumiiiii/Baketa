using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Platform;

    /// <summary>
    /// ファイルシステム操作のためのサービスインターフェース
    /// </summary>
    public interface IFileSystemService
    {
        /// <summary>
        /// アプリケーションデータディレクトリのパスを取得します
        /// </summary>
        /// <returns>アプリケーションデータディレクトリのパス</returns>
        string GetAppDataDirectory();

        /// <summary>
        /// 指定されたパスにディレクトリが存在するかどうかを確認します
        /// </summary>
        /// <param name="path">確認するパス</param>
        /// <returns>ディレクトリが存在する場合はtrue、そうでない場合はfalse</returns>
        bool DirectoryExists(string path);

        /// <summary>
        /// 指定されたパスにディレクトリを作成します
        /// </summary>
        /// <param name="path">作成するディレクトリのパス</param>
        void CreateDirectory(string path);

        /// <summary>
        /// 指定されたパスにファイルが存在するかどうかを確認します
        /// </summary>
        /// <param name="path">確認するパス</param>
        /// <returns>ファイルが存在する場合はtrue、そうでない場合はfalse</returns>
        bool FileExists(string path);

        /// <summary>
        /// 指定されたパスのファイルを削除します
        /// </summary>
        /// <param name="path">削除するファイルのパス</param>
        /// <returns>非同期タスク</returns>
        Task DeleteFileAsync(string path);

        /// <summary>
        /// 指定されたパスにテキストを書き込みます
        /// </summary>
        /// <param name="path">書き込み先のファイルパス</param>
        /// <param name="content">書き込むテキスト</param>
        /// <returns>非同期タスク</returns>
        Task WriteAllTextAsync(string path, string content);

        /// <summary>
        /// 指定されたパスのファイルからテキストを読み込みます
        /// </summary>
        /// <param name="path">読み込むファイルのパス</param>
        /// <returns>ファイルの内容</returns>
        Task<string> ReadAllTextAsync(string path);

        /// <summary>
        /// 指定されたディレクトリ内のファイルを取得します
        /// </summary>
        /// <param name="directory">ディレクトリのパス</param>
        /// <param name="searchPattern">検索パターン</param>
        /// <returns>ファイルパスの配列</returns>
        Task<string[]> GetFilesAsync(string directory, string searchPattern);
    }
