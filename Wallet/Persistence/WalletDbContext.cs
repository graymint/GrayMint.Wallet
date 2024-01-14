using EWallet.Models;
using Microsoft.EntityFrameworkCore;

namespace EWallet.Persistence;

public class WalletDbContext : DbContext
{
    public const string Schema = "dbo";

    public WalletDbContext(DbContextOptions options) : base(options)
    {

    }

    public DbSet<AppModel> Apps { get; set; } = default!;
    public DbSet<CurrencyModel> Currencies { get; set; } = default!;
    public DbSet<OrderModel> Orders { get; set; } = default!;
    public DbSet<WalletTransactionModel> WalletTransactions { get; set; } = default!;
    public DbSet<TransactionTypeLookup> TransactionTypes { get; set; } = default!;
    public DbSet<WalletBalanceModel> WalletBalances { get; set; } = default!;
    public DbSet<WalletModel> Wallets { get; set; } = default!;
    public DbSet<OrderItemModel> OrderItems { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AppModel>(entity =>
        {
            entity.HasKey(x => x.AppId);
            entity.Property(a => a.AppId).ValueGeneratedOnAdd();

            entity.HasOne(e => e.SystemWallet)
                .WithMany()
                .HasForeignKey(e => e.SystemWalletId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<CurrencyModel>(entity =>
        {
            entity.HasKey(x => x.CurrencyId);
            entity.Property(a => a.CurrencyId).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<OrderItemModel>(entity =>
        {
            entity.HasKey(x => x.OrderItemId);
            entity.Property(a => a.OrderItemId).ValueGeneratedOnAdd();
        });

        modelBuilder.Entity<OrderModel>(entity =>
        {
            entity.HasKey(x => x.OrderId);
            entity.Property(x => x.OrderId).ValueGeneratedOnAdd();

            entity.HasIndex(x => new { x.OrderReferenceNumber, x.AppId }).IsUnique();

            entity.Property(e => e.TransactionType)
                .HasColumnName(nameof(TransactionTypeLookup.TransactionTypeId));

            entity.HasIndex(e => new { e.AppId, e.CreatedTime })
                .IncludeProperties(x => x.OrderTypeId);

            entity.HasOne(e => e.TransactionTypeLookup)
                .WithMany(e => e.OrderModels)
                .HasForeignKey(e => e.TransactionType)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Currency)
                .WithMany(e => e.Orders)
                .HasForeignKey(e => e.CurrencyId)
                .OnDelete(DeleteBehavior.NoAction);

        });

        modelBuilder.Entity<WalletTransactionModel>(entity =>
        {
            entity.HasKey(x => x.WalletTransactionId);
            entity.Property(x => x.WalletTransactionId).ValueGeneratedNever();
            entity.Property(x => x.Balance).HasPrecision(23, 4);
            entity.Property(x => x.Amount).HasPrecision(23, 4);

            entity.HasOne(w => w.ReferenceWalletTransaction)
                .WithOne()
                .HasForeignKey<WalletTransactionModel>(w => w.WalletTransactionReferenceId);

            entity.HasOne(e => e.Wallet)
                .WithMany(x => x.OrderTransactions)
                .HasForeignKey(e => e.WalletId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.ReceiverWallet)
                .WithMany()
                .HasForeignKey(e => e.ReceiverWalletId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TransactionTypeLookup>(entity =>
        {
            entity.HasKey(e => e.TransactionTypeId);
            entity.Property(e => e.WalletTransferTypeName).HasMaxLength(50);
        });

        modelBuilder.Entity<WalletBalanceModel>(entity =>
        {
            entity.HasKey(e => e.WalletBalanceId);
            entity.Property(a => a.WalletBalanceId).ValueGeneratedOnAdd();

            entity.HasIndex(x => new { x.WalletId, x.CurrencyId }).IsUnique();
            entity.Property(x => x.Balance).HasPrecision(23, 4);
            entity.Property(x => x.MinBalance).HasPrecision(23, 4);

            entity.HasOne(e => e.Wallet)
                .WithMany(x => x.WalletBalances)
                .HasForeignKey(e => e.WalletId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(e => e.Currency)
                .WithMany(x => x.WalletBalances)
                .HasForeignKey(e => e.CurrencyId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<WalletModel>(entity =>
        {
            entity.Property(w => w.WalletId).ValueGeneratedOnAdd();
            entity.HasKey(w => w.WalletId);
        });


    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        configurationBuilder.Properties<string>()
            .HaveMaxLength(4000);

        configurationBuilder.Properties<decimal>()
            .HavePrecision(19, 4);
    }
}