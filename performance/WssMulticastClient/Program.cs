﻿using NDesk.Options;
using NetCoreServer;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;

namespace WssMulticastClient
{
    class MulticastClient : WssClient
    {
        public MulticastClient(SslContext context, string address, int port) : base(context, address, port) {}

        public override void OnWsConnecting(HttpRequest request)
        {
            request.SetBegin("GET", "/");
            request.SetHeader("Host", "localhost");
            request.SetHeader("Origin", "http://localhost");
            request.SetHeader("Upgrade", "websocket");
            request.SetHeader("Connection", "Upgrade");
            request.SetHeader("Sec-WebSocket-Key", Convert.ToBase64String(WsNonce));
            request.SetHeader("Sec-WebSocket-Protocol", "chat, superchat");
            request.SetHeader("Sec-WebSocket-Version", "13");
            request.SetBody();
        }

        public override void OnWsReceived(byte[] buffer, long offset, long size)
        {
            Program.TotalBytes += size;
        }

        protected override void OnError(SocketError error)
        {
            Console.WriteLine($"Client caught an error with code {error}");
            Program.TotalErrors++;
        }
    }

    class Program
    {
        public static byte[] MessageToSend;
        public static DateTime TimestampStart = DateTime.UtcNow;
        public static DateTime TimestampStop = DateTime.UtcNow;
        public static long TotalErrors;
        public static long TotalBytes;
        public static long TotalMessages;

        static void Main(string[] args)
        {
            bool help = false;
            string address = "127.0.0.1";
            int port = 8443;
            int clients = 100;
            int size = 32;
            int seconds = 10;

            var options = new OptionSet()
            {
                { "h|?|help",   v => help = v != null },
                { "a|address=", v => address = v },
                { "p|port=", v => port = int.Parse(v) },
                { "c|clients=", v => clients = int.Parse(v) },
                { "s|size=", v => size = int.Parse(v) },
                { "z|seconds=", v => seconds = int.Parse(v) }
            };

            try
            {
                options.Parse(args);
            }
            catch (OptionException e)
            {
                Console.Write("Command line error: ");
                Console.WriteLine(e.Message);
                Console.WriteLine("Try `--help' to get usage information.");
                return;
            }

            if (help)
            {
                Console.WriteLine("Usage:");
                options.WriteOptionDescriptions(Console.Out);
                return;
            }

            Console.WriteLine($"Server address: {address}");
            Console.WriteLine($"Server port: {port}");
            Console.WriteLine($"Working clients: {clients}");
            Console.WriteLine($"Message size: {size}");
            Console.WriteLine($"Seconds to benchmarking: {seconds}");

            Console.WriteLine();

            // Prepare a message to send
            MessageToSend = new byte[size];

            // Create and prepare a new SSL client context
            var context = new SslContext(SslProtocols.Tls13, new X509Certificate2("client.pfx", "qwerty"), (sender, certificate, chain, sslPolicyErrors) => true);

            // Create multicast clients
            var multicastClients = new List<MulticastClient>();
            for (int i = 0; i < clients; i++)
            {
                var client = new MulticastClient(context, address, port);
                // client.OptionNoDelay = true;
                multicastClients.Add(client);
            }

            TimestampStart = DateTime.UtcNow;

            // Connect clients
            Console.Write("Clients connecting...");
            foreach (var client in multicastClients)
                client.ConnectAsync();
            Console.WriteLine("Done!");
            foreach (var client in multicastClients)
                while (!client.IsConnected)
                    Thread.Yield();
            Console.WriteLine("All clients connected!");

            // Wait for benchmarking
            Console.Write("Benchmarking...");
            Thread.Sleep(seconds * 1000);
            Console.WriteLine("Done!");

            // Disconnect clients
            Console.Write("Clients disconnecting...");
            foreach (var client in multicastClients)
                client.CloseAsync(100);
            Console.WriteLine("Done!");
            foreach (var client in multicastClients)
                while (client.IsConnected)
                    Thread.Yield();
            Console.WriteLine("All clients disconnected!");

            TimestampStop = DateTime.UtcNow;

            Console.WriteLine();

            Console.WriteLine($"Errors: {TotalErrors}");

            Console.WriteLine();

            TotalMessages = TotalBytes / size;

            Console.WriteLine($"Total time: {Utilities.GenerateTimePeriod((TimestampStop - TimestampStart).TotalMilliseconds)}");
            Console.WriteLine($"Total data: {Utilities.GenerateDataSize(TotalBytes)}");
            Console.WriteLine($"Total messages: {TotalMessages}");
            Console.WriteLine($"Data throughput: {Utilities.GenerateDataSize((long)(TotalBytes / (TimestampStop - TimestampStart).TotalSeconds))}/s");
            if (TotalMessages > 0)
            {
                Console.WriteLine($"Message latency: {Utilities.GenerateTimePeriod((TimestampStop - TimestampStart).TotalMilliseconds / TotalMessages)}");
                Console.WriteLine($"Message throughput: {(long)(TotalMessages / (TimestampStop - TimestampStart).TotalSeconds)} msg/s");
            }
        }
    }
}
