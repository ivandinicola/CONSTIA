"""Manual-only disposable runner. Imports and pure guards perform no I/O."""
import argparse, gzip, hashlib, ipaddress, json, math, os, pathlib, re, secrets, subprocess, sys, tarfile, tempfile
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

def image_build_command(args,*,env=None,input=None,timeout=60):
    """Classify only structured signals at the image subprocess boundary."""
    args=checked_command(args)  # Preparation failures retain the historical fallback.
    try:
        result=subprocess.run(args,env=env,input=input,capture_output=True,timeout=timeout)
    except FileNotFoundError:
        _image_failure('BUILD_START_NOT_FOUND')
        raise
    except PermissionError:
        _image_failure('BUILD_START_DENIED')
        raise
    except OSError:
        _image_failure('BUILD_START_FAILED')
        raise
    except subprocess.TimeoutExpired:
        _image_failure('BUILD_TIMEOUT')
        raise
    except Exception:
        _image_failure('BUILD_UNEXPECTED_EXCEPTION')
        raise
    if result.returncode:
        _image_failure('BUILD_EXIT_NONZERO')
        raise Unsafe('Bounded operation failed; raw output withheld.')
    try:
        return result.stdout.decode().strip()
    except Exception:
        _image_failure('BUILD_UNEXPECTED_EXCEPTION')
        raise


def _image_failure(reason):
    c=ACTIVE_DIAGNOSTIC.get()
    if c is not None:c.fail('resource_image','RESOURCE_UNVERIFIED',reason)

def inspect(kind,x): return json.loads(command(['docker',kind,'inspect',x]))[0]

def require_local_engine():
    data=json.loads(command(['docker','context','inspect','default']))
    if len(data)!=1 or data[0].get('Endpoints',{}).get('docker',{}).get('Host')!='unix:///var/run/docker.sock':
        raise Unsafe('Only the default local Linux Docker endpoint is allowed.')
def state_path(): return pathlib.Path(os.environ['RUNNER_TEMP'])/('c3c02b-'+os.environ['GITHUB_RUN_ID']+'.json')


# Schema 3: one complete immutable manifest. Observations are never coverage.
MANIFEST_SCHEMA=3
PRODUCER_STATES=frozenset(('no_dispatch','quiescent','unresolved','unknown'))

def _session_identity():
    e=os.environ
    return dict(repository=REPOSITORY,run_id=e['GITHUB_RUN_ID'],attempt=e['GITHUB_RUN_ATTEMPT'],
                code_sha=e['CODE_SHA'],workflow_sha=e['GITHUB_WORKFLOW_SHA'],workflow_ref=e['GITHUB_WORKFLOW_REF'],
                policy_sha256=e['CONSTIA_B3_INVENTORY_SHA256'],actor_id=e['GITHUB_ACTOR_ID'])

def _manifest_for(s):
    return dict(schema=MANIFEST_SCHEMA,phase=ISOLATION,identity=_session_identity(),nonce=s['nonce'],
                resources={k:dict(kind=kind,name=name) for k,(kind,name) in resource_specs(s).items()})

def validate_manifest(m):
    authorize_isolation(os.environ,ISOLATION)
    if not isinstance(m,dict) or set(m)!={'schema','phase','identity','nonce','resources'}:
        raise Unsafe('Manifest fields differ.')
    if type(m['schema']) is not int or m['schema']!=3 or m['phase']!=ISOLATION or m['identity']!=_session_identity():
        raise Unsafe('Manifest identity differs.')
    if not isinstance(m['nonce'],str) or not re.fullmatch('[0-9a-f]{32}',m['nonce']):
        raise Unsafe('Manifest nonce differs.')
    expected={k:dict(kind=kind,name=name) for k,(kind,name) in resource_specs(dict(phase=ISOLATION,nonce=m['nonce'])).items()}
    if m['resources']!=expected:raise Unsafe('Potential resource universe differs.')

class _LegacyJournal(Unsafe):pass

def _read_manifest():
    p=state_path()
    before=p.lstat();raw=_bounded_bytes(p,131072);after=p.lstat()
    if _file_identity(before)!=_file_identity(after):raise Unsafe('Manifest changed during read.')
    m=_strict_json(raw)
    if isinstance(m,dict) and m.get('schema')==2:
        validate_isolation_journal(m,os.environ)
        raise _LegacyJournal('Legacy journals cannot certify recovery.')
    validate_manifest(m);digest=hashlib.sha256(raw).hexdigest();fingerprint=_file_identity(after)
    c=ACTIVE_DIAGNOSTIC.get()
    if c is not None and c.controller is not None:
        ctrl=c.controller
        if ctrl.bad or m!=ctrl.manifest or digest!=ctrl.digest or fingerprint!=ctrl.fingerprint:
            ctrl.bad=True;raise Unsafe('Manifest was substituted.')
    return m,digest,fingerprint

def _read_journal(path=None):
    if path is not None and path!=state_path():raise Unsafe('Journal path rejected.')
    return _read_manifest()[0]

def save(s):
    raise Unsafe('Mutable journal persistence retired; use manifest admission and explicit observations.')

def _save_journal(s):
    raise Unsafe('Generation publication retired.')

class _Producer:
    def __init__(self,s,manifest,digest,fingerprint):
        self.state=s;self.manifest=manifest;self.digest=digest;self.fingerprint=fingerprint
        self.active=True;self.dispatched=set();self.pending=set();self.ambiguous=False;self.bad=False;self.terminal_state=None
    def check(self,s):
        if self.bad or s is not self.state or _manifest_for(s)!=self.manifest:
            raise Unsafe('Producer session differs.')
        _read_manifest()
    def begin(self,s,key):
        if not self.active or key not in self.manifest['resources'] or key in self.dispatched:
            raise Unsafe('Producer closed or dispatch repeated.')
        self.check(s);self.dispatched.add(key);self.pending.add(key)
        ACTIVE_DIAGNOSTIC.get().creation_attempted=True
    def acknowledge(self,key):
        if not self.active or key not in self.pending:raise Unsafe('Unregistered or late creation response.')
        self.pending.remove(key)
    def freeze(self):
        if not self.active:return self.terminal_state
        self.active=False
        self.terminal_state='unresolved' if self.bad or self.ambiguous or self.pending else ('quiescent' if self.dispatched else 'no_dispatch')
        return self.terminal_state

def _admit_session(s):
    c=ACTIVE_DIAGNOSTIC.get()
    if c is None or c.controller is not None:raise Unsafe('Session admission requires unique run context.')
    validate_session(s)
    manifest=_manifest_for(s);validate_manifest(manifest)
    try:
        digest,fingerprint=diagnostic_call('journal_write',_publish_immutable_record_fd,state_path(),manifest,validate_manifest)
    except BaseException:
        if state_path().exists():c.journal_seen=True
        raise
    c.controller=_Producer(s,manifest,digest,fingerprint)
    c.session=dict(nonce=s['nonce'],manifest_sha256=digest,producer_state='unknown')
    c.journal_seen=True;c.journal_state='valid'
    return c.controller

def _observe_session(s):
    c=ACTIVE_DIAGNOSTIC.get()
    if c is None or c.controller is None:raise Unsafe('Observation has no admitted producer.')
    validate_session(s);c.controller.check(s)
    return s

def _freeze_producer(c):
    state=c.controller.freeze() if c.controller is not None else ('unknown' if c.creation_attempted else 'no_dispatch')
    c.session['producer_state']=state

