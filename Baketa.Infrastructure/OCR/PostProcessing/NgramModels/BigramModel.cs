using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.PostProcessing.NgramModels;

/// <summary>
/// 日本語・英語混在テキスト用のBigramモデル
/// </summary>
public class BigramModel : INgramModel
{
    private readonly ILogger<BigramModel> _logger;
    private readonly Dictionary<string, Dictionary<string, int>> _bigramCounts;
    private readonly Dictionary<string, int> _unigramCounts;
    private readonly Dictionary<string, double> _bigramProbabilities;
    private int _totalBigrams;
    private int _totalUnigrams;
    private readonly double _smoothingFactor;
    
    public BigramModel(ILogger<BigramModel> logger, double smoothingFactor = 0.01)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bigramCounts = new Dictionary<string, Dictionary<string, int>>();
        _unigramCounts = new Dictionary<string, int>();
        _bigramProbabilities = new Dictionary<string, double>();
        _smoothingFactor = smoothingFactor;
    }
    
    public int N => 2;
    
    /// <summary>
    /// 学習データからBigramモデルを構築
    /// </summary>
    public async Task TrainAsync(IEnumerable<string> trainingTexts)
    {
        _logger.LogInformation("Bigramモデルの学習を開始");
        
        await Task.Run(() =>
        {
            _bigramCounts.Clear();
            _unigramCounts.Clear();
            _bigramProbabilities.Clear();
            _totalBigrams = 0;
            _totalUnigrams = 0;
            
            foreach (var text in trainingTexts)
            {
                if (string.IsNullOrWhiteSpace(text))
                    continue;
                
                ProcessText(text);
            }
            
            CalculateProbabilities();
        });
        
        _logger.LogInformation("Bigramモデルの学習完了: {BigramCount}個のBigram, {UnigramCount}個のUnigram", 
            _bigramCounts.Count, _unigramCounts.Count);
    }
    
    /// <summary>
    /// テキストを処理してN-gramカウントを更新
    /// </summary>
    private void ProcessText(string text)
    {
        // 文字単位でのBigram処理
        var cleanText = text.Trim();
        if (cleanText.Length < 2)
            return;
        
        for (int i = 0; i < cleanText.Length - 1; i++)
        {
            var char1 = cleanText[i].ToString();
            var char2 = cleanText[i + 1].ToString();
            
            // Unigramカウント
            if (!_unigramCounts.ContainsKey(char1))
                _unigramCounts[char1] = 0;
            _unigramCounts[char1]++;
            _totalUnigrams++;
            
            // Bigramカウント
            if (!_bigramCounts.ContainsKey(char1))
                _bigramCounts[char1] = new Dictionary<string, int>();
            
            if (!_bigramCounts[char1].ContainsKey(char2))
                _bigramCounts[char1][char2] = 0;
            
            _bigramCounts[char1][char2]++;
            _totalBigrams++;
        }
        
        // 最後の文字のUnigramカウント
        if (cleanText.Length > 0)
        {
            var lastChar = cleanText[cleanText.Length - 1].ToString();
            if (!_unigramCounts.ContainsKey(lastChar))
                _unigramCounts[lastChar] = 0;
            _unigramCounts[lastChar]++;
            _totalUnigrams++;
        }
    }
    
    /// <summary>
    /// 確率を計算
    /// </summary>
    private void CalculateProbabilities()
    {
        foreach (var firstChar in _bigramCounts.Keys)
        {
            var firstCharCount = _unigramCounts.GetValueOrDefault(firstChar, 0);
            
            foreach (var secondChar in _bigramCounts[firstChar].Keys)
            {
                var bigramCount = _bigramCounts[firstChar][secondChar];
                var bigramKey = $"{firstChar}{secondChar}";
                
                // スムージング適用
                var probability = (bigramCount + _smoothingFactor) / (firstCharCount + _smoothingFactor * _unigramCounts.Count);
                _bigramProbabilities[bigramKey] = probability;
            }
        }
    }
    
    /// <summary>
    /// 指定されたコンテキストで最も可能性の高い次の文字を取得
    /// </summary>
    public IEnumerable<(string character, double probability)> GetCandidates(string context)
    {
        if (string.IsNullOrEmpty(context))
            return Enumerable.Empty<(string, double)>();
        
        var lastChar = context[context.Length - 1].ToString();
        
        if (!_bigramCounts.ContainsKey(lastChar))
            return Enumerable.Empty<(string, double)>();
        
        return _bigramCounts[lastChar]
            .Select(kvp => (kvp.Key, _bigramProbabilities.GetValueOrDefault($"{lastChar}{kvp.Key}", 0.0)))
            .OrderByDescending(x => x.Item2)
            .Take(10); // 上位10候補
    }
    
    /// <summary>
    /// 指定されたBigramの確率を取得
    /// </summary>
    public double GetProbability(string ngram)
    {
        if (string.IsNullOrEmpty(ngram) || ngram.Length != 2)
            return 0.0;
        
        return _bigramProbabilities.GetValueOrDefault(ngram, _smoothingFactor / _totalUnigrams);
    }
    
    /// <summary>
    /// 文字列の尤度を計算
    /// </summary>
    public double CalculateLikelihood(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length < 2)
            return 0.0;
        
        double logLikelihood = 0.0;
        
        for (int i = 0; i < text.Length - 1; i++)
        {
            var bigram = text.Substring(i, 2);
            var probability = GetProbability(bigram);
            
            if (probability > 0)
                logLikelihood += Math.Log(probability);
            else
                logLikelihood += Math.Log(_smoothingFactor / _totalUnigrams);
        }
        
        return logLikelihood;
    }
    
    /// <summary>
    /// モデルをファイルに保存
    /// </summary>
    public async Task SaveAsync(string filePath)
    {
        _logger.LogInformation("Bigramモデルを保存: {FilePath}", filePath);
        
        var modelData = new
        {
            BigramCounts = _bigramCounts,
            UnigramCounts = _unigramCounts,
            BigramProbabilities = _bigramProbabilities,
            TotalBigrams = _totalBigrams,
            TotalUnigrams = _totalUnigrams,
            SmoothingFactor = _smoothingFactor
        };
        
        var json = JsonSerializer.Serialize(modelData, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        
        await File.WriteAllTextAsync(filePath, json);
    }
    
    /// <summary>
    /// ファイルからモデルを読み込み
    /// </summary>
    public async Task LoadAsync(string filePath)
    {
        _logger.LogInformation("Bigramモデルを読み込み: {FilePath}", filePath);
        
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("モデルファイルが見つかりません: {FilePath}", filePath);
            return;
        }
        
        var json = await File.ReadAllTextAsync(filePath);
        var modelData = JsonSerializer.Deserialize<JsonElement>(json);
        
        _bigramCounts.Clear();
        _unigramCounts.Clear();
        _bigramProbabilities.Clear();
        
        // BigramCounts を復元
        if (modelData.TryGetProperty("BigramCounts", out var bigramCountsElement))
        {
            foreach (var firstCharProperty in bigramCountsElement.EnumerateObject())
            {
                var firstChar = firstCharProperty.Name;
                _bigramCounts[firstChar] = new Dictionary<string, int>();
                
                foreach (var secondCharProperty in firstCharProperty.Value.EnumerateObject())
                {
                    var secondChar = secondCharProperty.Name;
                    var count = secondCharProperty.Value.GetInt32();
                    _bigramCounts[firstChar][secondChar] = count;
                }
            }
        }
        
        // UnigramCounts を復元
        if (modelData.TryGetProperty("UnigramCounts", out var unigramCountsElement))
        {
            foreach (var charProperty in unigramCountsElement.EnumerateObject())
            {
                var character = charProperty.Name;
                var count = charProperty.Value.GetInt32();
                _unigramCounts[character] = count;
            }
        }
        
        // BigramProbabilities を復元
        if (modelData.TryGetProperty("BigramProbabilities", out var bigramProbabilitiesElement))
        {
            foreach (var bigramProperty in bigramProbabilitiesElement.EnumerateObject())
            {
                var bigram = bigramProperty.Name;
                var probability = bigramProperty.Value.GetDouble();
                _bigramProbabilities[bigram] = probability;
            }
        }
        
        // 統計情報を復元
        if (modelData.TryGetProperty("TotalBigrams", out var totalBigramsElement))
            _totalBigrams = totalBigramsElement.GetInt32();
        
        if (modelData.TryGetProperty("TotalUnigrams", out var totalUnigramsElement))
            _totalUnigrams = totalUnigramsElement.GetInt32();
        
        _logger.LogInformation("Bigramモデル読み込み完了: {BigramCount}個のBigram, {UnigramCount}個のUnigram", 
            _bigramCounts.Count, _unigramCounts.Count);
    }
}