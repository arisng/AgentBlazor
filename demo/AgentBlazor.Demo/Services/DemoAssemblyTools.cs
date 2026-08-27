using AgentBlazor.Attributes;

namespace AgentBlazor.Demo.Services;

/// <summary>
/// Assembly-scoped tools discoverable via <c>WithToolsFromAssembly</c>.
/// These are registered on a specific agent and demonstrate the
/// attribute-based tool discovery pattern.
/// </summary>
public static class DemoAssemblyTools
{
    [AgentAction("Get the current weather for a given city. Returns a short summary.", ActionId = "get_weather")]
    public static string GetWeather(
        [AgentParam("The city name to look up weather for.")] string city)
    {
        var forecasts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Seattle"] = "72°F, partly cloudy with a light breeze.",
            ["New York"] = "68°F, sunny with low humidity.",
            ["London"] = "61°F, overcast with a chance of rain.",
            ["Tokyo"] = "79°F, warm and humid.",
        };

        return forecasts.TryGetValue(city, out var forecast)
            ? $"Weather in {city}: {forecast}"
            : $"Weather in {city}: 70°F, clear skies (simulated).";
    }

    [AgentAction("Convert an amount from one currency to another at simulated rates.", ActionId = "convert_currency")]
    public static string ConvertCurrency(
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
            return $"Unsupported currency. Supported: {string.Join(", ", rates.Keys)}.";
        }

        var result = amount / fromRate * toRate;
        return $"{amount:N2} {from.ToUpperInvariant()} = {result:N2} {to.ToUpperInvariant()} (simulated rate).";
    }
}
