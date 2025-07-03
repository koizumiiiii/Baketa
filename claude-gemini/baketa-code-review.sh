#!/bin/bash
# baketa-code-review.sh - Baketa Geminiコードレビュー専用スクリプト

set -e

# 設定
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
REVIEW_DIR="$SCRIPT_DIR/reviews"
LOGS_DIR="$SCRIPT_DIR/logs"

# レビュー設定
REVIEW_TYPES=("architecture" "performance" "security" "style" "documentation" "testing" "full")
DEFAULT_OUTPUT_FORMAT="markdown"

# ディレクトリ作成
mkdir -p "$REVIEW_DIR" "$LOGS_DIR"

function log_message() {
    local level="$1"
    local message="$2"
    local timestamp=$(date '+%Y-%m-%d %H:%M:%S')
    echo "[$timestamp] [$level] $message" | tee -a "$LOGS_DIR/code-review.log"
}

function check_prerequisites() {
    log_message "INFO" "Geminiコードレビュー前提条件確認"
    
    if ! command -v gemini >/dev/null 2>&1; then
        log_message "ERROR" "Gemini CLIが見つかりません"
        echo ""
        echo "🔧 Gemini CLIをインストールしてください"
        exit 1
    fi
    
    if [ ! -d "$PROJECT_ROOT" ]; then
        log_message "ERROR" "プロジェクトディレクトリが見つかりません: $PROJECT_ROOT"
        exit 1
    fi
    
    log_message "INFO" "前提条件確認完了"
}

