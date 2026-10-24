"""Manual-only disposable runner. Imports and pure guards perform no I/O."""
import argparse, hashlib, ipaddress, json, math, os, pathlib, re, secrets, subprocess, sys, tarfile, tempfile
import xml.etree.ElementTree as ET
SQL_IMAGE='mcr.microsoft.com/mssql/server@sha256:cf8f0ab2ebe61feec2eed2629061a07d247f791c2a8ddda74a4ac9f06a291718'
SDK_IMAGE='mcr.microsoft.com/dotnet/sdk@sha256:8dd1a57fe6b7b8b1b357635db67e272c2f9e9d76d1dd0a32711c4c33c57d7b43'
REPOSITORY='ivandinicola/CONSTIA'
WORKFLOW_REF=REPOSITORY+'/.github/workflows/c3c02b-operational.yml@refs/heads/main'
BASE='79923e86eb1ce450db40d538d55848eaebe7877c'
ISOLATION='isolation-only'
INVENTORY_PATHS=('.github/scripts/c3c02b_runner.py','.github/scripts/test_c3c02b_runner.py',
 '.github/workflows/c3c02b-operational.yml','docs/operations/c3-c-02b.md',
 'tests/Constia.C3C02B.Tests/ConfirmationTests.cs','tests/Constia.C3C02B.Tests/Constia.C3C02B.Tests.csproj',
 'tests/Constia.C3C02B.Tests/DisposableSqlDatabase.cs','tests/Constia.C3C02B.Tests/OperationalTests.cs',
 'tests/Constia.C3C02B.Tests/QueriesTests.cs','tests/Constia.C3C02B.Tests/SafetyTests.cs',
 'tests/sandbox/c3c02b/Dockerfile','tests/sandbox/c3c02b/coverage.json','tests/sandbox/c3c02b/queries.sql',
 'tools/Constia.C3C02B.Harness/Constia.C3C02B.Harness.csproj','tools/Constia.C3C02B.Harness/Program.cs',
 'tools/Constia.C3C02B.Harness/SandboxIdentity.cs','tools/Constia.C3C02B.Harness/SimulatedResendHandler.cs')
FIELDS=set('repository code_sha workflow_sha workflow_ref run_id attempt nonce container_id container_name image database sql_ip hostname network_id network_name subnet gateway sdk_image runtime observer migrator'.split())
class Unsafe(RuntimeError): pass
def exact_id(x):
    if not isinstance(x,str) or not re.fullmatch('[0-9a-f]{64}',x): raise Unsafe('Complete ID required.')
    return x
def validate_identity(m):
    if not isinstance(m,dict) or set(m)!=FIELDS: raise Unsafe('Unexpected identity fields.')
    if any(not isinstance(x,str) for x in m.values()):raise Unsafe('Invalid identity field type.')
    if m['repository']!=REPOSITORY or m['attempt']!='1' or not re.fullmatch('[1-9][0-9]*',m['run_id']): raise Unsafe('Wrong run.')
    if any(not re.fullmatch('[0-9a-f]{40}',m[k]) for k in ('code_sha','workflow_sha')): raise Unsafe('Wrong revision.')
    if not re.fullmatch('[0-9a-f]{32}',m['nonce']): raise Unsafe('Wrong nonce.')
    for k in ('container_id','network_id'): exact_id(m[k])
    if (m['container_name']!='c3c02b-'+m['nonce'] or m['network_name']!=m['container_name'] or
        m['database']!='C3C02B_'+m['nonce'] or m['hostname']!=m['container_id'][:12]): raise Unsafe('Wrong resource.')
    topology(m['subnet'],m['gateway'],m['sql_ip'])
    if m['workflow_ref']!=WORKFLOW_REF: raise Unsafe('Wrong workflow reference.')
    if m['image']!=SQL_IMAGE or m['sdk_image']!=SDK_IMAGE: raise Unsafe('Wrong image.')
    if [m[k] for k in ('runtime','observer','migrator')]!=['c3_runtime','c3_observer','c3_migrator']: raise Unsafe('Wrong roles.')
def require_ci(e,approved):
    required=dict(GITHUB_ACTIONS='true',RUNNER_ENVIRONMENT='github-hosted',RUNNER_OS='Linux',GITHUB_REPOSITORY=REPOSITORY,
                  GITHUB_EVENT_NAME='workflow_dispatch',GITHUB_RUN_ATTEMPT='1')
    if any(e.get(k)!=v for k,v in required.items()): raise Unsafe('Only first approved hosted manual run.')
    if e.get('GITHUB_REF')!='refs/heads/main' or e.get('GITHUB_WORKFLOW_REF')!=WORKFLOW_REF or e.get('GITHUB_SHA')!=e.get('GITHUB_WORKFLOW_SHA'): raise Unsafe('Launcher is not the reviewed main workflow.')
    if not re.fullmatch('[0-9a-f]{40}',approved or '') or e.get('CODE_SHA')!=approved: raise Unsafe('B3 approval absent.')
    if not re.fullmatch('[0-9a-f]{40}',e.get('GITHUB_SHA','')): raise Unsafe('Invalid launcher revision.')
    if not re.fullmatch('[0-9a-f]{40}',e.get('GITHUB_WORKFLOW_SHA','')) or not re.fullmatch('[1-9][0-9]*',e.get('GITHUB_RUN_ID','')): raise Unsafe('Missing run identity.')
    if any(e.get(k) for k in ('HTTP_PROXY','HTTPS_PROXY','ALL_PROXY','http_proxy','https_proxy','all_proxy','DOCKER_HOST','DOCKER_CONTEXT','RESEND_API_KEY','SQLCONNSTR_DefaultConnection')): raise Unsafe('Inherited connections forbidden.')

def authorize_isolation(e,phase):
    # Trusted launcher policy is not a CLI approval. The empty installed policy remains closed.
    require_ci(e,e.get('CONSTIA_B3_APPROVED_SHA',''))
    if phase!=ISOLATION or e.get('C3C02B_PHASE')!=ISOLATION or e.get('CONSTIA_B3_APPROVED_PHASE')!=ISOLATION:
        raise Unsafe('Only explicitly approved isolation-only is supported; operational-full is blocked.')
    actor=e.get('CONSTIA_B3_APPROVED_ACTOR_ID','')
    if not re.fullmatch('[1-9][0-9]*',actor) or e.get('GITHUB_ACTOR_ID')!=actor:
        raise Unsafe('Actor is not authorized by the launcher.')
    if not re.fullmatch('[0-9a-f]{64}',e.get('CONSTIA_B3_INVENTORY_SHA256','')):
        raise Unsafe('Reviewed inventory policy absent.')

def verify_inventory(root,e):
    try:table=json.loads(e.get('CONSTIA_B3_INVENTORY',''))
    except (ValueError,TypeError):raise Unsafe('Approved inventory absent.') from None
    if not isinstance(table,dict) or set(table)!=set(INVENTORY_PATHS):raise Unsafe('Inventory paths differ.')
    if any(not isinstance(v,str) or not re.fullmatch('[0-9a-f]{64}',v) for v in table.values()):raise Unsafe('Inventory hash invalid.')
    digest=hashlib.sha256(json.dumps(table,sort_keys=True,separators=(',',':')).encode()).hexdigest()
    if digest!=e.get('CONSTIA_B3_INVENTORY_SHA256'):raise Unsafe('Inventory policy differs.')
    for rel,expected in table.items():
        path=root/rel
        if path.is_symlink() or not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest()!=expected:
            raise Unsafe('Reviewed file bytes differ.')
    return digest

