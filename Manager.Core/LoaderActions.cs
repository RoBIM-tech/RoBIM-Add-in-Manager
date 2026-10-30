namespace RoBIM.Manager.Core;

public static class CadLoaderAssets
{
    public const string Name = "RoBIM-CAD-Loader-AutoCAD2026.zip";
    public const string BundleName = "RoBIM.Cad.Loader.bundle";
    public const string LegacyBundleName = "DelHiddenNet.bundle";
    public static bool IsBundle(string? name) => name is BundleName or LegacyBundleName;
    public static string ExtractedBundle(string root)
    {
        var candidates = new[] { BundleName, LegacyBundleName }.Select(name => Path.Combine(root, name)).Where(Directory.Exists).ToArray();
        return candidates.Length == 1 ? candidates[0] : throw new IOException("CAD 로더 번들이 없거나 신·구 번들이 중복됩니다.");
    }
    public const string LegacyName = "DelHiddenNet-AutoCAD2026.zip";
    public static bool IsLoader(string? name) => name is Name or LegacyName;
    public static bool Matches(string? expected, string? actual) => expected == actual || IsLoader(expected) && IsLoader(actual);
}

public sealed record LoaderActionState(bool Enabled, string Text, string Detail);

public static class LoaderActions
{
    public static LoaderActionState Evaluate(ToolStatus state, ReleaseChannels? channels, string channel, bool canUseTest)
    {
        if (state.Modified) return new(false, "적용 차단", "관리 설치 파일이 변경되어 보호 중입니다. 설치 기록과 실제 파일을 확인해야 합니다." + (state.Detail.Length > 0 ? "\n" + state.Detail : ""));
        if (state.External) return new(false, "적용 차단", "외부 설치를 보호하고 있습니다. 관리 설치 여부를 확인해야 합니다.");
        if (channel == "test" && !canUseTest) return new(false, "권한 필요", "TEST 로더 사용 권한이 필요합니다.");
        if (channels is null) return new(false, "버전 확인 필요", "저장소 확인을 눌러 제공 버전을 확인하세요.");
        var offer = channel == "test" ? channels.Test : channels.Stable;
        if (offer is null) return new(false, "제공 버전 없음", "선택한 채널에 확인된 배포가 없습니다.");
        if (!state.Managed) return new(true, "설치", (channel == "test" ? "TEST " : "정식 ") + offer.Version + " 설치 가능");
        if (!ReleaseUpdates.VersionsComparable(state.Version, offer.Version)) return new(false, "버전 확인 필요", "설치 버전을 제공 버전과 비교할 수 없습니다. 설치 기록을 확인하세요.");
        if (ReleaseUpdates.IsNewer(state.Version, "", offer)) return new(true, "업데이트", state.Version + " → " + offer.Version + " 업데이트 가능");
        if (ReleaseUpdates.IsNewer(offer.Version, "", new(state.Version, ""))) return new(false, "이전 버전", "선택한 제공 버전이 설치 버전보다 낮습니다.");
        var detail = "선택한 " + (channel == "test" ? "TEST" : "정식") + " 버전은 이미 최신입니다.";
        if (channel == "stable" && canUseTest && new ReleaseChannels(channels.Stable, channels.Test).HasNewTest(state.Version))
            detail += " TEST " + channels.Test!.Version + " 제공 · TEST를 선택하면 업데이트할 수 있습니다.";
        return new(false, "최신 버전", detail);
    }
}
