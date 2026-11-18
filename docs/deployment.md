# Deployment

For whoever runs the server, `GrayMint.Wallet.Server`, or a host that embeds the wallet.

## A host that embeds the wallet

- `AddGrayMintWallet()` registers the service, and `AddGrayMintWalletDb` its context: on the host's connection,
  to run in the host's transactions, or on a connection of its own (the README shows both). The host doesn't
  start on a database whose wallet tables, or their guards, are missing or switched off.
- The host's setup runs `UseGrayMintWallet()`, with a login that may make tables. It makes the schema `gmwallet`
  and the wallet's tables when they are not there, and puts their triggers on them; it can run any number of
  times. On a connection of its own it makes the database too.
- The login the host runs with needs to read, insert and update the wallet's rows, and nothing more. Give it no
  right to alter the tables: on SQL Server that right would let it empty `Transfers`.
- Give the wallet's context no strategy that retries on failure (`EnableRetryOnFailure`): the start refuses one.
  Retry a whole call instead; its keys make that safe.
- On SQL Server, leave `XACT_ABORT` off, as SQL Server has it, or a call the database refuses ends the host's
  whole transaction.
- A job of the host's runs `FindTotalsMismatches` now and then and raises an alarm on anything it finds.

## The server

### Settings

Settings come the ASP.NET Core way: `appsettings.json`, then `appsettings.Production.json` beside it,
environment variables and arguments.

| Setting | What it is |
| --- | --- |
| `ConnectionStrings:WalletDatabase` | The server's database. Required. |
| `WalletServer:DatabaseProvider` | `PostgreSql` or `SqlServer`: what the connection string is for. Required. |
| `Auth:Issuer` | The issuer of the server's tokens. Required. |
| `Auth:Audience` | The audience of the server's tokens. |
| `Auth:Secret` | The key that signs the server's tokens: 32 random bytes as base64. Required, and of the server's own. Whoever holds it is the system. |
| `Auth:AllowSystemApiKey` | `true` opens `POST /api/authorization/system/api-key`, which takes the secret and answers with a token for the system. Off outside Development unless set. |
| `WalletServer:SystemAuthorizationCode` | Every system token carries it: changing it ends every system token at once. Required. |

The server checks its settings as it starts, and a missing or wrong one stops the start.

### The database

The server needs PostgreSQL 18 or SQL Server 2022 or later. Give it two logins: an owner of the tables, which the
setup alone uses, and a login for the server that may read and write rows and nothing more (on SQL Server,
`db_datareader` and `db_datawriter`). Then set the database up, with the owner's connection string:

    GrayMint.Wallet.Server setup --ConnectionStrings:WalletDatabase="..." --WalletServer:DatabaseProvider=PostgreSql

It makes the database and the tables when they are not there, puts their triggers on them, and can run any
number of times.

### Calling it

A caller signs in as the system with a token the secret signed: either one it makes itself with
GrayMint.Authorization, or one the system api-key address gives back for the secret. Every address takes it as
`Authorization: Bearer <token>`:

| Address | What it does |
| --- | --- |
| `POST /api/ledgers/{ledgerId}/accounts` | Makes an account. |
| `GET /api/ledgers/{ledgerId}/accounts/{accountId}` | An account, with its totals. |
| `GET /api/ledgers/{ledgerId}/accounts?ownerId=` | An owner's accounts. |
| `PUT /api/ledgers/{ledgerId}/accounts/{accountId}/min-balance` | Changes an account's limit. |
| `GET /api/ledgers/{ledgerId}/accounts/{accountId}/transfers?count=&beforeTransferId=` | Its transfers, newest first. |
| `POST /api/ledgers/{ledgerId}/transfers` | Makes a chain of transfers. |
| `POST /api/ledgers/{ledgerId}/transfers/post` | Posts pending transfers by their keys. |
| `POST /api/ledgers/{ledgerId}/transfers/void` | Voids pending transfers by their keys. |
| `GET /api/ledgers/{ledgerId}/transfers/by-key?key=` | A transfer by its key. |

Every id, in an address or in a body, is text: `"accountId": "42"`. A transfer's type is its name, such as
`"PostPending"`.

A refused request answers 400 when it is wrong in itself, 404 for an account or transfer that is not in the
ledger or an id that is no number, and 409 when the ledger's state refuses it: too little to spend, a key of
another transfer, an account that is there already, a pending transfer settled the other way. A caller that gets
no answer sends the same request again.

The server takes no request from a browser and sends no CORS headers. Run it behind TLS.
