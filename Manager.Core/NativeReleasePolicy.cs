namespace RoBIM.Manager.Core;

public sealed record NativeReleaseStatus(string Channel, string Version, ReleaseOffer? Update, ReleaseOffer? OptionalTest)
{
    public string Kind => Update is not null ? "update" : OptionalTest is not null ? "stable-with-test" : Channel;
}

/// <summary>Installed channel is authoritative; catalog defaults are never an upgrade by themselves.</summary>
public static class NativeReleasePolicy
{
    public static NativeReleaseStatus Evaluate(string version, string channel, ReleaseOffer? stable, ReleaseOffer? test)
    {
        stable = stable?.Channel == "stable" ? stable : null;
        test = test?.Channel == "test" ? test : null;
        ReleaseOffer? update = null, optional = null;
        if (channel == "stable")
        {
            if (stable is not null && ReleaseUpdates.IsNewer(version, channel, stable)) update = stable;
            if (new ReleaseChannels(stable, test).HasNewTest(version)) optional = test;
        }
        else if (channel == "test")
        {
            // A real promotion/newer stable takes priority. An older stable is only an explicit rollback.
            if (stable is not null && ReleaseUpdates.IsNewer(version, channel, stable)) update = stable;
            else if (test is not null && ReleaseUpdates.IsNewer(version, channel, test)) update = test;
        }
        return new(channel, version, update, optional);
    }

    public static ReleaseOffer? Newest(ReleaseOffer? first, ReleaseOffer? second) => first is null ? second :
        second is not null && ReleaseUpdates.IsNewer(first.Version, "", second) ? second : first;
}