def _working_state(m):
    i=m['identity']
    s=dict(schema=2,phase=ISOLATION,repository=REPOSITORY,run_id=i['run_id'],nonce=m['nonce'],
           code_sha=i['code_sha'],policy_sha256=i['policy_sha256'],workflow_sha=i['workflow_sha'],
           actor_id=i['actor_id'],network_name='c3c02b-'+m['nonce'],planned={})
    s['labels']=labels(s)
    for key,row in m['resources'].items():
        s['planned'][key]=dict(kind=row['kind'],name=row['name'],id=None,phase='uncertain')
    return s

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
    s['rules_sha256']=hashlib.sha256((v4+'\n'+v6).encode()).hexdigest();_observe_session(s)
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
    result=_bounded_docker(['docker',kind,'inspect',name])
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
    c=ACTIVE_DIAGNOSTIC.get()
    if c is None or c.controller is None:raise Unsafe('No admitted producer.')
    c.controller.begin(s,key)
    kind,name=resource_specs(s)[key]
    s['planned'][key]=dict(kind=kind,name=name,phase='creating',id=None)
    _observe_session(s)

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

def reconcile_resource(s,key,persist=None):
    persist = _observe_session if persist is None else persist
    validate_session(s)
    entry=s['planned'][key]
    kind,name=resource_specs(s)[key]
    if entry.get('kind')!=kind or entry.get('name')!=name: raise Unsafe('Journal resource name differs.')
    query=entry.get('id') or name
    obj=lookup(kind,query)
    if obj is None:
        entry['phase']='uncertain';persist(s)
        raise Unsafe('Creation may still be pending; no deletion authorized.')
    identifier=verify_owned(s,key,obj)
    entry.update(id=identifier,phase='owned');s[key]=identifier;persist(s)
    return obj

def _create_resource(s,key,args,**kwargs):
    plan_resource(s,key)
    try:
        c = ACTIVE_DIAGNOSTIC.get()
        if c is not None: c.creation_attempted = True
        try:
            output=diagnostic_call(_resource_stage(key), image_build_command if key=='app_image_id' else command, args, **kwargs)
        except BaseException:
            ACTIVE_DIAGNOSTIC.get().controller.ambiguous=True
            raise
        ACTIVE_DIAGNOSTIC.get().controller.acknowledge(key)
        try:
            obj=reconcile_resource(s,key)
        except Exception:
            if key=='app_image_id':_image_failure('IMAGE_CONFIRMATION_FAILED')
            raise
        if key in ('container_id','app_id','peer_id','denied_id','network_id','volume_id') and output!=s[key]:
            raise Unsafe('Creation response does not match inspected ownership.')
        return s[key]
    except (Unsafe,subprocess.TimeoutExpired,OSError,ValueError):
        c = ACTIVE_DIAGNOSTIC.get()
        if c is not None:
            stage = _resource_stage(key)
            c.fail(stage, *_DIAGNOSTIC_DEFAULTS[stage])
        # Only an exact planned name + complete owner/configuration proof may be adopted.
        try: reconcile_resource(s,key)
        except (Unsafe,subprocess.TimeoutExpired,OSError,ValueError):
            s['planned'][key]['phase']='uncertain'
            try:_observe_session(s)
            except OSError:pass  # The earlier intent remains; cleanup must not claim certainty.
        raise

def _resource_stage(key):
    return dict(app_image_id='resource_image', network_id='resource_network',
                app_id='resource_app', peer_id='resource_peer', denied_id='resource_denied').get(key, 'unexpected')

def create_resource(s,key,args,**kwargs):
    return diagnostic_call(_resource_stage(key), _create_resource, s, key, args, **kwargs)


def _bounded_docker(args,timeout=15,limit=131072):
    """Read-only Docker query: bound BOTH pipes while reading, not afterwards."""
    if sys.platform!='linux':raise Unsafe('Native bounded query requires Linux.')
    import selectors,time
    process=subprocess.Popen(checked_command(args),stdin=subprocess.DEVNULL,
                             stdout=subprocess.PIPE,stderr=subprocess.PIPE)
    output={};total=0;deadline=time.monotonic()+timeout
    try:
        with selectors.DefaultSelector() as selector:
            for stream in (process.stdout,process.stderr):
                os.set_blocking(stream.fileno(),False);selector.register(stream,selectors.EVENT_READ);output[stream]=bytearray()
            while selector.get_map():
                remaining=deadline-time.monotonic()
                if remaining<=0:raise Unsafe('Bounded query timed out.')
                events=selector.select(remaining)
                if not events:raise Unsafe('Bounded query timed out.')
                for key,_ in events:
                    chunk=os.read(key.fileobj.fileno(),min(4096,limit+1-total))
                    if not chunk:selector.unregister(key.fileobj);continue
                    total+=len(chunk)
                    if total>limit:raise Unsafe('Bounded query exceeds output limit.')
                    output[key.fileobj].extend(chunk)
            code=process.wait(timeout=max(0.01,deadline-time.monotonic()))
        return subprocess.CompletedProcess(args,code,bytes(output[process.stdout]),bytes(output[process.stderr]))
    finally:
        try:
            if process.poll() is None:
                process.kill();process.wait(timeout=2)
        finally:
            try:process.stdout.close()
            finally:process.stderr.close()

def _enumerate_session_ids():
    result=[];seen=set()
    for kind in ('image','network','container'):
        args=['docker',kind,'ls','--no-trunc','--format','{{.ID}}',
              '--filter','label=constia.owner=c3c02b',
              '--filter','label=constia.run='+os.environ['GITHUB_RUN_ID']]
        if kind in ('image','container'):args.append('--all')
        response=_bounded_docker(args)
        if response.returncode or response.stderr.strip():raise Unsafe('Resource enumeration failed.')
        for identifier in response.stdout.decode('utf-8').splitlines():
            if kind=='image':
                if not re.fullmatch('sha256:[0-9a-f]{64}',identifier):raise Unsafe('Enumeration image identity differs.')
            else:exact_id(identifier)
            pair=(kind,identifier)
            if pair not in seen:seen.add(pair);result.append(pair)
            if len(result)>5:raise Unsafe('Unexpected additional session resources.')
    return result

def _discover_session(s,known):
    candidates={}
    def observe(kind,obj):
        if not isinstance(obj,dict):raise Unsafe('Resource observation invalid.')
        identifier=obj.get('Id')
        if kind=='image':
            if not isinstance(identifier,str) or not re.fullmatch('sha256:[0-9a-f]{64}',identifier):raise Unsafe('Image ID incomplete.')
        else:exact_id(identifier)
        key=(kind,identifier)
        if key in candidates and candidates[key]!=obj:raise Unsafe('Resource observation changed.')
        candidates[key]=obj
        if len(candidates)>5:raise Unsafe('Session candidates exceed scope.')
    for kind,identifier in _enumerate_session_ids():
        obj=lookup(kind,identifier)
        if obj is None:raise Unsafe('Enumerated resource disappeared.')
        observe(kind,obj)
    for key,(kind,name) in resource_specs(s).items():
        obj=lookup(kind,name)
        if obj is not None:observe(kind,obj)
        identifier=known.get(key)
        if identifier is not None:
            obj=lookup(kind,identifier)
            if obj is not None:observe(kind,obj)
    found={}
    for (kind,identifier),obj in candidates.items():
        options=[key for key,(k,name) in resource_specs(s).items() if k==kind and
                 (name in obj.get('RepoTags',[]) if kind=='image' else obj.get('Name','').lstrip('/')==name)]
        if len(options)!=1:raise Unsafe('Unexpected labelled object or name.')
        key=options[0]
        raw=obj.get('Config',{}).get('Labels',{}) if kind in ('container','image') else obj.get('Labels',{})
        if any((raw or {}).get(k)!=v for k,v in labels(s).items()):raise Unsafe('Foreign session or revision.')
        if key in found and found[key]['Id']!=identifier:raise Unsafe('Ambiguous session role.')
        if known.get(key) not in (None,identifier):raise Unsafe('Known resource ID changed.')
        found[key]=obj;s[key]=identifier;s['planned'][key].update(id=identifier,phase='owned')
    if 'network_id' in found:
        n=found['network_id'];config=n.get('IPAM',{}).get('Config',[])
        if n.get('Internal') is not True or n.get('Driver')!='bridge' or n.get('EnableIPv6') is not False or len(config)!=1:
            raise Unsafe('Session network configuration differs.')
        topology(config[0]['Subnet'],config[0]['Gateway'])
        s.update(subnet=config[0]['Subnet'],gateway=config[0]['Gateway'])
        expected={obj['Id'] for key,obj in found.items() if key in ('app_id','peer_id','denied_id')}
        if not isinstance(n.get('Containers'),dict) or set(n['Containers'])!=expected:
            raise Unsafe('Foreign or missing network member.')
    for key,obj in found.items():
        if key in ('app_id','peer_id','denied_id') and ('app_image_id' not in found or 'network_id' not in found):
            raise Unsafe('Container ownership bindings incomplete.')
        verify_owned(s,key,obj)
    return found

