"""Exercise the real bridge resolver and Idling retry code without opening a model."""
import ast
from pathlib import Path
from types import SimpleNamespace as N

path = Path(__file__).resolve().parents[1] / 'reload-bridge/RoBIM Manager.extension/lib/robim_ribbon.py'
tree = ast.parse(path.read_text(encoding='utf-8-sig'))
names = {'_api_items', '_find_button', '_find_selected_ad', '_items', '_AdButton', '_ApplyRetry', '_on_idle', '_selected_executor', '_restore_selected_button'}
body = [n for n in tree.body if isinstance(n, (ast.FunctionDef, ast.ClassDef)) and n.name in names]
scope = {}
exec(compile(ast.Module(body=body, type_ignores=[]), str(path), 'exec'), scope)

def control(identity, text='Other caption'):
    return N(Id=identity, Text=text, IsEnabled=False, CommandHandler=object())

key = 'RoBIM 관리.tab/Links.panel/LinkCAD.stack/Relink Paths.pushbutton'
target = control('CustomCtrl_%CustomCtrl_%RoBIM 관리%Links%LinkCAD%Relink Paths')
foreign = control('CustomCtrl_%CustomCtrl_%Other%Links%LinkCAD%Relink Paths')
same_caption = control('CustomCtrl_%CustomCtrl_%RoBIM 관리%Links%Other command', 'Relink Paths')
items = [N(Items=[target]), foreign, same_caption]
scope['AW'] = N(ComponentManager=N(Ribbon=N(Tabs=[N(Panels=[N(Source=N(Items=items))])])) )
binding = scope['_find_selected_ad'](key)
assert binding[1] is target
binding[0].ItemText = 'Changed title'; binding[0].Enabled = True
assert target.Text == 'Changed title' and target.IsEnabled and not foreign.IsEnabled
assert scope['_find_selected_ad'](key)[1] is target, 'Display rename broke identity'
print('PASS stack control: exact internal ID, localized source, display rename, foreign control preserved')

# Revit may flatten the virtual .stack in its generated control ID.
target.Id = 'CustomCtrl_%CustomCtrl_%RoBIM 관리%Links%Relink Paths'
assert scope['_find_selected_ad'](key)[1] is target
target.Id = 'CustomCtrl_%CustomCtrl_%RoBIM 관리%링크%Relink Paths'
assert scope['_find_selected_ad'](key,'링크')[1] is target
target.Id = 'CustomCtrl_%CustomCtrl_%RoBIM 관리%Links%Relink Paths'
duplicate = control(target.Id)
items.append(duplicate)
try: scope['_find_selected_ad'](key)
except ValueError: pass
else: raise AssertionError('Ambiguous control accepted')
items.remove(duplicate)
assert scope['_find_selected_ad'](key.replace('Relink Paths', 'Not installed')) is None
print('PASS flattened stack ID, ambiguity rejected, absent command never guessed by caption')

# API omission is independent of the selected AdWindows control still being present.
scope['_ad'] = lambda value: value.Ad
assert scope['_find_button'](N(GetRibbonPanels=lambda tab: [N(Name='링크', GetItems=lambda: [])]), 'RoBIM 관리', 'Links', 'Relink Paths') is None
assert scope['_find_selected_ad'](key)[1] is target

retry = scope['_ApplyRetry'](); reports = 0
for now in (1, 3, 7, 15):
    assert retry.ready(('startup', 1), now)
    reports += retry.failed(('startup', 1), now, 'Same missing control')
assert reports == 1 and not retry.ready(('startup', 1), 9999)
assert retry.ready(('new-request', 1), 10000)
retry.succeeded(); assert retry.ready(('new-request', 1), 10001)
print('PASS bounded backoff: four attempts, one duplicate error log, explicit retry and success reset')

# Run the real Idling handler. No UI callbacks or file writes are replaced by fake model actions.
clock = {'now': 0}; state = {'request': {}, 'result': {}}; attempts = []; errors = []; receipts = []; writes = []
def apply(sender):
    attempts.append(clock['now'])
    if len(attempts) <= 4: raise ValueError('Missing Relink Paths')
    return 14
def read(filename, default):
    return state['request'] if filename.endswith('revit-ui-request.json') else state['result']
scope.update(long=int, time=N(time=lambda: clock['now']), _last_time=0, _retired_events=[],
    Process=N(GetCurrentProcess=lambda: N(Id=11, StartTime=N(ToUniversalTime=lambda: N(Ticks=22)), Dispose=lambda: None)),
    read=read, os=N(path=N(join=lambda *parts: '/'.join(parts), getmtime=lambda path: 1, isfile=lambda path: True)),
    BASE='base', _selected_root=lambda: 'selected', _fingerprint=None, _warmup=0,
    _retry=scope['_ApplyRetry'](), apply_layout=apply, _session_pid=11, _session_ticks=22,
    write=lambda name, data: writes.append((name, data)), _receipt=lambda request,status,message: receipts.append((status,message)),
    script=N(get_logger=lambda: N(error=lambda *args: errors.append(args))))
