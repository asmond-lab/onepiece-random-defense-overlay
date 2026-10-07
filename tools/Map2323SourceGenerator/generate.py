"""Derive a separately pinned 2.323 dataset from extracted members and 2.322 canonical rows.

Unchanged obfuscated functions are matched by their complete structural token stream;
changed exchange is explicitly identified by its audited branch. Abort on ambiguity.
"""
import collections
import copy
import hashlib
import json
import re
from pathlib import Path

root = Path(__file__).resolve().parents[2]
old_root = root / 'artifacts/ordr-2322/map-extracted'
new_root = root / 'artifacts/ordr-2323/map-audit/extracted'
data = root / 'Data'
old = (old_root / 'war3map.j').read_text(encoding='utf-8')
new = (new_root / 'war3map.j').read_text(encoding='utf-8')
lines = new.splitlines()
manifest = json.loads((new_root / 'extraction-manifest.json').read_text(encoding='utf-8'))
sha = hashlib.sha256(new.encode()).hexdigest()
assert sha == '9ecc275f549eadc565c7474f7d4449d9c856687e16a8108ef2ba7b586de13785'
assert (manifest['archiveBytes'], manifest['archiveSha256']) == (119514351, 'eac74a90a967473196c84446b880387857f50a828083c0e1b5fc02b325663535')
for member in manifest['members']:
    raw = (new_root / member['name']).read_bytes()
    assert len(raw) == member['bytes'] and hashlib.sha256(raw).hexdigest() == member['sha256']

keywords = {'function','takes','returns','nothing','local','set','call','if','then','else','endif','loop','exitwhen','endloop','return','endfunction','null','true','false','array'}
word = re.compile(r'\b[A-Za-z_][\w]*\b')
def structural(source):
    return word.sub(lambda m: m[0] if len(m[0]) > 8 or m[0] in keywords else '@', source)
def functions(source):
    return {m[1]: m for m in re.finditer(r'(?m)^function (\w+) takes .*?^endfunction$', source, re.S | re.M)}
old_funcs, new_funcs = functions(old), functions(new)
by_shape = collections.defaultdict(list)
for name, match in new_funcs.items():
    by_shape[structural(match[0])].append(name)
functions_map = {}
for name, match in old_funcs.items():
    matches = by_shape[structural(match[0])]
    if len(matches) == 1:
        functions_map[name] = matches[0]
assert len(functions_map) >= 1800

def mapped(name):
    if name not in functions_map:
        raise ValueError('Cannot independently identify 2.323 source function: ' + name)
    return functions_map[name]
def position(match):
    first = new.count('\n', 0, match.start()) + 1
    return first, first + match[0].count('\n')
def pin(a, b):
    return {'startLine': a, 'endLine': b, 'sha256': hashlib.sha256('\n'.join(lines[a-1:b]).encode()).hexdigest()}
def function_pin(name):
    start, end = position(new_funcs[mapped(name)])
    return mapped(name), start, end, pin(start, end)
def load(file):
    return json.loads((data / (file + '-2322.json')).read_text(encoding='utf-8'))
def save(file, contents, suffix='json'):
    name = file + '-2323.' + suffix
    raw = (json.dumps(contents, ensure_ascii=False, indent=2) + '\n').encode() if suffix == 'json' else contents.encode()
    (data / name).write_bytes(raw)
    entry = {'Identifier': file, 'File': name, 'NormalizedBytes': len(raw), 'Sha256': hashlib.sha256(raw).hexdigest()}
    print(name, len(raw), entry['Sha256'])
    return entry

def source_version(row):
    row['mapVersion'] = '2.323'
    if 'sourceSha256' in row: row['sourceSha256'] = sha
    if 'jassSha256' in row: row['jassSha256'] = sha
    return row