def _execution_witness(c,m,digest):
    if c.witness_sha256 is None:raise Unsafe('Durable execution witness unavailable.')
    record,actual=_validated_diagnostic('isolation-execution.json')
    expected=dict(nonce=m['nonce'],manifest_sha256=digest,producer_state=c.session['producer_state'])
    if actual!=c.witness_sha256 or record['session']!=expected or c.session!=expected:
        raise Unsafe('Execution witness session differs.')
    return actual

class _CleanupReceipt:
    def __init__(self,manifest,digest,fingerprint,witness,roles,certifiable):
        self.manifest=manifest;self.digest=digest;self.fingerprint=fingerprint
        self.witness=witness;self.roles=frozenset(roles);self.certifiable=certifiable
    def __bool__(self):return self.certifiable

def _authorized_cleanup_journal(path,expected=None):
    if path!=state_path():raise Unsafe('Cleanup manifest path differs.')
    m,digest,fp=_read_manifest()
    if expected is not None and m!=expected:raise Unsafe('Cleanup manifest changed.')
    return m

_JOURNAL_ABSENT=object()

def cleanup(expected_journal=None):
    authorize_isolation(os.environ,ISOLATION)
    c=ACTIVE_DIAGNOSTIC.get()
    if c is None:raise Unsafe('Cleanup has no invocation context.')
    if expected_journal is _JOURNAL_ABSENT:
        if not _no_link(state_path()) or state_path().exists():
            c.journal_seen=True;c.journal_state='preexisting'
            raise Unsafe('Manifest appeared after absence.')
        return None
    m,digest,fingerprint=_read_manifest()
    if expected_journal is not None and m!=expected_journal:raise Unsafe('Cleanup manifest changed.')
    witness=None
    try:witness=_execution_witness(c,m,digest)
    except (Exception,KeyboardInterrupt):
        if (c.witness_sha256 is not None or c.controller is None or c.controller.active or c.controller.manifest!=m or
            c.session!=dict(nonce=m['nonce'],manifest_sha256=digest,producer_state=c.controller.terminal_state)):
            raise Unsafe('Recovery has no authorized witness.') from None
    if c.controller is not None and c.controller.active:raise Unsafe('Producer is still active.')
    require_local_engine()
    s=_working_state(m);known={}
    if c.controller is not None:
        known={k:v['id'] for k,v in c.controller.state['planned'].items() if v.get('id') is not None}
    found=_discover_session(s,known)
    def require_session():
        current,sha,fp=_read_manifest()
        if current!=m or sha!=digest or fp!=fingerprint:raise Unsafe('Manifest continuity differs.')
        if witness is not None and _execution_witness(c,m,digest)!=witness:raise Unsafe('Witness changed.')
    for key in ('app_id','peer_id','denied_id','network_id','app_image_id'):
        if key not in found:continue
        require_session()
        obj=lookup(s['planned'][key]['kind'],s[key])
        if obj is None:continue
        identifier=verify_owned(s,key,obj);kind=s['planned'][key]['kind']
        require_session()
        args=['docker',kind,'rm']+(['--force'] if kind=='container' else [])+[identifier]
        diagnostic_call('cleanup_remove',command,args)
        if lookup(kind,identifier) is not None:raise Unsafe('Resource deletion unconfirmed.')
        s['planned'][key]['phase']='removed'
    require_session()
    known.update({k:obj['Id'] for k,obj in found.items()})
    if _discover_session(_working_state(m),known):raise Unsafe('Final absence not established.')
    require_session()
    certifiable=(witness is not None and c.session['producer_state'] in ('quiescent','no_dispatch') and c.operation_failure is None)
    return _CleanupReceipt(m,digest,fingerprint,witness,m['resources'],certifiable)

def _cleanup_confirmed(receipt,m,digest,fingerprint,witness):
    return (isinstance(receipt,_CleanupReceipt) and receipt.certifiable and receipt.manifest==m and
            receipt.digest==digest and receipt.fingerprint==fingerprint and receipt.witness==witness and
            receipt.roles==frozenset(m['resources']))

def _cleanup_diagnostic(c):
    c.cleaning=True
    try:
        p=state_path()
        if not _no_link(p):raise Unsafe('Manifest path rejected.')
        if not p.exists():
            if c.creation_attempted is False and c.journal_seen is False and c.session['producer_state']=='no_dispatch':
                if cleanup(_JOURNAL_ABSENT) is not None:raise Unsafe('Unexpected absent cleanup result.')
                c.journal_state='absent';c.cleanup='not_applicable';c.cleanup_scope='this_invocation_no_dispatch';return
            raise Unsafe('Missing manifest cannot prove absence.')
        c.journal_seen=True
        try:m,digest,fingerprint=_read_manifest()
        except _LegacyJournal:
            # Legacy is discovery-only. It never authorizes recovery deletion.
            require_local_engine();_enumerate_session_ids()
            raise Unsafe('Legacy recovery cannot be certified.') from None
        c.journal_state='valid'
        receipt=diagnostic_call('cleanup',cleanup,m)
        current,sha,fp=_read_manifest()
        witness=_execution_witness(c,current,sha)
        if not _cleanup_confirmed(receipt,current,sha,fp,witness):
            raise Unsafe('Cleanup receipt does not prove complete scope.')
        c.cleanup='verified';c.cleanup_scope='managed_session_resources'
    except (Exception,KeyboardInterrupt):
        if c.journal_state not in ('valid','absent','write_failed'):
            c.journal_state='invalid'
            if c.operation_failure is None:c.fail('journal_check','JOURNAL_REJECTED','INVALID')
        elif c.operation_failure is None:c.fail('cleanup','CLEANUP_UNVERIFIED','UNKNOWN')
        c.cleanup='failed' if c.operation_failure and c.operation_failure['code'] in ('CLEANUP_FAILED','JOURNAL_WRITE_FAILED') else 'uncertain'
    finally:c.cleaning=False

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
    config=network(s)['IPAM']['Config'][0];s.update(subnet=config['Subnet'],gateway=config['Gateway']);_observe_session(s)
    command(['docker','pull',SQL_IMAGE],timeout=300); image_id=inspect('image',SQL_IMAGE)['Id']
    s['sql_image_id']=image_id;_observe_session(s)
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
    s['sql_ip']=m['sql_ip'];_observe_session(s)
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
        create_resource(s,'app_image_id',['docker','build','--file','tests/sandbox/c3c02b/Dockerfile',*la,'--build-arg','CODE_SHA='+s['code_sha'],'--tag',tag,'-'],input=compressed_build_context(archive),timeout=600)
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
    raise Unsafe('Historical publication is disabled; trusted L2 validation is required.')

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

