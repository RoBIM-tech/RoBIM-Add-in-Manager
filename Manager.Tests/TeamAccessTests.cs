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
                var routes = new List<string>();
                using var access = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("alice", "Browser")), url => { routes.Add(url); return Task.FromResult(Response(body, code)); });
                Require(!access.Allowed, "Startup must be locked");
                var result = access.Refresh().GetAwaiter().GetResult();
                Require(result.Allowed == expected && access.Allowed == expected, "Wrong team decision");
                Require(routes.SequenceEqual(TeamAccess.Teams.Select(t => "/orgs/RoBIM-tech/teams/" + t + "/memberships/alice")), "Wrong team or trusted username");
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
            var pending = new TaskCompletionSource<bool>();
            using var access = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("alice", "Browser")), async _ => { await pending.Task; return Response("{\"state\":\"active\"}"); });
            var refresh = access.Refresh(); access.Lock(); pending.SetResult(true);
            refresh.GetAwaiter().GetResult(); Require(!access.Allowed, "Inflight login bypassed logout");
        });
        foreach (var team in TeamAccess.Teams)
            test("RoleScope_" + team, () =>
            {
                using var access = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("alice", "Browser")), route => Task.FromResult(
                    route.Contains("/teams/" + team + "/") ? Response("{\"state\":\"active\"}") : Response("{}", HttpStatusCode.NotFound)));
                access.Refresh().GetAwaiter().GetResult();
                Require(access.CanUse("stable"), "Every active role must permit stable");
                Require(access.CanUse("test") == (team != "robim"), "Wrong TEST scope");
                Require(!access.CanUse("unknown"), "Unknown channel must stay blocked");
            });
        test("FailedTestMembershipDoesNotRevokeVerifiedStable", () =>
        {
            using var access = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("alice", "Browser")), route => Task.FromResult(
                route.Contains("/teams/robim/") ? Response("{\"state\":\"active\"}") : Response("{}", HttpStatusCode.Forbidden)));
            access.Refresh().GetAwaiter().GetResult(); Require(access.Allowed && !access.TestAllowed, "Optional team error elevated/revoked stable access");
        });
        test("RemovingTestRoleRevokesTestButKeepsStable", () =>
        {
            var tester = true;
            using var access = new TeamAccess(false, () => Task.FromResult(new GitHubIdentity("alice", "Browser")), route => Task.FromResult(
                route.Contains("/teams/robim/") || tester && route.Contains("/teams/robim-test/") ? Response("{\"state\":\"active\"}") : Response("{}", HttpStatusCode.NotFound)));
            access.Refresh().GetAwaiter().GetResult(); Require(access.TestAllowed, "TEST role not granted");
            tester = false; access.Refresh().GetAwaiter().GetResult(); Require(access.Allowed && !access.TestAllowed, "Old TEST access survived removal");
        });
    }
}
