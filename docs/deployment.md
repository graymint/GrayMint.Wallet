# Deployment

For whoever runs the server, `GrayMint.Wallet.Server`, or a host that embeds the wallet.

## A host that embeds the wallet

- `AddGrayMintWallet()` registers the services (`WalletService`, `WalletTransferService`,
  `WalletReconciliationService`), and `AddGrayMintWalletDb` their context: on the host's connection, to run in the
  host's database transactions, or on a connection of its own (the README shows both). The host doesn't start on
  a database without the wallet's tables, with tables of another version, or that doesn't keep text as UTF-8.
- On SQL Server, the host's database needs a UTF-8 default collation, such as `Latin1_General_100_CI_AS_SC_UTF8`:
  SQL Server sends text in the database's own code page, and a letter outside it would arrive as `?`. Give it one
  when the database is made (`modelBuilder.UseCollation(...)` in the host's context), or change it with
  `ALTER DATABASE ... COLLATE Latin1_General_100_CI_AS_SC_UTF8`, which leaves the host's own columns as they are. A
  PostgreSQL database keeps text as UTF-8 by default.
- The host's setup runs `UseGrayMintWallet()`, with a login that may make tables. It makes the schema `gmwallet`,
  the wallet's tables and the row of their version when they are not there; it can run any number of times. On a
  connection of its own it makes the database too. Run a new build's setup before its first start: the start
  takes tables of its own version alone.
- The login the host runs with needs to read, insert and update the wallet's rows, and nothing more.
- A host that walls the rows off by tenant, as with a row-level security policy, names the tables from
  `WalletSchema`: every row of `TenantTables` has its tenant in `TenantId`, and `SchemaVersion` holds one row of
  no tenant, which every login reads.
- Give the wallet's context no strategy that retries on failure (`EnableRetryOnFailure`): the start refuses one.
  Retry a whole call instead; its keys make that safe.
- On SQL Server, leave `XACT_ABORT` off, as SQL Server has it, or a call the database refuses ends the host's
  whole database transaction.
- Run the wallet's calls at read committed, the databases' default. Under snapshot or repeatable read, a key
  another call took meanwhile comes out as the database's error, not as a conflict.
- The wallets a call locks stay locked until the host's database transaction ends: commit soon after the
  wallet's calls, and keep slow work, such as a call to a payment gateway, out of the database transaction. Two
  database transactions that each make several wallet calls can deadlock; the database ends one of them, which
  the host retries whole.
- Money enters a tenant from its root wallet in the currency, `WalletService.GetOrCreateRootWallet`, and leaves it
  to that wallet: a top-up paid from outside goes out of the root, a refund paid back goes into it. The owner
  `root` is the root wallets' alone.
- Nothing calls `WalletReconciliationService.Reconcile` on its own: call it when you want its report of where the
  books disagree. It reads every wallet and transfer the login can see. In a SQL Server database that reads
  committed rows by locks, not by their versions as one the setup makes does, it holds writes to the wallets off
  while it adds the currencies up.

## The server

### Settings

Settings come the ASP.NET Core way: `appsettings.json`, then `appsettings.Production.json` beside it,
environment variables and arguments.

| Setting | What it is |
| --- | --- |
| `ConnectionStrings:WalletDatabase` | The server's database. Required. |
| `WalletServer:DatabaseProvider` | `PostgreSql` or `SqlServer`: what the connection string is for. Required. |
| `WalletServer:ApiKeys` | The keys the system calls with: one, or two while a new key takes over. Each has at least 32 characters, such as 32 random bytes in base64 (`openssl rand -base64 32`). Required, and secret: whoever holds a key may move any tenant's money. |

The server checks its settings as it starts, and a missing or wrong one stops the start.

### The database

The server needs PostgreSQL 18 or SQL Server 2022 or later, and a database that keeps text as UTF-8: the setup
makes its database so, and a database made beforehand needs the UTF8 encoding on PostgreSQL, or a UTF-8 default
collation such as `Latin1_General_100_CI_AS_SC_UTF8` on SQL Server. Give it two logins: an owner of the tables,
which the setup alone uses, and a login for the server that may read and write rows and nothing more (on SQL
Server, `db_datareader` and `db_datawriter`). Then set the database up, with the owner's connection string:

    GrayMint.Wallet.Server setup --ConnectionStrings:WalletDatabase="..." --WalletServer:DatabaseProvider=PostgreSql

It needs no setting but these two, makes the database and the tables when they are not there, and can run any
number of times. Run it again before a new build of the server first starts: the server starts on tables of its
own version alone.

To change the key, add the new one as a second key, move the caller to it, then take the old one out. Each change
of the keys takes a restart.

### Calling it

The caller, the system, sends one of the server's keys with every request, as `Authorization: Bearer <key>`:

| Address | What it does |
| --- | --- |
| `POST /api/tenants/{tenantId}/wallets` | Makes a wallet. |
| `GET /api/tenants/{tenantId}/wallets/root?currency=` | The tenant's root wallet in a currency, made at the first call. |
| `GET /api/tenants/{tenantId}/wallets/{walletId}` | A wallet, with its totals. |
| `GET /api/tenants/{tenantId}/wallets?ownerReferenceKey=` | An owner's wallets. |
| `PATCH /api/tenants/{tenantId}/wallets/{walletId}` | Changes a wallet's limit: `{ "minAvailableBalance": { "value": -50 } }`. |
| `GET /api/tenants/{tenantId}/wallets/{walletId}/transactions?limit=&beforeTransferId=` | Its transactions, newest first: its statement, with each transfer's amount and the balances it left. |
| `POST /api/tenants/{tenantId}/transfers` | Makes linked transfers, all of them or none. |
| `POST /api/tenants/{tenantId}/transfers/post` | Posts pending transfers by their external reference keys. |
| `POST /api/tenants/{tenantId}/transfers/void` | Voids pending transfers by their external reference keys. |
| `GET /api/tenants/{tenantId}/transfers?externalReferenceKey=` | A transfer by its external reference key. |
| `GET /api/reconciliation` | Reconciles the books of every tenant and reports where they disagree, changing nothing. |

Nothing reconciles the books on its own: ask for the report when you want it. It reads every wallet and transfer,
and in a SQL Server database that reads committed rows by locks, not by their versions as one the setup makes does,
it holds writes to the wallets off while it adds the currencies up.

Every id, in an address or in a body, is text: `"walletId": "42"`. A transfer's type is its name, such as
`"PostPending"`.

A request without one of the server's keys answers 401. A refused request answers 400 when it is wrong in itself,
404 for a wallet or transfer that is not in the tenant or an id that is no number, and 409 when the tenant's state
refuses it: too little to spend, a key of another transfer, a wallet that is there already, a pending transfer
settled the other way. Any other failure, such as the database's, answers 500. A caller that gets no answer, or a
500, sends the same request again.

The API describes itself at `/openapi/v1.json`, for a tool that makes a client, and in a reference to read at
`/scalar`, where the server's root leads. Neither takes a key, and the description holds no data, only the
addresses and their words.

The server takes no request from another site's page and sends no CORS headers. Run it behind TLS.