def validate_preflight(m):
    fields=set('phase repository code_sha policy_sha256 workflow_sha workflow_ref actor_id run_id attempt nonce network_id network_name subnet gateway app_id app_ip peer_id peer_ip denied_id denied_ip image_id sdk_image namespace rules_sha256 proof_stage gateway_port'.split())
    if not isinstance(m,dict) or set(m)!=fields:raise Unsafe('Preflight fields differ; SQL identity is forbidden.')
    if m['phase']!=ISOLATION or m['repository']!=REPOSITORY or m['workflow_ref']!=WORKFLOW_REF or m['attempt']!='1':raise Unsafe('Wrong preflight phase/run.')
    for key in ('actor_id','run_id'):
        if not isinstance(m[key],str) or not re.fullmatch('[1-9][0-9]*',m[key]):raise Unsafe('Invalid actor/run.')
    for key,size in [('code_sha',40),('workflow_sha',40),('policy_sha256',64),('nonce',32)]:
        if not isinstance(m[key],str) or not re.fullmatch('[0-9a-f]{'+str(size)+'}',m[key]):raise Unsafe('Invalid preflight digest.')
    for key in ('app_id','peer_id','denied_id','network_id'):exact_id(m[key])
    if len({m[x] for x in ('app_id','peer_id','denied_id')})!=3:raise Unsafe('Ambiguous receiver identity.')
    if m['network_name']!='c3c02b-'+m['nonce'] or m['sdk_image']!=SDK_IMAGE or not re.fullmatch('sha256:[0-9a-f]{64}',m['image_id']):raise Unsafe('Wrong preflight resources.')
    for key in ('app_ip','peer_ip','denied_ip'):topology(m['subnet'],m['gateway'],m[key])
    if len({m[x] for x in ('app_ip','peer_ip','denied_ip')})!=3:raise Unsafe('Ambiguous receiver addresses.')
    if not isinstance(m['namespace'],str) or not re.fullmatch(r'net:\[[0-9]+\]',m['namespace']):raise Unsafe('Invalid preflight namespace.')
    if type(m['gateway_port'])!=int or not 1024<=m['gateway_port']<=65535 or m['gateway_port']==1433:raise Unsafe('Invalid sentinel port.')
    if m['proof_stage'] not in ('prepared','isolated'):raise Unsafe('Unknown proof stage.')
    if m['proof_stage']=='prepared' and m['rules_sha256'] is not None:raise Unsafe('Premature firewall claim.')
    if m['proof_stage']=='isolated' and (not isinstance(m['rules_sha256'],str) or not re.fullmatch('[0-9a-f]{64}',m['rules_sha256'])):raise Unsafe('Firewall proof absent.')

def validate_isolation_evidence(report):
    fields={'identity','phase','allowed_peer','denied_peer','gateway_blocked','controls_positive','simulated_transport','routes_sha256','sql','workers_started'}
    if not isinstance(report,dict) or set(report)!=fields:raise Unsafe('Preflight evidence cannot contain operational TRX or queries.')
    validate_preflight(report['identity'])
    if report['identity']['proof_stage']!='isolated' or report['phase']!=ISOLATION or report['sql']!='not_applicable' or type(report['workers_started'])!=int or report['workers_started']!=0:raise Unsafe('Incorrect preflight claims.')
    if any(report[x] is not True for x in ('allowed_peer','denied_peer','gateway_blocked','controls_positive','simulated_transport')):raise Unsafe('Isolation evidence incomplete.')
    if not isinstance(report['routes_sha256'],str) or not re.fullmatch('[0-9a-f]{64}',report['routes_sha256']):raise Unsafe('Route proof absent.')

def topology(subnet,gateway,host=None):
    try:
        net=ipaddress.ip_network(subnet,strict=True);gw=ipaddress.ip_address(gateway)
        allowed=any(net.subnet_of(ipaddress.ip_network(x)) for x in ('10.0.0.0/8','172.16.0.0/12','192.168.0.0/16')) if net.version==4 else False
        if not allowed or net.prefixlen>29 or str(net)!=subnet or str(gw)!=gateway or gw not in net or gw in (net.network_address,net.broadcast_address): raise ValueError()
        if host is not None:
            address=ipaddress.ip_address(host)
            if str(address)!=host or address not in net or address in (gw,net.network_address,net.broadcast_address): raise ValueError()
        return net
    except (ValueError,TypeError): raise Unsafe('Private dedicated network topology rejected.') from None
def checked_command(args):
    if not isinstance(args,list) or not args or not all(isinstance(x,str) and '\0' not in x for x in args): raise Unsafe('Explicit argument list required.')
    if args[0]=='docker' and any(x in ('--context','--host','-H') or x.startswith(('--context=','--host=','-H=')) for x in args[1:]):raise Unsafe('Docker endpoint override forbidden.')
    return [args[0],'--context','default',*args[1:]] if args[0]=='docker' else args
def public_text(t):
    if re.search(r'(?i)(bearer\s|password\s*=|connectionstring|re_[a-z0-9]{20,}|[\w.+-]+@(?:[\w-]+\.)+[\w-]+)',t): raise Unsafe('Private evidence rejected.')
    return t
def labels(m): return {'constia.owner':'c3c02b','constia.run':m['run_id'],'constia.nonce':m['nonce'],'constia.sha':m['code_sha']}
def validate_container(m,s,image_id):
    validate_identity(m)
    if (s.get('Id')!=m['container_id'] or s.get('Image')!=image_id or s['Config'].get('Image')!=SQL_IMAGE or
        s['Config'].get('Hostname')!=m['hostname'] or any(s['Config'].get('Labels',{}).get(k)!=v for k,v in labels(m).items()) or
        s['HostConfig'].get('Privileged') or s['HostConfig'].get('NetworkMode')!=m['network_name']): raise Unsafe('Container mismatch.')
    n=s['NetworkSettings']['Networks']
    if set(n)!={m['network_name']} or n[m['network_name']].get('IPAddress')!=m['sql_ip'] or n[m['network_name']].get('NetworkID')!=m['network_id']: raise Unsafe('Network mismatch.')
    if s['HostConfig'].get('Binds') or s['HostConfig'].get('PortBindings'): raise Unsafe('Host mounts/ports forbidden.')
def command(args,*,env=None,input=None,timeout=60):
    r=subprocess.run(checked_command(args),env=env,input=input,capture_output=True,timeout=timeout)
    if r.returncode: raise Unsafe('Bounded operation failed; raw output withheld.')
    return r.stdout.decode().strip()
def inspect(kind,x): return json.loads(command(['docker',kind,'inspect',x]))[0]

def require_local_engine():
    data=json.loads(command(['docker','context','inspect','default']))
    if len(data)!=1 or data[0].get('Endpoints',{}).get('docker',{}).get('Host')!='unix:///var/run/docker.sock':
        raise Unsafe('Only the default local Linux Docker endpoint is allowed.')
def state_path(): return pathlib.Path(os.environ['RUNNER_TEMP'])/('c3c02b-'+os.environ['GITHUB_RUN_ID']+'.json')
def save(s):
    p=state_path(); temporary=p.with_suffix('.json.tmp')
    fd=os.open(temporary,os.O_WRONLY|os.O_CREAT|os.O_EXCL,0o600)
    try:
        with os.fdopen(fd,'w',encoding='utf-8') as stream:
            json.dump(s,stream);stream.flush();os.fsync(stream.fileno())
        os.replace(temporary,p)
    finally:
        if temporary.exists(): temporary.unlink()
def network(s):
    n=inspect('network',s['network_id'])
    if n['Id']!=s['network_id'] or not n['Internal'] or n['Driver']!='bridge' or any(n.get('Labels',{}).get(k)!=v for k,v in s['labels'].items()): raise Unsafe('Network owner/isolation mismatch.')
    config=n.get('IPAM',{}).get('Config',[])
    if len(config)!=1 or n.get('EnableIPv6') is not False: raise Unsafe('Unexpected network address families.')
    topology(config[0]['Subnet'],config[0]['Gateway'])
    if 'subnet' in s and (s['subnet']!=config[0]['Subnet'] or s['gateway']!=config[0]['Gateway']): raise Unsafe('Recorded topology differs.')
    if s.get('phase')==ISOLATION and all(k in s for k in ('app_id','peer_id','denied_id','app_ip','peer_ip','denied_ip')):
        validate_preflight_membership(s,n)
    return n

def validate_preflight_membership(s,n):
    keys=('app_id','peer_id','denied_id');rows=n.get('Containers')
    if not isinstance(rows,dict) or set(rows)!={s[key] for key in keys}:raise Unsafe('Network contains missing or foreign endpoints.')
    for key in keys:
        row=rows[s[key]]
        expected=s[key.replace('_id','_ip')]+'/'+str(ipaddress.ip_network(s['subnet']).prefixlen)
        if row.get('Name')!=resource_specs(s)[key][1] or row.get('IPv4Address')!=expected or row.get('IPv6Address') not in (None,''):
            raise Unsafe('Network endpoint identity/address differs.')
def app(s,key='app_id'):
    a=inspect('container',s[key]);validate_app(s,a,key);return a

