// .NET Framework 4.8 兼容补丁：补齐 C# 现代语法所需的运行时特性类型。
// net5+ 已内置这些类型；net481 需要手写，仅编译期使用，无运行时语义。

namespace System.Runtime.CompilerServices
{
    /// <summary>init-only setter 所需标记。</summary>
    internal static class IsExternalInit
    {
    }

    /// <summary>required 成员所需标记。</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    internal sealed class RequiredMemberAttribute : Attribute
    {
    }

    /// <summary>required 成员构造函数标记。</summary>
    [AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
    internal sealed class SetsRequiredMembersAttribute : Attribute
    {
    }

    /// <summary>编译器特性所需标记（required 等新语法引用）。</summary>
    [AttributeUsage(AttributeTargets.All, AllowMultiple = true, Inherited = false)]
    internal sealed class CompilerFeatureRequiredAttribute : Attribute
    {
        public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;

        public string FeatureName { get; }

        public bool IsOptional { get; init; }
    }
}
