"""Generate complete 2.322 active recipes, commands and source-scoped profiles."""
import hashlib
import json
import re
import struct
from pathlib import Path

root = Path(__file__).resolve().parents[2]
source = root / 'artifacts/ordr-2322/map-extracted/war3map.j'
manifest = root / 'artifacts/ordr-2322/map-extracted/extraction-manifest.json'
data = root / 'Data'
s = source.read_bytes()
sha = hashlib.sha256(s).hexdigest()
assert sha == '6805a1def612d237ba0046a5000f8a2514e928445c8071ecb734b874fa39b47d'
lines = s.decode('utf-8').splitlines()
receipt = json.loads(manifest.read_text(encoding='utf-8'))
assert receipt['archiveBytes'] == 119516025 and receipt['archiveSha256'] == '93baeb58a6d7ad7e25c7e8a44a345f0c39b5907aa9c1774995730d4fbd101d9b'
assert next(m for m in receipt['members'] if m['name'] == 'war3map.j')['sha256'] == sha
for member in receipt['members']:
    extracted = source.parent / member['name']
    raw = extracted.read_bytes()
    assert len(raw) == member['bytes'] and hashlib.sha256(raw).hexdigest() == member['sha256'], member['name']

def evidence(a, b):
    text = '\n'.join(lines[a - 1:b])
    return {'startLine': a, 'endLine': b, 'sha256': hashlib.sha256(text.encode()).hexdigest()}

def save(name, value):
    text = json.dumps(value, ensure_ascii=False, indent=2) + '\n'
    (data / name).write_bytes(text.encode('utf-8'))
    return {'Identifier': name.split('-2322')[0], 'File': name, 'NormalizedBytes': len(text.encode()), 'Sha256': hashlib.sha256(text.encode()).hexdigest()}

pins = []
pins.append(save('map-source-metadata-2322.json', {'schemaVersion': 1, 'mapVersion': '2.322', 'archive': {k: receipt[k] for k in ('archive', 'archiveBytes', 'archiveSha256')}, 'members': receipt['members'], 'sourceRefs': [evidence(*r) for r in [(61986, 62034), (48957, 48977), (19100, 19126), (50975, 51000), (5370, 5382), (24262, 24265), (72808, 72821)]]}))
def code(hex_value):
    return bytes.fromhex(hex_value).decode('ascii')

def app(source_code):
    return source_code[::-1]

script = s.decode('utf-8')
registrations = list(re.finditer(r'call SaveBoolean\(VR,\$([0-9a-fA-F]{8}),1,true\)', script))
assert len(registrations) == 266
recipes = []
for i, registration in enumerate(registrations):
    start = script.count('\n', 0, registration.start()) + 1
    after = registrations[i+1].start() if i+1 < len(registrations) else script.find('endfunction', registration.end())
    block = script[registration.start():after].rstrip()
    output = re.search(r'call SaveInteger\(yR,LR,MR,\$([0-9a-fA-F]{8})\)\ncall SaveInteger\(AR,LR,MR,(\$[0-9a-fA-F]+|\d+)\)', block)
    assert output and 'call SaveInteger(vR,' in block, (start, block[:100])
    conditions = []
    for kind, ident, count in re.findall(r'call SaveInteger\(mR,LR,IR,\$([0-9a-fA-F]{8})\)\ncall SaveInteger\(lR,LR,IR,\$([0-9a-fA-F]{8})\)\ncall SaveInteger\(PR,LR,IR,(\$[0-9a-fA-F]+|\d+)\)', block):
        conditions.append({'kind': code(kind), 'id': code(ident), 'count': int(count[1:], 16) if count.startswith('$') else int(count)})
    assert len(conditions) == block.count('call SaveInteger(mR,LR,IR,')
    end = start + 9 + 5 * len(conditions) - 1
    assert lines[start - 1:end] == block.splitlines(), (start, end)
    count = output[2]
    recipes.append({'recipeId': code(registration[1]), 'output': {'kind': 'UNIT', 'id': code(output[1]), 'count': int(count[1:],16) if count.startswith('$') else int(count)}, 'conditions': conditions, 'source': evidence(start, end)})
