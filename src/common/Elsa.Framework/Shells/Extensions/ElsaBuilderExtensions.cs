using Elsa.Framework.Builders;

// ReSharper disable once CheckNamespace
namespace Elsa.Extensions;

public static class ElsaBuilderExtensions
{
    public static ShellBuilder AddShell(this ElsaBuilder elsaBuilder)
    {
        return elsaBuilder.AddShell(null, []);
    }
    
    public static ShellBuilder AddShell(this ElsaBuilder elsaBuilder, string id)
    {
        return elsaBuilder.AddShell(id, []);
    }
    
    public static ShellBuilder AddShell(this ElsaBuilder elsaBuilder, params Type[] features)
    {
        return elsaBuilder.AddShell(null, features);
    }
    
    public static ShellBuilder AddShell(this ElsaBuilder elsaBuilder, string? id, params Type[] features)
    {
        var shellBuilder = new ShellBuilder(elsaBuilder);
        if (id != null) shellBuilder.WithId(id);
        shellBuilder.AddFeatures(features);
        elsaBuilder.AddConfigurator(shellBuilder);
        return shellBuilder;
    }
}