using Microsoft.Extensions.Options;

namespace EWallet.Settings;

public sealed class AppSettings
{
    public string AppName { get; init; } = default!;
    public string SystemAuthorizationCode { get; init; } = default!;
    public string? AzureLogConnectionString { get; init; }

    /// <summary>
    /// How stale a currency-exchange rate may be before the shop refuses to place an order. Rates are
    /// refreshed by the admin's hourly job; if that stalls, settling at the last known rate would misprice
    /// the order, so selling pauses instead. Defaults to one hour.
    /// </summary>
    public TimeSpan MaxExchangeRateAge { get; init; } = TimeSpan.FromHours(1);

    public void Validate() {
        // Configure Settings
        const string msg = "The App in AppSettings must be initialized properly. OptionName: {0}";

        var propertyName = nameof(AppName);
        if (AppName == null)
            throw new OptionsValidationException(propertyName, typeof(string), [string.Format(msg, propertyName)]);

        propertyName = nameof(SystemAuthorizationCode);
        if (SystemAuthorizationCode == null)
            throw new OptionsValidationException(propertyName, typeof(string), [string.Format(msg, propertyName)]);

        propertyName = nameof(AzureLogConnectionString);
        if (AzureLogConnectionString == null)
            throw new OptionsValidationException(propertyName, typeof(string), [string.Format(msg, propertyName)]);
    }
}