using System;
using System.IO;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// SentencePieceテストの基底クラス
/// モデルファイルの存在確認とSkip機能を提供
/// </summary>
public abstract class SentencePieceTestBase
{
    /// <summary>
    /// モデルファイルが存在するかどうか確認し、存在しない場合はテストをスキップ
    /// </summary>
    /// <param name="modelPath">確認するモデルファイルパス</param>
    protected static void SkipIfModelNotExists(string modelPath)
    {
        // モデルファイルが存在しない場合はテストをスキップ
        Assert.True(File.Exists(modelPath), $"SKIPPED: OPUS-MT model file not found at: {modelPath}. Run download_opus_mt_models.ps1 to download models.");
    }

    /// <summary>
    /// プロジェクトルートのモデルファイルパスを取得
    /// </summary>
    /// <param name="modelFileName">モデルファイル名</param>
    /// <returns>モデルファイルの完全パス</returns>
    protected static string GetModelPath(string modelFileName)
    {
        var projectRoot = GetProjectRootDirectory();
        return Path.Combine(projectRoot, "Models", "SentencePiece", modelFileName);
    }

    private static string GetProjectRootDirectory()
    {
        var currentDir = Directory.GetCurrentDirectory();
        while (currentDir != null && !File.Exists(Path.Combine(currentDir, "Baketa.sln")))
        {
            currentDir = Directory.GetParent(currentDir)?.FullName;
        }
        return currentDir ?? throw new DirectoryNotFoundException("Project root not found");
    }
}