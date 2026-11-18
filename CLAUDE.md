# GrayMint.Wallet

A double-entry ledger on PostgreSQL or SQL Server, with a context and tables of its own that can run on the
host's connection and in the host's transaction, and a server that runs it as a service of its own. Read [docs/design.md](docs/design.md) before
changing code: it says what the code holds, what the database holds, and why each is where it is.
[docs/deployment.md](docs/deployment.md) is for whoever runs the server; keep both true in the same change that
makes them wrong.

## Rules

1. **The repository is general.** Its code, docs, tests and commit messages name no other product, company or
   person. Examples use neutral names: a shop, an order, `example.com`.
2. **A guard comes with its test, and the test is proven.** Whatever keeps money right (a limit, a lock, a key,
   a check of the database, a permission) gets a test that fails when the guard is switched off. A test that
   stays green with its guard off proves nothing. Where two guards stop the same thing, as the service's limit
   check and the database's, each gets a test the other can't pass for it.
3. **A transfer is never changed or deleted.** No code updates or deletes a row of `Transfers`. A mistake is put
   right by a transfer the other way. A pending transfer is settled by a second row, its post or its void. No
   trigger enforces it in the database: a writer that goes round the service could do worse, and reconciliation
   finds a transfer changed or deleted on its own.
4. **The totals move only under the account's lock.** Every write locks its accounts first, one by one in id
   order, in the host's transaction or one of its own, and adds its change to what the database holds. A write
   inside a transaction it did not begin takes a savepoint, so a refusal takes back all it wrote. The context is
   the wallet's own: never put the wallet's tables in a host's model, or a host's in the wallet's.
5. **A request sent again changes nothing.** Every new transfer carries the caller's key; the same chain sent
   again gives back what it made the first time, and a key of another transfer is refused, never posted twice.
6. **Tests run on a real PostgreSQL and a real SQL Server.** `docker compose up -d`, then `dotnet test`. Every
   case runs on both (`[EveryDatabase]`), each in a database of its own, made as the setup makes it. Run them
   after every change; a change isn't done until they pass.
7. **One model for both databases.** Whatever EF can say goes through EF. What the two databases say
   differently (a row lock, a catalog query) goes in `WalletDialect`, one class for each, and comes
   with a test that runs on both. The package names no database provider: the host names its own.
8. **Fail loudly.** A setting that is missing or wrong stops the start, not the first request; so does a
   database whose wallet tables lack their guards. No error is caught just to go on.
9. **.NET 10**, pinned by `global.json`, until the PostgreSQL provider for the next EF Core is released.

## Code

- One type per file, in the folder of its feature (`Accounts`, `Transfers`, `Reconciliation`, `Persistence`,
  `Exceptions`; in the server also `Security`), whatever its visibility.
- Fields and state at the top of a class. A variable gets its value where it is declared.
- No `!` to silence a null: check it, or throw with what is wrong.
- A method that waits is async and takes a required `CancellationToken`, which it passes on.
- A method hands back one named type, never a tuple. A list is an `IReadOnlyList<T>` wherever it is named by its
  type.
- An object is complete when its constructor returns. A component guards its own rules; no caller has to
  remember a step before calling it.
- A comment says why the code is there, or which tempting change would break what. No history, and nothing the
  code already says.
- No top-level statements.
- Nothing temporary in the repository: scratch goes to the system's temp folder.

## Publishing

Every push to `main` publishes the `GrayMint.Wallet` package to nuget.org, so `main` only gets what is ready to
ship. CI builds, runs the tests on the services of `compose.yaml`, raises the version in `pub/PubVersion.json`
and `src/Directory.Build.props` with a commit of its own, and pushes the package; a failed test stops it before
the version moves. Never change the version by hand. `publish_nugets.yml` is a thin wrapper around the shared
workflow of `graymint/GrayMint.Common`, and nuget.org takes the package only from that file in this repository
(a trusted publishing policy, made on nuget.org before the first publish), so renaming the file or the
repository stops publishing. The server is a host, not a package: it is never packed.

The README is the package's page on nuget.org.

Dependabot watches the dependencies: it raises an alert for a vulnerable package and opens a pull request that
fixes it, and once a month opens one for the workflows' actions (`.github/dependabot.yml`). Merging one is a
push to `main`, so it publishes; its tests run first, as for any push. The shared daily NuGet update
(`update_nugets.yml`) comes with the move to the next .NET (rule 9): until then it would try EF Core's next
design package, which needs the next .NET, and fail every day without updating anything.
