using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Infrastructure.OCR.PostProcessing.NgramModels;

/// <summary>
/// N-gramモデルのインターフェース
/// </summary>
public interface INgramModel
{
    /// <summary>
    /// N-gramの次数（2 = bigram, 3 = trigram）
    /// </summary>
    int N { get; }
    
    /// <summary>
    /// 学習データからN-gramモデルを構築
    /// </summary>
    Task TrainAsync(IEnumerable<string> trainingTexts);
    
    /// <summary>
    /// 指定されたコンテキストで最も可能性の高い次の文字を取得
    /// </summary>
    /// <param name="context">コンテキスト文字列</param>
    /// <returns>候補文字とその確率のペア</returns>
    IEnumerable<(string character, double probability)> GetCandidates(string context);
    
    /// <summary>
    /// 指定されたN-gramの確率を取得
    /// </summary>
    /// <param name="ngram">N-gram文字列</param>
    /// <returns>確率値</returns>
    double GetProbability(string ngram);
    
    /// <summary>
    /// 文字列の尤度を計算
    /// </summary>
    /// <param name="text">評価する文字列</param>
    /// <returns>尤度スコア</returns>
    double CalculateLikelihood(string text);
    
    /// <summary>
    /// モデルをファイルに保存
    /// </summary>
    Task SaveAsync(string filePath);
    
    /// <summary>
    /// ファイルからモデルを読み込み
    /// </summary>
    Task LoadAsync(string filePath);
}