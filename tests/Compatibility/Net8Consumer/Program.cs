using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddOptions<CompatibilityOptions>()
    .BindConfiguration("Compatibilty");

public sealed class CompatibilityOptions
{
    public string Value { get; set; } = "";
}
