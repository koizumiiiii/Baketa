using System;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// オブジェクトプールの基本インターフェース
/// </summary>
/// <typeparam name="T">プールするオブジェクトの型</typeparam>
public interface IObjectPool<T> : IDisposable where T : class
{
    /// <summary>
    /// プールからオブジェクトを取得
    /// </summary>
    /// <returns>プールされたオブジェクトまたは新規作成オブジェクト</returns>
    T Acquire();
    
    /// <summary>
    /// オブジェクトをプールに返却
    /// </summary>
    /// <param name="item">返却するオブジェクト</param>
    void Release(T item);
    
    /// <summary>
    /// プール内のすべてのオブジェクトをクリア
    /// </summary>
    void Clear();
    
    /// <summary>
    /// プールの統計情報を取得
    /// </summary>
    ObjectPoolStatistics Statistics { get; }
}

/// <summary>
/// オブジェクトプールの統計情報
/// </summary>
public class ObjectPoolStatistics
{
    /// <summary>現在プール内にあるオブジェクト数</summary>
    public int PooledCount { get; set; }
    
    /// <summary>プールの最大容量</summary>
    public int MaxCapacity { get; set; }
    
    /// <summary>プールから取得された総回数</summary>
    public long TotalGets { get; set; }
    
    /// <summary>プールに返却された総回数</summary>
    public long TotalReturns { get; set; }
    
    /// <summary>新規作成された総回数（プールにオブジェクトがなかった場合）</summary>
    public long TotalCreations { get; set; }
    
    /// <summary>プールヒット率（Get時にプールにオブジェクトがあった割合）</summary>
    public double HitRate => TotalGets > 0 ? (double)(TotalGets - TotalCreations) / TotalGets : 0.0;
    
    /// <summary>メモリ効率（返却率）</summary>
    public double ReturnRate => TotalGets > 0 ? (double)TotalReturns / TotalGets : 0.0;
    
    /// <summary>統計情報をクリア</summary>
    public void Clear()
    {
        TotalGets = 0;
        TotalReturns = 0;
        TotalCreations = 0;
    }
    
    /// <summary>統計情報の文字列表現</summary>
    public override string ToString()
    {
        return $"Pool[{PooledCount}/{MaxCapacity}] Gets:{TotalGets} Returns:{TotalReturns} Creates:{TotalCreations} HitRate:{HitRate:P1} ReturnRate:{ReturnRate:P1}";
    }
}