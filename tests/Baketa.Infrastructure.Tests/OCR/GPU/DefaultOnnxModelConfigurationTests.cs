using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.GPU;

namespace Baketa.Infrastructure.Tests.OCR.GPU;

/// <summary>
/// DefaultOnnxModelConfiguration テスト
/// Issue #143 Week 2 Phase 2: モデル出力テンソル名外部化テスト
/// </summary>
public class DefaultOnnxModelConfigurationTests
{
    private readonly Mock<ILogger<DefaultOnnxModelConfiguration>> _mockLogger;
    private readonly OcrSettings _testSettings;
    private readonly DefaultOnnxModelConfiguration _configuration;

    public DefaultOnnxModelConfigurationTests()
    {
        _mockLogger = new Mock<ILogger<DefaultOnnxModelConfiguration>>();
        
        _testSettings = new OcrSettings
        {
            GpuSettings = new GpuOcrSettings
            {
                DetectionModelPath = @"test_models\detection.onnx",
                RecognitionModelPath = @"test_models\recognition.onnx",
                LanguageIdentificationModelPath = @"test_models\language_id.onnx"
            }
        };
        
        var options = new OptionsWrapper<OcrSettings>(_testSettings);
        _configuration = new DefaultOnnxModelConfiguration(_mockLogger.Object, options);
    }

    [Fact]
    public void Constructor_ShouldInitializeSuccessfully()
    {
        // Act & Assert
        Assert.NotNull(_configuration);
        
        // デフォルトモデルが初期化されていることを確認
        var availableModels = _configuration.GetAvailableModels();
        Assert.Contains("TextDetection", availableModels);
        Assert.Contains("TextRecognition", availableModels);
        Assert.Contains("LanguageIdentification", availableModels);
    }

    [Fact]
    public void GetDetectionModelInfo_ShouldReturnValidConfiguration()
    {
        // Act
        var modelInfo = _configuration.GetDetectionModelInfo();

        // Assert
        Assert.NotNull(modelInfo);
        Assert.Equal(_testSettings.GpuSettings.DetectionModelPath, modelInfo.ModelPath);
        Assert.Contains("x", modelInfo.InputTensorNames);
        Assert.Contains("save_infer_model/scale_0.tmp_1", modelInfo.OutputTensorNames);
        Assert.Equal(1, modelInfo.RecommendedBatchSize);
        Assert.Equal("PP-OCRv4", modelInfo.ModelVersion);
        
        // 前処理設定確認
        Assert.NotNull(modelInfo.PreprocessingConfig);
        Assert.True(modelInfo.PreprocessingConfig.EnableNormalization);
        Assert.NotNull(modelInfo.PreprocessingConfig.ResizeConfig);
        Assert.Equal(960, modelInfo.PreprocessingConfig.ResizeConfig.TargetWidth);
        
        // 後処理設定確認
        Assert.NotNull(modelInfo.PostprocessingConfig);
        Assert.Equal(0.3f, modelInfo.PostprocessingConfig.ConfidenceThreshold);
        Assert.Equal(1000, modelInfo.PostprocessingConfig.MaxDetections);
    }

    [Fact]
    public void GetRecognitionModelInfo_ShouldReturnValidConfiguration()
    {
        // Act
        var modelInfo = _configuration.GetRecognitionModelInfo();

        // Assert
        Assert.NotNull(modelInfo);
        Assert.Equal(_testSettings.GpuSettings.RecognitionModelPath, modelInfo.ModelPath);
        Assert.Contains("x", modelInfo.InputTensorNames);
        Assert.Contains("save_infer_model/scale_0.tmp_1", modelInfo.OutputTensorNames);
        Assert.Equal(8, modelInfo.RecommendedBatchSize);
        Assert.Equal("PP-OCRv4", modelInfo.ModelVersion);
        
        // 入力形状確認
        Assert.True(modelInfo.InputShapes.ContainsKey("x"));
        var inputShape = modelInfo.InputShapes["x"];
        Assert.Equal(new[] { 1, 3, 48, 320 }, inputShape);
        
        // メタデータ確認
        Assert.Equal("TextRecognition", modelInfo.Metadata["ModelType"]);
        Assert.Equal("CTC", modelInfo.Metadata["OutputFormat"]);
    }

    [Fact]
    public void GetLanguageIdentificationModelInfo_ShouldReturnValidConfiguration()
    {
        // Act
        var modelInfo = _configuration.GetLanguageIdentificationModelInfo();

        // Assert
        Assert.NotNull(modelInfo);
        Assert.Equal(_testSettings.GpuSettings.LanguageIdentificationModelPath, modelInfo.ModelPath);
        Assert.Equal(16, modelInfo.RecommendedBatchSize); // 軽量なので大きなバッチサイズ
        Assert.Equal(64, modelInfo.EstimatedMemoryUsageMB); // 軽量
        
        // 言語識別特有の設定確認
        Assert.Equal(0.9f, modelInfo.PostprocessingConfig!.ConfidenceThreshold); // 高い信頼度要求
        Assert.Equal("LanguageIdentification", modelInfo.Metadata["ModelType"]);
        
        // 入力サイズが言語識別用に最適化されていることを確認
        var inputShape = modelInfo.InputShapes["x"];
        Assert.Equal(new[] { 1, 3, 48, 192 }, inputShape);
    }

    [Fact]
    public void GetCustomModelInfo_WithValidName_ShouldReturnModel()
    {
        // Arrange
        var customModel = new OnnxModelInfo
        {
            ModelPath = @"custom\model.onnx",
            InputTensorNames = ["input"],
            OutputTensorNames = ["output"],
            ModelVersion = "1.0"
        };
        
        _configuration.UpdateModelInfo("CustomModel", customModel);

        // Act
        var result = _configuration.GetCustomModelInfo("CustomModel");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(customModel.ModelPath, result.ModelPath);
        Assert.Equal(customModel.ModelVersion, result.ModelVersion);
    }