# Re-identify every recipe block from its literal four-character registration ID,
# and check its canonical output and condition stream against the independently
# parsed numeric-stream audit. The provenance is the actual 2.323 line range.
recipe_pat = re.compile(r'call SaveBoolean\(\w+,\$([0-9a-fA-F]{8}),1,true\)')
registrations = list(recipe_pat.finditer(new))
assert len(registrations) == 266
new_recipes = {}
for i, match in enumerate(registrations):
    rid = bytes.fromhex(match[1]).decode('ascii')
    assert rid not in new_recipes
    end = registrations[i+1].start() if i+1 < len(registrations) else new.find('endfunction', match.end())
    body = new[match.start():end]
    output = re.search(r'call SaveInteger\(\w+,\w+,\w+,\$([0-9a-fA-F]{8})\)\ncall SaveInteger\(\w+,\w+,\w+,(\$[0-9a-fA-F]+|\d+)\)',body)
    assert output, rid
    terms = [(bytes.fromhex(k).decode(), bytes.fromhex(v).decode(), int(c[1:],16) if c.startswith('$') else int(c)) for k,v,c in re.findall(r'call SaveInteger\(\w+,\w+,\w+,\$([0-9a-fA-F]{8})\)\ncall SaveInteger\(\w+,\w+,\w+,\$([0-9a-fA-F]{8})\)\ncall SaveInteger\(\w+,\w+,\w+,(\$[0-9a-fA-F]+|\d+)\)',body)]
    start = new.count('\n',0,match.start())+1
    new_recipes[rid] = (bytes.fromhex(output[1]).decode(), int(output[2][1:],16) if output[2].startswith('$') else int(output[2]), terms, start)
recipes = source_version(load('map-recipes'))
assert set(new_recipes) == {r['recipeId'] for r in recipes['recipes']}
for row in recipes['recipes']:
    rid = row['recipeId']; output, count, terms, start = new_recipes[rid]
    assert (output, count, terms) == (row['output']['id'], row['output']['count'], [(t['kind'],t['id'],t['count']) for t in row['conditions']]), rid
    end = start + 9 + 5*len(terms) - 1
    row['source'] = pin(start,end)
# Both choices and commands are separately re-extracted, not renamed historical pins.
choices = [(bytes.fromhex(a).decode(),bytes.fromhex(b).decode(),new.count('\n',0,m.start())+1)
           for m in re.finditer(r'call SaveInteger\(\w+,\$([0-9a-fA-F]{8}),\w+,\$([0-9a-fA-F]{8})\)',new)
           if (a:=m[1]) and (b:=m[2]) and bytes.fromhex(b).decode('ascii','replace') in new_recipes]
assert len(choices) == 265, len(choices)
choices_by_output = {output: (rid,line) for output,rid,line in choices}
assert len(choices_by_output) == 265
for choice in recipes['activeChoices']:
    assert choices_by_output[choice['outputId']][0] == choice['recipeId']
    choice['line'] = choices_by_output[choice['outputId']][1]
# PICK validation and consumption are distinct source-only conditions; no automatic craft approval.
for kind, evidence in recipes['conditionalHandlers'].items():
    for role, old_pin in evidence.items():
        original = '\n'.join(old.splitlines()[old_pin['startLine']-1:old_pin['endLine']])
        shape = structural(original)
        hits = [i+1 for i in range(len(lines)-len(original.splitlines())+1)
                if structural('\n'.join(lines[i:i+len(original.splitlines())])) == shape]
        assert len(hits)==1,(kind,role,len(hits))
        evidence[role] = pin(hits[0],hits[0]+len(original.splitlines())-1)
pins=[]
metadata = {'schemaVersion':1,'mapVersion':'2.323','archive':{k:manifest[k] for k in ('archive','archiveBytes','archiveSha256')}, 'members':manifest['members'], 'sourceRefs':[pin(*position(new_funcs[mapped(name)])) for name in ('epR','Rvf','AuR')]}
pins.append(save('map-source-metadata',metadata))
pins.append(save('map-recipes',recipes))

navigation=source_version(load('navigation-mechanics'))
for option in navigation['options']:
    handler=option['handler']; name,start,end,_=function_pin(handler['name'])
    handler.update(name=name,source=pin(start,end),rawSource='\n'.join(lines[start-1:end]))
    option['registrationLines']=[i+1 for i,line in enumerate(lines) if option['name'] in line and '"' in line]
    assert option['registrationLines']
for exchange in navigation['exchanges']:
    old_name=exchange['name']
    name='W5s' if old_name == 'jKC' else mapped(old_name)
    match=new_funcs[name]; start,end=position(match)
    exchange.update(name=name,source=pin(start,end),registrationLine=0,rawSource='\n'.join(lines[start-1:end]))
    # Actual ability-registration mapping has to be established before consuming an exchange handler.
    registrations_for_ability=[i+1 for i,line in enumerate(lines) if '$'+exchange['ability'].encode().hex() in line.lower() and line.startswith('call ')]
    assert registrations_for_ability,exchange['ability']
    exchange['registrationLine']=registrations_for_ability[-1]
    if old_name=='jKC':
        assert 'else\nset r5s=1' in match[0]
        exchange['cost']=3
