using Microsoft.Extensions.Logging;

namespace NimChatGui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<NimApiClient>();
        builder.Services.AddSingleton<McpCatalogClient>();
        builder.Services.AddSingleton<McpToolGenerator>();
        builder.Services.AddSingleton<McpToolExecutor>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
