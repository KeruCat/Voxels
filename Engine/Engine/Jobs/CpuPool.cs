namespace Engine.Jobs;

/// <summary>Делегат задачи. Задача — структура в массиве, поэтому на каждую задачу не аллоцируется ничего.</summary>
public delegate void JobFn<T>(ref T job);

/// <summary>
/// Пул для CPU-работы пачками: задачи лежат в массиве структур, выполняются параллельно,
/// вызов синхронный (вернулся — всё готово). Главный поток после Run разбирает результаты.
/// </summary>
public sealed class CpuPool
{
    private readonly ParallelOptions _options;

    public CpuPool(int maxThreads = 0)
    {
        int n = maxThreads > 0 ? maxThreads : Math.Max(1, Environment.ProcessorCount - 1);
        _options = new ParallelOptions { MaxDegreeOfParallelism = n };
    }

    /// <summary>Выполняет первые count задач массива. Пул работает только с данными задач, общего состояния не трогает.</summary>
    public void Run<T>(T[] jobs, int count, JobFn<T> fn)
    {
        if (count <= 0) return;
        if (count == 1) { fn(ref jobs[0]); return; }
        Parallel.For(0, count, _options, i => fn(ref jobs[i]));
    }
}
