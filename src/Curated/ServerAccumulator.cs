// ============================================================================
// Project:     SiCADA
// File:        ServerAccumulator.cs
// 
// Summary:
//     Base accumulator class for TCP server-client communication.
//     Manages client connections, disconnections, and data reception. 
//     Implemenst functions for broadcasting data to all connected clients
//     and provides virtual methods for higher level accumulator classes.
//
// Author:          [Marko Lazarević]
// Created:         [2025-10-14]
// File Version:    [1.0.0]
// ============================================================================

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

public static partial class SiCADA
{
    public class ServerAccumulator
    {
        private readonly int _accumulatorPort;

        private TcpListener _listener;
        private readonly ConcurrentBag<TcpClient> _clients = new();
        private CancellationTokenSource _cts;

        private readonly int _clientConnectDelay;
        private readonly int _clientListenerDelay;
        private readonly int _clientDisconnectDelay;

        public ServerAccumulator(
            int accumulatorPort,
            int clientConnectDelay = 100,
            int clientListenerDelay = 50,
            int clientDisconnectDelay = 500)
        {
            _accumulatorPort = accumulatorPort;
            _clientConnectDelay = clientConnectDelay;
            _clientListenerDelay = clientListenerDelay;
            _clientDisconnectDelay = clientDisconnectDelay;

            _serverAccumulators.Add(this);
        }

        public bool Started => _cts != null && !_cts.IsCancellationRequested;

        protected virtual void OnClientConnected(TcpClient client) { }
        protected virtual void OnClientDisconnected(TcpClient client) { }
        protected virtual void OnRecieved(TcpClient client, byte[] data) { }
        protected virtual void OnStart() { }
        protected virtual void OnStop() { }

        public void Broadcast(byte[] data)
        {
            if (!Started) return;

            foreach (var client in _clients)
            {
                try
                {
                    client.GetStream().Write(data, 0, data.Length);
                }
                catch
                {
                    Console.WriteLine($"ServerAccumulator: Client disconnected while broadcasting!");
                }
            }
        }

        public void Start()
        {
            _listener = new TcpListener(IPAddress.Parse(_SiCADAServerIp), _accumulatorPort);
            _listener.Start();

            _cts = new CancellationTokenSource();

            Task.Run(() => ClientConnectLoop(_cts.Token));
            Task.Run(() => ClientDisconnectLoop(_cts.Token));
            Task.Run(() => ClientListenerLoop(_cts.Token));

            OnStart();
            Console.WriteLine($"ServerAccumulator: started!");
        }

        public void Stop()
        {
            _cts.Cancel();
            _listener.Stop();
            OnStop();
            Console.WriteLine($"ServerAccumulator: stopped!");
        }

        private async Task ClientConnectLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_listener.Pending())
                    {
                        var client = _listener.AcceptTcpClient();
                        string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();

                        if (_SiCADAClients.Contains(clientIp))
                        {
                            _clients.Add(client);
                            Console.WriteLine($"ServerAccumulator: client connected ({clientIp}), total {_clients.Count}");
                            OnClientConnected(client);
                        }
                        else
                        {
                            client.Dispose();
                        }
                    }
                }
                catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
                {
                    Console.WriteLine($"ServerAccumulator: exception in ClientConnectLoop: {ex}");
                }

                try { await Task.Delay(_clientConnectDelay, token); } catch { }
            }
        }

        private async Task ClientDisconnectLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var client in _clients.ToArray())
                {
                    if (!IsClientConnected(client))
                    {
                        _clients.TryTake(out var removedClient);
                        string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString();
                        Console.WriteLine($"ServerAccumulator: client disconnected, total clients: {_clients.Count}");
                        removedClient?.Close();
                        OnClientDisconnected(removedClient);
                    }
                }

                try { await Task.Delay(_clientDisconnectDelay, token); } catch { }
            }
        }

        private async Task ClientListenerLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                foreach (var client in _clients)
                {
                    try
                    {
                        if (client.Available > 0)
                        {
                            byte[] buffer = new byte[client.Available];
                            int bytesRead = client.GetStream().Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0) OnRecieved(client, buffer);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"ServerAccumulator: exception in ClientListenerLoop: {ex}");
                    }
                }

                try { await Task.Delay(_clientListenerDelay, token); } catch { }
            }
        }

        private bool IsClientConnected(TcpClient client)
        {
            try
            {
                return !(client.Client.Poll(1, SelectMode.SelectRead) && client.Client.Available == 0);
            }
            catch (SocketException e)
            {
                Console.WriteLine($"ServerCollector: exception in IsClientConnected: {e}");
                return false;
            }
        }
    }
}