wisp=new_funcs['W5s']; a,b=position(wisp)
navigation['doubleWispExchange'].update(failureSelectionWisps=1,failureStatus='ExplicitSingleWispElse',source=pin(a,b))
for key in ('randomBox','grantForwarding'):
    old_pins = navigation[key].get('source') if key == 'randomBox' else navigation[key]
    for old_pin in ([old_pins] if key == 'randomBox' else old_pins):
        excerpt='\n'.join(old.splitlines()[old_pin['startLine']-1:old_pin['endLine']]); shape=structural(excerpt)
        hits=[i+1 for i in range(len(lines)-len(excerpt.splitlines())+1) if structural('\n'.join(lines[i:i+len(excerpt.splitlines())]))==shape]
        assert len(hits)==1,(key,len(hits))
        new_pin=pin(hits[0],hits[0]+len(excerpt.splitlines())-1)
        if key == 'randomBox': navigation[key]['source']=new_pin
        else: old_pin.update(new_pin)
pins.append(save('navigation-mechanics',navigation))

story=source_version(load('story-progression'))
for stage in story['stages']:
    name,start,end,_=function_pin(stage['completionFunction']); delta=start-stage['source']['startLine']
    assert len(stage['rawSource'].splitlines())==end-start+1
    stage['completionFunction']=name;stage['name']=name;stage['source']=pin(start,end)
    stage['sourceLine']=start;stage['sourceStartLine']=start;stage['sourceEndLine']=end
    stage['baseSourceLine']+=delta
    stage['rawSource']='\n'.join(lines[start-1:end])
    for operation in stage['directPayoutOperations']:
        operation['sourceLine']+=delta
        operation['statement']=lines[operation['sourceLine']-1]
# Additional berry clauses require independent evidence: find structurally matching snippets.
for old_pin in story['berry']['source']:
    excerpt='\n'.join(old.splitlines()[old_pin['startLine']-1:old_pin['endLine']]); shape=structural(excerpt)
    enclosing=next((name for name,match in old_funcs.items() if old.count('\n',0,match.start())+1 <= old_pin['startLine'] <= old.count('\n',0,match.end())+1),None)
    if enclosing in functions_map:
        original_start=old.count('\n',0,old_funcs[enclosing].start())+1
        current_start=new.count('\n',0,new_funcs[mapped(enclosing)].start())+1
        hits=[current_start+old_pin['startLine']-original_start]
    else:
        hits=[i+1 for i in range(len(lines)-len(excerpt.splitlines())+1) if structural('\n'.join(lines[i:i+len(excerpt.splitlines())]))==shape]
    if old_pin['startLine'] == 5370:
        assert lines[21951] == 'if not Ss then' and '1 베리' in lines[21952]
        old_pin.update(pin(21946,21961))
        story['berry']['poneglyphSuccessImmediateConditional'] = 'not Ss; condition is not an observed player state'
    elif old_pin['startLine'] == 24262:
        assert lines[22623].startswith('if (not Ss)and ') and '1 베리' in lines[22625]
        old_pin.update(pin(22624,22627))
        story['berry']['treasureAdditionalConditional'] = 'not Ss; condition is not an observed player state'
    else:
        assert len(hits)==1 and structural('\n'.join(lines[hits[0]-1:hits[0]-1+len(excerpt.splitlines())]))==shape,('berry',old_pin,enclosing,len(hits))
        old_pin.update(pin(hits[0],hits[0]+len(excerpt.splitlines())-1))
pins.append(save('story-progression',story))

# Commands are extracted afresh and compared by semantic output and alias text.
command_rows=[(bytes.fromhex(m[1]).decode()[::-1],m[2],new.count('\n',0,m.start())+1) for m in re.finditer(r'call SaveStr\(\w+,\$([0-9a-fA-F]{8}),\w+,"([^"]*)"\)',new)]
commands=collections.defaultdict(list)
for output,alias,line in command_rows: commands[output].append((alias,line))
original_commands=(data/'map-combine-commands-2322.txt').read_text(encoding='utf8').splitlines()
expected={line.split('=',1)[0]:line.split('=',1)[1].split('|') for line in original_commands if not line.startswith('#')}
assert len(command_rows)==162 and len(commands)==81 and {k:[a for a,_ in v] for k,v in commands.items()}==expected
command_text='# schema=1\n# map=2.323\n# source-sha256='+sha+'\n# audited-results=81\n# command-aliases=162\n'+''.join(k+'='+'|'.join(a for a,_ in v)+'\n' for k,v in sorted(commands.items()))
pins.append(save('commands',command_text,'txt'))

