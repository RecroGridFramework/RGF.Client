using Microsoft.Extensions.Logging;

namespace Recrovit.RecroGridFramework.Client.Services;

public static class RgfLoggerFactory
{
    internal static void Initialize(ILoggerFactory loggerFactory)
    {
        LoggerFactory = loggerFactory;
    }

    private static ILoggerFactory? LoggerFactory;

    public static ILogger? GetLogger(Type type) => LoggerFactory?.CreateLogger(type);

    public static ILogger? GetLogger<T>() => LoggerFactory?.CreateLogger<T>();

    public static ILogger GetRequiredLogger<T>() => GetLogger<T>() ?? throw new InvalidOperationException("RgfLoggerFactory is not initialized.");
}