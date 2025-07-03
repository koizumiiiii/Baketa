#!/bin/bash
# baketa-auto-dev.sh - Baketa自動開発スクリプト（WSL完結版）

set -e  # エラー時即座に終了

# 設定
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
LOGS_DIR="$SCRIPT_DIR/logs"
CONFIG_FILE="$SCRIPT_DIR/config.json"
MAX_RETRIES=3

# ログディレクトリ作成
mkdir -p "$LOGS_DIR"

function log_message() {
    local level="$1"
    local message="$2"
    local timestamp=$(date '+%Y-%m-%d %H:%M:%S')
    echo "[$timestamp] [$level] $message" | tee -a "$LOGS_DIR/auto-dev.log"
}

function check_prerequisites() {
    log_message "INFO" "前提条件確認開始"
    
    # .NET SDK確認
    if ! command -v dotnet >/dev/null 2>&1; then
        log_message "ERROR" ".NET SDKが見つかりません"
        echo ""
        echo "🔧 .NET SDKをインストールしてください:"
        echo "   wget https://packages.microsoft.com/config/ubuntu/\$(lsb_release -rs)/packages-microsoft-prod.deb"
        echo "   sudo dpkg -i packages-microsoft-prod.deb"
        echo "   sudo apt update"
        echo "   sudo apt install -y dotnet-sdk-8.0"
        exit 1
    fi
    
    # Claude Code確認
    if ! command -v claude-code >/dev/null 2>&1; then
        log_message "WARN" "Claude Codeが見つかりません（手動モードで実行）"
        MANUAL_MODE=true
    else
        log_message "INFO" "Claude Code利用可能"
        MANUAL_MODE=false
    fi
    
    # Gemini CLI確認
    if ! command -v gemini >/dev/null 2>&1; then
        log_message "WARN" "Gemini CLIが見つかりません（エラー分析なしで実行）"
        GEMINI_AVAILABLE=false
    else
        log_message "INFO" "Gemini CLI利用可能"
        GEMINI_AVAILABLE=true
    fi
    
    log_message "INFO" "前提条件確認完了"
}

function execute_implementation() {
    local feature_name="$1"
    local description="$2"
    
    log_message "INFO" "実装開始: $feature_name"
    
    local prompt="Baketa Windows専用OCRオーバーレイアプリの機能実装:

機能名: $feature_name
説明: $description

要件:
- Windows専用アプリケーション（WSL環境でビルド・テスト）
- クリーンアーキテクチャ（Core/Infrastructure/Application/UI）
- PaddleOCR + OpenCV画像処理
- Avalonia UI
- 非同期処理とエラー処理を適切に実装

プロジェクト構成:
- Baketa.Core: コア機能と抽象化
- Baketa.Infrastructure: プラットフォーム非依存のインフラ
- Baketa.Infrastructure.Platform: Windows固有の実装
- Baketa.Application: ビジネスロジックと機能統合
- Baketa.UI: ユーザーインターフェース (Avalonia UI)

実装してください。"

    if [ "$MANUAL_MODE" = "true" ]; then
        log_message "WARN" "手動実装モード"
        echo ""
        echo "📝 以下の内容で実装してください:"
        echo "   機能名: $feature_name"
        echo "   説明: $description"
        echo ""
        read -p "実装完了後、Enterを押してください..."
    else
        log_message "INFO" "Claude Code実行中..."
        claude-code "$prompt"
    fi
    
    log_message "INFO" "実装完了"
}

function execute_build_with_retry() {
    log_message "INFO" "ビルド確認開始"
    
    cd "$PROJECT_ROOT"
    
    for attempt in $(seq 1 $MAX_RETRIES); do
        log_message "INFO" "ビルド試行 $attempt/$MAX_RETRIES"
        
        if dotnet build --configuration Release --verbosity minimal 2>&1 | tee "$LOGS_DIR/build_attempt_$attempt.log"; then
            log_message "INFO" "✅ ビルド成功"
            return 0
        else
            log_message "WARN" "❌ ビルド失敗 (試行 $attempt/$MAX_RETRIES)"
            
            if [ $attempt -lt $MAX_RETRIES ]; then
                # エラー修正を試行
                local build_errors=$(tail -20 "$LOGS_DIR/build_attempt_$attempt.log")
                fix_build_errors "$build_errors" "$attempt"
            fi
        fi
    done
    
    log_message "ERROR" "最大試行数に達しました。ビルドに失敗しました。"
    return 1
}

function fix_build_errors() {
    local build_errors="$1"
    local attempt="$2"
    
    log_message "INFO" "ビルドエラー修正試行 $attempt"
    
    local fix_prompt="以下のビルドエラーを修正してください:

エラー内容:
$build_errors

修正要求:
- コンパイルエラーを解決
- 依存関係の問題を修正
- Baketa Windows専用OCRアプリの要件を満たす修正

修正してください。"

    if [ "$MANUAL_MODE" = "true" ]; then
        echo ""
        echo "🔧 以下のビルドエラーを修正してください:"
        echo "$build_errors"
        echo ""
        read -p "修正完了後、Enterを押してください..."
    else
        log_message "INFO" "Claude Codeでビルドエラー修正中..."
        claude-code "$fix_prompt"
    fi
}

