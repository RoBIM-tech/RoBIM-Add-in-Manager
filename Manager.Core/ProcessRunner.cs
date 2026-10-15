using System.Diagnostics;

namespace RoBIM.Manager.Core;

public sealed record ProcessResult(int ExitCode, string Output, string Error);

public static class ProcessRunner
{
    public static async Task<ProcessResult> Run(string executable, IEnumerable<string> args,
        string? workingDirectory = null, string? input = null, CancellationToken cancellation = default,
        bool interactive = false)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            RedirectStandardInput = input is not null
        };
        if (workingDirectory is not null) info.WorkingDirectory = workingDirectory;
        var argumentList = args.ToArray();
        foreach (var arg in argumentList) info.ArgumentList.Add(arg);
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GCM_INTERACTIVE"] = interactive ? "always" : "never";
        if (OperatingSystem.IsWindows()) info.Environment["GCM_CREDENTIAL_STORE"] = "wincredman";
        // Every manager operation uses the same OS credential helper, including git clone/fetch.
        info.Environment["GIT_CONFIG_COUNT"] = "1";
        info.Environment["GIT_CONFIG_KEY_0"] = "credential.helper";
        info.Environment["GIT_CONFIG_VALUE_0"] = "manager";
        if (executable.Equals("git", StringComparison.OrdinalIgnoreCase) && argumentList.Any(x => x is "clone" or "fetch" or "ls-remote")) GitHubSession.ConfigureGit(info);
        using var process = Process.Start(info) ?? throw new IOException("프로세스를 시작할 수 없습니다.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellation);
        var stderr = process.StandardError.ReadToEndAsync(cancellation);
        if (input is not null)
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromMinutes(interactive ? 10 : 3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { try { process.Kill(true); } catch (InvalidOperationException) { } throw; }
        return new(process.ExitCode, await stdout, await stderr);
    }

    public static Task<ProcessResult> Git(string directory, params string[] args) => Run("git",
        new[] { "-c", "core.hooksPath=NUL", "-c", "core.fsmonitor=false", "-c", "core.longpaths=true", "-C", directory }.Concat(args));

    public static async Task<string> GitRequired(string directory, params string[] args)
    {
        var result = await Git(directory, args);
        if (result.ExitCode != 0) throw new IOException("Git 작업에 실패했습니다. Git 설치·저장소 접근·GitHub 로그인을 확인하십시오.");
        return result.Output.Trim();
    }
}
