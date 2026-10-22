"""Require every distributed native/CAD command to be in the embedded Manager catalog."""
import argparse
import json
import re
from pathlib import Path

def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))

def validate_catalog(catalog):
    if catalog.get('schemaVersion') != 1: raise ValueError('Catalog schema must be 1')
    packages, commands = set(), set()
    for repo in catalog['repositories']:
        if not re.fullmatch(r'[A-Za-z0-9-]+/[A-Za-z0-9_.-]+', repo['repository']): raise ValueError('Invalid repository')
        for package in repo['packages']:
            key=(repo['repository'].lower(), package['id'])
            if key in packages: raise ValueError('Duplicate package')
            packages.add(key)
            if package['host'] not in ('revit','autocad') or package['distribution'] != 'release': raise ValueError('New tools must be independent release packages')
    repositories={r['repository'].lower():r for r in catalog['repositories']}
    if len(repositories)!=len(catalog['repositories']): raise ValueError('Duplicate repository')
    for name,repo in repositories.items():
        declared=[c for c in catalog['commands'] if c['repository'].lower()==name]
        if any(p['host']=='revit' for p in repo['packages']):
            if len(repo['packages'])!=1 or len(declared)!=1: raise ValueError('One native button/package per repository required: '+repo['repository'])
        elif name!='gagaggan/robim-cad' and (len(repo['packages'])!=1 or len(declared)!=1):
            raise ValueError('New button repositories must own one command')
    for command in catalog['commands']:
        key=(command['host'],command['moduleId'],command['commandId'])
        if key in commands: raise ValueError('Duplicate global command ID')
        commands.add(key)
        if (command['repository'].lower(),command['packageId']) not in packages: raise ValueError('Unregistered package')
        for value in (command['moduleId'],command['commandId']):
            if not re.fullmatch(r'[a-z][a-z0-9.-]{0,63}',value): raise ValueError('Invalid command ID')

def validate_source(catalog, source, host, repository):
    validate_catalog(catalog)
    if host == 'revit':
        candidates=[source/'package/robim-module.json',source/'robim-module.json']
        manifest=next((p for p in candidates if p.is_file()),None)
        if manifest is None: raise ValueError('robim-module.json missing')
        module=read(manifest)
        definitions=[(module['id'],c['id']) for c in module['commands']]
        if len(definitions)!=1: raise ValueError('One command per native button repository required')
        release=read(manifest.parent/'robim-release.json')
        entries={m['id']:m for m in release['modules']}
        maturity=entries[module['id']]
        if maturity['version'] != module['version']: raise ValueError('Release/module version mismatch')
        if maturity['channel'] not in ('test','stable'): raise ValueError('Explicit test/stable channel required')
        if maturity['channel']=='stable':
            if any('-' in d['version'].split('+')[0] for d in maturity.get('libraries',[])): raise ValueError('Stable cannot depend on TEST libraries')
            v=maturity.get('validation',{})
            if '-' in maturity['version'].split('+')[0] or not all(v.get(k) for k in ('by','hostBuild','evidence')) or not v['evidence'].startswith('https://'): raise ValueError('Stable needs host verification evidence')
        descriptor=read(source/'robim-package.json')
        packageIds={p['id'] for p in descriptor['packages'] if p['host']=='revit' and p['distribution']=='release'}
        if len(descriptor['packages'])!=1 or len(packageIds)!=1: raise ValueError('One native package per button repository required')
        central=[r for r in catalog['repositories'] if r['repository']==repository]
        if len(central)!=1 or central[0]['packages']!=descriptor['packages']: raise ValueError('Source/Manager package definitions differ')
        import xml.etree.ElementTree as ET
        libraries={x['id']:x['version'] for x in maturity.get('libraries',[])}
        for project in (source/'src').rglob('*.csproj'):
            tree=ET.parse(project)
            for ref in tree.findall('.//ProjectReference'):
                target=(project.parent/ref.attrib['Include']).resolve()
                if not target.is_relative_to(source.resolve()): raise ValueError('Cross-repository ProjectReference forbidden; use a pinned package')
            for ref in tree.findall('.//PackageReference'):
                name=ref.attrib.get('Include','')
                if name.startswith('RoBIM.'):
                    version=ref.attrib.get('Version','')
                    if not re.fullmatch(r'\[[^,\[\]*]+\]',version) or libraries.get(name)!=version[1:-1]: raise ValueError('RoBIM dependency must be exactly pinned and declared: '+name)

    elif host=='autocad':
        manifest=source/'bundle/DelHiddenNet.bundle/Contents/Windows/robim-cad.json'
        definitions=[(m['id'],a['id']) for m in read(manifest)['modules'] for a in m['actions']]
        packageIds={p['id'] for r in catalog['repositories'] if r['repository']==repository for p in r['packages'] if p['host']=='autocad'}
    else: raise ValueError('New pyRevit commands are not accepted; use a native module')
    registered={(c['moduleId'],c['commandId']) for c in catalog['commands'] if c['host']==host and c['repository']==repository and c['packageId'] in packageIds}
    missing=set(definitions)-registered
    if missing: raise ValueError('Manager registration missing: '+repr(sorted(missing)))
    if len(definitions)!=len(set(definitions)): raise ValueError('Duplicate command ID in source')
    return len(definitions)

if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--catalog',type=Path,default=Path(__file__).resolve().parents[1]/'catalog/modules.json')
    p.add_argument('--source',type=Path)
    p.add_argument('--host',choices=['revit','autocad'])
    p.add_argument('--repository')
    a=p.parse_args()
    catalog=read(a.catalog); validate_catalog(catalog)
    if a.source:
        if not a.host or not a.repository: p.error('--source requires --host and --repository')
        print('PASS registered commands:',validate_source(catalog,a.source.resolve(),a.host,a.repository))
    else: print('PASS central Manager catalog')
