# GrayMint.Wallet

A double-entry ledger for .NET on PostgreSQL or SQL Server: wallets, transfers that are never changed, amounts
held as pending and later posted or voided, and limits that the database itself holds. Its tables live in a
schema of their own, `gmwallet`, in the host's database or in one of their own, and a call can run in the host's
own database transaction, so an order and its money are saved together or not at all.

## The accounting it follows

The wallet keeps its books by double-entry bookkeeping, for one kind of account: money a platform holds for an
owner.

- **Every amount is recorded twice.** A transfer takes an amount out of one wallet and puts the same amount into
  another of the same tenant and currency, so in each tenant and currency the balances add up to zero. Money enters
  a tenant and leaves it only through the tenant's root wallet, which stands for the world outside: its balance is
  minus everything the tenant's other wallets hold.
- **In and out, not debit and credit.** In accounting terms each wallet is a liability, money owed to its owner, so
  money in is a credit and money out a debit. As every wallet is that same kind of account, the wallet names its
  totals by direction, `PostedIn`, `PostedOut`, `PendingIn` and `PendingOut`, and a balance is in minus out.
- **A journal that is only added to.** A transfer, once recorded, is never changed or deleted. A mistake is put
  right by a transfer the other way, and a hold is settled by a second record, its post or its void, so the history
  always shows what happened and in what order.
- **Booked and available balances.** A hold, a pending transfer, lowers what a wallet may spend at once, its
  `AvailableBalance`; only its post changes the booked balance, `PostedBalance`. Money on its way in can't be spent
  before it is posted.
- **A running balance on every line.** Each transfer writes a line on each of its two wallets: the amount, below 0
  when the money went out, and the wallet's balances after it, written under the wallet's lock. A wallet's lines are
  its statement, and each balance follows from the one before.
- **Books reconciled against the journal.** Reconciliation holds each wallet's totals against what its transfers
  add up to, and its balances against its newest line, and adds up each tenant's wallets in each currency, which
  comes to zero, as in a trial balance. It runs only when called.

## What it holds

- **Tenants.** Every wallet and transfer belongs to one tenant, a partition of the host's such as one store of a
  platform, and money never leaves its tenant. A tenant's root wallet in each currency is where money enters the
  tenant and leaves it.
- **Limits.** A wallet has a `MinAvailableBalance`: 0 for no credit, below 0 for a credit limit, or none for no
  limit. Its `AvailableBalance`, what it may spend, is the money posted in minus the money posted and pending out;
  its `PostedBalance` counts what is posted only. Money pending in can't be spent before it is posted, not even by
  the next of the transfers linked to it. The service checks the limit, and a check of the database holds it too.
- **Linked transfers.** The transfers of one call are linked: made in order, each seeing the ones before it, all of
  them or none.
- **Pending.** A pending transfer holds its amount on the wallet it comes from until it is posted or voided.
- **Statements.** Each transfer makes a wallet transaction on each of its two wallets: the amount, below 0 when it
  went out, and the wallet's balances after it. A wallet's transactions (`WalletService.GetTransactions`) are its
  statement.
- **Retries.** Each transfer has an external reference key, the caller's own, unique in its tenant. The same call
  sent again with the same keys changes nothing and gives back what the first one made; a key used for another
  transfer is refused.
- **Concurrency.** A call locks its wallets, in id order, until its database transaction ends; calls on other
  wallets don't wait for it.
- **Reconciliation.** `Reconcile` reports every wallet whose totals are not what its transfers add up to, or whose
  balances are not the ones its newest wallet transaction left, and every currency of a tenant whose wallets don't
  add up to zero. Nothing calls it on its own.
- **Ids are text.** Every id a call takes or gives is text, `"42"`, so a host's code never changes with the
  database's keys.

## Projects

