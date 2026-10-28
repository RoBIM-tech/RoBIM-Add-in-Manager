using System.Net;
using System.Text;
using RoBIM.Manager.Core;

internal static class GitHubDeviceLoginTests
{
    private sealed class Handler(params string[] responses) : HttpMessageHandler
    {
        public readonly List<(string Uri, string Body)> Requests = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Requests.Add((request.RequestUri!.AbsoluteUri, await request.Content!.ReadAsStringAsync(cancellation)));
            return new(HttpStatusCode.OK) { Content = new StringContent(responses[Requests.Count - 1], Encoding.UTF8, "application/json") };
        }
    }
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public static void Run(Action<string, Action> test)
    {
        const string start = "{\"device_code\":\"device-secret\",\"user_code\":\"ABCD-1234\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":900,\"interval\":5}";
        void Require(bool value) { if (!value) throw new Exception("Device login contract failed"); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (IOException ex) { Require(ex.Message.Contains(message) && !ex.Message.Contains("secret")); return; }
            throw new Exception("Expected login failure");
        }
        test("DeviceLoginUsesHttpsWithoutGitAndRequestsTeamRepositoryScopes", () =>
        {
            var handler = new Handler(start); var login = new GitHubDeviceLogin(new HttpClient(handler));
            var code = login.Begin().GetAwaiter().GetResult();
            Require(code.UserCode == "ABCD-1234" && handler.Requests[0].Uri == "https://github.com/login/device/code" &&
                handler.Requests[0].Body.Contains("scope=repo+read%3Aorg") && !handler.Requests[0].Body.Contains("client_secret"));
        });
        test("DeviceLoginPendingAndSlowDownRespectPollingIntervals", () =>
        {
            var clock = new Clock(); var waits = new List<double>();
            var handler = new Handler(start, "{\"error\":\"authorization_pending\"}", "{\"error\":\"slow_down\"}", "{\"access_token\":\"test_token\"}");
            var login = new GitHubDeviceLogin(new HttpClient(handler), (delay, _) => { waits.Add(delay.TotalSeconds); clock.Now += delay; return Task.CompletedTask; }, clock);
            var token = login.Complete(login.Begin().GetAwaiter().GetResult()).GetAwaiter().GetResult();
            Require(token == "test_token" && waits.SequenceEqual(new double[] { 5, 5, 10 }) && handler.Requests.Skip(1).All(x => x.Uri == "https://github.com/login/oauth/access_token"));
        });
        foreach (var error in new[] { "access_denied", "expired_token", "unexpected_secret" })
            test("DeviceLoginHandles_" + error, () =>
            {
                var handler = new Handler(start, "{\"error\":\"" + error + "\"}");
                var login = new GitHubDeviceLogin(new HttpClient(handler), (_, _) => Task.CompletedTask);
                Reject(() => login.Complete(login.Begin().GetAwaiter().GetResult()).GetAwaiter().GetResult(), error == "access_denied" ? "취소" : error == "expired_token" ? "만료" : "완료하지");
            });
        test("DeviceLoginStopsPollingAtExpiry", () =>
        {
            var clock = new Clock(); var handler = new Handler(start);
            var login = new GitHubDeviceLogin(new HttpClient(handler), (_, _) => { clock.Now += TimeSpan.FromHours(1); return Task.CompletedTask; }, clock);
            Reject(() => login.Complete(login.Begin().GetAwaiter().GetResult()).GetAwaiter().GetResult(), "만료");
            Require(handler.Requests.Count == 1);
        });
        test("DeviceLoginCancellationStopsBeforeTokenRequest", () =>
        {
            var handler = new Handler(start); var login = new GitHubDeviceLogin(new HttpClient(handler));
            var code = login.Begin().GetAwaiter().GetResult(); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { login.Complete(code, cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Require(handler.Requests.Count == 1); }
        });
        test("DeviceLoginRejectsUnexpectedVerificationDestination", () =>
        {
            var login = new GitHubDeviceLogin(new HttpClient(new Handler(start.Replace("https://github.com/login/device", "https://example.com/login/device"))));
            Reject(() => login.Begin().GetAwaiter().GetResult(), "주소");
        });
        test("DeviceLoginRejectsInvalidCodeWithoutDisplayingResponse", () =>
        {
            var login = new GitHubDeviceLogin(new HttpClient(new Handler(start.Replace("ABCD-1234", "invalid_secret"))));
            Reject(() => login.Begin().GetAwaiter().GetResult(), "응답");
        });
        test("DeviceLoginRejectsInvalidTokenWithoutDisplayingResponse", () =>
        {
            var login = new GitHubDeviceLogin(new HttpClient(new Handler(start, "{\"access_token\":\"invalid secret\"}")), (_, _) => Task.CompletedTask);
            Reject(() => login.Complete(login.Begin().GetAwaiter().GetResult()).GetAwaiter().GetResult(), "응답");
        });
    }
}
