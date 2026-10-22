using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoBIM.Manager.Core;

public sealed record ReleaseOffer(string Version, string Channel, long AssetId = 0, string Digest = "", string Branch = "");

// Metadata only. This service never downloads or prepares an install payload.
public static class ReleaseUpdates
{
    public static async Task<ReleaseOffer> Check(ExtensionRepository repository, ExtensionPackage package)
    {
        if (package.Distribution == "git")
        {
            using var response = await GitHubSession.Request("/repos/" + repository.Repository + "/commits/" + Uri.EscapeDataString(repository.Branch));
            if (!response.IsSuccessStatusCode) throw new IOException("소스 버전을 확인하지 못했습니다.");
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var sha = document.RootElement.GetProperty("sha").GetString() ?? "";
            if (!Regex.IsMatch(sha, "^[a-fA-F0-9]{40}$")) throw new IOException("소스 버전 정보 오류");
            return new(repository.Branch + "@" + sha[..12], package.Channel, Branch: repository.Branch);
        }
        using var releases = await GitHubSession.Request("/repos/" + repository.Repository + "/releases?per_page=100");
        if (!releases.IsSuccessStatusCode) throw new IOException("배포 릴리스 조회 실패");
        using var json = JsonDocument.Parse(await releases.Content.ReadAsStringAsync());
        return Select(json.RootElement, package);
    }
    public static ReleaseOffer Select(JsonElement releases, ExtensionPackage package)
    {
        foreach (var release in releases.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean() || (package.Channel is "test" or "stable" && release.GetProperty("prerelease").GetBoolean() != (package.Channel == "test"))) continue;
            foreach (var asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != package.Asset) continue;
                var digest = asset.TryGetProperty("digest", out var value) ? value.GetString() : null;
                if (asset.GetProperty("size").GetInt64() is <= 0 or > 128L * 1024 * 1024 || digest is null || !Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
                    throw new IOException("ZIP 크기 또는 SHA-256 배포 검증 정보 오류");
                return new(release.GetProperty("tag_name").GetString()!, package.Channel == "unknown" && release.GetProperty("prerelease").GetBoolean() ? "test" : package.Channel, asset.GetProperty("id").GetInt64(), digest);
            }
        }
        throw new IOException("배포 구분에 맞는 ZIP을 찾지 못했습니다.");
    }
    public static bool IsNewer(string installed, string installedChannel, ReleaseOffer offer)
    {
        if (string.IsNullOrWhiteSpace(installed)) return false; // Unknown is not an uninstalled or outdated version.
        if (installedChannel == "stable" && offer.Channel == "test") return false;
        var oldGit = installed.LastIndexOf('@'); var newGit = offer.Version.LastIndexOf('@');
        if (oldGit > 0 && newGit > 0)
            return installed[..oldGit] == offer.Version[..newGit] && !installed.Equals(offer.Version, StringComparison.OrdinalIgnoreCase);
        var previous = Parse(installed); var next = Parse(offer.Version);
        if (previous is null || next is null) return false;
        for (var i = 0; i < 4; i++)
        {
            var difference = previous.Value.Numbers[i].CompareTo(next.Value.Numbers[i]);
            if (difference != 0) return difference < 0;
        }
        var a = previous.Value.Pre; var b = next.Value.Pre;
        if (a == b) return false;
        if (a.Length == 0) return false;
        if (b.Length == 0) return true;
        var aa = a.Split('.'); var bb = b.Split('.');
        for (var i = 0; i < Math.Min(aa.Length, bb.Length); i++)
        {
            var an = BigInteger.TryParse(aa[i], out var av); var bn = BigInteger.TryParse(bb[i], out var bv);
            var difference = an && bn ? av.CompareTo(bv) : an != bn ? (an ? -1 : 1) : string.CompareOrdinal(aa[i], bb[i]);
            if (difference != 0) return difference < 0;
        }
        return aa.Length < bb.Length;
    }
    private static (BigInteger[] Numbers, string Pre)? Parse(string text)
    {
        var match = Regex.Match(text, @"^[vV]?(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$");
        if (!match.Success || text.Length > 200) return null;
        return (Enumerable.Range(1, 4).Select(i => match.Groups[i].Success ? BigInteger.Parse(match.Groups[i].Value) : BigInteger.Zero).ToArray(), match.Groups[5].Value);
    }
}
