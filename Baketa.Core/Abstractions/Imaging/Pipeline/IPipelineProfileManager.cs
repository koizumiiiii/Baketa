using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// パイプラインプロファイルの管理を担当するインターフェース
/// </summary>
public interface IPipelineProfileManager
{
    /// <summary>
    /// パイプライン設定をプロファイルとして保存します
    /// </summary>
    /// <param name="profileName">プロファイル名</param>
    /// <param name="pipeline">保存するパイプライン</param>
    /// <returns>保存に成功した場合はtrue、それ以外はfalse</returns>
    Task<bool> SaveProfileAsync(string profileName, IImagePipeline pipeline);

    /// <summary>
    /// 指定された名前のプロファイルからパイプラインを読み込みます
    /// </summary>
    /// <param name="profileName">プロファイル名</param>
    /// <returns>読み込まれたパイプライン、またはプロファイルが存在しない場合はnull</returns>
    Task<IImagePipeline?> LoadProfileAsync(string profileName);

    /// <summary>
    /// 利用可能なプロファイル名のリストを取得します
    /// </summary>
    /// <returns>プロファイル名のリスト</returns>
    Task<List<string>> GetAvailableProfilesAsync();

    /// <summary>
    /// 指定されたプロファイルを削除します
    /// </summary>
    /// <param name="profileName">プロファイル名</param>
    /// <returns>削除に成功した場合はtrue、それ以外はfalse</returns>
    Task<bool> DeleteProfileAsync(string profileName);

    /// <summary>
    /// プロファイルキャッシュをクリアします
    /// </summary>
    void ClearCache();
}
