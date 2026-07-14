using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Transport;

namespace LuminaUI.Diagnostics;

public sealed class LuminaUIDiagnosticsHost : IDisposable
{
    private readonly object _gate = new();
    private readonly DiagnosticDispatcher _dispatcher;
    private readonly DiagnosticsServices? _services;
    private readonly LuminaUIDiagnosticsOptions _options;
    private Discovery.DiagnosticsAppRegistration? _appRegistration;
    private PipeDiagnosticsServer? _server;

    public LuminaUIDiagnosticsHost(
        LuminaUIDiagnosticsOptions? options = null,
        IEnumerable<IDiagnosticToolHandler>? handlers = null)
    {
        _options = options ?? new LuminaUIDiagnosticsOptions();
        if (handlers is null)
        {
            _services = DiagnosticsServiceFactory.CreateDefault();
            _dispatcher = _services.Dispatcher;
        }
        else
        {
            _dispatcher = new DiagnosticDispatcher(handlers);
        }

        DiagnosticsPipeName = _options.ResolveDiagnosticsPipeName();
        DefaultTimeoutMs = _options.DefaultTimeoutMs > 0
            ? _options.DefaultTimeoutMs
            : throw new ArgumentOutOfRangeException(nameof(options), "Default timeout must be positive.");
    }

    public string DiagnosticsPipeName { get; }

    public int DefaultTimeoutMs { get; }

    public bool IsRunning { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
                return;

            _server = new PipeDiagnosticsServer(DiagnosticsPipeName, _dispatcher, DefaultTimeoutMs);
            _server.Start();
            var sessionId = _services?.NodeRegistry.ApplicationSessionId;
            _appRegistration = Discovery.DiagnosticsAppRegistration.TryCreate(DiagnosticsPipeName, sessionId);
            IsRunning = true;
        }
    }

    public void Stop()
    {
        PipeDiagnosticsServer? server;
        Discovery.DiagnosticsAppRegistration? appRegistration;
        lock (_gate)
        {
            if (!IsRunning)
                return;

            server = _server;
            appRegistration = _appRegistration;
            _server = null;
            _appRegistration = null;
            IsRunning = false;
        }

        appRegistration?.Dispose();
        server?.Dispose();
    }

    public DiagnosticDispatcher? Dispatcher => _dispatcher;

    public bool TryGetNodeRegistry([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Inspection.NodeRegistry? nodeRegistry)
    {
        nodeRegistry = _services?.NodeRegistry;
        return nodeRegistry is not null;
    }

    public void Dispose()
    {
        Stop();
        _services?.Dispose();
    }
}