function get_project_structure() {
    log_message "INFO" "プロジェクト構造分析中..."
    
    cd "$PROJECT_ROOT"
    
    # C#プロジェクトファイルを検索
    local csproj_files=$(find . -name "*.csproj" -not -path "*/bin/*" -not -path "*/obj/*")
    local cs_files=$(find . -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" | head -20)
    
    echo "## プロジェクト構造

### C#プロジェクト:
$csproj_files

### 主要C#ファイル (サンプル):
$cs_files"
}

function analyze_codebase() {
    local target_path="$1"
    local focus_areas="$2"
    
    log_message "INFO" "コードベース分析開始: $target_path"
    
    cd "$PROJECT_ROOT"
    
    if [ ! -e "$target_path" ]; then
        log_message "ERROR" "指定されたパスが見つかりません: $target_path"
        return 1
    fi
    
    # ファイル/ディレクトリに応じた分析
    if [ -d "$target_path" ]; then
        analyze_directory "$target_path" "$focus_areas"
    elif [ -f "$target_path" ]; then
        analyze_file "$target_path" "$focus_areas"
    fi
}

function analyze_directory() {
    local dir_path="$1"
    local focus_areas="$2"
    
    log_message "INFO" "ディレクトリ分析: $dir_path"
    
    # ディレクトリ内のC#ファイルを収集
    local cs_files=$(find "$dir_path" -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*")
    
    if [ -z "$cs_files" ]; then
        log_message "WARN" "C#ファイルが見つかりません: $dir_path"
        return 1
    fi
    
    # 主要ファイルのコード内容を取得（長すぎる場合は制限）
    local code_content=""
    local file_count=0
    
    for file in $cs_files; do
        if [ $file_count -ge 10 ]; then  # 最大10ファイルまで
            code_content="$code_content\n\n[... 他 $(echo "$cs_files" | wc -l | xargs echo) - $file_count ファイル省略 ...]"
            break
        fi
        
        local file_size=$(wc -c < "$file")
        if [ $file_size -gt 5000 ]; then  # 5KB以上のファイルは先頭のみ
            code_content="$code_content\n\n## $file (先頭部分のみ):\n\`\`\`csharp\n$(head -50 "$file")\n[... 省略 ...]\n\`\`\`"
        else
            code_content="$code_content\n\n## $file:\n\`\`\`csharp\n$(cat "$file")\n\`\`\`"
        fi
        
        ((file_count++))
    done
    
    # Geminiでレビュー実行
    execute_gemini_review "$code_content" "$focus_areas" "directory:$dir_path"
}

function analyze_file() {
    local file_path="$1"
    local focus_areas="$2"
    
    log_message "INFO" "ファイル分析: $file_path"
    
    if [[ ! "$file_path" =~ \.(cs|csproj)$ ]]; then
        log_message "WARN" "C#ファイル以外は分析対象外: $file_path"
        return 1
    fi
    
    local code_content="## $file_path:\n\`\`\`csharp\n$(cat "$file_path")\n\`\`\`"
    
    # Geminiでレビュー実行
    execute_gemini_review "$code_content" "$focus_areas" "file:$file_path"
}

function execute_gemini_review() {
    local code_content="$1"
    local focus_areas="$2"
    local target_info="$3"
    
    log_message "INFO" "Geminiレビュー実行中..."
    
    # フォーカス領域に応じたプロンプト生成
    local review_prompt=$(generate_review_prompt "$focus_areas")
    
    local full_prompt="あなたはBaketa Windows専用OCRオーバーレイアプリケーションの経験豊富なC#開発者です。

## プロジェクト背景:
- Windows専用OCRオーバーレイアプリ
- クリーンアーキテクチャ (Core/Infrastructure/Application/UI)
- PaddleOCR + OpenCV画像処理
- Avalonia UI
- 非同期処理とリアルタイム性能が重要

## レビュー対象:
$target_info

## コード内容:
$code_content

## レビュー指示:
$review_prompt

## 出力形式:
以下のMarkdown形式で回答してください:

# コードレビュー結果

## 📊 総合評価
- **品質スコア**: X/10
- **主要な問題**: N個
- **改善提案**: N個

## 🔍 詳細分析

### ✅ 良い点
- 具体的な良い実装ポイント

### ⚠️ 改善が必要な点  
- 具体的な問題と修正提案

### 🚀 最適化提案
- パフォーマンス改善案
- 設計改善案

## 📝 具体的な修正例
\`\`\`csharp
// 修正前
[問題のあるコード]

// 修正後  
[改善されたコード]
\`\`\`

## 🎯 次のステップ
1. 優先度の高い修正項目
2. 長期的な改善計画"

    # Gemini実行
    local review_result
    if review_result=$(gemini cli "$full_prompt" 2>/dev/null); then
        log_message "INFO" "Geminiレビュー完了"
        
        # レビュー結果を保存
        save_review_result "$review_result" "$target_info" "$focus_areas"
        
        # 結果表示
        echo "$review_result"
    else
        log_message "ERROR" "Geminiレビューに失敗しました"
        return 1
    fi
}

function generate_review_prompt() {
    local focus_areas="$1"
    
    case "$focus_areas" in
        "architecture")
            echo "アーキテクチャとデザインパターンに重点を置いてレビューしてください:
- クリーンアーキテクチャの原則遵守
- 依存性注入の適切な使用
- 責任分離の実装
- インターフェース設計"
            ;;
        "performance")
            echo "パフォーマンスと効率性に重点を置いてレビューしてください:
- 非同期処理の最適化
- メモリ使用量の効率化
- OCR処理の最適化
- UI応答性の向上"
            ;;
        "security")
            echo "セキュリティに重点を置いてレビューしてください:
- 入力検証
- エラーハンドリング
- リソース管理
- 機密情報の取り扱い"
            ;;
        "style")
            echo "コーディングスタイルと規約に重点を置いてレビューしてください:
- C#命名規約
- コードの可読性
- コメントとドキュメント
- 一貫性"
            ;;
        "documentation")
            echo "ドキュメンテーションに重点を置いてレビューしてください:
- XMLドキュメントコメント
- コードの自己説明性
- README更新の必要性
- API仕様の明確性"
            ;;
        "testing")
            echo "テスタビリティとテストに重点を置いてレビューしてください:
- 単体テストの網羅性
- モックとスタブの使用
- テストの保守性
- テストデータの管理"
            ;;
        "full")
            echo "包括的なレビューを実行してください:
- アーキテクチャ
- パフォーマンス
- セキュリティ
- コーディングスタイル
- テスタビリティ
- ドキュメンテーション"
            ;;
        *)
            echo "一般的なコードレビューを実行してください"
            ;;
    esac
}

function save_review_result() {
    local review_result="$1"
    local target_info="$2"
    local focus_areas="$3"
    local timestamp=$(date '+%Y%m%d_%H%M%S')
    
    local sanitized_target=$(echo "$target_info" | tr '/' '_' | tr ':' '_')
    local output_file="$REVIEW_DIR/review_${sanitized_target}_${focus_areas}_${timestamp}.md"
    
    cat > "$output_file" << EOF
# Baketa コードレビュー結果

**レビュー対象**: $target_info  
**フォーカス**: $focus_areas  
**実行日時**: $(date)  
**レビューア**: Gemini CLI

---

$review_result

---

**生成ファイル**: $output_file
EOF

    log_message "INFO" "レビュー結果保存: $output_file"
    echo ""
    echo "📝 レビュー結果が保存されました: $output_file"
}

function list_recent_reviews() {
    echo "📋 最近のレビュー結果 (最新10件):"
    echo ""
    
    if [ -d "$REVIEW_DIR" ] && [ "$(ls -A "$REVIEW_DIR" 2>/dev/null)" ]; then
        ls -lt "$REVIEW_DIR"/*.md 2>/dev/null | head -10 | while read -r line; do
            echo "  $line"
        done
    else
        echo "  レビュー結果なし"
    fi
}

function interactive_mode() {
    echo "🤖 Baketa対話型コードレビューモード"
    echo ""
    
    while true; do
        echo "📋 レビューオプション:"
        echo "  1. ファイルレビュー"
        echo "  2. ディレクトリレビュー"  
        echo "  3. プロジェクト全体レビュー"
        echo "  4. 最近のレビュー表示"
        echo "  5. 終了"
        echo ""
        
        read -p "選択してください (1-5): " choice
        
        case $choice in
            1)
                read -p "📁 ファイルパスを入力: " file_path
                read -p "🎯 フォーカス領域 [${REVIEW_TYPES[*]}]: " focus
                [ -z "$focus" ] && focus="full"
                echo ""
                analyze_codebase "$file_path" "$focus"
                ;;
            2)
                read -p "📁 ディレクトリパスを入力: " dir_path
                read -p "🎯 フォーカス領域 [${REVIEW_TYPES[*]}]: " focus
                [ -z "$focus" ] && focus="full"
                echo ""
                analyze_codebase "$dir_path" "$focus"
                ;;
            3)
                read -p "🎯 フォーカス領域 [${REVIEW_TYPES[*]}]: " focus
                [ -z "$focus" ] && focus="architecture"
                echo ""
                analyze_codebase "." "$focus"
                ;;
            4)
                list_recent_reviews
                ;;
            5)
                echo "👋 コードレビューモード終了"
                break
                ;;
            *)
                echo "❌ 無効な選択です"
                ;;
        esac
        echo ""
    done
}

function show_usage() {
    echo "Baketa Geminiコードレビュー専用スクリプト"
    echo ""
    echo "使用法:"
    echo "  $0 --target PATH --focus TYPE     # 指定パスをレビュー"
    echo "  $0 --file PATH --focus TYPE       # ファイルをレビュー"
    echo "  $0 --dir PATH --focus TYPE        # ディレクトリをレビュー"
    echo "  $0 --interactive                  # 対話モード"
    echo "  $0 --list                         # 最近のレビュー表示"
    echo ""
    echo "フォーカス領域:"
    printf "  %s\n" "${REVIEW_TYPES[@]}"
    echo ""
    echo "例:"
    echo "  $0 --target Baketa.Core --focus architecture"
    echo "  $0 --file Baketa.Core/Services/OcrService.cs --focus performance"
    echo "  $0 --dir Baketa.UI --focus style"
    echo "  $0 --interactive"
}

# メイン処理
check_prerequisites

case "${1:-help}" in
    "--target")
        TARGET_PATH="$2"
        FOCUS="${4:-full}"
        if [ -z "$TARGET_PATH" ]; then
            echo "❌ ターゲットパスが指定されていません"
            show_usage
            exit 1
        fi
        analyze_codebase "$TARGET_PATH" "$FOCUS"
        ;;
    "--file")
        FILE_PATH="$2"
        FOCUS="${4:-full}"
        if [ -z "$FILE_PATH" ]; then
            echo "❌ ファイルパスが指定されていません"
            show_usage
            exit 1
        fi
        analyze_codebase "$FILE_PATH" "$FOCUS"
        ;;
    "--dir")
        DIR_PATH="$2"
        FOCUS="${4:-full}"
        if [ -z "$DIR_PATH" ]; then
            echo "❌ ディレクトリパスが指定されていません"
            show_usage
            exit 1
        fi
        analyze_codebase "$DIR_PATH" "$FOCUS"
        ;;
    "--interactive"|"-i")
        interactive_mode
        ;;
    "--list"|"-l")
        list_recent_reviews
        ;;
    "--help"|"-h"|"help"|*)
        show_usage
        ;;
esac