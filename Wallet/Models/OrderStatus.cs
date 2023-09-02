using System.Text.Json.Serialization;
namespace EWallet.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrderStatus : byte
{
    Authorized = 1,
    Captured = 2,
    Voided = 3
}