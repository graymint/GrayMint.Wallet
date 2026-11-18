# Design

The wallet is a ledger on TigerBeetle's model, kept in PostgreSQL or SQL Server. This page says how it is built
and why, and what keeps money right: what the code holds and what the database holds beside it.

## The model

- **A ledger** is a book of accounts that money never leaves. It is a number of the host's choosing, such as one
  for each tenant; the wallet keeps no table of ledgers.
- **An account** (`gmwallet.Accounts`) belongs to one ledger, one owner and one currency, and has a code that
  tells the owner's accounts apart, such as its credit and its commission. There is one account for each ledger,
  owner, code and currency. It keeps four totals, posted and pending, of its debits and its credits, and a
  `MinBalance`.
- **A transfer** (`gmwallet.Transfers`) moves an amount from its debit account to its credit account, both of
  its ledger and its currency. Its type is `Posted`, `Pending`, `PostPending` or `VoidPending`. The first two
  carry the caller's key; the last two settle a pending transfer, name it, and carry no key.

An account's balance is its credits minus its debits, of what is posted. What it may spend is its posted
credits minus its posted and pending debits: a pending debit is held at once, and a pending credit is not there
until it is posted. `MinBalance` is the lowest that may go: 0 for no credit, below 0 for a credit limit, null for
no limit, as a host's own cash or revenue account has.

Amounts are decimals with at most 8 places, above 0, and at most 10^15 each. A ninth decimal is refused, as the
database would round it away without a word. An account's totals only grow, and each holds up to 10^20, as many
whole digits as a decimal holds with 8 places: the database refuses a transfer that would take a total past
that, which no real ledger comes near.

Owners, codes, currencies and keys differ by any character, their case too, on either database: SQL Server's
columns get a binary collation, since a database's own there mostly ignores case. A space at either end is
refused, as SQL Server would not tell `a` from `a `.

## Its own context, and the host's transaction

The wallet has a context of its own, `WalletDbContext`, whose tables are in the schema `gmwallet`. It has no
migrations: the setup (`UseGrayMintWallet`) makes the tables from its model when none of them is there, and puts
on them what the model can't say (the triggers below). It runs any number of times and puts back a trigger that
was dropped or switched off. A database with only some of the tables is refused, and nothing is changed in it.

A host registers the context one of two ways:

- **On the host's connection** (`AddGrayMintWalletDb<THostDbContext>`), so the host's rows and the money can be
  saved in one transaction: an order is never paid without being there, or there unpaid. For each call the
  host's context opens the connection, so the host's connection interceptors run as for its own commands (one
  that tells the database which tenant a connection works for, say), and the call works in the transaction the
  host's context has open.
- **On a connection of its own** (`AddGrayMintWalletDb`), as the server has it.

A call that writes runs in the transaction that is open. Inside one it did not begin, it takes a savepoint
first: when the database refuses something halfway, the call takes back what it wrote, so a host that goes on
and commits commits none of it, and the transaction stays usable, which PostgreSQL would not do after an error.
With no transaction open, a call that writes runs in one of its own. On SQL Server, a deadlock, or any error
while `XACT_ABORT` is on, ends the whole transaction, the host's with it: nothing is left to take back, and the
call lets the database's error go on as it came. A host that wants the savepoints there leaves `XACT_ABORT`
off, as SQL Server has it.

A strategy that retries on failure (`EnableRetryOnFailure`) refuses a transaction begun outside it, so the
wallet's context has none: the setup and the start refuse one. A caller retries a whole call instead, which the
keys make safe.

The context being the wallet's own, a call never saves the host's unsaved changes. It writes its new rows
through its own change tracker, one transfer at a time so the ids follow the chain on either database, and
empties the tracker after each. It reads without tracking, and changes totals with one `UPDATE` each.

## Writing money

`CreateTransfers` takes a chain of transfers:

1. It checks the request: keys, amounts, codes, no account on both sides, no key twice.
2. It locks every account of the chain, one by one in id order, so two calls that need the same accounts never
   wait for each other in a circle. The lock is `FOR UPDATE` on PostgreSQL and `UPDLOCK, ROWLOCK` on SQL Server,
   and holds until the transaction ends. An account of another ledger is not found.
3. After the lock, it looks for the chain's keys. If every transfer of the chain is there as sent, the chain was
   made before: it is given back and nothing changes. If some key is there but names another transfer, or only
   part of the chain is there, the call is refused. A chain sent twice at once waits at the lock for the first
   and then finds it. Each transfer is matched by itself, as in TigerBeetle: a key names one transfer, so the
   same transfers sent again in another order, or some of them, are given back and move no money twice.