    [Fact]
    public void GetCustomModelInfo_WithInvalidName_ShouldReturnNull()
    {
        // Act
        var result = _configuration.GetCustomModelInfo("NonExistentModel");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCustomModelInfo_WithEmptyName_ShouldReturnNull()
    {
        // Act
        var result = _configuration.GetCustomModelInfo("");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void UpdateModelInfo_ShouldAddOrUpdateModel()
    {
        // Arrange
        var modelInfo = new OnnxModelInfo
        {
            ModelPath = @"updated\model.onnx",
            InputTensorNames = ["new_input"],
            OutputTensorNames = ["new_output"],
            ModelVersion = "2.0"
        };

        // Act
        _configuration.UpdateModelInfo("TestModel", modelInfo);
        var result = _configuration.GetCustomModelInfo("TestModel");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(modelInfo.ModelPath, result.ModelPath);
        Assert.Equal(modelInfo.ModelVersion, result.ModelVersion);
        Assert.Contains("new_input", result.InputTensorNames);
        Assert.Contains("new_output", result.OutputTensorNames);
    }

    [Fact]
    public void UpdateModelInfo_WithNullModelInfo_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            _configuration.UpdateModelInfo("TestModel", null!));
    }

    [Fact]
    public void UpdateModelInfo_WithEmptyModelName_ShouldThrowException()
    {
        // Arrange
        var modelInfo = new OnnxModelInfo();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => 
            _configuration.UpdateModelInfo("", modelInfo));
    }

    [Fact]
    public void GetAvailableModels_ShouldReturnAllRegisteredModels()
    {
        // Arrange
        var customModel = new OnnxModelInfo { ModelPath = @"test\model.onnx" };
        _configuration.UpdateModelInfo("CustomModel", customModel);

        // Act
        var availableModels = _configuration.GetAvailableModels();

        // Assert
        Assert.Contains("TextDetection", availableModels);
        Assert.Contains("TextRecognition", availableModels);
        Assert.Contains("LanguageIdentification", availableModels);
        Assert.Contains("CustomModel", availableModels);
        Assert.True(availableModels.Count >= 4);
    }

    [Fact]
    public void ValidateModel_WithNonExistentModel_ShouldReturnInvalid()
    {
        // Act
        var result = _configuration.ValidateModel("NonExistentModel");

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("モデル 'NonExistentModel' が見つかりません", result.ValidationErrors);
    }

    [Fact]
    public void ValidateModel_WithValidModel_ShouldReturnValidationDetails()
    {
        // Arrange
        var modelInfo = new OnnxModelInfo
        {
            ModelPath = @"test\model.onnx", // 実在しないパス
            InputTensorNames = ["input1", "input2"],
            OutputTensorNames = ["output1"],
            InputShapes = new Dictionary<string, int[]>
            {
                ["input1"] = [1, 3, 224, 224]
            },
            EstimatedMemoryUsageMB = 512,
            ModelVersion = "1.0"
        };
        
        _configuration.UpdateModelInfo("TestModel", modelInfo);

        // Act
        var result = _configuration.ValidateModel("TestModel");

        // Assert
        Assert.False(result.IsValid); // ファイルが存在しないため
        Assert.Contains(result.ValidationErrors, e => e.Contains("モデルファイルが存在しません"));
        
        // 警告チェック
        Assert.Contains(result.ValidationWarnings, w => w.Contains("入力テンソル 'input2' の形状が定義されていません"));
        Assert.Contains(result.ValidationWarnings, w => w.Contains("出力テンソル 'output1' の形状が定義されていません"));
        
        // 詳細情報チェック
        Assert.Equal(2, result.ValidationDetails["InputTensorCount"]);
        Assert.Equal(1, result.ValidationDetails["OutputTensorCount"]);
        Assert.Equal("1.0", result.ValidationDetails["ModelVersion"]);
    }

    [Fact]
    public void ValidateModel_WithLargeMemoryUsage_ShouldGenerateWarning()
    {
        // Arrange
        var modelInfo = new OnnxModelInfo
        {
            ModelPath = @"test\large_model.onnx",
            InputTensorNames = ["input"],
            OutputTensorNames = ["output"],
            EstimatedMemoryUsageMB = 20480, // 20GB
            ModelVersion = "1.0"
        };
        
        _configuration.UpdateModelInfo("LargeModel", modelInfo);

        // Act
        var result = _configuration.ValidateModel("LargeModel");

        // Assert
        Assert.Contains(result.ValidationWarnings, w => w.Contains("推定メモリ使用量が大きすぎます"));
    }

    [Fact]
    public void ValidateModel_WithEmptyTensorNames_ShouldGenerateWarnings()
    {
        // Arrange
        var modelInfo = new OnnxModelInfo
        {
            ModelPath = @"test\empty_tensors.onnx",
            InputTensorNames = [], // 空
            OutputTensorNames = [], // 空
            ModelVersion = "1.0"
        };
        
        _configuration.UpdateModelInfo("EmptyTensorsModel", modelInfo);

        // Act
        var result = _configuration.ValidateModel("EmptyTensorsModel");

        // Assert
        Assert.Contains(result.ValidationWarnings, w => w.Contains("入力テンソル名が定義されていません"));
        Assert.Contains(result.ValidationWarnings, w => w.Contains("出力テンソル名が定義されていません"));
    }
}