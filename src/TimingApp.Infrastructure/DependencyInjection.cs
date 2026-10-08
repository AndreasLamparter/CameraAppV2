using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TimingApp.Application.FinishRecording;
using TimingApp.Infrastructure.FinishRecording;
using TimingApp.Infrastructure.Storage;

namespace TimingApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var storage = configuration.GetSection(StorageOptions.Section).Get<StorageOptions>() ?? new StorageOptions();
        services.AddSingleton(storage);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IMediaDirectoryResolver, MediaDirectoryResolver>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();
        services.AddSingleton<IRecordingStore, FileRecordingStore>();
        return services;
    }
}
