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
}