# The same object parser as the 2.322 generator; extract byte offsets afresh.
import struct
def object_fields(member,ability):
    raw=(new_root/member).read_bytes(); offset=0
    def integer():
        nonlocal offset
        value=struct.unpack_from('<i',raw,offset)[0];offset+=4;return value
    assert integer()==2
    result=[]
    for table in range(2):
        count=integer();assert 0<=count<100000
        for _ in range(count):
            old_id,new_id=raw[offset:offset+4],raw[offset+4:offset+8];offset+=8
            fields=[]
            for _ in range(integer()):
                at=offset;field=raw[offset:offset+4].decode('ascii');offset+=4
                kind=integer()
                if ability: level,data_pointer=integer(),integer()
                else:level=data_pointer=0
                if kind==3:
                    stop=raw.index(0,offset);value=raw[offset:stop].decode('utf8','replace');offset=stop+1
                else:value=integer()
                offset+=4
                fields.append((field,level,value,at))
            result.append((new_id.decode('ascii','replace'),fields))
    assert offset==len(raw)
    return result
ability_fields=dict(object_fields('war3map.w3a',True));unit_fields=dict(object_fields('war3map.w3u',False))
wts=(new_root/'war3map.wts').read_bytes()
wts_strings={int(m[1]):m[2].strip().decode('utf8','replace') for m in re.finditer(rb'STRING (\d+)\r?\n(?:[^\r\n]*\r?\n)*?\{\r?\n(.*?)\r?\n\}',wts,re.DOTALL)}
hotkeys=[]
for choice in recipes['activeChoices']:
    registration=next(row for row in recipes['recipes'] if row['recipeId']==choice['recipeId'])
    fields=ability_fields.get(choice['recipeId'],[])
    hit=next(((value,at) for field,level,value,at in fields if field=='ahky'),None)
    if hit is None or not hit[0]:continue
    marker=re.fullmatch(r'TRIGSTR_(\d+)',hit[0]);key=wts_strings[int(marker[1])] if marker else hit[0]
    hosts=[(unit,at) for unit,fields in unit_fields.items() for field,level,value,at in fields if field=='uabi' and choice['recipeId'] in value.split(',')]
    assert hosts
    hotkeys.append({'result':choice['outputId'][::-1],'ability':choice['recipeId'],'key':key,'outputCount':registration['output']['count'],'recipeLine':registration['source']['startLine'],'hotkeyOffset':hit[1], 'hosts':[{'rawcode':unit[::-1],'abilityOffset':at} for unit,at in hosts],'hotkeyStringId':int(marker[1]) if marker else None})
old_hotkeys=load('map-combine-hotkeys')['entries']
assert [(h['result'],h['ability'],h['key'],[v['rawcode'] for v in h['hosts']]) for h in hotkeys]==[(h['result'],h['ability'],h['key'],[v['rawcode'] for v in h['hosts']]) for h in old_hotkeys]
member_hash={m['name']:m['sha256'] for m in manifest['members']}
pins.append(save('map-combine-hotkeys',{'schemaVersion':1,'mapVersion':'2.323','sourceSha256':sha,'abilitySha256':member_hash['war3map.w3a'],'unitSha256':member_hash['war3map.w3u'],'stringsSha256':member_hash['war3map.wts'],'entries':hotkeys,'commands':[{'result':key,'aliases':[alias for alias,_ in rows],'sourceLines':[line for _,line in rows]} for key,rows in sorted(commands.items())]}))

mechanics=source_version(load('map-mechanics'))
for evidence in mechanics['source']:
    snippet='\n'.join(old.splitlines()[evidence['startLine']-1:evidence['endLine']]); shape=structural(snippet)
    matches=[i+1 for i in range(len(lines)-len(snippet.splitlines())+1) if structural('\n'.join(lines[i:i+len(snippet.splitlines())]))==shape]
    assert len(matches)==1,('mechanics',evidence,len(matches))
    evidence.update(pin(matches[0],matches[0]+len(snippet.splitlines())-1))
for evidence in mechanics['boardRegistryDeltaOnly']['currentSource']:
    snippet='\n'.join(old.splitlines()[evidence['startLine']-1:evidence['endLine']]);shape=structural(snippet)
    matches=[i+1 for i in range(len(lines)-len(snippet.splitlines())+1) if structural('\n'.join(lines[i:i+len(snippet.splitlines())]))==shape]
    assert len(matches)==1,('board',evidence,len(matches))
    evidence.update(pin(matches[0],matches[0]+len(snippet.splitlines())-1))
