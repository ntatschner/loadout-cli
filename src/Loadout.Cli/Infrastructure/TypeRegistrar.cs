using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Cli;

namespace Loadout.Cli.Infrastructure;

/// <summary>
/// Bridges Spectre's command resolution to the standard dependency injection
/// container, so commands take their services as constructor parameters like
/// everything else in the launcher.
/// </summary>
public sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly IServiceCollection _services;

    public TypeRegistrar(IServiceCollection services) => _services = services;

    /// <inheritdoc />
    public ITypeResolver Build() => new TypeResolver(_services.BuildServiceProvider());

    /// <inheritdoc />
    public void Register(Type service, Type implementation) =>
        _services.AddSingleton(service, implementation);

    /// <inheritdoc />
    public void RegisterInstance(Type service, object implementation) =>
        _services.AddSingleton(service, implementation);

    /// <inheritdoc />
    public void RegisterLazy(Type service, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        _services.AddSingleton(service, _ => factory());
    }
}

/// <inheritdoc cref="ITypeResolver" />
public sealed class TypeResolver : ITypeResolver, IDisposable
{
    private readonly ServiceProvider _provider;

    public TypeResolver(ServiceProvider provider) => _provider = provider;

    /// <inheritdoc />
    public object? Resolve(Type? type) => type is null ? null : _provider.GetService(type);

    /// <inheritdoc />
    public void Dispose() => _provider.Dispose();
}
