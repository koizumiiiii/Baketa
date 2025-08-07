# Task Completion Procedures

## Mandatory Post-Implementation Process

### Required Steps After Any Code Implementation
All code implementations **MUST** follow this mandatory verification process:

#### 1. Build Verification (必須)
```bash
cd "E:\dev\Baketa"
dotnet build Baketa.sln --configuration Debug
```
**Decision Points:**
- **If BUILD SUCCEEDS**: Proceed to step 2
- **If BUILD FAILS**: Fix all compilation errors immediately before proceeding

#### 2. Error Resolution (エラー時必須)
When build fails, address these areas:
- **Compilation Errors**: Must be resolved completely
- **Warning Analysis**: Critical warnings must be addressed
- **Dependency Issues**: Ensure all NuGet packages and references are correct

#### 3. Gemini Code Review (ビルド成功後必須)
Once build succeeds with no errors, **MANDATORY** code review using gemini command:

```bash
gemini -p "実装完了しました。以下のコードについてレビューをお願いします。

## 実装内容
[実装した機能の概要]

## 変更ファイル
[変更されたファイルのリスト]

## 期待効果
[実装により期待される効果]

技術的な観点から問題点、改善点、潜在的なリスクについてレビューしてください。"
```

#### 4. Review Response Integration
- **Gemini指摘事項**: 重大な問題は即座に修正
- **改善提案**: 必要に応じて追加実装を検討
- **ベストプラクティス**: 将来の実装に反映

## Pre-Implementation Required Procedures

### Think Mode Implementation Approach
- **Basic Stance**: All implementations must demonstrate thought process in Think Mode
- **Mandatory Pre-Implementation Analysis**: Always execute the following 2 steps

#### 1. Root Cause Analysis
- **Problem Essence Identification**: Identify true causes rather than superficial symptoms
- **Architecture Impact**: Verify consistency with current architecture
- **Design Pattern Compliance**: Validate consistency with existing design patterns
- **Technical Debt Assessment**: Evaluate whether modifications increase or decrease technical debt

#### 2. Impact Analysis
- **Dependency Verification**: Identify other files that depend on modification targets
- **Interface Change Impact**: Assess impact on public APIs and internal interfaces
- **Test Impact Scope**: Identify test files that will be affected
- **Build/Compilation Impact**: Predict impact of modifications on build process
- **Performance Impact**: Evaluate impact on runtime performance

#### 3. Implementation Strategy Development
- **Phased Implementation Plan**: Divide large changes into safe phases
- **Risk Mitigation Measures**: Prepare for anticipated risks and countermeasures
- **Verification Methods**: Pre-define verification procedures after implementation

### Implementation Procedure Template
```
## Think Mode Analysis

### 1. Root Cause Analysis
- Problem Essence: 
- Architecture Impact: 
- Technical Debt Assessment: 

### 2. Impact Analysis  
- Dependencies: 
- Interface Changes: 
- Test Impact: 
- Build Impact: 

### 3. Implementation Strategy
- Implementation Steps: 
- Risk Mitigation: 
- Verification Methods: 
```

## Testing and Quality Assurance

### Testing Requirements
- **Unit Tests**: Run relevant unit tests after implementation
- **Integration Tests**: Execute integration tests for cross-component changes
- **Performance Tests**: Run performance benchmarks if performance-related changes

### Quality Verification Commands
```bash
# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter "ClassName~[YourTestClass]"

# Performance verification
powershell -Command "cd 'E:\dev\Baketa'; python scripts\current_bottleneck_analysis.py"
```

## Git Commit Standards

### Commit Message Format
```
feat: [Japanese description of feature]

[Detailed description in Japanese]

🤖 Generated with Claude Code

Co-Authored-By: Claude <noreply@anthropic.com>
```

### Pre-Commit Checklist
1. ✅ Build verification completed
2. ✅ Tests passing
3. ✅ Gemini code review completed
4. ✅ Critical issues addressed
5. ✅ Commit message follows format

## Process Enforcement

### No Exceptions Policy
- **Mandatory Process**: この手順はすべての実装に適用
- **Documentation**: 大きな変更の場合は適切なドキュメント更新も実施
- **Quality Assurance**: コードレビューは品質保証の必須プロセス

### Escalation Process
If any step in the mandatory process fails:
1. **Build Failures**: Fix immediately, do not proceed
2. **Test Failures**: Investigate and fix root cause
3. **Review Issues**: Address Gemini feedback before final commit

## Special Considerations

### Native DLL Changes
When modifying C++/WinRT components:
1. **Build Native First**: Always build BaketaCaptureNative.sln before .NET solution
2. **DLL Copy**: Ensure DLL is copied to output directory
3. **P/Invoke Testing**: Verify P/Invoke declarations work correctly

### Performance-Critical Changes
For OCR, translation, or UI performance changes:
1. **Baseline Measurement**: Record performance before changes
2. **Post-Implementation Measurement**: Verify performance improvements
3. **Regression Testing**: Ensure no performance regressions in other areas

### UI Changes
For Avalonia UI modifications:
1. **Visual Testing**: Manually verify UI appearance and behavior
2. **Accessibility**: Ensure accessibility features remain functional
3. **Responsiveness**: Test UI responsiveness across different scenarios