def validate_app(s,a,key='app_id'):
    h=a['HostConfig']
    if (a['Id']!=s[key] or a['Image']!=s['app_image_id'] or any(a['Config'].get('Labels',{}).get(k)!=v for k,v in s['labels'].items()) or
        h.get('Privileged') or h.get('Binds') or not h.get('ReadonlyRootfs') or any(x['Type']!='tmpfs' for x in a.get('Mounts',[])) or h['NetworkMode']!=s['network_name'] or set(h.get('CapDrop') or [])!={'ALL'} or h.get('CapAdd') or
        h.get('PortBindings') or set(a['NetworkSettings']['Networks'])!={s['network_name']}): raise Unsafe('Harness isolation mismatch.')
    attachment=a['NetworkSettings']['Networks'][s['network_name']]
    if attachment.get('NetworkID')!=s['network_id']: raise Unsafe('Harness attached to a different network.')
    topology(s['subnet'],s['gateway'],attachment.get('IPAddress'))
    if not set(h.get('SecurityOpt') or []).intersection({'no-new-privileges','no-new-privileges:true'}) or h.get('PidMode','') not in ('','private') or h.get('IpcMode','private')!='private': raise Unsafe('Host namespaces/privileges rejected.')
    for item in a['Config'].get('Env',[]):
        key,_,value=item.partition('=');upper=key.upper()
        if value and (upper.endswith('_PROXY') or upper in ('DOCKER_HOST','DOCKER_CONTEXT') or any(x in upper for x in ('PASSWORD','SECRET','TOKEN','API_KEY','APIKEY','ACCESS_KEY','CONNECTIONSTRINGS','SQLCONNSTR','JWTSETTINGS','RESEND','BIENVENIDA','VERIFICACIONCORREO'))): raise Unsafe('Unexpected inherited credential/configuration.')
    return a

def firewall_snapshot(sql_ip):
    if str(ipaddress.ip_address(sql_ip))!=sql_ip: raise Unsafe('Noncanonical SQL address.')
    return (['-P OUTPUT DROP','-A OUTPUT -d 127.0.0.1/32 -o lo -j ACCEPT',
             '-A OUTPUT -d '+sql_ip+'/32 -p tcp -m tcp --dport 1433 -j ACCEPT'],
            ['-P OUTPUT DROP','-A OUTPUT -d ::1/128 -o lo -j ACCEPT'])

def validate_firewall(sql_ip,v4,v6):
    expected=firewall_snapshot(sql_ip)
    if v4.splitlines()!=expected[0] or v6.splitlines()!=expected[1]: raise Unsafe('Firewall differs.')
def namespace_firewall_command(pid,namespace,sql_ip):
    if not re.fullmatch('[1-9][0-9]*',str(pid)) or not re.fullmatch(r'net:\[[0-9]+\]',namespace):
        raise Unsafe('Invalid namespace identity.')
    # Static script; caller values are positional arguments, never executable shell text.
    script='''test "$(readlink /proc/self/ns/net)" = "$1"
iptables -P OUTPUT DROP
iptables -F OUTPUT
iptables -A OUTPUT -o lo -d 127.0.0.1/32 -j ACCEPT
iptables -A OUTPUT -d "$2" -p tcp --dport 1433 -j ACCEPT
ip6tables -P OUTPUT DROP
ip6tables -F OUTPUT
ip6tables -A OUTPUT -o lo -d ::1/128 -j ACCEPT
printf '%s\\n' IPV4
iptables -S OUTPUT
printf '%s\\n' IPV6
ip6tables -S OUTPUT
'''
    return ['sudo','nsenter','--target',str(pid),'--net','--','sh','-eu','-c',script,'c3c02b-firewall',namespace,sql_ip]

def isolate(s,sql_ip):
    topology(s['subnet'],s['gateway'],sql_ip)
    target=s.get('peer_ip') if s.get('phase')==ISOLATION else s.get('sql_ip')
    if sql_ip!=target: raise Unsafe('Firewall target is not the recorded allowed member.')
    app(s);network(s)
    namespace=command(['docker','exec',s['app_id'],'readlink','/proc/self/ns/net'])
    current=app(s)
    output=command(namespace_firewall_command(current['State']['Pid'],namespace,sql_ip),timeout=15)
    if output.count('IPV4\n')!=1 or output.count('IPV6\n')!=1: raise Unsafe('Namespace firewall proof missing.')
    first,second=output.split('IPV6\n')
    if not first.startswith('IPV4\n'): raise Unsafe('Unexpected firewall output.')
    v4=first[len('IPV4\n'):].rstrip('\n');v6=second.rstrip('\n')
    validate_firewall(sql_ip,v4,v6)
    app(s);network(s)
    s['rules_sha256']=hashlib.sha256((v4+'\n'+v6).encode()).hexdigest();save(s)
def sanitized_trx(source,target,expected):
    with source.open('rb') as stream: raw=stream.read(8*1024*1024+1)
    if len(raw)>8*1024*1024: raise Unsafe('TRX exceeds the reviewed evidence limit.')
    tree=ET.ElementTree(ET.fromstring(raw));n={'t':'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'};ns='{'+n['t']+'}'
    if tree.getroot().tag!=ns+'TestRun' or not expected:raise Unsafe('TRX root or expected inventory invalid.')
    results=tree.findall('t:Results/t:UnitTestResult',n);definitions=tree.findall('t:TestDefinitions/t:UnitTest',n)
    if any(x.attrib.get('outcome') not in ('Passed','Failed','NotExecuted','Inconclusive','Timeout','Aborted','Error','NotRunnable','Disconnected','Warning','InProgress','Pending') for x in results):raise Unsafe('Unknown TRX outcome.')
    methods=tree.findall('t:TestDefinitions/t:UnitTest/t:TestMethod',n)
    actual=[m.attrib.get('name') for m in methods]
    definition_ids=[x.attrib.get('id') for x in definitions];result_ids=[x.attrib.get('testId') for x in results]
    if any(not isinstance(x,str) or not re.fullmatch('[A-Za-z_][A-Za-z0-9_]*',x) for x in actual+expected):raise Unsafe('Invalid test method identity.')
    if any(not isinstance(x,str) or not re.fullmatch('[A-Za-z0-9_-]{1,80}',x) for x in definition_ids+result_ids):raise Unsafe('Invalid TRX identity.')
    if (len(set(expected))!=len(expected) or sorted(actual)!=sorted(expected) or len(set(actual))!=len(actual) or
        len(results)!=len(expected) or len(definitions)!=len(expected) or None in definition_ids or
        len(set(result_ids))!=len(result_ids) or sorted(definition_ids)!=sorted(result_ids)):
        raise Unsafe('TRX inventory/results mismatch.')
    counters=tree.findall('t:ResultSummary/t:Counters',n)
    if len(counters)!=1:raise Unsafe('TRX counters missing or duplicated.')
    numbers={}
    for key,value in counters[0].attrib.items():
        if not re.fullmatch('[0-9]+',value):raise Unsafe('Invalid TRX counter.')
        numbers[key]=int(value)
    if numbers.get('total')!=len(expected) or numbers.get('executed')!=len(results):raise Unsafe('TRX counters mismatch.')
    passed=sum(x.attrib.get('outcome')=='Passed' for x in results)
    failed=sum(x.attrib.get('outcome')=='Failed' for x in results)
    if numbers.get('passed')!=passed or numbers.get('failed',0)!=failed:raise Unsafe('TRX outcome counters differ.')
    # Reconstruct an allowlisted TRX; arbitrary nodes/attributes/logs are never copied.
    safe=ET.Element(ns+'TestRun',{'name':'C3C02B sanitized'})
    defs=ET.SubElement(safe,ns+'TestDefinitions')
    for definition in definitions:
        method=definition.find('t:TestMethod',n)
        item=ET.SubElement(defs,ns+'UnitTest',{'id':definition.attrib['id'],'name':method.attrib['name']})
        ET.SubElement(item,ns+'TestMethod',{'name':method.attrib['name']})
    rows=ET.SubElement(safe,ns+'Results')
    for row in results:
        item=ET.SubElement(rows,ns+'UnitTestResult',{'testId':row.attrib['testId'],'outcome':row.attrib.get('outcome','Unknown')})
        if row.attrib.get('outcome')!='Passed':
            ET.SubElement(ET.SubElement(ET.SubElement(item,ns+'Output'),ns+'ErrorInfo'),ns+'Message').text='[diagnostic withheld]'
    summary=ET.SubElement(safe,ns+'ResultSummary')
    ET.SubElement(summary,ns+'Counters',{k:str(v) for k,v in numbers.items()})
    text=ET.tostring(safe,encoding='unicode');public_text(text);target.write_text(text,encoding='utf-8')
    return passed==len(expected) and all(value==0 for key,value in numbers.items() if key not in ('total','executed','passed'))

