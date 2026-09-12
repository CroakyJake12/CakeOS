using System.Reflection;
using System.Runtime.Loader;
using Haven.UI.Components;

namespace CakeOS.HuiLinuxHost;

/// <summary>
/// Supplies an application-owned, platform-neutral HUI root to the Linux host.
/// </summary>
public interface IHuiRootProvider
{
    Page CreateRoot();
}

public static class HuiRootProviderResolver
{
    public const string AssemblyOption = "--hui-root-provider-assembly";
    public const string TypeOption = "--hui-root-provider-type";

    public static IHuiRootProvider? Resolve(string[] args, out string[] hostArgs)
    {
        string? assemblyPath = null;
        string? typeName = null;
        var remaining = new List<string>();

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];
            if (arg is AssemblyOption or TypeOption)
            {
                if (++index == args.Length)
                    throw new ArgumentException($"{arg} requires a value.");

                if (arg == AssemblyOption)
                {
                    if (assemblyPath is not null)
                        throw new ArgumentException($"{AssemblyOption} may only be specified once.");
                    assemblyPath = args[index];
                }
                else
                {
                    if (typeName is not null)
                        throw new ArgumentException($"{TypeOption} may only be specified once.");
                    typeName = args[index];
                }
            }
            else
            {
                remaining.Add(arg);
            }
        }

        hostArgs = remaining.ToArray();
        if (assemblyPath is null && typeName is null)
            return null;
        if (assemblyPath is null || typeName is null)
            throw new ArgumentException($"{AssemblyOption} and {TypeOption} must be specified together.");
        if (!Path.IsPathFullyQualified(assemblyPath))
            throw new ArgumentException($"{AssemblyOption} must be an absolute path.");

        var assembly = new RootProviderLoadContext(Path.GetFullPath(assemblyPath))
            .LoadFromAssemblyPath(Path.GetFullPath(assemblyPath));
        var providerType = assembly.GetType(typeName, throwOnError: true, ignoreCase: false)
            ?? throw new InvalidOperationException($"Provider type '{typeName}' was not found.");
        if (!typeof(IHuiRootProvider).IsAssignableFrom(providerType))
            throw new ArgumentException($"Provider type '{typeName}' must implement {nameof(IHuiRootProvider)}.");

        return Activator.CreateInstance(providerType) as IHuiRootProvider
            ?? throw new InvalidOperationException($"Provider type '{typeName}' must have a public parameterless constructor.");
    }

    private sealed class RootProviderLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver _dependencies;

        public RootProviderLoadContext(string assemblyPath) => _dependencies = new AssemblyDependencyResolver(assemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            var hostAssembly = Default.Assemblies.FirstOrDefault(assembly =>
                AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), assemblyName));
            if (hostAssembly is not null)
                return hostAssembly;

            var dependencyPath = _dependencies.ResolveAssemblyToPath(assemblyName);
            return dependencyPath is null ? null : LoadFromAssemblyPath(dependencyPath);
        }
    }
}
