namespace RoBIM.Manager.Core;

public static class GitHubAccount
{
    public static Task<IReadOnlyList<string>> SavedAccounts() => Task.FromResult(WindowsTokenStore.Accounts());

    public static async Task LoginAndSave(Func<GitHubDeviceCode, CancellationToken, Task> showCode, CancellationToken cancellation = default)
    {
        var login = new GitHubDeviceLogin();
        var code = await login.Begin(cancellation);
        await showCode(code, cancellation);
        var token = await login.Complete(code, cancellation);
        await GitHubSession.UseDeviceToken(token);
    }
}
