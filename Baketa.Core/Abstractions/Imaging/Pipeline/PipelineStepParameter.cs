using System;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// パイプラインステップのパラメータ定義を表すクラス
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="name">パラメータ名</param>
/// <param name="description">パラメータの説明</param>
/// <param name="parameterType">パラメータの型</param>
/// <param name="defaultValue">デフォルト値</param>
/// <param name="minValue">最小値（数値型パラメータのみ）</param>
/// <param name="maxValue">最大値（数値型パラメータのみ）</param>
/// <param name="allowedValues">許容される値のリスト（列挙型や選択肢がある場合）</param>
public class PipelineStepParameter(
        string name,
        string description,
        Type parameterType,
        object? defaultValue = null,
        object? minValue = null,
        object? maxValue = null,
        IReadOnlyCollection<object>? allowedValues = null)
{
    /// <summary>
    /// パラメータ名
    /// </summary>
    public string Name { get; } = name ?? throw new ArgumentNullException(nameof(name));

    /// <summary>
    /// パラメータの説明
    /// </summary>
    public string Description { get; } = description ?? throw new ArgumentNullException(nameof(description));

    /// <summary>
    /// パラメータの型
    /// </summary>
    public Type ParameterType { get; } = parameterType ?? throw new ArgumentNullException(nameof(parameterType));

    /// <summary>
    /// デフォルト値
    /// </summary>
    public object? DefaultValue { get; } = defaultValue;

    /// <summary>
    /// パラメータが必須かどうか
    /// </summary>
    public bool IsRequired { get; } = defaultValue == null; // デフォルト値がない場合は必須

    /// <summary>
    /// パラメータの最小値（数値型パラメータのみ）
    /// </summary>
    public object? MinValue { get; } = minValue;

    /// <summary>
    /// パラメータの最大値（数値型パラメータのみ）
    /// </summary>
    public object? MaxValue { get; } = maxValue;

    /// <summary>
    /// パラメータのステップサイズ（数値型パラメータのみ）
    /// </summary>
    public object? StepSize { get; }

    /// <summary>
    /// 許容される値のリスト（列挙型や選択肢がある場合）
    /// </summary>
    public IReadOnlyCollection<object>? AllowedValues { get; } = allowedValues;

    /// <summary>
    /// 値がこのパラメータに対して有効かどうかを検証します
    /// </summary>
    /// <param name="value">検証する値</param>
    /// <returns>有効な場合はtrue、そうでない場合はfalse</returns>
    public bool ValidateValue(object? value)
        {
            // 必須パラメータのnullチェック
            if (IsRequired && value == null)
                return false;
                
            // null値が許可されている場合
            if (!IsRequired && value == null)
                return true;
                
            // 型チェック
            if (value != null && !ParameterType.IsInstanceOfType(value))
                return false;
                
            // 許容値リストのチェック
            if (AllowedValues != null && AllowedValues.Count > 0)
            {
                return AllowedValues.Contains(value);
            }
            
            // 数値型の場合、最小値・最大値のチェック
            if (value != null && IsNumericType(ParameterType))
            {
                // 全てdoubleに変換して比較
                double doubleValue = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                
                // 最小値のチェック
                if (MinValue != null)
                {
                    double minValue = Convert.ToDouble(MinValue, System.Globalization.CultureInfo.InvariantCulture);
                    if (doubleValue < minValue)
                        return false;
                }
                
                // 最大値のチェック
                if (MaxValue != null)
                {
                    double maxValue = Convert.ToDouble(MaxValue, System.Globalization.CultureInfo.InvariantCulture);
                    if (doubleValue > maxValue)
                        return false;
                }
            }
            
            // すべてのチェックに通過
            return true;
        }
        
        /// <summary>
        /// 指定された型が数値型かどうかを判定します
        /// </summary>
        /// <param name="type">判定する型</param>
        /// <returns>数値型の場合はtrue、そうでない場合はfalse</returns>
        private static bool IsNumericType(Type type)
        {
            return Type.GetTypeCode(type) switch
            {
                TypeCode.Byte => true,
                TypeCode.SByte => true,
                TypeCode.UInt16 => true,
                TypeCode.UInt32 => true,
                TypeCode.UInt64 => true,
                TypeCode.Int16 => true,
                TypeCode.Int32 => true,
                TypeCode.Int64 => true,
                TypeCode.Decimal => true,
                TypeCode.Double => true,
                TypeCode.Single => true,
                _ => false
            };
        }
    }
