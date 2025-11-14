// C# 12 Global Using Directives for Baketa.UI
// プロジェクト全体で使用される基本的な名前空間と型エイリアス

// .NET基本型
global using System;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.Linq;
// Reactive Extensions
global using System.Reactive.Linq;
global using System.Reactive.Subjects;
global using System.Threading;
global using System.Threading.Tasks;
// Avalonia (基本のみ、具体的なコントロールは個別にusing)
global using Avalonia;
global using Avalonia.Controls;
// Microsoft Extensions
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
// ReactiveUI
global using ReactiveUI;
// Avalonia型エイリアス
global using AvaloniaPoint = Avalonia.Point;
global using AvaloniaRect = Avalonia.Rect;
global using AvaloniaSize = Avalonia.Size;
// Baketa Core型エイリアス（名前空間衝突回避）
global using CorePoint = Baketa.Core.UI.Geometry.Point;
global using CoreRect = Baketa.Core.UI.Geometry.Rect;
global using CoreSize = Baketa.Core.UI.Geometry.Size;
// System.Drawing型とAvaloniaの型の競合を回避
// 必要に応じて明示的にSystemDrawingPoint等の名前で参照可能
global using SystemDrawingPoint = System.Drawing.Point;
global using SystemDrawingRectangle = System.Drawing.Rectangle;
global using SystemDrawingSize = System.Drawing.Size;
