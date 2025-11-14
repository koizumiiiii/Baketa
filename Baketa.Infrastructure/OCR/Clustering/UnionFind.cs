namespace Baketa.Infrastructure.OCR.Clustering;

/// <summary>
/// Union-Find (Disjoint Set Union) データ構造
/// 連結成分検出のための効率的なアルゴリズム実装
/// </summary>
/// <remarks>
/// Phase 3.4A: OCRグルーピング問題の根本解決
/// - 時間計算量: O(α(N)) per operation（αはアッカーマン関数の逆関数、実質的にO(1)）
/// - 空間計算量: O(N)
/// - 経路圧縮最適化 (Path Compression) 実装済み
/// - ランクベース結合 (Union by Rank) 実装済み
/// </remarks>
public sealed class UnionFind
{
    private readonly int[] _parent;  // 各要素の親ノードのインデックス
    private readonly int[] _rank;    // 各要素のランク（木の高さの上限）

    /// <summary>
    /// UnionFindデータ構造を初期化
    /// </summary>
    /// <param name="size">要素数（OCRリージョン数）</param>
    public UnionFind(int size)
    {
        _parent = new int[size];
        _rank = new int[size];

        // 初期化: 各要素は自分自身を親とする独立したセット
        for (int i = 0; i < size; i++)
        {
            _parent[i] = i;
            _rank[i] = 0;
        }
    }

    /// <summary>
    /// 要素xが属するセットの代表元（ルート）を取得
    /// 経路圧縮最適化により、以降のFind操作が高速化される
    /// </summary>
    /// <param name="x">検索対象の要素インデックス</param>
    /// <returns>セットの代表元インデックス</returns>
    public int Find(int x)
    {
        // 経路圧縮 (Path Compression):
        // xからルートまでのパス上の全ノードを直接ルートに接続
        if (_parent[x] != x)
        {
            _parent[x] = Find(_parent[x]); // 再帰的にルートを探索し、経路を圧縮
        }
        return _parent[x];
    }

    /// <summary>
    /// 要素xとyが属するセットを結合
    /// ランクベース結合により、木の高さを抑制
    /// </summary>
    /// <param name="x">結合対象の要素1のインデックス</param>
    /// <param name="y">結合対象の要素2のインデックス</param>
    /// <returns>結合が実行された場合true、既に同じセットの場合false</returns>
    public bool Union(int x, int y)
    {
        int rootX = Find(x);
        int rootY = Find(y);

        // 既に同じセットに属している
        if (rootX == rootY)
            return false;

        // ランクベース結合 (Union by Rank):
        // ランクが小さい方を大きい方に結合し、木の高さを抑制
        if (_rank[rootX] < _rank[rootY])
        {
            _parent[rootX] = rootY;
        }
        else if (_rank[rootX] > _rank[rootY])
        {
            _parent[rootY] = rootX;
        }
        else
        {
            // ランクが同じ場合、一方をルートにし、そのランクを+1
            _parent[rootY] = rootX;
            _rank[rootX]++;
        }

        return true;
    }

    /// <summary>
    /// 連結成分をグループ化して返す
    /// </summary>
    /// <returns>連結成分ごとのグループ（キー: 代表元インデックス、値: 要素インデックスリスト）</returns>
    public Dictionary<int, List<int>> GetConnectedComponents()
    {
        var components = new Dictionary<int, List<int>>();

        for (int i = 0; i < _parent.Length; i++)
        {
            int root = Find(i);
            if (!components.TryGetValue(root, out var list))
            {
                list = [];
                components[root] = list;
            }
            list.Add(i);
        }

        return components;
    }
}
