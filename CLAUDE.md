# GrayMint.Wallet

A double-entry ledger on PostgreSQL or SQL Server, with a context and tables of its own that can run on the
host's connection and in the host's database transaction, and a server that runs it as a service of its own.
Read [docs/design.md](docs/design.md) before changing code: it says what the code holds, what the database
holds, and why each is where it is.
[docs/deployment.md](docs/deployment.md) is for whoever runs the server; keep both true in the same change that
makes them wrong.

## Rules

1. **The repository is general.** Its code, docs, tests and commit messages name no other product, company or
   person. Examples use neutral names: a shop, an order, `example.com`.
2. **A guard comes with its test, written so it can't pass for another reason.** Whatever keeps money right (a
   limit, a lock, a key, a check of the database, the server's API key) gets a test that checks exactly what the
   guard refuses: the exception's exact type; its message, or the name of the database's rule, where another refusal
   could throw the same; and that nothing was kept. A test of a lock makes the calls meet: one holds the lock while
   the other waits (`WaitForBlockedCalls`), or many go at once. Where two guards stop the same thing, as the
   service's limit check and the database's, each gets a test the other can't pass for it. No test is proven by
   taking its guard out of the code: coverage shows what no test runs (rule 6), and the test's checks show why it
   fails.
3. **A transfer is never changed or deleted.** No code updates or deletes a row of `Transfers`, or of
   `WalletTransactions`, where each transfer leaves its balances on its two wallets. A mistake is put right by a
   transfer the other way. A pending transfer is settled by a second row, its post or its void. No trigger
   enforces it in the database: a writer that goes round the service could do worse, and reconciliation finds a
   transfer changed or deleted on its own.
4. **The totals move only under the wallet's lock.** Every write locks its wallets first, one by one in id
   order, in the host's database transaction or one of its own, and adds its change to what the database holds.
   A write inside a database transaction it did not begin takes a savepoint, so a refusal takes back all it
   wrote. The context is the wallet's own: never put the wallet's tables in a host's model, or a host's in the
   wallet's.
5. **A request sent again changes nothing.** Every new transfer carries the caller's external reference key; the
   same linked transfers sent again give back what they made the first time, and a key of another transfer is
   refused, never posted twice.
6. **Tests run on a real PostgreSQL and a real SQL Server.** `docker compose up -d`, then `dotnet test`. Every
   case runs on both (`[EveryDatabase]`), each in a database of its own, made as the setup makes it. Run them
   after every change; a change isn't done until they pass. With
   `--collect "XPlat Code Coverage" --settings tests/GrayMint.Wallet.Test/coverage.runsettings` they also write which
   lines of `src` ran (`coverage.cobertura.xml` under `TestResults`): a line no test runs gets a test, except a
   throw for a state the code never reaches (a root wallet gone after it was made) or for a third database.
7. **One model for both databases.** Whatever EF can say goes through EF. What the two databases say
   differently (a row lock, a read at one moment, the time a new row gets, how text is kept) goes in
   `WalletDialect`, and comes with a test that runs on both. The package names no database provider: the host
   names its own.
8. **Fail loudly.** A setting that is missing or wrong stops the start, not the first request; so does a
   database without the wallet's tables, with tables of another version, or that doesn't keep text as UTF-8. No
   error is caught just to go on.
9. **.NET 10**, pinned by `global.json`, until the PostgreSQL provider for the next EF Core is released.
10. **A change to the tables raises their version.** The start takes tables of `WalletSchema.Version` alone. A
    change to the model raises it, and gives the setup a step that takes tables of the version before to it, with
    a test that runs the step on such tables, in the same change.

## Code

- One type per file, whatever its visibility. In the package, in the folder of its feature (`Wallets`, `Transfers`,
  `Reconciliation`, `Persistence`, `Exceptions`); a table's row is a model in `Persistence/Models`, a query's own
  shape a view in `Persistence/Views`. `Persistence` is internal throughout: the tables' names a host may need
  (`WalletSchema`) are at the root, beside `WalletLimits` and the registration. The server, two thin layers over
  the package, is laid out by layer: `Controllers`, its API classes in `Dtos/<feature>`, the converters between
  them and the package's in `DtoConverters`, and `Security`.
- A host sees the three services, the classes they take and give, the exceptions, `WalletLimits`, `WalletSchema`
  and the registration, and nothing else: the context, its models and the repo are internal, so no host writes
  round the services, and a service's constructor is internal, so only the registration makes one. No interface
  stands over a service of one implementation. A public class never is a row; a converter makes it from the model.
- A service checks its request, begins a session (`WalletRepo.BeginRead`, `BeginWrite`) and calls the repo in it.
  The repo never begins one, and a service never calls a service in its session: a session begun inside another
  lets go of the database transaction the outer one began on a host's connection.
- Every id in a public class or parameter is text; the database keeps numbers, and `WalletIds` turns one into the
  other. An id that is no number is one that is not there.
- Every public type and member has an XML doc (`///`): what a caller gets, and what refuses it. The build fails
  without one.
- The server's API describes itself through GrayMint.Common.OpenApi, which reads the doc comments: every action,
  parameter and class it shows has one, which `ApiDescriptionTest` checks. Its own words, its introduction and the
  name of its key, are in `ApiDocs`.
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
