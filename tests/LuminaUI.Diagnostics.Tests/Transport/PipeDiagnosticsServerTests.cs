using System.IO.Pipes;
using System.Text;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Dispatch;
using LuminaUI.Diagnostics.Transport;

namespace LuminaUI.Diagnostics.Tests.Transport;

public sealed class PipeDiagnosticsServerTests
{
    [Fact]
    public async Task Server_AcceptsConcurrentClients()
    {
        var handler = new ConcurrentBarrierHandler();
        var pipeName = $"lumina-diagnostics-test-{Guid.NewGuid():N}";
        using var server = new PipeDiagnosticsServer(pipeName, new DiagnosticDispatcher([handler]), 5_000);
        server.Start();

        var responses = await Task.WhenAll(
            SendAsync(pipeName, "first"),
            SendAsync(pipeName, "second"));

        Assert.Equal(2, handler.EnteredCount);
        Assert.All(responses, response => Assert.True(response.Success));
    }

    private static async Task<DiagnosticResponse> SendAsync(string pipeName, string id)
    {
        await using var pipe = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(5_000);

        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var request = new DiagnosticRequest(id, ConcurrentBarrierHandler.MethodName, new JsonObject(), 5_000);
        await writer.WriteLineAsync(DiagnosticJson.SerializeRequest(request));

        var responseLine = await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(responseLine);
        return DiagnosticJson.DeserializeResponse(responseLine);
    }

    private sealed class ConcurrentBarrierHandler : IDiagnosticToolHandler
    {
        public const string MethodName = "test_concurrent_clients";
        private readonly TaskCompletionSource _bothEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _enteredCount;

        public int EnteredCount => Volatile.Read(ref _enteredCount);
        public string Method => MethodName;

        public async Task<DiagnosticResponse> HandleAsync(
            DiagnosticRequest request,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _enteredCount) == 2)
                _bothEntered.TrySetResult();

            await _bothEntered.Task.WaitAsync(cancellationToken);
            return DiagnosticResponse.Ok(request.Id, new JsonObject { ["status"] = "ok" });
        }
    }
}