choices = [{'outputId': code(m[1]), 'recipeId': code(m[2]), 'line': script.count('\n',0,m.start())+1} for m in re.finditer(r'call SaveInteger\(Lg,\$([0-9a-fA-F]{8}),RQf,\$([0-9a-fA-F]{8})\)',script)]
assert len(choices) == 265 and len({c['outputId'] for c in choices}) == 265
registered = {r['recipeId']: r for r in recipes}
assert len(registered) == 266 and {c['recipeId'] for c in choices} == set(registered) - {'TEST'}
assert all(registered[c['recipeId']]['output']['id'] == c['outputId'] for c in choices)
yujiro = registered['A0QG']
assert yujiro['output']['id'] == 'h0C2' and [(t['kind'],t['id'],t['count']) for t in yujiro['conditions']] == [('KING','h0C2',1),('PICK','A800',1),('UNIT','h06T',1),('UNIT','h02Y',1),('UNIT','h03Y',1),('UNIT','h06G',1),('GOLD','GOLD',10000),('WOOD','WOOD',7)]
pins.append(save('map-recipes-2322.json', {'schemaVersion': 1, 'mapVersion': '2.322', 'sourceSha256': sha, 'coverage': 'All 265 active registered outputs; TEST excluded', 'recipes': recipes, 'activeChoices': choices, 'conditionalHandlers': {'PICK': {'validation': evidence(29091,29100), 'consumption': evidence(67396,67400)}}}))
def function(name):
    matches = [i for i, line in enumerate(lines) if line.startswith('function '+name+' takes')]
    assert len(matches) == 1, name
    start = matches[0]+1
    end = lines.index('endfunction', start)+1
    return {'name': name, 'source': evidence(start,end), 'rawSource': '\n'.join(lines[start-1:end])}

# Seven actual ability registrations in main; no inherited handler alias or cost from the 2.320 bundle.
registration_lines = lines[83112:83119]
aliases = dict(re.findall(r'set (\w+)=function (\w+)', script))
exchanges = []
for index,line in enumerate(registration_lines,83113):
    m = re.fullmatch(r'call N9R\(10,\$([0-9a-fA-F]{8}),(\w+)\)',line)
    assert m and m[2] in aliases
    fn = function(aliases[m[2]])
    costs = re.findall(r'GetItemCharges\(Ns\[\w+\]\)<(\d+)',fn['rawSource'])
    if costs:
        assert len(costs) == 1 and re.search(r'GetItemCharges\(Ns\[\w+\]\)-(\d+)', fn['rawSource'])[1] == costs[0]
    exchanges.append({'ability': code(m[1]), 'cost': int(costs[0]) if costs else None, 'registrationLine': index, **fn})
assert [e['ability'] for e in exchanges] == ['A0CI','A0EZ','A0DB','A0IY','A0JR','A0BV','A0MA']
prior_options = json.loads((data/'navigation-mechanics-2320.json').read_text(encoding='utf-8'))['Options']
options = []
for prior in prior_options:
    name = prior['Name']
    matches = [i+1 for i,line in enumerate(lines) if name in line and '"' in line]
    assert matches, name
    category_handler = {'AlliedForces':'Rvf','PathOfKings':'kZC','Gambler':'wNC','BestHelp':'nwf','Random':'O4C'}[prior['Category']]
    handler = function(category_handler)
    assert name in handler['rawSource'], (name,category_handler)
    options.append({'id': prior['Id'].replace('Exchange2320','Exchange2322'), 'name': name, 'category': prior['Category'], 'registrationLines': matches, 'handler': handler})
