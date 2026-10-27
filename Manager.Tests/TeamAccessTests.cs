using System.Net;
using System.Text;
using RoBIM.Manager.Core;

internal static class TeamAccessTests
{
    public static void Run(Action<string, Action> test)
    {
        void Require(bool value, string detail) { if (!value) throw new Exception(detail); }
        System.Net.Http.HttpResponseMessage Response(string state, HttpStatusCode code = HttpStatusCode.OK) =>
            new(code) { Content = new System.Net.Http.StringContent(state, Encoding.UTF8, "application/json") };
        foreach (var (name, body, code, expected) in new[]
        {
            ("ActiveTeamAllows", "{\"state\":\"active\"}", HttpStatusCode.OK, true),
            ("PendingInvitationDenies", "{\"state\":\"pending\"}", HttpStatusCode.OK, false),
            ("NonMemberDenies", "{}", HttpStatusCode.NotFound, false),
            ("InsufficientPermissionDenies", "{}", HttpStatusCode.Forbidden, false),
            ("MalformedMembershipDenies", "not json", HttpStatusCode.OK, false)
        })
            test(name, () =>
            {
                string? route = null;
                using var access = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("alice", "Browser")), url => { route = url; return Task.FromResult(Response(body, code)); });
                Require(!access.Allowed, "Startup must be locked");
                var result = access.Refresh().GetAwaiter().GetResult();
                Require(result.Allowed == expected && access.Allowed == expected, "Wrong team decision");
                Require(route == "/orgs/RoBIM-tech/teams/robim/memberships/alice", "Wrong team or trusted username");
            });
        test("NetworkFailureRevokesPriorTeamAccess", () =>
        {
            var fail = false;
            using var access = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("alice", "Browser")), _ => fail ? throw new IOException("offline") : Task.FromResult(Response("{\"state\":\"active\"}")));
            access.Refresh().GetAwaiter().GetResult(); Require(access.Allowed, "Initial login failed");
            fail = true; access.Refresh().GetAwaiter().GetResult(); Require(!access.Allowed, "Offline access remained allowed");
        });
        test("LogoutCannotBeUndoneByInflightMembershipResponse", () =>
        {
            var pending = new TaskCompletionSource<System.Net.Http.HttpResponseMessage>();
            using var access = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("alice", "Browser")), _ => pending.Task);
            var refresh = access.Refresh(); access.Lock(); pending.SetResult(Response("{\"state\":\"active\"}"));
            refresh.GetAwaiter().GetResult(); Require(!access.Allowed, "Inflight login bypassed logout");
        });
    }
}
