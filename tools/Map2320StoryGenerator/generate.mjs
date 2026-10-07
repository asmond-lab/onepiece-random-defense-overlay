import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
const root=process.argv[2]||process.cwd();
const read=p=>fs.readFileSync(path.join(root,p));
const hash=b=>crypto.createHash('sha256').update(b).digest('hex');
const src='docs/analysis-2320/modern-reader/members-ko-2.320/';
const bytes=read(src+'war3map.j'), lines=bytes.toString('utf8').split(/\r?\n/), functions={};
let f;
lines.forEach((text,i)=>{const m=text.match(/^function (\w+) takes/);if(m)f={Function:m[1],StartLine:i+1,EndLine:0,Lines:[]};if(f)f.Lines.push(text);if(text==='endfunction'&&f){f.EndLine=i+1;functions[f.Function]=f;f=null;}});
const used=new Set();
function pin(line){const f=Object.values(functions).find(f=>f.StartLine<=line&&line<=f.EndLine);if(!f)throw Error('no function '+line);used.add(f.Function);return {Function:f.Function,StartLine:f.StartLine,EndLine:f.EndLine,EvidenceLine:line,Text:lines[line-1]};}
function find(name,needle){const f=functions[name],i=f.Lines.findIndex(l=>l.includes(needle));if(i<0)throw Error(name+': '+needle);return f.StartLine+i;}
const factory={uTT:[10416,7598],gpy:[11197,7598],Eiy:[12144,7598],KAT:[7598],bnT:[14710],AuT:[17387,17388,17390,17391],GnT:[16469,16472,16482,16485,16493,16498,16442,16443,16444],MVy:[14363,14364]};
function reward(line,condition='ActivePlayer'){const text=lines[line-1];let Kind,Id,Amount=1,extra=[];
const raw=h=>Buffer.from(h,'hex').toString('ascii'),num=s=>s.startsWith('$')?parseInt(s.slice(1),16):Number(s);let m;
if(m=text.match(/call SetPlayerState\(.+,(PLAYER_STATE_RESOURCE_\w+),GetPlayerState\(.+\)\+([\w$]+)\)/)){Id=m[1];Kind=Id.endsWith('GOLD')?'Gold':Id.endsWith('LUMBER')?'Lumber':'FoodUsed';Amount=num(m[2]);}
else if(m=text.match(/call (uTT|gpy|Eiy|bnT)\(.+,([\w$]+)\)/)){Kind=m[1]==='bnT'?'Gold':'Unit';Id={uTT:'e0IX',gpy:'e018',Eiy:'e017',bnT:'PLAYER_STATE_RESOURCE_GOLD'}[m[1]];Amount=num(m[2]);extra=factory[m[1]];}
else if(m=text.match(/call KAT\(.+,\$([0-9a-f]{8}),(\d+),/)){Kind='Unit';Id=raw(m[1]);Amount=num(m[2]);extra=factory.KAT;}
else if(m=text.match(/call CreateUnit\([^,]+,\$([0-9a-f]{8}),/)){Kind='Unit';Id=raw(m[1]);}
else if(m=text.match(/call SetPlayerTechResearched\(.+,\$([0-9a-f]{8}),(\d+)\)/)){Kind='Technology';Id=raw(m[1]);Amount=num(m[2]);}
else if(text.includes('call AuT(')){Kind='WorldGambleCharge';Id='H0AW';extra=factory.AuT;}
else if(m=text.match(/GnT\(.+,\$([0-9a-f]{8})\)/)){Kind='RandomItem';Id=raw(m[1]);extra=[...factory.GnT,6993,7000];}
else throw Error('unhandled '+line+' '+text);
return {Kind,Id,Amount,Limit:Kind==='WorldGambleCharge'?16:0,ChanceNumerator:1,ChanceDenominator:1,Condition:condition,AmountExpression:Kind==='WorldGambleCharge'?'Og[player]==1 ? 2 : 1':'Constant',Known:true,Sources:[line,...extra].map(pin)};}
const handlers=['fqT','Dub','iJy','bOT','ZAy','pmT','NTF','Dey','kOb','NNb','N3T','ZbT','ulb','YTF'];
const baseLines=[[40778,40779,40708],[46402,46403,46344],[23667,23668,23669,23609],[76448,76450,76451,76455],[18217,18219,18220,18224],[79340,79342,79343,79347],[47878,47880,47884],[41561,41563,41567],[58452,58454,58455,58460],[17510,17512,17513,17514],[21607,21609,21610,21611],[23466,23468,23471,23475],[52688,52690,52691],[37118]];
const contribution=[[],[],[],[76373],[18197],[79320],[47894],[41665],[58472],[17526],[21622],[23487],[52703],[37040]];
const mvp=[[40784,40785],[46408,46409],[23674,23675],[76460,76461],[18315,18316],[79437],[47898],[41669],[58476],[17530,17531],[21626,21627],[23491,23492],[52707,52708],[37125]];
const hidden=[[],[],[],[],[18225,18241],[79363],[],[41568,41569,41585],[58461,58462],[17515,17516],[21612],[23470,23476,23477],[52693],[]];
const objectives=Array.from({length:14},(_,i)=>Buffer.from(lines[85352+i].match(/\$([0-9a-f]{8})/)[1],'hex').toString('ascii'));
const Stages=handlers.map((name,i)=>{const f=functions[name];used.add(name);const wrapper=Object.values(functions).find(f=>f.Lines.includes('call '+name+'()'));used.add(wrapper.Function);const bindingLine=lines.findIndex(l=>l.endsWith('=function '+wrapper.Function))+1;const callback=lines[bindingLine-1].match(/^set (\w+)=/)[1];const registration=lines.findIndex(l=>l.includes(','+callback+')')&&l.includes('call F6y('))+1;
const spawn=lines.findIndex(l=>l.includes('CreateUnit(Player(5),$'+Buffer.from(objectives[i]).toString('hex')))+1;
const Evidence=[pin(85353+i),pin(bindingLine),pin(registration),pin(spawn),pin(find(wrapper.Function,'call '+name+'()'))];
const s={Ordinal:i+1,ObjectiveRawcode:objectives[i],OwnerId:5,MilestoneId:i===8?'Marineford':'stage'+(i+1),CompletionFunction:name,Qualification:i<3?'UnconditionalActivePlayer':'ActivePlayingUserCountAtCompletion',Evidence,EveryPlayerBase:baseLines[i].map(l=>reward(l)),ContributionQualified:contribution[i].map(l=>reward(l,'ContributionQualified')),Mvp:mvp[i].map(l=>reward(l,'MaximumDamageContributor')),HiddenOrSideEffect:hidden[i].map(l=>reward(l))};
for(const r of [...s.EveryPlayerBase,...s.ContributionQualified,...s.Mvp,...s.HiddenOrSideEffect]){const rf=r.Sources[0].Function;if(rf!==name){const call=f.Lines.findIndex(l=>l==='call '+rf+'()');if(call>=0)r.Sources.unshift(pin(f.StartLine+call));}if(r.Kind==='RandomItem'){r.Condition='ActivePlayerMissingItemAndPoolContainsId';r.ChanceDenominator=20;r.AmountExpression='Nominal5Of100RngBucketsNotUniformityProof';r.Sources.push(pin(find(rf,'if ptT(1,$64)<=5 then')));r.Sources.push(pin(r.Id==='AI00'?16395:r.Id==='AI02'?16399:16401));}}
function effect(Kind,Id,Amount,Condition,AmountExpression,ls){s.HiddenOrSideEffect.push({Kind,Id,Amount,Limit:0,ChanceNumerator:1,ChanceDenominator:1,Condition,AmountExpression,Known:true,Sources:ls.map(pin)});}
effect('ClearScore','ky',1,'ActivePlayerPositiveComputedScore','R2I((300-elapsedSeconds)*damage/objectiveMaxLife*5); only >0',[find(name,'=R2I('),find(name,'call GgF('),17411]);
effect('MvpCounter','QF',1,'MaximumDamageContributor','LastAscendingActivePlayerWinsEqualDamage',[find(name,'set QF['),find(name,'set VT['),find(name,'set DT[')]);
effect('StoryLifecycle','Completion',1,'Always','StopTimersAndRefreshContributionBoard',f.Lines.map((l,k)=>[l,f.StartLine+k]).filter(([l])=>/call (PauseTimer|DestroyTimerDialog|t7y|jib|j0y)\(/.test(l)).map(x=>x[1]));
if(i===12||i===13)effect('HeroExperience','Hc[Hg[player]]',300,'ActivePlayerEnumeratedHero','PerEnumeratedHero',[i===12?52692:37119,i===12?81361:81366,i===12?79976:27907]);
if(i===11)effect('MissionNotice','MoriaKill',1,'Always','DisplayedActivationNoticeOnly',[23499]);
if(i===12)effect('ConditionalTimerSeconds','EggheadAppearance',120,'DifficultyAtLeast5','ay>=5; TimerStart120Seconds',[52731,52760]);
if(i===13)effect('PermanentAbility','A13A',1,'ActivePlayer','UnitAddAbilityAndMakePermanent',[37120,14363,14364]);
const wl=wrapper.Lines.map((l,k)=>[l,wrapper.StartLine+k]).filter(([l])=>/^(call |set )/.test(l)&&l!=='call '+name+'()');
if(wl.length)effect('CompletionDispatch',wrapper.Function,1,'Always','ExactPinnedWrapperBody',wl.map(x=>x[1]));return s;});
for(const n of ['KAT','uTT','gpy','Eiy','bnT','AuT','GnT','jwb','i0y','tZb','F6y','jib','j0y','GgF','ptT'])used.add(n);
const objects=JSON.parse(read('docs/analysis-2320/object-diff/2.320.w3u.json')).tables.flatMap(t=>t.objects).filter(o=>['e016','e017','e018','e019','e01A','e0IX','e0IA','h05Y'].includes(o.id)).map(o=>({Id:o.id,StartOffset:o.offset,EndOffset:o.endOffset,Name:o.fields.find(f=>f.fieldId==='unam')?.resolvedValue||'',Fields:o.fields.filter(f=>['unam','utip','uabi','umdl','utyp','uhpm'].includes(f.fieldId)).map(f=>({Id:f.fieldId,Offset:f.offset,EndOffset:f.endOffset,Value:String(f.resolvedValue??f.value)}))}));
const Members=['war3map.j','war3map.w3u','war3map.wts'].map(Name=>{const b=read(src+Name);return{Name,LengthBytes:b.length,Sha256:hash(b)}});
const Source={MapVersion:'2.320',Members,Functions:[...used].sort().map(n=>functions[n]),Objects:objects};
const doc={SchemaVersion:1,Source,Stages};const out=JSON.stringify(doc,null,2)+'\n';fs.writeFileSync(path.join(root,'Data/story-progression-2320.json'),out);
console.log(JSON.stringify({DataSha256:hash(out),SourceSha256:hash(JSON.stringify(Source)),Stages:Stages.length,Components:Stages.reduce((n,s)=>n+s.EveryPlayerBase.length+s.ContributionQualified.length+s.Mvp.length+s.HiddenOrSideEffect.length,0)},null,2));