def validate_queries(rows,expected):
    required={'id','rows','elapsed_ms','syntax','restricted_role','plan_sha256','estimated_cost','operators'}
    if not expected or not isinstance(rows,list) or any(not isinstance(x,dict) for x in rows) or len(rows)!=len(expected) or [x.get('id') for x in rows]!=expected or len(set(expected))!=len(expected):
        raise Unsafe('Query evidence inventory differs.')
    for row in rows:
        if set(row)!=required or row['syntax'] is not True or row['restricted_role'] is not True:
            raise Unsafe('Query evidence incomplete.')
        if type(row['rows'])!=int or row['rows']<0 or not isinstance(row['plan_sha256'],str) or not re.fullmatch('[0-9A-Fa-f]{64}',row['plan_sha256']):
            raise Unsafe('Query row/plan proof invalid.')
        for key in ('elapsed_ms','estimated_cost'):
            value=row[key]
            if type(value) not in (int,float) or not math.isfinite(value) or value<0:raise Unsafe('Query metric invalid.')
        if not isinstance(row['operators'],list) or not row['operators'] or any(not isinstance(x,str) or not re.fullmatch('[A-Za-z0-9 /()_-]{1,80}',x) for x in row['operators']):
            raise Unsafe('Query operators missing or private.')
    return rows
def validate_session(s):
    if s.get('schema')!=2 or s.get('repository')!=REPOSITORY or not re.fullmatch('[1-9][0-9]*',s.get('run_id','')) or not re.fullmatch('[0-9a-f]{32}',s.get('nonce','')) or not re.fullmatch('[0-9a-f]{40}',s.get('code_sha','')):
        raise Unsafe('Invalid resource journal.')
    if s.get('labels')!=labels(s) or s.get('network_name')!='c3c02b-'+s['nonce'] or not isinstance(s.get('planned'),dict):
        raise Unsafe('Resource journal ownership differs.')
    if 'phase' in s and s['phase']!=ISOLATION:raise Unsafe('Operational resource creation is blocked.')
    if set(s['planned'])-set(resource_specs(s)): raise Unsafe('Unknown resource type.')
    if 'cleanup_complete' in s and type(s['cleanup_complete']) is not bool:raise Unsafe('Invalid cleanup marker.')
    for key,entry in s['planned'].items():
        kind,name=resource_specs(s)[key]
        if entry.get('kind')!=kind or entry.get('name')!=name or entry.get('phase') not in ('creating','owned','uncertain','removed'):
            raise Unsafe('Resource journal entry differs.')
        if entry['phase'] in ('owned','removed') or entry.get('id') is not None:
            value=entry.get('id','')
            if kind=='volume':
                if value!=name:raise Unsafe('Wrong volume identity.')
            elif kind=='image':
                if not re.fullmatch('sha256:[0-9a-f]{64}',value):raise Unsafe('Wrong image identity.')
            else:exact_id(value)

def resource_specs(s):
    if s.get('phase')==ISOLATION:
        return {'network_id':('network','c3c02b-'+s['nonce']),
                'app_image_id':('image','c3c02b:'+s['nonce']),
                'app_id':('container','c3c02b-app-'+s['nonce']),
                'peer_id':('container','c3c02b-peer-'+s['nonce']),
                'denied_id':('container','c3c02b-denied-'+s['nonce'])}
    return {'network_id':('network','c3c02b-'+s['nonce']),
            'volume_id':('volume','c3c02b-data-'+s['nonce']),
            'container_id':('container','c3c02b-'+s['nonce']),
            'app_image_id':('image','c3c02b:'+s['nonce']),
            'app_id':('container','c3c02b-app-'+s['nonce'])}

def lookup(kind,name):
    result=subprocess.run(checked_command(['docker',kind,'inspect',name]),capture_output=True,timeout=15)
    if result.returncode:
        message=result.stderr.decode(errors='replace').strip()
        escaped=re.escape(name)
        patterns=[r'Error(?: response from daemon)?: No such (?:object|container|image|network|volume): '+escaped,
                  r'Error response from daemon: network '+escaped+r' not found',
                  r'Error response from daemon: get '+escaped+r': no such volume']
        if result.returncode==1 and result.stdout.strip() in (b'',b'[]') and any(re.fullmatch(x,message) for x in patterns):
            return None
        raise Unsafe('Resource existence could not be established.')
    data=json.loads(result.stdout)
    if not isinstance(data,list) or len(data)!=1: raise Unsafe('Ambiguous exact resource lookup.')
    return data[0]

def plan_resource(s,key):
    validate_session(s)
    if key not in resource_specs(s) or key in s['planned']: raise Unsafe('Unexpected/repeated resource creation.')
    kind,name=resource_specs(s)[key]
    s['planned'][key]={'kind':kind,'name':name,'phase':'creating','id':None}
    save(s)  # Ownership intent is durable BEFORE the Docker command.

def verify_owned(s,key,obj):
    kind,name=resource_specs(s)[key]
    raw_labels=obj.get('Config',{}).get('Labels',{}) if kind in ('container','image') else obj.get('Labels',{})
    if any((raw_labels or {}).get(k)!=v for k,v in labels(s).items()): raise Unsafe('Resource owner mismatch.')
    if kind=='volume':
        if obj.get('Name')!=name or obj.get('Driver')!='local' or obj.get('Options'): raise Unsafe('Unsafe named volume.')
        identifier=name  # Docker volumes have a name, not an opaque container-style ID.
    elif kind=='image':
        identifier=obj.get('Id','')
        if not re.fullmatch('sha256:[0-9a-f]{64}',identifier) or name not in obj.get('RepoTags',[]): raise Unsafe('Image identity mismatch.')
    else:
        identifier=exact_id(obj.get('Id'))
        actual=obj.get('Name','').lstrip('/')
        if actual!=name: raise Unsafe('Resource exact name mismatch.')
    if s['planned'][key].get('id') not in (None,identifier): raise Unsafe('Recorded ID was replaced.')
    if key=='network_id':
        if obj.get('Internal') is not True or obj.get('Driver')!='bridge': raise Unsafe('Network not private.')
    if key=='container_id':
        if obj.get('Image')!=s.get('sql_image_id') or obj.get('Config',{}).get('Image')!=SQL_IMAGE:
            raise Unsafe('SQL image ownership differs.')
        h=obj.get('HostConfig',{})
        n=obj.get('NetworkSettings',{}).get('Networks',{})
        # Disconnection is allowed only for an already recorded SQL container during a controlled fault.
        if n and (set(n)!={s['network_name']} or n[s['network_name']].get('NetworkID')!=s.get('network_id')):
            raise Unsafe('SQL membership differs.')
        if not n and s['planned'][key]['phase']=='creating': raise Unsafe('New SQL container has no dedicated network.')
        mounts=obj.get('Mounts',[])
        if h.get('Privileged') or h.get('Binds') or h.get('PortBindings') or h.get('NetworkMode')!=s['network_name'] or len(mounts)!=1:
            raise Unsafe('SQL resource isolation differs.')
        mount=mounts[0]
        if mount.get('Type')!='volume' or mount.get('Name')!=resource_specs(s)['volume_id'][1] or mount.get('Destination')!='/var/opt/mssql':
            raise Unsafe('SQL volume owner differs.')
    if key in ('app_id','peer_id','denied_id') and obj.get('Image')!=s.get('app_image_id'): raise Unsafe('Harness image ownership differs.')
    if s.get('phase')==ISOLATION and kind=='container':validate_app({**s,key:identifier},obj,key)
    return identifier

def reconcile_resource(s,key):
    validate_session(s)
    entry=s['planned'][key]
    kind,name=resource_specs(s)[key]
    if entry.get('kind')!=kind or entry.get('name')!=name: raise Unsafe('Journal resource name differs.')
    query=entry.get('id') or name
    obj=lookup(kind,query)
    if obj is None:
        entry['phase']='uncertain';save(s)
        raise Unsafe('Creation may still be pending; no deletion authorized.')
    identifier=verify_owned(s,key,obj)
    entry.update(id=identifier,phase='owned');s[key]=identifier;save(s)
    return obj

