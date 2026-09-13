using System.IO.Pipes;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Desktop;

internal sealed class DevToolsSingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _activationPipeName;

    public DevToolsSingleInstance(int targetProcessId)
    {
        _activationPipeName = ActivationPipeName.ForProcess(targetProcessId);
        _mutex = new Mutex(
            initiallyOwned: true,
            $"LuminaUI.DevTools.{targetProcessId}",
            out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void StartActivationListener(Action activate, CancellationToken cancellationToken) =>
        _ = ListenAsync(activate, cancellationToken);

    public void ActivatePrimary()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(
                    ".",
                    _activationPipeName,
                    PipeDirection.Out,
                    PipeOptions.Asynchronous);
                pipe.Connect(100);
                pipe.WriteByte(1);
                pipe.Flush();
                return;
            }
            catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
            {
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        if (IsPrimary)
            _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private async Task ListenAsync(Action activate, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _activationPipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                if (pipe.ReadByte() >= 0)
                    activate();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}