sender = N(Application=N(Documents=[]))
for now in range(1, 100):
    clock['now'] = now; scope['_on_idle'](sender, None)
assert attempts == [1, 3, 7, 15] and len(errors) == 1, (attempts, errors)
assert writes[-1][1]['RetryPaused'] is True
state.update(request={'Id':'retry','Pid':11,'StartTicks':22,'Reload':False}, result={'Id':'retry','Status':'failed'})
clock['now']=100; scope['_on_idle'](sender, None)
assert len(attempts)==5 and receipts[-1][0]=='applied', 'Successful retry kept old failure receipt'
assert writes[-1][1]['Status']=='resolved' and not writes[-1][1]['RetryPaused'], 'Recovered error remained marked as paused'
print('PASS real Idling: log flood stopped, no endless UI retries, new request recovers failed receipt')

# A missing stack button can be restored using its already loaded command class.
import sys
from unittest.mock import patch
from types import ModuleType
fake_ui = ModuleType('Autodesk.Revit.UI')
class Data:
    def __init__(self, name, title, assembly, command):
        self.Name=name; self.Title=title; self.Assembly=assembly; self.Command=command
fake_ui.PushButtonData=Data
api_items=[]; created=[]
def add(data):
    created.append(data)
    api=N(Name=data.Name, ItemText=data.Title, Ad=control('CustomCtrl_%RoBIM 관리%링크%Relink Paths'))
    api_items.append(api); return api
panel=N(Name='링크',GetItems=lambda:api_items,AddItem=add)
availability=N(FullName='selected_relink_avail')
assembly=N(Location='already-loaded.dll',GetType=lambda name,*args:availability if name.endswith('_avail') else None)
restore_scope=dict(scope, _selected_executor=lambda root,key:(assembly,N(FullName='selected_relink'),'selected_relink'))
exec(compile(ast.Module(body=[n for n in body if isinstance(n,ast.FunctionDef) and n.name=='_restore_selected_button'],type_ignores=[]),str(path),'exec'),restore_scope)
with patch.dict(sys.modules, {'Autodesk.Revit.UI':fake_ui}):
    actual=restore_scope['_restore_selected_button'](N(GetRibbonPanels=lambda tab:[panel]),'root',key,'링크',{'Title':'Relink Paths','Description':'Selected command'})
    assert actual[0] is api_items[0] and created[0].Command=='selected_relink'
    assert created[0].AvailabilityClassName=='selected_relink_avail'
    assert restore_scope['_restore_selected_button'](N(GetRibbonPanels=lambda tab:[panel]),'root',key,'링크',{'Title':'Relink Paths'}) is None
    assert len(created)==1
print('PASS missing single stack carrier restored once, using existing executor/availability without executing command')

# Resolve only a current-generation IExternalCommand for the exact active script path.
import os
fake_components=ModuleType('pyrevit.extensions.genericcomps')
fake_components.GenericUIComponent=N(make_unique_name=lambda directory:'selected_relink')
fake_loader=ModuleType('pyrevit.loader')
fake_loader.sessioninfo=N(get_loaded_pyrevit_assemblies=lambda:['current'])
fake_ui.IExternalCommand=type('IExternalCommand',(),{})
command=N(IsAbstract=False,IsCommand=True,FullName='Selected_Relink')
old=N(GetName=lambda:N(Name='old'),GetType=lambda *args:command)
current=N(GetName=lambda:N(Name='current'),GetType=lambda name,throws,ignore:command if ignore and name.lower()==command.FullName.lower() else None)
scope.update(os=N(path=N(abspath=os.path.abspath,join=os.path.join,isfile=lambda p:True),sep=os.sep),
    clr=N(GetClrType=lambda interface:N(IsAssignableFrom=lambda value:value.IsCommand)),
    AppDomain=N(CurrentDomain=N(GetAssemblies=lambda:[old,current])))
with patch.dict(sys.modules,{'pyrevit.extensions.genericcomps':fake_components,'pyrevit.loader':fake_loader,'Autodesk.Revit.UI':fake_ui}):
    assert scope['_selected_executor'](os.path.abspath('active'),key)[0] is current
    assert scope['_selected_executor'](os.path.abspath('active'),key)[2] == 'Selected_Relink'
    try: scope['_selected_executor'](os.path.abspath('active'),'../escape.pushbutton')
    except ValueError: pass
    else: raise AssertionError('Escaping script path accepted')
    command.IsCommand=False
    assert scope['_selected_executor'](os.path.abspath('active'),key) is None
print('PASS current assembly only, path traversal rejected, non-command class refused')
