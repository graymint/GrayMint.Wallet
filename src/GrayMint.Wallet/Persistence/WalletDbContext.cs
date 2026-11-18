using System.Linq.Expressions;
using GrayMint.Wallet.Accounts;
using GrayMint.Wallet.Transfers;
using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet.Persistence;

// The wallet's own tables, in a schema of their own beside the host's. It has no migrations: the setup makes the
// tables from this model and puts beside them what the model can't say (WalletExtension.UseGrayMintWallet).
public class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public const string Schema = "gmwallet";

    internal const string AccountsTable = "Accounts";
    internal const string TransfersTable = "Transfers";
    internal const string AccountTotalsCheck = "CK_Accounts_Totals";
    internal const string AccountMinBalanceCheck = "CK_Accounts_MinBalance";
    internal const string TransferAmountCheck = "CK_Transfers_Amount";
    internal const string TransferAccountsCheck = "CK_Transfers_Accounts";
    internal const string TransferTypeCheck = "CK_Transfers_Type";

    internal static readonly IReadOnlyList<string> AccountChecks = [AccountTotalsCheck, AccountMinBalanceCheck];
    internal static readonly IReadOnlyList<string> TransferChecks =
        [TransferAmountCheck, TransferAccountsCheck, TransferTypeCheck];
    internal static readonly IReadOnlyList<string> Checks = [.. AccountChecks, .. TransferChecks];

    private const int AmountPrecision = 28;

    // The database writes the time in UTC; SQL Server keeps no kind with it, so it is said when it is read.
    private static readonly Expression<Func<DateTime, DateTime>> ToUtc = x => x;
    private static readonly Expression<Func<DateTime, DateTime>> AsUtc = x => DateTime.SpecifyKind(x, DateTimeKind.Utc);

    internal DbSet<WalletAccount> Accounts => Set<WalletAccount>();
    internal DbSet<WalletTransfer> Transfers => Set<WalletTransfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dialect = WalletDialect.For(Database);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<WalletAccount>(entity => {
            entity.ToTable(AccountsTable, table => {
                table.HasCheckConstraint(AccountTotalsCheck,
                    "\"DebitsPending\" >= 0 AND \"DebitsPosted\" >= 0 AND \"CreditsPending\" >= 0 AND \"CreditsPosted\" >= 0");
                // the database's own hold on a limit, beside WalletService's check
                table.HasCheckConstraint(AccountMinBalanceCheck,
                    "\"MinBalance\" IS NULL OR \"CreditsPosted\" - \"DebitsPosted\" - \"DebitsPending\" >= \"MinBalance\"");
            });
            entity.HasKey(x => x.AccountId);
            entity.Property(x => x.OwnerId).HasMaxLength(WalletService.MaxOwnerIdLength).UseCollation(dialect.TextCollation);
            entity.Property(x => x.Code).HasMaxLength(WalletService.MaxCodeLength).UseCollation(dialect.TextCollation);
            entity.Property(x => x.Currency).HasMaxLength(WalletService.MaxCurrencyLength).UseCollation(dialect.TextCollation);
            entity.Property(x => x.MinBalance).HasPrecision(AmountPrecision, WalletService.AmountScale);
            entity.Property(x => x.DebitsPending).HasPrecision(AmountPrecision, WalletService.AmountScale).HasDefaultValue(0m);
            entity.Property(x => x.DebitsPosted).HasPrecision(AmountPrecision, WalletService.AmountScale).HasDefaultValue(0m);
            entity.Property(x => x.CreditsPending).HasPrecision(AmountPrecision, WalletService.AmountScale).HasDefaultValue(0m);
            entity.Property(x => x.CreditsPosted).HasPrecision(AmountPrecision, WalletService.AmountScale).HasDefaultValue(0m);
            entity.Property(x => x.CreatedTime).HasDefaultValueSql(dialect.UtcNowSql).HasConversion(ToUtc, AsUtc);
            entity.HasIndex(x => new { x.LedgerId, x.OwnerId, x.Code, x.Currency }).IsUnique();

            // a transfer names its accounts with its own ledger and currency, so the database itself keeps money in
            // one ledger and one currency
            entity.HasAlternateKey(x => new { x.LedgerId, x.Currency, x.AccountId });
        });

        modelBuilder.Entity<WalletTransfer>(entity => {
            entity.ToTable(TransfersTable, table => {
                table.HasCheckConstraint(TransferAmountCheck, "\"Amount\" > 0");
                table.HasCheckConstraint(TransferAccountsCheck, "\"DebitAccountId\" <> \"CreditAccountId\"");
                table.HasCheckConstraint(TransferTypeCheck,
                    "(\"Type\" IN ('Posted', 'Pending') AND \"Key\" IS NOT NULL AND \"PendingTransferId\" IS NULL) OR " +
                    "(\"Type\" IN ('PostPending', 'VoidPending') AND \"Key\" IS NULL AND \"PendingTransferId\" IS NOT NULL)");
            });
            entity.HasKey(x => x.TransferId);
            entity.Property(x => x.Currency).HasMaxLength(WalletService.MaxCurrencyLength).UseCollation(dialect.TextCollation);
            entity.Property(x => x.Type).HasConversion<string>().HasMaxLength(20).UseCollation(dialect.TextCollation);
            entity.Property(x => x.Key).HasMaxLength(WalletService.MaxKeyLength).UseCollation(dialect.TextCollation);
            entity.Property(x => x.Code).HasMaxLength(WalletService.MaxCodeLength).UseCollation(dialect.TextCollation);
            entity.Property(x => x.Amount).HasPrecision(AmountPrecision, WalletService.AmountScale);
            entity.Property(x => x.CreatedTime).HasDefaultValueSql(dialect.UtcNowSql).HasConversion(ToUtc, AsUtc);

            // A key may be missing (a post or a void has none) and is unique where it is there: PostgreSQL leaves
            // missing values out of a unique index, and EF gives SQL Server's index a filter that does.
            entity.HasIndex(x => new { x.LedgerId, x.Key }).IsUnique();
            // one post or void for each pending transfer
            entity.HasIndex(x => x.PendingTransferId).IsUnique();
            entity.HasIndex(x => new { x.DebitAccountId, x.TransferId });
            entity.HasIndex(x => new { x.CreditAccountId, x.TransferId });

            entity.HasOne<WalletAccount>().WithMany()
                .HasForeignKey(x => new { x.LedgerId, x.Currency, x.DebitAccountId })
                .HasPrincipalKey(x => new { x.LedgerId, x.Currency, x.AccountId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<WalletAccount>().WithMany()
                .HasForeignKey(x => new { x.LedgerId, x.Currency, x.CreditAccountId })
                .HasPrincipalKey(x => new { x.LedgerId, x.Currency, x.AccountId })
                .OnDelete(DeleteBehavior.Restrict);

            // a post or a void settles a transfer of its own ledger
            entity.HasAlternateKey(x => new { x.LedgerId, x.TransferId });
            entity.HasOne<WalletTransfer>().WithMany()
                .HasForeignKey(x => new { x.LedgerId, x.PendingTransferId })
                .HasPrincipalKey(x => new { x.LedgerId, x.TransferId })
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