function execute_tests_with_analysis() {
    log_message "INFO" "テスト実行開始"
    
    cd "$PROJECT_ROOT"
    
    # テストプロジェクトの存在確認
    if ! find . -name "*.Test*.csproj" -o -name "*Tests.csproj" | grep -q .; then
        log_message "WARN" "テストプロジェクトが見つかりません"
        return 0
    fi
    
    for attempt in $(seq 1 $MAX_RETRIES); do
        log_message "INFO" "テスト実行試行 $attempt/$MAX_RETRIES"
        
        if dotnet test --configuration Release --logger "console;verbosity=detailed" 2>&1 | tee "$LOGS_DIR/test_attempt_$attempt.log"; then
            log_message "INFO" "✅ 全テスト通過"
            return 0
        else
            log_message "WARN" "❌ テスト失敗 (試行 $attempt/$MAX_RETRIES)"
            
            if [ $attempt -lt $MAX_RETRIES ]; then
                # テストエラー分析・修正を試行
                local test_errors=$(tail -30 "$LOGS_DIR/test_attempt_$attempt.log")
                fix_test_errors "$test_errors" "$attempt"
            fi
        fi
    done
    
    log_message "ERROR" "最大試行数に達しました。テストに失敗しました。"
    return 1
}

function fix_test_errors() {
    local test_errors="$1"
    local attempt="$2"
    
    log_message "INFO" "テストエラー分析・修正試行 $attempt"
    
    # Gemini分析（利用可能な場合）
    local analysis=""
    if [ "$GEMINI_AVAILABLE" = "true" ]; then
        log_message "INFO" "Geminiでエラー分析中..."
        
        local gemini_prompt="以下のC#テストエラーを分析し、根本原因と修正案をJSON形式で返してください:

テストエラー:
$test_errors

JSON形式で回答:
{
  \"rootCause\": \"エラーの根本原因\",
  \"recommendation\": \"具体的な修正推奨事項\",
  \"priority\": \"high|medium|low\"
}"

        analysis=$(gemini cli "$gemini_prompt" 2>/dev/null || echo "Gemini分析に失敗しました")
        log_message "INFO" "Gemini分析完了"
    fi
    
    # Claude Codeで修正
    local fix_prompt="以下のテストエラーを修正してください:

テストエラー:
$test_errors

$([ -n "$analysis" ] && echo "Gemini分析結果:
$analysis")

修正要求:
- テスト失敗の原因を解決
- 実装とテストの不整合を修正
- Baketa Windows専用OCRアプリの要件を満たす修正

修正してください。"

    if [ "$MANUAL_MODE" = "true" ]; then
        echo ""
        echo "🧪 以下のテストエラーを修正してください:"
        echo "$test_errors"
        [ -n "$analysis" ] && echo -e "\n🤖 Gemini分析:\n$analysis"
        echo ""
        read -p "修正完了後、Enterを押してください..."
    else
        log_message "INFO" "Claude Codeでテストエラー修正中..."
        claude-code "$fix_prompt"
    fi
}

function run_development_cycle() {
    local feature_name="$1"
    local description="$2"
    
    if [ -z "$feature_name" ] || [ -z "$description" ]; then
        echo "使用法: $0 '機能名' '機能説明'"
        echo "例: $0 'OCR最適化' 'OpenCVフィルタによるテキスト検出精度向上'"
        exit 1
    fi
    
    log_message "INFO" "=== Baketa自動開発サイクル開始 ==="
    log_message "INFO" "機能名: $feature_name"
    log_message "INFO" "説明: $description"
    
    # 1. 前提条件確認
    check_prerequisites
    
    # 2. 実装実行
    execute_implementation "$feature_name" "$description"
    
    # 3. ビルド確認（リトライ付き）
    if ! execute_build_with_retry; then
        log_message "ERROR" "ビルドに失敗しました"
        exit 1
    fi
    
    # 4. テスト実行（分析・修正付き）  
    if ! execute_tests_with_analysis; then
        log_message "ERROR" "テストに失敗しました"
        exit 1
    fi
    
    log_message "INFO" "🎉 自動開発サイクル完了！"
    echo ""
    echo "✅ 実装・ビルド・テストが全て成功しました"
    echo "📁 ログは $LOGS_DIR に保存されています"
}

function show_usage() {
    echo "Baketa自動開発スクリプト（WSL完結版）"
    echo ""
    echo "使用法:"
    echo "  $0 '機能名' '機能説明'           # 自動開発実行"
    echo "  $0 interactive                  # 対話モード"
    echo "  $0 check                        # 前提条件確認"
    echo "  $0 logs                         # ログ表示"
    echo ""
    echo "例:"
    echo "  $0 'OCR最適化' 'OpenCVによる精度向上'"
    echo "  $0 'UI改善' 'Avaloniaコントロールの追加'"
    echo ""
    echo "自動実行フロー:"
    echo "  Claude実装 → ビルド確認 → テスト実行 → エラー時自動修正"
}

function interactive_mode() {
    echo "🤖 Baketa対話開発モード（WSL完結版）"
    echo ""
    
    while true; do
        echo -n "💡 開発要求を入力してください (機能名: 説明): "
        read input
        
        if [ "$input" = "exit" ]; then
            echo "👋 対話モード終了"
            break
        fi
        
        if [[ "$input" =~ ^([^:]+):[[:space:]]*(.+) ]]; then
            feature_name=$(echo "${BASH_REMATCH[1]}" | xargs)  # trim whitespace
            description=$(echo "${BASH_REMATCH[2]}" | xargs)
            
            echo ""
            run_development_cycle "$feature_name" "$description"
            echo ""
        else
            echo "❌ 形式が正しくありません。'機能名: 説明' の形式で入力してください。"
        fi
    done
}

# メイン処理
case "${1:-help}" in
    "check")
        check_prerequisites
        ;;
    "logs")
        echo "📁 ログディレクトリ: $LOGS_DIR"
        ls -la "$LOGS_DIR/" 2>/dev/null || echo "ログファイルなし"
        ;;
    "interactive")
        interactive_mode
        ;;
    "help"|"--help"|"-h")
        show_usage
        ;;
    *)
        if [ $# -eq 2 ]; then
            run_development_cycle "$1" "$2"
        else
            show_usage
        fi
        ;;
esac