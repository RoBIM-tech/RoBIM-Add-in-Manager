"""Historical compatibility regression only; no pyRevit runtime is operated."""
import ast
from pathlib import Path
from types import SimpleNamespace as N

path=Path(__file__).resolve().parents[1]/'reload-bridge/RoBIM Manager.extension/lib/robim_ribbon.py'
tree=ast.parse(path.read_text(encoding='utf-8'))
nodes=[n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name in ('_items','_retire_unselected_python')]
scope={};exec(compile(ast.Module(body=nodes,type_ignores=[]),str(path),'exec'),scope)
def button(identity):return N(Id=identity,CommandHandler=object(),IsVisible=True,IsEnabled=True)
stale=[button('CustomCtrl_%CustomCtrl_%RoBIM 기본%Elements%AutoJoin'),
       button('CustomCtrl_%CustomCtrl_%RoBIM 관리%Links%Relink Paths'),
       button('CustomCtrl_%CustomCtrl_%RoBIM 기본%Views%ViewRotation')]
duplicate=button(stale[0].Id)
current=button('CustomCtrl_%CustomCtrl_%RoBIM 기본%Elements%BeamToSlab')
native=button('CustomCtrl_%CustomCtrl_%RoBIM TEST%Native%RoBIM.Native.123.1')
foreign=button('CustomCtrl_%CustomCtrl_%Other Product%Elements%AutoJoin')
caption_only=button('OtherButton');caption_only.Text='AUTOJOIN'
container=N(Items=stale+[duplicate,current,native,foreign,caption_only])
scope.update(AW=N(ComponentManager=N(Ribbon=N(Tabs=[N(Panels=[N(Source=N(Items=[container]))])]))),
             Object=N(ReferenceEquals=lambda a,b:a is b))
bindings={'robim-revit:RoBIM 기본.tab/Elements.panel/BeamToSlab.pushbutton':(None,current)}
assigned={'robim-revit:RoBIM 管理.tab/Old.panel/Old.pushbutton':None}
retired=scope['_retire_unselected_python'](bindings,assigned)
assert len(retired)==4 and all(not b.IsVisible and not b.IsEnabled for b in stale+[duplicate])
assert all(b.IsVisible and b.IsEnabled for b in (current,native,foreign,caption_only))
assert scope['_retire_unselected_python'](bindings,assigned)==retired
# Reinstalling the original button restores it through the normal selected binding path.
stale[0].IsVisible=stale[0].IsEnabled=True
bindings['robim-revit:RoBIM 기본.tab/Elements.panel/AutoJoin.pushbutton']=(None,stale[0])
scope['_retire_unselected_python'](bindings,assigned)
assert stale[0].IsVisible and stale[0].IsEnabled
print('PASS removed/nested/duplicate legacy controls retired; selected/native/foreign/caption-only preserved; repeated apply and reinstall safe')

# Read-only historical root discovery must never start the retired CLI.
core = Path(__file__).resolve().parents[1] / 'Manager.Core'
probe = (core / 'EnvironmentProbe.cs').read_text(encoding='utf-8')
roots = probe.split('public static Task<IReadOnlyList<string>> RevitRoots()', 1)[1].split('public static string PyRevitExecutable', 1)[0]
assert 'ProcessRunner.Run' not in roots and 'PyRevitExecutable(' not in roots
profiles = (core / 'RevitProfiles.cs').read_text(encoding='utf-8')
for signature in ('public async Task Apply(bool developer)', 'public async Task ApplySelection()'):
    body = profiles.split(signature, 1)[1].split('\n    }', 1)[0]
    assert body.index('RevitRuntimePolicy.EnsureAllowed') < body.index('ensureClosed')
print('PASS Native-only discovery and profile gates prevent legacy CLI execution')
