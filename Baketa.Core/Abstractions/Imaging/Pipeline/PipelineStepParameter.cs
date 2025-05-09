using System;
using System.Collections.Generic;

namespace Baketa.Core.Abstractions.Imaging.Pipeline
{
    /// <summary>
    /// パイプラインステップのパラメータを表すクラス
    /// </summary>
    public class PipelineStepParameter
    {
        /// <summary>
        /// パラメータ名
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// パラメータの説明
        /// </summary>
        public string Description { get; }
        
        /// <summary>
        /// パラメータの型
        /// </summary>
        public Type ParameterType { get; }
        
        /// <summary>
        /// デフォルト値
        /// </summary>
        public object? DefaultValue { get; }
        
        /// <summary>
        /// 最小値（数値パラメータの場合）
        /// </summary>
        public object? MinValue { get; }
        
        /// <summary>
        /// 最大値（数値パラメータの場合）
        /// </summary>
        public object? MaxValue { get; }
        
        /// <summary>
        /// 選択肢（列挙型パラメータの場合）
        /// </summary>
        public IReadOnlyCollection<object>? Options { get; }

        /// <summary>
        /// パラメータを作成します
        /// </summary>
        /// <param name="name">パラメータ名</param>
        /// <param name="description">パラメータの説明</param>
        /// <param name="parameterType">パラメータの型</param>
        /// <param name="defaultValue">デフォルト値</param>
        /// <param name="minValue">最小値（数値パラメータの場合）</param>
        /// <param name="maxValue">最大値（数値パラメータの場合）</param>
        /// <param name="options">選択肢（列挙型パラメータの場合）</param>
        public PipelineStepParameter(
            string name,
            string description,
            Type parameterType,
            object? defaultValue,
            object? minValue = null,
            object? maxValue = null,
            IReadOnlyCollection<object>? options = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
            ArgumentNullException.ThrowIfNull(description, nameof(description));
            ArgumentNullException.ThrowIfNull(parameterType, nameof(parameterType));
            
            // defaultValueがnullでなく、かつパラメータ型に代入できない場合はエラー
            if (defaultValue != null && !parameterType.IsInstanceOfType(defaultValue))
            {
                throw new ArgumentException(
                    $"デフォルト値の型 {defaultValue.GetType().Name} がパラメータ型 {parameterType.Name} と一致しません", 
                    nameof(defaultValue));
            }

            Name = name;
            Description = description;
            ParameterType = parameterType;
            DefaultValue = defaultValue;
            MinValue = minValue;
            MaxValue = maxValue;
            Options = options;
        }

        /// <summary>
        /// パラメータが有効な値であるかを検証します
        /// </summary>
        /// <param name="value">検証する値</param>
        /// <returns>有効な場合はtrue、無効な場合はfalse</returns>
        public bool ValidateValue(object? value)
        {
            if (value == null)
            {
                return !ParameterType.IsValueType || Nullable.GetUnderlyingType(ParameterType) != null;
            }

            if (!ParameterType.IsInstanceOfType(value))
            {
                return false;
            }

            // 数値型の場合、範囲チェック
            if (IsNumericType(ParameterType) && MinValue != null && MaxValue != null)
            {
                var compareResult = CompareValues(value, MinValue);
                if (compareResult < 0)
                {
                    return false;
                }

                compareResult = CompareValues(value, MaxValue);
                if (compareResult > 0)
                {
                    return false;
                }
            }

            // 列挙型の場合、選択肢チェック
            if (Options != null && Options.Count > 0)
            {
                return Options.Contains(value);
            }

            return true;
        }

        /// <summary>
        /// 指定された型が数値型かどうかを判断します
        /// </summary>
        /// <param name="type">チェックする型</param>
        /// <returns>数値型の場合はtrue、そうでない場合はfalse</returns>
        private static bool IsNumericType(Type type)
        {
            // Nullable型の場合は基底型を取得
            var underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            return underlyingType == typeof(byte)
                || underlyingType == typeof(sbyte)
                || underlyingType == typeof(short)
                || underlyingType == typeof(ushort)
                || underlyingType == typeof(int)
                || underlyingType == typeof(uint)
                || underlyingType == typeof(long)
                || underlyingType == typeof(ulong)
                || underlyingType == typeof(float)
                || underlyingType == typeof(double)
                || underlyingType == typeof(decimal);
        }

        /// <summary>
        /// 2つの値を比較します
        /// </summary>
        /// <param name="value1">比較する値1</param>
        /// <param name="value2">比較する値2</param>
        /// <returns>比較結果</returns>
        private static int CompareValues(object value1, object value2)
        {
            if (value1 is IComparable comparable && value2 != null)
            {
                return comparable.CompareTo(value2);
            }

            return 0;
        }
    }
}