def compressed_build_context(archive):
    """Compress the unchanged tar bytes with a deterministic gzip timestamp."""
    return gzip.compress(archive.read_bytes(),mtime=0)

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
            '--build-arg','CODE_SHA='+s['code_sha'],'--tag',resource_specs(s)['app_image_id'][1],'-'],input=compressed_build_context(archive),timeout=600)

def write_preflight_identity(s,m):
    validate_preflight(m)
    for key in ('app_id','peer_id','denied_id'):
        app(s,key)
        command(['docker','exec','-i',s[key],'sh','-c','umask 077; cat > /run/c3/preflight.tmp; mv /run/c3/preflight.tmp /run/c3/preflight.json'],input=json.dumps(m).encode())

def run_isolation(phase):
    e=os.environ;authorize_isolation(e,phase)
    root=pathlib.Path(__file__).resolve().parents[2]
    if diagnostic_call('git_head', command, ['git','-C',str(root),'rev-parse','HEAD']) != e['CODE_SHA']:
        diagnostic_reject('git_head', 'GIT_REJECTED', 'HEAD_MISMATCH')
    if diagnostic_call('git_status', command, ['git','-C',str(root),'status','--porcelain']):
        diagnostic_reject('git_status', 'GIT_REJECTED', 'DIRTY')
    digest = diagnostic_call('inventory', verify_inventory, root, e)
    diagnostic_call('journal_check', _empty_journal)
    diagnostic_call('engine_context', require_local_engine)
    nonce=secrets.token_hex(16)
    s=dict(schema=2,phase=ISOLATION,repository=REPOSITORY,planned={},run_id=e['GITHUB_RUN_ID'],nonce=nonce,
           code_sha=e['CODE_SHA'],policy_sha256=digest,workflow_sha=e['GITHUB_WORKFLOW_SHA'],actor_id=e['GITHUB_ACTOR_ID'],network_name='c3c02b-'+nonce)
    s['labels']=labels(s);_admit_session(s)
    la=[x for k,v in s['labels'].items() for x in ('--label',k+'='+v)]
    diagnostic_call('build_context', build_preflight_image, root, s, la)
    create_resource(s,'network_id',['docker','network','create','--internal','--driver','bridge',*la,s['network_name']])
    config=diagnostic_call('isolation_setup', network, s)['IPAM']['Config'][0];s.update(subnet=config['Subnet'],gateway=config['Gateway']);_observe_session(s)
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
    addresses={key:diagnostic_call('isolation_setup', app, s, key)['NetworkSettings']['Networks'][s['network_name']]['IPAddress'] for key in ('app_id','peer_id','denied_id')}
    s['peer_ip']=addresses['peer_id'];s['app_ip']=addresses['app_id'];s['denied_ip']=addresses['denied_id']
    s['namespace']=diagnostic_call('isolation_setup', command, ['docker','exec',s['app_id'],'readlink','/proc/self/ns/net']);_observe_session(s)
    import socket,threading,time
    stop=threading.Event();receiver_errors=[]
    with diagnostic_call('isolation_setup', socket.socket) as receiver:
        diagnostic_call('isolation_setup', receiver.bind, ('0.0.0.0',0))
        diagnostic_call('isolation_setup', receiver.listen)
        diagnostic_call('isolation_setup', receiver.settimeout, 0.2)
        m=dict(phase=ISOLATION,repository=REPOSITORY,code_sha=s['code_sha'],policy_sha256=digest,
            workflow_sha=s['workflow_sha'],workflow_ref=WORKFLOW_REF,actor_id=s['actor_id'],run_id=s['run_id'],attempt='1',nonce=nonce,
            network_id=s['network_id'],network_name=s['network_name'],subnet=s['subnet'],gateway=s['gateway'],
            app_id=s['app_id'],app_ip=addresses['app_id'],peer_id=s['peer_id'],peer_ip=addresses['peer_id'],
            denied_id=s['denied_id'],denied_ip=addresses['denied_id'],image_id=s['app_image_id'],sdk_image=SDK_IMAGE,
            namespace=s['namespace'],rules_sha256=None,proof_stage='prepared',gateway_port=receiver.getsockname()[1])
        diagnostic_call('isolation_setup', write_preflight_identity, s, m)
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
        c = ACTIVE_DIAGNOSTIC.get()
        if c is not None: c.current_stage = 'connectivity'
        try:
            for key,role in [('peer_id','peer'),('denied_id','denied')]:
                command(['docker','exec','-d',s[key],'dotnet','/app/harness/Constia.C3C02B.Harness.dll','isolation-'+role])
                deadline=time.monotonic()+15
                while command(['docker','exec',s[key],'sh','-c','test ! -f /run/c3/receiver-ready || cat /run/c3/receiver-ready'])!='ready':
                    if time.monotonic()>=deadline:raise Unsafe('Synthetic receiver not ready.')
                    time.sleep(0.1)
            if command(['docker','exec',s['app_id'],'dotnet','/app/harness/Constia.C3C02B.Harness.dll','isolation-controls'])!='CONTROLS_POSITIVE':raise Unsafe('Positive controls failed.')
            diagnostic_call('isolation_setup', isolate, s, m['peer_ip']);m.update(proof_stage='isolated',rules_sha256=s['rules_sha256']);diagnostic_call('isolation_setup', write_preflight_identity, s, m)
            route_hash=diagnostic_call('isolation_setup', routes_proof, s)
            result=diagnostic_call('connectivity', command, ['docker','exec',s['app_id'],'dotnet','/app/harness/Constia.C3C02B.Harness.dll',ISOLATION],timeout=30)
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
            diagnostic_call('isolation_setup', verify_final_isolation, s, route_hash)
            report=dict(identity=m,phase=ISOLATION,allowed_peer=True,denied_peer=True,gateway_blocked=True,
                        controls_positive=True,simulated_transport=True,routes_sha256=route_hash,sql='not_applicable',workers_started=0)
            diagnostic_call('evidence', validate_isolation_evidence, report);body=json.dumps(report,sort_keys=True);public_text(body)
            diagnostic_call('evidence_write', (public_directory()/'isolation.json').write_text, body, encoding='utf-8')
        except (Exception, KeyboardInterrupt):
            c = ACTIVE_DIAGNOSTIC.get()
            if c is not None and c.first_failure is None:
                c.fail(c.current_stage, *_DIAGNOSTIC_DEFAULTS[c.current_stage])
            raise
        finally:
            diagnostic_call('connectivity', _stop_receiver, stop, thread)

def validate_isolation_journal(s,e):
    validate_session(s)
    allowed=set('schema phase repository planned run_id nonce code_sha policy_sha256 workflow_sha actor_id network_name labels cleanup_complete app_image_id network_id subnet gateway app_id peer_id denied_id peer_ip app_ip denied_ip namespace rules_sha256'.split())
    if set(s)-allowed or s.get('phase')!=ISOLATION:raise Unsafe('Foreign/SQL cleanup journal.')
    for key,env in [('run_id','GITHUB_RUN_ID'),('code_sha','CODE_SHA'),('policy_sha256','CONSTIA_B3_INVENTORY_SHA256'),
                    ('workflow_sha','GITHUB_WORKFLOW_SHA'),('actor_id','GITHUB_ACTOR_ID')]:
        if s.get(key)!=e.get(env):raise Unsafe('Cleanup authorization identity differs.')

def publish_isolation(run_ok,cleanup_ok,operation):
    raise Unsafe('Historical publication is disabled; trusted L2 validation is required.')


