#if NET48
namespace System.Runtime.CompilerServices;

internal static class IsExternalInit
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
internal sealed class RequiredMemberAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Constructor | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
internal sealed class CompilerFeatureRequiredAttribute : Attribute
{
    public CompilerFeatureRequiredAttribute(string featureName) => FeatureName = featureName;
    public string FeatureName { get; }
    public bool IsOptional { get; set; }
}
#endif
