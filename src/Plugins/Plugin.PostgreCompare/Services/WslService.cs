using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Plugin.PostgreCompare.Services;

public sealed class WslPostgreStartResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
}

/// <summary>
/// Windows から WSL 内の PostgreSQL（pg ユーザー・pg_ctl）を起動する。
/// </summary>
public class WslService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 指定ディストリで <c>pg</c> ユーザーになり、<c>source ~/.bash_profile</c> の後に <c>pg_ctl start</c> を実行する。
    /// </summary>
    public async Task<WslPostgreStartResult> StartPostgreSqlAsPgAsync(
        string distributionName,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(distributionName);

        var effectiveTimeout = timeout ?? DefaultTimeout;
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(effectiveTimeout);

        var psi = new ProcessStartInfo
        {
            FileName = GetWslExecutablePath(),
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        psi.ArgumentList.Add("-d");
        psi.ArgumentList.Add(distributionName.Trim());
        psi.ArgumentList.Add("-u");
        psi.ArgumentList.Add("pg");
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("bash");
        psi.ArgumentList.Add("-lc");
        psi.ArgumentList.Add("source ~/.bash_profile && pg_ctl start");

        using var process = new Process { StartInfo = psi };

        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                outputBuilder.AppendLine(e.Data);
            }
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
            {
                errorBuilder.AppendLine(e.Data);
            }
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch
            {
                // ignore
            }

            throw new TimeoutException($"WSL コマンドが {effectiveTimeout.TotalSeconds} 秒以内に終了しませんでした。");
        }

        return new WslPostgreStartResult
        {
            ExitCode = process.ExitCode,
            StandardOutput = outputBuilder.ToString().TrimEnd(),
            StandardError = errorBuilder.ToString().TrimEnd()
        };
    }

    private static string GetWslExecutablePath()
    {
        var systemWsl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wsl.exe");
        return File.Exists(systemWsl) ? systemWsl : "wsl.exe";
    }
}
