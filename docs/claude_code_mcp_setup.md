# Claude Code MCP (Model Context Protocol) 設定ガイド

## 現在利用可能なMCP機能

Claude Codeでは以下のMCP機能が利用できています：

### **1. ファイルシステムアクセス**
- ファイル読み取り・書き込み
- ディレクトリ操作
- ファイル検索・一覧取得

### **2. GitHub統合**
- リポジトリ操作
- Issue・Pull Request管理
- ファイル作成・更新

### **3. メモリ管理**
- ナレッジグラフ作成
- エンティティ関係管理
- 長期記憶機能

### **4. Web検索・取得**
- Brave Search API
- ローカル検索
- Webページ内容取得

### **5. データベースアクセス**
- PostgreSQL クエリ実行
- データ分析・操作

### **6. ブラウザ自動化**
- Puppeteer統合
- ページナビゲーション
- スクリーンショット取得

### **7. 分析ツール**
- JavaScript REPL
- データ分析・可視化
- 計算処理

## MCPサーバー設定の確認方法

### **Windows環境での設定確認**
```powershell
# Claude Desktop設定ファイルの場所
$configPath = "$env:APPDATA\Claude\claude_desktop_config.json"
if (Test-Path $configPath) {
    Get-Content $configPath | ConvertFrom-Json | ConvertTo-Json -Depth 10
} else {
    Write-Host "Claude Desktop設定ファイルが見つかりません: $configPath"
}
```

### **設定ファイル例**
```json
{
  "mcpServers": {
    "filesystem": {
      "command": "npx",
      "args": ["@modelcontextprotocol/server-filesystem"],
      "env": {
        "ALLOWED_DIRECTORIES": ["E:\\dev\\Baketa"]
      }
    },
    "github": {
      "command": "npx", 
      "args": ["@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "your_token_here"
      }
    },
    "postgres": {
      "command": "npx",
      "args": ["@modelcontextprotocol/server-postgres"],
      "env": {
        "POSTGRES_CONNECTION_STRING": "postgresql://user:password@localhost/dbname"
      }
    }
  }
}
```

## Baketa プロジェクト用 MCP最適化設定

### **推奨MCP設定**
```json
{
  "mcpServers": {
    "baketa_filesystem": {
      "command": "npx",
      "args": ["@modelcontextprotocol/server-filesystem"],
      "env": {
        "ALLOWED_DIRECTORIES": [
          "E:\\dev\\Baketa",
          "E:\\dev\\Baketa\\docs",
          "E:\\dev\\Baketa\\scripts"
        ]
      }
    },
    "baketa_github": {
      "command": "npx",
      "args": ["@modelcontextprotocol/server-github"],
      "env": {
        "GITHUB_PERSONAL_ACCESS_TOKEN": "your_token",
        "REPOSITORY": "your_username/Baketa"
      }
    },
    "web_research": {
      "command": "npx",
      "args": ["@modelcontextprotocol/server-brave-search"],
      "env": {
        "BRAVE_API_KEY": "your_brave_api_key"
      }
    }
  }
}
```

## モデル選択の指針

### **Claude Sonnet 4（推奨・デフォルト）**
```bash
# バランスの取れた高性能モデル
claude --model claude-sonnet-4-20250514 "複雑なリファクタリング作業"
```

**最適な用途**:
- 通常のコード実装・修正
- アーキテクチャ設計
- 複雑な問題解決
- ドキュメント作成

### **Claude Opus 4（最高精度）**
```bash
# 最高精度が必要な場合
claude --model claude-opus-4 "複雑なアルゴリズム設計"
```

**最適な用途**:
- 複雑なアルゴリズム実装
- 重要なアーキテクチャ決定
- 高精度が要求される分析
- 大規模リファクタリング

### **Claude Haiku（高速・軽量）**
```bash  
# 簡単な作業や高速応答が必要な場合
claude --model claude-haiku "簡単なバグ修正"
```

**最適な用途**:
- 簡単なバグ修正
- コード整形
- 軽微な変更
- 高速な応答が必要な場合

## 設定確認・トラブルシューティング

### **現在のMCP状態確認**
```bash
# Claude Codeでの確認
claude "現在利用可能なMCP機能を教えてください"
```

### **設定ファイル確認**
```powershell
# Windows
Get-Content "$env:APPDATA\Claude\claude_desktop_config.json"

# 設定ディレクトリ確認
ls "$env:APPDATA\Claude\"
```

### **よくある問題**

#### **1. ファイルアクセス制限**
```json
// 解決策: ALLOWED_DIRECTORIES に適切なパスを追加
"env": {
  "ALLOWED_DIRECTORIES": ["E:\\dev\\Baketa"]
}
```

#### **2. GitHub API制限**
```json
// 解決策: 適切なPersonal Access Tokenを設定
"env": {
  "GITHUB_PERSONAL_ACCESS_TOKEN": "ghp_your_token_here"
}
```

#### **3. モデル指定エラー**
```bash
# 解決策: 正確なモデル名を使用
claude --model claude-sonnet-4-20250514 "指示内容"
```

## パフォーマンス最適化

### **Baketa開発に最適化された設定**

#### **ファイルシステム最適化**
```json
{
  "env": {
    "ALLOWED_DIRECTORIES": ["E:\\dev\\Baketa"],
    "IGNORE_PATTERNS": ["bin/**", "obj/**", "Models/**", "*.onnx"]
  }
}
```

#### **GitHub統合最適化**
```json
{
  "env": {
    "REPOSITORY": "your_username/Baketa",
    "DEFAULT_BRANCH": "main",
    "ISSUE_LABELS": ["bug", "enhancement", "architecture"]
  }
}
```

この設定により、Claude CodeがBaketaプロジェクトで最大限の効果を発揮できます。