mechanics['boardRegistryDeltaOnly']['previousScriptSha256']=hashlib.sha256(old.encode()).hexdigest()
pins.append(save('map-mechanics',mechanics))
# Growth declarations are fresh; map only individually proven semantic signals from
# complete unchanged source functions, never declaration position after obfuscation.
growth=source_version(load('map-growth-globals'))
growth_lines=old.split('endglobals')[0].splitlines()[1:]
current_lines=new.split('endglobals')[0].splitlines()[1:]
def declarations(rows):
    return {line.split('=')[0].split()[-1]:line.split('=')[0].split()[:-1] for line in rows if line.strip() and not line.startswith('//')}
old_decl=declarations(growth_lines);current_decl=declarations(current_lines)
assert len(old_decl)==3926 and len(current_decl)==3925
signals=collections.defaultdict(collections.Counter)
for original,translated in functions_map.items():
    old_tokens=word.findall(old_funcs[original][0]);new_tokens=word.findall(new_funcs[translated][0])
    if len(old_tokens)!=len(new_tokens):continue
    for old_token,new_token in zip(old_tokens,new_tokens):
        if old_token in old_decl and new_token in current_decl and old_decl[old_token]==current_decl[new_token]:
            signals[old_token][new_token]+=1
required=['yl','GR','hR','Vs','xT']
activity=source_version(load('map-activity'))
for item in activity['gambles']:
    required.extend(x for k in ('attemptsGlobal','successesGlobal','failuresGlobal') if (x:=item.get(k)))
    required.extend(item['branchGlobals'])
required.extend(x['name'] for x in activity['integerArrays'])
translate={}
for key in required:
    ranks=signals[key].most_common()
    assert ranks and (len(ranks)==1 or ranks[0][1] > ranks[1][1]*2),(key,ranks[:4])
    translate[key]=ranks[0][0]
assert len(set(translate.values()))==len(translate),(translate)
# Directly parse every current declaration and require the same JASS type coding.
by_type={tuple(old_decl[name]):type_id for name,type_id in growth['globals'].items()}
assert all(by_type[tuple(old_decl[name])]==type_id for name,type_id in growth['globals'].items())
growth['globals']={name:by_type[tuple(kind)] for name,kind in current_decl.items()}
assert growth['globals'][translate['yl']]==12
save('map-growth-globals',growth)
for item in activity['integerArrays']:
    old_name=item['name'];item['name']=translate[old_name]
    item['sourceLines']=[i+1 for i,line in enumerate(lines) if re.search(r'\b'+re.escape(item['name'])+r'\b',line)][:10]
    assert item['sourceLines'] and growth['globals'][item['name']]==9
for item in activity['recipes']:
    recipe_id=item['id'];assert recipe_id in new_recipes
    item['sourceLine']=new_recipes[recipe_id][3]
    if item['isActiveChoice']:
        item['activeChoiceSourceLine']=next(line for output,rid,line in choices if rid==recipe_id and output[::-1]==item['outputRawcode'])
    else:
        item['activeChoiceSourceLine']=None
for item in activity['wisps']+activity['gambles']:
    for function in item['sourceFunctions']:
        name,start,end,_=function_pin(function['name'])
        function.update(name=name,startLine=start,endLine=end)
    for key in ('consumptionCounterGlobal','attemptsGlobal','successesGlobal','failuresGlobal'):
        if item.get(key):item[key]=translate[item[key]]
    if 'branchGlobals' in item:item['branchGlobals']=[translate[x] for x in item['branchGlobals']]
    if 'poolNames' in item:
        item['poolNames']=[re.sub(r'^[A-Za-z_][A-Za-z0-9_]*',lambda m:signals[m[0]].most_common(1)[0][0],name) for name in item['poolNames']]
activity['sourcePath']='artifacts/ordr-2323/map-audit/extracted/war3map.j'
save('map-activity',activity)
unit_additions=source_version(load('map-unit-additions'))
yujiro=new_recipes['A0QG']
assert yujiro[:3] == ('h0C2',1,[('KING','h0C2',1),('PICK','A800',1),('UNIT','h06T',1),('UNIT','h02Y',1),('UNIT','h03Y',1),('UNIT','h06G',1),('GOLD','GOLD',10000),('WOOD','WOOD',7)])
for index,condition in enumerate(unit_additions['units'][0]['conditions']):
    condition['sourceLine']=yujiro[3]+9+5*index
save('map-unit-additions',unit_additions)
save('map-source-manifest',{'SchemaVersion':1,'MapVersion':'2.323','ArchiveSha256':manifest['archiveSha256'],'Members':pins})
