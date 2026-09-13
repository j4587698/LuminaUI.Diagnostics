namespace LuminaUI.Diagnostics.UI.ViewModels;

public sealed record RuntimeMetricsViewModel(
    long ManagedHeapBytes,
    long WorkingSetBytes,
    int ThreadCount,
    TimeSpan CpuTime,
    int Generation0Collections,
    int Generation1Collections,
    int Generation2Collections)
{
    public static RuntimeMetricsViewModel Empty { get; } = new(0, 0, 0, TimeSpan.Zero, 0, 0, 0);

    public static RuntimeMetricsViewModel ReadCurrent()
    {
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        return new RuntimeMetricsViewModel(
            GC.GetTotalMemory(false),
            process.WorkingSet64,
            process.Threads.Count,
            process.TotalProcessorTime,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }
}
