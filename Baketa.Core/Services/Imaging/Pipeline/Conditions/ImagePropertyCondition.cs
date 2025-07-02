using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;

namespace Baketa.Core.Services.Imaging.Pipeline.Conditions;

    /// <summary>
    /// 画像のプロパティに基づく条件
    /// </summary>
    public class ImagePropertyCondition : IPipelineCondition
    {
        /// <summary>
        /// 画像プロパティの種類
        /// </summary>
        public enum PropertyType
        {
            /// <summary>
            /// 画像の幅
            /// </summary>
            Width,
            
            /// <summary>
            /// 画像の高さ
            /// </summary>
            Height,
            
            /// <summary>
            /// 画像の形式
            /// </summary>
            Format,
            
            /// <summary>
            /// 画像のアスペクト比（幅÷高さ）
            /// </summary>
            AspectRatio,
            
            /// <summary>
            /// 画像の面積（幅×高さ）
            /// </summary>
            Area
        }
        
        /// <summary>
        /// 比較演算子
        /// </summary>
        public enum ComparisonOperator
        {
            /// <summary>
            /// 等しい
            /// </summary>
            Equal,
            
            /// <summary>
            /// 等しくない
            /// </summary>
            NotEqual,
            
            /// <summary>
            /// より大きい
            /// </summary>
            GreaterThan,
            
            /// <summary>
            /// 以上
            /// </summary>
            GreaterThanOrEqual,
            
            /// <summary>
            /// より小さい
            /// </summary>
            LessThan,
            
            /// <summary>
            /// 以下
            /// </summary>
            LessThanOrEqual
        }
        
        private readonly PropertyType _propertyType;
        private readonly ComparisonOperator _operator;
        private readonly object _compareValue;
        
        /// <summary>
        /// 条件の説明
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// 新しいImagePropertyConditionを作成します
        /// </summary>
        /// <param name="propertyType">比較する画像プロパティの種類</param>
        /// <param name="operator">比較演算子</param>
        /// <param name="compareValue">比較する値</param>
        public ImagePropertyCondition(PropertyType propertyType, ComparisonOperator @operator, object compareValue)
        {
            _propertyType = propertyType;
            _operator = @operator;
            _compareValue = compareValue ?? throw new ArgumentNullException(nameof(compareValue));
            
            Description = $"画像の{GetPropertyTypeString(propertyType)}が{GetOperatorString(@operator)}{compareValue}";
        }

        /// <summary>
        /// 条件を評価します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <returns>条件が真の場合はtrue、偽の場合はfalse</returns>
        public Task<bool> EvaluateAsync(IAdvancedImage input, PipelineContext context)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(context);
            
            // 画像プロパティの値を取得
            object propertyValue = GetPropertyValue(input);
            
            // 値を比較
            bool result = Compare(propertyValue, _compareValue);
            
            return Task.FromResult(result);
        }

        /// <summary>
        /// 画像から指定されたプロパティの値を取得します
        /// </summary>
        /// <param name="image">画像</param>
        /// <returns>プロパティの値</returns>
        private object GetPropertyValue(IAdvancedImage image)
        {
            return _propertyType switch
            {
                PropertyType.Width => image.Width,
                PropertyType.Height => image.Height,
                PropertyType.Format => image.Format,
                PropertyType.AspectRatio => CalculateAspectRatio(image),
                PropertyType.Area => CalculateArea(image),
                _ => throw new InvalidOperationException($"不明なプロパティ種類: {_propertyType}")
            };
        }

        /// <summary>
        /// 画像のアスペクト比を計算します
        /// </summary>
        /// <param name="image">画像</param>
        /// <returns>アスペクト比</returns>
        private static double CalculateAspectRatio(IAdvancedImage image)
        {
            return image.Height != 0 ? (double)image.Width / image.Height : 0;
        }

        /// <summary>
        /// 画像の面積を計算します
        /// </summary>
        /// <param name="image">画像</param>
        /// <returns>面積</returns>
        private static int CalculateArea(IAdvancedImage image)
        {
            return image.Width * image.Height;
        }

        /// <summary>
        /// 値を比較します
        /// </summary>
        /// <param name="value1">比較する値1</param>
        /// <param name="value2">比較する値2</param>
        /// <returns>比較結果</returns>
        private bool Compare(object value1, object value2)
        {
            // 数値同士の比較
            if (value1 is IComparable comparable1 && TryConvertToCompatibleType(value2, value1.GetType(), out var convertedValue2))
            {
                int comparisonResult = comparable1.CompareTo(convertedValue2);
                
                return _operator switch
                {
                    ComparisonOperator.Equal => comparisonResult == 0,
                    ComparisonOperator.NotEqual => comparisonResult != 0,
                    ComparisonOperator.GreaterThan => comparisonResult > 0,
                    ComparisonOperator.GreaterThanOrEqual => comparisonResult >= 0,
                    ComparisonOperator.LessThan => comparisonResult < 0,
                    ComparisonOperator.LessThanOrEqual => comparisonResult <= 0,
                    _ => throw new InvalidOperationException($"不明な比較演算子: {_operator}")
                };
            }
            
            // それ以外の比較（Equals）
            if (_operator == ComparisonOperator.Equal)
            {
                return value1.Equals(value2);
            }
            
            if (_operator == ComparisonOperator.NotEqual)
            {
                return !value1.Equals(value2);
            }
            
            throw new InvalidOperationException($"型 {value1.GetType().Name} と {value2.GetType().Name} を比較できません。");
        }

        /// <summary>
        /// 値を互換性のある型に変換します
        /// </summary>
        /// <param name="value">変換する値</param>
        /// <param name="targetType">変換先の型</param>
        /// <param name="convertedValue">変換された値</param>
        /// <returns>変換に成功した場合はtrue、失敗した場合はfalse</returns>
        private static bool TryConvertToCompatibleType(object value, Type targetType, out object convertedValue)
        {
            try
            {
                convertedValue = Convert.ChangeType(value, targetType, System.Globalization.CultureInfo.InvariantCulture);
                return true;
            }
            catch (InvalidCastException)
            {
                convertedValue = null!;
                return false;
            }
            catch (FormatException)
            {
                convertedValue = null!;
                return false;
            }
            catch (OverflowException)
            {
                convertedValue = null!;
                return false;
            }
        }

        /// <summary>
        /// プロパティ種類の文字列表現を取得します
        /// </summary>
        /// <param name="propertyType">プロパティ種類</param>
        /// <returns>文字列表現</returns>
        private static string GetPropertyTypeString(PropertyType propertyType)
        {
            return propertyType switch
            {
                PropertyType.Width => "幅",
                PropertyType.Height => "高さ",
                PropertyType.Format => "フォーマット",
                PropertyType.AspectRatio => "アスペクト比",
                PropertyType.Area => "面積",
                _ => propertyType.ToString()
            };
        }

        /// <summary>
        /// 演算子の文字列表現を取得します
        /// </summary>
        /// <param name="operator">演算子</param>
        /// <returns>文字列表現</returns>
        private static string GetOperatorString(ComparisonOperator @operator)
        {
            return @operator switch
            {
                ComparisonOperator.Equal => "＝",
                ComparisonOperator.NotEqual => "≠",
                ComparisonOperator.GreaterThan => "＞",
                ComparisonOperator.GreaterThanOrEqual => "≧",
                ComparisonOperator.LessThan => "＜",
                ComparisonOperator.LessThanOrEqual => "≦",
                _ => @operator.ToString()
            };
        }
    }
