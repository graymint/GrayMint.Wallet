using Microsoft.EntityFrameworkCore;

namespace GrayMint.Wallet.Test.Helper;

// a host's own context, as a host writes one: its own table, and nothing of the wallet's
public class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestOrder> Orders => Set<TestOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestOrder>().ToTable("Orders");
    }
}
