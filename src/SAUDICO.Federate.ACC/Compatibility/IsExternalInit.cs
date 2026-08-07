// Required for C# init-only properties when targeting .NET Framework 4.8 (Revit 2024).
#if NET48
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit
    {
    }
}
#endif
