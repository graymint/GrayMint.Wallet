using EWallet.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EWallet.Test.Tests;

[TestClass]
public class CurrencyTest : BaseControllerTest
{
    [TestMethod]
    public async Task Crud()
    {
        // Act
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);
        var currencies = await TestInit1.CurrenciesClient.GetCurrenciesAsync(TestInit1.AppId);

        // Assert
        Assert.IsNotNull(currencies.SingleOrDefault(x => x == currencyId));
    }

    [TestMethod]
    public async Task SystemWallet_must_has_max_min_balance_when_currency_is_created()
    {
        // Act
        var currencyId = await TestInit1.CurrenciesClient.CreateAsync(TestInit1.AppId);

        var minBalances = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, TestInit1.SystemWalletId);

        // Assert
        ArgumentNullException.ThrowIfNull(minBalances.Currencies);
        Assert.IsNotNull(minBalances.Currencies.SingleOrDefault(x => x.CurrencyId == currencyId &&
                                                                     x is { MinBalance: -long.MaxValue, Balance: 0 }));
    }
}
