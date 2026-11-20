using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet.Test.Helper;

// a host's own context, as a host writes one: its own table, and nothing of the wallet's
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestOrder> Orders => Set<TestOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // a host on SQL Server makes its database UTF-8, as the wallet's start asks
        if (Database.IsSqlServer())
            modelBuilder.UseCollation("Latin1_General_100_CI_AS_SC_UTF8");

        modelBuilder.Entity<TestOrder>().ToTable("Orders");
    }
}
