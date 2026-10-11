using System.Runtime.CompilerServices;

// 纯逻辑的决策函数保持 internal，仅对测试工程开放，避免扩大公共 API 面。
[assembly: InternalsVisibleTo("WithWindows.Core.Tests")]
