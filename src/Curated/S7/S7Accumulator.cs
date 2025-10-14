
using System.Xml.Linq;

public static partial class SiCADA
{
    public class S7Accumulator
    {
        private int _accumulatorPort;
        private S7.Net.CpuType _cpuType;
        private string _plcIp;
        private short _plcRack;
        private short _plcSlot;
        private int _minReadCycleMillis;
        protected S7.Net.Plc _s7Plc;
        private List<S7Db> _s7PlcDbs = [];

        private S7ServerAccumulator? _s7ServerAccumulator;
        protected S7ClientAccumulator? _s7ClientAccumulator;

        public S7Accumulator
            (
            int SiCADAPort,
            S7.Net.CpuType cpuType,
            string plcIp, 
            short plcRack, 
            short plcSlot,
            int minReadCycleMillis = 100
            )         
        {
            _accumulatorPort = SiCADAPort;
            _cpuType = cpuType;
            _plcIp = plcIp;
            _plcRack = plcRack;
            _plcSlot = plcSlot;
            _minReadCycleMillis = minReadCycleMillis;
            _s7Plc = new(_cpuType, _plcIp, _plcRack, _plcSlot);

            if (_isSiCADAServer)
            {
                _s7ServerAccumulator = new(_accumulatorPort, _s7Plc, _s7PlcDbs);
            }
            else
                _s7ClientAccumulator = new(); // TODO
        }

        protected void AddS7db(S7Db s7db) {_s7PlcDbs.Add(s7db);}
    }
}