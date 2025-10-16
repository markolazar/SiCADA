
using S7.Net;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Xml.Linq;
using static SiCADA;

public static partial class SiCADA
{
    public class S7ServerAccumulator : ServerAccumulator
    {

        private readonly List<byte> _s7Broadcast = new();
        private readonly Plc _s7Plc;
        private readonly List<S7Db> _s7PlcDbs;
        private readonly int _minPlcReadIntMillis;

        private Stopwatch _readBroadcastProcessThreadTimer = Stopwatch.StartNew();
        private Task? _taskReadBroadcastProcess;
        private CancellationTokenSource? _cts;
        private bool _bReadBroadcastProcess;
        private bool _plcConnected;

        public bool PlcConnected => _plcConnected;

        public S7ServerAccumulator(
            int accumulatorPort,
            Plc s7Plc,
            List<S7Db> s7PlcDbs,
            int minPlcReadIntMillis = 50
        ) : base(accumulatorPort)
        {
            _s7Plc = s7Plc ?? throw new ArgumentNullException(nameof(s7Plc));
            _s7PlcDbs = s7PlcDbs ?? throw new ArgumentNullException(nameof(s7PlcDbs));
            _minPlcReadIntMillis = minPlcReadIntMillis;
        }
        protected override void OnStart()
        {
            _bReadBroadcastProcess = true;
            _cts = new CancellationTokenSource();
            _taskReadBroadcastProcess = Task.Run(() => ReadBroadcastProcessLoop(_cts.Token));
        }

        protected override void OnStop()
        {
            _bReadBroadcastProcess = false;
            _cts?.Cancel();

            if (_taskReadBroadcastProcess != null)
            {
                try
                {
                    _taskReadBroadcastProcess.Wait();
                }
                catch (AggregateException ex)
                {
                    foreach (var inner in ex.InnerExceptions)
                    {
                        if (inner is TaskCanceledException) continue;
                        Console.WriteLine(inner);
                    }
                }
            }
        }

        protected override void OnClientConnected(TcpClient client)
        {
            foreach (var s7Db in _s7PlcDbs)
                s7Db.OnClientConnect();
        }

        protected override void OnRecieved(TcpClient client, byte[] buffer)
        {
            var chunkLength = BitConverter.ToInt32(buffer, 0);
            int i = sizeof(int);

            while (i < chunkLength)
            {
                ushort dbNum = BitConverter.ToUInt16(buffer, i);
                i += sizeof(ushort);

                uint bitOffset = BitConverter.ToUInt32(buffer, i);
                i += sizeof(uint);

                byte s7VarBitLength = buffer[i];
                i += sizeof(byte);

                byte[] cvBytes = s7VarBitLength == 1
                    ? new byte[1] { buffer[i] }
                    : buffer.Skip(i).Take(s7VarBitLength / 8).ToArray();

                if (s7VarBitLength == 1)
                {
                    bool cv = (cvBytes[0] & (1 << (int)(bitOffset % 8))) != 0;
                    _s7Plc.WriteBit(DataType.DataBlock, dbNum, (int)(bitOffset / 8), (int)(bitOffset % 8), cv);
                }
                else
                {
                    _s7Plc.WriteBytes(DataType.DataBlock, dbNum, (int)(bitOffset / 8), cvBytes);
                }

                i += s7VarBitLength;
            }

            string clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address.ToString();
            Console.WriteLine($"S7ServerAccumulator: Received data from S7ClientCollector");
        }

        // -----------------------------------------------------------
        // Async loop reading from PLC and broadcasting
        // -----------------------------------------------------------
        private async Task ReadBroadcastProcessLoop(CancellationToken token)
        {
            while (_bReadBroadcastProcess && !token.IsCancellationRequested)
            {
                try
                {
                    _readBroadcastProcessThreadTimer.Restart();

                    if (_s7Plc.IsConnected)
                    {
                        _plcConnected = true;

                        // 1) Read from PLC
                        foreach (var s7Db in _s7PlcDbs)
                        {
                            var bytesRead = _s7Plc.ReadBytes(DataType.DataBlock, s7Db.Num, 0, s7Db.Len);
                            s7Db.SetBytes(bytesRead);
                            s7Db.ParseCVs();
                        }

                        // 2) Prepare broadcast
                        foreach (var s7Db in _s7PlcDbs.Where(d => d.BroadcastFlag))
                        {
                            var dbNumBytes = BitConverter.GetBytes((short)s7Db.Num);
                            var chunkLengthBytes = BitConverter.GetBytes((short)(sizeof(short) + sizeof(short) + s7Db.Bytes.Length));

                            _s7Broadcast.AddRange(chunkLengthBytes);
                            _s7Broadcast.AddRange(dbNumBytes);
                            _s7Broadcast.AddRange(s7Db.Bytes);

                            s7Db.ResetBroadcastFlag();
                        }

                        // 3) Broadcast if needed
                        if (_s7Broadcast.Count > 0)
                        {
                            int broadcastLength = sizeof(int) + _s7Broadcast.Count;
                            _s7Broadcast.InsertRange(0, BitConverter.GetBytes(broadcastLength));

                            Broadcast(_s7Broadcast.ToArray());
                            _s7Broadcast.Clear();
                        }

                        _readBroadcastProcessThreadTimer.Stop();

                        int remainingTime = _minPlcReadIntMillis
                                            - (int)_readBroadcastProcessThreadTimer.ElapsedMilliseconds;

                        if (remainingTime > 0)
                        {
                            try { await Task.Delay(remainingTime, token); }
                            catch (TaskCanceledException) { /* normal on stop */ }
                        }

                        //if (!_firstScan)
                        //{
                        //    _firstScan = true;
                        //    OnFirstScan?.Invoke();
                        //}
                    }
                    else
                    {
                        try
                        {
                            foreach (var s7Db in _s7PlcDbs) s7Db.SetBroadcastFlag();
                            _s7Plc.Open();
                        }
                        catch
                        {
                            Console.WriteLine($"S7ServerAccumulator Can't connect to S7 PLC, retry in 3 seconds");
                            try { await Task.Delay(3000, token); }
                            catch (TaskCanceledException) { /* normal on stop */ }
                        }
                    }
                }
                catch (Exception ex)
                {
                    //Log.Error(ex, $"{_name} S7ServerAccumulator: Exception in ReadBroadcastProcessLoop");
                }
            }
        }
    }
}