assert len(options) == 15
pins.append(save('navigation-mechanics-2322.json', {'schemaVersion': 1, 'mapVersion': '2.322', 'sourceSha256': sha, 'options': options, 'exchanges': exchanges, 'doubleWispExchange': {'cost': 3, 'successNumerator': 35, 'rollDenominator': 100, 'successSelectionWisps': 2, 'failureSelectionWisps': None, 'failureStatus': 'UninitializedJassLocal', 'source': evidence(48957, 48974)}, 'randomBox': {'cost': 1, 'rollDenominator': 10, 'goldRolls': 5, 'goldAmount': 5000, 'randomWispRolls': 4, 'selectionWispRolls': 1, 'source': evidence(19100, 19123)}, 'grantForwarding': [evidence(4059, 4075)]}))
# Each stage's complete 2.322 completion function is embedded as machine-accessible source,
# with its base active-player payout parsed from the function itself. Other effect branches
# remain in the complete function, not misrepresented as old 2.320 values.
objectives = ['n000','n002','n003','n004','n005','n006','n007','n008','n00A','n001','n00C','n00D','n00B','n009']
stage_handlers = ['epR','ovf','uqf','apC','UnR','SYC','fdC','XBf','klC','kff','sSR','NOC','qhR','ZnR']
expected_gold = [180,800,1000,2000,3000,4000,6000,8000,9000,10000,12500,10000,5000,5000]
expected_wood = [0,0,0,1,1,3,4,4,5,4,4,3,3,0]
stages = []
for i,name in enumerate(stage_handlers):
    fn = function(name)
    source_lines = fn['rawSource'].splitlines()
    gold_matches = [(fn['source']['startLine']+j,m) for j,line in enumerate(source_lines) if (m:=re.search(r'call SetPlayerState\(\w+,PLAYER_STATE_RESOURCE_GOLD,GetPlayerState\(\w+,PLAYER_STATE_RESOURCE_GOLD\)\+(\$[0-9a-fA-F]+|\d+)\)',line))]
    assert gold_matches, name
    base_line, gold_match = gold_matches[0]
    gold = int(gold_match[1][1:],16) if gold_match[1].startswith('$') else int(gold_match[1])
    base_tail = source_lines[base_line-fn['source']['startLine']:]
    base_end = next(j for j,line in enumerate(base_tail) if line == 'endif')
    base_lines = base_tail[:base_end]
    wood = [int(n[1:],16) if n.startswith('$') else int(n) for line in base_lines for n in re.findall(r'PLAYER_STATE_RESOURCE_LUMBER\)\+(\$[0-9a-fA-F]+|\d+)\)',line)]
    assert gold == expected_gold[i] and sum(wood) == expected_wood[i], (name,gold,wood)
    units = []
    for line in base_lines:
        match = re.search(r'call (R6R|G1f|HXf)\(Player\(\w+\),(\d+)\)',line)
        if match: units.append({'id': {'R6R':'e018','G1f':'e0IX','HXf':'e017'}[match[1]],'count':int(match[2])})
        match = re.search(r'call hNf\((?:Player\(\w+\)|\w+),\$([0-9a-fA-F]{8}),(\d+),',line)
        if match: units.append({'id':code(match[1]),'count':int(match[2])})
        match = re.search(r'call CreateUnit\(\w+,\$([0-9a-fA-F]{8}),',line)
        if match: units.append({'id':code(match[1]),'count':1})
    assert units or i == 13, (name,base_lines)
    operations = []
    for line_index,line in enumerate(source_lines):
        kind = 'Resource' if 'call SetPlayerState(' in line and 'PLAYER_STATE_RESOURCE_' in line else 'Unit' if re.search(r'call (R6R|G1f|HXf|hNf|CreateUnit)\(',line) else 'Technology' if 'call SetPlayerTechResearched(' in line else None
        if kind:
            operations.append({'kind':kind,'sourceLine':fn['source']['startLine']+line_index,'statement':line,'branch':'BaseActivePlayer' if 0<=line_index-(base_line-fn['source']['startLine'])<base_end else 'OtherSourceBranch'})
    assert operations
    stages.append({'ordinal':i+1,'objectiveRawcode':objectives[i],'completionFunction':name,'baseGold':gold,'baseLumber':sum(wood),'baseUnits':units,'baseSourceLine':base_line,'directPayoutOperations':operations,**fn})
