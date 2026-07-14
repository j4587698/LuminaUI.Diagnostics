namespace LuminaUI.Diagnostics.Abstractions;

public static class ActivationPipeName
{
    public static string ForProcess(int processId) =>
        $"lumina-ui-devtools-activation-{processId}";
}
