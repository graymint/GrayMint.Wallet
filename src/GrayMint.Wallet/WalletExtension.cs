using System.Data.Common;
using GrayMint.Wallet.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet;

public static class WalletExtension
{
    extension(IServiceCollection services)
    {
        // The wallet's service. The host doesn't start on a database whose wallet tables, or their guards, are
        // missing or off.
        public IServiceCollection AddGrayMintWallet()
        {
            services.AddScoped(serviceProvider => new WalletService(
                serviceProvider.GetRequiredService<WalletDbContext>(),
                serviceProvider.GetService<HostDatabase>()));
            services.AddHostedService<WalletStartup>();
            return services;
        }

        // The wallet's tables on a connection of their own, such as in a database of their own.
        public IServiceCollection AddGrayMintWalletDb(Action<DbContextOptionsBuilder> dbOptionsAction)
        {
            services.AddDbContext<WalletDbContext>(dbOptionsAction);
            return services;
        }

        // The wallet's tables in the host's database, on the host's own connection: the host's context opens it,
        // its connection interceptors with it, and a call runs in the transaction the host's context has open, so an
        // order and its money commit together. dbOptionsAction gets that connection, as in
        // (connection, options) => options.UseNpgsql(connection).
        public IServiceCollection AddGrayMintWalletDb<THostDbContext>(
            Action<DbConnection, DbContextOptionsBuilder> dbOptionsAction)
            where THostDbContext : DbContext
        {
            services.AddScoped(serviceProvider =>
                new HostDatabase(serviceProvider.GetRequiredService<THostDbContext>().Database));
            services.AddDbContext<WalletDbContext>((serviceProvider, options) => dbOptionsAction(
                serviceProvider.GetRequiredService<HostDatabase>().Database.GetDbConnection(), options));
            return services;
        }
    }

    // Makes the wallet's tables when they are not there and puts their guards on them; it can run any number of
    // times, and needs a login that may make tables. On a connection of its own it makes the database too.
    public static async Task<IServiceProvider> UseGrayMintWallet(this IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        await WalletSetup.Run(
            scope.ServiceProvider.GetRequiredService<WalletDbContext>(),
            scope.ServiceProvider.GetService<HostDatabase>(),
            cancellationToken);
        return serviceProvider;
    }
}
