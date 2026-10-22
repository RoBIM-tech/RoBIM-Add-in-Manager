# -*- coding: utf-8 -*-
"""RoBIM-owned UI bridge. Standard Idling and pyRevit Reload; no model changes."""
import io
import os
import traceback
try:
    from robim_ribbon import start
    start(__revit__)
except Exception as error:
    path = os.path.join(os.environ["LOCALAPPDATA"], "RoBIM", "Manager", "ribbon-startup-error.txt")
    with io.open(path, "w", encoding="utf-8") as stream:
        stream.write(u"{0}\n{1}".format(traceback.format_exc(), getattr(error, "InnerException", "")))
