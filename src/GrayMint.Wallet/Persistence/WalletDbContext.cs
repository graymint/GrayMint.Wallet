using System.Linq.Expressions;
using GrayMint.Wallet.Persistence.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GrayMint.Wallet.Persistence;

// The wallet's own tables, in a schema of their own beside the host's. It has no migrations: the setup makes the
// tables from this model (WalletSetup), and WalletSchema.Version says which shape they have.
internal sealed class WalletDbContext(DbContextOptions<WalletDbContext> options) : DbContext(options)
{
    public const string WalletTotalsCheck = "CK_Wallets_Totals";
    public const string WalletMinAvailableBalanceCheck = "CK_Wallets_MinAvailableBalance";
    public const string TransferAmountCheck = "CK_Transfers_Amount";
    public const string TransferWalletsCheck = "CK_Transfers_Wallets";
    public const string TransferTypeCheck = "CK_Transfers_Type";
    public const string MetadataOneRowCheck = "CK_Metadata_OneRow";

    // the key of the metadata's one row
    public const int MetadataRowId = 1;

    private const int AmountPrecision = 28;

    // The database writes the time in UTC; SQL Server keeps no kind with it, so it is said when it is read.
    private static readonly Expression<Func<DateTime, DateTime>> ToUtc = x => x;
    private static readonly Expression<Func<DateTime, DateTime>> AsUtc = x => DateTime.SpecifyKind(x, DateTimeKind.Utc);

    public DbSet<WalletModel> Wallets => Set<WalletModel>();
    public DbSet<TransferModel> Transfers => Set<TransferModel>();
    public DbSet<WalletTransactionModel> WalletTransactions => Set<WalletTransactionModel>();
    public DbSet<MetadataModel> Metadata => Set<MetadataModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var dialect = WalletDialect.For(Database);
        modelBuilder.HasDefaultSchema(WalletSchema.Name);
        // the database the setup makes keeps text as UTF-8, which a varchar parameter needs on SQL Server
        modelBuilder.UseCollation(dialect.DatabaseCollation);
        // SQL Server names a default by its table and column (DF_Wallets_PostedIn), not at random as it does
        // unasked, so a setup step can drop or change it by name, as SQL Server asks before a column of it changes
        modelBuilder.Model.UseNamedDefaultConstraints(true);

        modelBuilder.Entity<WalletModel>(entity => {
            entity.ToTable(WalletSchema.WalletsTable, table => {
                table.HasCheckConstraint(WalletTotalsCheck,
                    "\"PendingOut\" >= 0 AND \"PostedOut\" >= 0 AND \"PendingIn\" >= 0 AND \"PostedIn\" >= 0");

                // the database's own hold on a limit, beside the one WalletChange checks
                table.HasCheckConstraint(WalletMinAvailableBalanceCheck,
                    "\"MinAvailableBalance\" IS NULL OR " +
                    "\"PostedIn\" - \"PostedOut\" - \"PendingOut\" >= \"MinAvailableBalance\"");
            });

            entity.HasKey(x => x.WalletId);
            Text(entity.Property(x => x.OwnerReferenceKey), WalletLimits.MaxOwnerReferenceKeyLength);
            Text(entity.Property(x => x.WalletCode), WalletLimits.MaxCodeLength);
            Text(entity.Property(x => x.Currency), WalletLimits.MaxCurrencyLength);
            entity.Property(x => x.MinAvailableBalance).HasPrecision(AmountPrecision, WalletLimits.AmountScale);
            entity.Property(x => x.PostedIn).HasPrecision(AmountPrecision, WalletLimits.AmountScale).HasDefaultValue(0m);
            entity.Property(x => x.PostedOut).HasPrecision(AmountPrecision, WalletLimits.AmountScale).HasDefaultValue(0m);
            entity.Property(x => x.PendingIn).HasPrecision(AmountPrecision, WalletLimits.AmountScale).HasDefaultValue(0m);
            entity.Property(x => x.PendingOut).HasPrecision(AmountPrecision, WalletLimits.AmountScale).HasDefaultValue(0m);
            entity.Property(x => x.CreatedTime).HasDefaultValueSql(dialect.UtcNowSql).HasConversion(ToUtc, AsUtc);
            entity.HasIndex(x => new { x.TenantId, x.OwnerReferenceKey, x.WalletCode, x.Currency }).IsUnique();

            // a transfer names its wallets with its own tenant and currency, so the database itself keeps money in
            // one tenant and one currency; a wallet transaction names its wallet with its own tenant
            entity.HasAlternateKey(x => new { x.TenantId, x.Currency, x.WalletId });
            entity.HasAlternateKey(x => new { x.TenantId, x.WalletId });
        });

