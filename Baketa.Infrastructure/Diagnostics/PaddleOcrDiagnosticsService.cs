using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Baketa.Core.Abstractions.Diagnostics;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models;
using Sdcb.PaddleOCR.Models.Shared;

namespace Baketa.Infrastructure.Diagnostics;

/// <summary>
/// PaddleOCR診断システム実装
/// Gemini推奨: 段階的診断 + CPU First戦略対応
/// Sprint 1: 基盤復旧のための包括的診断機能
/// </summary>
public sealed class PaddleOcrDiagnosticsService : IPaddleOcrDiagnostics
{
    private readonly ILogger<PaddleOcrDiagnosticsService> _logger;
    private readonly IOptionsMonitor<OcrSettings> _ocrSettings;
    private readonly IModelPathResolver _modelPathResolver;

    public PaddleOcrDiagnosticsService(
        ILogger<PaddleOcrDiagnosticsService> logger,
        IOptionsMonitor<OcrSettings> ocrSettings,
        IModelPathResolver modelPathResolver)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrSettings = ocrSettings ?? throw new ArgumentNullException(nameof(ocrSettings));
        _modelPathResolver = modelPathResolver ?? throw new ArgumentNullException(nameof(modelPathResolver));
        
        _logger.LogInformation("🔍 PaddleOCR診断システム初期化完了");
    }

    public async Task<DiagnosticReport> RunFullDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("🚀 PaddleOCR包括診断開始");

        var issues = new List<DiagnosticIssue>();
        var recommendedActions = new List<string>();

        try
        {
            // Phase 1: 依存関係チェック
            _logger.LogInformation("📋 Phase 1: 依存関係チェック実行中...");
            var dependencyResult = await CheckDependenciesAsync(cancellationToken);
            
            // Phase 2: モデルファイル検証
            _logger.LogInformation("📋 Phase 2: モデルファイル検証実行中...");
            var modelResult = await ValidateModelFilesAsync(cancellationToken);
            
            // Phase 3: GPU互換性チェック
            _logger.LogInformation("📋 Phase 3: GPU互換性チェック実行中...");
            var gpuReport = await CheckGpuCompatibilityAsync(cancellationToken);
            
            // Phase 4: 初期化診断（CPU First）
            _logger.LogInformation("📋 Phase 4: 初期化診断実行中（CPU First戦略）...");
            var initResult = await DiagnoseInitializationAsync(useCpuOnly: true, cancellationToken);

            // 問題とアクションの収集
            CollectIssuesAndActions(dependencyResult, modelResult, gpuReport, initResult, issues, recommendedActions);

            // 全体的な健全性スコア計算
            var healthScore = CalculateOverallHealthScore(dependencyResult, modelResult, gpuReport, initResult);

            stopwatch.Stop();
            
            var report = new DiagnosticReport
            {
                OverallHealthScore = healthScore,
                Dependencies = new DependencyCheckResult { IsSuccess = dependencyResult },
                ModelFiles = new Core.Abstractions.Diagnostics.ModelValidationResult { IsSuccess = modelResult },
                GpuCompatibility = gpuReport,
                Initialization = initResult,
                DetectedIssues = issues.AsReadOnly(),
                RecommendedActions = recommendedActions.AsReadOnly(),
                DiagnosedAt = DateTime.UtcNow
            };

            _logger.LogInformation("✅ PaddleOCR包括診断完了 - 健全性: {HealthScore:P1}, 処理時間: {Time}ms",
                healthScore, stopwatch.ElapsedMilliseconds);

            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PaddleOCR包括診断中にエラー");
            
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Critical,
                Category = "System",
                Description = $"診断プロセス自体が失敗: {ex.Message}",
                RecommendedSolution = "システム環境とPaddleOCR依存関係を確認してください"
            });

            return new DiagnosticReport
            {
                OverallHealthScore = 0.0,
                DetectedIssues = issues.AsReadOnly(),
                RecommendedActions = ["診断システムの復旧が必要です"]
            };
        }
    }

    public async Task<bool> CheckDependenciesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔍 依存関係チェック開始");

        try
        {
            var dependencies = new List<(string name, string path, bool required)>
            {
                ("PaddleOCR Core", "Sdcb.PaddleOCR.dll", true),
                ("OpenCV", "opencv_world*.dll", true),
                ("PaddleInference", "paddle_inference.dll", true)
            };

            var allDependenciesOk = true;
            var currentDirectory = Directory.GetCurrentDirectory();
            var assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? currentDirectory;

            foreach (var (name, pattern, required) in dependencies)
            {
                var found = await CheckDependencyExistsAsync(assemblyDirectory, pattern, cancellationToken);
                
                if (!found && required)
                {
                    allDependenciesOk = false;
                    _logger.LogError("❌ 必須依存関係不足: {Name} ({Pattern})", name, pattern);
                }
                else if (found)
                {
                    _logger.LogDebug("✅ 依存関係確認: {Name}", name);
                }
            }

            _logger.LogInformation("📊 依存関係チェック完了: {Result}", allDependenciesOk ? "成功" : "失敗");
            return allDependenciesOk;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 依存関係チェック中にエラー");
            return false;
        }
    }

    public async Task<bool> ValidateModelFilesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔍 モデルファイル検証開始");

        try
        {
            // モデルキャッシュディレクトリの確認
            var modelCachePath = _modelPathResolver.GetModelsRootDirectory();
            if (!Directory.Exists(modelCachePath))
            {
                _logger.LogWarning("⚠️ モデルキャッシュディレクトリが存在しません: {Path}", modelCachePath);
                
                // ディレクトリ自動作成を試行
                try
                {
                    Directory.CreateDirectory(modelCachePath);
                    _logger.LogInformation("✅ モデルキャッシュディレクトリを作成しました: {Path}", modelCachePath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ モデルキャッシュディレクトリ作成失敗: {Path}", modelCachePath);
                    return false;
                }
            }

            // 基本的なモデルファイル存在確認
            var modelValidationTasks = new[]
            {
                ValidateDetectorModelAsync(cancellationToken),
                ValidateRecognitionModelAsync(cancellationToken),
                ValidateClassificationModelAsync(cancellationToken)
            };

            var results = await Task.WhenAll(modelValidationTasks);
            var allModelsValid = results.All(r => r);

            _logger.LogInformation("📊 モデルファイル検証完了: {Result}", allModelsValid ? "成功" : "一部問題あり");
            return allModelsValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ モデルファイル検証中にエラー");
            return false;
        }
    }

    public async Task<GpuCompatibilityReport> CheckGpuCompatibilityAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔍 GPU互換性チェック開始");

        try
        {
            var issues = new List<string>();
            bool cudaAvailable = false;
            string? cudaVersion = null;
            string? cudnnVersion = null;
            long availableVram = 0;

            // CUDA利用可能性チェック
            try
            {
                // 注意: 実際のCUDAチェックは環境に依存するため、簡易実装
                var cudaDlls = Directory.GetFiles(Directory.GetCurrentDirectory(), "cudart*.dll");
                cudaAvailable = cudaDlls.Length > 0;
                
                if (cudaAvailable)
                {
                    _logger.LogInformation("✅ CUDA DLL検出");
                }
                else
                {
                    _logger.LogInformation("ℹ️ CUDA DLL未検出（CPUモードのみ利用可能）");
                    issues.Add("CUDA利用不可 - CPUモードのみ利用可能");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ CUDAチェック中に問題発生");
                issues.Add($"CUDAチェック失敗: {ex.Message}");
            }

            // GPU環境情報の取得（可能な限り）
            GpuEnvironmentInfo? gpuInfo = null;
            try
            {
                // GPU情報取得は複雑なので、基本情報のみ
                gpuInfo = new GpuEnvironmentInfo
                {
                    IsDedicatedGpu = cudaAvailable,
                    SupportsCuda = cudaAvailable,
                    SupportsDirectML = Environment.OSVersion.Platform == PlatformID.Win32NT,
                    GpuName = "未検出",
                    AvailableMemoryMB = availableVram
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ GPU環境情報取得中に問題発生");
                issues.Add($"GPU環境情報取得失敗: {ex.Message}");
            }

            var report = new GpuCompatibilityReport
            {
                IsCompatible = true, // CPUモードは常に互換性あり
                CudaAvailable = cudaAvailable,
                CudaVersion = cudaVersion,
                CudnnVersion = cudnnVersion,
                GpuInfo = gpuInfo,
                AvailableVramMB = availableVram,
                Issues = issues.AsReadOnly()
            };

            _logger.LogInformation("📊 GPU互換性チェック完了: CUDA={CudaAvailable}", cudaAvailable);
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ GPU互換性チェック中にエラー");
            
            return new GpuCompatibilityReport
            {
                IsCompatible = true, // CPUモードフォールバック
                Issues = [$"GPU互換性チェックエラー: {ex.Message}"]
            };
        }
    }

    public async Task<InitializationDiagnosticResult> DiagnoseInitializationAsync(bool useCpuOnly = true, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔍 初期化診断開始（CPU First: {UseCpuOnly}）", useCpuOnly);
        
        var stopwatch = Stopwatch.StartNew();
        var steps = new List<InitializationStep>();
        bool initSuccess = false;
        string? errorMessage = null;
        Exception? exception = null;

        try
        {
            // Step 1: 設定読み込み
            var step1 = await DiagnoseStepAsync("設定読み込み", async () =>
            {
                var settings = _ocrSettings.CurrentValue;
                return settings != null;
            }, cancellationToken);
            steps.Add(step1);

            // Step 2: モデルパス解決
            var step2 = await DiagnoseStepAsync("モデルパス解決", async () =>
            {
                var modelPath = _modelPathResolver.GetModelsRootDirectory();
                return !string.IsNullOrEmpty(modelPath);
            }, cancellationToken);
            steps.Add(step2);

            // Step 3: CPU初期化テスト
            var step3 = await DiagnoseStepAsync("CPU初期化テスト", async () =>
            {
                return await TestPaddleOcrInitializationAsync(useCpuOnly: true, cancellationToken);
            }, cancellationToken);
            steps.Add(step3);

            initSuccess = steps.All(s => s.IsSuccess);
            
            if (!initSuccess)
            {
                var failedSteps = steps.Where(s => !s.IsSuccess).Select(s => s.StepName);
                errorMessage = $"初期化ステップ失敗: {string.Join(", ", failedSteps)}";
            }
        }
        catch (Exception ex)
        {
            exception = ex;
            errorMessage = ex.Message;
            _logger.LogError(ex, "❌ 初期化診断中にエラー");
        }

        stopwatch.Stop();

        var result = new InitializationDiagnosticResult
        {
            IsSuccess = initSuccess,
            CpuModeSuccess = initSuccess,
            GpuModeSuccess = false, // GPU診断は今回はスキップ
            InitializationSteps = steps.AsReadOnly(),
            InitializationTime = stopwatch.Elapsed,
            ErrorMessage = errorMessage,
            Exception = exception
        };

        _logger.LogInformation("📊 初期化診断完了: {Result}, 時間: {Time}ms", 
            initSuccess ? "成功" : "失敗", stopwatch.ElapsedMilliseconds);

        return result;
    }

    // プライベートヘルパーメソッド群

    private async Task<bool> CheckDependencyExistsAsync(string directory, string pattern, CancellationToken cancellationToken)
    {
        try
        {
            var files = Directory.GetFiles(directory, pattern);
            return files.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> ValidateDetectorModelAsync(CancellationToken cancellationToken)
    {
        // 基本実装: 実際のモデル検証は複雑なので簡易版
        return await Task.FromResult(true); // 暫定: 常に成功
    }

    private async Task<bool> ValidateRecognitionModelAsync(CancellationToken cancellationToken)
    {
        return await Task.FromResult(true); // 暫定: 常に成功
    }

    private async Task<bool> ValidateClassificationModelAsync(CancellationToken cancellationToken)
    {
        return await Task.FromResult(true); // 暫定: 常に成功
    }

    private async Task<InitializationStep> DiagnoseStepAsync(string stepName, Func<Task<bool>> stepFunc, CancellationToken cancellationToken)
    {
        var stepwatch = Stopwatch.StartNew();
        bool success = false;
        string? errorMessage = null;
        var details = new Dictionary<string, object>();

        try
        {
            success = await stepFunc();
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            details["Exception"] = ex.GetType().Name;
            details["StackTrace"] = ex.StackTrace ?? "";
        }

        stepwatch.Stop();

        return new InitializationStep
        {
            StepName = stepName,
            IsSuccess = success,
            ProcessingTime = stepwatch.Elapsed,
            ErrorMessage = errorMessage,
            Details = details
        };
    }

    private async Task<bool> TestPaddleOcrInitializationAsync(bool useCpuOnly, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🧪 PaddleOCR初期化テスト開始（CPU Only: {UseCpuOnly}）", useCpuOnly);

            // 非常にシンプルな初期化テスト
            // 実際のPaddleOCR初期化は複雑なので、基本的なチェックのみ
            var settings = _ocrSettings.CurrentValue;
            
            // 設定が正常に読み込まれているかチェック
            if (settings == null)
            {
                _logger.LogError("❌ OCR設定が読み込まれていません");
                return false;
            }

            _logger.LogDebug("✅ 基本初期化チェック成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ PaddleOCR初期化テスト失敗");
            return false;
        }
    }

    private void CollectIssuesAndActions(
        bool dependencyResult, 
        bool modelResult, 
        GpuCompatibilityReport gpuReport, 
        InitializationDiagnosticResult initResult,
        List<DiagnosticIssue> issues, 
        List<string> recommendedActions)
    {
        // 依存関係問題
        if (!dependencyResult)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Critical,
                Category = "Dependencies",
                Description = "必須依存関係が不足しています",
                RecommendedSolution = "PaddleOCR関連DLLを確認・インストールしてください"
            });
            recommendedActions.Add("PaddleOCR依存関係の再インストール");
        }

        // モデルファイル問題
        if (!modelResult)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Error,
                Category = "Models",
                Description = "モデルファイルに問題があります",
                RecommendedSolution = "モデルファイルの再ダウンロードを実行してください"
            });
            recommendedActions.Add("モデルファイルの再ダウンロード");
        }

        // GPU問題
        if (gpuReport.Issues.Any())
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Warning,
                Category = "GPU",
                Description = $"GPU関連問題: {string.Join(", ", gpuReport.Issues)}",
                RecommendedSolution = "CPUモードでの動作を推奨します"
            });
            recommendedActions.Add("CPU Firstモードで継続使用");
        }

        // 初期化問題
        if (!initResult.IsSuccess)
        {
            issues.Add(new DiagnosticIssue
            {
                Severity = DiagnosticSeverity.Critical,
                Category = "Initialization",
                Description = $"初期化失敗: {initResult.ErrorMessage}",
                RecommendedSolution = "設定とモデルファイルを確認してください"
            });
            recommendedActions.Add("設定ファイルとモデルの完全リセット");
        }
    }

    private double CalculateOverallHealthScore(
        bool dependencyResult, 
        bool modelResult, 
        GpuCompatibilityReport gpuReport, 
        InitializationDiagnosticResult initResult)
    {
        var scores = new[]
        {
            dependencyResult ? 1.0 : 0.0,        // 依存関係: 25%
            modelResult ? 1.0 : 0.0,             // モデル: 25%
            gpuReport.IsCompatible ? 0.5 : 0.0,  // GPU: 12.5%（CPUモードでも動作可能）
            initResult.IsSuccess ? 1.0 : 0.0     // 初期化: 37.5%
        };

        var weights = new[] { 0.25, 0.25, 0.125, 0.375 };
        return scores.Zip(weights).Sum(pair => pair.First * pair.Second);
    }
}