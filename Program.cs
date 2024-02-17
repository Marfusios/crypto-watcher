using System;
using System.Text;
using System.Threading.Tasks;
using CryptoWatcher.Background;
using CryptoWatcher.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace CryptoWatcher
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            using var host = CreateHostBuilder(args).Build();

            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            var rule = new Rule("₿ Crypto Watcher ₿");
            AnsiConsole.Write(rule);
            AnsiConsole.WriteLine();

            await host.RunAsync();
        }

        private static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                })
                .ConfigureServices(Configure)
                .UseConsoleLifetime();

        private static void Configure(HostBuilderContext context, IServiceCollection services)
        {
            var config = context.Configuration;

            services.Configure<CryptoWatcherSettings>(config.GetSection("Watcher"), o => o.BindNonPublicProperties = true);
            services.Configure<HostOptions>(option =>
            {
                // wait for graceful exit
                option.ShutdownTimeout = TimeSpan.FromMinutes(1);
            });

            services.AddHostedService<RealtimePriceService>();
            services.AddHostedService<OrderBookL3Service>();
        }
    }
}
