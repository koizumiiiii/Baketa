using System;

namespace Baketa.Core.Models.Roi;

/// <summary>
/// ROIヒートマップデータを表すモデル
/// </summary>
/// <remarks>
/// 画面全体のテキスト出現頻度を2次元グリッドとして保持します。
/// 学習によって各セルの値が更新され、テキスト出現傾向を可視化します。
/// </remarks>
public sealed record RoiHeatmapData
{
    /// <summary>
    /// 浮動小数点比較用のイプシロン値
    /// </summary>
    public const float EpsilonForComparison = 1e-6f;

    /// <summary>
    /// グリッドの行数（垂直方向の分割数）
    /// </summary>
    public int Rows { get; init; }

    /// <summary>
    /// グリッドの列数（水平方向の分割数）
    /// </summary>
    public int Columns { get; init; }

    /// <summary>
    /// ヒートマップの値（0.0-1.0）
    /// </summary>
    /// <remarks>
    /// [row * Columns + column] でアクセス。
    /// 1.0に近いほどテキストが頻繁に出現する。
    /// </remarks>
    public float[] Values { get; init; } = [];

    /// <summary>
    /// 各セルの総サンプル数
    /// </summary>
    /// <remarks>
    /// 学習の信頼度計算に使用。
    /// </remarks>
    public int[] SampleCounts { get; init; } = [];

    /// <summary>
    /// [Issue #354] 各セルの連続miss数
    /// </summary>
    /// <remarks>
    /// テキスト検出されなかった連続回数。検出時にリセット。
    /// 下位互換性のため、nullの場合はゼロ配列として扱う。
    /// </remarks>
    public int[]? MissCounts { get; init; }

    /// <summary>
    /// 最後に更新された時刻
    /// </summary>
    public DateTime LastUpdatedAt { get; init; }

    /// <summary>
    /// 総学習サンプル数
    /// </summary>
    public long TotalSamples { get; init; }

