using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TimingApp.Application.FinishRecording;
using TimingApp.Infrastructure.Camera.Capture;
using TimingApp.Infrastructure.Camera.Rendering;
using TimingApp.Infrastructure.Camera.Sources;
using TimingApp.Infrastructure.Camera.Video;

namespace TimingApp.Infrastructure.Camera;

public static class DependencyInjection
{
    public static IServiceCollection AddCameraInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(CameraOptions.Section).Get<CameraOptions>() ?? new CameraOptions();
        services.AddSingleton(options);
        services.AddSingleton<SimulationControl>();
        services.AddSingleton<CaptureClock>();
        services.AddSingleton<ICameraSourceFactory, CameraSourceFactory>();
        services.AddSingleton<ICameraDeviceEnumerator>(p => options.UsesMediaFoundation
            ? ActivatorUtilities.CreateInstance<MediaFoundationDevices>(p)
            : ActivatorUtilities.CreateInstance<DirectShowDevices>(p));
        services.AddSingleton<CameraSystem>();
        services.AddSingleton<ICameraSystem>(p => p.GetRequiredService<CameraSystem>());
        services.AddSingleton<ILivePreview>(p => p.GetRequiredService<CameraSystem>());
        services.AddSingleton<IFinishImageRenderer, FinishImageRenderer>();
        services.AddSingleton<IVideoEncoder, FfmpegVideoEncoder>();
        return services;
    }

}
