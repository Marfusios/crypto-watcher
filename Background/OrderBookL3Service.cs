using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bitfinex.Client.Websocket;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Websockets;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.OrderBooks.SourcesL3;
using CryptoWatcher.Configuration;
using CryptoWatcher.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Websocket.Client;

namespace CryptoWatcher.Background
{
    public class OrderBookL3Service : BackgroundService
    {
        private readonly CryptoWatcherSettings _settings;

        public OrderBookL3Service(IOptions<CryptoWatcherSettings> settings)
        {
            _settings = settings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_settings.Mode != CryptoWatcherMode.L3)
                return;

            // only Bitfinex currently supports L3 order book
            var exchange = "bitfinex";
            var symbol = "BTCUSD";
            var topLevelsCount = 20; // how many levels will be displayed

            CryptoOrderBook orderBook = null;
            await StartExchange(exchange, new[] { symbol }, book => orderBook = book, stoppingToken);

            await AnsiConsole.Live(Text.Empty)
                .StartAsync(async ctx =>
                {
                    AnsiConsole.MarkupLine("[lightgoldenrod2]Waiting for order change...[/]");
                    orderBook.OrderBookUpdatedStream.Subscribe(_ => DisplayOrderBook(orderBook, topLevelsCount, ctx));
                    await stoppingToken;
                });
        }

        private void DisplayOrderBook(CryptoOrderBook ob, int levelsCount, LiveDisplayContext ctx)
        {
            var bids = ob.BidLevels.Take(levelsCount).ToArray();
            var asks = ob.AskLevels.Take(levelsCount).ToArray();

            var asksOrdered = asks.OrderByDescending(x => x.Price).ToArray();
            var bidsOrdered = bids.OrderByDescending(x => x.Price).ToArray();

            var table = new Table()
                .Title($"L3 Order Book ([bold deepskyblue2]{ob.ExchangeName.ToUpper()}[/] {ob.TargetPairOriginal})", new Style(Color.LightGoldenrod2))
                .Border(TableBorder.Rounded)
                .AddColumn("#", t => t.Alignment = Justify.Left)
                .AddColumn("Price", t => t.Alignment = Justify.Right)
                .AddColumn("Amount", t => t.Alignment = Justify.Right)
                .AddColumn("Total", t => t.Alignment = Justify.Right)
                .AddColumn("Distance", t => t.Alignment = Justify.Right)
                .AddColumn("Price Updated", t => t.Alignment = Justify.Right)
                .AddColumn("Amount Updated", t => t.Alignment = Justify.Right)
                .AddColumn("Amount Changed", t => t.Alignment = Justify.Right)
                ;

            var totalAsks = asksOrdered.Length;
            var counter = 0;

            var mid = ob.MidPrice;

            foreach (var ask in asksOrdered)
            {
                var index = totalAsks - counter;
                var total = asksOrdered[counter..totalAsks].Sum(x => x.Amount);
                var dis = (1 - mid / ask.Price) * 100;

                table.AddRow(
                    $"[bold indianred_1]{index}[/]",
                    $"{ask.Price:0.00}",
                    $"{ask.Amount:0.0000}",
                    $"{total:0.0000}",
                    $"{dis:#.0000}%",
                    $"{ask.PriceUpdatedCount:#}",
                    $"{ask.AmountUpdatedCount:#}",
                    $"{ask.AmountDifferenceAggregated:#.####} {GetDifferenceSign(ask.AmountDifferenceAggregated)}"
                );
                counter++;
            }

            table.AddRow(string.Empty);

            counter = 0;

            foreach (var bid in bidsOrdered)
            {
                counter++;
                var total = bidsOrdered[..counter].Sum(x => x.Amount);
                var dis =   (mid / bid.Price - 1) * 100;

                table.AddRow(
                    $"[bold darkseagreen3_1]{counter}[/]",
                    $"{bid.Price:0.00}",
                    $"{bid.Amount:0.0000}",
                    $"{total:0.0000}",
                    $"{dis:#.0000}%",
                    $"{bid.PriceUpdatedCount:#}",
                    $"{bid.AmountUpdatedCount:#}",
                    $"{bid.AmountDifferenceAggregated:#.####} {GetDifferenceSign(bid.AmountDifferenceAggregated)}"
                );
            }

            ctx.UpdateTarget(table);
        }

        private string GetDifferenceSign(double diff) => diff switch
        {
            > 0 => "[green]⬆[/]",
            < -0 => "[red]⬇[/]",
            _ => "[yellow]┅[/]"
        };

        private static Task StartExchange(string exchange, string[] pairs, Action<CryptoOrderBook> initialized, CancellationToken stoppingToken)
        {
            var exchangeSafe = (exchange ?? string.Empty).ToLower();
            return exchangeSafe switch
            {
                "bitfinex" => StartBitfinex(pairs, initialized, stoppingToken),
                _ => throw new InvalidOperationException($"Unsupported exchange: '{exchange}'")
            };
        }

        private static async Task InitOrderBooks(string[] pairs, Action<CryptoOrderBook> initialized, CancellationToken stoppingToken,
            IWebsocketClient client, OrderBookSourceBase source)
        {
            foreach (var pair in pairs)
            {
                var orderBook = new CryptoOrderBook(pair, source, CryptoOrderBookType.L3);
                initialized(orderBook);
            }

            stoppingToken.Register(client.Dispose);

            await client.Start();
        }

        private static async Task StartBitfinex(string[] pairs, Action<CryptoOrderBook> initialized, CancellationToken stoppingToken)
        {
            var url = BitfinexValues.ApiWebsocketUrl;
            var communicator = new BitfinexWebsocketCommunicator(url) { Name = "Bitfinex" };
            var client = new BitfinexWebsocketClient(communicator);
            var source = new BitfinexOrderBookL3Source(client);

            await InitOrderBooks(pairs, initialized, stoppingToken, communicator, source);

            foreach (var pair in pairs)
            {
                // Send subscription request to raw order book data
                client.Send(new Bitfinex.Client.Websocket.Requests.Subscriptions.RawBookSubscribeRequest(pair, "100"));
            }
        }
    }
}
