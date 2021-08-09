using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance.Client.Websocket;
using Binance.Client.Websocket.Client;
using Binance.Client.Websocket.Subscriptions;
using Binance.Client.Websocket.Websockets;
using Bitfinex.Client.Websocket;
using Bitfinex.Client.Websocket.Client;
using Bitfinex.Client.Websocket.Utils;
using Bitfinex.Client.Websocket.Websockets;
using Bitmex.Client.Websocket;
using Bitmex.Client.Websocket.Client;
using Bitmex.Client.Websocket.Websockets;
using Bitstamp.Client.Websocket;
using Bitstamp.Client.Websocket.Client;
using Bitstamp.Client.Websocket.Communicator;
using Coinbase.Client.Websocket;
using Coinbase.Client.Websocket.Channels;
using Coinbase.Client.Websocket.Client;
using Coinbase.Client.Websocket.Communicator;
using Coinbase.Client.Websocket.Requests;
using Crypto.Websocket.Extensions.Core.OrderBooks;
using Crypto.Websocket.Extensions.Core.OrderBooks.Models;
using Crypto.Websocket.Extensions.Core.OrderBooks.Sources;
using Crypto.Websocket.Extensions.OrderBooks.Sources;
using CryptoWatcher.Configuration;
using CryptoWatcher.Utils;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Websocket.Client;
using Channel = Bitstamp.Client.Websocket.Channels.Channel;

namespace CryptoWatcher.Background
{
    public class RealtimePriceService : BackgroundService
    {
        private readonly CryptoWatcherSettings _settings;

        public RealtimePriceService(IOptions<CryptoWatcherSettings> settings)
        {
            _settings = settings.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_settings.Mode != CryptoWatcherMode.PriceChanges)
                return;

            var streams = new List<IObservable<IOrderBookChangeInfo>>();

            await AnsiConsole
                .Status()
                .StartAsync("Connecting to cryptocurrency exchanges", async ctx =>
                {
                    var markets = _settings.Markets.OrderBy(x => x.Key).ToArray();
                    var count = 1;
                    var totalCount = markets.Length;

                    foreach (var market in markets)
                    {
                        var exchange = market.Key;
                        var pairs = market.Value;

                        ctx.Status($"Connecting to {exchange} (#{count} of {totalCount})");

                        await StartExchange(exchange, pairs, book => streams.Add(book.BidAskUpdatedStream), stoppingToken);

                        ctx.Status($"Subscribed to {exchange} (#{count} of {totalCount})");
                        count += 1;
                    }
                });

            await AnsiConsole.Live(Text.Empty)
                .StartAsync(async ctx =>
                {
                    AnsiConsole.MarkupLine("[lightgoldenrod2]Waiting for price change...[/]");
                    streams.CombineLatest().Subscribe(changes => DisplayQuotes(changes, ctx));
                    await stoppingToken;
                });
        }

        private void DisplayQuotes(IList<IOrderBookChangeInfo> changes, LiveDisplayContext ctx)
        {
            var average = changes.Average(x => x.Quotes.Mid);

            var table = new Table()
                .Title("🤑 Realtime Price", new Style(Color.LightGoldenrod2))
                .Border(TableBorder.Rounded)
                .AddColumn("Exchange", t => t.Alignment = Justify.Left)
                .AddColumn("Symbol", t => t.Alignment = Justify.Center)
                .AddColumn("Current Price", t => t.Alignment = Justify.Right)
                .AddColumn($"Diff from Avg ([bold gold3_1]{average:0.00}[/])", t => t.Alignment = Justify.Right)
                ;

            var orderedChanges = changes.OrderBy(x => x.ExchangeName).ToArray();
            foreach (var info in orderedChanges)
            {
                var quote = info.Quotes;
                var diff = quote.Mid - average;
                var diffSign = GetDifferenceSign(diff);

                table.AddRow(
                    $"[bold deepskyblue2]{info.ExchangeName.ToUpper()}[/]",
                    $"{info.PairOriginal}",
                    $"{quote.Mid:0.00}",
                    $"{diff:0.00} {diffSign}"
                );
            }

            ctx.UpdateTarget(table);
        }

        private string GetDifferenceSign(double diff) => diff switch
        {
            > 0.01 => "[green]⬆[/]",
            < -0.01 => "[red]⬇[/]",
            _ => "[yellow]┅[/]"
        };

