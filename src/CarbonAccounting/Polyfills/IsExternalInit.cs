#if NETSTANDARD2_0

// Hand-written rather than pulled from a polyfill package: the library's headline
// property is that it adds nothing to a consumer's dependency graph.

using System.ComponentModel;

namespace System.Runtime.CompilerServices;

/// <summary>
/// Reserved for the compiler to support <c>init</c> accessors on frameworks that
/// do not ship this type.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
internal static class IsExternalInit
{
}

#endif
