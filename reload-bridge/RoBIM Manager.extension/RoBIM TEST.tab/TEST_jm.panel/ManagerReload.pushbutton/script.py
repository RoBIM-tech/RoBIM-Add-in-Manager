# -*- coding: utf-8 -*-
"""Apply a prepared RoBIM package, then reload using the native pyRevit API."""
import io
import json
import os

from System import Environment
from System.Diagnostics import Process, ProcessStartInfo, FileVersionInfo
from pyrevit import forms, script
from pyrevit.loader import sessionmgr


def main():
    # No transaction or model editing: run this command after closing RoBIM tool windows.
    if any(doc.IsModifiable for doc in __revit__.Application.Documents):
        forms.alert(u"실행 중인 모델 작업이 있습니다. 작업을 마친 뒤 다시 실행하십시오.")
        return
    if not forms.alert(
            u"열린 RoBIM 도구 창을 닫았는지 확인하십시오.\n"
            u"매니저에서 준비한 변경을 적용하고 pyRevit Reload를 실행합니다.\n"
            u"Revit 모델을 저장하거나 수정하지 않습니다. 계속하시겠습니까?",
            yes=True, no=True, ok=False):
        return
    base = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
    exe = os.path.join(base, "Programs", "RoBIM Manager", "RoBIM.Manager.exe")
    if not os.path.isfile(exe):
        forms.alert(u"RoBIM Manager Preview 3 이상을 먼저 설치하십시오.")
        return
    version = FileVersionInfo.GetVersionInfo(exe).ProductVersion or ""
    if not version or version.startswith("1.0.0-preview.1") or version.startswith("1.0.0-preview.2"):
        forms.alert(u"Reload 연결에는 RoBIM Manager Preview 3 이상이 필요합니다.")
        return
    current = Process.GetCurrentProcess()
    info = ProcessStartInfo()
    info.FileName = exe
    info.Arguments = "--apply-revit-reload {0} {1}".format(
        current.Id, current.StartTime.ToUniversalTime().Ticks)
    info.UseShellExecute = False
    info.CreateNoWindow = True
    process = Process.Start(info)
    # Do not time out and leave a background apply running: wait until its result is final.
    process.WaitForExit()
    code = process.ExitCode
    process.Dispose()
    current.Dispose()
    if code != 0:
        message = u"업데이트 적용에 실패했습니다. 매니저에서 다시 준비하십시오."
        result = os.path.join(base, "RoBIM", "Manager", "reload-result.json")
        if os.path.isfile(result):
            try:
                with io.open(result, "r", encoding="utf-8-sig") as stream:
                    message = json.load(stream).get("Message", message)
            except Exception:
                pass
        forms.alert(message, title="RoBIM Reload")
        return
    try:
        sessionmgr.reload_pyrevit()
    except Exception as error:
        script.get_logger().exception("RoBIM files applied, pyRevit reload failed")
        forms.alert(u"파일 적용 후 Reload에 실패했습니다. pyRevit Reload를 다시 실행하거나 Revit을 재시작하십시오.\n" + str(error))


main()
