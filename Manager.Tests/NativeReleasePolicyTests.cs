using RoBIM.Manager.Core;

internal static class NativeReleasePolicyTests
{
    internal static void Run(Action<string, Action> test, Action<bool, string> require)
    {
        ReleaseOffer S(string v) => new(v, "stable");
        ReleaseOffer T(string v) => new(v, "test");
        test("InstalledStableWithNewTestIsOptionalNotUpdate", () =>
        {
            var state = NativeReleasePolicy.Evaluate("0.2.0", "stable", S("v0.2.0"), T("v0.2.1-test.1"));
            require(state.Kind == "stable-with-test" && state.Update is null && state.OptionalTest?.Version == "v0.2.1-test.1", "TEST discovery changed stable update channel");
        });
        test("InstalledCurrentTestIgnoresOlderStable", () =>
        {
            var state = NativeReleasePolicy.Evaluate("0.2.1-test.1", "test", S("0.2.0"), T("v0.2.1-test.1"));
            require(state.Kind == "test" && state.Update is null && state.OptionalTest is null, "Installed TEST remains a false stable update");
        });
        test("InstalledTestTracksLaterTest", () => require(NativeReleasePolicy.Evaluate("0.2.1-test.1", "test", S("0.2.0"), T("0.2.1-test.2")).Update?.Version == "0.2.1-test.2", "TEST update missed"));
        test("InstalledTestPromotesToSameBaseStable", () => require(NativeReleasePolicy.Evaluate("0.2.1-test.2", "test", S("0.2.1"), T("0.2.1-test.2")).Update?.Channel == "stable", "Stable promotion missed"));
        test("InstalledFutureTestIgnoresOlderStablePromotion", () => require(NativeReleasePolicy.Evaluate("0.3.0-test.1", "test", S("0.2.1"), null).Update is null, "Older stable became a downgrade"));
        test("PromotionPrioritizesStableOverOptionalFutureTest", () => require(NativeReleasePolicy.Evaluate("0.2.1-test.1", "test", S("0.2.1"), T("0.3.0-test.1")).Update?.Channel == "stable", "Promotion hidden by future TEST"));
        test("InstalledPromotedStableStopsUpdating", () => require(NativeReleasePolicy.Evaluate("0.2.1", "stable", S("v0.2.1"), T("0.2.1-test.2")).Kind == "stable", "Promoted version remains outdated"));
        test("StableUpdateAndOptionalTestRemainDistinct", () =>
        {
            var state = NativeReleasePolicy.Evaluate("0.2.0", "stable", S("0.2.1"), T("0.3.0-test.1"));
            require(state.Kind == "update" && state.Update?.Channel == "stable" && state.OptionalTest?.Channel == "test", "Optional TEST replaced stable update");
        });
        test("UnknownInstalledChannelDoesNotInventUpdate", () => require(NativeReleasePolicy.Evaluate("0.2.0", "unknown", S("0.3.0"), T("0.4.0-test.1")).Update is null, "Unknown channel guessed"));
        test("OlderCacheCannotOverrideNewerOffer", () => require(NativeReleasePolicy.Newest(T("0.2.1-test.2"), T("0.2.1-test.1"))?.Version == "0.2.1-test.2", "Cache caused downgrade"));
    }
}