# H2 candidates are not a publication certificate. L2 must independently validate them.
import contextvars, stat
DIAGNOSTIC_SCHEMA = 2
MAX_DIAGNOSTIC_BYTES = 8192
DIAGNOSTIC_FILES = {
    'isolation-execution.json': 'execution',
    'isolation-cleanup-primary.json': 'cleanup_primary',
    'isolation-cleanup-final.json': 'cleanup_final',
}
DIAGNOSTIC_CATALOG = frozenset({
    *(('resource_image','RESOURCE_UNVERIFIED',reason) for reason in (
      'BUILD_START_NOT_FOUND','BUILD_START_DENIED','BUILD_START_FAILED','BUILD_EXIT_NONZERO',
      'BUILD_TIMEOUT','IMAGE_CONFIRMATION_FAILED','BUILD_UNEXPECTED_EXCEPTION')),
    ('cli', 'INPUT_REJECTED', 'INVALID'),
    ('authorization', 'AUTH_REJECTED', 'POLICY'),
    ('git_head', 'GIT_REJECTED', 'QUERY_FAILED'),
    ('git_head', 'GIT_REJECTED', 'HEAD_MISMATCH'),
    ('git_status', 'GIT_REJECTED', 'QUERY_FAILED'),
    ('git_status', 'GIT_REJECTED', 'DIRTY'),
    ('inventory', 'INVENTORY_REJECTED', 'INVALID'),
    ('journal_check', 'JOURNAL_REJECTED', 'PREEXISTING'),
    ('journal_check', 'JOURNAL_REJECTED', 'INVALID'),
    ('journal_write', 'JOURNAL_WRITE_FAILED', 'IO_FAILED'),
    ('engine_context', 'ENGINE_REJECTED', 'CONTEXT_OR_QUERY'),
    ('build_context', 'BUILD_CONTEXT_FAILED', 'PREPARATION'),
    *((stage, 'RESOURCE_UNVERIFIED', 'UNCONFIRMED') for stage in
      ('resource_image', 'resource_network', 'resource_app', 'resource_peer', 'resource_denied')),
    ('isolation_setup', 'ISOLATION_UNVERIFIED', 'CHECK_FAILED'),
    ('connectivity', 'CONNECTIVITY_UNVERIFIED', 'CHECK_FAILED'),
    ('cleanup', 'CLEANUP_UNVERIFIED', 'UNKNOWN'),
    ('cleanup_remove', 'CLEANUP_FAILED', 'REMOVE_FAILED'),
    ('evidence', 'EVIDENCE_INVALID', 'INVALID'),
    ('evidence_write', 'EVIDENCE_WRITE_FAILED', 'IO_FAILED'),
    ('unexpected', 'UNEXPECTED_FAILURE', 'UNCLASSIFIED'),
})
_DIAGNOSTIC_DEFAULTS = {stage: (code, reason) for stage, code, reason in DIAGNOSTIC_CATALOG
                        if stage not in ('git_head', 'git_status', 'journal_check')}
_DIAGNOSTIC_DEFAULTS.update(resource_image=('RESOURCE_UNVERIFIED','UNCONFIRMED'),
                            git_head=('GIT_REJECTED', 'QUERY_FAILED'),
                            git_status=('GIT_REJECTED', 'QUERY_FAILED'),
                            journal_check=('JOURNAL_REJECTED', 'INVALID'))
ACTIVE_DIAGNOSTIC = contextvars.ContextVar('isolation_diagnostic', default=None)


class DiagnosticContext:
    def __init__(self):
        self.current_stage = 'unexpected'
        self.first_failure = None
        self.operation_failure = None
        self.last_completed_stage = None
        self.creation_attempted = False
        self.execution = 'not_started'
        self.cleanup = 'uncertain'
        self.cleanup_scope = 'unknown'
        self.journal_state = 'unknown'
        self.journal_seen = False
        self.evidence = 'incomplete'
        self.proof_state = 'absent'
        self.proof_sha256 = None
        self.cleaning = False
        self.controller = None
        self.session = dict(nonce=None,manifest_sha256=None,producer_state='unknown')
        self.witness_sha256 = None

    def fail(self, stage, code, reason):
        if (stage, code, reason) not in DIAGNOSTIC_CATALOG:
            raise Unsafe('Diagnostic outside fixed catalog.')
        failure = dict(stage=stage, code=code, reason=reason)
        if self.cleaning:
            if self.operation_failure is None:
                self.operation_failure = failure
        elif self.first_failure is None:
            self.first_failure = failure
        # All three strings were selected from the literal catalog, never exception text.
        if not _emit_diagnostic(stage, code, reason):
            self.evidence = 'unavailable'


def _emit_diagnostic(stage, code, reason):
    if (stage, code, reason) not in DIAGNOSTIC_CATALOG:
        raise Unsafe('Diagnostic outside fixed catalog.')
    try:
        print('H2_DIAGNOSTIC ' + stage + ' ' + code + ' ' + reason, file=sys.stderr)
        return True
    except Exception:
        return False


def diagnostic_call(stage, function, *args, **kwargs):
    c = ACTIVE_DIAGNOSTIC.get()
    if c is None:
        return function(*args, **kwargs)
    c.current_stage = stage
    try:
        result = function(*args, **kwargs)
    except (Exception, KeyboardInterrupt):
        code, reason = _DIAGNOSTIC_DEFAULTS[stage]
        c.fail(stage, code, reason)
        if stage == 'journal_write':
            c.journal_state = 'write_failed'
        raise
    c.current_stage = stage
    c.last_completed_stage = stage
    return result


def diagnostic_reject(stage, code, reason):
    c = ACTIVE_DIAGNOSTIC.get()
    if c is not None:
        c.fail(stage, code, reason)
    raise Unsafe('Reviewed condition rejected.')


def _diagnostic_identity():
    e = os.environ
    identity = dict(repository=REPOSITORY, run_id=e['GITHUB_RUN_ID'], attempt='1',
                    code_sha=e['CODE_SHA'], workflow_sha=e['GITHUB_WORKFLOW_SHA'],
                    policy_sha256=e['CONSTIA_B3_INVENTORY_SHA256'])
    if (not re.fullmatch('[1-9][0-9]*', identity['run_id']) or
        any(not re.fullmatch('[0-9a-f]{40}', identity[k]) for k in ('code_sha', 'workflow_sha')) or
        not re.fullmatch('[0-9a-f]{64}', identity['policy_sha256'])):
        raise Unsafe('Diagnostic identity invalid.')
    return identity


def _no_link(path):
    return not path.is_symlink() and not path.is_junction()


def diagnostic_directory():
    raw = pathlib.Path(os.environ['RUNNER_TEMP'])
    if not raw.is_absolute() or any(not _no_link(p) for p in (raw, *raw.parents)):
        raise Unsafe('Diagnostic root rejected.')
    root = raw.resolve(strict=True)
    out = root / 'c3c02b-diagnostics'
    if not _no_link(out) or out.resolve().parent != root:
        raise Unsafe('Diagnostic directory rejected.')
    out.mkdir(exist_ok=True)
    if not out.is_dir() or any(p.name not in DIAGNOSTIC_FILES or not _no_link(p) or not p.is_file()
                               for p in out.iterdir()):
        raise Unsafe('Unexpected diagnostic path.')
    return out


def _bounded_bytes(path, maximum):
    if not _no_link(path):
        raise Unsafe('Linked evidence rejected.')
    fd = os.open(path, os.O_RDONLY | getattr(os, 'O_BINARY', 0) | getattr(os, 'O_NOFOLLOW', 0))
    try:
        before = os.fstat(fd)
        if not stat.S_ISREG(before.st_mode) or before.st_nlink != 1 or before.st_size > maximum:
            raise Unsafe('Evidence bounds rejected.')
        data = bytearray()
        while len(data) <= maximum:
            chunk = os.read(fd, min(4096, maximum + 1 - len(data)))
            if not chunk:
                break
            data.extend(chunk)
        after = os.fstat(fd)
        current = path.lstat()
        if (len(data) > maximum or not _no_link(path) or
            (before.st_dev, before.st_ino, before.st_size, before.st_mtime_ns) !=
            (after.st_dev, after.st_ino, after.st_size, after.st_mtime_ns) or
            (after.st_dev, after.st_ino) != (current.st_dev, current.st_ino)):
            raise Unsafe('Evidence changed during read.')
        return bytes(data)
    finally:
        os.close(fd)


