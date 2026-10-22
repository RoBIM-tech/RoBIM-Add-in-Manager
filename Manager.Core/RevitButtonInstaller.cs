namespace RoBIM.Manager.Core;

public sealed class RevitButtonInstaller(ManagerPaths paths, Action? ensureClosed = null)
{
    public async Task Set(SourcePackage source, string id, bool selected, IReadOnlyList<string> current, Func<Task> activate)
    {
        void Check() { if (ensureClosed is null) EnvironmentProbe.EnsureHostClosed(Catalog.RevitSelected); else ensureClosed(); }
        Check();
        if (await new RevitReload(paths).SetPendingWhenClosed(source, id, selected, current, activate, Check)) return;
        await SetCore(source, id, selected, current, activate);
    }
    internal async Task SetCore(SourcePackage source, string id, bool selected, IReadOnlyList<string> current, Func<Task> activate)
    {
        void Check() { if (ensureClosed is null) EnvironmentProbe.EnsureHostClosed(Catalog.RevitSelected); else ensureClosed(); }
        Check();
        var service = new InstallService(paths, _ => Check()); var before = service.Status(Catalog.RevitSelected);
        if (before.External || before.Modified) throw new IOException("버튼 선택 설치의 로컬 변경을 보존하기 위해 작업을 중단합니다.");
        var ids = current.ToHashSet(StringComparer.OrdinalIgnoreCase); if (selected) ids.Add(id); else ids.Remove(id);
        var output = SafeFiles.Child(paths.Data, "ButtonPackages/" + Guid.NewGuid().ToString("N"));
        var package = RevitButtons.Prepare(source, output, ids.ToArray()); service.Install(Catalog.RevitSelected, package);
        var previous = before.Managed ? service.Backups(Catalog.RevitSelected).First() : null;
        try { await activate(); }
        catch (Exception activation)
        {
            try { if (previous is not null) service.Restore(Catalog.RevitSelected, previous); else service.Remove(Catalog.RevitSelected); }
            catch (Exception rollback) { throw new IOException("프로필 활성화와 설치 복원에 실패했습니다. 백업을 확인하십시오.", new AggregateException(activation, rollback)); }
            throw;
        }
    }
}
