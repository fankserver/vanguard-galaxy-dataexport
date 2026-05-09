// netstandard2.1 polyfill so `record` types compile under modern Roslyn.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
