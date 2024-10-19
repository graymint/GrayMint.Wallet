using System.Net;
using EWallet.Api;
using EWallet.Test.Helper;
using GrayMint.Common.ApiClients;
using GrayMint.Common.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EWallet.Test.Tests;

[TestClass]
public class WalletTest : BaseControllerTest
{
    [TestMethod]
    public async Task Crud()
    {
        // Act
        var walletCreated = await TestInit1.WalletsClient.CreateWalletAsync(TestInit1.AppId);

        // Get Wallet
        var wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletCreated.WalletId);

        // Assert
        Assert.AreEqual(walletCreated.WalletId, wallet.WalletId);
        ArgumentNullException.ThrowIfNull(wallet.Currencies);
        Assert.AreEqual(0, wallet.Currencies.Count);
    }

    [TestMethod]
    public Task Fail_GetWallet_With_AppId_That_Does_Not_Exists()
    {
        return TestUtil.AssertApiException(HttpStatusCode.Forbidden, TestInit1.WalletsClient.GetWalletAsync(0, 1));
    }

    [TestMethod]
    public async Task Fail_GetWallet_With_WalletId_That_Does_Not_Exists_For_App()
    {
        try
        {
            // Act
            await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, 0);
            Assert.Fail("Sequence contains no element error expected");
        }
        catch (ApiException ex) when (ex.Message.Contains("Sequence contains no element"))
        {
        }
    }

    [TestMethod]
    public async Task Success_MinBalance_when_does_not_set_yet()
    {
        // create dom
        var walletDom = await WalletDom.Create(TestInit1, null);

        // set min balance
        const int minBalance = -1000;
        var request = new SetMinBalanceRequest
        {
            CurrencyId = walletDom.CurrencyId,
            MinBalance = minBalance
        };
        await TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, walletDom.Wallet.WalletId, request);

        // Assert
        var wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom.Wallet.WalletId);
        ArgumentNullException.ThrowIfNull(wallet.Currencies);
        Assert.AreEqual(wallet.Currencies.Single(x => x.CurrencyId == walletDom.CurrencyId).MinBalance, minBalance);
    }

    [TestMethod]
    public async Task Success_MinBalance_when_is_set_before()
    {
        // create dom
        var walletDom = await WalletDom.Create(TestInit1);

        // check min balance initialized
        var wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom.Wallet.WalletId);

        // Assert
        ArgumentNullException.ThrowIfNull(wallet.Currencies);
        Assert.AreEqual(wallet.Currencies.Single(x => x.CurrencyId == walletDom.CurrencyId).MinBalance, -long.MaxValue);

        // set min balance
        const int minBalance = -1000;
        var request = new SetMinBalanceRequest
        {
            CurrencyId = walletDom.CurrencyId,
            MinBalance = minBalance
        };
        await TestInit1.WalletsClient.SetMinBalanceAsync(TestInit1.AppId, walletDom.Wallet.WalletId, request);

        // Assert
        wallet = await TestInit1.WalletsClient.GetWalletAsync(TestInit1.AppId, walletDom.Wallet.WalletId);
        ArgumentNullException.ThrowIfNull(wallet.Currencies);
        Assert.AreEqual(wallet.Currencies.Single(x => x.CurrencyId == walletDom.CurrencyId).MinBalance, minBalance);
    }
}