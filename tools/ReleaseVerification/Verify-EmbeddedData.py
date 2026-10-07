"""Read-only verification of every Data payload in an existing .NET single-file bundle."""
import hashlib
import json
import pathlib
import struct
import sys
import zlib

exe, root, receipt = map(pathlib.Path, sys.argv[1:4])
raw = exe.read_bytes()
marker = bytes.fromhex('8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae')
index = raw.find(marker)
assert index >= 8 and raw.find(marker,index+1) < 0, 'Missing/ambiguous bundle marker'
header = struct.unpack_from('<q',raw,index-8)[0]
assert 0 <= header < len(raw)
major,minor,count = struct.unpack_from('<IIi',raw,header)
assert major == 6 and 50 < count < 10000

def string(position):
    size=shift=0
    while True:
        value=raw[position];position+=1
        size|=(value&127)<<shift
        if value<128:break
        shift+=7
        assert shift<=28
    return raw[position:position+size].decode('utf8'),position+size

_,cursor=string(header+12)
cursor+=40
members=[]
seen=set()
for _ in range(count):
    offset,size,compressed=struct.unpack_from('<qqq',raw,cursor)
    cursor+=25
    name,cursor=string(cursor)
    name=name.replace('\\','/')
    assert name not in seen and not name.startswith('/') and '..' not in name.split('/') and ':' not in name
    seen.add(name)
    stored=compressed or size
    assert min(offset,size,compressed)>=0 and size<=512*1024*1024 and offset+stored<=len(raw)
    if not name.startswith('Data/'):continue
    payload=raw[offset:offset+stored]
    if compressed:payload=zlib.decompress(payload,-15)
    assert len(payload)==size
    source=root / ('docs/bullet-guide-1-source.md' if name=='Data/bullet-guide-1-source.md' else name)
    assert source.is_file() and payload==source.read_bytes(),name
    members.append({'path':name,'bytes':size,'sha256':hashlib.sha256(payload).hexdigest(),
                    'bundleOffset':offset,'compressedBytes':compressed})
expected={'Data/'+p.relative_to(root/'Data').as_posix() for p in (root/'Data').rglob('*') if p.is_file()}
expected.add('Data/bullet-guide-1-source.md')
assert {m['path'] for m in members}==expected and len(members)==len(expected)
assert any('2323' in x for x in expected)
result={'executable':str(exe.resolve()),'executableBytes':len(raw),'executableSha256':hashlib.sha256(raw).hexdigest(),
        'bundleManifestEntries':count,'dataMembers':len(members),'dataMemberReceipts':sorted(members,key=lambda m:m['path'])}
receipt.write_text(json.dumps(result,indent=2)+'\n',encoding='utf8')
print(json.dumps({k:v for k,v in result.items() if k!='dataMemberReceipts'},indent=2))
