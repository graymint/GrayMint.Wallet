using System.Data.Common;
using GrayMint.Wallet.Wallets;
using GrayMint.Wallet.Persistence;
using GrayMint.Wallet.Reconciliation;
using GrayMint.Wallet.Transfers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GrayMint.Wallet;

/// <summary>Registers the wallet in a host, and makes its tables.</summary>
public static class WalletExtension
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the wallet's services, each for a scope: <see cref="WalletService"/>,
        /// <see cref="WalletTransferService"/> and <see cref="WalletReconciliationService"/>. Their context comes from
        /// <c>AddGrayMintWalletDb</c>. The host doesn't start on a database without the wallet's tables, or with tables
        /// of another version.
        /// </summary>
        /// <returns>The services, to register more.</returns>
        public IServiceCollection AddGrayMintWallet()
        {
            services.AddScoped(serviceProvider => new WalletRepo(
                serviceProvider.GetRequiredService<WalletDbContext>(),
                serviceProvider.GetService<HostDatabase>()));

            services.AddScoped(serviceProvider =>
                new WalletService(serviceProvider.GetRequiredService<WalletRepo>()));

            services.AddScoped(serviceProvider =>
                new WalletTransferService(serviceProvider.GetRequiredService<WalletRepo>()));

            services.AddScoped(serviceProvider =>
                new WalletReconciliationService(serviceProvider.GetRequiredService<WalletRepo>()));

            services.AddHostedService<WalletStartup>();
            return services;
        }

        /// <summary>The wallet's tables on a connection of their own, such as in a database of their own.</summary>
        /// <param name="dbOptionsAction">
        /// Names the database, as in <c>options =&gt; options.UseNpgsql(connectionString)</c>.
        /// </param>
        /// <returns>The services, to register more.</returns>
        public IServiceCollection AddGrayMintWalletDb(Action<DbContextOptionsBuilder> dbOptionsAction)
        {
            services.AddDbContext<WalletDbContext>(dbOptionsAction);
            return services;
        }

        /// <summary>
        /// The wallet's tables in the host's database, on the host's own connection: the host's context opens it, its
        /// connection interceptors with it, and a call runs in the database transaction the host's context has open, so
        /// an order and its money commit together.
        /// </summary>
        /// <typeparam name="THostDbContext">The host's context, whose connection the wallet works on.</typeparam>
        /// <param name="dbOptionsAction">
        /// Gets that connection and names its database, as in
        /// <c>(connection, options) =&gt; options.UseNpgsql(connection)</c>.
        /// </param>
        /// <returns>The services, to register more.</returns>
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

    /// <summary>
    /// Makes the wallet's tables when they are not there, and on a connection of its own the database too. It needs a
    /// login that may make tables, and can run any number of times. It refuses a database with only some of the
    /// tables, or with tables of another version, and changes nothing in it.
    /// </summary>
    /// <param name="serviceProvider">The host's services, with the wallet registered.</param>
    /// <param name="cancellationToken">The token that cancels the setup.</param>
    /// <returns>The host's services.</returns>
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