        private static Task StartExchange(string exchange, string[] pairs, Action<ICryptoOrderBook> initialized, CancellationToken stoppingToken)
        {
            var exchangeSafe = (exchange ?? string.Empty).ToLower();
            return exchangeSafe switch
            {
                "bitmex" => StartBitmex(pairs, initialized, stoppingToken),
                "bitfinex" => StartBitfinex(pairs, initialized, stoppingToken),
                "binance" => StartBinance(pairs, initialized, stoppingToken),
                "coinbase" => StartCoinbase(pairs, initialized, stoppingToken),
                "bitstamp" => StartBitstamp(pairs, initialized, stoppingToken),
                _ => throw new InvalidOperationException($"Unsupported exchange: '{exchange}'")
            };
        }

        private static async Task InitOrderBooks(string[] pairs, Action<ICryptoOrderBook> initialized, CancellationToken stoppingToken,
            IWebsocketClient client, OrderBookSourceBase source)
        {
            foreach (var pair in pairs)
            {
                var orderBook = new CryptoOrderBookL2(pair, source);
                initialized(orderBook);
            }

            stoppingToken.Register(client.Dispose);

            await client.Start();
        }

        private static async Task StartBitmex(string[] pairs, Action<ICryptoOrderBook> initialized, CancellationToken stoppingToken)
        {
            var url = BitmexValues.ApiWebsocketUrl;
            var communicator = new BitmexWebsocketCommunicator(url) { Name = "Bitmex" };
            var client = new BitmexWebsocketClient(communicator);
            var source = new BitmexOrderBookSource(client);

            await InitOrderBooks(pairs, initialized, stoppingToken, communicator, source);

            foreach (var pair in pairs)
            {
                // Send subscription request to order book data
                client.Send(new Bitmex.Client.Websocket.Requests.BookSubscribeRequest(pair));
            }
        }

        private static async Task StartBitfinex(string[] pairs, Action<ICryptoOrderBook> initialized, CancellationToken stoppingToken)
        {
            var url = BitfinexValues.ApiWebsocketUrl;
            var communicator = new BitfinexWebsocketCommunicator(url) { Name = "Bitfinex" };
            var client = new BitfinexWebsocketClient(communicator);
            var source = new BitfinexOrderBookSource(client);

            await InitOrderBooks(pairs, initialized, stoppingToken, communicator, source);

            foreach (var pair in pairs)
            {
                // Send subscription request to order book data
                client.Send(new Bitfinex.Client.Websocket.Requests.Subscriptions.BookSubscribeRequest(pair,
                    BitfinexPrecision.P0, BitfinexFrequency.Realtime, "100"));
            }
        }

        private static async Task StartBinance(string[] pairs, Action<ICryptoOrderBook> initialized, CancellationToken stoppingToken)
        {
            var url = BinanceValues.ApiWebsocketUrl;
            var communicator = new BinanceWebsocketCommunicator(url) { Name = "Binance" };
            var client = new BinanceWebsocketClient(communicator);

            SubscriptionBase[] subscriptions = pairs
                .Select(x => new OrderBookDiffSubscription(x))
                .ToArray();
            client.SetSubscriptions(subscriptions);

            var source = new BinanceOrderBookSource(client);

            await InitOrderBooks(pairs, initialized, stoppingToken, communicator, source);

            foreach (var pair in pairs)
            {
                // Binance is special
                // We need to load snapshot in advance manually via REST call
                await source.LoadSnapshot(communicator, pair);
            }
        }

        private static async Task StartCoinbase(string[] pairs, Action<ICryptoOrderBook> initialized, CancellationToken stoppingToken)
        {
            var url = CoinbaseValues.ApiWebsocketUrl;
            var communicator = new CoinbaseWebsocketCommunicator(url) { Name = "Coinbase" };
            var client = new CoinbaseWebsocketClient(communicator);
            var source = new CoinbaseOrderBookSource(client);

            await InitOrderBooks(pairs, initialized, stoppingToken, communicator, source);

            foreach (var pair in pairs)
            {
                // Send subscription request to order book data
                client.Send(new SubscribeRequest(
                    new[] { pair },
                    ChannelSubscriptionType.Level2
                ));
            }
        }

        private static async Task StartBitstamp(string[] pairs, Action<ICryptoOrderBook> initialized, CancellationToken stoppingToken)
        {
            var url = BitstampValues.ApiWebsocketUrl;
            var communicator = new BitstampWebsocketCommunicator(url) { Name = "Bitstamp" };
            var client = new BitstampWebsocketClient(communicator);
            var source = new BitstampOrderBookSource(client);

            await InitOrderBooks(pairs, initialized, stoppingToken, communicator, source);

            foreach (var pair in pairs)
            {
                // Send subscription request to order book data
                client.Send(new Bitstamp.Client.Websocket.Requests.SubscribeRequest(
                    pair,
                    Channel.OrderBook
                ));
            }
        }
    }
}