    /// <summary>
    /// 設定値の妥当性を検証
    /// </summary>
    public bool IsValid()
    {
        if (Rows <= 0 || Columns <= 0)
        {
            return false;
        }

        var expectedSize = Rows * Columns;
        if (Values.Length != expectedSize || SampleCounts.Length != expectedSize)
        {
            return false;
        }

        // [Issue #354] MissCountsが存在する場合はサイズをチェック
        if (MissCounts != null && MissCounts.Length != expectedSize)
        {
            return false;
        }

        // 全ての値が有効範囲内であることを確認
        foreach (var value in Values)
        {
            if (value is < 0.0f or > 1.0f + EpsilonForComparison)
            {
                return false;
            }
        }

        foreach (var count in SampleCounts)
        {
            if (count < 0)
            {
                return false;
            }
        }

        // [Issue #354] MissCountsの値を検証
        if (MissCounts != null)
        {
            foreach (var count in MissCounts)
            {
                if (count < 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// 指定したセルの値を取得
    /// </summary>
    /// <param name="row">行インデックス</param>
    /// <param name="column">列インデックス</param>
    /// <returns>ヒートマップ値（0.0-1.0）</returns>
    public float GetValue(int row, int column)
    {
        if (row < 0 || row >= Rows || column < 0 || column >= Columns)
        {
            return 0.0f;
        }

        return Values[row * Columns + column];
    }

    /// <summary>
    /// 指定したセルのサンプル数を取得
    /// </summary>
    /// <param name="row">行インデックス</param>
    /// <param name="column">列インデックス</param>
    /// <returns>サンプル数</returns>
    public int GetSampleCount(int row, int column)
    {
        if (row < 0 || row >= Rows || column < 0 || column >= Columns)
        {
            return 0;
        }

        return SampleCounts[row * Columns + column];
    }

    /// <summary>
    /// [Issue #354] 指定したセルの連続miss数を取得
    /// </summary>
    /// <param name="row">行インデックス</param>
    /// <param name="column">列インデックス</param>
    /// <returns>連続miss数</returns>
    public int GetMissCount(int row, int column)
    {
        if (row < 0 || row >= Rows || column < 0 || column >= Columns)
        {
            return 0;
        }

        // 下位互換性: MissCountsがnullの場合は0を返す
        if (MissCounts == null)
        {
            return 0;
        }

        return MissCounts[row * Columns + column];
    }

    /// <summary>
    /// 正規化座標からセルインデックスを取得
    /// </summary>
    /// <param name="normalizedX">正規化X座標（0.0-1.0）</param>
    /// <param name="normalizedY">正規化Y座標（0.0-1.0）</param>
    /// <returns>(row, column)のタプル</returns>
    public (int row, int column) GetCellIndex(float normalizedX, float normalizedY)
    {
        var column = Math.Clamp((int)(normalizedX * Columns), 0, Columns - 1);
        var row = Math.Clamp((int)(normalizedY * Rows), 0, Rows - 1);
        return (row, column);
    }

    /// <summary>
    /// 正規化座標でのヒートマップ値を取得
    /// </summary>
    /// <param name="normalizedX">正規化X座標（0.0-1.0）</param>
    /// <param name="normalizedY">正規化Y座標（0.0-1.0）</param>
    /// <returns>ヒートマップ値（0.0-1.0）</returns>
    public float GetValueAt(float normalizedX, float normalizedY)
    {
        var (row, column) = GetCellIndex(normalizedX, normalizedY);
        return GetValue(row, column);
    }

    /// <summary>
    /// 指定した矩形領域の平均ヒートマップ値を計算
    /// </summary>
    /// <param name="bounds">正規化矩形</param>
    /// <returns>平均ヒートマップ値（0.0-1.0）</returns>
    public float GetAverageValueForRegion(NormalizedRect bounds)
    {
        var (startRow, startColumn) = GetCellIndex(bounds.X, bounds.Y);
        var (endRow, endColumn) = GetCellIndex(bounds.Right, bounds.Bottom);

        var sum = 0.0f;
        var count = 0;

        for (var row = startRow; row <= endRow; row++)
        {
            for (var column = startColumn; column <= endColumn; column++)
            {
                sum += GetValue(row, column);
                count++;
            }
        }

        return count > 0 ? sum / count : 0.0f;
    }

    /// <summary>
    /// 指定した閾値以上のセルを含む領域を検出
    /// </summary>
    /// <param name="threshold">閾値（0.0-1.0）</param>
    /// <returns>高ヒートマップ値のセル情報</returns>
    public (int row, int column, float value)[] GetHighValueCells(float threshold = 0.5f)
    {
        var results = new System.Collections.Generic.List<(int, int, float)>();

        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var value = GetValue(row, column);
                if (value >= threshold)
                {
                    results.Add((row, column, value));
                }
            }
        }

        return [.. results];
    }

    /// <summary>
    /// セルの値を更新した新しいヒートマップを作成
    /// </summary>
    /// <param name="row">行インデックス</param>
    /// <param name="column">列インデックス</param>
    /// <param name="detected">テキストが検出されたかどうか</param>
    /// <param name="learningRate">学習率</param>
    /// <returns>更新されたヒートマップ</returns>
    public RoiHeatmapData WithUpdatedCell(int row, int column, bool detected, float learningRate = 0.1f)
    {
        if (row < 0 || row >= Rows || column < 0 || column >= Columns)
        {
            return this;
        }

        var newValues = (float[])Values.Clone();
        var newSampleCounts = (int[])SampleCounts.Clone();
        var index = row * Columns + column;

        // 指数移動平均による更新
        var currentValue = newValues[index];
        var newValue = detected ? 1.0f : 0.0f;
        newValues[index] = currentValue + learningRate * (newValue - currentValue);
        newSampleCounts[index]++;

        return this with
        {
            Values = newValues,
            SampleCounts = newSampleCounts,
            LastUpdatedAt = DateTime.UtcNow,
            TotalSamples = TotalSamples + 1
        };
    }

    /// <summary>
    /// 複数セルの値を一括更新した新しいヒートマップを作成
    /// </summary>
    /// <param name="detectedCells">検出されたセルのリスト</param>
    /// <param name="learningRate">学習率</param>
    /// <returns>更新されたヒートマップ</returns>
    public RoiHeatmapData WithUpdatedCells((int row, int column)[] detectedCells, float learningRate = 0.1f)
    {
        var newValues = (float[])Values.Clone();
        var newSampleCounts = (int[])SampleCounts.Clone();
        var detectedSet = new System.Collections.Generic.HashSet<int>();

        foreach (var (row, column) in detectedCells)
        {
            if (row >= 0 && row < Rows && column >= 0 && column < Columns)
            {
                detectedSet.Add(row * Columns + column);
            }
        }

        // 全セルを更新
        for (var index = 0; index < newValues.Length; index++)
        {
            var detected = detectedSet.Contains(index);
            var currentValue = newValues[index];
            var newValue = detected ? 1.0f : 0.0f;
            newValues[index] = currentValue + learningRate * (newValue - currentValue);
            newSampleCounts[index]++;
        }

        return this with
        {
            Values = newValues,
            SampleCounts = newSampleCounts,
            LastUpdatedAt = DateTime.UtcNow,
            TotalSamples = TotalSamples + 1
        };
    }

    /// <summary>
    /// [Issue #354] 重み付きで複数セルの値を一括更新した新しいヒートマップを作成
    /// </summary>
    /// <param name="detectedCells">検出されたセルと重みのリスト</param>
    /// <param name="learningRate">基本学習率</param>
    /// <returns>更新されたヒートマップ</returns>
    public RoiHeatmapData WithUpdatedCellsWeighted((int row, int column, int weight)[] detectedCells, float learningRate = 0.1f)
    {
        var newValues = (float[])Values.Clone();
        var newSampleCounts = (int[])SampleCounts.Clone();

        // セルごとの最大重みを集約（同じセルに複数検出がある場合）
        var cellWeights = new System.Collections.Generic.Dictionary<int, int>();
        foreach (var (row, column, weight) in detectedCells)
        {
            if (row >= 0 && row < Rows && column >= 0 && column < Columns)
            {
                var index = row * Columns + column;
                if (!cellWeights.TryGetValue(index, out var existingWeight) || weight > existingWeight)
                {
                    cellWeights[index] = weight;
                }
            }
        }

        // 全セルを更新
        for (var index = 0; index < newValues.Length; index++)
        {
            var currentValue = newValues[index];
            if (cellWeights.TryGetValue(index, out var weight))
            {
                // 検出されたセル: 重み付き学習率で更新
                var effectiveLearningRate = learningRate * weight;
                newValues[index] = currentValue + effectiveLearningRate * (1.0f - currentValue);
            }
            else
            {
                // 検出されなかったセル: 通常の減衰
                newValues[index] = currentValue + learningRate * (0.0f - currentValue);
            }
            newSampleCounts[index]++;
        }

        return this with
        {
            Values = newValues,
            SampleCounts = newSampleCounts,
            LastUpdatedAt = DateTime.UtcNow,
            TotalSamples = TotalSamples + 1
        };
    }

    /// <summary>
    /// 減衰を適用した新しいヒートマップを作成
    /// </summary>
    /// <param name="decayRate">減衰率（0.0-1.0）</param>
    /// <returns>減衰後のヒートマップ</returns>
    public RoiHeatmapData WithDecay(float decayRate = 0.01f)
    {
        var newValues = new float[Values.Length];

        for (var i = 0; i < Values.Length; i++)
        {
            newValues[i] = Values[i] * (1.0f - decayRate);
        }

        return this with
        {
            Values = newValues,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// [Issue #354] miss記録を適用した新しいヒートマップを作成
    /// </summary>
    /// <param name="missCells">missしたセルのリスト</param>
    /// <param name="resetThreshold">この回数連続missでスコアをリセット</param>
    /// <returns>更新されたヒートマップ</returns>
    public RoiHeatmapData WithRecordedMiss((int row, int column)[] missCells, int resetThreshold = 3)
    {
        var newValues = (float[])Values.Clone();
        var newMissCounts = EnsureMissCounts();

        foreach (var (row, column) in missCells)
        {
            if (row >= 0 && row < Rows && column >= 0 && column < Columns)
            {
                var index = row * Columns + column;
                newMissCounts[index]++;

                // 連続missが閾値以上ならスコアをリセット
                if (newMissCounts[index] >= resetThreshold)
                {
                    newValues[index] = 0.0f;
                }
            }
        }

        return this with
        {
            Values = newValues,
            MissCounts = newMissCounts,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// [Issue #354] 検出成功時にmissカウントをリセットした新しいヒートマップを作成
    /// </summary>
    /// <param name="detectedCells">検出されたセルのリスト</param>
    /// <returns>更新されたヒートマップ</returns>
    public RoiHeatmapData WithResetMissCount((int row, int column)[] detectedCells)
    {
        var newMissCounts = EnsureMissCounts();

        foreach (var (row, column) in detectedCells)
        {
            if (row >= 0 && row < Rows && column >= 0 && column < Columns)
            {
                var index = row * Columns + column;
                newMissCounts[index] = 0; // 検出されたのでmissカウントをリセット
            }
        }

        return this with
        {
            MissCounts = newMissCounts,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// [Issue #354] 連続missが閾値を超えたセルを取得
    /// </summary>
    /// <param name="threshold">閾値</param>
    /// <returns>閾値を超えたセルのリスト</returns>
    public (int row, int column, int missCount)[] GetHighMissCells(int threshold = 5)
    {
        if (MissCounts == null)
        {
            return [];
        }

        var results = new System.Collections.Generic.List<(int, int, int)>();
        for (var row = 0; row < Rows; row++)
        {
            for (var column = 0; column < Columns; column++)
            {
                var missCount = MissCounts[row * Columns + column];
                if (missCount >= threshold)
                {
                    results.Add((row, column, missCount));
                }
            }
        }
        return [.. results];
    }

    /// <summary>
    /// [Issue #354] MissCountsを確保（nullの場合はゼロ配列を作成）
    /// </summary>
    private int[] EnsureMissCounts()
    {
        if (MissCounts != null)
        {
            return (int[])MissCounts.Clone();
        }
        return new int[Rows * Columns];
    }

    /// <summary>
    /// 新しいヒートマップを作成
    /// </summary>
    /// <param name="rows">行数</param>
    /// <param name="columns">列数</param>
    /// <param name="initialValue">初期値</param>
    public static RoiHeatmapData Create(int rows, int columns, float initialValue = 0.0f)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(rows, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(columns, 0);

        var size = rows * columns;
        var values = new float[size];
        var sampleCounts = new int[size];
        var missCounts = new int[size]; // [Issue #354]

        if (initialValue > 0.0f)
        {
            Array.Fill(values, initialValue);
        }

        return new RoiHeatmapData
        {
            Rows = rows,
            Columns = columns,
            Values = values,
            SampleCounts = sampleCounts,
            MissCounts = missCounts, // [Issue #354]
            LastUpdatedAt = DateTime.UtcNow,
            TotalSamples = 0
        };
    }
}
