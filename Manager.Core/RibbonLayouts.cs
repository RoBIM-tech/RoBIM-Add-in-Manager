using System.Diagnostics;
using System.Text.Json;

namespace RoBIM.Manager.Core;

public sealed record RibbonTool(string Id, string Title, string Tab, string Panel, bool Installed, string? Icon = null);
public sealed record RibbonPlacement(string Id, string Title, string Size = "Large", string? Stack = null, int StackRows = 0, string Kind = "Button");
public sealed record RibbonPanelLayout(string Name, RibbonPlacement[] Buttons);
public sealed record RibbonTabLayout(string Name, RibbonPanelLayout[] Panels);
public sealed record RibbonLayout(int SchemaVersion, string Host, RibbonTabLayout[] Tabs);

public sealed class RibbonLayouts(ManagerPaths paths)
{
    public static RibbonTool DefaultLocation(RibbonTool tool)
    {
        var reserved = !tool.Id.StartsWith("manager:", StringComparison.Ordinal) && (tool.Tab.Trim() == "RoBIM 관리" || tool.Panel.Trim() == "관리");
        var tab = tool.Tab.Trim();
        if (tab is "RoBIM TEST" or "RoBIM 도구" or "RoBIM 기본") tab = "RoBIM";
        if (tab == "RoBIM 도면") tab = "RoBIM View/Sheet";
        if (tool.Id.StartsWith("native:sheets:", StringComparison.Ordinal) || tool.Id.StartsWith("native:view-duplicate:", StringComparison.Ordinal) || tool.Id.StartsWith("native:view-rotation:", StringComparison.Ordinal)) tab = "RoBIM View/Sheet";
        return tool with { Tab = reserved || string.IsNullOrWhiteSpace(tab) ? "RoBIM" : tab,
            Panel = reserved || string.IsNullOrWhiteSpace(tool.Panel) ? "도구" : tool.Panel.Trim() };
    }
    public static RibbonTool FromGroup(string id, string title, string group, bool installed, string? icon = null)
    {
        var pieces = group.Split(" / ", 2, StringSplitOptions.TrimEntries);
        return DefaultLocation(new(id, title, pieces.Length > 1 ? pieces[0] : "", pieces.Length > 1 ? ToolNames.Group(pieces[1]) : "", installed, icon));
    }
    public static RibbonTool[] WithManagementTools(IEnumerable<RibbonTool> values)
    {
        var tools = values.Select(DefaultLocation).ToList();
        if (tools.Any(t => t.Installed && (t.Id.StartsWith("robim-revit:", StringComparison.Ordinal) || t.Id.StartsWith("revit:", StringComparison.Ordinal))))
            tools.Add(new("manager:pyrevit-reload", "pyRevit Reload", "RoBIM 관리", "Manager", true));
        if (tools.Any(t => t.Installed && t.Id.StartsWith("native:", StringComparison.Ordinal)))
        {
            tools.Add(new("manager:RoBIM.Native.Reload", "Reload", "RoBIM 관리", "관리", true));
            tools.Add(new("manager:RoBIM.Native.Manager", "Add-in Manager", "RoBIM 관리", "관리", true));
        }
        return tools.ToArray();
    }
    public static RibbonLayout IncludeMissingManagement(RibbonLayout layout, IEnumerable<RibbonTool> tools)
    {
        var ids = layout.Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).Select(b => b.Id).ToHashSet();
        var tabs = layout.Tabs.ToList();
        foreach (var tool in tools.Where(t => t.Installed && t.Id.StartsWith("manager:", StringComparison.Ordinal) && !ids.Contains(t.Id)))
        {
            var ti = tabs.FindIndex(t => t.Name.Equals(tool.Tab, StringComparison.OrdinalIgnoreCase));
            if (ti < 0) { tabs.Add(new(tool.Tab, [])); ti = tabs.Count - 1; }
            var panels = tabs[ti].Panels.ToList(); var pi = panels.FindIndex(p => p.Name.Equals(tool.Panel, StringComparison.OrdinalIgnoreCase));
            if (pi < 0) panels.Add(new(tool.Panel, [new(tool.Id, tool.Title)]));
            else panels[pi] = panels[pi] with { Buttons = [..panels[pi].Buttons, new(tool.Id, tool.Title)] };
            tabs[ti] = tabs[ti] with { Panels = panels.ToArray() };
        }
        return layout with { Tabs = tabs.ToArray() };
    }
    public string FilePath(string host) => SafeFiles.Child(paths.Data, "ribbon-layout-" + Host(host).ToLowerInvariant() + ".json");
    private static string Host(string host) => host is "Revit" or "AutoCAD" or "Navisworks" ? host : throw new IOException("지원하지 않는 리본 호스트");
    public static void Validate(RibbonLayout layout)
    {
        _ = Host(layout.Host);
        if (layout.SchemaVersion != 1 || layout.Tabs is null || layout.Tabs.Length > 30) throw new IOException("리본 구성 형식 오류");
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stackIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        static void Name(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 80 || value.Any(char.IsControl)) throw new IOException("탭·패널 이름은 1~80자여야 합니다."); }
        var tabs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tab in layout.Tabs)
        {
            Name(tab.Name); if (!tabs.Add(tab.Name) || tab.Panels is null || tab.Panels.Length > 50) throw new IOException("탭 이름 중복 또는 패널 수 초과");
            var panels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var panel in tab.Panels)
            {
                Name(panel.Name); if (!panels.Add(panel.Name) || panel.Buttons is null || panel.Buttons.Length > 200) throw new IOException("패널 이름 중복 또는 버튼 수 초과");
                foreach (var button in panel.Buttons)
                {
                    if (button.Kind is not ("Button" or "Separator") || button.Kind == "Separator" && (!button.Id.StartsWith("separator:", StringComparison.Ordinal) || button.Stack is not null || button.StackRows != 0 || button.Size != "Large"))
                        throw new IOException("구분선은 버튼 묶음 밖에 배치하십시오.");
                    if (string.IsNullOrWhiteSpace(button.Id) || button.Id.Length > 500 || button.Id.Any(char.IsControl) || !ids.Add(button.Id) ||
                        string.IsNullOrWhiteSpace(button.Title) || button.Title.Length > 120 || button.Title.Any(char.IsControl) || button.Size is not ("Large" or "Small"))
                        throw new IOException("버튼 ID 중복·표시 이름·크기 오류");
                }
                foreach (var button in panel.Buttons)
                    if (button.Stack is null ? button.StackRows != 0 : string.IsNullOrWhiteSpace(button.Stack) || button.Stack.Length > 80 || button.Stack.Any(char.IsControl) || button.Size != "Small" || button.StackRows is not (2 or 3))
                        throw new IOException("버튼 묶음은 2단 또는 3단 작은 버튼으로 구성하십시오.");
                foreach (var group in panel.Buttons.Where(b => b.Stack is not null).GroupBy(b => b.Stack!, StringComparer.OrdinalIgnoreCase))
                {
                    if (!stackIds.Add(group.Key) || group.Any(b => b.StackRows != group.First().StackRows) || group.Count() != group.First().StackRows)
                        throw new IOException("한 묶음에 같은 패널의 버튼을 정확히 2개 또는 3개 넣으십시오.");
                    var indices = panel.Buttons.Select((b, i) => (b, i)).Where(x => string.Equals(x.b.Stack, group.Key, StringComparison.OrdinalIgnoreCase)).Select(x => x.i).ToArray();
                    if (indices.Last() - indices.First() + 1 != indices.Length) throw new IOException("버튼 묶음 중간에 다른 버튼이나 구분선을 넣을 수 없습니다.");
                }
            }
        }
    }
    public RibbonLayout Read(string host)
    {
        var file = FilePath(host); SafeFiles.NoLinks(file);
        if (!File.Exists(file)) return new(1, host, []);
        if (new FileInfo(file).Length > 1024 * 1024) throw new IOException("리본 설정 크기 초과");
        var layout = JsonSerializer.Deserialize<RibbonLayout>(File.ReadAllText(file)) ?? throw new IOException("리본 설정 오류");
        Validate(layout); if (layout.Host != host) throw new IOException("다른 호스트의 리본 설정"); return layout;
    }
    public void Save(RibbonLayout layout)
    {
        Validate(layout); var file = FilePath(layout.Host); SafeFiles.NoLinks(file); Directory.CreateDirectory(paths.Data);
        var temporary = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true })); File.Move(temporary, file, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static RibbonLayout Defaults(string host, IEnumerable<RibbonTool> tools) => new(1, host,
        tools.Where(x => x.Installed).Select(DefaultLocation).GroupBy(x => x.Tab).Select(tab => new RibbonTabLayout(tab.Key,
            tab.GroupBy(x => x.Panel).Select(panel => new RibbonPanelLayout(panel.Key, panel.Select(x => new RibbonPlacement(x.Id, x.Title)).ToArray())).ToArray())).ToArray());
    public void EnsureInstalled(RibbonTool tool, IEnumerable<RibbonTool> initialTools, Action? select = null)
    {
        tool = DefaultLocation(tool);
        if (!tool.Installed) throw new IOException("설치한 버튼만 자동 배치할 수 있습니다.");
        var file = FilePath("Revit"); SafeFiles.NoLinks(file);
        var before = File.Exists(file) ? File.ReadAllBytes(file) : null;
        var layout = before is not null ? Read("Revit") : Defaults("Revit", initialTools);
        if (layout.Tabs.SelectMany(t => t.Panels).SelectMany(p => p.Buttons).Any(b => b.Id == tool.Id)) { select?.Invoke(); return; }
        var tabs = layout.Tabs.ToList();
        var tabIndex = tabs.FindIndex(t => t.Name.Equals(tool.Tab, StringComparison.OrdinalIgnoreCase));
        if (tabIndex < 0) { tabs.Add(new(tool.Tab, [])); tabIndex = tabs.Count - 1; }
        var panels = tabs[tabIndex].Panels.ToList();
        var panelIndex = panels.FindIndex(p => p.Name.Equals(tool.Panel, StringComparison.OrdinalIgnoreCase));
        var placement = new RibbonPlacement(tool.Id, ToolNames.Display(tool.Title));
        if (panelIndex < 0) panels.Add(new(tool.Panel, [placement]));
        else panels[panelIndex] = panels[panelIndex] with { Buttons = [..panels[panelIndex].Buttons, placement] };
        tabs[tabIndex] = tabs[tabIndex] with { Panels = panels.ToArray() };
        Save(layout with { Tabs = tabs.ToArray() });
        var written = File.ReadAllBytes(file);
        try { select?.Invoke(); }
        catch
        {
            // Roll back only this operation's placement; never overwrite another edit.
            if (File.Exists(file) && File.ReadAllBytes(file).SequenceEqual(written))
            {
                if (before is null) File.Delete(file);
                else
                {
                    var restore = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try { File.WriteAllBytes(restore, before); File.Move(restore, file, true); }
                    finally { if (File.Exists(restore)) File.Delete(restore); }
                }
            }
            throw;
        }
    }
    public string RequestRevit(bool reload,Func<IReadOnlyList<RevitSession>>? sessionsProbe=null)
    {
        var sessions=sessionsProbe is null?NativeRevitCommands.Sessions():sessionsProbe();
        if(sessions.Count==0)return "저장됨 · 다음 Revit 시작 시 적용";
        var id=Guid.NewGuid().ToString("N");var errors=new Dictionary<string,string>();var sent=0;
        var layout=Read("Revit");
        foreach(var session in sessions)
        {
            var key=session.Pid+"-"+session.StartTicks;
            try {
                var ready=SafeFiles.Child(paths.Data,"UiBridgeSessions/"+session.Pid+".json");
                if(!File.Exists(ready))throw new IOException("리본 연결 업데이트 필요");
                using var document=JsonDocument.Parse(File.ReadAllText(ready));var bridge=document.RootElement;
                var version=bridge.GetProperty("Version").GetInt32();
                if(bridge.GetProperty("StartTicks").GetInt64()!=session.StartTicks)throw new IOException("리본 연결 세션 불일치");
                if(version<1 || version>4 || sessions.Count>1 && version<4)throw new IOException("여러 실행 창 적용에는 새 Revit Host가 필요합니다.");
                if(version<2 && layout.Tabs.SelectMany(t=>t.Panels).SelectMany(p=>p.Buttons).Any(b=>b.Stack is not null))throw new IOException("묶음 지원 리본 연결 업데이트 필요");
                if(version<3 && layout.Tabs.SelectMany(t=>t.Panels).SelectMany(p=>p.Buttons).Any(b=>b.Kind=="Separator"))throw new IOException("구분선 지원 리본 연결 업데이트 필요");
                var request=new {Id=id,session.Pid,session.StartTicks,Reload=reload};
                var relative=version>=4?"revit-ui-requests/"+key+".json":"revit-ui-request.json";
                SaveUiRequest(SafeFiles.Child(paths.Data,relative),request);sent++;
            } catch(Exception error) when(error is IOException or JsonException or InvalidOperationException or KeyNotFoundException) {errors[key]=error.Message;}
        }
        SaveUiRequest(SafeFiles.Child(paths.Data,"revit-ui-request-batch.json"),new {Id=id,Targets=sessions,Errors=errors});
        return "Revit 적용 요청 "+sent+"개 · 연결 확인 필요 "+errors.Count+"개 · 실행 창별 응답을 기다립니다.";
    }
    private static void SaveUiRequest<T>(string file,T value)
    {
        SafeFiles.NoLinks(file);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temporary,JsonSerializer.Serialize(value));File.Move(temporary,file,true);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    }
    private string RevitBatchResult(IReadOnlyList<RevitSession> sessions,string file)
    {
        using var document=JsonDocument.Parse(File.ReadAllText(file));var batch=document.RootElement;
        var id=batch.GetProperty("Id").GetString();var errors=batch.GetProperty("Errors");
        var targets=batch.GetProperty("Targets").Deserialize<RevitSession[]>()??[];
        var details=new List<string>();
        var values=sessions.Select(session=>{
            try {
            var key=session.Pid+"-"+session.StartTicks;
            if(errors.TryGetProperty(key,out var error)){details.Add("PID "+session.Pid+": "+error.GetString());return new SessionApplyResult(session.Pid,session.StartTicks,"failed");}
            if(!targets.Contains(session))return new SessionApplyResult(session.Pid,session.StartTicks,"pending");
            var result=SafeFiles.Child(paths.Data,$"revit-ui-results/{id}-{key}.json");
            var perSession=File.Exists(result);
            if(!File.Exists(result) && sessions.Count==1) result=SafeFiles.Child(paths.Data,"revit-ui-result.json");
            if(!File.Exists(result))return new SessionApplyResult(session.Pid,session.StartTicks,"pending");
            using var receipt=JsonDocument.Parse(File.ReadAllText(result));var value=receipt.RootElement;
            var valid=value.TryGetProperty("Id",out var actual)&&actual.GetString()==id;
            if(perSession)valid &= value.TryGetProperty("Pid",out _) && value.TryGetProperty("StartTicks",out _);
            if(value.TryGetProperty("Pid",out var pid))valid &= pid.GetInt32()==session.Pid;
            if(value.TryGetProperty("StartTicks",out var ticks))valid &= ticks.GetInt64()==session.StartTicks;
            return new SessionApplyResult(session.Pid,session.StartTicks,valid?value.GetProperty("Status").GetString()??"pending":"pending");
            } catch(Exception error) when(error is IOException or JsonException or InvalidOperationException or KeyNotFoundException){return new SessionApplyResult(session.Pid,session.StartTicks,"failed",error.Message);}
        }).ToArray();
        return SessionApplyResult.Summary("Revit 리본",values)+(details.Count==0?"":" · "+string.Join(" / ",details));
    }
    public string RevitResult(Func<RevitSession?>? sessionProbe = null,Func<IReadOnlyList<RevitSession>>? sessionsProbe=null)
    {
        var sessions=sessionsProbe is not null?sessionsProbe():sessionProbe is not null?sessionProbe() is {} one?new[]{one}:Array.Empty<RevitSession>():NativeRevitCommands.Sessions();
        var batchFile=SafeFiles.Child(paths.Data,"revit-ui-request-batch.json");
        if(sessions.Count>0 && File.Exists(batchFile))return RevitBatchResult(sessions,batchFile);
        var session=sessions.FirstOrDefault();
        if (session is null) return "저장됨 · Revit을 실행한 뒤 적용 상태를 확인하세요.";
        var requestFile=SafeFiles.Child(paths.Data,"revit-ui-request.json");
        if (!File.Exists(requestFile)) return "현재 Revit 세션의 리본 적용 요청이 없습니다. Revit 적용을 누르세요.";
        using var request=JsonDocument.Parse(File.ReadAllText(requestFile));
        var expected=request.RootElement;
        if (expected.GetProperty("Pid").GetInt32()!=session.Pid || expected.GetProperty("StartTicks").GetInt64()!=session.StartTicks)
            return "이전 Revit 세션의 기록입니다. 현재 Revit에 적용을 다시 요청하세요.";
        var resultFile=SafeFiles.Child(paths.Data,"revit-ui-result.json");
        if (!File.Exists(resultFile)) return "현재 요청 적용 대기 중 · Revit에서 실제 적용 응답을 기다리고 있습니다.";
        using var result=JsonDocument.Parse(File.ReadAllText(resultFile));
        var actual=result.RootElement;
        if (!actual.TryGetProperty("Id",out var id) || id.GetString()!=expected.GetProperty("Id").GetString())
            return "현재 요청 적용 대기 중 · 이전 적용 결과를 현재 성공으로 표시하지 않습니다.";
        var status=actual.TryGetProperty("Status",out var state) ? state.GetString() : "";
        var message=actual.TryGetProperty("Message",out var detail) ? detail.GetString() : "";
        return status switch { "applied"=>"현재 Revit 리본 적용 완료 · "+message, "failed"=>"현재 Revit 리본 적용 실패 · "+message,
            _=>"현재 요청 적용 대기 중 · "+message };
    }
}
