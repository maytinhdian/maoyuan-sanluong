using System.Collections.Concurrent;
using Microsoft.AspNetCore.Http;

namespace DisplayBoard.Server;

/// <summary>File giao diện web (wwwroot) nhúng trong DLL.</summary>
public static class WebAssets
{
    private static readonly ConcurrentDictionary<string, byte[]?> Cache = new(StringComparer.Ordinal);

    public static byte[]? Get(string name)
    {
        // Chỉ tên file phẳng trong wwwroot, không cho đi ra ngoài.
        if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || name.Contains(".."))
            return null;
        return Cache.GetOrAdd(name, static n =>
        {
            using var stream = typeof(WebAssets).Assembly.GetManifestResourceStream("wwwroot/" + n);
            if (stream is null)
                return null;
            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            return memory.ToArray();
        });
    }

    public static IResult Result(string name)
    {
        var bytes = Get(name);
        return bytes is null ? Results.NotFound() : Results.Bytes(bytes, ContentTypes.ForFile(name));
    }
}

internal static class ContentTypes
{
    public static string ForFile(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".svg" => "image/svg+xml",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".bmp" => "image/bmp",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream"
    };
}
