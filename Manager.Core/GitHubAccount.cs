namespace RoBIM.Manager.Core;

public static class GitHubAccount
{
    public static async Task<IReadOnlyList<string>> SavedAccounts()
    {
        var result = await ProcessRunner.Run("git", ["credential-manager", "github", "list"]);
        if (result.ExitCode != 0) throw new IOException("Git Credential Manager의 저장 계정을 확인할 수 없습니다.");
        return result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => System.Text.RegularExpressions.Regex.IsMatch(x, "^[A-Za-z0-9-]{1,39}$")).Distinct().ToArray();
    }

    public static async Task LoginAndSave()
    {
        var result = await ProcessRunner.Run("git", ["credential-manager", "github", "login", "--url", "https://github.com", "--browser"], interactive: true);
        if (result.ExitCode != 0) throw new IOException("로그인이 완료되지 않았습니다. 브라우저 인증을 완료한 뒤 다시 시도하십시오.");
        await GitHubSession.UseBrowser();
    }
}
