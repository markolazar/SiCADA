public static partial class SiCADA
{
    public static Action? Init { get; set; }
    public static Action? OnStart { get; set; }
    private static List<ServerAccumulator> _serverAccumulators = [];
    private static List<ClientAccumulator> _clientAccumulators = [];
    private static List<string> _SiCADAClients = [];
    private static string? _SiCADAServerIp;
    private static bool _isSiCADAServer;

    public static void Start(bool isSiCADAServer = true)
    {
        _isSiCADAServer = isSiCADAServer;

        if (Init is null)
            throw new InvalidOperationException
                (
                "SiCADA is not initialized. Assign SiCADA.Init = () => { /* your setup */ }; before calling SiCADA.Start()."
                );

        Init.Invoke();

        if (_SiCADAServerIp is null)
            throw new InvalidOperationException
                (
                "SiCADA server IP is not set. Please assign it using SetSiCADAServerIp(string serverIp) before starting SiCADA."
                );

        if (_isSiCADAServer)
            StartServerAccumulators();
        else
            StartClientAccumulators();
    }
    public static void StartClientAccumulators()
    {
        // TODO
    }
    public static void StartServerAccumulators()
    {
        if (_serverAccumulators.Count <= 0)
            throw new InvalidOperationException
                (
                "No Accumulators were initialized!"
                );

        foreach (ServerAccumulator serverAccumulator in _serverAccumulators) serverAccumulator.Start();
    }
    public static void AddSiCADAClient(string plcIp) {_SiCADAClients.Add(plcIp);}
    public static void SetSiCADAServerIp(string SiCADAServerIp){_SiCADAServerIp = SiCADAServerIp;}
}
