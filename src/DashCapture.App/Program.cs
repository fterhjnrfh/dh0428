using Avalonia;
using DashCapture.Core.Configuration;

namespace DashCapture.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        CaptureSettings settings = AppSettingsLoader.Load();
        BuildAvaloniaApp(settings.Platform).StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        return BuildAvaloniaApp(new PlatformSettings());
    }

    private static AppBuilder BuildAvaloniaApp(PlatformSettings platform)
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();

        return ConfigureGpuRendering(builder, platform);
    }

    private static AppBuilder ConfigureGpuRendering(AppBuilder builder, PlatformSettings platform)
    {
        if (!platform.EnableGpuRendering)
        {
            return builder;
        }

        long gpuCacheBytes = Math.Max(64, platform.GpuResourceCacheMb) * 1024L * 1024L;
        builder = builder.With(new SkiaOptions
        {
            MaxGpuResourceSizeBytes = gpuCacheBytes
        });

        if (platform.AllowCpuFallback)
        {
            return builder
                .With(new Win32PlatformOptions
                {
                    RenderingMode = new[]
                    {
                        Win32RenderingMode.Vulkan,
                        Win32RenderingMode.AngleEgl,
                        Win32RenderingMode.Wgl,
                        Win32RenderingMode.Software
                    }
                })
                .With(new X11PlatformOptions
                {
                    RenderingMode = new[]
                    {
                        X11RenderingMode.Vulkan,
                        X11RenderingMode.Egl,
                        X11RenderingMode.Glx,
                        X11RenderingMode.Software
                    }
                })
                .With(new AvaloniaNativePlatformOptions
                {
                    RenderingMode = new[]
                    {
                        AvaloniaNativeRenderingMode.Metal,
                        AvaloniaNativeRenderingMode.OpenGl,
                        AvaloniaNativeRenderingMode.Software
                    }
                });
        }

        return builder
            .With(new Win32PlatformOptions
            {
                RenderingMode = new[]
                {
                    Win32RenderingMode.Vulkan,
                    Win32RenderingMode.AngleEgl,
                    Win32RenderingMode.Wgl
                }
            })
            .With(new X11PlatformOptions
            {
                RenderingMode = new[]
                {
                    X11RenderingMode.Vulkan,
                    X11RenderingMode.Egl,
                    X11RenderingMode.Glx
                }
            })
            .With(new AvaloniaNativePlatformOptions
            {
                RenderingMode = new[]
                {
                    AvaloniaNativeRenderingMode.Metal,
                    AvaloniaNativeRenderingMode.OpenGl
                }
            });
    }
}
