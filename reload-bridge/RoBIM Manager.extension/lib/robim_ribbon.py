# -*- coding: utf-8 -*-
import io
import json
import os
import time
import clr
clr.AddReference("AdWindows")
clr.AddReference("System.Diagnostics.Process")
clr.AddReference("System.Security.Cryptography")
clr.AddReference("PresentationFramework")
clr.AddReference("PresentationCore")
import Autodesk.Windows as AW
from System import Environment, Object, AppDomain, Guid
from System.Diagnostics import Process, ProcessStartInfo
from System.Reflection import BindingFlags
from System.Security.Cryptography import SHA256
from System.Text import Encoding
from System.Windows.Controls import Orientation
from pyrevit import script
from Autodesk.Revit.UI import IExternalEventHandler, ExternalEvent

BASE = os.path.join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RoBIM", "Manager")
_handler = None
_application = None
_last_time = 0
_fingerprint = None
_warmup = 0
_reload_event = None
_reload_request = None
_retired_events = []
_session_pid = 0
_session_ticks = 0
_owned_tabs = set([u"RoBIM 기본", u"RoBIM 도면", u"RoBIM 관리", u"RoBIM TEST"])


def read(path, default=None):
    if not os.path.isfile(path):
        return default
    if os.path.getsize(path) > 2 * 1024 * 1024:
        raise ValueError("RoBIM UI data exceeds size limit")
    with io.open(path, "r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def write(path, data):
    parent = os.path.dirname(path)
    if not os.path.isdir(parent):
        os.makedirs(parent)
    from System.IO import File
    temporary = path + ".ui.tmp"
    with io.open(temporary, "w", encoding="utf-8") as stream:
        stream.write(json.dumps(data, ensure_ascii=False))
    # Replace atomically so the Manager never reads a partial result.
    if File.Exists(path):
        File.Replace(temporary, path, None)
    else:
        File.Move(temporary, path)


def _ad(item):
    method = item.GetType().GetMethod("getRibbonItem", BindingFlags.NonPublic | BindingFlags.Instance)
    return method.Invoke(item, None) if method else None


def _api_items(items):
    for item in items:
        yield item
        if hasattr(item, "GetItems"):
            for child in _api_items(item.GetItems()):
                yield child


def _find_button(uiapp, tab, panel_name, name, prefix=False, panel_title=None):
    try:
        panels = uiapp.GetRibbonPanels(tab)
    except Exception:
        return None
    found = []
    for panel in panels:
        if panel_name and panel.Name not in (panel_name, panel_title):
            continue
        for item in _api_items(panel.GetItems()):
            if (item.Name.startswith(name) if prefix else item.Name == name):
                ad = _ad(item)
                if ad is not None:
                    found.append((item, ad))
    if not found and not prefix:
        # Revit exposes localized panel titles as Name in some pyRevit versions.
        for panel in panels:
            for item in _api_items(panel.GetItems()):
                if item.Name == name:
                    ad = _ad(item)
                    if ad is not None:
                        found.append((item, ad))
        if len(found) > 1:
            raise ValueError("Ambiguous RoBIM command name: " + name)
    return found[-1] if found else None


def _selected_root():
    location = read(os.path.join(BASE, "pyrevit-location.json"), {})
    root = location.get("Root") or os.path.join(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pyRevit", "Extensions")
    return os.path.join(root, "RoBIM-Selected.extension")


def _bindings(uiapp):
    result = {}
    missing = []
    root = _selected_root()
    data = read(os.path.join(root, "robim-button-selection.json"), {})
    selected = set(data.get("Selected", []))
    for button in data.get("Buttons", []):
        key = button["Id"]
        if key not in selected:
            continue
        pieces = key.split("/")
        tab = pieces[0].rsplit(".", 1)[0]
        panel = next((p.rsplit(".", 1)[0] for p in pieces if p.endswith(".panel")), None)
        name = pieces[-1].rsplit(".", 1)[0]
        panel_title = button.get("Group", "").split(" / ")[-1]
        binding = _find_button(uiapp, tab, panel, name, panel_title=panel_title)
        if binding is not None:
            result["robim-revit:" + key] = (binding[0], binding[1], tab, panel, button["Title"])
            _owned_tabs.add(tab)
        else:
            missing.append(key)
    def field(value, name, default=None):
        return value.get(name, value.get(name[0].lower() + name[1:], default))
    native = read(os.path.join(BASE, "NativeRevit", "active.json"), {})
    for module in field(native, "Modules", []):
        manifest = field(module, "Manifest")
        selected = set(field(module, "Selected", []))
        for command in field(manifest, "Commands", []):
            command_id = field(command, "Id")
            module_id = field(manifest, "Id")
            tab_name, panel_name = field(command, "Tab"), field(command, "Panel")
            if command_id not in selected:
                continue
            location = module_id + ":" + command_id + "|" + tab_name + "|" + panel_name
            sha = SHA256.Create()
            try:
                digest = "".join("{0:02X}".format(int(x)) for x in sha.ComputeHash(Encoding.UTF8.GetBytes(location)))[:20]
            finally:
                sha.Dispose()
            binding = _find_button(uiapp, tab_name, panel_name, "RoBIM.Native." + digest + ".", True)
            if binding is not None:
                result["native:" + module_id + ":" + command_id] = (binding[0], binding[1], tab_name, panel_name, field(command, "Title"))
                _owned_tabs.add(tab_name)
            else:
                missing.append(module_id + ":" + command_id)
    # Connection controls belong to Management and remain visible while tools are installed.
    if result:
        has_python = any(key.startswith("robim-revit:") for key in result)
        has_native = any(key.startswith("native:") for key in result)
        if has_python:
            binding = _find_button(uiapp, u"RoBIM 관리", "Manager", "ManagerReload")
            if binding is not None:
                result["manager:pyrevit-reload"] = (binding[0], binding[1], u"RoBIM 관리", "Manager", u"pyRevit\nReload")
        if has_native:
            for name, title in [("RoBIM.Native.Reload", u"독립 Add-in\nReload"), ("RoBIM.Native.Manager", "Add-in\nManager")]:
                binding = _find_button(uiapp, u"RoBIM 관리", u"독립 Add-in", name)
                if binding is not None:
                    result["manager:" + name] = (binding[0], binding[1], u"RoBIM 관리", u"독립 Add-in", title)
    write(os.path.join(BASE, "revit-ui-bindings.json"), {"Resolved": list(result.keys()), "Missing": missing})
    if missing:
        raise ValueError(u"설치된 버튼의 리본 연결을 찾지 못했습니다: " + u", ".join(missing))
    return result


def _items(collection):
    for item in list(collection):
        yield collection, item
        if hasattr(item, "Items"):
            for pair in _items(item.Items):
                yield pair


def _owner(button):
    for tab in AW.ComponentManager.Ribbon.Tabs:
        for panel in tab.Panels:
            for collection, item in _items(panel.Source.Items):
                if Object.ReferenceEquals(item, button):
                    return collection
    return None


def _tab(title):
    return next((t for t in AW.ComponentManager.Ribbon.Tabs if t.Title == title), None)


def _panel(uiapp, tab_name, panel_name):
    tab = _tab(tab_name)
    if tab is not None and tab_name not in _owned_tabs:
        raise ValueError(u"다른 제품의 탭과 이름이 겹칩니다: " + tab_name)
    if tab is None:
        uiapp.CreateRibbonTab(tab_name)
        _owned_tabs.add(tab_name)
        tab = _tab(tab_name)
    panel = next((p for p in tab.Panels if p.Source.Title == panel_name), None)
    if panel is None:
        uiapp.CreateRibbonPanel(tab_name, panel_name)
        panel = next(p for p in tab.Panels if p.Source.Title == panel_name)
    tab.IsVisible = True
    tab.IsEnabled = True
    panel.IsVisible = True
    panel.IsEnabled = True
    return tab, panel


def _visible(collection):
    for item in collection:
        if getattr(item, "IsVisible", True):
            if hasattr(item, "Items"):
                if _visible(item.Items):
                    return True
            elif getattr(item, "Id", None):
                return True
    return False


def apply_layout(uiapp):
    bindings = _bindings(uiapp)
    layout = read(os.path.join(BASE, "ribbon-layout-revit.json"), {"SchemaVersion": 1, "Host": "Revit", "Tabs": []})
    if layout.get("SchemaVersion") != 1 or layout.get("Host") != "Revit":
        raise ValueError("Invalid RoBIM ribbon layout schema")
    assigned = {}
    for tab in layout.get("Tabs", []):
        for panel in tab.get("Panels", []):
            for button in panel.get("Buttons", []):
                if button.get("Kind", "Button") == "Separator":
                    if button.get("Stack") or button.get("StackRows", 0):
                        raise ValueError("Separator cannot be inside a stacked column")
                    continue
                if button["Id"] in assigned:
                    raise ValueError("Duplicate ribbon button")
                assigned[button["Id"]] = (tab["Name"], panel["Name"], button["Title"], button["Size"], button.get("Stack"), button.get("StackRows", 0))
    specifications = {}
    for key, value in assigned.items():
        tab_name, panel_name, title, size, stack, rows = value
        if stack:
            if size != "Small" or rows not in (2, 3):
                raise ValueError("Invalid stacked ribbon column")
            group_key = (tab_name, panel_name, stack.lower())
            entry = specifications.setdefault(group_key, [rows, 0])
            if entry[0] != rows:
                raise ValueError("Mixed stacked ribbon row count")
            entry[1] += 1
    if any(count != rows for rows, count in specifications.values()):
        raise ValueError("A ribbon column requires exactly two or three buttons")
    # Resolve all destinations before moving controls. Uninstalled IDs never create tabs.
    planned = []
    destinations = {}
    for key, binding in bindings.items():
        tab, panel, title, size, stack, rows = assigned.get(key, (binding[2], binding[3], binding[4], "Large", None, 0))
        if not tab or not panel or size not in ("Large", "Small"):
            raise ValueError("Invalid ribbon destination")
        destinations[(tab, panel)] = _panel(uiapp, tab, panel)
        planned.append((key, binding, tab, panel, title, size, stack, rows))
    # Profile order controls button ordering; unconfigured buttons retain their defaults.
    order = [b["Id"] for t in layout.get("Tabs", []) for p in t.get("Panels", []) for b in p.get("Buttons", [])]
    planned.sort(key=lambda x: order.index(x[0]) if x[0] in order else len(order))
    applied = []
    stacks = {}
    for key, binding, tab_name, panel_name, title, size, stack, rows in planned:
        api, item = binding[0], binding[1]
        collection = _owner(item)
        if collection is None:
            raise ValueError("Ribbon control owner not found: " + key)
        target = destinations[(tab_name, panel_name)][1].Source.Items
        if stack:
            group_key = (tab_name, panel_name, stack.lower())
            if group_key not in stacks:
                column = AW.RibbonRowPanel()
                column.Id = "RoBIM.Stack." + stack + "." + Guid.NewGuid().ToString("N")
                target.Add(column)
                stacks[group_key] = column
            target = stacks[group_key].Items
            if target.Count:
                target.Add(AW.RibbonRowBreak())
        collection.Remove(item)
        target.Add(item)
        item.Size = AW.RibbonItemSize.Standard if stack or size == "Small" else AW.RibbonItemSize.Large
        if hasattr(item, "Orientation"):
            item.Orientation = Orientation.Horizontal if stack or size == "Small" else Orientation.Vertical
        item.ShowText = True
        api.ItemText = title
        # pyRevit cleanup can disable untouched Native controls on a shared tab.
        # Restore only resolved, installed buttons. Revit command availability still applies.
        before_enabled = bool(api.Enabled)
        api.Enabled = True
        item.IsVisible = True
        applied.append({"Id": key, "Tab": tab_name, "Panel": panel_name, "Title": title, "Size": size, "Stack": stack, "StackRows": rows, "EnabledBeforeRestore": before_enabled, "Enabled": bool(api.Enabled), "UiEnabled": bool(item.IsEnabled), "PanelEnabled": bool(destinations[(tab_name, panel_name)][1].IsEnabled)})
    # Remove obsolete Manager-owned columns after all active buttons have moved.
    functional = [binding[1] for binding in bindings.values()]
    for ribbon_tab in AW.ComponentManager.Ribbon.Tabs:
        if ribbon_tab.Title not in _owned_tabs:
            continue
        for panel in ribbon_tab.Panels:
            for old in list(panel.Source.Items):
                if isinstance(old, AW.RibbonRowPanel) and (old.Id or "").startswith("RoBIM.Stack."):
                    if not any(Object.ReferenceEquals(child, button) for collection, child in _items(old.Items) for button in functional):
                        panel.Source.Items.Remove(old)
                elif isinstance(old, AW.RibbonSeparator) and (old.Id or "").startswith("RoBIM.Separator."):
                    panel.Source.Items.Remove(old)
    separators = []
    for specification in layout.get("Tabs", []):
        for panel_spec in specification.get("Panels", []):
            destination = destinations.get((specification["Name"], panel_spec["Name"]))
            if destination is None:
                continue
            collection = destination[1].Source.Items
            configured = panel_spec.get("Buttons", [])
            for index, entry in enumerate(configured):
                if entry.get("Kind", "Button") != "Separator":
                    continue
                following = next((candidate for candidate in configured[index + 1:] if candidate["Id"] in bindings), None)
                separator = AW.RibbonSeparator()
                separator.Id = "RoBIM.Separator." + entry["Id"]
                if following is None:
                    collection.Add(separator)
                else:
                    item = bindings[following["Id"]][1]
                    if following.get("Stack"):
                        item = stacks[(specification["Name"], panel_spec["Name"], following["Stack"].lower())]
                    collection.Insert(collection.IndexOf(item), separator)
                separators.append({"Id": entry["Id"], "Tab": specification["Name"], "Panel": panel_spec["Name"], "ControlType": separator.GetType().FullName})
    for tab in AW.ComponentManager.Ribbon.Tabs:
        if tab.Title not in _owned_tabs:
            continue
        if not bindings:
            tab.IsVisible = False
            continue
        functional = [binding[1] for binding in bindings.values()]
        for panel in tab.Panels:
            panel.IsVisible = any(Object.ReferenceEquals(item, button)
                                  for collection, item in _items(panel.Source.Items)
                                  for button in functional)
        tab.IsVisible = any(p.IsVisible for p in tab.Panels)
    # Reorder only RoBIM-owned controls. Other products' controls are retained.
    ribbon_tabs = AW.ComponentManager.Ribbon.Tabs
    visible_owned = [t for t in ribbon_tabs if t.Title in _owned_tabs and t.IsVisible]
    if visible_owned:
        anchor = min(ribbon_tabs.IndexOf(t) for t in visible_owned)
        names = [t["Name"] for t in layout.get("Tabs", [])]
        ordered = sorted(visible_owned, key=lambda t: names.index(t.Title) if t.Title in names else len(names))
        for tab in visible_owned:
            ribbon_tabs.Remove(tab)
        for index, tab in enumerate(ordered):
            ribbon_tabs.Insert(anchor + index, tab)
            specification = next((t for t in layout.get("Tabs", []) if t["Name"] == tab.Title), None)
            if specification is not None:
                panel_names = [p["Name"] for p in specification.get("Panels", [])]
                panels = list(tab.Panels)
                ordered_panels = sorted(panels, key=lambda p: panel_names.index(p.Source.Title) if p.Source.Title in panel_names else len(panel_names))
                for panel in panels:
                    tab.Panels.Remove(panel)
                for panel in ordered_panels:
                    tab.Panels.Add(panel)
    columns = [{"Tab": group[0], "Panel": group[1], "Stack": group[2], "ControlType": column.GetType().FullName,
                "ButtonCount": sum(1 for item in column.Items if any(Object.ReferenceEquals(item, button) for button in functional)),
                "RowBreakCount": sum(1 for item in column.Items if isinstance(item, AW.RibbonRowBreak))}
               for group, column in stacks.items()]
    write(os.path.join(BASE, "revit-ui-state.json"), {"Pid": _session_pid, "StartTicks": _session_ticks, "Buttons": applied, "Columns": columns, "Separators": separators, "OwnedTabs": list(_owned_tabs), "VisibleTabs": [t.Title for t in AW.ComponentManager.Ribbon.Tabs if t.Title in _owned_tabs and t.IsVisible]})
    capture = os.path.join(BASE, "capture-ribbon-preview.request")
    if os.path.isfile(capture):
        os.remove(capture)
        try:
            from System.Windows.Media import PixelFormats
            from System.Windows.Media.Imaging import RenderTargetBitmap, PngBitmapEncoder, BitmapFrame
            from System.IO import FileStream, FileMode, FileAccess
            ribbon = AW.ComponentManager.Ribbon
            previous_tab = next((tab for tab in ribbon.Tabs if tab.IsActive), None)
            capture_tab = next((_tab(spec["Name"]) for spec in layout.get("Tabs", []) if _tab(spec["Name"]) is not None), None)
            if capture_tab is not None:
                capture_tab.IsActive = True
            ribbon.UpdateLayout()
            bitmap = RenderTargetBitmap(int(ribbon.ActualWidth), int(ribbon.ActualHeight), 96, 96, PixelFormats.Pbgra32)
            bitmap.Render(ribbon)
            encoder = PngBitmapEncoder()
            encoder.Frames.Add(BitmapFrame.Create(bitmap))
            stream = FileStream(os.path.join(BASE, "revit-ribbon-preview.png"), FileMode.Create, FileAccess.Write)
            try:
                encoder.Save(stream)
            finally:
                stream.Dispose()
                if previous_tab is not None:
                    previous_tab.IsActive = True
        except Exception as error:
            with io.open(os.path.join(BASE, "ribbon-capture-error.txt"), "w", encoding="utf-8") as stream:
                stream.write(str(error))
    return len(applied)


def _receipt(request, status, message):
    write(os.path.join(BASE, "revit-ui-result.json"), {"Id": request.get("Id", "startup"), "Status": status, "Message": message})


class _ReloadHandler(IExternalEventHandler):
    def GetName(self):
        return "RoBIM pyRevit Reload"

    def Execute(self, uiapp):
        try:
            from pyrevit.loader import sessionmgr
            sessionmgr.reload_pyrevit()
        except Exception as error:
            _receipt(_reload_request or {}, "failed", u"Reload 실패: {0}".format(error))


def _on_idle(sender, args):
    global _last_time, _fingerprint, _warmup
    now = time.time()
    if now - _last_time < 1:
        return
    _last_time = now
    request = None
    matched_request = False
    try:
        while _retired_events:
            _retired_events.pop().Dispose()
        current = Process.GetCurrentProcess()
        pid, ticks = int(current.Id), long(current.StartTime.ToUniversalTime().Ticks)
        current.Dispose()
        request = read(os.path.join(BASE, "revit-ui-request.json"), {})
        result = read(os.path.join(BASE, "revit-ui-result.json"), {})
        matched_request = bool(request and request.get("Pid") == pid and request.get("StartTicks") == ticks)
        pending = bool(matched_request and
                       (result.get("Id") != request.get("Id") or result.get("Status") == "reloading"))
        if pending and any(doc.IsModifiable for doc in sender.Application.Documents):
            return
        if pending and request.get("Reload") and result.get("Status") != "reloading":
            info = ProcessStartInfo()
            info.FileName = os.path.join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "RoBIM Manager", "RoBIM.Manager.exe")
            info.Arguments = "--apply-revit-reload {0} {1}".format(pid, ticks)
            # Do not inherit a pyRevit bundle directory: Windows locks a process cwd.
            info.WorkingDirectory = BASE
            info.UseShellExecute = False
            info.CreateNoWindow = True
            process = Process.Start(info)
            process.WaitForExit()
            code = process.ExitCode
            process.Dispose()
            if code != 0:
                raise ValueError(u"준비된 설치 변경 적용 실패. Manager 작업 기록을 확인하세요.")
            _receipt(request, "reloading", u"설치 변경 적용 완료 · pyRevit Reload 중")
            global _reload_request
            _reload_request = request
            # Re-registering Revit events while Idling is being raised is unsupported.
            # Execute the standard pyRevit reload in a separate ExternalEvent context.
            _reload_event.Raise()
            stop()
            return
        files = [os.path.join(BASE, "ribbon-layout-revit.json"), os.path.join(BASE, "NativeRevit", "active.json"),
                 os.path.join(_selected_root(), "robim-button-selection.json")]
        fingerprint = tuple(os.path.getmtime(p) if os.path.isfile(p) else 0 for p in files)
        if pending or fingerprint != _fingerprint or _warmup < 4:
            count = apply_layout(sender)
            _fingerprint = fingerprint
            _warmup += 1
            if pending or (matched_request and result.get("Status") == "applied"):
                _receipt(request, "applied", u"리본 적용 완료 · 설치 버튼 {0}개 · 빈 탭 숨김".format(count))
    except Exception as error:
        if matched_request:
            _receipt(request, "failed", str(error))
        script.get_logger().error("RoBIM ribbon apply failed: %s", error)


def stop():
    global _handler
    if _handler is not None and _application is not None:
        try:
            _application.Idling -= _handler
        except Exception:
            pass
    _handler = None


def start(uiapp):
    global _handler, _application, _reload_event, _session_pid, _session_ticks, _fingerprint, _warmup
    from System import EventHandler
    from Autodesk.Revit.UI.Events import IdlingEventArgs
    previous = AppDomain.CurrentDomain.GetData("RoBIM.Ribbon.Handler")
    if previous is not None:
        try:
            uiapp.Idling -= previous
        except Exception:
            pass
    process = Process.GetCurrentProcess()
    _session_pid = int(process.Id)
    _session_ticks = long(process.StartTime.ToUniversalTime().Ticks)
    process.Dispose()
    host_dll = os.path.join(BASE, "NativeRevit", "Host", "RoBIM.Revit.Host.bundle", "Contents", "2026", "RoBIM.Revit.Host.dll")
    write(os.path.join(BASE, "revit-host-file-check.json"), {"Pid": _session_pid, "StartTicks": _session_ticks, "HostDll": host_dll, "Exists": os.path.isfile(host_dll)})
    state = read(os.path.join(BASE, "revit-ui-state.json"), {})
    if state.get("Pid") == _session_pid and state.get("StartTicks") == _session_ticks:
        for name in state.get("OwnedTabs", []):
            _owned_tabs.add(name)
    # Run after pyRevit UI cleanup, including repeated starts in a reused engine.
    _fingerprint = None
    _warmup = 0
    _application = uiapp
    previous_event = AppDomain.CurrentDomain.GetData("RoBIM.Ribbon.ReloadEvent")
    if previous_event is not None:
        _retired_events.append(previous_event)
    _reload_event = ExternalEvent.Create(_ReloadHandler())
    AppDomain.CurrentDomain.SetData("RoBIM.Ribbon.ReloadEvent", _reload_event)
    _handler = EventHandler[IdlingEventArgs](_on_idle)
    AppDomain.CurrentDomain.SetData("RoBIM.Ribbon.Handler", _handler)
    uiapp.Idling += _handler
    write(os.path.join(BASE, "UiBridgeSessions", str(_session_pid) + ".json"), {"Version": 3, "StartTicks": _session_ticks})
