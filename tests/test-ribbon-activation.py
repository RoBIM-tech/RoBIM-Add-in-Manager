"""Exercise real layout code after a shared-tab pyRevit cleanup, without Revit."""
import ast
import os
from pathlib import Path
from types import SimpleNamespace as N

source = Path(__file__).resolve().parents[1] / 'reload-bridge' / 'RoBIM Manager.extension' / 'lib' / 'robim_ribbon.py'
tree = ast.parse(source.read_text(encoding='utf-8-sig'))
functions = {'_items', '_owner', '_tab', '_panel', 'apply_layout'}
code = compile(ast.Module(body=[n for n in tree.body if isinstance(n, ast.FunctionDef) and n.name in functions], type_ignores=[]), str(source), 'exec')

class Collection(list):
    def Add(self, item): self.append(item)
    def Remove(self, item): self.remove(item)
    def Insert(self, index, item): self.insert(index, item)
    def IndexOf(self, item): return self.index(item)
    @property
    def Count(self): return len(self)

class ApiButton:
    def __init__(self, item): self.item = item
    @property
    def Enabled(self): return self.item.IsEnabled
    @Enabled.setter
    def Enabled(self, value): self.item.IsEnabled = value

class Row:
    def __init__(self): self.Items = Collection()
    def GetType(self): return N(FullName='Autodesk.Windows.RibbonRowPanel')
class Separator: pass
class Break: pass

def button(name): return N(Id=name, IsEnabled=False, IsVisible=False, Orientation=None)
def panel(name, items=()): return N(Source=N(Title=name, Items=Collection(items)), IsEnabled=False, IsVisible=False)
def tab(name, panels): return N(Title=name, Panels=Collection(panels), IsEnabled=False, IsVisible=False)

reload, manager, foreign, removed = [button(name) for name in ['Reload', 'Manager', 'Foreign', 'Removed']]
controls = panel('Native', [reload, manager, removed])
foreign_panel = panel('Foreign', [foreign])
owned_tab = tab('RoBIM Management', [controls])
foreign_tab = tab('Other Product', [foreign_panel])
ribbon = N(Tabs=Collection([owned_tab, foreign_tab]))
bindings = {'manager:RoBIM.Native.Reload': (ApiButton(reload), reload, owned_tab.Title, 'Native', 'Reload'),
            'manager:RoBIM.Native.Manager': (ApiButton(manager), manager, owned_tab.Title, 'Native', 'Manager')}
layout = {'SchemaVersion': 1, 'Host': 'Revit', 'Tabs': [{'Name': owned_tab.Title, 'Panels': [{'Name': 'Native', 'Buttons': [
    {'Id': key, 'Title': key, 'Size': 'Large'} for key in bindings]}]},
    {'Name': 'Uninstalled', 'Panels': [{'Name': 'Tools', 'Buttons': [{'Id': 'native:missing:run', 'Title': 'Missing', 'Size': 'Large'}]}]}]}
written = {}
class Ui:
    def CreateRibbonTab(self, title): raise AssertionError('Must not create a tab for uninstalled tools')
    def CreateRibbonPanel(self, tab, title): raise AssertionError('Existing panel should be reused')

scope = {'AW': N(ComponentManager=N(Ribbon=ribbon), RibbonRowPanel=Row, RibbonSeparator=Separator, RibbonRowBreak=Break,
                 RibbonItemSize=N(Standard=0, Large=1)),
         'Object': N(ReferenceEquals=lambda a,b: a is b), 'Orientation': N(Horizontal=0, Vertical=1),
         '_owned_tabs': {owned_tab.Title}, '_bindings': lambda app: bindings,
         'read': lambda path, default=None: layout, 'write': lambda path, data: written.update({path: data}),
         'os': os, 'BASE': str(Path(__file__).parent), '_session_pid': 1, '_session_ticks': 2}
exec(code, scope)
assert scope['apply_layout'](Ui()) == 2
assert reload.IsEnabled and manager.IsEnabled, 'Visible Native controls remained disabled after cleanup'
assert controls.IsEnabled and owned_tab.IsEnabled, 'Disabled parent blocks otherwise enabled buttons'
assert not foreign.IsEnabled and not foreign_panel.IsEnabled and not foreign_tab.IsEnabled, 'Changed another product'
assert not removed.IsEnabled and not removed.IsVisible, 'Reactivated an uninstalled control'
assert len(ribbon.Tabs) == 2 and len(controls.Source.Items) == 3, 'Created unwanted controls'
state = next(v for k,v in written.items() if k.endswith('revit-ui-state.json'))
assert all(not b['EnabledBeforeRestore'] and b['Enabled'] and b['UiEnabled'] and b['PanelEnabled'] for b in state['Buttons'])
assert scope['apply_layout'](Ui()) == 2 and len(controls.Source.Items) == 3, 'Repeated apply duplicates controls'
print('PASS shared-tab cleanup recovery: buttons and parents enabled; foreign/removed controls preserved; repeated apply stable')

# Management controls use the same saved destination, caption and column policy as tools.
destination = panel('My Controls')
owned_tab.Panels.Add(destination)
for entry in layout['Tabs'][0]['Panels'][0]['Buttons']:
    entry.update(Title='Edited ' + entry['Id'], Size='Small', Stack='controls', StackRows=2)
layout['Tabs'][0]['Panels'][0]['Name'] = destination.Source.Title
scope['Guid'] = N(NewGuid=lambda: N(ToString=lambda fmt: 'test-column'))
assert scope['apply_layout'](Ui()) == 2
assert len(destination.Source.Items) == 1 and isinstance(destination.Source.Items[0], Row)
column = destination.Source.Items[0]
assert len(column.Items) == 3 and column.Items[0] is reload and column.Items[2] is manager
assert reload.Size == 0 and manager.Size == 0
assert all(b[0].ItemText.startswith('Edited ') for b in bindings.values())
assert not controls.IsVisible and destination.IsVisible
assert scope['apply_layout'](Ui()) == 2 and len(destination.Source.Items) == 1
print('PASS management editing: moved panel, custom captions, stacked two-row controls, repeated apply without duplicates')

feature = button('Feature')
controls.Source.Items.Add(feature)
fallback_tab = tab('RoBIM 도구', [panel('도구')])
ribbon.Tabs.Add(fallback_tab)
scope['_owned_tabs'].add(fallback_tab.Title)
bindings['native:no-panel:run'] = (ApiButton(feature), feature, 'RoBIM 관리', '독립 Add-in', 'Feature')
assert scope['apply_layout'](Ui()) == 3
assert feature in fallback_tab.Panels[0].Source.Items, 'Unplaced feature entered Management'
assert feature not in controls.Source.Items
print('PASS unplaced feature fallback: management remains reserved for connection controls')
