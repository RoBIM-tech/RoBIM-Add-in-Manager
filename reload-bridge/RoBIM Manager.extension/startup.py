# -*- coding: utf-8 -*-
"""RoBIM-owned UI bridge. Standard Idling and pyRevit Reload; no model changes."""
import io
import os
import sys
import traceback
try:
    if "robim_ribbon" in sys.modules:
        try:
            from importlib import reload as reload_module
        except ImportError:
            reload_module = reload
        reload_module(sys.modules["robim_ribbon"])
    from robim_ribbon import start
    start(__revit__)
except Exception as error:
    path = os.path.join(os.environ["LOCALAPPDATA"], "RoBIM", "Manager", "ribbon-startup-error.txt")
    with io.open(path, "w", encoding="utf-8") as stream:
        stream.write(u"{0}\n{1}".format(traceback.format_exc(), getattr(error, "InnerException", "")))
