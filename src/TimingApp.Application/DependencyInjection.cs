using Microsoft.Extensions.DependencyInjection;
using TimingApp.Application.FinishRecording;

namespace TimingApp.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<RecordingSaver>();
        services.AddSingleton<FinishRecordingService>();
        services.AddSingleton<SettingsService>();
        services.AddSingleton<RecordingService>();
        return services;
    }
}