| Project | What it is |
| --- | --- |
| `src/GrayMint.Wallet` | The wallet, as a library: the `GrayMint.Wallet` package on nuget.org |
| `src/GrayMint.Wallet.Server` | A ready-to-run service over the same wallet, for callers that keep their orders elsewhere |
| `tests/GrayMint.Wallet.Test` | Tests that run every case on a real PostgreSQL and a real SQL Server |

## Use it in a host

The wallet has a context of its own. To run in the host's database transactions, it works on the host's
connection:

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

The host refuses to start on a database without the wallet's tables, or with tables of another version, or that
doesn't keep text as UTF-8. A PostgreSQL database does by default. On SQL Server, give the host's database a UTF-8
default collation, such as `Latin1_General_100_CI_AS_SC_UTF8` (`modelBuilder.UseCollation(...)` in the host's context
makes it so; `ALTER DATABASE ... COLLATE ...` changes one there already): SQL Server sends text in the database's own
code page, and a letter outside it would arrive as `?`.

The host injects the wallet's services where it needs them: `WalletService` for wallets,
`WalletTransferService` for transfers, and `WalletReconciliationService` for reconciliation. Move money
in the host's own database transaction:

```csharp
await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
dbContext.Orders.Add(order);
await dbContext.SaveChangesAsync(cancellationToken);
await transferService.Create(tenantId, [
    new WalletTransferCreateRequest {
        ExternalReferenceKey = $"order-{order.OrderId}/pay",
        FromWalletId = customerCredit.WalletId,
        ToWalletId = shopRevenue.WalletId,
        Amount = 12.50m
    }
], cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

The host's context opens the connection, so its connection interceptors run for the wallet's commands too. A call
the database refuses takes back what it wrote and leaves the host's database transaction usable (on SQL Server,
while `XACT_ABORT` is off, as it has it). The wallets a call locks stay locked until the host's database
transaction ends, so commit soon after the wallet's calls, and keep slow work, such as a call to a payment
gateway, out of the database transaction.

A wallet that needs no database transaction of the host's takes a connection of its own:

```csharp
builder.Services.AddGrayMintWallet();
builder.Services.AddGrayMintWalletDb(options => options.UseSqlServer(connectionString));
```

A tenant is a partition of the host's that money never leaves, such as one store of a platform; its id is a number
of the host's choosing, given as text, such as `"7"`. Money enters a tenant from its root wallet in the currency
(`WalletService.GetOrCreateRootWallet`, made at the first call) and leaves it to that wallet, so the root has no
limit and goes below zero by everything the tenant's other wallets hold:

```csharp
var root = await walletService.GetOrCreateRootWallet(tenantId, "USD", cancellationToken);
await transferService.Create(tenantId, [
    new WalletTransferCreateRequest {
        ExternalReferenceKey = $"payment-{payment.PaymentId}/top-up",
        FromWalletId = root.WalletId,
        ToWalletId = customerCredit.WalletId,
        Amount = 20m
    }
], cancellationToken);
```

A host that puts walls of its own on the wallet's rows, such as a row-level security policy by tenant, names the
tables from `WalletSchema`.

## The server

`GrayMint.Wallet.Server` runs the same wallet as a service of its own, for a caller that keeps its orders
elsewhere and calls it with one of the server's API keys. Such a caller saves its order and its money in two
places, so when it can't tell whether a request went through it sends the same request again, and the external
reference keys make that safe. It reconciles the books when its caller asks, at `GET /api/reconciliation`. Its API
describes itself at `/openapi/v1.json`, with a reference to read at `/scalar`.
How to run it is in [docs/deployment.md](https://github.com/graymint/GrayMint.Wallet/blob/main/docs/deployment.md),
and how the wallet is built in [docs/design.md](https://github.com/graymint/GrayMint.Wallet/blob/main/docs/design.md).

## Develop

You need the .NET 11 SDK and Docker.

```bash
docker compose up -d   # PostgreSQL 19 on 127.0.0.1:5436 and SQL Server 2025 on 127.0.0.1:14336
dotnet test
```

Each test runs once on each database, in a database of its own that it drops when it ends.
