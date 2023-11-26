using EWallet.Test.Helper;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace EWallet.Test.Tests;

[TestClass]
public class OrderTypeTest : BaseControllerTest
{
    [TestMethod]
    public async Task Crud()
    {
        var orderType = await TestInit1.OrderTypesClient.CreateAsync(TestInit1.AppId, Guid.NewGuid().ToString());
        var orderTypes = await TestInit1.OrderTypesClient.GetOrderTypesAsync(TestInit1.AppId);

        Assert.IsNotNull(orderTypes.SingleOrDefault(x => x.OrderTypeId == orderType.OrderTypeId));
    }
}
