using DotNetEnv;

namespace nerv.log.Services;

public class EnvService
{
    public EnvService()
    {
        Env.Load();
    }

    public int MinWorkers => Env.GetInt("SCALING_MIN_WORKERS", 2);
    public int MaxWorkers => Env.GetInt("SCALING_MAX_WORKERS", 10);
    public int ThresholdByWorker => Env.GetInt("SCALING_THRESHOLD_PER_WORKER", 2000);
    public int BatchSize => Env.GetInt("SCALING_BATCH_SIZE", 1000);
}