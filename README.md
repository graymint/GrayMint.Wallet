# GrayMint.Wallet

A double-entry ledger for .NET on PostgreSQL or SQL Server: accounts, transfers that are never changed, amounts
held as pending and later posted or voided, and limits that the database itself holds. Its tables live in a
schema of their own, `gmwallet`, in the host's database or in one of their own, and a call can run in the host's
own transaction, so an order and its money are saved together or not at all.

It follows the model of [TigerBeetle](https://tigerbeetle.com): every movement of money is a transfer from one
account to another, an account keeps its totals of what is posted and what is pending, and a request carries a
key of the caller's, so sending it again changes nothing.

## What it holds

- **Double entry.** A transfer debits one account and credits another of the same ledger and currency. An
  account's balance is its credits minus its debits.
- **Nothing is changed afterwards.** The database refuses to change or delete a transfer, for every role. A
  mistake is put right by a transfer the other way.
- **Limits.** An account has a `MinBalance`: 0 for no credit, below 0 for a credit limit, or none for no limit.
  What may be spent is the posted credits minus the posted and pending debits. A pending credit can't be spent
  before it is posted, not even by the next transfer of its own chain. The service checks the limit, and a check
  of the database holds it too.
- **Chains.** The transfers of one call are made in order, each seeing the ones before it, all of them or none.
- **Pending.** A pending transfer holds its amount on the debit account until it is posted or voided.
- **Retries.** Each transfer has a key, unique in its ledger. The same call sent again with the same keys
  changes nothing and gives back what the first one made; a key used for another transfer is refused.
- **Concurrency.** A call locks its accounts, in id order, until its transaction ends.
- **Reconciliation.** `FindTotalsMismatches` lists every account whose stored totals are not what its transfers
  add up to.

## Projects

| Project | What it is |
| --- | --- |
| `src/GrayMint.Wallet` | The ledger, as a library: the `GrayMint.Wallet` package on nuget.org |
| `src/GrayMint.Wallet.Server` | A ready-to-run service over the same ledger, for callers that keep their orders elsewhere |
| `tests/GrayMint.Wallet.Test` | Tests that run every case on a real PostgreSQL and a real SQL Server |

## Use it in a host

The wallet has a context of its own. To run in the host's transactions, it works on the host's connection:

```csharp
builder.Services.AddDbContext<ShopDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddGrayMintWallet();
builder.Services.AddGrayMintWalletDb<ShopDbContext>((connection, options) => options.UseNpgsql(connection));
```

On SQL Server, `UseSqlServer` takes the place of `UseNpgsql`. The host's setup makes the wallet's tables, with a
login that may make tables; it can run any number of times:

```csharp
await app.Services.UseGrayMintWallet(cancellationToken);
```

The host refuses to start on a database whose wallet tables, or their checks, are missing or switched off.

Move money in the host's own transaction:

```csharp
await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
dbContext.Orders.Add(order);
await dbContext.SaveChangesAsync(cancellationToken);
await walletService.CreateTransfers(ledgerId, [
    new WalletTransferRequest {
        Key = $"order-{order.OrderId}/pay",
        DebitAccountId = customerCredit.AccountId,
        CreditAccountId = shopRevenue.AccountId,
        Amount = 12.50m
    }
], cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The host's context opens the connection, so its connection interceptors run for the wallet's commands too. A call
the database refuses takes back what it wrote and leaves the host's transaction usable (on SQL Server, while
`XACT_ABORT` is off, as it has it).

A wallet that needs no host transaction takes a connection of its own:

```csharp
builder.Services.AddGrayMintWallet();
builder.Services.AddGrayMintWalletDb(options => options.UseSqlServer(connectionString));
```

A ledger is a book of accounts that money never leaves, such as one for each tenant of a host; its id is the
host's to give.

## The server

`GrayMint.Wallet.Server` runs the same ledger as a service of its own, for a caller that keeps its orders
elsewhere. Such a caller saves its order and its money in two places, so when it can't tell whether a request
went through it sends the same request again, and the keys make that safe. Its ids go out as text. How to run it
is in [docs/deployment.md](https://github.com/graymint/GrayMint.Wallet/blob/main/docs/deployment.md), and how the
ledger is built in [docs/design.md](https://github.com/graymint/GrayMint.Wallet/blob/main/docs/design.md).

## Develop

You need the .NET 10 SDK and Docker.

```bash
docker compose up -d   # PostgreSQL 18 on 127.0.0.1:5436 and SQL Server 2025 on 127.0.0.1:14336
dotnet test
```

Each test runs once on each database, in a database of its own that it drops when it ends.
