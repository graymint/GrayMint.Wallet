# Design

The wallet is a double-entry ledger on TigerBeetle's model, kept in PostgreSQL or SQL Server. This page says how it
is built and why, and what keeps money right: what the code holds and what the database holds beside it.

## The model

- **A tenant** is a partition of the host's that money never leaves, such as one store of a platform. Its id is a
  number of the host's choosing; the wallet keeps no table of tenants. (TigerBeetle's partition is a ledger, one
  for each currency; a tenant holds wallets of any currencies, and a transfer stays in one of them.)
- **A wallet** (`gmwallet.Wallets`, TigerBeetle's account) belongs to one tenant, one owner and one currency, and
  has a code that tells the owner's wallets apart, such as its credit and its commission. There is one wallet for
  each tenant, owner, code and currency. It keeps four totals of the money in and out, posted and pending
  (`PostedIn`, `PostedOut`, `PendingIn`, `PendingOut`: TigerBeetle's credits and debits), and a
  `MinAvailableBalance`.
- **A root wallet** is a tenant's wallet in one currency that money enters the tenant from and leaves it to, such
  as a top-up paid from outside or a refund paid back. `WalletService.GetOrCreateRootWallet` makes it at its first
  call, with the owner `root` (`WalletService.RootOwnerReferenceKey`) and no limit. No other wallet has that owner,
  and no limit is put on it.
- **A transfer** (`gmwallet.Transfers`) moves an amount from one wallet to another, both of its tenant and its
  currency (`FromWalletId` and `ToWalletId`: TigerBeetle's debit and credit accounts). Its type is `Posted`,
  `Pending`, `PostPending` or `VoidPending`. The first two carry the caller's key; the last two settle a pending
  transfer, name it, and carry no key.
- **A wallet transaction** (`gmwallet.WalletTransactions`) is a wallet's side of a transfer, a line of its
  statement: the transfer's amount, below 0 on the wallet it comes from and above 0 on the one it goes to (the
  other way round for a void, which undoes its pending transfer), and the wallet's posted and available balance
  after it. Each transfer makes one on each of its two wallets, of every
  type, so a wallet's transactions in the order of its transfers are its running balance.

A wallet's `PostedBalance` is the money posted in minus the money posted out. Its `AvailableBalance`, what it may
spend, is the money posted in minus the money posted and pending out: money pending out is held at once, and money
pending in is not there until it is posted. `MinAvailableBalance` is the lowest that may go: 0 for no credit, below
0 for a credit limit, null for no limit, as a root wallet has.

A transfer takes from one wallet what it gives another, so the posted balances of a tenant's wallets in a currency
add up to zero, and their money pending in is their money pending out. The root's balance is minus everything the
tenant's other wallets in its currency hold.

Amounts are decimals with at most 8 places, above 0, and at most 10^15 each. A ninth decimal is refused, as the
database would round it away without a word. Each of a wallet's totals holds up to 10^20, as many whole digits
as a decimal holds with 8 places; the posted ones only grow, and the database refuses a transfer that would take
a total past that, which no real wallet comes near.

Owners, codes, currencies and external reference keys differ by any character, their case too, on either database: SQL
Server's columns get a binary collation, since a database's own there mostly ignores case. A space at either end
is refused, as SQL Server would not tell `a` from `a `.

Text is UTF-8 on both databases, a byte a character of ASCII, and a key may be in any script. PostgreSQL keeps text
in its database's encoding, UTF-8 by default, and counts a length in characters. SQL Server keeps `varchar` as UTF-8
under a UTF-8 collation (`Latin1_General_100_BIN2_UTF8` on the wallet's columns), where `nvarchar` would take two bytes
a character, and counts a length in bytes, so a column of 100 characters is 300 bytes long, the most 100 .NET
characters take. Its client sends a `varchar` parameter in the code page of the database's own collation, whatever
the column's, so the database's must be UTF-8 too: a letter outside its code page would arrive as `?`, and two keys
would be one. The setup makes its database so, and the start refuses a database, of either kind, that doesn't keep
text as UTF-8.

Every id a call takes or gives is text: a wallet's, a transfer's and a tenant's. The database keeps numbers, the
smallest and quickest keys on both databases; outside, text lets them change without a host's code changing. An id
that is no number names nothing, and is answered as one that is not there.

## The code

A host injects three services, one for each feature: `WalletService`, `WalletTransferService` and
`WalletReconciliationService`. Everything behind them is internal, so a host can't
write round them: the context, the rows (`Persistence.Models`), and `WalletRepo`, the one class that queries and
writes the tables. A service checks its request, begins a session, and calls the repo in it; the repo never begins
one, so every query of a call runs on the call's connection and in its database transaction. Converters make the
public classes from the rows, so a change to the tables never changes them unseen. `WalletSchema` names the
schema and the tables, for a host that walls them off; `WalletLimits` gives the limits, for a host that keeps the
same values.
A wallet goes out as `WalletInfo` (see Choices).

## Its own context, and the host's database transaction

The wallet has a context of its own, `WalletDbContext`, whose tables are in the schema `gmwallet`. It has no
migrations: the setup (`UseGrayMintWallet`) makes the tables from its model when none of them is there, writes
their version in `gmwallet.SchemaVersion`, and runs any number of times. A database with only some of the tables
is refused, and nothing is changed in it. The start refuses tables of any version but the one the build knows
(`WalletSchema.Version`), and tables without one: a build would read tables of another shape wrong. A change to
the tables raises the version, and gives the setup a step that takes tables of the version before to it.

A host registers the context one of two ways:

- **On the host's connection** (`AddGrayMintWalletDb<THostDbContext>`), so the host's rows and the money can be
  saved in one database transaction: an order is never paid without being there, or there unpaid. For each call
  the host's context opens the connection, so the host's connection interceptors run as for its own commands (one
  that tells the database which tenant a connection works for, say), and the call works in the database
  transaction the host's context has open.
- **On a connection of its own** (`AddGrayMintWalletDb`), as the server has it.

A call that writes runs in the database transaction that is open. Inside one it did not begin, it takes a
savepoint first: when the database refuses something halfway, the call takes back what it wrote, so a host that
goes on and commits commits none of it, and the database transaction stays usable, which PostgreSQL would not do
after an error. With no database transaction open, a call that writes runs in one of its own. On SQL Server, a
deadlock, or any error while `XACT_ABORT` is on, ends the whole database transaction, the host's with it: nothing
is left to take back, and the call lets the database's error go on as it came. A host that wants the savepoints
there leaves `XACT_ABORT` off, as SQL Server has it.

A strategy that retries on failure (`EnableRetryOnFailure`) refuses a database transaction begun outside it, so
the wallet's context has none: the setup and the start refuse one. A caller retries a whole call instead, which
the keys make safe.

The calls expect read committed, the databases' default, with or without row versions. Under snapshot or
repeatable read, a key that another call took meanwhile comes out as the database's error rather than a conflict,
and a wallet that another database transaction changed meanwhile can't be locked; the host retries the whole
database transaction. A host's database transaction also keeps the locks of every call made in it until it ends:
each call locks in id order, but two database transactions that each make several calls can still wait for each
other in a circle, and the database ends one of them as a deadlock, to be retried whole.

The context being the wallet's own, a call never saves the host's unsaved changes. It writes its new rows
through its own change tracker, one transfer at a time so the ids follow the transfers' order on either database,
then the transfers' wallet transactions at once, as a wallet's are ordered by those ids; and empties the tracker
after each. It reads without tracking, and changes totals with one `UPDATE` each.

## Writing money

`WalletTransferService.Create` takes linked transfers:

1. It checks the request: keys, amounts, codes, no wallet on both sides, no key twice.
2. It locks every wallet of the transfers, one by one in id order, so two calls that need the same wallets never
   wait for each other in a circle. The lock is `FOR UPDATE` on PostgreSQL and `UPDLOCK, ROWLOCK` on SQL Server,
   and holds until the database transaction ends. A wallet of another tenant is not found.
3. After the lock, it looks for the transfers' external reference keys. If every transfer is there as sent, they were
   made before: they are given back and nothing changes. If some key is there but names another transfer, or only
   some of the transfers are there, the call is refused. Transfers sent twice at once wait at the lock for the
   first call and then find what it made. Each transfer is matched by itself, as in TigerBeetle: a key names one
   transfer, so the same transfers sent again in another order, or some of them, are given back and move no money
   twice.
4. It applies the transfers in order to the locked totals, so each sees the ones before it, and refuses them
   all if any would take the wallet it comes from below its `MinAvailableBalance`.
5. It inserts the transfers, then their wallet transactions with the balances each left on its two wallets, as
   it applied them to the locked totals; and adds each wallet's change to its totals, by the change, so the
   database adds it to what it holds.

A key that another call takes meanwhile, on wallets of its own, so that the lock didn't keep the two apart, is
refused by the unique index. EF takes the refused insert back to a savepoint of its own, and the call answers it
as a key of another transfer. A wallet made twice at once goes the same way, and a root wallet asked for twice
at once is made by one call, while the other, refused, takes it. Two calls that use the same keys
for other transfers, in another order, can each hold a key the other waits for: the database ends one as a
deadlock, and sent again it is refused as a conflict.

`PostPending` and `VoidPending` take external reference keys of pending transfers and settle them in one go.
Each is settled once: a pending transfer settled the same way before comes back as it was, one settled the other
way is refused. Posting moves the amount from pending to posted on both wallets; voiding gives it back. A post
or a void makes its wallet transactions as any transfer does.

`WalletService.Update` changes a wallet's limit, the one thing of a wallet a caller changes, as a `Patch<T>` of
GrayMint.Common: a field the request leaves out keeps its value. It takes a lower limit at any time, and a higher one
only while the wallet keeps to it. A root wallet takes none.

## What the database holds

The code checks everything first, for errors that say what is wrong. The database holds the same things again,
so a fault in the code, or a write that goes round it, can't break them:

- **A check of the wallets**: no total below 0, and what may be spent never below `MinAvailableBalance`.
- **Checks of the transfers**: an amount above 0, two different wallets, and an external reference key or a settled
  transfer as the type asks.
- **Foreign keys by tenant and currency**: a transfer names its wallets together with its tenant and its
  currency, and a settlement names the transfer it settles together with its tenant, so money can't cross either.
  A wallet transaction names its wallet and its transfer together with its tenant.
- **Unique indexes**: one external reference key for each tenant, one settlement for each pending transfer, one
  wallet for each tenant, owner, code and currency, one wallet transaction for each wallet and transfer.

The model gives all of them, and the setup makes them with the tables. At the host's start, the wallet checks that
its tables are there, of its version; the host doesn't start otherwise.

The database holds that a post or a void names a transfer of its own tenant, not that the transfer is pending or
that the settlement copies its wallets and amount: the service checks and copies them, and a writer that goes
round the service could as well write any transfer that looks right, which no key of the database tells apart.
For the same reason it doesn't hold that a wallet transaction's wallet is one of its transfer's two, or that its
balances follow from the one before: the service writes them under the wallets' locks.

## Two databases

Everything but four things goes through EF, which says it in each database's own way. The four are in
`WalletDialect`, for each database: the row lock, a read of all the wallets at one moment, the time a new row gets,
and how text is kept: the collations that make SQL Server keep UTF-8 and tell case apart, a text column's length,
and how a database says it keeps UTF-8. The start finds the tables through `INFORMATION_SCHEMA`, which both
databases have. Each case of the tests runs on both.

A database the setup makes on SQL Server reads committed rows by their versions, as EF makes one, so a statement
reads its rows at one moment, as PostgreSQL's always do. A host's database may read them by locks, row by row; the
code holds for both, and the test that depends on it turns the versions off.

## Reconciliation

The totals are kept with the wallets so a balance is read without adding up transfers, and each wallet transaction
keeps the balances its transfer left, so a statement is read without adding them up either.
`WalletReconciliationService.Reconcile` compares them and reports where they disagree, each a write that went
round the service or a fault in it:

- each wallet's totals, against what its transfers add up to;
- each wallet's balances, against the ones its newest wallet transaction left, or 0 for a wallet without one;
- each tenant's wallets in each currency, against zero, as a trial balance: as much posted in as posted out, and
  as much pending in as pending out.

Nothing calls it on its own: it reads every wallet and transfer the caller can see, so the host calls it when it
wants the report.

One query isn't sure to read a wallet's totals, its transfers and its wallet transactions at one moment (SQL
Server, unless the database reads committed rows by their versions), so it only names the wallets to look at
again; each is looked at again under its lock, which no write of it gets past, and only what still differs there
is reported. The currencies are added up in one query from the wallets at one moment: a PostgreSQL statement reads
them so, and on SQL Server, unless the database reads committed rows by their versions, a shared lock of the
wallets' table waits for the writes under way and holds new ones off until the query ends. They are added up
first, while the check holds no wallet's lock.

The foreign keys keep a transfer in its currency, so a currency stops adding up only through a wallet whose totals
are off, which the check lists too; the sum is the figure to look at first. Only each wallet's newest wallet
transaction is checked: one changed before it moves no money, and only a write round the service changes one.

## The server

`GrayMint.Wallet.Server` is a host over the same service, on a connection of its own, to the database its
settings name. A caller there keeps its orders in its own database, so it saves an order and its money in two
places; when it can't tell whether a request went through, it sends the same one again, and the keys make that
safe. So a failure of the server's own, such as the database's, answers 500: GrayMint's handler answers 400 for
an exception it is told nothing about, which would tell the caller its request was wrong and not to send it again,
when a commit may even have gone through.

Its ids are the package's text ids, as in GrayMint's other services; an id that is no number answers 404. Its
requests and answers are classes of its own, copied from the package's, a transfer's type too, so a change to the
package never changes the API unseen; they are in `Dtos`, and the converters between them and the package's in
`DtoConverters`.

Its API describes itself through GrayMint.Common.OpenApi: ASP.NET Core's OpenAPI document at `/openapi/v1.json`,
worded from the doc comments of its controllers and classes and of the packages' types it shows, with Scalar's
reference at `/scalar`, where the root leads. The server adds its title and its introduction (`ApiIntro.md`), and
names its bearer token for what it is, an API key. `ApiDescriptionTest` fails on an operation, parameter, section
or field without words.

Its one caller is the system: a service that holds one of the server's API keys (`WalletServer:ApiKeys`), which
every address asks for as `Authorization: Bearer <key>`. `EveryAddressAsksForTheServersKey` reads the addresses
from the API's description, so a new one is held to it too. A key is compared by its hash, in fixed time and with
every key. Two keys at once let a new key take over before the old one goes, and a key shorter than 32 characters,
or with a space at either end, stops the start. The server takes no request from a browser, so it sends no CORS
headers.

## Choices

- **Money pending in can't be spent** before it is posted, not even by the next of the transfers linked to
  it, as in TigerBeetle. Linked transfers that move money through a wallet post it there first.
- **Wallet transactions beside the transfers.** A transfer stays one row, with an amount above 0 and its two
  wallets for the direction; its wallet transactions give each wallet's side of it, signed, with the balances it
  left. So a statement is read without adding up a wallet's history, and its running balance is the one the lock
  saw. Banking APIs name a line of an account's history so, with the balance after each. TigerBeetle keeps the
  same for an account with the `history` flag. The database's own transaction
  is always called a database transaction here, so the two are never confused.
- **An external reference key, not an id.** Every id here is a row of the wallet's own, as `WalletId` and
  `TransferId` are; the caller's key for a transfer is its own reference to it, and a key says it is unique.
  It is what makes a request sent again change nothing.
- **In and out, not debits and credits.** A wallet's balance is always what came in minus what went out, so its
  totals and a transfer's two wallets are named by the way the money goes. TigerBeetle's debits and credits leave
  that to each account, as an asset grows by its debits and a liability by its credits; a wallet has one way.
- **A tenant is the host's number**, not a table: the host decides what a tenant is, such as one store of a
  platform, and can keep the wallet's rows behind its own walls by it (a row-level security policy on `TenantId`,
  for instance). A host's database, or a server, serves one product; a wallet that several products shared would
  need a partition above the tenant, and a key for each product.
- **One root wallet for each tenant and currency**, not one wallet for the host: money can't leave a tenant, so
  each needs its own way in and out.
- **Reconciliation runs only when called.** It reads every wallet and transfer, so the host decides when; the
  server's caller asks for it at `GET /api/reconciliation`.
- **A plain API key for the server**, not GrayMint.Authorization. The server has one caller, so it needs no users,
  roles or tokens; GrayMint.Authorization pays off where callers have different rights, such as several products
  sharing one server, each limited to its own.
- **`WalletInfo`, not `Wallet`.** A type named like the package's namespace, `GrayMint.Wallet`, can't be named by
  its simple name in code under that namespace (CA1724), so the package's wallet is `WalletInfo`. The server's
  answer, in a namespace of its own, is `Wallet`.
- **No pending transfer runs out by itself.** The host posts or voids each one; a job of the host's can void
  the ones it no longer wants.
- **No database provider in the package.** The host names its own, so a host on PostgreSQL carries nothing of
  SQL Server's, and the other way round.
- **A version, not migrations.** EF's migrations are written for one database at a time, while one model makes the
  wallet's tables on both; so the setup keeps the version of their shape in a row of its own, as a library that
  makes its own tables in a host's database does.
- **No interfaces for the services.** Each has one implementation, and its tests run on real databases, as the
  host's can. A public interface would also break any host class that implements it, such as a test's stand-in,
  with every method added to it.
- **No trigger refuses a change to a transfer.** The service never changes or deletes one, and a writer that goes
  round the service could as well insert a transfer or change the totals, which no trigger stops. Reconciliation
  finds a transfer changed or deleted on its own, as its wallets' totals no longer add up.
- **The start doesn't look at the checks.** The setup makes them with the tables. Dropping one or switching it off
  takes the right to alter the tables, and whoever has it can as well change any row; the checks are there for a
  fault in the code or a careless write, neither of which can switch them off.
