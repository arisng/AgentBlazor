using AgentBlazor.App;
using AgentBlazor.Attributes;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// A simple capability demonstrating standalone agent actions that do not
/// belong to a workflow. Registered via <c>AddCapability&lt;T&gt;()</c> in
/// Program.cs so the actions are discoverable by the runtime and available
/// to dynamically-built agents through the Agent Builder tool picker.
/// </summary>
[AgentCapability("demo_util", Name = "Demo Utilities",
    Description = "Lightweight utility actions (glossary lookup, time, weather, currency conversion) available to any agent.",
    Category = "Utility")]
public sealed class DemoAssemblyCapabilities
{
    [AgentAction("Get the current weather for a given city. Returns a short summary.",
        ActionId = "get_weather")]
    public Task<CapabilityResult> GetWeatherAsync(
        [AgentParam("The city name to look up weather for.")] string city)
    {
        var forecasts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Seattle"] = "72°F, partly cloudy with a light breeze.",
            ["New York"] = "68°F, sunny with low humidity.",
            ["London"] = "61°F, overcast with a chance of rain.",
            ["Tokyo"] = "79°F, warm and humid.",
        };

        var text = forecasts.TryGetValue(city, out var forecast)
            ? $"Weather in {city}: {forecast}"
            : $"Weather in {city}: 70°F, clear skies (simulated).";

        return Task.FromResult(CapabilityResult.Success(text));
    }

    [AgentAction("Convert an amount from one currency to another at simulated rates.",
        ActionId = "convert_currency")]
    public Task<CapabilityResult> ConvertCurrencyAsync(
        [AgentParam("The amount to convert.")] double amount,
        [AgentParam("Source currency code (e.g. USD).")] string from,
        [AgentParam("Target currency code (e.g. EUR).")] string to)
    {
        var rates = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1.0,
            ["EUR"] = 0.92,
            ["GBP"] = 0.79,
            ["JPY"] = 149.5,
        };

        if (!rates.TryGetValue(from, out var fromRate) || !rates.TryGetValue(to, out var toRate))
        {
            return Task.FromResult(
                CapabilityResult.InvalidArguments($"Unsupported currency. Supported: {string.Join(", ", rates.Keys)}.")
                    .WithOutput("supportedCurrencies", string.Join(", ", rates.Keys)));
        }

        var result = amount / fromRate * toRate;
        var text = $"{amount:N2} {from.ToUpperInvariant()} = {result:N2} {to.ToUpperInvariant()} (simulated rate).";
        return Task.FromResult(CapabilityResult.Success(text));
    }
}
