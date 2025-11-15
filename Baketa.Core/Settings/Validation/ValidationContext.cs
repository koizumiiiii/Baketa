using System;
using System.Collections.Generic;

namespace Baketa.Core.Settings.Validation;

/// <summary>
/// 検証コンテキストクラス
/// 検証時の追加情報を保持し、検証ルール間でデータを共有
/// </summary>
public sealed class ValidationContext
{
    /// <summary>
    /// 全体の設定オブジェクト
    /// </summary>
    public AppSettings Settings { get; }

    /// <summary>
    /// カテゴリ名
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// プロパティパス
    /// </summary>
    public string PropertyPath { get; }

    /// <summary>
    /// 検証の深さ
    /// </summary>
    public int Depth { get; internal set; }

    /// <summary>
    /// 親コンテキスト
    /// </summary>
    public ValidationContext? Parent { get; }

    /// <summary>
    /// 追加のプロパティディクショナリ
    /// 検証ルール間でカスタムデータを共有するために使用
    /// </summary>
    public IReadOnlyDictionary<string, object> Properties { get; }

    /// <summary>
    /// 検証開始時刻
    /// </summary>
    public DateTime ValidationStartTime { get; }

    /// <summary>
    /// 検証モード
    /// </summary>
    public ValidationMode Mode { get; }

    private readonly Dictionary<string, object> _properties;

    /// <summary>
    /// ValidationContextを初期化します
    /// </summary>
    /// <param name="settings">全体の設定オブジェクト</param>
    /// <param name="category">カテゴリ名</param>
    /// <param name="propertyPath">プロパティパス</param>
    /// <param name="parent">親コンテキスト</param>
    /// <param name="mode">検証モード</param>
    public ValidationContext(
        AppSettings settings,
        string category,
        string propertyPath,
        ValidationContext? parent = null,
        ValidationMode mode = ValidationMode.Full)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Category = category ?? throw new ArgumentNullException(nameof(category));
        PropertyPath = propertyPath ?? throw new ArgumentNullException(nameof(propertyPath));
        Parent = parent;
        Mode = mode;

        _properties = [];
        Properties = _properties;
        ValidationStartTime = DateTime.Now;
        Depth = parent?.Depth + 1 ?? 0;
    }

    /// <summary>
    /// プロパティを設定します
    /// </summary>
    /// <param name="key">キー</param>
    /// <param name="value">値</param>
    public void SetProperty(string key, object value)
    {
        ArgumentNullException.ThrowIfNull(key);
        _properties[key] = value;
    }

    /// <summary>
    /// プロパティを取得します
    /// </summary>
    /// <typeparam name="T">プロパティの型</typeparam>
    /// <param name="key">キー</param>
    /// <param name="defaultValue">デフォルト値</param>
    /// <returns>プロパティ値</returns>
    public T GetProperty<T>(string key, T defaultValue = default!)
    {
        ArgumentNullException.ThrowIfNull(key);

        if (_properties.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// プロパティが存在するかどうかを確認します
    /// </summary>
    /// <param name="key">キー</param>
    /// <returns>存在する場合はtrue</returns>
    public bool HasProperty(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _properties.ContainsKey(key);
    }

    /// <summary>
    /// 子コンテキストを作成します
    /// </summary>
    /// <param name="childPropertyPath">子プロパティパス</param>
    /// <param name="childCategory">子カテゴリ（省略時は現在のカテゴリを継承）</param>
    /// <returns>子コンテキスト</returns>
    public ValidationContext CreateChild(string childPropertyPath, string? childCategory = null)
    {
        ArgumentNullException.ThrowIfNull(childPropertyPath);

        var fullPath = string.IsNullOrEmpty(PropertyPath)
            ? childPropertyPath
            : $"{PropertyPath}.{childPropertyPath}";

        return new ValidationContext(
            Settings,
            childCategory ?? Category,
            fullPath,
            this,
            Mode);
    }

    /// <summary>
    /// フルパスを取得します（カテゴリ + プロパティパス）
    /// </summary>
    /// <returns>フルパス</returns>
    public string GetFullPath()
    {
        return string.IsNullOrEmpty(Category)
            ? PropertyPath
            : $"{Category}.{PropertyPath}";
    }

    /// <summary>
    /// ルートコンテキストまでの経路を取得します
    /// </summary>
    /// <returns>ルートからのパス配列</returns>
    public string[] GetPathFromRoot()
    {
        var paths = new List<string>();
        var current = this;

        while (current != null)
        {
            if (!string.IsNullOrEmpty(current.PropertyPath))
            {
                paths.Insert(0, current.PropertyPath);
            }
            current = current.Parent;
        }

        return [.. paths];
    }

    /// <summary>
    /// 検証実行時間を取得します
    /// </summary>
    /// <returns>実行時間（ミリ秒）</returns>
    public double GetElapsedMilliseconds()
    {
        return (DateTime.Now - ValidationStartTime).TotalMilliseconds;
    }
}

/// <summary>
/// 検証モード列挙型
/// </summary>
public enum ValidationMode
{
    /// <summary>
    /// 完全検証（すべてのルールを実行）
    /// </summary>
    Full,

    /// <summary>
    /// 高速検証（基本的なルールのみ）
    /// </summary>
    Fast,

    /// <summary>
    /// 厳密検証（すべてのルールと警告チェック）
    /// </summary>
    Strict,

    /// <summary>
    /// αテスト検証（αテスト固有のルールのみ）
    /// </summary>
    AlphaTest
}