def create_resource(s,key,args,**kwargs):
    plan_resource(s,key)
    try:
        output=command(args,**kwargs)
        obj=reconcile_resource(s,key)
        if key in ('container_id','app_id','peer_id','denied_id','network_id','volume_id') and output!=s[key]:
            raise Unsafe('Creation response does not match inspected ownership.')
        return s[key]
    except (Unsafe,subprocess.TimeoutExpired,OSError,ValueError):
        # Only an exact planned name + complete owner/configuration proof may be adopted.
        try: reconcile_resource(s,key)
        except (Unsafe,subprocess.TimeoutExpired,OSError,ValueError):
            s['planned'][key]['phase']='uncertain'
            try:save(s)
            except OSError:pass  # The earlier intent remains; cleanup must not claim certainty.
        raise

def cleanup():
    p=state_path()
    if not p.exists(): return True
    s=json.loads(p.read_text());validate_session(s)
    uncertain=[]
    for key in ('app_id','peer_id','denied_id','container_id','network_id','volume_id','app_image_id'):
        if key not in s['planned']: continue
        if s['planned'][key]['phase']=='removed':
            if lookup(s['planned'][key]['kind'],s['planned'][key]['id']) is not None:uncertain.append(key)
            continue
        try:
            obj=reconcile_resource(s,key)
            identifier=verify_owned(s,key,obj);kind=s['planned'][key]['kind']
            args=['docker',kind,'rm']
            if kind=='container':args+=['--force']
            command(args+[identifier])  # Exact recorded ID, never prefix/list/prune.
            if lookup(kind,identifier) is not None: raise Unsafe('Resource deletion not confirmed.')
            s['planned'][key]['phase']='removed';save(s)
        except (Unsafe,subprocess.TimeoutExpired,OSError,ValueError):
            s['planned'][key]['phase']='uncertain'
            uncertain.append(key)
    s['cleanup_complete']=not uncertain
    try:save(s)
    except OSError:raise Unsafe('Cleanup journal could not be persisted.') from None
    if uncertain: raise Unsafe('Cleanup incomplete or ownership uncertain.')
    return True
def run():
    raise Unsafe('operational-full is not authorized in B3-B1.')

def operational_full_prepared():
    raise Unsafe('operational-full remains blocked; no SQL resources may be created.')
    # B2's audited preparation is retained below, unreachable until separately redesigned and authorized.
    require_ci(os.environ,os.environ.get('CONSTIA_B3_APPROVED_SHA',''))
    if state_path().exists():raise Unsafe('This run already has a resource journal.')
    require_local_engine()
    root=pathlib.Path(__file__).resolve().parents[2]
    if command(['git','-C',str(root),'rev-parse','HEAD'])!=os.environ['CODE_SHA'] or command(['git','-C',str(root),'status','--porcelain']): raise Unsafe('Unreviewed checkout.')
    nonce=secrets.token_hex(16)
    s=dict(schema=2,repository=REPOSITORY,planned={},run_id=os.environ['GITHUB_RUN_ID'],nonce=nonce,code_sha=os.environ['CODE_SHA'],network_name='c3c02b-'+nonce)
    s['labels']=labels(s); save(s)
    la=[x for k,v in s['labels'].items() for x in ('--label',k+'='+v)]
    create_resource(s,'network_id',['docker','network','create','--internal','--driver','bridge',*la,s['network_name']])
    config=network(s)['IPAM']['Config'][0];s.update(subnet=config['Subnet'],gateway=config['Gateway']);save(s)
    command(['docker','pull',SQL_IMAGE],timeout=300); image_id=inspect('image',SQL_IMAGE)['Id']
    s['sql_image_id']=image_id;save(s)
    volume=resource_specs(s)['volume_id'][1]
    create_resource(s,'volume_id',['docker','volume','create',*la,volume])
    secret=secrets.token_hex(32)+'aA!7'
    env={k:v for k,v in os.environ.items() if k in ('PATH','HOME')}; env['MSSQL_SA_PASSWORD']=secret
    create_resource(s,'container_id',['docker','run','-d','--name',s['network_name'],*la,'--network',s['network_name'],
        '--mount','type=volume,source='+volume+',target=/var/opt/mssql',
        '--env','ACCEPT_EULA=Y','--env','MSSQL_PID=Developer','--env','MSSQL_SA_PASSWORD',SQL_IMAGE],env=env)
    c=inspect('container',s['container_id'])
    m=dict(repository=REPOSITORY,code_sha=s['code_sha'],workflow_sha=os.environ['GITHUB_WORKFLOW_SHA'],workflow_ref=WORKFLOW_REF,run_id=s['run_id'],attempt='1',
        nonce=nonce,container_id=s['container_id'],container_name=s['network_name'],image=SQL_IMAGE,database='C3C02B_'+nonce,
        sql_ip=c['NetworkSettings']['Networks'][s['network_name']]['IPAddress'],hostname=s['container_id'][:12],
        network_id=s['network_id'],network_name=s['network_name'],subnet=s['subnet'],gateway=s['gateway'],sdk_image=SDK_IMAGE,runtime='c3_runtime',observer='c3_observer',migrator='c3_migrator')
    validate_container(m,c,image_id)
    s['sql_ip']=m['sql_ip'];save(s)
    with tempfile.TemporaryDirectory(dir=os.environ['RUNNER_TEMP']) as tmp:
        archive=pathlib.Path(tmp)/'context.tar'
        with tarfile.open(archive,'w') as t:
            for rel in command(['git','-C',str(root),'ls-files','-z']).split('\0'):
                if not rel: continue
                parts=pathlib.PurePosixPath(rel).parts
                if any(p in ('.git','bin','obj','.env') for p in parts): raise Unsafe('Unsafe build input.')
                source=parts[0] in ('src','tools','tests') and (pathlib.PurePosixPath(rel).suffix in ('.cs','.csproj','.props','.targets','.resx') or rel.startswith('tests/sandbox/c3c02b/'))
                if source or rel in ('global.json','Directory.Build.props','Directory.Packages.props'):
                    if (root/rel).is_symlink(): raise Unsafe('Build symlink forbidden.')
                    t.add(root/rel,arcname=rel,recursive=False)
        tag='c3c02b:'+nonce
        create_resource(s,'app_image_id',['docker','build','--file','tests/sandbox/c3c02b/Dockerfile',*la,'--build-arg','CODE_SHA='+s['code_sha'],'--tag',tag,'-'],input=archive.read_bytes(),timeout=600)
    metadata=['GITHUB_ACTIONS','RUNNER_ENVIRONMENT','GITHUB_EVENT_NAME','GITHUB_RUN_ATTEMPT','GITHUB_SHA','GITHUB_WORKFLOW_SHA','GITHUB_WORKFLOW_REF','GITHUB_REF','GITHUB_RUN_ID','CONSTIA_B3_APPROVED_SHA','CODE_SHA']
    inherited=[x for k in metadata for x in ('--env',k+'='+os.environ[k])]
    create_resource(s,'app_id',['docker','run','-d','--name',resource_specs(s)['app_id'][1],*la,*inherited,'--network',s['network_name'],
        '--pid','private','--ipc','private','--cap-drop','ALL','--security-opt','no-new-privileges',
        '--read-only','--tmpfs','/tmp:rw,noexec,nosuid,size=256m','--tmpfs','/run/c3:rw,noexec,nosuid,size=32m',s['app_image_id'],'sleep','infinity'])
    isolate(s,m['sql_ip'])
    credentials={k:secrets.token_hex(32)+'aA!7' for k in ('runtime','observer','migrator')}; credentials['admin']=secret
    for name,value in [('identity.json',m),('credentials.json',credentials)]:
        command(['docker','exec','-i',s['app_id'],'sh','-c','umask 077; cat > /run/c3/'+name],input=json.dumps(value).encode())
    import socket
    with socket.socket() as receiver:
        receiver.bind(('0.0.0.0',0)); receiver.listen(); port=receiver.getsockname()[1]
        gateway=network(s)['IPAM']['Config'][0]['Gateway']
        sentinel=json.dumps({'gateway':gateway,'port':port}).encode()
        command(['docker','exec','-i',s['app_id'],'sh','-c','umask 077; cat > /run/c3/sentinel.json'],input=sentinel)
        # Positive receiver control occurs on the runner only; the harness must remain unable to reach it.
        with socket.create_connection(('127.0.0.1',port),timeout=1):
            control,_=receiver.accept();control.close()
        command(['docker','exec',s['app_id'],'dotnet','/app/harness/Constia.C3C02B.Harness.dll','probe',gateway,str(port)],timeout=10)
        receiver.settimeout(0.1)
        try:
            conn,_=receiver.accept(); conn.close(); raise Unsafe('Gateway sentinel reachable.')
        except TimeoutError: pass
    command(['docker','exec',s['app_id'],'dotnet','/app/harness/Constia.C3C02B.Harness.dll','verify'],timeout=90)
    coverage=json.loads((root/'tests/sandbox/c3c02b/coverage.json').read_text())
    import threading
    stopped=threading.Event(); control_errors=[]
    def service_control():
        previous=None
        try:
            while not stopped.wait(0.2):
                body=command(['docker','exec',s['app_id'],'sh','-c','test ! -f /run/c3/request.json || cat /run/c3/request.json'])
                if not body: continue
                request=json.loads(body)
                if set(request)!={'id','action','nonce'} or request['nonce']!=nonce or not re.fullmatch('[0-9a-f]{32}',request['id']) or request['action'] not in ('disconnect','reconnect'): raise Unsafe('Invalid control request.')
                if request['id']==previous: continue
                app(s);network(s)
                current=inspect('container',s['container_id'])
                if current['Id']!=s['container_id'] or current['Image']!=image_id or any(current['Config'].get('Labels',{}).get(k)!=v for k,v in s['labels'].items()): raise Unsafe('Fault target mismatch.')
                if request['action']=='disconnect':
                    validate_container(m,current,image_id)
                    command(['docker','network','disconnect',exact_id(s['network_id']),exact_id(s['container_id'])])
                else:
                    if current['NetworkSettings']['Networks']: raise Unsafe('SQL has an unexpected attachment.')
                    command(['docker','network','connect','--ip',m['sql_ip'],exact_id(s['network_id']),exact_id(s['container_id'])])
                    validate_container(m,inspect('container',s['container_id']),image_id)
                previous=request['id']
                ack=json.dumps({'id':previous,'status':'ok'}).encode()
                command(['docker','exec','-i',s['app_id'],'sh','-c','umask 077; cat > /run/c3/ack.tmp; mv /run/c3/ack.tmp /run/c3/ack.json'],input=ack)
        except Exception:
            control_errors.append('Fault service failed closed.')
    control_thread=threading.Thread(target=service_control,daemon=True);control_thread.start()
    test_failed=False
    try:
        command(['docker','exec',s['app_id'],'dotnet','vstest','/app/tests/Constia.C3C02B.Tests.dll',
                 '/Logger:trx;LogFileName=operational.trx','/ResultsDirectory:/run/c3/results'],timeout=1800)
    except (Unsafe,subprocess.TimeoutExpired):
        test_failed=True
    finally:
        stopped.set();control_thread.join(timeout=65)
    out=public_directory()
    raw=pathlib.Path(os.environ['RUNNER_TEMP'])/('c3c02b-'+nonce+'.trx')
    success=False
    try:
        command(['docker','cp',s['app_id']+':/run/c3/results/operational.trx',str(raw)])
        success=sanitized_trx(raw,out/'operational.trx',coverage['test_methods'])
    except (Unsafe,OSError,ValueError,TypeError,ET.ParseError,subprocess.TimeoutExpired):
        success=False
    query_file=pathlib.Path(os.environ['RUNNER_TEMP'])/('c3c02b-'+nonce+'-queries.json')
    query_ok=False
    try:
        command(['docker','cp',s['app_id']+':/run/c3/query-evidence/blocks.json',str(query_file)])
        if query_file.stat().st_size>512*1024:raise Unsafe('Query evidence exceeds limit.')
        query_text=query_file.read_text();public_text(query_text)
        validate_queries(json.loads(query_text),coverage['query_blocks']);query_ok=True
        (out/'query-blocks.json').write_text(query_text,encoding='utf-8')
    except (Unsafe,OSError,ValueError,TypeError,subprocess.TimeoutExpired):
        query_ok=False
    success=success and not test_failed and query_ok and not control_thread.is_alive() and not control_errors
    text=json.dumps(dict(identity=m,rules_sha256=s['rules_sha256'],real_provider_calls=0,
        basis='verified output allowlist and mandatory simulated transport',sql_major_checked=16,
        restore_and_build='completed',status='evidence_complete_cleanup_pending' if success else 'failed',base=BASE,
        trx_complete=success,query_complete=query_ok,test_process_success=not test_failed,
        control_stopped=not control_thread.is_alive() and not control_errors))
    public_text(text); (out/'identity-and-isolation.json').write_text(text,encoding='utf-8')
    if not success: raise Unsafe('Operational evidence contains failures or missing cases.')
