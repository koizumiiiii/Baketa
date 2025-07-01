#!/bin/bash
# baketa-bridge.sh - WSLからWindows PowerShellへのブリッジ (キューイング方式)

# 動的パス設定
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WINDOWS_PROJECT_ROOT="$(dirname "$(dirname "$SCRIPT_DIR")")"
BRIDGE_DIR="$WINDOWS_PROJECT_ROOT/claude-gemini/bridge"
REQUESTS_QUEUE_DIR="$BRIDGE_DIR/requests"
PROCESSED_DIR="$BRIDGE_DIR/processed"

# ディレクトリ作成
mkdir -p "$BRIDGE_DIR" "$REQUESTS_QUEUE_DIR" "$PROCESSED_DIR"

function send_development_request() {
    local feature_name="$1"
    local description="$2"

    if [ -z "$feature_name" ] || [ -z "$description" ]; then
        echo "使用法: send_development_request '機能名' '説明'"
        return 1
    fi

    echo "🔄 Claude Code (WSL) → Windows PowerShell ブリッジ (キューイング方式)"
    echo "   機能名: $feature_name"
    echo "   説明: $description"

    # 一意なファイル名生成
    local timestamp=$(date +%s%N)
    local request_file="$REQUESTS_QUEUE_DIR/request_${timestamp}.json"

    # 要求JSON作成
    cat > "$request_file" << EOF
{
    "id": "$timestamp",
    "featureName": "$feature_name",
    "description": "$description",
    "status": "pending",
    "source": "wsl",
    "createdAt": "$(date -Iseconds)"
}
EOF

    echo "✅ 開発要求をキューに追加しました"
    echo "   ファイル: $request_file"
    echo ""
    echo "💡 Windows側で監視が必要:"
    echo "   cd $WINDOWS_PROJECT_ROOT\\claude-gemini\\scripts"
    echo "   .\\baketa-watcher.ps1 -WatchMode file"
}

function start_interactive_mode() {
    echo "🤖 Baketa WSL対話開発モード (キューイング方式)"
    echo ""
    echo "⚠️  事前にWindows側で監視開始:"
    echo "   cd ${WINDOWS_PROJECT_ROOT}\\claude-gemini\\scripts"
    echo "   .\\baketa-watcher.ps1 -WatchMode file"
    echo ""

    while true; do
        echo -n "💡 開発要求を入力してください (機能名: 説明): "
        read input

        if [ "$input" = "exit" ]; then
            echo "👋 対話モード終了"
            break
        fi

        if [[ "$input" =~ ^([^:]+):[[:space:]]*(.+) ]]; then
            feature_name="${BASH_REMATCH[1]// /}"
            description="${BASH_REMATCH[2]}"

            send_development_request "$feature_name" "$description"
        else
            echo "❌ 形式が正しくありません。'機能名: 説明' の形式で入力してください。"
        fi
    done
}

function show_queue_status() {
    echo "📊 開発要求キューの状態"
    echo ""
    echo "待機中の要求:"
    if [ -d "$REQUESTS_QUEUE_DIR" ] && [ "$(ls -A "$REQUESTS_QUEUE_DIR" 2>/dev/null)" ]; then
        ls -la "$REQUESTS_QUEUE_DIR"/*.json 2>/dev/null | while read line; do
            echo "  $line"
        done
    else
        echo "  なし"
    fi

    echo ""
    echo "処理済みの要求 (最新5件):"
    if [ -d "$PROCESSED_DIR" ] && [ "$(ls -A "$PROCESSED_DIR" 2>/dev/null)" ]; then
        ls -lt "$PROCESSED_DIR"/*.json 2>/dev/null | head -5 | while read line; do
            echo "  $line"
        done
    else
        echo "  なし"
    fi
}

function check_windows_watcher() {
    local watcher_pid_file="$BRIDGE_DIR/watcher.pid"

    if [ -f "$watcher_pid_file" ]; then
        echo "✅ Windows監視プロセス稼働中 (PID: $(cat "$watcher_pid_file"))"
        return 0
    else
        echo "⚠️  Windows監視プロセスが見つかりません"
        echo ""
        echo "Windows PowerShellで以下を実行してください:"
        echo "   cd ${WINDOWS_PROJECT_ROOT}\\claude-gemini\\scripts"
        echo "   .\\baketa-watcher.ps1 -WatchMode file"
        echo ""
        return 1
    fi
}

case "${1:-help}" in
    "interactive")
        start_interactive_mode
        ;;
    "add")
        if [ $# -eq 3 ]; then
            send_development_request "$2" "$3"
        else
            echo "使用法: $0 add '機能名' '説明'"
        fi
        ;;
    "status")
        show_queue_status
        check_windows_watcher
        ;;
    "help"|*)
        echo "Baketa WSL-Windows開発ブリッジ (キューイング方式)"
        echo ""
        echo "使用法:"
        echo "  $0 interactive              # 対話モード開始"
        echo "  $0 add '機能名' '説明'        # 開発要求追加"
        echo "  $0 status                   # キューの状態確認"
        echo ""
        echo "例:"
        echo "  $0 add 'OCR最適化' 'OpenCVによる精度向上'"
        echo "  $0 interactive"
        echo "  $0 status"
        ;;
esac