4. It applies the transfers in order to the locked totals, so each sees the ones before it, and refuses the
   chain if any debit would take an account below its `MinBalance`.
5. It inserts the transfers and adds each account's change to its totals, by the change, so the database adds
   it to what it holds.

A key that another call takes meanwhile, on accounts of its own, so that the lock didn't keep the two apart, is
refused by the unique index. EF takes the refused insert back to a savepoint of its own, and the call answers it
as a key of another transfer. An account made twice at once goes the same way.

`PostPendingTransfers` and `VoidPendingTransfers` take keys of pending transfers and settle them in one go. Each
is settled once: a pending transfer settled the same way before comes back as it was, one settled the other way
is refused. Posting moves the amount from pending to posted on both accounts; voiding gives it back.

`SetMinBalance` takes a lower limit at any time, and a higher one only while the account keeps to it.

## What the database holds

The code checks everything first, for errors that say what is wrong. The database holds the same things again,
so a fault in the code, or a write that goes round it, can't break them:

- **A check of the accounts**: no total below 0, and what may be spent never below `MinBalance`.
- **Checks of the transfers**: an amount above 0, two different accounts, and a key or a settled transfer as the
  type asks.
- **Foreign keys by ledger and currency**: a transfer names its accounts together with its ledger and its
  currency, and a settlement names its pending transfer together with its ledger, so money can't cross either.
- **Unique indexes**: one key for each ledger, one settlement for each pending transfer, one account for each
  ledger, owner, code and currency.
- **Triggers**: a transfer is never updated or deleted, by any role, its owner included. On PostgreSQL a second
  trigger refuses to empty the table, as `TRUNCATE` passes row triggers by. SQL Server has no trigger for
  `TRUNCATE TABLE`, which there needs the right to alter the table, a right the wallet's login never needs.
  Dropping a table, or a trigger, is a change of the schema, which only the owner of the tables can make.

The model gives the checks, keys and indexes; the setup makes the triggers. At the host's start, the wallet
checks that its tables are there, its triggers are there and on for every session, and its checks are there, each
on its own table, and trusted; the host doesn't start otherwise.

The database holds that a post or a void names a pending transfer of its own ledger, not that it copies that
transfer's accounts and amount: the service copies them, and a writer that goes round the service could as well
write any transfer that looks right, which no key of the database tells apart.

## Two databases

Everything but three things goes through EF, which says it in each database's own way. The three are in
`WalletDialect`, one class for each database: the row lock, the triggers, and the queries of the database's
catalog that the start checks them by. Each case of the tests runs on both.

## Reconciliation

The totals are kept with the accounts so a balance is read without adding up transfers.
`FindTotalsMismatches` adds them up and lists every account whose stored totals differ: a write that went round
the service, or a fault in it. One query isn't sure to read an account's totals and its transfers at one moment
(SQL Server, unless the database reads committed rows by their versions), so it only names the accounts to look
at again; each is looked at again under its lock, which no write of it gets past, and only what still differs
there is listed. A host runs it now and then, from a job, and raises an alarm on anything it finds;
it reads every account the caller can see.

## The server

`GrayMint.Wallet.Server` is a host over the same service, on a connection of its own, to the database its
settings name. A caller there keeps its orders in its own database, so it saves an order and its money in two
places; when it can't tell whether a request went through, it sends the same one again, and the keys make that
safe.

Every id goes out as text, as in GrayMint's other services, so a key's type inside can change without the API
changing; an id that is no number is answered as one that is not there.

Its one caller is the system: a service that holds the server's signing secret (GrayMint.Authorization's
microservice authentication). Two walls stand between anyone else and the money, each with its test: only the
system gets the authorization code that a token must carry to sign in, and only the system gets the permissions
that every address asks for. The server takes no request from a browser, so it sends no CORS headers.

## Choices

- **A pending credit can't be spent** before it is posted, not even by the next transfer of its chain, as in
  TigerBeetle. A chain that moves money through an account posts it there first.
- **A ledger is the host's number**, not a table: the host decides what a book is, such as a tenant, and can keep
  the wallet's rows behind its own walls by it (a row-level security policy on `LedgerId`, for instance).
- **No pending transfer runs out by itself.** The host posts or voids each one; a job of the host's can void
  the ones it no longer wants.
- **No database provider in the package.** The host names its own, so a host on PostgreSQL carries nothing of
  SQL Server's, and the other way round.
