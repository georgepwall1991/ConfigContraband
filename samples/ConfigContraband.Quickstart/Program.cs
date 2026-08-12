using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Green path: section exists, required key is present, DataAnnotations + ValidateOnStart are wired.
services.AddOptions<StripeOptions>()
    .BindConfiguration("Stripe")
    .ValidateDataAnnotations()
    .ValidateOnStart();

_ = services;

public sealed class StripeOptions
{
    [Required]
    public string ApiKey { get; set; } = "";

    public string WebhookSecret { get; set; } = "";
}