assert len(stages)==14
pins.append(save('story-progression-2322.json', {'schemaVersion': 1, 'mapVersion': '2.322', 'sourceSha256': sha, 'stages': stages, 'berry': {'baseClear': 1, 'nightmareClearAdditional': 1, 'nightmareZeroUnitClearAdditional': 1, 'allThreeMissionsClearAdditional': 1, 'poneglyphSuccessImmediate': 1, 'treasureChanceNumerator': 25, 'treasureChanceDenominator': 1000, 'treasureSuccessAdditional': 1, 'source': [evidence(*r) for r in [(45543,45543),(72808,72821),(50975,50999),(5370,5382),(24262,24265)]]}}))
old_script = (root / 'artifacts/activity-logging-20260921/source/war3map.j').read_bytes()
assert hashlib.sha256(old_script).hexdigest() == '55d0ffb9921433f45a9244cb946bdd27dcd2552a3550d30c4617c2eaccb94e97'
old_lines = old_script.decode('utf-8').splitlines()
assert 'call K8g($4130574e,0,1,1,-5.0000)' in old_lines[4512]
board_lines = lines[84252:84256] + lines[84475:84476]
board_rows = []
for line in board_lines:
    match = re.fullmatch(r'call AmC\(\$([0-9a-fA-F]{8}),0,1,(\d+),(-?\d+\.\d+)\)', line)
    assert match, line
    board_rows.append({'ability': code(match[1]), 'level': int(match[2]), 'value': int(float(match[3]))})
assert board_rows == [{'ability': 'A0WN', 'level': i, 'value': v} for i, v in [(1,-5),(2,-20),(3,-40),(4,-50)]] + [{'ability': 'A0BS', 'level': 1, 'value': -5}]
pins.append(save('map-mechanics-2322.json', {'schemaVersion': 1, 'mapVersion': '2.322', 'sourceSha256': sha, 'automaticCraftEligibility': 'Unresolved KING and PICK; target ability A800 must be observed', 'source': [evidence(29091,29100), evidence(67396,67400)], 'boardRegistryDeltaOnly': {'previousScriptSha256': hashlib.sha256(old_script).hexdigest(), 'rows': board_rows, 'currentSource': [evidence(84253,84256), evidence(84476,84476)], 'previousSource': [{'startLine': 4513, 'endLine': 4513, 'sha256': hashlib.sha256(old_lines[4512].encode()).hexdigest()}, {'startLine': 4558, 'endLine': 4558, 'sha256': hashlib.sha256(old_lines[4557].encode()).hexdigest()}]}}))
command_rows = [(code(m[1]), m[2], script.count('\n',0,m.start())+1) for m in re.finditer(r'call SaveStr\(Lg,\$([0-9a-fA-F]{8}),qPf,"([^"\\]*)"\)', script)]
by_result = {}
for result, alias, line in command_rows:
    by_result.setdefault(app(result), []).append((alias, line))