def public_directory():
    root=pathlib.Path(os.environ['RUNNER_TEMP']).resolve()
    out=root/'c3c02b-public'
    if out.is_symlink() or out.resolve().parent!=root:raise Unsafe('Evidence path escapes the runner.')
    out.mkdir(exist_ok=True)
    return out

def final_status(run_ok,cleanup_ok,evidence_ok,previous_failure=False):
    return 'passed' if run_ok is True and cleanup_ok is True and evidence_ok is True and previous_failure is False else 'failed'

def publish_execution(run_ok,cleanup_ok,operation):
    out=public_directory();report_path=out/'execution.json'
    previous=json.loads(report_path.read_text()) if report_path.exists() else None
    if previous and (previous.get('run_id')!=os.environ['GITHUB_RUN_ID'] or previous.get('code_sha')!=os.environ['CODE_SHA']):
        raise Unsafe('Evidence belongs to another run.')
    if operation=='cleanup':
        run_ok=bool(previous and previous.get('run_ok') is True)
    mandatory=('operational.trx','identity-and-isolation.json','query-blocks.json')
    evidence_ok=all((out/x).is_file() for x in mandatory)
    if operation=='cleanup' and previous:
        try:
            hashes=json.loads((out/'evidence-sha256.json').read_text())
            evidence_ok=evidence_ok and {x['path'] for x in hashes}==set((*mandatory,'execution.json')) and len(hashes)==4
            evidence_ok=evidence_ok and all(hashlib.sha256((out/x['path']).read_bytes()).hexdigest()==x['sha256'] for x in hashes)
        except (OSError,ValueError,KeyError,TypeError):evidence_ok=False
    journal=None
    if state_path().exists():
        journal=json.loads(state_path().read_text());validate_session(journal)
        if journal['run_id']!=os.environ['GITHUB_RUN_ID'] or journal['code_sha']!=os.environ['CODE_SHA']:
            raise Unsafe('Cleanup report run identity differs.')
    cleanup_ok=cleanup_ok and bool(journal and journal.get('cleanup_complete'))
    status=final_status(run_ok,cleanup_ok,evidence_ok,bool(previous and previous.get('status')=='failed'))
    resources=[]
    if journal:
        for entry in journal['planned'].values():
            resources.append({k:entry.get(k) for k in ('kind','name','id','phase')})
    report=dict(status=status,run_id=os.environ['GITHUB_RUN_ID'],code_sha=os.environ['CODE_SHA'],
        workflow_sha=os.environ['GITHUB_WORKFLOW_SHA'],run_ok=run_ok,evidence_complete=evidence_ok,
        cleanup_verified=cleanup_ok,resources=resources,
        failures=[] if status=='passed' else ['run_evidence_or_cleanup_not_verified'])
    text=json.dumps(report,sort_keys=True);public_text(text)
    report_path.write_text(text,encoding='utf-8')
    identity_path=out/'identity-and-isolation.json'
    if identity_path.exists():
        identity=json.loads(identity_path.read_text())
        identity['status']=status;identity['cleanup_verified']=cleanup_ok
        body=json.dumps(identity,sort_keys=True);public_text(body);identity_path.write_text(body,encoding='utf-8')
    hashes=[]
    for name in (*mandatory,'execution.json'):
        path=out/name
        if path.is_file():hashes.append(dict(path=name,bytes=path.stat().st_size,sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
    (out/'evidence-sha256.json').write_text(json.dumps(hashes,sort_keys=True),encoding='utf-8')
    return status

def verify_installed_rules(s):
    a=app(s);ns=command(['docker','exec',s['app_id'],'readlink','/proc/self/ns/net'])
    if ns!=s['namespace']:raise Unsafe('Firewall namespace changed.')
    script='test "$(readlink /proc/self/ns/net)" = "$1"; printf "IPV4\\n"; iptables -S OUTPUT; printf "IPV6\\n"; ip6tables -S OUTPUT'
    raw=command(['sudo','nsenter','--target',str(a['State']['Pid']),'--net','--','sh','-eu','-c',script,'c3-rules',ns])
    if not raw.startswith('IPV4\n') or raw.count('IPV6\n')!=1:raise Unsafe('Firewall proof absent.')
    v4,v6=raw[len('IPV4\n'):].split('IPV6\n');v4=v4.rstrip('\n');v6=v6.rstrip('\n')
    validate_firewall(s['peer_ip'],v4,v6)
    if hashlib.sha256((v4+'\n'+v6).encode()).hexdigest()!=s['rules_sha256']:raise Unsafe('Firewall changed during probes.')

def verify_final_isolation(s,route_hash):
    verify_installed_rules(s);network(s)
    if routes_proof(s)!=route_hash:raise Unsafe('Routes changed during probes.')

def routes_proof(s):
    a=app(s)
    ns=command(['docker','exec',s['app_id'],'readlink','/proc/self/ns/net'])
    if ns!=s['namespace']:raise Unsafe('Preflight namespace changed.')
    script='test "$(readlink /proc/self/ns/net)" = "$1"; ip -j -4 route show table all; printf "\\nIPV6\\n"; ip -j -6 route show table all'
    raw=command(['sudo','nsenter','--target',str(a['State']['Pid']),'--net','--','sh','-eu','-c',script,'c3-routes',ns])
    if raw.count('\nIPV6\n')!=1:raise Unsafe('Route proof missing.')
    v4,v6=raw.split('\nIPV6\n');validate_routes(s,json.loads(v4),json.loads(v6))
    return hashlib.sha256(raw.encode()).hexdigest()

def validate_routes(s,v4,v6):
    if not isinstance(v4,list) or not v4 or not isinstance(v6,list):raise Unsafe('Routes missing.')
    connected=False
    for row in v4:
        if not isinstance(row,dict) or row.get('dev') not in ('lo','eth0'):raise Unsafe('Unexpected route interface.')
        dest=row.get('dst');gateway=row.get('gateway')
        if gateway is not None and (gateway!=s['gateway'] or dest!='default' or row.get('dev')!='eth0'):raise Unsafe('Unexpected gateway route.')
        if dest==s['subnet'] and row.get('dev')=='eth0' and gateway is None:connected=True;continue
        if dest=='default' and gateway==s['gateway']:continue
        try:net=ipaddress.ip_network(dest,strict=False)
        except (ValueError,TypeError):raise Unsafe('Ambiguous route.') from None
        if row.get('dev')=='lo' and net.subnet_of(ipaddress.ip_network('127.0.0.0/8')):continue
        if row.get('type') in ('local','broadcast') and net.subnet_of(ipaddress.ip_network(s['subnet'])):continue
        raise Unsafe('Route outside exclusive topology.')
    if not connected:raise Unsafe('No route to exclusive peer network.')
    if any(not isinstance(row,dict) or row.get('dev')!='lo' or row.get('dst') not in ('::1','::1/128') for row in v6):
        raise Unsafe('Non-loopback IPv6 route.')

def build_preflight_image(root,s,la):
    with tempfile.TemporaryDirectory(dir=os.environ['RUNNER_TEMP']) as tmp:
        archive=pathlib.Path(tmp)/'context.tar'
        with tarfile.open(archive,'w') as t:
            for rel in command(['git','-C',str(root),'ls-files','-z']).split('\0'):
                if not rel:continue
                parts=pathlib.PurePosixPath(rel).parts
                if any(p in ('.git','bin','obj','.env') for p in parts):raise Unsafe('Unsafe build input.')
                source=parts[0] in ('src','tools','tests') and (pathlib.PurePosixPath(rel).suffix in ('.cs','.csproj','.props','.targets','.resx') or rel.startswith('tests/sandbox/c3c02b/'))
                if source or rel in ('global.json','Directory.Build.props','Directory.Packages.props'):
                    if (root/rel).is_symlink():raise Unsafe('Build symlink forbidden.')
                    t.add(root/rel,arcname=rel,recursive=False)
        create_resource(s,'app_image_id',['docker','build','--file','tests/sandbox/c3c02b/Dockerfile',*la,
            '--build-arg','CODE_SHA='+s['code_sha'],'--tag',resource_specs(s)['app_image_id'][1],'-'],input=archive.read_bytes(),timeout=600)

def write_preflight_identity(s,m):
    validate_preflight(m)
    for key in ('app_id','peer_id','denied_id'):
        app(s,key)
        command(['docker','exec','-i',s[key],'sh','-c','umask 077; cat > /run/c3/preflight.tmp; mv /run/c3/preflight.tmp /run/c3/preflight.json'],input=json.dumps(m).encode())

def run_isolation(phase):
    e=os.environ;authorize_isolation(e,phase)
    root=pathlib.Path(__file__).resolve().parents[2]
    if command(['git','-C',str(root),'rev-parse','HEAD'])!=e['CODE_SHA'] or command(['git','-C',str(root),'status','--porcelain']):raise Unsafe('Unreviewed checkout.')
    digest=verify_inventory(root,e)
    if state_path().exists():raise Unsafe('Run already has a resource journal.')
    require_local_engine()
    nonce=secrets.token_hex(16)
    s=dict(schema=2,phase=ISOLATION,repository=REPOSITORY,planned={},run_id=e['GITHUB_RUN_ID'],nonce=nonce,
           code_sha=e['CODE_SHA'],policy_sha256=digest,workflow_sha=e['GITHUB_WORKFLOW_SHA'],actor_id=e['GITHUB_ACTOR_ID'],network_name='c3c02b-'+nonce)
    s['labels']=labels(s);save(s)
    la=[x for k,v in s['labels'].items() for x in ('--label',k+'='+v)]
    build_preflight_image(root,s,la)
    create_resource(s,'network_id',['docker','network','create','--internal','--driver','bridge',*la,s['network_name']])
    config=network(s)['IPAM']['Config'][0];s.update(subnet=config['Subnet'],gateway=config['Gateway']);save(s)
    metadata=('GITHUB_ACTIONS','RUNNER_ENVIRONMENT','GITHUB_EVENT_NAME','GITHUB_RUN_ATTEMPT','GITHUB_SHA',
              'GITHUB_WORKFLOW_SHA','GITHUB_WORKFLOW_REF','GITHUB_REF','GITHUB_RUN_ID','CODE_SHA',
              'GITHUB_ACTOR_ID','CONSTIA_B3_APPROVED_ACTOR_ID','CONSTIA_B3_APPROVED_SHA',
              'C3C02B_PHASE','CONSTIA_B3_APPROVED_PHASE','CONSTIA_B3_INVENTORY_SHA256')
    inherited=[x for k in metadata for x in ('--env',k+'='+e[k])]
    for key in ('app_id','peer_id','denied_id'):
        create_resource(s,key,['docker','run','-d','--name',resource_specs(s)[key][1],*la,*inherited,
            '--network',s['network_name'],'--pid','private','--ipc','private','--cap-drop','ALL',
            '--security-opt','no-new-privileges','--read-only','--tmpfs','/tmp:rw,noexec,nosuid,size=256m',
            '--tmpfs','/run/c3:rw,noexec,nosuid,size=32m',s['app_image_id'],'sleep','infinity'])
    addresses={key:app(s,key)['NetworkSettings']['Networks'][s['network_name']]['IPAddress'] for key in ('app_id','peer_id','denied_id')}
    s['peer_ip']=addresses['peer_id'];s['app_ip']=addresses['app_id'];s['denied_ip']=addresses['denied_id']
    s['namespace']=command(['docker','exec',s['app_id'],'readlink','/proc/self/ns/net']);save(s)
    import socket,threading,time
    stop=threading.Event();receiver_errors=[]
    with socket.socket() as receiver:
        receiver.bind(('0.0.0.0',0));receiver.listen();receiver.settimeout(0.2)
        m=dict(phase=ISOLATION,repository=REPOSITORY,code_sha=s['code_sha'],policy_sha256=digest,
            workflow_sha=s['workflow_sha'],workflow_ref=WORKFLOW_REF,actor_id=s['actor_id'],run_id=s['run_id'],attempt='1',nonce=nonce,
            network_id=s['network_id'],network_name=s['network_name'],subnet=s['subnet'],gateway=s['gateway'],
            app_id=s['app_id'],app_ip=addresses['app_id'],peer_id=s['peer_id'],peer_ip=addresses['peer_id'],
            denied_id=s['denied_id'],denied_ip=addresses['denied_id'],image_id=s['app_image_id'],sdk_image=SDK_IMAGE,
            namespace=s['namespace'],rules_sha256=None,proof_stage='prepared',gateway_port=receiver.getsockname()[1])
        write_preflight_identity(s,m)
        def serve():
            try:
                while not stop.is_set():
                    try:conn,_=receiver.accept()
                    except TimeoutError:continue
                    with conn:
                        conn.settimeout(2);data=b''
                        while len(data)<32:
                            chunk=conn.recv(32-len(data))
                            if not chunk:break
                            data+=chunk
                        if data==nonce.encode():conn.sendall(('c3c02b:'+nonce+':gateway').encode())
            except OSError:
                if not stop.is_set():receiver_errors.append('synthetic_receiver_failed')
        thread=threading.Thread(target=serve,daemon=True);thread.start()
        try:
            for key,role in [('peer_id','peer'),('denied_id','denied')]:
                command(['docker','exec','-d',s[key],'dotnet','/app/harness/Constia.C3C02B.Harness.dll','isolation-'+role])
                deadline=time.monotonic()+15
                while command(['docker','exec',s[key],'sh','-c','test ! -f /run/c3/receiver-ready || cat /run/c3/receiver-ready'])!='ready':
                    if time.monotonic()>=deadline:raise Unsafe('Synthetic receiver not ready.')
                    time.sleep(0.1)
            if command(['docker','exec',s['app_id'],'dotnet','/app/harness/Constia.C3C02B.Harness.dll','isolation-controls'])!='CONTROLS_POSITIVE':raise Unsafe('Positive controls failed.')
            isolate(s,m['peer_ip']);m.update(proof_stage='isolated',rules_sha256=s['rules_sha256']);write_preflight_identity(s,m)
            route_hash=routes_proof(s)
            result=command(['docker','exec',s['app_id'],'dotnet','/app/harness/Constia.C3C02B.Harness.dll',ISOLATION],timeout=30)
            # Fake transport emits only fixed phase and hash lines; require the final phase-specific marker.
            if result.splitlines()[-1:]!=['ISOLATION_PASSED_NO_SQL_NO_WORKERS']:raise Unsafe('Isolation probes incomplete.')
            for key,role in [('peer_id','peer'),('denied_id','denied')]:
                if command(['docker','exec',s[key],'dotnet','/app/harness/Constia.C3C02B.Harness.dll',role+'-health'])!='PEER_HEALTHY':raise Unsafe('Negative control receiver was not alive.')
            with socket.create_connection(('127.0.0.1',m['gateway_port']),timeout=2) as control:
                control.sendall(nonce.encode());reply=b'';expected=('c3c02b:'+nonce+':gateway').encode()
                while len(reply)<len(expected):
                    chunk=control.recv(len(expected)-len(reply))
                    if not chunk:break
                    reply+=chunk
                if reply!=expected:raise Unsafe('Gateway receiver health failed.')
            if receiver_errors:raise Unsafe('Synthetic receiver failed.')
            # Recheck actual rules and topology after probes; proof cannot survive drift.
            verify_final_isolation(s,route_hash)
            report=dict(identity=m,phase=ISOLATION,allowed_peer=True,denied_peer=True,gateway_blocked=True,
                        controls_positive=True,simulated_transport=True,routes_sha256=route_hash,sql='not_applicable',workers_started=0)
            validate_isolation_evidence(report);body=json.dumps(report,sort_keys=True);public_text(body)
            (public_directory()/'isolation.json').write_text(body,encoding='utf-8')
        finally:
            stop.set();thread.join(timeout=3)
            if thread.is_alive():raise Unsafe('Synthetic receiver did not stop.')

def validate_isolation_journal(s,e):
    validate_session(s)
    allowed=set('schema phase repository planned run_id nonce code_sha policy_sha256 workflow_sha actor_id network_name labels cleanup_complete app_image_id network_id subnet gateway app_id peer_id denied_id peer_ip app_ip denied_ip namespace rules_sha256'.split())
    if set(s)-allowed or s.get('phase')!=ISOLATION:raise Unsafe('Foreign/SQL cleanup journal.')
    for key,env in [('run_id','GITHUB_RUN_ID'),('code_sha','CODE_SHA'),('policy_sha256','CONSTIA_B3_INVENTORY_SHA256'),
                    ('workflow_sha','GITHUB_WORKFLOW_SHA'),('actor_id','GITHUB_ACTOR_ID')]:
        if s.get(key)!=e.get(env):raise Unsafe('Cleanup authorization identity differs.')

def publish_isolation(run_ok,cleanup_ok,operation):
    out=public_directory();report_path=out/'isolation-execution.json'
    previous=json.loads(report_path.read_text()) if report_path.exists() else None
    if previous and (previous.get('run_id')!=os.environ['GITHUB_RUN_ID'] or previous.get('code_sha')!=os.environ['CODE_SHA'] or previous.get('phase')!=ISOLATION):raise Unsafe('Wrong preflight report owner.')
    permitted={'isolation.json','isolation-execution.json','isolation-sha256.json'}
    if any(p.name not in permitted or p.is_symlink() or not p.is_file() for p in out.iterdir()):raise Unsafe('Unexpected/operational artifacts forbidden in preflight.')
    evidence_ok=False
    try:
        report=json.loads((out/'isolation.json').read_text());validate_isolation_evidence(report)
        m=report['identity'];e=os.environ
        evidence_ok=all(m[k]==e[v] for k,v in [('code_sha','CODE_SHA'),('workflow_sha','GITHUB_WORKFLOW_SHA'),
            ('policy_sha256','CONSTIA_B3_INVENTORY_SHA256'),('run_id','GITHUB_RUN_ID'),('actor_id','GITHUB_ACTOR_ID')])
        if operation=='cleanup':
            hashes=json.loads((out/'isolation-sha256.json').read_text())
            evidence_ok=evidence_ok and set(hashes)=={'isolation.json','isolation-execution.json'} and all(hashlib.sha256((out/name).read_bytes()).hexdigest()==value for name,value in hashes.items())
    except (Unsafe,OSError,ValueError,TypeError,KeyError):evidence_ok=False
    journal=json.loads(state_path().read_text()) if state_path().exists() else None
    if journal:
        validate_isolation_journal(journal,os.environ)
    cleanup_ok=cleanup_ok and bool(journal and journal.get('cleanup_complete'))
    if operation=='cleanup':run_ok=bool(previous and previous.get('run_ok') is True)
    status=final_status(run_ok,cleanup_ok,evidence_ok,bool(previous and previous.get('status')=='failed'))
    final=dict(phase=ISOLATION,status=status,code_sha=os.environ['CODE_SHA'],workflow_sha=os.environ['GITHUB_WORKFLOW_SHA'],
               policy_sha256=os.environ['CONSTIA_B3_INVENTORY_SHA256'],run_id=os.environ['GITHUB_RUN_ID'],attempt='1',
               run_ok=run_ok,cleanup_verified=cleanup_ok,evidence_complete=evidence_ok,sql='not_applicable',workers_started=0,
               resources=list(journal['planned'].values()) if journal else [])
    body=json.dumps(final,sort_keys=True);public_text(body);report_path.write_text(body,encoding='utf-8')
    hashes={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in (out/'isolation.json',report_path) if p.is_file()}
    (out/'isolation-sha256.json').write_text(json.dumps(hashes,sort_keys=True),encoding='utf-8')
    return status

def main():
    parser=argparse.ArgumentParser();parser.add_argument('operation',choices=('run','cleanup'))
    parser.add_argument('--phase',required=True,action='append');args=parser.parse_args()
    phase=args.phase[0] if len(args.phase)==1 else None
    run_ok=False;cleanup_ok=False
    try:
        authorize_isolation(os.environ,phase)
    except Unsafe:
        print('C3C02B not authorized; no infrastructure started.',file=sys.stderr);return 1
    try:
        if args.operation=='run':
            run_isolation(phase);run_ok=True
    except (Exception,KeyboardInterrupt):
        run_ok=False  # Do not copy arbitrary exceptions/credentials into evidence.
    finally:
        try:
            if state_path().exists():
                journal=json.loads(state_path().read_text())
                validate_isolation_journal(journal,os.environ)
            cleanup_ok=cleanup()
        except (Exception,KeyboardInterrupt):cleanup_ok=False
    try:
        status=publish_isolation(run_ok,cleanup_ok,args.operation)
    except (Exception,KeyboardInterrupt):
        print('C3C02B evidence finalization failed closed.',file=sys.stderr);return 1
    if status!='passed':
        print('C3C02B failed or remains unverified; inspect sanitized evidence.',file=sys.stderr);return 1
    return 0
if __name__=='__main__':sys.exit(main())
