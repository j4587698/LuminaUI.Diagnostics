using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LuminaUI.Diagnostics.Abstractions;

namespace LuminaUI.Diagnostics.Transport;

public sealed class PipeDiagnosticClient
{
    private readonly string _pipeName;

    public PipeDiagnosticClient(string pipeName)
    {
        if (string.IsNullOrWhiteSpace(pipeName))
            throw new ArgumentException("Pipe name is required.", nameof(pipeName));

        _pipeName = pipeName.Trim();
    }

    public async Task<DiagnosticResponse> SendAsync(
        DiagnosticRequest request,
        CancellationToken cancellationToken = default)
    {
        var timeoutMs = request.TimeoutMs > 0
            ? request.TimeoutMs
            : LuminaUIDiagnosticsProtocol.DefaultTimeoutMs;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        try
        {
            await using var pipe = new NamedPipeClientStream(
                ".",
                _pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(timeoutMs, timeout.Token).ConfigureAwait(false);

            using var reader = new StreamReader(
                pipe,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false,
                leaveOpen: true);
            await using var writer = new StreamWriter(
                pipe,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };

            var payload = DiagnosticJson.SerializeRequest(request with { TimeoutMs = timeoutMs });
            await writer.WriteLineAsync(payload.AsMemory(), timeout.Token).ConfigureAwait(false);
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line is null)
                throw new IOException("Diagnostic pipe closed before a response was received.");

            return DiagnosticJson.DeserializeResponse(line);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(request, $"Diagnostic pipe operation timed out after {timeoutMs} ms: {ex.Message}", timeoutMs);
        }
        catch (JsonException ex)
        {
            return DiagnosticResponse.Fail(
                request.Id,
                DiagnosticErrorCode.SerializationFailure,
                $"Diagnostic response JSON was invalid: {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException or InvalidOperationException)
        {
            return Failure(request, $"Diagnostic pipe operation failed: {ex.Message}", timeoutMs);
        }
    }

    private DiagnosticResponse Failure(DiagnosticRequest request, string message, int timeoutMs) =>
        DiagnosticResponse.Fail(
            request.Id,
            DiagnosticErrorCode.TransportFailure,
            message,
            new JsonObject
            {
                ["pipeName"] = _pipeName,
                ["timeoutMs"] = timeoutMs
            });
}