        modelBuilder.Entity<TransferModel>(entity => {
            entity.ToTable(WalletSchema.TransfersTable, table => {
                table.HasCheckConstraint(TransferAmountCheck, "\"Amount\" > 0");
                table.HasCheckConstraint(TransferWalletsCheck, "\"FromWalletId\" <> \"ToWalletId\"");
                table.HasCheckConstraint(TransferTypeCheck,
                    "(\"Type\" IN ('Posted', 'Pending') AND \"ExternalReferenceKey\" IS NOT NULL " +
                    "AND \"PendingTransferId\" IS NULL) OR " +
                    "(\"Type\" IN ('PostPending', 'VoidPending') AND \"ExternalReferenceKey\" IS NULL " +
                    "AND \"PendingTransferId\" IS NOT NULL)");
            });

            entity.HasKey(x => x.TransferId);
            Text(entity.Property(x => x.Currency), WalletLimits.MaxCurrencyLength);
            Text(entity.Property(x => x.Type).HasConversion<string>(), 20);
            Text(entity.Property(x => x.ExternalReferenceKey), WalletLimits.MaxExternalReferenceKeyLength);
            Text(entity.Property(x => x.PurposeCode), WalletLimits.MaxCodeLength);
            entity.Property(x => x.Amount).HasPrecision(AmountPrecision, WalletLimits.AmountScale);
            entity.Property(x => x.CreatedTime).HasDefaultValueSql(dialect.UtcNowSql).HasConversion(ToUtc, AsUtc);

            // A key may be missing (a post or a void has none) and is unique where it is there: PostgreSQL leaves
            // missing values out of a unique index, and EF gives SQL Server's index a filter that does.
            entity.HasIndex(x => new { x.TenantId, x.ExternalReferenceKey }).IsUnique();
            // one post or void for each pending transfer
            entity.HasIndex(x => x.PendingTransferId).IsUnique();
            entity.HasIndex(x => new { x.FromWalletId, x.TransferId });
            entity.HasIndex(x => new { x.ToWalletId, x.TransferId });

            entity.HasOne<WalletModel>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.Currency, x.FromWalletId })
                .HasPrincipalKey(x => new { x.TenantId, x.Currency, x.WalletId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<WalletModel>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.Currency, x.ToWalletId })
                .HasPrincipalKey(x => new { x.TenantId, x.Currency, x.WalletId })
                .OnDelete(DeleteBehavior.Restrict);

            // a post or a void settles a transfer of its own tenant
            entity.HasAlternateKey(x => new { x.TenantId, x.TransferId });
            entity.HasOne<TransferModel>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.PendingTransferId })
                .HasPrincipalKey(x => new { x.TenantId, x.TransferId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<WalletTransactionModel>(entity => {
            entity.ToTable(WalletSchema.WalletTransactionsTable);
            // A transfer moves money between two different wallets, so it has one wallet transaction on each;
            // keyed by the wallet first, a wallet's statement is one range of the key, in the order of its
            // transfers.
            entity.HasKey(x => new { x.WalletId, x.TransferId });
            entity.Property(x => x.Amount).HasPrecision(AmountPrecision, WalletLimits.AmountScale);
            entity.Property(x => x.PostedBalance).HasPrecision(AmountPrecision, WalletLimits.AmountScale);
            entity.Property(x => x.AvailableBalance).HasPrecision(AmountPrecision, WalletLimits.AmountScale);

            // a wallet transaction belongs to a transfer and a wallet of its own tenant
            entity.HasOne<TransferModel>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.TransferId })
                .HasPrincipalKey(x => new { x.TenantId, x.TransferId })
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne<WalletModel>().WithMany()
                .HasForeignKey(x => new { x.TenantId, x.WalletId })
                .HasPrincipalKey(x => new { x.TenantId, x.WalletId })
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MetadataModel>(entity => {
            // one row, so the start reads one version, whatever wrote to the table
            entity.ToTable(WalletSchema.MetadataTable, table =>
                table.HasCheckConstraint(MetadataOneRowCheck, $"\"MetadataId\" = {MetadataRowId}"));

            entity.HasKey(x => x.MetadataId);
            entity.Property(x => x.MetadataId).ValueGeneratedNever();
        });

        return;

        // Every text column through here: UTF-8 on both databases, a byte a character of ASCII, and two texts one only
        // when every character is the same, its case too.
        void Text(PropertyBuilder property, int maxLength)
        {
            property.HasMaxLength(dialect.TextLength(maxLength)).IsUnicode(false).UseCollation(dialect.TextCollation);
        }
    }
}