assert len(command_rows) == 162 and len(by_result) == 81 and all(len(v) == 2 for v in by_result.values())
assert '2C0h' not in by_result  # No registered Yujiro chat aliases.
commands = '# schema=1\n# map=2.322\n# source-sha256=' + sha + '\n# audited-results=81\n# command-aliases=162\n' + ''.join(result + '=' + '|'.join(alias for alias, _ in aliases) + '\n' for result, aliases in sorted(by_result.items()))
(data / 'map-combine-commands-2322.txt').write_bytes(commands.encode())
pins.append({'Identifier': 'commands', 'File': 'map-combine-commands-2322.txt', 'NormalizedBytes': len(commands.encode()), 'Sha256': hashlib.sha256(commands.encode()).hexdigest()})
def object_fields(member, ability):
    raw = (source.parent/member).read_bytes()
    offset = 0
    def integer():
        nonlocal offset
        value = struct.unpack_from('<i',raw,offset)[0]
        offset += 4
        return value
    assert integer() == 2
    result = []
    for table in range(2):
        count = integer()
        assert 0 <= count < 100000
        for _ in range(count):
            old_id, new_id = raw[offset:offset+4], raw[offset+4:offset+8]
            offset += 8
            fields = []
            for _ in range(integer()):
                at = offset
                field = raw[offset:offset+4].decode('ascii')
                offset += 4
                kind = integer()
                if ability:
                    level, data_pointer = integer(), integer()
                else:
                    level = data_pointer = 0
                if kind == 3:
                    stop = raw.index(0,offset)
                    value = raw[offset:stop].decode('utf-8','replace')
                    offset = stop+1
                else:
                    value = integer()
                offset += 4  # terminating object ID
                fields.append((field,level,value,at))
            result.append((new_id.decode('ascii','replace'),fields))
    assert offset == len(raw)
    return result

ability_fields = dict(object_fields('war3map.w3a',True))
unit_fields = dict(object_fields('war3map.w3u',False))
wts = (source.parent/'war3map.wts').read_bytes()
wts_strings = {int(m[1]):m[2].strip().decode('utf-8','replace') for m in re.finditer(rb'STRING (\d+)\r?\n(?:[^\r\n]*\r?\n)*?\{\r?\n(.*?)\r?\n\}',wts,re.DOTALL)}
hotkeys = []
for choice in choices:
    recipe = registered[choice['recipeId']]
    rawcode = choice['recipeId']
    fields = ability_fields.get(rawcode,[])
    hotkey = next(((v,at) for field,level,v,at in fields if field == 'ahky'),None)
    if hotkey is None or not hotkey[0]:
        continue
    marker = re.fullmatch(r'TRIGSTR_(\d+)',hotkey[0])
    if marker:
        key = wts_strings.get(int(marker[1]))
        assert key, (rawcode, marker[1])
    else:
        key = hotkey[0]
    hosts = [(unit,at) for unit,fields in unit_fields.items() for field,level,v,at in fields if field == 'uabi' and rawcode in v.split(',')]
    assert hosts, rawcode
    hotkeys.append({'result':app(choice['outputId']),'ability':rawcode,'key':key,'outputCount':recipe['output']['count'],'recipeLine':recipe['source']['startLine'],'hotkeyOffset':hotkey[1],'hosts':[{'rawcode':app(unit),'abilityOffset':at} for unit,at in hosts],'hotkeyStringId':int(marker[1]) if marker else None})
assert len(hotkeys) >= 150 and any(x['ability']=='A0QG' and x['key']=='Z' for x in hotkeys)
pins.append(save('map-combine-hotkeys-2322.json', {'schemaVersion':1,'mapVersion':'2.322','sourceSha256':sha,'abilitySha256':next(x['sha256'] for x in receipt['members'] if x['name']=='war3map.w3a'),'unitSha256':next(x['sha256'] for x in receipt['members'] if x['name']=='war3map.w3u'),'stringsSha256':next(x['sha256'] for x in receipt['members'] if x['name']=='war3map.wts'),'entries':hotkeys,'commands':[{'result':result,'aliases':[alias for alias,_ in aliases],'sourceLines':[line for _,line in aliases]} for result,aliases in sorted(by_result.items())]}))
save('map-source-manifest-2322.json', {'SchemaVersion': 1, 'MapVersion': '2.322', 'ArchiveSha256': receipt['archiveSha256'], 'Members': pins})
for p in pins:
    print(p['File'], p['NormalizedBytes'], p['Sha256'])
