namespace LuminaUI.Diagnostics.Binding;

internal static class BindingErrorDiagnostics
{
    private static readonly object Gate = new();
    private static BindingErrorLogSink? _sink;
    private static int _references;

    public static BindingErrorStore Store { get; } = new();

    public static IDisposable Install()
    {
        lock (Gate)
        {
            _sink ??= BindingErrorLogSink.Install(Store);
            _references++;
        }

        return new Registration();
    }

    private static void Release()
    {
        lock (Gate)
        {
            if (_references == 0)
                return;

            _references--;
            if (_references != 0)
                return;

            _sink?.Dispose();
            _sink = null;
        }
    }

    private sealed class Registration : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            Release();
        }
    }
}
