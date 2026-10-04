using System.Diagnostics;

namespace DisplayBoard.Tests.Data;

/// <summary>
/// Cho LibreOffice mở file xlsx, tính lại toàn bộ công thức và lưu ra file mới (có giá trị đã tính).
/// Máy không cài LibreOffice thì bỏ qua bài test, trừ khi đặt biến môi trường REQUIRE_LIBREOFFICE=1 (CI Linux).
/// </summary>
internal static class LibreOffice
{
    private static readonly Lazy<string?> Executable = new(Find);

    public static bool Available(out string reason)
    {
        if (Executable.Value is not null)
        {
            reason = "";
            return true;
        }
        reason = "Không tìm thấy LibreOffice (soffice).";
        if (Environment.GetEnvironmentVariable("REQUIRE_LIBREOFFICE") == "1")
            throw new InvalidOperationException(reason + " Biến REQUIRE_LIBREOFFICE=1 nên bài test phải chạy.");
        return false;
    }

    public static string Recalculate(string xlsx)
    {
        var outDir = Path.Combine(Path.GetDirectoryName(xlsx)!, "recalc");
        Directory.CreateDirectory(outDir);
        // Mỗi lần chạy một hồ sơ riêng để các test chạy song song không tranh nhau.
        var profile = new Uri(Path.Combine(Path.GetTempPath(), "lo-profile-" + Guid.NewGuid().ToString("N"))).AbsoluteUri;
        var start = new ProcessStartInfo(Executable.Value!)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var arg in new[] { $"-env:UserInstallation={profile}", "--headless", "--norestore", "--convert-to", "xlsx", "--outdir", outDir, xlsx })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(3)))
        {
            process.Kill(true);
            throw new TimeoutException("LibreOffice chạy quá lâu.");
        }
        var result = Path.Combine(outDir, Path.GetFileName(xlsx));
        if (!File.Exists(result))
            throw new InvalidOperationException($"LibreOffice không tạo được file: {output.Result} {error.Result}");
        return result;
    }

    private static string? Find()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? [@"C:\Program Files\LibreOffice\program\soffice.exe", @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"]
            : ["/usr/bin/soffice", "/usr/local/bin/soffice", "/usr/lib/libreoffice/program/soffice", "/Applications/LibreOffice.app/Contents/MacOS/soffice"];
        var found = candidates.FirstOrDefault(File.Exists);
        if (found is not null)
            return found;
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            var path = Path.Combine(dir, OperatingSystem.IsWindows() ? "soffice.exe" : "soffice");
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