def _strict_json(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            if key in result:
                raise Unsafe('Duplicate JSON key.')
            result[key] = value
        return result
    def constant(value):
        raise Unsafe('Nonfinite JSON rejected.')
    try:
        return json.loads(raw.decode('utf-8'), object_pairs_hook=pairs, parse_constant=constant)
    except (ValueError, UnicodeError, TypeError):
        raise Unsafe('JSON rejected.') from None


def make_diagnostic(kind, c, execution_sha256=None, primary_sha256=None):
    if c.controller is not None and c.controller.active:raise Unsafe('Active producer cannot emit a witness.')
    return dict(schema=DIAGNOSTIC_SCHEMA, kind=kind, phase=ISOLATION,
                session=dict(c.session),
                identity=_diagnostic_identity(), last_completed_stage=c.last_completed_stage,
                first_failure=c.first_failure, operation_failure=c.operation_failure,
                execution=c.execution, creation_attempted=c.creation_attempted,
                journal_state=c.journal_state, journal_seen=c.journal_seen, cleanup=c.cleanup, cleanup_scope=c.cleanup_scope,
                evidence=c.evidence, proof_state=c.proof_state, proof_sha256=c.proof_sha256,
                execution_sha256=execution_sha256, primary_sha256=primary_sha256)


def validate_diagnostic(record):
    fields = set('schema kind phase identity last_completed_stage first_failure operation_failure '
                 'execution creation_attempted journal_state journal_seen cleanup cleanup_scope evidence proof_state '
                 'proof_sha256 execution_sha256 primary_sha256 session'.split())
    if not isinstance(record, dict) or set(record) != fields:
        raise Unsafe('Diagnostic fields differ.')
    if (type(record['schema']) is not int or record['schema'] != DIAGNOSTIC_SCHEMA or
        record['kind'] not in DIAGNOSTIC_FILES.values() or record['phase'] != ISOLATION or
        record['identity'] != _diagnostic_identity()):
        raise Unsafe('Diagnostic identity or version differs.')
    session=record['session']
    if not isinstance(session,dict) or set(session)!={'nonce','manifest_sha256','producer_state'}:
        raise Unsafe('Diagnostic session fields differ.')
    if session['producer_state'] not in PRODUCER_STATES:
        raise Unsafe('Diagnostic producer state differs.')
    if (session['nonce'] is None)!=(session['manifest_sha256'] is None):
        raise Unsafe('Incomplete session binding.')
    if session['nonce'] is not None and (
        not isinstance(session['nonce'],str) or not re.fullmatch('[0-9a-f]{32}',session['nonce']) or
        not isinstance(session['manifest_sha256'],str) or not re.fullmatch('[0-9a-f]{64}',session['manifest_sha256'])):
        raise Unsafe('Diagnostic session binding differs.')
    if session['producer_state'] in ('quiescent','unresolved') and session['nonce'] is None:
        raise Unsafe('Producer has no manifest.')
    if session['producer_state']=='no_dispatch' and record['creation_attempted'] is True:
        raise Unsafe('Contradictory producer state.')
    if record['cleanup']=='verified' and (
        session['nonce'] is None or session['producer_state'] not in ('quiescent','no_dispatch')):
        raise Unsafe('Cleanup has no closed producer.')
    for key in ('first_failure', 'operation_failure'):
        value = record[key]
        if value is not None and (not isinstance(value, dict) or set(value) != {'stage', 'code', 'reason'} or
                                  any(not isinstance(v, str) for v in value.values()) or
                                  (value['stage'], value['code'], value['reason']) not in DIAGNOSTIC_CATALOG):
            raise Unsafe('Diagnostic failure outside catalog.')
    if record['last_completed_stage'] is not None and record['last_completed_stage'] not in _DIAGNOSTIC_DEFAULTS:
        raise Unsafe('Unknown diagnostic stage.')
    if record['execution'] not in (None, 'not_started', 'failed_before_creation',
                                    'failed_after_creation_attempt', 'completed'):
        raise Unsafe('Invalid execution result.')
    if record['creation_attempted'] is not None and type(record['creation_attempted']) is not bool:
        raise Unsafe('Invalid creation result.')
    if (record['execution'] in ('completed', 'failed_after_creation_attempt') and record['creation_attempted'] is not True or
        record['execution'] in ('not_started', 'failed_before_creation') and record['creation_attempted'] is not False):
        raise Unsafe('Contradictory creation result.')
    if (record['cleanup'] not in ('not_applicable', 'verified', 'uncertain', 'failed') or
        record['cleanup_scope'] not in ('unknown', 'this_invocation_no_dispatch', 'managed_session_resources') or
        record['journal_state'] not in ('unknown', 'absent', 'preexisting', 'valid', 'invalid', 'write_failed') or
        record['evidence'] not in ('incomplete', 'unavailable') or
        record['proof_state'] not in ('absent', 'candidate_valid', 'invalid', 'unavailable')):
        raise Unsafe('Invalid diagnostic state; L2 validation cannot be claimed.')
    if record['journal_seen'] is not None and type(record['journal_seen']) is not bool:
        raise Unsafe('Invalid journal observation.')
    if record['cleanup'] == 'not_applicable' and not (
        record['creation_attempted'] is False and record['execution'] in ('not_started', 'failed_before_creation') and
        record['journal_state'] == 'absent' and record['journal_seen'] is False and
        record['cleanup_scope'] == 'this_invocation_no_dispatch'):
        raise Unsafe('Absence not demonstrated.')
    if record['cleanup'] == 'verified' and not (
        record['journal_state'] == 'valid' and record['journal_seen'] is True and
        record['cleanup_scope'] == 'managed_session_resources'):
        raise Unsafe('Cleanup scope not demonstrated.')
    for key in ('proof_sha256', 'execution_sha256', 'primary_sha256'):
        value = record[key]
        if value is not None and (not isinstance(value, str) or not re.fullmatch('[0-9a-f]{64}', value)):
            raise Unsafe('Invalid diagnostic hash.')
    if ((record['proof_state'] == 'candidate_valid') != (record['proof_sha256'] is not None) or
        record['kind'] == 'execution' and (record['execution_sha256'] is not None or record['primary_sha256'] is not None) or
        record['kind'] == 'cleanup_primary' and record['primary_sha256'] is not None):
        raise Unsafe('Contradictory diagnostic references.')



def _file_identity(row):
    return (row.st_dev,row.st_ino,row.st_size,row.st_mtime_ns,
            row.st_ctime_ns if sys.platform == 'linux' else None)

class _LinuxPublicationDirectory:
    """Pinned directories and an anonymous inode; never unlink a pathname."""
    def __init__(self, path):
        self.fds=[];self.edges=[]
        try:
            if sys.platform != 'linux' or not hasattr(os,'O_TMPFILE'):
                raise Unsafe('Native Linux publication unavailable.')
            root=pathlib.Path(os.environ['RUNNER_TEMP'])
            self.path=pathlib.Path(path)
            if not root.is_absolute() or '..' in root.parts or self.path not in (root,root/'c3c02b-diagnostics'):
                raise Unsafe('Publication directory rejected.')
            flags=os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW|os.O_CLOEXEC
            current=os.open('/',flags);self.fds.append(current)
            for index,part in enumerate(self.path.parts[1:]):
                try:child=os.open(part,flags,dir_fd=current)
                except FileNotFoundError:
                    if self.path != root/'c3c02b-diagnostics' or index != len(self.path.parts)-2:raise
                    os.mkdir(part,0o700,dir_fd=current)
                    child=os.open(part,flags,dir_fd=current)
                self.edges.append((current,part,child));self.fds.append(child);current=child
            self.fd=current
            row=os.fstat(current)
            if row.st_uid != os.geteuid() or row.st_mode & 0o022:
                raise Unsafe('Writable or foreign publication directory.')
            self.check()
        except BaseException:
            self.close();raise
    def __enter__(self):return self
    def __exit__(self,*unused):self.close()
    def close(self):
        pending,self.fds=self.fds,[]
        failure=None
        for fd in reversed(pending):
            try:os.close(fd)
            except OSError as ex:
                if failure is None:failure=ex
        if failure is not None:raise failure
    def check(self):
        for parent,name,fd in self.edges:
            entry=os.stat(name,dir_fd=parent,follow_symlinks=False);opened=os.fstat(fd)
            if not stat.S_ISDIR(entry.st_mode) or (entry.st_dev,entry.st_ino)!=(opened.st_dev,opened.st_ino):
                raise Unsafe('Publication directory changed.')
        if self.path.name=='c3c02b-diagnostics' and any(n not in DIAGNOSTIC_FILES for n in os.listdir(self.fd)):
            raise Unsafe('Unexpected diagnostic entry.')
    def anonymous(self):
        return os.open('.',os.O_TMPFILE|os.O_RDWR|os.O_CLOEXEC,0o600,dir_fd=self.fd)
    def link(self,fd,name):
        os.link('/proc/self/fd/'+str(fd),name,dst_dir_fd=self.fd,follow_symlinks=True)
    def sync(self):os.fsync(self.fd)
    def stat_fd(self,fd):return os.fstat(fd)
    def stat_name(self,name):return os.stat(name,dir_fd=self.fd,follow_symlinks=False)

def _publication_directory(path):
    return _LinuxPublicationDirectory(path)

def _read_publication_fd(directory,fd,maximum):
    before=directory.stat_fd(fd)
    if not stat.S_ISREG(before.st_mode) or before.st_size>maximum:
        raise Unsafe('Publication descriptor rejected.')
    os.lseek(fd,0,os.SEEK_SET);data=bytearray()
    while len(data)<=maximum:
        chunk=os.read(fd,min(4096,maximum+1-len(data)))
        if not chunk:break
        data.extend(chunk)
    after=directory.stat_fd(fd)
    if len(data)>maximum or _file_identity(before)!=_file_identity(after):
        raise Unsafe('Publication descriptor changed.')
    return bytes(data)

def _publish_immutable_record_fd(path,record,validator):
    authorize_isolation(os.environ,ISOLATION)
    path=pathlib.Path(path);root=pathlib.Path(os.environ['RUNNER_TEMP'])
    if not root.is_absolute() or any(not _no_link(p) for p in (root,*root.parents)):
        raise Unsafe('Publication root rejected.')
    root=root.resolve(strict=True)
    path=path.parent.resolve(strict=True)/path.name
    if path==root/state_path().name:maximum=131072
    elif path.parent==root/'c3c02b-diagnostics' and path.name in DIAGNOSTIC_FILES:maximum=MAX_DIAGNOSTIC_BYTES
    else:raise Unsafe('Publication target rejected.')
    validator(record)
    data=json.dumps(record,sort_keys=True,separators=(',',':'),allow_nan=False).encode('utf-8')
    if len(data)>maximum:raise Unsafe('Publication exceeds bounds.')
    with _publication_directory(path.parent) as directory:
        directory.check();fd=directory.anonymous()
        try:
            initial=directory.stat_fd(fd)
            if not stat.S_ISREG(initial.st_mode) or initial.st_nlink!=0:
                raise Unsafe('Anonymous inode required.')
            offset=0
            while offset<len(data):
                count=os.write(fd,data[offset:])
                if count<=0:raise OSError('Incomplete publication write.')
                offset+=count
            os.fsync(fd)
            raw=_read_publication_fd(directory,fd,maximum)
            if raw!=data:raise Unsafe('Written bytes differ.')
            validator(_strict_json(raw))
            directory.link(fd,path.name)
            directory.sync();directory.check()
            published=_read_publication_fd(directory,fd,maximum)
            if published!=data:raise Unsafe('Published bytes differ.')
            validator(_strict_json(published))
            row=directory.stat_fd(fd);entry=directory.stat_name(path.name)
            if (not stat.S_ISREG(entry.st_mode) or row.st_nlink!=1 or entry.st_nlink!=1 or
                _file_identity(row)!=_file_identity(entry) or
                (row.st_dev,row.st_ino)!=(initial.st_dev,initial.st_ino)):
                raise Unsafe('Published inode association differs.')
            directory.check()
            if _file_identity(directory.stat_fd(fd))!=_file_identity(row):
                raise Unsafe('Publication changed before confirmation.')
            return hashlib.sha256(published).hexdigest(),_file_identity(row)
        finally:
            os.close(fd)

def write_diagnostic(name,record):
    if name not in DIAGNOSTIC_FILES or record.get('kind')!=DIAGNOSTIC_FILES[name]:
        raise Unsafe('Diagnostic filename rejected.')
    return _publish_immutable_record_fd(diagnostic_directory()/name,record,validate_diagnostic)[0]

def _read_diagnostic(name):
    if name not in DIAGNOSTIC_FILES:
        raise Unsafe('Diagnostic filename rejected.')
    raw = _bounded_bytes(diagnostic_directory() / name, MAX_DIAGNOSTIC_BYTES)
    record = _strict_json(raw)
    validate_diagnostic(record)
    if record['kind'] != DIAGNOSTIC_FILES[name]:
        raise Unsafe('Diagnostic role differs.')
    return record, hashlib.sha256(raw).hexdigest()


def _validated_diagnostic(name, snapshots=None):
    snapshots = {} if snapshots is None else snapshots
    if name in snapshots:
        return snapshots[name]
    record, digest = _read_diagnostic(name)
    if record['execution_sha256'] is not None:
        try:
            original, expected = _validated_diagnostic('isolation-execution.json', snapshots)
        except FileNotFoundError:
            raise Unsafe('Referenced execution absent.') from None
        if expected != record['execution_sha256'] or any(record[k] != original[k] for k in
            ('execution', 'creation_attempted', 'first_failure', 'session')):
            raise Unsafe('Diagnostic execution binding differs.')
        if original['journal_seen'] is True and record['journal_seen'] is not True:
            raise Unsafe('Journal observation cannot be forgotten.')
        if original['evidence'] == 'unavailable' and record['evidence'] != 'unavailable':
            raise Unsafe('Unavailable evidence cannot be silently improved.')
        if record['proof_state'] == 'candidate_valid' and (
            original['proof_state'] != 'candidate_valid' or record['proof_sha256'] != original['proof_sha256']):
            raise Unsafe('Diagnostic proof binding differs.')
    if record['primary_sha256'] is not None:
        try:
            primary, expected = _validated_diagnostic('isolation-cleanup-primary.json', snapshots)
        except FileNotFoundError:
            raise Unsafe('Referenced primary absent.') from None
        if (expected != record['primary_sha256'] or primary['execution_sha256'] != record['execution_sha256'] or primary['session'] != record['session'] or
            primary['journal_seen'] is True and record['journal_seen'] is not True):
            raise Unsafe('Diagnostic cleanup binding differs.')
        if record['execution_sha256'] is None and any(record[k] != primary[k] for k in
            ('execution', 'creation_attempted', 'first_failure', 'session')):
            raise Unsafe('Recovered primary snapshot differs.')
        if primary['evidence'] == 'unavailable' and record['evidence'] != 'unavailable':
            raise Unsafe('Unavailable primary evidence cannot be silently improved.')
        if record['proof_state'] == 'candidate_valid' and (
            primary['proof_state'] != 'candidate_valid' or record['proof_sha256'] != primary['proof_sha256']):
            raise Unsafe('Primary proof binding differs.')
    snapshots[name] = record, digest
    return record, digest


def read_diagnostic(name):
    return _validated_diagnostic(name)[0]


def _candidate_proof(c):
    c.proof_state = 'absent'
    c.proof_sha256 = None
    path = public_directory() / 'isolation.json'
    try:
        raw = _bounded_bytes(path, 131072)
        report = _strict_json(raw)
        validate_isolation_evidence(report)
        m = report['identity']
        e = os.environ
        if any(m[key] != e[env] for key, env in
               [('code_sha', 'CODE_SHA'), ('workflow_sha', 'GITHUB_WORKFLOW_SHA'),
                ('policy_sha256', 'CONSTIA_B3_INVENTORY_SHA256'), ('run_id', 'GITHUB_RUN_ID'),
                ('actor_id', 'GITHUB_ACTOR_ID')]):
            raise Unsafe('Isolation proof owner differs.')
        if c.session['nonce'] is None or m['nonce']!=c.session['nonce']:
            raise Unsafe('Proof session differs.')
        c.proof_state = 'candidate_valid'
        c.proof_sha256 = hashlib.sha256(raw).hexdigest()
    except FileNotFoundError:
        c.proof_state = 'absent'
    except (Exception, KeyboardInterrupt):
        c.proof_state = 'invalid'
        c.fail('evidence', 'EVIDENCE_INVALID', 'INVALID')



def _try_write_diagnostic(name, record, c):
    try:
        return diagnostic_call('evidence_write', write_diagnostic, name, record)
    except (Exception, KeyboardInterrupt):
        c.evidence = 'unavailable'
        return None


class _DiagnosticParser(argparse.ArgumentParser):
    def error(self, message):
        _emit_diagnostic('cli', 'INPUT_REJECTED', 'INVALID')
        raise SystemExit(2)


def diagnosed_main():
    parser = _DiagnosticParser(add_help=False)
    parser.add_argument('operation', choices=('run', 'cleanup'))
    parser.add_argument('--phase', required=True, action='append')
    try:
        args = parser.parse_args()
    except SystemExit as ex:
        return ex.code
    phase = args.phase[0] if len(args.phase) == 1 else None
    c = DiagnosticContext()
    active = ACTIVE_DIAGNOSTIC.set(c)
    try:
        try:
            diagnostic_call('authorization', authorize_isolation, os.environ, phase)
        except (Exception, KeyboardInterrupt):
            return 1  # Untrusted context cannot choose an output directory.
        if args.operation == 'run':
            execution_hash = primary_hash = None
            try:
                try:
                    if (diagnostic_call('evidence', diagnostic_directory) / 'isolation-execution.json').exists():
                        diagnostic_reject('evidence', 'EVIDENCE_INVALID', 'INVALID')
                    run_isolation(phase)
                    c.execution = 'completed'
                except (Exception, KeyboardInterrupt):
                    if c.first_failure is None:
                        c.fail(c.current_stage, *_DIAGNOSTIC_DEFAULTS[c.current_stage])
                    c.execution = 'failed_after_creation_attempt' if c.creation_attempted else 'failed_before_creation'
                _freeze_producer(c)
                try:
                    _candidate_proof(c)
                except (Exception, KeyboardInterrupt):
                    c.evidence = 'unavailable'
                    c.fail('evidence', 'EVIDENCE_INVALID', 'INVALID')
                try:
                    original = make_diagnostic('execution', c)
                    execution_hash = _try_write_diagnostic('isolation-execution.json', original, c)
                    c.witness_sha256 = execution_hash
                except (Exception, KeyboardInterrupt):
                    c.evidence = 'unavailable'
                    c.fail('evidence_write', 'EVIDENCE_WRITE_FAILED', 'IO_FAILED')
            finally:
                _cleanup_diagnostic(c)
            try:
                primary = make_diagnostic('cleanup_primary', c, execution_hash)
                primary_hash = _try_write_diagnostic('isolation-cleanup-primary.json', primary, c)
            except (Exception, KeyboardInterrupt):
                c.evidence = 'unavailable'
                c.fail('evidence_write', 'EVIDENCE_WRITE_FAILED', 'IO_FAILED')
            return 0 if (c.execution == 'completed' and c.first_failure is None and
                         c.operation_failure is None and c.cleanup == 'verified' and
                         c.proof_state == 'candidate_valid' and execution_hash and primary_hash) else 1
        execution_hash = primary_hash = None
        c.execution = c.creation_attempted = c.journal_seen = None
        snapshots = {}
        try:
            prior, execution_hash = _validated_diagnostic('isolation-execution.json', snapshots)
            c.witness_sha256 = execution_hash
            for key in ('execution', 'creation_attempted', 'first_failure', 'proof_state', 'proof_sha256', 'journal_seen', 'evidence', 'session'):
                setattr(c, key, prior[key])
        except FileNotFoundError:
            pass
        except (Exception, KeyboardInterrupt):
            c.cleaning = True
            c.fail('evidence', 'EVIDENCE_INVALID', 'INVALID')
            c.cleaning = False
        # A primary corruption must not erase an independently valid execution snapshot.
        try:
            primary, primary_hash = _validated_diagnostic('isolation-cleanup-primary.json', snapshots)
            if execution_hash is None and primary['execution_sha256'] is None:
                for key in ('execution', 'creation_attempted', 'first_failure', 'proof_state', 'proof_sha256', 'journal_seen', 'session'):
                    setattr(c, key, primary[key])
            if primary['evidence'] == 'unavailable':
                c.evidence = 'unavailable'
            if primary['journal_seen'] is True:
                c.journal_seen = True
        except FileNotFoundError:
            pass
        except (Exception, KeyboardInterrupt):
            c.cleaning = True
            c.fail('evidence', 'EVIDENCE_INVALID', 'INVALID')
            c.cleaning = False
            c.journal_seen = True  # An unreadable prior cleanup cannot prove non-adoption.
        if (execution_hash or primary_hash) and c.proof_state == 'candidate_valid':
            expected_proof = c.proof_sha256
            c.cleaning = True
            try:
                _candidate_proof(c)
                if c.proof_state != 'candidate_valid' or c.proof_sha256 != expected_proof:
                    c.proof_state = 'invalid'
                    c.proof_sha256 = None
                    c.fail('evidence', 'EVIDENCE_INVALID', 'INVALID')
            except (Exception, KeyboardInterrupt):
                c.proof_state = 'unavailable'
                c.proof_sha256 = None
                c.fail('evidence', 'EVIDENCE_INVALID', 'INVALID')
            finally:
                c.cleaning = False
        _cleanup_diagnostic(c)
        final = make_diagnostic('cleanup_final', c, execution_hash, primary_hash)
        final_hash = _try_write_diagnostic('isolation-cleanup-final.json', final, c)
        return 0 if final_hash and c.operation_failure is None and c.cleanup in ('verified', 'not_applicable') else 1
    finally:
        ACTIVE_DIAGNOSTIC.reset(active)


def _stop_receiver(stop, thread):
    stop.set();thread.join(timeout=3)
    if thread.is_alive():raise Unsafe('Synthetic receiver did not stop.')


def _empty_journal():
    p = state_path()
    c = ACTIVE_DIAGNOSTIC.get()
    if not _no_link(p):
        if c is not None: c.journal_seen = True
        diagnostic_reject('journal_check', 'JOURNAL_REJECTED', 'INVALID')
    if p.exists():
        if c is not None:
            c.journal_state = 'preexisting'
            c.journal_seen = True
        diagnostic_reject('journal_check', 'JOURNAL_REJECTED', 'PREEXISTING')
    if c is not None: c.journal_state = 'absent'

def main():
    return diagnosed_main()

if __name__=='__main__':sys.exit(main())
