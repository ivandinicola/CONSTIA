"""Pure tests: no Docker, SQL, subprocesses or network."""
import copy
import importlib.util
import pathlib
import unittest

SPEC = importlib.util.spec_from_file_location('runner', pathlib.Path(__file__).with_name('c3c02b_runner.py'))
r = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(r)


class SafetyTests(unittest.TestCase):
    def identity(self):
        return dict(repository='ivandinicola/CONSTIA', code_sha='a'*40, workflow_sha='b'*40,
                    run_id='123', attempt='1', nonce='c'*32, container_id='d'*64,
                    container_name='c3c02b-'+'c'*32, image=r.SQL_IMAGE,
                    database='C3C02B_'+'c'*32, sql_ip='172.29.0.2', hostname='d'*12,
                    network_id='e'*64, network_name='c3c02b-'+'c'*32,
                    sdk_image=r.SDK_IMAGE, runtime='c3_runtime', observer='c3_observer', migrator='c3_migrator',
                    subnet='172.29.0.0/24',gateway='172.29.0.1',workflow_ref=r.WORKFLOW_REF)

    def test_valid_identity(self):
        r.validate_identity(self.identity())

    def test_wrong_destination_rejected(self):
        for key, value in [('repository','else/other'),('database','master'),('sql_ip','8.8.8.8'),
                           ('container_id','short'),('image','mssql:latest'),('nonce','../x'),
                           ('runtime','sa'),('hostname','unexpected')]:
            with self.subTest(key=key), self.assertRaises(r.Unsafe):
                m=self.identity(); m[key]=value; r.validate_identity(m)

    def test_manifest_extra_fields_rejected(self):
        m=self.identity(); m['connection_string']='private'
        with self.assertRaises(r.Unsafe): r.validate_identity(m)

    def test_no_cleanup_by_name_or_prefix(self):
        for resource in ['c3c02b-*','all','short','../x']:
            with self.assertRaises(r.Unsafe): r.exact_id(resource)

    def test_environment_cannot_authorize_itself(self):
        env=dict(GITHUB_ACTIONS='true', RUNNER_ENVIRONMENT='github-hosted', RUNNER_OS='Linux',
                 GITHUB_REPOSITORY='ivandinicola/CONSTIA', GITHUB_EVENT_NAME='workflow_dispatch',
                 GITHUB_SHA='b'*40, CODE_SHA='a'*40, GITHUB_WORKFLOW_SHA='b'*40, GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1',
                 GITHUB_REF='refs/heads/main',GITHUB_WORKFLOW_REF=r.WORKFLOW_REF)
        with self.assertRaises(r.Unsafe): r.require_ci(env, '')
        # The approved SHA is a reviewed launcher constant, not an arbitrary CLI input.
        r.require_ci(env, 'a'*40)
        for key,value in [('GITHUB_RUN_ATTEMPT','2'),('RUNNER_ENVIRONMENT','self-hosted'),
                          ('GITHUB_EVENT_NAME','pull_request'),('HTTPS_PROXY','http://proxy'),
                          ('DOCKER_HOST','tcp://elsewhere'),('RESEND_API_KEY','anything')]:
            changed=copy.copy(env); changed[key]=value
            with self.subTest(key=key), self.assertRaises(r.Unsafe): r.require_ci(changed, 'a'*40)

    def test_command_arguments_are_lists(self):
        with self.assertRaises(r.Unsafe): r.checked_command('docker rm everything')

    def test_privacy_rejects_unapproved_artifact_content(self):
        for value in ['Bearer example','Password=synthetic','re_12345678901234567890','a@example.invalid']:
            with self.subTest(value=value), self.assertRaises(r.Unsafe): r.public_text(value)
        self.assertEqual(r.SQL_IMAGE,r.public_text(r.SQL_IMAGE))
        self.assertEqual(r.SDK_IMAGE,r.public_text(r.SDK_IMAGE))

    def test_network_snapshot_rejects_extra_attachment(self):
        m=self.identity()
        state={'Id':m['container_id'],'Image':'image-id','Config':{'Image':r.SQL_IMAGE,
               'Hostname':m['hostname'],'Labels':r.labels(m)},'HostConfig':{'Privileged':False,'NetworkMode':m['network_name']},
               'NetworkSettings':{'Networks':{m['network_name']:{'IPAddress':m['sql_ip'],'NetworkID':m['network_id']}}}}
        r.validate_container(m,state,'image-id')
        state['NetworkSettings']['Networks']['bridge']={}
        with self.assertRaises(r.Unsafe): r.validate_container(m,state,'image-id')


    def test_atomic_manifest_retains_previous_owner_on_failure(self):

        def check(case):
            p=r.state_path();p.write_bytes(b'FOREIGN')
            with case.assertRaises(FileExistsError):r._admit_session(case.s)
            case.assertEqual(b'FOREIGN',p.read_bytes());case.assertFalse(case.c.creation_attempted)
        _with_protocol_fixture(check)

    def test_cleanup_rejects_foreign_container_before_mutation(self):

        def check(case):
            case.populate();case.witness()
            case.objects[('container','1'*64)]['Config']['Labels']['constia.owner']='foreign'
            case.assertNotEqual('verified',case.clean());case.assertEqual([],case.removals)
            case.assertIn(('container','1'*64),case.objects)
        _with_protocol_fixture(check)

    def test_external_network_fails_closed(self):
        from unittest.mock import patch
        with patch.object(r,'inspect',return_value={'Id':'e'*64,'Internal':False,'Driver':'bridge'}):
            with self.assertRaises(r.Unsafe):r.network({'network_id':'e'*64,'labels':{}})

    def test_failed_trx_is_retained_but_never_successful(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            source=pathlib.Path(d)/'raw.trx';target=pathlib.Path(d)/'safe.trx'
            source.write_text('''<TestRun name="runner@example.invalid" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
              <TestDefinitions><UnitTest id="one"><TestMethod name="Case"/></UnitTest></TestDefinitions>
              <Results><UnitTestResult testId="one" outcome="Failed"><Output><ErrorInfo>
              <Message>Bearer private@example.invalid</Message></ErrorInfo></Output></UnitTestResult></Results>
              <ResultSummary><Counters total="1" executed="1" passed="0" failed="1"/></ResultSummary></TestRun>''')
            self.assertFalse(r.sanitized_trx(source,target,['Case']))
            self.assertNotIn('Bearer',target.read_text())
            self.assertNotIn('@example.invalid',target.read_text())
            with self.assertRaises(r.Unsafe):r.sanitized_trx(source,target,['OtherCase'])


    def ci_environment(self):
        return dict(GITHUB_ACTIONS='true',RUNNER_ENVIRONMENT='github-hosted',RUNNER_OS='Linux',
                    GITHUB_REPOSITORY='ivandinicola/CONSTIA',GITHUB_EVENT_NAME='workflow_dispatch',
                    GITHUB_SHA='b'*40,GITHUB_WORKFLOW_SHA='b'*40,CODE_SHA='a'*40,
                    GITHUB_RUN_ID='123',GITHUB_RUN_ATTEMPT='1',GITHUB_REF='refs/heads/main',
                    GITHUB_WORKFLOW_REF='ivandinicola/CONSTIA/.github/workflows/c3c02b-operational.yml@refs/heads/main')

    def test_h01_launcher_ref_and_revision_must_match(self):
        for key,value in [('GITHUB_REF','refs/heads/unreviewed'),
                          ('GITHUB_WORKFLOW_REF','ivandinicola/CONSTIA/.github/workflows/other.yml@refs/heads/main'),
                          ('GITHUB_WORKFLOW_SHA','c'*40)]:
            with self.subTest(key=key),self.assertRaises(r.Unsafe):
                env=self.ci_environment();env[key]=value;r.require_ci(env,'a'*40)

    def test_h04_private_topologies_are_not_limited_to_172(self):
        for cidr,gateway,host in [('10.8.0.0/24','10.8.0.1','10.8.0.2'),
                                 ('192.168.60.0/24','192.168.60.1','192.168.60.2')]:
            with self.subTest(cidr=cidr):
                m=self.identity();m['sql_ip']=host
                if 'subnet' in r.FIELDS:m.update(subnet=cidr,gateway=gateway)
                r.validate_identity(m)

    def test_h03_duplicate_rules_are_rejected(self):
        v4='-P OUTPUT DROP\n-A OUTPUT -d 127.0.0.1/32 -o lo -j ACCEPT\n-A OUTPUT -d 172.29.0.2/32 -p tcp -m tcp --dport 1433 -j ACCEPT'
        v6='-P OUTPUT DROP\n-A OUTPUT -d ::1/128 -o lo -j ACCEPT'
        # Duplication does not itself permit egress; it is rejected as exact-proof drift.
        with self.assertRaises(r.Unsafe):r.validate_firewall('172.29.0.2',v4+'\n'+v4.splitlines()[-1],v6)

    def test_h05_query_report_cannot_be_just_an_id_list(self):
        self.assertTrue(hasattr(r,'validate_queries'),'No semantic evidence validator exists yet')
        with self.assertRaises(r.Unsafe):r.validate_queries([{'id':'Q00.1'}],['Q00.1'])

    def test_h02_uncertain_resources_need_planned_identity(self):

        def check(case):
            ctrl=r._admit_session(case.s)
            m,_,_=r._read_manifest();case.assertEqual(5,len(m['resources']))
            with case.assertRaises(r.Unsafe):r.plan_resource(case.s,'volume_id')
            case.assertEqual(set(),ctrl.dispatched)
            r.plan_resource(case.s,'network_id')
            case.assertIn('network_id',ctrl.pending)
            case.assertEqual(5,len(r._read_manifest()[0]['resources']))
            case.assertEqual([],case.removals)
        _with_protocol_fixture(check)


    def test_h01_gate_cannot_be_disabled_by_python_optimization(self):
        import os,re
        from unittest.mock import patch
        text=pathlib.Path(__file__).parents[1].joinpath('workflows/c3c02b-operational.yml').read_text()
        body=re.search(r'        run: \|\n((?:          .*\n)+)',text).group(1)
        code='\n'.join(line[10:] for line in body.splitlines())
        env=dict(EVENT='workflow_dispatch',REPOSITORY='ivandinicola/CONSTIA',REVISION='b'*40,
                 ATTEMPT='1',LAUNCHER_REF='refs/heads/main',WORKFLOW_REF=r.WORKFLOW_REF,
                 WORKFLOW_REVISION='b'*40,CODE_SHA='a'*40,CONSTIA_B3_APPROVED_SHA='')
        with patch.dict(os.environ,env,clear=True),self.assertRaises(SystemExit):
            exec(compile(code,'gate','exec',optimize=2),{})


    def session(self):
        s=dict(schema=2,repository=r.REPOSITORY,planned={},run_id='123',nonce='c'*32,code_sha='a'*40,
               network_name='c3c02b-'+'c'*32,subnet='172.29.0.0/24',gateway='172.29.0.1')
        s['labels']=r.labels(s)
        return s

    def test_h02_intent_precedes_creation_and_response_loss_is_reconciled(self):

        import subprocess
        from unittest.mock import patch
        def check(case):
            case.populate(('network_id',));r._admit_session(case.s)
            def lost(args,**kw):
                case.assertEqual(5,len(r._read_manifest()[0]['resources']))
                raise subprocess.TimeoutExpired(['synthetic'],1)
            with patch.object(r,'command',side_effect=lost):
                with case.assertRaises(subprocess.TimeoutExpired):r.create_resource(case.s,'network_id',['docker','network','create','synthetic'])
            case.assertEqual('e'*64,case.s['network_id'])
            r._freeze_producer(case.c);case.assertEqual('unresolved',case.c.session['producer_state'])
            case.assertNotEqual('verified',case.clean())
        _with_protocol_fixture(check)

    def test_h02_absent_after_timeout_remains_uncertain(self):

        import subprocess
        from unittest.mock import patch
        def check(case):
            r._admit_session(case.s)
            with patch.object(r,'command',side_effect=subprocess.TimeoutExpired(['synthetic'],1)):
                with case.assertRaises(subprocess.TimeoutExpired):r.create_resource(case.s,'network_id',['docker','network','create','synthetic'])
            r._freeze_producer(case.c)
            case.assertEqual('unresolved',case.c.session['producer_state'])
            case.assertNotEqual('verified',case.clean());case.assertEqual([],case.removals)
        _with_protocol_fixture(check)

    def test_h02_matching_name_with_foreign_labels_is_not_owned(self):
        from unittest.mock import patch
        s=self.session()
        _legacy_plan_fixture(s,'volume_id')
        obj={'Name':r.resource_specs(s)['volume_id'][1],'Driver':'local','Options':None,'Labels':{'constia.owner':'elsewhere'}}
        with self.assertRaises(r.Unsafe):r.verify_owned(s,'volume_id',obj)
        obj['Labels']=s['labels'];obj['Options']={'device':'/host/data','o':'bind'}
        with self.assertRaises(r.Unsafe):r.verify_owned(s,'volume_id',obj)

    def test_h02_cleanup_failure_is_not_success(self):

        from unittest.mock import patch
        def check(case):
            case.populate(('network_id',));case.witness();actual=case.engine_command
            def command(args,**kw):
                if 'rm' in args:raise OSError('PRIVATE_SECRET')
                return actual(args,**kw)
            with patch.object(r,'command',side_effect=command):
                case.assertEqual('failed',case.clean())
            case.assertEqual('CLEANUP_FAILED',case.c.operation_failure['code'])
            case.assertTrue(case.objects)
        _with_protocol_fixture(check)


    def test_h03_rule_construction_and_namespace_binding(self):
        v4,v6=r.firewall_snapshot('10.8.0.2')
        r.validate_firewall('10.8.0.2','\n'.join(v4),'\n'.join(v6))
        for a,b in [(v4+['-A OUTPUT -j ACCEPT'],v6),(v4[1:],v6),(v4,v6+['-A OUTPUT -j ACCEPT'])]:
            with self.assertRaises(r.Unsafe):r.validate_firewall('10.8.0.2','\n'.join(a),'\n'.join(b))
        args=r.namespace_firewall_command('123','net:[456]','10.8.0.2')
        self.assertEqual(['c3c02b-firewall','net:[456]','10.8.0.2'],args[-3:])
        self.assertIn('test "$(readlink /proc/self/ns/net)" = "$1"',args[-4])
        with self.assertRaises(r.Unsafe):r.namespace_firewall_command('0','net:[456]','10.8.0.2')

    def test_h03_guard_rejects_host_namespaces_and_inherited_secrets(self):
        from unittest.mock import patch
        s=self.session();s.update(app_id='d'*64,app_image_id='sha256:'+'f'*64,network_id='e'*64)
        obj={'Id':s['app_id'],'Image':s['app_image_id'],'Config':{'Labels':s['labels'],'Env':[]},
             'HostConfig':{'NetworkMode':s['network_name'],'ReadonlyRootfs':True,'CapDrop':['ALL'],'SecurityOpt':['no-new-privileges'],'IpcMode':'private'},
             'Mounts':[],'NetworkSettings':{'Networks':{s['network_name']:{'NetworkID':s['network_id'],'IPAddress':'172.29.0.3'}}}}
        with patch.object(r,'inspect',return_value=obj):r.app(s)
        for key,value in [('PidMode','host'),('IpcMode','host'),('SecurityOpt',[])]:
            bad=copy.deepcopy(obj);bad['HostConfig'][key]=value
            with patch.object(r,'inspect',return_value=bad),self.assertRaises(r.Unsafe):r.app(s)
        bad=copy.deepcopy(obj);bad['Config']['Env']=['RESEND_API_KEY=synthetic-forbidden']
        with patch.object(r,'inspect',return_value=bad),self.assertRaises(r.Unsafe):r.app(s)

    def test_h04_topology_rejects_public_ambiguous_or_wrong_members(self):
        for values in [('8.8.8.0/24','8.8.8.1','8.8.8.2'),('10.8.0.0/24','10.9.0.1','10.8.0.2'),
                       ('10.8.0.0/24','10.8.0.1','10.8.0.1'),('10.8.0.0/24','10.8.0.1','10.8.0.255'),
                       ('10.0.0.0/7','10.0.0.1','10.0.0.2'),('fd00::/64','fd00::1','fd00::2')]:
            with self.subTest(values=values),self.assertRaises(r.Unsafe):r.topology(*values)

    def test_h05_queries_need_real_validation_fields(self):
        valid=dict(id='Q00.1',rows=1,elapsed_ms=1.0,syntax=True,restricted_role=True,
                   plan_sha256='a'*64,estimated_cost=0.1,operators=['Constant Scan'])
        r.validate_queries([valid],['Q00.1'])
        for key,value in [('syntax',False),('restricted_role',False),('elapsed_ms',float('nan')),
                          ('estimated_cost',-1),('plan_sha256',''),('operators',[]),('rows',-1)]:
            row=copy.deepcopy(valid);row[key]=value
            with self.subTest(key=key),self.assertRaises(r.Unsafe):r.validate_queries([row],['Q00.1'])
        with self.assertRaises(r.Unsafe):r.validate_queries([valid,valid],['Q00.1'])

    def test_h05_final_success_requires_all_evidence_and_verified_cleanup(self):
        self.assertEqual('passed',r.final_status(True,True,True))
        for args in [(False,True,True),(True,False,True),(True,True,False),(True,True,True,True),('true',True,True)]:
            with self.subTest(args=args):self.assertEqual('failed',r.final_status(*args))

    def test_h05_trx_missing_omitted_duplicate_and_corrupt_counters_fail(self):
        import tempfile
        base='''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
        <TestDefinitions><UnitTest id="one"><TestMethod name="Case"/></UnitTest></TestDefinitions>
        <Results><UnitTestResult testId="one" outcome="Passed"/></Results>
        <ResultSummary><Counters total="1" executed="1" passed="1" failed="0"/></ResultSummary></TestRun>'''
        with tempfile.TemporaryDirectory() as d:
            p=pathlib.Path(d)/'a.trx';t=pathlib.Path(d)/'b.trx'
            p.write_text(base);self.assertTrue(r.sanitized_trx(p,t,['Case']))
            variants=[base.replace('executed="1"','executed="0"'),base.replace('total="1"','total="2"'),
                      base.replace('passed="1"','passed="0"'),base.replace('failed="0"','failed="1"'),
                      base.replace('<UnitTestResult testId="one" outcome="Passed"/>',''),
                      base.replace('<UnitTestResult testId="one" outcome="Passed"/>','<UnitTestResult testId="one" outcome="Passed"/><UnitTestResult testId="one" outcome="Passed"/>')]
            for text in variants:
                p.write_text(text)
                with self.assertRaises(r.Unsafe):r.sanitized_trx(p,t,['Case'])
            p.write_text(base.replace('outcome="Passed"','outcome="NotExecuted"').replace('passed="1"','passed="0"'))
            self.assertFalse(r.sanitized_trx(p,t,['Case']))
            p.write_text('<incomplete')
            with self.assertRaises(r.ET.ParseError):r.sanitized_trx(p,t,['Case'])
            p.unlink()
            with self.assertRaises(FileNotFoundError):r.sanitized_trx(p,t,['Case'])


    def test_h03_endpoint_override_and_nonlocal_engine_are_rejected(self):
        import json
        from unittest.mock import patch
        for args in [['docker','--context','remote','ps'],['docker','--host=tcp://elsewhere','ps']]:
            with self.assertRaises(r.Unsafe):r.checked_command(args)
        self.assertEqual(['docker','--context','default','ps'],r.checked_command(['docker','ps']))
        with patch.object(r,'command',return_value=json.dumps([{'Endpoints':{'docker':{'Host':'tcp://remote:2375'}}}])):
            with self.assertRaises(r.Unsafe):r.require_local_engine()

    def test_h02_successful_exact_cleanup_is_idempotent(self):

        def check(case):
            case.populate(('network_id',));case.witness()
            case.assertEqual('verified',case.clean())
            case.assertEqual([['docker','network','rm','e'*64]],case.removals)
            n=len(case.queries);case.assertEqual('verified',case.clean())
            case.assertEqual(1,len(case.removals));case.assertGreater(len(case.queries),n)
        _with_protocol_fixture(check)

    def test_h04_network_requires_exact_ipam_and_membership(self):
        from unittest.mock import patch
        s=self.session();s['network_id']='e'*64
        n={'Id':s['network_id'],'Internal':True,'Driver':'bridge','Labels':s['labels'],'EnableIPv6':False,
           'IPAM':{'Config':[{'Subnet':s['subnet'],'Gateway':s['gateway']}]}}
        with patch.object(r,'inspect',return_value=n):r.network(s)
        bad=copy.deepcopy(n);bad['IPAM']['Config'][0]['Subnet']='10.8.0.0/24'
        with patch.object(r,'inspect',return_value=bad),self.assertRaises(r.Unsafe):r.network(s)
        bad=copy.deepcopy(n);bad['IPAM']['Config'].append({'Subnet':'10.9.0.0/24','Gateway':'10.9.0.1'})
        with patch.object(r,'inspect',return_value=bad),self.assertRaises(r.Unsafe):r.network(s)

    def test_h05_cleanup_failure_does_not_publish_passed_or_upgrade_failure(self):

        from unittest.mock import patch
        import tempfile
        with tempfile.TemporaryDirectory() as directory:
            p=pathlib.Path(directory)/'isolation-execution.json';p.write_bytes(b'{"status":"failed"}')
            with patch.object(r,'public_directory',return_value=p.parent):
                for run_ok,cleanup_ok in ((True,False),(False,True),(True,True)):
                    with self.assertRaises(r.Unsafe):r.publish_execution(run_ok,cleanup_ok,'cleanup')
            self.assertEqual(b'{"status":"failed"}',p.read_bytes())


    def test_h05_arbitrary_trx_nodes_are_not_copied(self):
        import tempfile
        with tempfile.TemporaryDirectory() as d:
            source=pathlib.Path(d)/'a.trx';target=pathlib.Path(d)/'b.trx'
            source.write_text('''<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
            <PrivateNode arbitrary="opaque-private-value">opaque-private-value</PrivateNode>
            <TestDefinitions><UnitTest id="one"><TestMethod name="Case"/></UnitTest></TestDefinitions>
            <Results><UnitTestResult testId="one" outcome="Passed"/></Results>
            <ResultSummary><Counters total="1" executed="1" passed="1" failed="0"/></ResultSummary></TestRun>''')
            self.assertTrue(r.sanitized_trx(source,target,['Case']))
            self.assertNotIn('opaque-private-value',target.read_text())
            self.assertNotIn('PrivateNode',target.read_text())

    def test_h05_modified_published_artifact_invalidates_cleanup_report(self):

        import json
        def check(case):
            case.witness()
            primary=r.make_diagnostic('cleanup_primary',case.c,case.c.witness_sha256)
            r.write_diagnostic('isolation-cleanup-primary.json',primary)
            p=r.diagnostic_directory()/'isolation-execution.json'
            original=json.loads(p.read_text());original['first_failure']=dict(stage='git_head',code='GIT_REJECTED',reason='HEAD_MISMATCH')
            p.write_text(json.dumps(original))
            with case.assertRaises(r.Unsafe):r.read_diagnostic('isolation-cleanup-primary.json')
        _with_protocol_fixture(check)

    def test_h02_replaced_recorded_id_is_rejected(self):
        from unittest.mock import patch
        s=self.session()
        _legacy_plan_fixture(s,'network_id')
        s['planned']['network_id'].update(id='d'*64,phase='owned')
        obj={'Id':'e'*64,'Name':s['network_name'],'Internal':True,'Driver':'bridge','Labels':s['labels']}
        with self.assertRaises(r.Unsafe):r.verify_owned(s,'network_id',obj)


    def test_h02_lookup_is_exact_and_does_not_treat_daemon_errors_as_absence(self):
        import subprocess,json
        from unittest.mock import patch
        name='c3c02b-'+'c'*32
        missing=subprocess.CompletedProcess([],1,stdout=b'[]',stderr=('Error response from daemon: network '+name+' not found').encode())
        with patch.object(r,'_bounded_docker',return_value=missing) as called:
            self.assertIsNone(r.lookup('network',name))
            self.assertEqual(['docker','network','inspect',name],called.call_args.args[0])
        broken=subprocess.CompletedProcess([],1,stdout=b'',stderr=b'Cannot connect to daemon')
        with patch.object(r,'_bounded_docker',return_value=broken),self.assertRaises(r.Unsafe):r.lookup('network',name)
        ambiguous=subprocess.CompletedProcess([],0,stdout=json.dumps([{'Id':'d'*64},{'Id':'e'*64}]).encode(),stderr=b'')
        with patch.object(r,'_bounded_docker',return_value=ambiguous),self.assertRaises(r.Unsafe):r.lookup('network',name)

class IsolationPhaseTests(unittest.TestCase):
    def environment(self):
        e=SafetyTests().ci_environment()
        e.update(C3C02B_PHASE='isolation-only',CONSTIA_B3_APPROVED_PHASE='isolation-only',
                 GITHUB_ACTOR_ID='1234',CONSTIA_B3_APPROVED_ACTOR_ID='1234',
                 CONSTIA_B3_INVENTORY_SHA256='f'*64,CONSTIA_B3_APPROVED_SHA='a'*40)
        return e

    def test_phase_is_explicit_and_full_is_never_authorized(self):
        self.assertTrue(hasattr(r,'authorize_isolation'),'Missing separate isolation authorization')
        r.authorize_isolation(self.environment(),'isolation-only')
        for phase in (None,'','unknown','operational-full'):
            with self.subTest(phase=phase),self.assertRaises(r.Unsafe):r.authorize_isolation(self.environment(),phase)
        for key,value in [('C3C02B_PHASE','operational-full'),('CONSTIA_B3_APPROVED_PHASE','operational-full'),
                          ('GITHUB_ACTOR_ID','9999'),('GITHUB_REF','refs/heads/feature'),
                          ('CONSTIA_B3_INVENTORY_SHA256',''),('CONSTIA_B3_APPROVED_SHA','')]:
            e=self.environment();e[key]=value
            with self.subTest(key=key),self.assertRaises(r.Unsafe):r.authorize_isolation(e,'isolation-only')

    def identity(self):
        return dict(phase='isolation-only',repository=r.REPOSITORY,code_sha='a'*40,policy_sha256='f'*64,
                    workflow_sha='b'*40,workflow_ref=r.WORKFLOW_REF,actor_id='1234',run_id='123',attempt='1',nonce='c'*32,
                    network_id='e'*64,network_name='c3c02b-'+'c'*32,subnet='10.8.0.0/24',gateway='10.8.0.1',
                    app_id='1'*64,app_ip='10.8.0.2',peer_id='2'*64,peer_ip='10.8.0.3',
                    denied_id='3'*64,denied_ip='10.8.0.4',image_id='sha256:'+'d'*64,sdk_image=r.SDK_IMAGE,
                    namespace='net:[123]',rules_sha256=None,proof_stage='prepared',gateway_port=40000)

    def test_preflight_identity_never_accepts_sql_or_another_phase(self):
        self.assertTrue(hasattr(r,'validate_preflight'),'No preflight identity')
        m=self.identity();r.validate_preflight(m)
        for key,value in [('phase','operational-full'),('database','C3C02B_x'),('sql_major',16),
                          ('runtime','c3_runtime'),('peer_ip','8.8.8.8'),('denied_ip',m['peer_ip']),
                          ('denied_id',m['peer_id']),('gateway_port',1433),('namespace','unknown')]:
            x=copy.deepcopy(m);x[key]=value
            with self.subTest(key=key),self.assertRaises(r.Unsafe):r.validate_preflight(x)

    def test_inventory_bytes_and_exact_paths_are_checked(self):
        import tempfile,json,hashlib
        self.assertTrue(hasattr(r,'verify_inventory'),'No approved inventory guard')
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d);table={x:hashlib.sha256(b'reviewed').hexdigest() for x in r.INVENTORY_PATHS}
            for rel in table:
                p=root/rel;p.parent.mkdir(parents=True,exist_ok=True);p.write_bytes(b'reviewed')
            digest=hashlib.sha256(json.dumps(table,sort_keys=True,separators=(',',':')).encode()).hexdigest()
            e=dict(CONSTIA_B3_INVENTORY=json.dumps(table),CONSTIA_B3_INVENTORY_SHA256=digest)
            r.verify_inventory(root,e)
            (root/next(iter(table))).write_bytes(b'changed')
            with self.assertRaises(r.Unsafe):r.verify_inventory(root,e)
            e['CONSTIA_B3_INVENTORY_SHA256']='0'*64
            with self.assertRaises(r.Unsafe):r.verify_inventory(root,e)

    def test_operational_entry_is_blocked_before_any_command(self):
        from unittest.mock import patch
        with patch.object(r,'command') as cmd,self.assertRaises(r.Unsafe):r.run()
        cmd.assert_not_called()

    def report(self):
        m=self.identity();m.update(proof_stage='isolated',rules_sha256='e'*64)
        return dict(identity=m,phase='isolation-only',allowed_peer=True,denied_peer=True,gateway_blocked=True,
                    controls_positive=True,simulated_transport=True,routes_sha256='a'*64,sql='not_applicable',workers_started=0)

    def test_preflight_evidence_cannot_claim_operational_results(self):
        self.assertTrue(hasattr(r,'validate_isolation_evidence'),'No phase-specific evidence validator')
        m=self.identity();m.update(proof_stage='isolated',rules_sha256='e'*64)
        report=dict(identity=m,phase='isolation-only',allowed_peer=True,denied_peer=True,gateway_blocked=True,
                    controls_positive=True,simulated_transport=True,routes_sha256='a'*64,
                    sql='not_applicable',workers_started=0)
        r.validate_isolation_evidence(report)
        for key,value in [('operational_trx','passed'),('query_blocks',22),('sql','verified'),
                          ('workers_started',1),('denied_peer',False),('simulated_transport',False)]:
            x=copy.deepcopy(report);x[key]=value
            with self.subTest(key=key),self.assertRaises(r.Unsafe):r.validate_isolation_evidence(x)

    def test_new_peer_ownership_can_be_verified_before_its_id_is_saved(self):
        s=SafetyTests().session();s.update(phase='isolation-only',network_id='e'*64,app_image_id='sha256:'+'d'*64)
        from unittest.mock import patch
        _legacy_plan_fixture(s,'peer_id')
        obj={'Id':'2'*64,'Name':'/'+r.resource_specs(s)['peer_id'][1],'Image':s['app_image_id'],
             'Config':{'Labels':s['labels'],'Env':[]},'Mounts':[],
             'HostConfig':{'NetworkMode':s['network_name'],'ReadonlyRootfs':True,'CapDrop':['ALL'],
                           'SecurityOpt':['no-new-privileges'],'IpcMode':'private'},
             'NetworkSettings':{'Networks':{s['network_name']:{'NetworkID':s['network_id'],'IPAddress':'172.29.0.3'}}}}
        self.assertEqual('2'*64,r.verify_owned(s,'peer_id',obj))
        self.assertNotIn('peer_id',s)

    def test_preflight_network_cannot_include_a_foreign_endpoint(self):
        self.assertTrue(hasattr(r,'validate_preflight_membership'),'Missing exclusive endpoint validation')
        m=self.identity();s=SafetyTests().session();s.update(phase='isolation-only',**{k:m[k] for k in ('app_id','peer_id','denied_id','app_ip','peer_ip','denied_ip','subnet','gateway')})
        rows={s[key]:{'Name':r.resource_specs(s)[key][1],'IPv4Address':s[key.replace('_id','_ip')]+'/24'} for key in ('app_id','peer_id','denied_id')}
        r.validate_preflight_membership(s,{'Containers':rows})
        foreign=copy.deepcopy(rows);foreign['9'*64]={'Name':'foreign','IPv4Address':'10.8.0.9/24'}
        with self.assertRaises(r.Unsafe):r.validate_preflight_membership(s,{'Containers':foreign})
        wrong=copy.deepcopy(rows);wrong[s['peer_id']]['IPv4Address']='10.8.0.9/24'
        with self.assertRaises(r.Unsafe):r.validate_preflight_membership(s,{'Containers':wrong})

    def test_rules_are_rechecked_without_repairing_them(self):
        from unittest.mock import patch
        import hashlib
        s=SafetyTests().session();s.update(app_id='1'*64,peer_ip='172.29.0.2',namespace='net:[123]')
        v4,v6=r.firewall_snapshot(s['peer_ip']);a='\n'.join(v4);b='\n'.join(v6)
        s['rules_sha256']=hashlib.sha256((a+'\n'+b).encode()).hexdigest()
        with patch.object(r,'app',return_value={'State':{'Pid':123}}),patch.object(r,'command',side_effect=['net:[123]','IPV4\n'+a+'\nIPV6\n'+b]) as cmd:
            r.verify_installed_rules(s)
            script=cmd.call_args.args[0][-3]
            self.assertIn('iptables -S OUTPUT',script)
            self.assertNotIn('iptables -P',script);self.assertNotIn('iptables -F',script)
        with patch.object(r,'app',return_value={'State':{'Pid':123}}),patch.object(r,'command',side_effect=['net:[123]','IPV4\n'+a+'\n-A OUTPUT -j ACCEPT\nIPV6\n'+b]):
            with self.assertRaises(r.Unsafe):r.verify_installed_rules(s)

    def test_routes_reject_foreign_gateway_ipv6_and_unexpected_network(self):
        s=dict(subnet='10.8.0.0/24',gateway='10.8.0.1')
        good=[{'dst':'10.8.0.0/24','dev':'eth0'},{'dst':'default','gateway':'10.8.0.1','dev':'eth0'}]
        r.validate_routes(s,good,[])
        for rows,ipv6 in [(good+[{'dst':'8.8.8.0/24','dev':'eth0'}],[]),
                          ([{'dst':'default','gateway':'10.8.0.1','dev':'eth0'}],[]),
                          (good,[{'dst':'default','dev':'eth0'}]),
                          (good+[{'dst':'default','gateway':'10.8.0.9','dev':'eth0'}],[])]:
            with self.assertRaises(r.Unsafe):r.validate_routes(s,rows,ipv6)

    def test_main_rejects_full_even_if_caller_fills_every_policy_value(self):
        import os
        from unittest.mock import patch
        with patch.dict(os.environ,self.environment(),clear=True),patch.object(r.sys,'argv',['runner','run','--phase','operational-full']),patch.object(r,'command') as cmd:
            self.assertEqual(1,r.main());cmd.assert_not_called()

    def test_main_rejects_duplicate_phase_even_when_the_last_value_is_isolation(self):
        import os
        from unittest.mock import patch
        with patch.dict(os.environ,self.environment(),clear=True),patch.object(r.sys,'argv',['runner','run','--phase','operational-full','--phase','isolation-only']),patch.object(r,'run_isolation') as run,patch.object(r,'cleanup'),patch.object(r,'publish_isolation',return_value='failed'):
            self.assertEqual(1,r.main());run.assert_not_called()

    def test_isolation_resource_inventory_has_no_sql_or_volume(self):
        s=SafetyTests().session();s['phase']='isolation-only'
        self.assertEqual({'app_id','peer_id','denied_id','network_id','app_image_id'},set(r.resource_specs(s)))
        from unittest.mock import patch
        for key in ('container_id','volume_id'):
            with patch.object(r,'save'),self.assertRaises(r.Unsafe):r.plan_resource(s,key)

    def test_workflow_gate_rejects_wrong_phase_actor_and_digest(self):
        import os,re,json,hashlib
        from unittest.mock import patch
        text=pathlib.Path(__file__).parents[1].joinpath('workflows/c3c02b-operational.yml').read_text()
        code='\n'.join(x[10:] for x in re.search(r'        run: \|\n((?:          .*\n)+)',text).group(1).splitlines())
        table={p:'a'*64 for p in r.INVENTORY_PATHS}
        e=self.environment();e.update(EVENT='workflow_dispatch',REPOSITORY=r.REPOSITORY,REVISION='b'*40,
            ATTEMPT='1',LAUNCHER_REF='refs/heads/main',WORKFLOW_REF=r.WORKFLOW_REF,WORKFLOW_REVISION='b'*40,
            ACTOR_ID='1234',CONSTIA_B3_INVENTORY=json.dumps(table),
            CONSTIA_B3_INVENTORY_SHA256=hashlib.sha256(json.dumps(table,sort_keys=True,separators=(',',':')).encode()).hexdigest())
        with patch.dict(os.environ,e,clear=True):exec(compile(code,'workflow_gate','exec',optimize=2),{})
        for key,value in [('ACTOR_ID','9999'),('CONSTIA_B3_APPROVED_PHASE','operational-full'),
                          ('LAUNCHER_REF','refs/heads/feature'),('CONSTIA_B3_INVENTORY_SHA256','b'*64),
                          ('CONSTIA_B3_APPROVED_SHA','')]:
            bad=dict(e);bad[key]=value
            with patch.dict(os.environ,bad,clear=True),self.subTest(key=key),self.assertRaises(SystemExit):exec(compile(code,'gate','exec',optimize=2),{})

    def test_isolation_cleanup_journal_binds_phase_code_policy_and_launcher(self):
        self.assertTrue(hasattr(r,'validate_isolation_journal'),'Missing phase-specific cleanup identity')
        s=SafetyTests().session();s.update(phase='isolation-only',policy_sha256='f'*64,workflow_sha='b'*40,actor_id='1234')
        r.validate_isolation_journal(s,self.environment())
        for key,value in [('phase','operational-full'),('policy_sha256','a'*64),('actor_id','9999'),('sql_image_id','sha256:'+'a'*64)]:
            bad=dict(s);bad[key]=value
            with self.subTest(key=key),self.assertRaises(r.Unsafe):r.validate_isolation_journal(bad,self.environment())

    def test_phase_authorization_precedes_any_side_effect(self):
        import os
        from unittest.mock import patch
        for phase in ('operational-full',None,'unknown'):
            with patch.dict(os.environ,self.environment(),clear=True),patch.object(r,'command') as cmd,patch.object(r,'save') as save:
                with self.assertRaises(r.Unsafe):r.run_isolation(phase)
                cmd.assert_not_called();save.assert_not_called()

    def test_isolation_publisher_never_accepts_operational_trx(self):
        import os,tempfile,json
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d:
            out=pathlib.Path(d)/'c3c02b-public';out.mkdir();(out/'operational.trx').write_text('synthetic')
            e=self.environment();e['RUNNER_TEMP']=d
            with patch.dict(os.environ,e,clear=True),self.assertRaises(r.Unsafe):r.publish_isolation(True,True,'run')

    def test_isolation_publisher_rejects_unexpected_artifacts(self):
        import os,tempfile
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d:
            out=pathlib.Path(d)/'c3c02b-public';out.mkdir();(out/'unexpected.json').write_text('{}')
            e=self.environment();e['RUNNER_TEMP']=d
            with patch.dict(os.environ,e,clear=True),self.assertRaises(r.Unsafe):r.publish_isolation(True,True,'run')

    def test_final_route_drift_fails_instead_of_repairing_it(self):
        from unittest.mock import patch
        self.assertTrue(hasattr(r,'verify_final_isolation'),'No final route drift guard')
        with patch.object(r,'verify_installed_rules') as rules,patch.object(r,'network') as net,patch.object(r,'routes_proof',return_value='b'*64):
            with self.assertRaises(r.Unsafe):r.verify_final_isolation({},'a'*64)
            rules.assert_called_once();net.assert_called_once()

    def test_isolation_publisher_requires_complete_evidence_and_cleanup(self):

        # B8/B9 schema2 candidates never certify L2 publication, even with positive inputs.
        for run_ok,cleanup_ok in ((True,True),(False,True),(True,False)):
            with self.assertRaises(r.Unsafe):r.publish_isolation(run_ok,cleanup_ok,'run')



class H2DiagnosticTests(unittest.TestCase):
    def test_contract_is_versioned_and_has_three_immutable_records(self):
        self.assertTrue(hasattr(r, 'DIAGNOSTIC_FILES'), 'H2 diagnostic contract is missing')
        self.assertEqual({'isolation-execution.json', 'isolation-cleanup-primary.json',
                          'isolation-cleanup-final.json'}, set(r.DIAGNOSTIC_FILES))
        self.assertEqual(2, r.DIAGNOSTIC_SCHEMA)

    def environment(self, directory):
        e = IsolationPhaseTests().environment()
        e['RUNNER_TEMP'] = str(directory)
        return e

    def context(self):
        return r.DiagnosticContext()

    def execution(self):
        return r.make_diagnostic('execution', self.context())

    def test_every_diagnostic_pair_is_fixed_and_unexpected_text_is_rejected(self):
        import tempfile, os
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ, self.environment(d), clear=True):
            for stage, code, reason in r.DIAGNOSTIC_CATALOG:
                c = self.context()
                c.fail(stage, code, reason)
                rec = r.make_diagnostic('execution', c)
                r.validate_diagnostic(rec)
                self.assertEqual(dict(stage=stage, code=code, reason=reason), rec['first_failure'])
            for values in [('secret', 'AUTH_REJECTED', 'POLICY'), ('authorization', 'Bearer secret', 'POLICY'),
                           ('authorization', 'AUTH_REJECTED', 'private@example.invalid')]:
                with self.subTest(values=values), self.assertRaises(r.Unsafe):
                    self.context().fail(*values)

    def test_first_failure_survives_cleanup_and_unexpected_exceptions_are_not_copied(self):
        import tempfile, os, io, contextlib
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ, self.environment(d), clear=True):
            c = self.context()
            token = r.ACTIVE_DIAGNOSTIC.set(c)
            try:
                with self.assertRaises(OSError):
                    r.diagnostic_call('git_head', lambda: (_ for _ in ()).throw(OSError('Bearer PRIVATE_SECRET')))
                first = copy.deepcopy(c.first_failure)
                c.cleaning = True
                with self.assertRaises(RuntimeError):
                    r.diagnostic_call('cleanup', lambda: (_ for _ in ()).throw(RuntimeError('private@example.invalid')))
                self.assertEqual(first, c.first_failure)
                self.assertIsNotNone(c.operation_failure)
                self.assertNotIn('PRIVATE_SECRET', str(r.make_diagnostic('execution', c)))
            finally:
                r.ACTIVE_DIAGNOSTIC.reset(token)

    def test_records_are_exclusive_bounded_and_hash_linked(self):
        import tempfile, os, json, hashlib
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ, self.environment(d), clear=True):
            rec = self.execution()
            digest = r.write_diagnostic('isolation-execution.json', rec)
            self.assertEqual(digest, hashlib.sha256((r.diagnostic_directory()/'isolation-execution.json').read_bytes()).hexdigest())
            with self.assertRaises(FileExistsError):
                r.write_diagnostic('isolation-execution.json', rec)
            self.assertEqual(rec, r.read_diagnostic('isolation-execution.json'))
            extra = copy.deepcopy(rec); extra['token'] = 'PRIVATE_SECRET'
            with self.assertRaises(r.Unsafe): r.validate_diagnostic(extra)
            with self.assertRaises(r.Unsafe): r.write_diagnostic('../escape.json', rec)
            path = r.diagnostic_directory()/'isolation-execution.json'
            for data in [b'{"schema":1,"schema":1}', b'{', b'x'*(r.MAX_DIAGNOSTIC_BYTES+1)]:
                path.write_bytes(data)
                with self.assertRaises((r.Unsafe, ValueError)): r.read_diagnostic(path.name)

    def test_candidate_cannot_claim_L2_validation_or_contradict_creation(self):
        import tempfile, os
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ, self.environment(d), clear=True):
            rec = self.execution()
            for key, value in [('evidence','complete_validated'), ('schema',1), ('phase','operational-full'),
                               ('creation_attempted', 'false'), ('execution','completed')]:
                bad = copy.deepcopy(rec); bad[key] = value
                with self.subTest(key=key), self.assertRaises(r.Unsafe): r.validate_diagnostic(bad)

    def test_partial_write_is_never_replaced_or_validated(self):
        import tempfile, os
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ, self.environment(d), clear=True):
            rec = self.execution()
            with patch.object(r.os, 'fsync', side_effect=OSError('PRIVATE_SECRET')):
                with self.assertRaises(OSError): r.write_diagnostic('isolation-execution.json', rec)
            self.assertFalse((r.diagnostic_directory()/'isolation-execution.json').exists())
            r.write_diagnostic('isolation-execution.json', rec)
            self.assertEqual(rec, r.read_diagnostic('isolation-execution.json'))

    def test_cleanup_without_execution_is_uncertain_and_cannot_invent_absence(self):
        import tempfile, os, io, contextlib
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ, self.environment(d), clear=True), \
             patch.object(r.sys,'argv',['runner','cleanup','--phase','isolation-only']), \
             patch.object(r.subprocess,'run',side_effect=AssertionError('Native process forbidden')):
            with contextlib.redirect_stderr(io.StringIO()): self.assertEqual(1, r.main())
            rec = r.read_diagnostic('isolation-cleanup-final.json')
            self.assertEqual('uncertain', rec['cleanup'])
            self.assertIsNone(rec['execution'])
            self.assertIsNone(rec['creation_attempted'])

    def test_synthetic_failure_before_dispatch_is_not_applicable_only_with_proof(self):
        import tempfile, os, io, contextlib
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ,self.environment(d),clear=True):
            def fail(phase):
                r.diagnostic_reject('git_head', 'GIT_REJECTED', 'HEAD_MISMATCH')
            with patch.object(r,'run_isolation',side_effect=fail), patch.object(r.sys,'argv',['runner','run','--phase','isolation-only']), \
                 patch.object(r.subprocess,'run',side_effect=AssertionError('Native process forbidden')),contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(1,r.main())
            original=(r.diagnostic_directory()/'isolation-execution.json').read_bytes()
            primary=r.read_diagnostic('isolation-cleanup-primary.json')
            self.assertEqual('failed_before_creation',primary['execution'])
            self.assertEqual('not_applicable',primary['cleanup'])
            self.assertEqual('this_invocation_no_dispatch',primary['cleanup_scope'])
            with patch.object(r.sys,'argv',['runner','cleanup','--phase','isolation-only']),contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(0,r.main())
            self.assertEqual(original,(r.diagnostic_directory()/'isolation-execution.json').read_bytes())
            final=r.read_diagnostic('isolation-cleanup-final.json')
            self.assertEqual(primary['first_failure'],final['first_failure'])
            with patch.object(r.sys,'argv',['runner','cleanup','--phase','isolation-only']),contextlib.redirect_stderr(io.StringIO()):
                self.assertEqual(1,r.main())

    def test_diagnostics_reject_symlink_file(self):
        import tempfile, os
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ,self.environment(d),clear=True):
            target=pathlib.Path(d)/'target';target.write_text('{}')
            link=r.diagnostic_directory()/'isolation-execution.json'
            try: link.symlink_to(target)
            except OSError as ex:
                if getattr(ex,'winerror',None)==1314: self.skipTest('Windows symlink privilege unavailable (1314)')
                raise
            with self.assertRaises(r.Unsafe): r.read_diagnostic(link.name)

    def test_diagnostics_reject_symlink_directory(self):
        import tempfile, os
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d, patch.dict(os.environ,self.environment(d),clear=True):
            target=pathlib.Path(d)/'target';target.mkdir()
            link=pathlib.Path(d)/'c3c02b-diagnostics'
            try: link.symlink_to(target,target_is_directory=True)
            except OSError as ex:
                if getattr(ex,'winerror',None)==1314: self.skipTest('Windows symlink privilege unavailable (1314)')
                raise
            with self.assertRaises(r.Unsafe): r.diagnostic_directory()



class H2SyntheticIntegrationTests(unittest.TestCase):
    def setUp(self):
        import tempfile, os, json, hashlib
        from contextlib import ExitStack
        from unittest.mock import patch
        self.tmp=tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root=pathlib.Path(self.tmp.name)/'source';self.root.mkdir()
        self.rt=pathlib.Path(self.tmp.name)/'runtime';self.rt.mkdir()
        table={}
        for rel in r.INVENTORY_PATHS:
            path=self.root/rel;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'reviewed')
            table[rel]=hashlib.sha256(b'reviewed').hexdigest()
        self.e=IsolationPhaseTests().environment()
        self.e.update(RUNNER_TEMP=str(self.rt),CONSTIA_B3_INVENTORY=json.dumps(table),
                      CONSTIA_B3_INVENTORY_SHA256=hashlib.sha256(json.dumps(table,sort_keys=True,separators=(',',':')).encode()).hexdigest())
        self.stack=ExitStack();self.addCleanup(self.stack.close)
        self.stack.enter_context(patch.dict(os.environ,self.e,clear=True))
        self.stack.enter_context(patch.object(r,'__file__',str(self.root/'.github/scripts/c3c02b_runner.py')))
        self.native=self.stack.enter_context(patch.object(r.subprocess,'run',side_effect=AssertionError('Native process forbidden')))
        self.popen=self.stack.enter_context(patch.object(r.subprocess,'Popen',side_effect=AssertionError('Native process forbidden')))
        self.command=self.stack.enter_context(patch.object(r,'command',side_effect=self.fake_command))
        self.stack.enter_context(patch.object(r,'image_build_command',side_effect=lambda *a,**kw:r.command(*a,**kw)))
        self.build=self.stack.enter_context(patch.object(r,'build_preflight_image',side_effect=AssertionError('Build boundary forbidden')))
        self.addCleanup(self.native.assert_not_called)
        self.addCleanup(self.popen.assert_not_called)
        import subprocess
        def empty_query(args,**kw):
            if args[2]=='ls':return subprocess.CompletedProcess(args,0,b'',b'')
            return subprocess.CompletedProcess(args,1,b'[]',('Error: No such object: '+args[3]).encode())
        self.stack.enter_context(patch.object(r,'_bounded_docker',side_effect=empty_query))

    def fake_command(self,args,**kwargs):
        import json
        if args[0]=='git' and args[-2:]==['rev-parse','HEAD']:return self.e['CODE_SHA']
        if args[0]=='git' and args[-2:]==['status','--porcelain']:return ''
        if args==['docker','context','inspect','default']:
            return json.dumps([{'Endpoints':{'docker':{'Host':'unix:///var/run/docker.sock'}}}])
        raise AssertionError('Unexpected synthetic command')

    def invoke(self,operation='run'):
        import io,contextlib
        from unittest.mock import patch
        stream=io.StringIO()
        with patch.object(r.sys,'argv',['runner',operation,'--phase','isolation-only']),contextlib.redirect_stderr(stream):
            code=r.main()
        self.assertNotIn('PRIVATE_SECRET',stream.getvalue())
        self.assertNotIn('private@example.invalid',stream.getvalue())
        return code

    def journal(self):
        s=SafetyTests().session()
        s.update(phase='isolation-only',policy_sha256=self.e['CONSTIA_B3_INVENTORY_SHA256'],
                 workflow_sha=self.e['GITHUB_WORKFLOW_SHA'],actor_id=self.e['GITHUB_ACTOR_ID'])
        return s

    def original(self):
        return r.read_diagnostic('isolation-execution.json')

    def test_real_head_guard_identifies_mismatch(self):
        self.command.side_effect=lambda args,**kw:'c'*40
        self.assertEqual(1,self.invoke())
        self.assertEqual('HEAD_MISMATCH',self.original()['first_failure']['reason'])
        self.build.assert_not_called()

    def test_real_worktree_guard_identifies_dirty(self):
        self.command.side_effect=lambda args,**kw:self.e['CODE_SHA'] if args[-1]=='HEAD' else ' M file.cs'
        self.assertEqual(1,self.invoke())
        self.assertEqual('DIRTY',self.original()['first_failure']['reason'])
        self.build.assert_not_called()

    def test_real_inventory_guard_identifies_modified_bytes(self):
        (self.root/r.INVENTORY_PATHS[0]).write_bytes(b'changed')
        self.assertEqual(1,self.invoke())
        self.assertEqual('INVENTORY_REJECTED',self.original()['first_failure']['code'])
        self.build.assert_not_called()

    def test_real_engine_guard_identifies_endpoint_rejection(self):
        fake=self.fake_command
        self.command.side_effect=lambda args,**kw:'[]' if args[0]=='docker' else fake(args,**kw)
        self.assertEqual(1,self.invoke())
        self.assertEqual('ENGINE_REJECTED',self.original()['first_failure']['code'])
        self.build.assert_not_called()

    def test_real_git_oserror_is_fixed_and_private_message_is_not_emitted(self):
        self.command.side_effect=OSError('PRIVATE_SECRET private@example.invalid')
        self.assertEqual(1,self.invoke())
        self.assertEqual('GIT_REJECTED',self.original()['first_failure']['code'])

    def test_matching_preexisting_journal_is_not_mistaken_for_no_resources(self):
        import json
        r.state_path().write_text(json.dumps(self.journal()))
        self.assertEqual(1,self.invoke())
        self.assertEqual('PREEXISTING',self.original()['first_failure']['reason'])
        primary=r.read_diagnostic('isolation-cleanup-primary.json')
        self.assertEqual('uncertain',primary['cleanup'])
        self.assertEqual('unknown',primary['cleanup_scope'])

    def test_foreign_journal_is_never_adopted(self):
        import json
        journal=self.journal();journal['workflow_sha']='c'*40
        raw=json.dumps(journal);r.state_path().write_text(raw)
        self.assertEqual(1,self.invoke())
        self.assertEqual(raw,r.state_path().read_text())
        self.assertEqual('uncertain',r.read_diagnostic('isolation-cleanup-primary.json')['cleanup'])

    def test_truncated_journal_remains_uncertain(self):
        r.state_path().write_text('{"schema":')
        self.assertEqual(1,self.invoke('cleanup'))
        final=r.read_diagnostic('isolation-cleanup-final.json')
        self.assertIsNone(final['creation_attempted'])
        self.assertEqual('uncertain',final['cleanup'])
        self.assertEqual('JOURNAL_REJECTED',final['operation_failure']['code'])

    def test_initial_journal_persistence_failure_does_not_dispatch_creation(self):

        from unittest.mock import patch
        actual=r._publish_immutable_record_fd
        def publish(path,*args):
            if pathlib.Path(path)==r.state_path():raise OSError('PRIVATE_SECRET')
            return actual(path,*args)
        with patch.object(r,'_publish_immutable_record_fd',side_effect=publish):
            self.assertEqual(1,self.invoke())
        self.assertEqual('JOURNAL_WRITE_FAILED',self.original()['first_failure']['code'])
        self.assertFalse(self.original()['creation_attempted']);self.build.assert_not_called()

    def test_intent_write_failure_precedes_dispatch(self):

        from unittest.mock import patch
        actual=r._publish_immutable_record_fd
        def publish(path,*args):
            if pathlib.Path(path)==r.state_path():raise OSError('PRIVATE_SECRET')
            return actual(path,*args)
        with patch.object(r,'_publish_immutable_record_fd',side_effect=publish):
            self.assertEqual(1,self.invoke())
        self.assertEqual('failed_before_creation',self.original()['execution'])
        self.assertFalse(self.original()['creation_attempted'])
        self.assertFalse(any(call.args[0][:2]==['docker','build'] for call in self.command.call_args_list))

    def test_response_loss_after_dispatch_preserves_first_failure_and_uncertainty(self):
        import subprocess
        from unittest.mock import patch
        fake=self.fake_command
        def command(args,**kwargs):
            if args[:2]==['docker','build']:raise subprocess.TimeoutExpired(['synthetic'],1)
            return fake(args,**kwargs)
        self.command.side_effect=command
        self.build.side_effect=lambda root,s,la:r.create_resource(s,'app_image_id',['docker','build','synthetic'])
        with patch.object(r,'lookup',return_value=None):
            self.assertEqual(1,self.invoke())
        original=self.original();primary=r.read_diagnostic('isolation-cleanup-primary.json')
        self.assertEqual('failed_after_creation_attempt',original['execution'])
        self.assertTrue(original['creation_attempted'])
        self.assertEqual('RESOURCE_UNVERIFIED',original['first_failure']['code'])
        self.assertEqual(original['first_failure'],primary['first_failure'])
        self.assertEqual('uncertain',primary['cleanup'])

    def test_corrupt_execution_does_not_skip_cleanup_or_invent_success(self):
        from unittest.mock import patch
        self.command.side_effect=lambda args,**kw:'c'*40
        self.assertEqual(1,self.invoke())
        path=r.diagnostic_directory()/'isolation-execution.json';path.write_text('{"unexpected":true}')
        with patch.object(r,'_cleanup_diagnostic',wraps=r._cleanup_diagnostic) as cleanup:
            self.assertEqual(1,self.invoke('cleanup'));cleanup.assert_called_once()
        final=r.read_diagnostic('isolation-cleanup-final.json')
        self.assertIsNone(final['execution'])
        self.assertEqual('uncertain',final['cleanup'])

    def test_hash_binding_rejects_changed_original_and_foreign_identity(self):
        import json
        self.command.side_effect=lambda args,**kw:'c'*40
        self.assertEqual(1,self.invoke())
        p=r.diagnostic_directory()/'isolation-execution.json'
        rec=json.loads(p.read_text());rec['identity']['run_id']='999';p.write_text(json.dumps(rec))
        with self.assertRaises(r.Unsafe):r.read_diagnostic('isolation-cleanup-primary.json')

    def test_absent_and_corrupt_proof_are_never_L2_validated(self):
        c=r.DiagnosticContext()
        r._candidate_proof(c)
        self.assertEqual('absent',c.proof_state)
        (r.public_directory()/'isolation.json').write_text('{"private":"PRIVATE_SECRET"}')
        r._candidate_proof(c)
        self.assertEqual('invalid',c.proof_state)
        self.assertEqual('incomplete',c.evidence)

    def test_unexpected_artifact_and_symlink_root_checks_fail_closed(self):
        (r.diagnostic_directory()/'unexpected.json').write_text('{}')
        with self.assertRaises(r.Unsafe):r.diagnostic_directory()

    def test_every_observed_stage_failure_maps_to_fixed_diagnostic(self):
        import contextlib,io
        for stage,(code,reason) in r._DIAGNOSTIC_DEFAULTS.items():
            with self.subTest(stage=stage):
                c=r.DiagnosticContext();active=r.ACTIVE_DIAGNOSTIC.set(c)
                try:
                    with contextlib.redirect_stderr(io.StringIO()),self.assertRaises(OSError):
                        r.diagnostic_call(stage,lambda:(_ for _ in ()).throw(OSError('PRIVATE_SECRET')))
                    self.assertEqual(dict(stage=stage,code=code,reason=reason),c.first_failure)
                finally:r.ACTIVE_DIAGNOSTIC.reset(active)

    def test_cleanup_failed_remove_is_distinct_from_uncertain_lookup(self):

        from unittest.mock import patch
        def check(case):
            case.populate(('network_id',));case.witness();actual=case.engine_command
            def command(args,**kw):
                if 'rm' in args:raise OSError('PRIVATE_SECRET')
                return actual(args,**kw)
            with patch.object(r,'command',side_effect=command):case.assertEqual('failed',case.clean())
            record=r.make_diagnostic('cleanup_final',case.c,case.c.witness_sha256)
            r.write_diagnostic('isolation-cleanup-final.json',record)
            case.assertEqual('CLEANUP_FAILED',r.read_diagnostic('isolation-cleanup-final.json')['operation_failure']['code'])
        _with_protocol_fixture(check)



    def test_successful_synthetic_execution_is_only_candidate_evidence(self):
        import json
        def simulated(phase):
            c=r.ACTIVE_DIAGNOSTIC.get();c.creation_attempted=True
            _simulate_manifest_dispatch(self.journal())
            report=IsolationPhaseTests().report()
            report['identity']['policy_sha256']=self.e['CONSTIA_B3_INVENTORY_SHA256']
            (r.public_directory()/'isolation.json').write_text(json.dumps(report))
        self.build.side_effect=None
        from unittest.mock import patch
        with patch.object(r,'run_isolation',side_effect=simulated):
            self.assertEqual(0,self.invoke())
        original=self.original()
        self.assertEqual('completed',original['execution'])
        self.assertEqual('candidate_valid',original['proof_state'])
        self.assertEqual('incomplete',original['evidence'])
        self.assertEqual('verified',r.read_diagnostic('isolation-cleanup-primary.json')['cleanup'])
        self.assertEqual(0,self.invoke('cleanup'))
        self.assertEqual('completed',r.read_diagnostic('isolation-cleanup-final.json')['execution'])

    def test_false_cleanup_result_cannot_certify_cleanup(self):
        from unittest.mock import patch
        self.command.side_effect=lambda args,**kw:'c'*40
        with patch.object(r,'cleanup',return_value=False):
            self.assertEqual(1,self.invoke())
        self.assertNotEqual('not_applicable',r.read_diagnostic('isolation-cleanup-primary.json')['cleanup'])
        self.assertNotEqual('verified',r.read_diagnostic('isolation-cleanup-primary.json')['cleanup'])

    def test_existing_execution_blocks_repeat_run_before_dispatch(self):
        from unittest.mock import patch
        self.command.side_effect=lambda args,**kw:'c'*40
        self.assertEqual(1,self.invoke())
        original=(r.diagnostic_directory()/'isolation-execution.json').read_bytes()
        with patch.object(r,'run_isolation',side_effect=AssertionError('Repeat run forbidden')) as run:
            self.assertEqual(1,self.invoke());run.assert_not_called()
        self.assertEqual(original,(r.diagnostic_directory()/'isolation-execution.json').read_bytes())



    def test_creation_error_precedes_failed_reconciliation_persistence(self):

        import subprocess
        from unittest.mock import patch
        def check(case):
            r._admit_session(case.s);actual=r._observe_session;events=[]
            def observe(state):
                events.append(True)
                if len(events)>1:raise OSError('PRIVATE_SECRET')
                return actual(state)
            with patch.object(r,'_observe_session',side_effect=observe),patch.object(r,'command',side_effect=subprocess.TimeoutExpired(['synthetic'],1)):
                with case.assertRaises(subprocess.TimeoutExpired):r.create_resource(case.s,'network_id',['docker','network','create','synthetic'])
            case.assertTrue(case.c.creation_attempted)
            case.assertEqual('RESOURCE_UNVERIFIED',case.c.first_failure['code'])
        _with_protocol_fixture(check)

    def test_corrupt_primary_does_not_erase_valid_execution_or_create_bad_binding(self):
        self.command.side_effect=lambda args,**kw:'c'*40
        self.assertEqual(1,self.invoke())
        original=self.original()
        (r.diagnostic_directory()/'isolation-cleanup-primary.json').write_text('{')
        self.assertEqual(1,self.invoke('cleanup'))
        final=r.read_diagnostic('isolation-cleanup-final.json')
        self.assertEqual(original['execution'],final['execution'])
        self.assertEqual(original['creation_attempted'],final['creation_attempted'])
        self.assertEqual(original['first_failure'],final['first_failure'])

    def test_previous_journal_observation_cannot_become_not_applicable(self):
        import json
        r.state_path().write_text(json.dumps(self.journal()))
        self.assertEqual(1,self.invoke())
        r.state_path().unlink()
        self.assertEqual(1,self.invoke('cleanup'))
        final=r.read_diagnostic('isolation-cleanup-final.json')
        self.assertEqual('uncertain',final['cleanup'])
        self.assertNotEqual('not_applicable',final['cleanup'])

    def test_deleted_proof_is_not_reused_as_current_candidate(self):
        import json
        from unittest.mock import patch
        def simulated(phase):
            r.ACTIVE_DIAGNOSTIC.get().creation_attempted=True
            _simulate_manifest_dispatch(self.journal())
            report=IsolationPhaseTests().report()
            report['identity']['policy_sha256']=self.e['CONSTIA_B3_INVENTORY_SHA256']
            (r.public_directory()/'isolation.json').write_text(json.dumps(report))
        with patch.object(r,'run_isolation',side_effect=simulated):
            self.assertEqual(0,self.invoke())
        (r.public_directory()/'isolation.json').unlink()
        self.invoke('cleanup')
        final=r.read_diagnostic('isolation-cleanup-final.json')
        self.assertNotEqual('candidate_valid',final['proof_state'])
        self.assertIsNone(final['proof_sha256'])



    def test_cleanup_reads_primary_content_and_hash_in_one_snapshot(self):
        from unittest.mock import patch
        self.command.side_effect=lambda args,**kw:'c'*40
        self.assertEqual(1,self.invoke())
        with patch.object(r,'_read_diagnostic',wraps=r._read_diagnostic) as read:
            self.assertEqual(0,self.invoke('cleanup'))
        names=[call.args[0] for call in read.call_args_list]
        self.assertEqual(1,names.count('isolation-cleanup-primary.json'))
        self.assertLessEqual(names.count('isolation-execution.json'),2)



    def test_symlink_journal_observation_never_proves_non_adoption(self):
        from unittest.mock import patch
        c=r.DiagnosticContext();active=r.ACTIVE_DIAGNOSTIC.set(c)
        try:
            with patch.object(r,'_no_link',return_value=False):
                with self.assertRaises(r.Unsafe):r._empty_journal()
            self.assertTrue(c.journal_seen)
        finally:r.ACTIVE_DIAGNOSTIC.reset(active)

    def test_diagnostic_directory_failure_is_not_reported_as_authorization_rejection(self):
        import io,contextlib
        from unittest.mock import patch
        (r.diagnostic_directory()/'unexpected.json').write_text('{}')
        stream=io.StringIO()
        with patch.object(r.sys,'argv',['runner','run','--phase','isolation-only']),contextlib.redirect_stderr(stream):
            self.assertEqual(1,r.main())
        self.assertIn('EVIDENCE_INVALID',stream.getvalue())
        self.assertNotIn('AUTH_REJECTED',stream.getvalue())



class B3RegressionTests(unittest.TestCase):
    setUp = H2SyntheticIntegrationTests.setUp
    fake_command = H2SyntheticIntegrationTests.fake_command
    invoke = H2SyntheticIntegrationTests.invoke
    journal = H2SyntheticIntegrationTests.journal
    original = H2SyntheticIntegrationTests.original

    def test_b3_journal_disappears_inside_cleanup_is_uncertain(self):
        from unittest.mock import patch
        def check(case):
            case.populate(('network_id',));case.witness()
            actual=r.cleanup
            def vanished(*args,**kwargs):
                r.state_path().unlink()
                return actual(*args,**kwargs)
            with patch.object(r,'cleanup',side_effect=vanished) as attempt:
                status=case.clean()
            attempt.assert_called_once()
            case.assertEqual('uncertain',status)
            case.assertTrue(case.c.journal_seen)
            case.assertIsNotNone(case.c.operation_failure)
            case.assertEqual([],case.removals)
            case.assertTrue(case.objects)
        _with_protocol_fixture(check)

    def broken_channel_case(self, error):
        import io,json,contextlib
        from unittest.mock import patch
        class Broken:
            def write(self,text):raise error
            def flush(self):pass
        def simulated(phase):
            _simulate_manifest_dispatch(self.journal())
        with patch.object(r,'run_isolation',side_effect=simulated), \
             patch.object(r,'_candidate_proof',side_effect=OSError('PRIVATE_SECRET')), \
             patch.object(r,'_cleanup_diagnostic',wraps=r._cleanup_diagnostic) as cleanup, \
             patch.object(r.sys,'argv',['runner','run','--phase',r.ISOLATION]), \
             patch.object(r.sys,'stderr',Broken()):
            try:result=r.main()
            except Exception as ex:self.fail('Diagnostic emission escaped: '+type(ex).__name__)
        self.assertEqual(1,result)
        cleanup.assert_called_once()
        rec=self.original()
        self.assertEqual('EVIDENCE_INVALID',rec['first_failure']['code'])
        self.assertEqual('completed',rec['execution'])
        self.assertNotIn('PRIVATE_SECRET',str(rec))

    def test_b3_broken_pipe_does_not_skip_cleanup(self):
        self.broken_channel_case(BrokenPipeError('PRIVATE_SECRET'))

    def test_b3_oserror_logging_does_not_skip_cleanup(self):
        self.broken_channel_case(OSError('PRIVATE_SECRET'))

    def test_b3_ownership_failure_precedes_later_persistence_failure(self):

        from unittest.mock import patch
        def check(case):
            r._admit_session(case.s)
            foreign=dict(Id='sha256:'+'e'*64,RepoTags=[r.resource_specs(case.s)['app_image_id'][1]],Config=dict(Labels={'constia.owner':'foreign'}))
            with patch.object(r,'command',return_value='sha256:'+'e'*64),patch.object(r,'lookup',return_value=foreign):
                with case.assertRaises(r.Unsafe):r.create_resource(case.s,'app_image_id',['docker','build','synthetic'])
            r._freeze_producer(case.c)
            first=copy.deepcopy(case.c.first_failure)
            case.c.execution='failed_after_creation_attempt'
            with patch.object(r,'write_diagnostic',side_effect=OSError('PRIVATE_SECRET')):
                case.assertIsNone(r._try_write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',case.c),case.c))
            case.assertTrue(case.c.creation_attempted);case.assertEqual(first,case.c.first_failure)
            case.assertEqual('RESOURCE_UNVERIFIED',first['code'])
        _with_protocol_fixture(check)

    def seed_linked_records(self, contradictory=False):
        import copy,hashlib
        c=r.DiagnosticContext();c.execution='failed_before_creation'
        c.first_failure=dict(stage='git_head',code='GIT_REJECTED',reason='HEAD_MISMATCH')
        original=r.make_diagnostic('execution',c)
        execution_hash=r.write_diagnostic('isolation-execution.json',original)
        primary=r.make_diagnostic('cleanup_primary',c,execution_hash)
        if contradictory:
            primary['first_failure']=dict(stage='git_status',code='GIT_REJECTED',reason='DIRTY')
        primary_hash=r.write_diagnostic('isolation-cleanup-primary.json',primary)
        final=r.make_diagnostic('cleanup_final',c,execution_hash,primary_hash)
        r.write_diagnostic('isolation-cleanup-final.json',final)

    def test_b3_final_rejects_semantically_invalid_primary_with_correct_hash(self):
        self.seed_linked_records(contradictory=True)
        with self.assertRaises(r.Unsafe):r.read_diagnostic('isolation-cleanup-primary.json')
        with self.assertRaises(r.Unsafe):r.read_diagnostic('isolation-cleanup-final.json')

    def test_b3_final_accepts_valid_primary_chain(self):
        self.seed_linked_records()
        final=r.read_diagnostic('isolation-cleanup-final.json')
        self.assertEqual('HEAD_MISMATCH',final['first_failure']['reason'])

    def seed_partial_publication(self):
        from unittest.mock import patch
        original_write=r.write_diagnostic
        def write(name,record):
            if name=='isolation-execution.json':raise OSError('PRIVATE_SECRET')
            return original_write(name,record)
        self.command.side_effect=lambda args,**kw:'c'*40
        with patch.object(r,'write_diagnostic',side_effect=write):
            self.assertEqual(1,self.invoke())
        return r.read_diagnostic('isolation-cleanup-primary.json')

    def test_b3_final_recovers_available_first_failure_and_unavailable_state(self):
        primary=self.seed_partial_publication()
        self.assertEqual('unavailable',primary['evidence'])
        self.assertFalse((r.diagnostic_directory()/'isolation-execution.json').exists())
        before=(r.diagnostic_directory()/'isolation-cleanup-primary.json').read_bytes()
        self.invoke('cleanup')
        final=r.read_diagnostic('isolation-cleanup-final.json')
        self.assertEqual(primary['first_failure'],final['first_failure'])
        self.assertEqual(primary['execution'],final['execution'])
        self.assertEqual(primary['creation_attempted'],final['creation_attempted'])
        self.assertEqual('unavailable',final['evidence'])
        self.assertEqual(before,(r.diagnostic_directory()/'isolation-cleanup-primary.json').read_bytes())

    def test_b3_corrupt_primary_is_not_used_as_recovered_snapshot(self):
        self.seed_partial_publication()
        (r.diagnostic_directory()/'isolation-cleanup-primary.json').write_text('{"schema":1,"schema":1}')
        self.assertEqual(1,self.invoke('cleanup'))
        final=r.read_diagnostic('isolation-cleanup-final.json')
        self.assertIsNone(final['execution'])
        self.assertIsNone(final['first_failure'])
        self.assertIsNone(final['primary_sha256'])
        self.assertEqual('EVIDENCE_INVALID',final['operation_failure']['code'])



# B4JournalBindingTests: mutable-generation fixtures replaced by schema3 protocol coverage R01-R24.


# B5JournalPersistenceTests: mutable-generation fixtures replaced by schema3 protocol coverage R01-R24.


# B6JournalConsistencyTests: mutable-generation fixtures replaced by schema3 protocol coverage R01-R24.


class B9RedRegressionTests(unittest.TestCase):
    setUp = H2SyntheticIntegrationTests.setUp
    fake_command = H2SyntheticIntegrationTests.fake_command
    journal = H2SyntheticIntegrationTests.journal

    def test_R01_legacy_tail_loss_after_restart_never_certifies(self):
        import json,hashlib,contextlib,io,importlib.util
        from unittest.mock import patch
        s=self.journal();p=r.state_path();p.write_text(json.dumps(s))
        owned=copy.deepcopy(s);owned['network_id']='e'*64
        owned['planned']['network_id']=dict(kind='network',name=s['network_name'],phase='owned',id='e'*64)
        # Lost v1/v2 leave the actual old anchor, while a resource remains alive.
        c=r.DiagnosticContext();c.execution='failed_after_creation_attempt';c.creation_attempted=True
        c.journal_seen=True;c.journal_state='valid'
        c.first_failure=dict(stage='connectivity',code='CONNECTIVITY_UNVERIFIED',reason='CHECK_FAILED')
        r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',c))
        spec=importlib.util.spec_from_file_location('fresh_b9',pathlib.Path(__file__).with_name('c3c02b_runner.py'))
        fresh=importlib.util.module_from_spec(spec);spec.loader.exec_module(fresh)
        with _model_publication(fresh),patch.object(fresh.sys,'argv',['runner','cleanup','--phase','isolation-only']), \
             patch.object(fresh,'lookup',return_value={'Id':'e'*64}),patch.object(fresh,'command',return_value=''), \
             patch.object(fresh.subprocess,'run',side_effect=AssertionError('No native processes')), \
             contextlib.redirect_stderr(io.StringIO()):
            fresh.main()
            result=fresh.read_diagnostic('isolation-cleanup-final.json')
        self.assertNotEqual('verified',result['cleanup'])

    def test_R13_published_hash_never_refers_to_substituted_bytes(self):
        import hashlib
        from unittest.mock import patch
        c=r.DiagnosticContext();record=r.make_diagnostic('execution',c);actual=r.os.link
        foreign=b'FOREIGN_SYNTHETIC_EVIDENCE';injected=[]
        def replace(source,target,**kw):
            source=pathlib.Path(source);source.unlink();source.write_bytes(foreign);injected.append(source)
            return actual(source,target,**kw)
        error=None;digest=None
        try:
            with patch.object(r.os,'link',side_effect=replace):
                digest=r.write_diagnostic('isolation-execution.json',record)
        except (r.Unsafe,OSError):error=True
        path=r.diagnostic_directory()/'isolation-execution.json'
        self.assertTrue(error or digest==hashlib.sha256(path.read_bytes()).hexdigest())

    def test_R16_failed_publication_never_unlinks_substituted_temporary(self):
        import contextlib, errno, os
        from unittest.mock import patch
        class InjectedLinkFailure(OSError): pass
        c=r.DiagnosticContext();record=r.make_diagnostic('execution',c)
        directory_path=r.diagnostic_directory()
        foreign_path=directory_path/'.isolation-execution.json.tmp'
        foreign=b'FOREIGN_SYNTHETIC_EVIDENCE';injected=[];descriptors=[]
        expected=InjectedLinkFailure('synthetic publication failure')
        original=r._publication_directory
        @contextlib.contextmanager
        def fail_publication(path):
            with original(path) as directory:
                def fail_link(fd,name):
                    self.assertEqual('isolation-execution.json',name)
                    os.fstat(fd)  # The real anonymous descriptor must still be open.
                    descriptors.extend([fd,*getattr(directory,'fds',[])])
                    with foreign_path.open('xb') as output:output.write(foreign)
                    injected.append(foreign_path)
                    raise expected
                with patch.object(directory,'link',side_effect=fail_link):
                    yield directory
        with patch.object(r,'_publication_directory',side_effect=fail_publication):
            with self.assertRaises(InjectedLinkFailure) as caught:
                r.write_diagnostic('isolation-execution.json',record)
        self.assertIs(expected,caught.exception)
        self.assertEqual([foreign_path],injected)
        self.assertTrue(descriptors)
        for fd in descriptors:
            with self.assertRaises(OSError) as closed:os.fstat(fd)
            self.assertEqual(errno.EBADF,closed.exception.errno)
        self.assertEqual(foreign,foreign_path.read_bytes())
        self.assertFalse((directory_path/'isolation-execution.json').exists())


# Native-boundary simulation ONLY in this test file. No production switch.
class _WindowsPublicationModel:
    storage = None
    def __init__(self,module,path):
        self.module=module;self.path=pathlib.Path(path);self.backings={}
        self.bound=(self.path.stat().st_dev,self.path.stat().st_ino)
    def __enter__(self):return self
    def __exit__(self,*unused):pass
    def check(self):
        st=self.path.lstat()
        if self.path.is_symlink() or (st.st_dev,st.st_ino)!=self.bound:raise self.module.Unsafe('Test directory changed')
        if self.path.name=='c3c02b-diagnostics' and any(p.name not in self.module.DIAGNOSTIC_FILES for p in self.path.iterdir()):
            raise self.module.Unsafe('Unexpected test diagnostic')
    def anonymous(self):
        import os,tempfile
        fd,name=tempfile.mkstemp(dir=self.storage);os.close(fd);pathlib.Path(name).unlink()
        fd=os.open(name,os.O_RDWR|os.O_CREAT|os.O_EXCL|os.O_TEMPORARY|os.O_BINARY,0o600)
        self.backings[fd]=pathlib.Path(name)
        return fd
    def stat_fd(self,fd):
        import os,types
        st=os.fstat(fd)
        if fd not in self.backings:return st
        fields={k:getattr(st,k) for k in dir(st) if k.startswith('st_')}
        fields['st_nlink']=0
        return types.SimpleNamespace(**fields)
    def link(self,fd,name):
        import os
        source=self.backings[fd];original=os.fstat(fd)
        os.link(source,self.path/name,follow_symlinks=False)
        # This fixture models the native link, never a production fallback.
        now=source.lstat()
        if (now.st_dev,now.st_ino)==(original.st_dev,original.st_ino):
            source.unlink();self.backings.pop(fd)
    def sync(self):pass
    def stat_name(self,name):return (self.path/name).lstat()

_TEST_NATIVE_STACK=None
def _model_publication(module):
    import contextlib,os
    from unittest.mock import patch
    @contextlib.contextmanager
    def model():
        if os.name!='nt' or not hasattr(module,'_publication_directory'):
            yield;return
        with patch.object(module,'_publication_directory',side_effect=lambda p:_WindowsPublicationModel(module,p)):
            yield
    return model()

def setUpModule():
    import contextlib,tempfile,os
    global _TEST_NATIVE_STACK
    if os.name=='nt' and hasattr(r,'_publication_directory'):
        _TEST_NATIVE_STACK=contextlib.ExitStack()
        _WindowsPublicationModel.storage=_TEST_NATIVE_STACK.enter_context(tempfile.TemporaryDirectory())
        _TEST_NATIVE_STACK.enter_context(_model_publication(r))

def tearDownModule():
    if _TEST_NATIVE_STACK is not None:_TEST_NATIVE_STACK.close()


@unittest.skipUnless(r.sys.platform=='linux','Native Linux filesystem gate pending on Windows')
class B9NativePublicationGateTests(unittest.TestCase):
    setUp=H2SyntheticIntegrationTests.setUp
    fake_command=H2SyntheticIntegrationTests.fake_command

    def test_native_anonymous_link_hash_inode_and_descriptor_close(self):
        import hashlib,os
        from unittest.mock import patch
        seen=[];actual=r._LinuxPublicationDirectory.anonymous
        def anonymous(directory):
            fd=actual(directory);self.assertEqual(0,os.fstat(fd).st_nlink);seen.append(fd);return fd
        with patch.object(r._LinuxPublicationDirectory,'anonymous',anonymous):
            digest=r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        p=r.diagnostic_directory()/'isolation-execution.json'
        self.assertEqual(digest,hashlib.sha256(p.read_bytes()).hexdigest());self.assertEqual(1,p.stat().st_nlink)
        for fd in seen:
            with self.assertRaises(OSError):os.fstat(fd)

    def test_native_existing_destination_is_preserved(self):
        p=r.diagnostic_directory()/'isolation-execution.json';p.write_bytes(b'FOREIGN')
        with self.assertRaises(FileExistsError):r.write_diagnostic(p.name,r.make_diagnostic('execution',r.DiagnosticContext()))
        self.assertEqual(b'FOREIGN',p.read_bytes())

    def test_native_short_writes_are_completed(self):
        from unittest.mock import patch
        actual=r.os.write
        with patch.object(r.os,'write',side_effect=lambda fd,data:actual(fd,data[:7])):
            digest=r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        import hashlib
        self.assertEqual(digest,hashlib.sha256((r.diagnostic_directory()/'isolation-execution.json').read_bytes()).hexdigest())

    def test_native_file_sync_failure_closes_anonymous_fd(self):
        import os
        from unittest.mock import patch
        seen=[];actual=r._LinuxPublicationDirectory.anonymous
        def anonymous(directory):
            fd=actual(directory);seen.append(fd);return fd
        with patch.object(r._LinuxPublicationDirectory,'anonymous',anonymous),patch.object(r.os,'fsync',side_effect=OSError('synthetic')):
            with self.assertRaises(OSError):r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        self.assertFalse((r.diagnostic_directory()/'isolation-execution.json').exists())
        for fd in seen:
            with self.assertRaises(OSError):os.fstat(fd)

    def test_native_failed_link_preserves_foreign_named_temporary(self):
        from unittest.mock import patch
        old=r.diagnostic_directory()/'.isolation-execution.json.tmp'
        def link(directory,fd,name):
            old.write_bytes(b'FOREIGN');raise OSError('synthetic')
        with patch.object(r._LinuxPublicationDirectory,'link',link):
            with self.assertRaises(OSError):r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        self.assertEqual(b'FOREIGN',old.read_bytes())

    def test_native_directory_sync_failure_never_deletes_published_file(self):
        from unittest.mock import patch
        out=r.diagnostic_directory()
        with patch.object(r._LinuxPublicationDirectory,'sync',side_effect=OSError('synthetic')):
            with self.assertRaises(OSError):r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        self.assertTrue((out/'isolation-execution.json').is_file())


class B9ProtocolTests(unittest.TestCase):
    setUpBase=H2SyntheticIntegrationTests.setUp
    fake_command=H2SyntheticIntegrationTests.fake_command
    journal=H2SyntheticIntegrationTests.journal
    def setUp(self):
        import contextlib
        from unittest.mock import patch
        self.setUpBase()
        self.objects={};self.removals=[];self.queries=[];self.enumeration_override=None
        self.command.side_effect=self.engine_command
        self.query_patch=self.stack.enter_context(patch.object(r,'_bounded_docker',side_effect=self.engine_query))
        self.c=r.DiagnosticContext();token=r.ACTIVE_DIAGNOSTIC.set(self.c)
        self.addCleanup(r.ACTIVE_DIAGNOSTIC.reset,token)
        self.s=self.journal()

    def engine_command(self,args,**kw):
        import copy
        if args[:1]==['docker'] and 'rm' in args:
            self.removals.append(args)
            kind=args[1];identifier=args[-1]
            self.objects.pop((kind,identifier),None)
            if kind=='container':
                for (k,_),obj in self.objects.items():
                    if k=='network':obj['Containers'].pop(identifier,None)
            return ''
        return self.fake_command(args,**kw)

    def engine_query(self,args,**kw):
        import subprocess,json
        self.queries.append(list(args));kind=args[1]
        if args[2]=='ls':
            self.assertIn('label=constia.owner=c3c02b',args)
            self.assertIn('label=constia.run='+self.e['GITHUB_RUN_ID'],args)
            self.assertFalse(any('constia.sha=' in x or 'constia.nonce=' in x for x in args))
            if self.enumeration_override is not None:
                ids=self.enumeration_override.get(kind,[])
            else:
                ids=[]
                for (k,identifier),obj in self.objects.items():
                    lab=obj.get('Config',{}).get('Labels',{}) if k in ('image','container') else obj.get('Labels',{})
                    if k==kind and lab.get('constia.owner')=='c3c02b' and lab.get('constia.run')==self.e['GITHUB_RUN_ID']:
                        ids.append(identifier)
            return subprocess.CompletedProcess(args,0,('\n'.join(ids)).encode(),b'')
        self.assertEqual('inspect',args[2]);query=args[3];found=[]
        for (k,identifier),obj in self.objects.items():
            if k==kind and (identifier==query or (query in obj.get('RepoTags',[]) if k=='image' else obj.get('Name','').lstrip('/')==query)):
                found.append(obj)
        if not found:return subprocess.CompletedProcess(args,1,b'[]',('Error: No such object: '+query).encode())
        return subprocess.CompletedProcess(args,0,json.dumps(found).encode(),b'')

    def populate(self,roles=('app_image_id','network_id','app_id','peer_id','denied_id')):
        import copy
        names=r.resource_specs(self.s);lab=r.labels(self.s)
        image='sha256:'+'a'*64;network='e'*64
        rows={
            'app_image_id':dict(Id=image,RepoTags=[names['app_image_id'][1]],Config=dict(Labels=lab.copy())),
            'network_id':dict(Id=network,Name=names['network_id'][1],Labels=lab.copy(),Internal=True,Driver='bridge',
                              EnableIPv6=False,IPAM=dict(Config=[dict(Subnet='172.31.240.0/28',Gateway='172.31.240.1')]),Containers={})}
        for n,key in enumerate(('app_id','peer_id','denied_id'),1):
            identifier=str(n)*64;ip='172.31.240.'+str(n+1)
            rows[key]=dict(Id=identifier,Name='/'+names[key][1],Image=image,Config=dict(Labels=lab.copy(),Env=[]),
                HostConfig=dict(Privileged=False,Binds=None,ReadonlyRootfs=True,NetworkMode=names['network_id'][1],
                    CapDrop=['ALL'],CapAdd=None,PortBindings={},SecurityOpt=['no-new-privileges'],PidMode='private',IpcMode='private'),
                Mounts=[dict(Type='tmpfs')],NetworkSettings=dict(Networks={
                    names['network_id'][1]:dict(NetworkID=network,IPAddress=ip)}))
            if key in roles:rows['network_id']['Containers'][identifier]=dict(Name=names[key][1],IPv4Address=ip+'/28',IPv6Address='')
        self.rows=rows
        for key in roles:self.objects[(names[key][0],rows[key]['Id'])]=copy.deepcopy(rows[key])

    def witness(self,state='quiescent'):
        self.ctrl=r._admit_session(self.s)
        if state!='no_dispatch':
            roles=[key for key in r.resource_specs(self.s) if key in getattr(self,'rows',{}) and
                   (r.resource_specs(self.s)[key][0],self.rows[key]['Id']) in self.objects] or ['network_id']
            for key in roles:
                self.ctrl.begin(self.s,key)
                if state!='unresolved':self.ctrl.acknowledge(key)
        r._freeze_producer(self.c)
        if state=='unknown':self.c.session['producer_state']='unknown'
        self.c.execution='failed_after_creation_attempt' if self.c.creation_attempted else 'failed_before_creation'
        self.c.first_failure=dict(stage='connectivity',code='CONNECTIVITY_UNVERIFIED',reason='CHECK_FAILED')

        self.c.witness_sha256=r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',self.c))
        return self.ctrl

    def clean(self):
        import io,contextlib
        with contextlib.redirect_stderr(io.StringIO()) as output:r._cleanup_diagnostic(self.c)
        self.assertNotIn('PRIVATE_SECRET',output.getvalue())
        return self.c.cleanup

    def fresh_case(self,callback):
        case=B9ProtocolTests('test_R03_missing_or_wrong_binding')
        case.setUp()
        try:callback(case)
        finally:case.doCleanups()

    def test_R01_restart_does_not_lose_potential_resources(self):
        import importlib.util,contextlib,io
        from unittest.mock import patch
        self.populate(('network_id',));self.witness()
        # Legacy hints disappear; schema3 remains complete.
        p=r.state_path();hint=p.with_name(p.name+'.legacy.v0001.json');hint.write_text('{}');hint.unlink()
        spec=importlib.util.spec_from_file_location('fresh_protocol',pathlib.Path(__file__).with_name('c3c02b_runner.py'))
        fresh=importlib.util.module_from_spec(spec);spec.loader.exec_module(fresh)
        with _model_publication(fresh),patch.object(fresh,'_bounded_docker',side_effect=self.engine_query), \
             patch.object(fresh,'command',side_effect=self.engine_command),patch.object(fresh.sys,'argv',['runner','cleanup','--phase','isolation-only']), \
             contextlib.redirect_stderr(io.StringIO()):
            code=fresh.main();final=fresh.read_diagnostic('isolation-cleanup-final.json')
        self.assertEqual(0,code);self.assertEqual('verified',final['cleanup'])
        self.assertEqual([],list(self.objects));self.assertTrue(self.queries)
        self.assertEqual([['docker','network','rm','e'*64]],self.removals)
        self.assertEqual(self.c.first_failure,final['first_failure'])

    def test_R02_partial_hint_loss_cannot_shrink_universe(self):
        self.populate(('app_image_id','network_id'));self.witness()
        p=r.state_path();p.with_name(p.name+'.legacy.v0001.json').write_text('partial legacy observation')
        self.assertEqual('verified',self.clean());self.assertEqual(2,len(self.removals))
        manifest,_,_=r._read_manifest();self.assertEqual(5,len(manifest['resources']))

    def test_R03_missing_or_wrong_binding(self):
        import json
        def scenario(case,kind):
            case.populate(('network_id',));case.witness()
            if kind=='missing_manifest':r.state_path().unlink()
            elif kind=='missing_witness':(r.diagnostic_directory()/'isolation-execution.json').unlink()
            elif kind=='truncated':r.state_path().write_text('{')
            elif kind=='nonce':
                m,_,_=r._read_manifest();m['nonce']='a'*32;r.state_path().write_text(json.dumps(m))
            else:case.c.session['manifest_sha256']='b'*64
            case.assertNotEqual('verified',case.clean());case.assertEqual([],case.removals)
        for kind in ('missing_manifest','missing_witness','truncated','nonce','digest'):
            with self.subTest(case=kind):self.fresh_case(lambda case:scenario(case,kind))

    def test_R04_query_error_is_not_absence(self):
        self.populate(('network_id',));self.witness()
        self.query_patch.side_effect=OSError('PRIVATE_SECRET')
        self.assertNotEqual('verified',self.clean());self.assertEqual([],self.removals)
        self.assertTrue(self.objects)

    def test_R05_reserved_names_find_objects_omitted_by_listing(self):
        self.populate(('network_id',));self.witness();self.enumeration_override={}
        self.assertEqual('verified',self.clean());self.assertEqual(1,len(self.removals))
        self.assertTrue(any(x[2]=='inspect' and x[3]==self.s['network_name'] for x in self.queries))

    def test_R06_excess_malformed_and_failed_enumerations_reject(self):
        import subprocess
        def scenario(case,mode):
            case.populate(('network_id',));case.witness()
            if mode=='excess':case.enumeration_override={'network':[str(n)*64 for n in range(1,7)]}
            else:
                case.query_patch.side_effect=lambda args,**kw:subprocess.CompletedProcess(args,1 if mode=='failed' else 0,b'not-a-full-id',b'')
            case.assertNotEqual('verified',case.clean());case.assertEqual([],case.removals)
        for mode in ('excess','malformed','failed'):
            with self.subTest(mode=mode):self.fresh_case(lambda case:scenario(case,mode))

    def test_R07_original_ownership_and_configuration_guards_remain(self):
        def scenario(case,field):
            case.populate();case.witness();obj=case.objects[('container','1'*64)]
            if field=='labels':obj['Config']['Labels']['constia.sha']='f'*40
            elif field=='mount':obj['HostConfig']['Binds']=['/host:/host']
            elif field=='caps':obj['HostConfig']['CapDrop']=[]
            elif field=='image':obj['Image']='sha256:'+'f'*64

            elif field=='network':obj['NetworkSettings']['Networks'][case.s['network_name']]['NetworkID']='f'*64
            else:obj['HostConfig']['PortBindings']={'80/tcp':[{'HostPort':'80'}]}
            case.assertNotEqual('verified',case.clean());case.assertEqual([],case.removals)
        for field in ('labels','mount','caps','image','network','port'):
            with self.subTest(field=field):self.fresh_case(lambda case:scenario(case,field))

    def test_R08_bindings_are_rebuilt_only_from_owned_objects(self):
        self.populate();self.witness()
        self.assertNotIn('app_image_id',self.s)
        self.assertEqual('verified',self.clean());self.assertEqual(5,len(self.removals))
        self.assertEqual({},self.objects)

    def test_R09_closed_producer_cannot_dispatch(self):
        self.witness();before=list(self.command.call_args_list)
        with self.assertRaises(r.Unsafe):r.create_resource(self.s,'network_id',['docker','network','create','forbidden'])
        self.assertEqual(before,self.command.call_args_list)
        self.assertFalse(self.ctrl.active)

    def test_R10_unresolved_never_improves_after_empty_list_or_late_creation(self):
        self.witness('unresolved')
        self.assertNotEqual('verified',self.clean())
        self.populate(('network_id',))
        self.assertNotEqual('verified',self.clean())
        self.assertEqual('unresolved',self.c.session['producer_state'])
        self.assertFalse(self.ctrl.active)

    def test_R11_connectivity_failure_can_have_verified_bounded_cleanup(self):
        self.populate();self.witness()
        self.assertEqual('verified',self.clean())
        self.assertEqual('CONNECTIVITY_UNVERIFIED',self.c.first_failure['code'])
        self.assertEqual('managed_session_resources',self.c.cleanup_scope)
        self.assertEqual(5,len(self.removals))

    def test_R12_repeated_cleanup_rechecks_absence_without_repeating_remove(self):
        self.populate(('network_id',));self.witness()
        self.assertEqual('verified',self.clean());count=len(self.queries)
        self.assertEqual('verified',self.clean());self.assertEqual(1,len(self.removals))
        self.assertGreater(len(self.queries),count)

    def test_R13_no_named_temporary_cleanup_operation(self):
        from unittest.mock import patch
        record=r.make_diagnostic('execution',r.DiagnosticContext())
        cls=_WindowsPublicationModel if r.sys.platform!='linux' else r._LinuxPublicationDirectory
        actual=cls.anonymous
        def anonymous(directory):
            fd=actual(directory)
            blocker=patch.object(r.os,'unlink',side_effect=AssertionError('Production unlink forbidden'))
            blocker.start();self.addCleanup(blocker.stop)
            return fd
        with patch.object(cls,'anonymous',anonymous),patch.object(r.os,'write',side_effect=OSError('synthetic')):
            with self.assertRaises(OSError):r.write_diagnostic('isolation-execution.json',record)
        self.assertFalse((r.diagnostic_directory()/'isolation-execution.json').exists())

    def test_R14_existing_destination_is_never_replaced(self):
        p=r.diagnostic_directory()/'isolation-execution.json';p.write_bytes(b'FOREIGN')
        with self.assertRaises(FileExistsError):r.write_diagnostic(p.name,r.make_diagnostic('execution',r.DiagnosticContext()))
        self.assertEqual(b'FOREIGN',p.read_bytes())

    def test_R15_short_writes_publish_exact_descriptor_bytes(self):
        import hashlib
        from unittest.mock import patch
        actual=r.os.write
        with patch.object(r.os,'write',side_effect=lambda fd,data:actual(fd,data[:11])):
            digest=r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        self.assertEqual(digest,hashlib.sha256((r.diagnostic_directory()/'isolation-execution.json').read_bytes()).hexdigest())

    def test_R16_file_directory_sync_and_read_failures_never_return_hash(self):
        from unittest.mock import patch
        def scenario(case,stage):
            record=r.make_diagnostic('execution',r.DiagnosticContext())
            if stage=='file':context=patch.object(r.os,'fsync',side_effect=OSError('synthetic'))
            elif stage=='read':context=patch.object(r.os,'read',side_effect=OSError('synthetic'))
            else:
                cls=_WindowsPublicationModel if r.sys.platform!='linux' else r._LinuxPublicationDirectory
                context=patch.object(cls,'sync',side_effect=OSError('synthetic'))
            with context:
                with case.assertRaises(OSError):r.write_diagnostic('isolation-execution.json',record)
        for stage in ('file','read','directory'):
            with self.subTest(stage=stage):self.fresh_case(lambda case:scenario(case,stage))

    def test_R17_publication_descriptors_are_closed(self):
        import os
        from unittest.mock import patch
        cls=_WindowsPublicationModel if r.sys.platform!='linux' else r._LinuxPublicationDirectory
        actual=cls.anonymous;seen=[]
        def anonymous(directory):

            fd=actual(directory);self.assertFalse(os.get_inheritable(fd));seen.append(fd);return fd
        with patch.object(cls,'anonymous',anonymous):
            r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        for fd in seen:
            with self.assertRaises(OSError):os.fstat(fd)

    def test_publication_directory_attempts_all_closes_after_one_error(self):
        from unittest.mock import patch
        directory=r._LinuxPublicationDirectory.__new__(r._LinuxPublicationDirectory)
        directory.fds=[101,102,103]
        attempted=[]
        def close(fd):
            attempted.append(fd)
            if fd==103:raise OSError('synthetic close failure')
        with patch.object(r.os,'close',side_effect=close):
            with self.assertRaises(OSError):directory.close()
        self.assertEqual([103,102,101],attempted)
        self.assertEqual([],directory.fds)

    def test_bounded_query_closes_pipes_even_when_reaping_fails(self):
        import io,subprocess
        from unittest.mock import Mock,patch
        process=Mock()
        process.stdout=io.BytesIO();process.stderr=io.BytesIO()
        process.poll.return_value=None
        process.wait.side_effect=subprocess.TimeoutExpired('synthetic',2)
        with patch.object(r.sys,'platform','linux'),patch.object(r.subprocess,'Popen',return_value=process), \
             patch('selectors.DefaultSelector',side_effect=OSError('synthetic')):
            with self.assertRaises(Exception):
                _NATIVE_BOUNDED_QUERY(['docker','network','ls'])
        self.assertTrue(process.stdout.closed)
        self.assertTrue(process.stderr.closed)

    def test_bounded_query_rejects_output_excess_and_timeouts(self):
        import types
        from unittest.mock import Mock,patch
        for mode in ('bytes','no_events','deadline'):
            with self.subTest(mode=mode):
                process=Mock();process.stdout.fileno.return_value=101;process.stderr.fileno.return_value=102
                process.poll.return_value=0
                selector=Mock()
                selector.__enter__=Mock(return_value=selector)
                selector.__exit__=Mock(return_value=False)
                selector.get_map.return_value={101:True,102:True}
                selector.select.return_value=[] if mode=='no_events' else [(types.SimpleNamespace(fileobj=process.stdout),1)]
                times=[0,16] if mode=='deadline' else [0,1]
                with patch.object(r.sys,'platform','linux'),patch.object(r.subprocess,'Popen',return_value=process), \
                     patch('selectors.DefaultSelector',return_value=selector),patch.object(r.os,'set_blocking'), \
                     patch.object(r.os,'read',return_value=b'X'*9),patch('time.monotonic',side_effect=times):
                    with self.assertRaises(r.Unsafe):_NATIVE_BOUNDED_QUERY(['docker','network','ls'],limit=8)
                process.stdout.close.assert_called_once()
                process.stderr.close.assert_called_once()

    def test_R18_schema_session_transitive_binding_and_no_L2_claim(self):
        self.witness()
        primary=r.make_diagnostic('cleanup_primary',self.c,self.c.witness_sha256)
        primary['session']['nonce']='a'*32
        r.write_diagnostic('isolation-cleanup-primary.json',primary)
        with self.assertRaises(r.Unsafe):r.read_diagnostic('isolation-cleanup-primary.json')
        for key,value in (('schema',1),('evidence','complete_validated')):
            record=r.make_diagnostic('execution',self.c);record[key]=value
            with self.assertRaises(r.Unsafe):r.validate_diagnostic(record)

    def test_R19_stderr_failure_does_not_skip_cleanup_or_leak_exception(self):
        import json,io,contextlib
        from unittest.mock import patch
        class Broken:
            def write(self,*unused):raise BrokenPipeError()
        def fail(phase):
            self.c=r.ACTIVE_DIAGNOSTIC.get();self.s=self.journal();self.populate()
            self.witness()
            # witness() already emits execution; remove only this fixture artifact
            # so main exercises its own immutable publication.
            (r.diagnostic_directory()/'isolation-execution.json').unlink()
            raise OSError('PRIVATE_SECRET')
        # Use a separate run context; no pre-existing session.
        with patch.object(r,'run_isolation',side_effect=fail),patch.object(r.sys,'argv',['runner','run','--phase','isolation-only']), \
             patch.object(r.sys,'stderr',Broken()):
            code=r.main()
        self.assertEqual(1,code);self.assertEqual(5,len(self.removals))
        for p in r.diagnostic_directory().iterdir():self.assertNotIn('PRIVATE_SECRET',p.read_text())

    def test_manifest_replacement_immediately_before_remove_preserves_resource(self):
        from unittest.mock import patch
        self.populate(('network_id',));self.witness()
        actual=r.verify_owned;calls=[]
        def verify(*args):
            result=actual(*args);calls.append(result)
            if len(calls)==2:
                p=r.state_path();p.write_bytes(p.read_bytes()+b' ')
            return result
        with patch.object(r,'verify_owned',side_effect=verify):
            self.assertEqual('uncertain',self.clean())
        self.assertEqual(2,len(calls))
        self.assertEqual([],self.removals)
        self.assertTrue(self.objects)

    def test_closed_unresolved_producer_cannot_accept_a_late_ack(self):
        self.witness('unresolved')
        with self.assertRaises(r.Unsafe):self.ctrl.acknowledge('network_id')
        self.assertEqual('unresolved',self.ctrl.freeze())
        self.assertEqual('unresolved',self.c.session['producer_state'])

    def test_corrupted_descriptor_bytes_are_not_published(self):
        from unittest.mock import patch
        actual=r.os.write
        with patch.object(r.os,'write',side_effect=lambda fd,data:actual(fd,b'X'+data[1:])):
            with self.assertRaises(r.Unsafe):
                r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        self.assertFalse((r.diagnostic_directory()/'isolation-execution.json').exists())

    def test_post_link_association_failure_preserves_foreign_destination(self):
        from unittest.mock import patch
        cls=_WindowsPublicationModel if r.sys.platform!='linux' else r._LinuxPublicationDirectory
        actual=cls.stat_name
        def switched(directory,name):
            p=directory.path/name;p.unlink();p.write_bytes(b'FOREIGN')
            return actual(directory,name)
        with patch.object(cls,'stat_name',switched):
            with self.assertRaises(r.Unsafe):
                r.write_diagnostic('isolation-execution.json',r.make_diagnostic('execution',r.DiagnosticContext()))
        self.assertEqual(b'FOREIGN',(r.diagnostic_directory()/'isolation-execution.json').read_bytes())

    def test_resource_appearing_during_final_absence_prevents_verified(self):
        from unittest.mock import patch
        self.populate(('network_id',));self.witness()
        actual=r._discover_session;calls=[]
        def discover(*args):
            calls.append(True)
            if len(calls)==2:self.populate(('network_id',))
            return actual(*args)
        with patch.object(r,'_discover_session',side_effect=discover):
            self.assertEqual('uncertain',self.clean())
        self.assertEqual(2,len(calls))
        self.assertTrue(self.objects)

    def test_identical_manifest_bytes_on_replaced_inode_cannot_authorize_remove(self):
        self.populate(('network_id',));self.witness()
        p=r.state_path();raw=p.read_bytes();inode=p.stat().st_ino
        replacement=p.with_name(p.name+'.replacement');replacement.write_bytes(raw);replacement.replace(p)
        self.assertNotEqual(inode,p.stat().st_ino)
        self.assertEqual('uncertain',self.clean())
        self.assertEqual([],self.removals);self.assertTrue(self.objects)
        self.assertEqual(raw,p.read_bytes())

    def test_descriptor_directory_parent_replacement_is_rejected(self):
        import stat,types
        from unittest.mock import patch
        directory=r._LinuxPublicationDirectory.__new__(r._LinuxPublicationDirectory)
        directory.edges=[(101,'protected',102)];directory.path=pathlib.Path('/sample')
        opened=types.SimpleNamespace(st_mode=stat.S_IFDIR|0o700,st_dev=1,st_ino=11)
        replacement=types.SimpleNamespace(st_mode=stat.S_IFDIR|0o700,st_dev=1,st_ino=12)
        with patch.object(r.os,'stat',return_value=replacement),patch.object(r.os,'fstat',return_value=opened):
            with self.assertRaises(r.Unsafe):directory.check()

    def test_R21_environment_cannot_enable_native_backend_on_windows(self):
        if r.sys.platform!='linux':
            with self.assertRaises(r.Unsafe):r._LinuxPublicationDirectory(self.root)
        else:
            with r._LinuxPublicationDirectory(pathlib.Path(self.e['RUNNER_TEMP'])) as d:self.assertIsNotNone(d.fd)

    def test_R22_write_failure_is_not_an_execution_approval(self):
        from unittest.mock import patch
        import contextlib,io
        actual=r.write_diagnostic
        def run(phase):
            self.c=r.ACTIVE_DIAGNOSTIC.get();self.s=self.journal();self.populate(('network_id',))
            self.ctrl=r._admit_session(self.s)
            self.ctrl.begin(self.s,'network_id');self.ctrl.acknowledge('network_id')
        def write(name,record):
            if name=='isolation-cleanup-primary.json':raise OSError('synthetic')
            return actual(name,record)
        with patch.object(r,'run_isolation',side_effect=run),patch.object(r,'write_diagnostic',side_effect=write), \
             patch.object(r.sys,'argv',['runner','run','--phase','isolation-only']),contextlib.redirect_stderr(io.StringIO()):
            code=r.main()
        self.assertEqual(1,code)
        original=r.read_diagnostic('isolation-execution.json')
        self.assertNotEqual('complete_validated',original['evidence'])

    def test_R23_cache_is_outside_scope_but_extra_labelled_image_blocks(self):
        def scenario(case,extra):
            case.populate(('network_id',));case.witness()
            labels=r.labels(case.s) if extra else {}
            case.objects[('image','sha256:'+'f'*64)]=dict(Id='sha256:'+'f'*64,RepoTags=['base-cache'],Config=dict(Labels=labels))
            result=case.clean()
            if extra:case.assertNotEqual('verified',result);case.assertEqual([],case.removals)
            else:case.assertEqual('verified',result);case.assertEqual(1,len(case.removals))
            case.assertIn(('image','sha256:'+'f'*64),case.objects)
        for extra in (False,True):
            with self.subTest(extra=extra):self.fresh_case(lambda case:scenario(case,extra))

    def test_R24_other_revision_nonce_and_run_are_classified_without_deletion(self):
        def scenario(case,field):
            case.populate(('network_id',));case.witness()
            obj=copy.deepcopy(case.objects[('network','e'*64)]);obj['Id']='f'*64;obj['Name']='other-session'
            obj['Labels'][field]='999' if field=='constia.run' else ('f'*40 if field=='constia.sha' else 'a'*32)
            case.objects[('network','f'*64)]=obj
            result=case.clean()
            if field=='constia.run':
                case.assertEqual('verified',result);case.assertEqual(1,len(case.removals))
            else:case.assertNotEqual('verified',result);case.assertEqual([],case.removals)
            case.assertIn(('network','f'*64),case.objects)
        for field in ('constia.sha','constia.nonce','constia.run'):

            with self.subTest(field=field):self.fresh_case(lambda case:scenario(case,field))




def _legacy_plan_fixture(s,key):
    kind,name=r.resource_specs(s)[key]
    s['planned'][key]=dict(kind=kind,name=name,id=None,phase='creating')

def _with_protocol_fixture(callback):
    case=B9ProtocolTests('test_R03_missing_or_wrong_binding')
    case.setUp()
    try:callback(case)
    finally:case.doCleanups()

def _simulate_manifest_dispatch(s):
    ctrl=r._admit_session(s)
    ctrl.begin(s,'network_id');ctrl.acknowledge('network_id')

_NATIVE_BOUNDED_QUERY = r._bounded_docker


_H4_ORIGINAL_COMMAND = r.command
_H4_ORIGINAL_IMAGE_COMMAND = r.image_build_command

class H4ImageDiagnosticTests(unittest.TestCase):
    """Structured signals only; all native processes are blocked by the fixture."""
    def scenario(self, outcome, reason, confirmation=False):
        import contextlib,io,subprocess
        from unittest.mock import patch
        captured={}
        def check(case):
            r._admit_session(case.s)
            original=outcome if isinstance(outcome,Exception) else None
            # Replace the fixture's process blocker only at the actual subprocess boundary.
            with patch.object(r,'image_build_command',_H4_ORIGINAL_IMAGE_COMMAND),patch.object(r,'command',_H4_ORIGINAL_COMMAND), \
                 patch.object(r.subprocess,'run',side_effect=original,
                              return_value=None if original else outcome) as process, \
                 patch.object(r,'checked_command',wraps=r.checked_command) as checked, \
                 contextlib.redirect_stderr(io.StringIO()) as err:
                if confirmation:
                    failure=r.Unsafe('PRIVATE_SECRET confirmation')
                    process.side_effect=None
                    with patch.object(r,'reconcile_resource',side_effect=failure) as reconcile:
                        with case.assertRaises(r.Unsafe) as ex:
                            r.create_resource(case.s,'app_image_id',['docker','build','-'],input=b'tar',timeout=600)
                    case.assertIs(failure,ex.exception);case.assertEqual(2,reconcile.call_count)
                else:
                    expected=type(original) if original else r.Unsafe
                    with case.assertRaises(expected) as ex:
                        r.create_resource(case.s,'app_image_id',['docker','build','-'],input=b'tar',timeout=600)
                    if original:case.assertIs(original,ex.exception)
                process.assert_called_once_with(['docker','--context','default','build','-'],env=None,input=b'tar',capture_output=True,timeout=600)
                checked.assert_called_once_with(['docker','build','-'])
                case.assertEqual(dict(stage='resource_image',code='RESOURCE_UNVERIFIED',reason=reason),case.c.first_failure)
                case.assertNotIn('PRIVATE_SECRET',err.getvalue())
            case.assertTrue(case.c.creation_attempted)
            case.assertIn('app_image_id',case.c.controller.dispatched)
            case.assertEqual(set() if confirmation else {'app_image_id'},case.c.controller.pending)
            case.assertEqual(not confirmation,case.c.controller.ambiguous)
            r._freeze_producer(case.c)
            case.assertEqual('quiescent' if confirmation else 'unresolved',case.c.session['producer_state'])
            case.assertEqual('resource_image' if confirmation else 'journal_write',case.c.last_completed_stage)
            case.c.execution='failed_after_creation_attempt'
            record=r.make_diagnostic('execution',case.c);r.validate_diagnostic(record)
            case.assertNotIn('PRIVATE_SECRET',str(record))
            # A later cleanup diagnostic cannot rewrite the build failure or certify success.
            first=case.c.first_failure.copy();case.c.cleaning=True
            with contextlib.redirect_stderr(io.StringIO()):
                case.c.fail('cleanup','CLEANUP_UNVERIFIED','UNKNOWN')
            case.assertEqual(first,case.c.first_failure)
            case.assertEqual('uncertain',case.c.cleanup)
            captured.update(record)
        _with_protocol_fixture(check)
        return captured

    def test_T01_executable_not_found(self):
        self.scenario(FileNotFoundError('PRIVATE_SECRET'),'BUILD_START_NOT_FOUND')

    def test_T02_process_permission_denied(self):
        self.scenario(PermissionError('PRIVATE_SECRET'),'BUILD_START_DENIED')

    def test_T03_other_process_start_error(self):
        self.scenario(OSError('PRIVATE_SECRET'),'BUILD_START_FAILED')

    def test_T04_nonzero_exit_is_not_daemon_diagnosis(self):
        import subprocess
        self.scenario(subprocess.CompletedProcess([],125,b'PRIVATE_SECRET',b'PRIVATE_SECRET'),'BUILD_EXIT_NONZERO')

    def test_T05_process_timeout_preserves_original_exception(self):
        import subprocess
        self.scenario(subprocess.TimeoutExpired('PRIVATE_SECRET',600,output=b'PRIVATE_SECRET',stderr=b'PRIVATE_SECRET'),'BUILD_TIMEOUT')

    def test_T06_unexpected_process_error(self):
        self.scenario(RuntimeError('PRIVATE_SECRET'),'BUILD_UNEXPECTED_EXCEPTION')

    def test_T07_confirmation_after_acknowledgement(self):
        import subprocess
        self.scenario(subprocess.CompletedProcess([],0,b'image',b'PRIVATE_SECRET'),'IMAGE_CONFIRMATION_FAILED',True)

    def test_T08_preparation_error_retains_historical_fallback(self):
        from unittest.mock import patch
        import contextlib,io
        def check(case):
            r._admit_session(case.s)
            error=FileNotFoundError('PRIVATE_SECRET preparation')
            with patch.object(r,'plan_resource',side_effect=error),contextlib.redirect_stderr(io.StringIO()):
                with case.assertRaises(FileNotFoundError) as ex:
                    r.create_resource(case.s,'app_image_id',['docker','build','-'])
            case.assertIs(error,ex.exception)
            case.assertEqual('UNCONFIRMED',case.c.first_failure['reason'])
            case.assertFalse(case.c.creation_attempted);case.native.assert_not_called()
        _with_protocol_fixture(check)

    def test_T09_validation_is_not_process_start(self):
        from unittest.mock import patch
        import contextlib,io
        def check(case):
            r._admit_session(case.s);error=FileNotFoundError('PRIVATE_SECRET argument validation')
            with patch.object(r,'image_build_command',_H4_ORIGINAL_IMAGE_COMMAND),patch.object(r,'command',_H4_ORIGINAL_COMMAND),patch.object(r,'checked_command',side_effect=error),contextlib.redirect_stderr(io.StringIO()):
                with case.assertRaises(FileNotFoundError) as ex:
                    r.create_resource(case.s,'app_image_id',['docker','build','-'])
            case.assertIs(error,ex.exception);case.native.assert_not_called()
            case.assertEqual('UNCONFIRMED',case.c.first_failure['reason'])
        _with_protocol_fixture(check)

    def test_T10_success_keeps_bytes_and_other_commands_unchanged(self):
        import subprocess
        from unittest.mock import patch
        with patch.object(r.subprocess,'run',return_value=subprocess.CompletedProcess([],0,b' image\n',b'')) as p:
            self.assertEqual('image',r.image_build_command(['docker','build','-'],input=b'tar',timeout=600))
            p.assert_called_once_with(['docker','--context','default','build','-'],env=None,input=b'tar',capture_output=True,timeout=600)

    def test_T11_fallback_and_catalog_are_explicit(self):
        self.assertEqual(('RESOURCE_UNVERIFIED','UNCONFIRMED'),r._DIAGNOSTIC_DEFAULTS['resource_image'])
        with self.assertRaises(r.Unsafe):
            r.DiagnosticContext().fail('resource_image','RESOURCE_UNVERIFIED','PRIVATE_SECRET')


_H5_ORIGINAL_BUILD = r.build_preflight_image


class H5BuildTransportTests(unittest.TestCase):
    """Real tar/gzip bytes; native subprocesses are replaced at their boundary."""
    def context(self, directory):
        import os
        root=pathlib.Path(directory)/'checkout';root.mkdir()
        files={'src/Example.cs':b'class Example {}\n',
               'tests/sandbox/c3c02b/Dockerfile':b'FROM pinned@example\n',
               'global.json':b'{}\n',
               'notes.txt':b'PRIVATE_SECRET not a build input'}
        for name,body in files.items():
            path=root/name;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(body)
            os.utime(path,ns=(1500000000,1500000000))
        selected=('src/Example.cs','tests/sandbox/c3c02b/Dockerfile','global.json')
        import io,tarfile
        expected=io.BytesIO()
        with tarfile.open(fileobj=expected,mode='w') as archive:
            for name in selected:archive.add(root/name,arcname=name,recursive=False)
        return root,files,selected,expected.getvalue()

    def routes(self):
        # Exercise only the existing legacy context/build block, never its blocked
        # SQL preparation. The real public entry point remains tested as rejected.
        import ast,inspect
        node=ast.parse(inspect.getsource(r.operational_full_prepared)).body[0]
        blocks=[n for n in node.body if isinstance(n,ast.With) and
                any(isinstance(x,ast.Call) and isinstance(x.func,ast.Attribute) and
                    x.func.attr=='add' for x in ast.walk(n))]
        self.assertEqual(1,len(blocks))
        function=ast.FunctionDef(name='legacy_context_only',
            args=ast.arguments(posonlyargs=[],args=[ast.arg(arg=n) for n in ('root','s','la','nonce')],
                               kwonlyargs=[],kw_defaults=[],defaults=[]),
            body=[blocks[0]],decorator_list=[])
        module=ast.fix_missing_locations(ast.Module(body=[function],type_ignores=[]))
        namespace=dict(r.__dict__);exec(compile(module,'<legacy-context-only>','exec'),namespace)
        import types
        legacy=types.FunctionType(namespace['legacy_context_only'].__code__,r.__dict__)
        return (_H5_ORIGINAL_BUILD,lambda root,s,la:legacy(root,s,la,s['nonce']))

    def test_gzip_stdin_preserves_pax_tar_both_routes_and_command_contract(self):
        import gzip,io,subprocess,tarfile,tempfile
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as directory:
            root,files,selected,expected=self.context(directory)
            # C60's condition: PAX header plus payload precedes the first file.
            self.assertEqual(b'././@PaxHeader',expected[:100].rstrip(b'\0'))
            self.assertEqual(b'x',expected[156:157])
            self.assertIn(b'mtime=1.5\n',expected[512:1024])
            with self.assertRaises(tarfile.ReadError):
                tarfile.open(fileobj=io.BytesIO(expected[:1024]),mode='r:')
            state=dict(run_id='123',nonce='c'*32,code_sha='a'*40)
            la=['--label','constia.owner=c3c02b','--label','constia.run=123',
                '--label','constia.nonce='+'c'*32,'--label','constia.sha='+'a'*40]
            want=['docker','--context','default','build','--file','tests/sandbox/c3c02b/Dockerfile',
                  *la,'--build-arg','CODE_SHA='+'a'*40,'--tag','c3c02b:'+'c'*32,'-']
            payloads=[]
            for route in self.routes():
                with self.subTest(route=route.__name__),patch.dict(r.os.environ,{'RUNNER_TEMP':directory}), \
                     patch.object(r,'command',return_value='\0'.join(files)+'\0'), \
                     patch.object(r,'create_resource',side_effect=lambda s,k,args,**kw:_H4_ORIGINAL_COMMAND(args,**kw)), \
                     patch.object(r.subprocess,'run',return_value=subprocess.CompletedProcess([],0,b'image',b'')) as process:
                    route(root,state,la)
                    self.assertEqual(1,process.call_count)
                    args,kw=process.call_args
                    self.assertEqual((want,),args)
                    self.assertEqual({'input','timeout','env','capture_output'},set(kw))
                    self.assertEqual(600,kw['timeout']);self.assertIsNone(kw['env'])
                    self.assertTrue(kw['capture_output'])
                    compressed=kw['input'];payloads.append(compressed)
                    # This recognizes gzip magic, NOT a native Buildx execution.
                    self.assertEqual(b'\x1f\x8b\x08',compressed[:3])
                    self.assertEqual(b'\0'*4,compressed[4:8])
                    plain=gzip.decompress(compressed)
                    self.assertEqual(expected,plain)
                    with tarfile.open(fileobj=io.BytesIO(plain),mode='r:') as archive:
                        self.assertEqual(list(selected),archive.getnames())
                        for member in archive:
                            self.assertEqual(files[member.name],archive.extractfile(member).read())
                            self.assertEqual(1.5,member.mtime)
                    self.assertNotIn(b'PRIVATE_SECRET',plain)
            self.assertEqual(2,len(payloads))
            if len(payloads)==2:self.assertEqual(payloads[0],payloads[1])

    def test_compression_error_is_exact_and_prevents_dispatch_in_both_routes(self):
        import gzip,tempfile
        from unittest.mock import patch
        class CompressionFailure(OSError):pass
        with tempfile.TemporaryDirectory() as directory:
            root,files,_,_=self.context(directory)
            failure=CompressionFailure('synthetic compression failure')
            for route in self.routes():
                with self.subTest(route=route.__name__),patch.dict(r.os.environ,{'RUNNER_TEMP':directory}), \
                     patch.object(r,'command',return_value='\0'.join(files)), \
                     patch.object(gzip,'compress',side_effect=failure) as compress, \
                     patch.object(r,'create_resource',side_effect=AssertionError('Dispatch forbidden')) as dispatch, \
                     patch.object(r.subprocess,'run',side_effect=AssertionError('Native process forbidden')) as process:
                    with self.assertRaises(CompressionFailure) as caught:
                        route(root,dict(nonce='c'*32,code_sha='a'*40),[])
                    self.assertIs(failure,caught.exception);self.assertEqual(1,compress.call_count)
                    dispatch.assert_not_called();process.assert_not_called()

    def test_unsafe_context_still_rejected_before_dispatch_in_both_routes(self):
        import tempfile
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as directory:
            for route in self.routes():
                with self.subTest(route=route.__name__),patch.dict(r.os.environ,{'RUNNER_TEMP':directory}), \
                     patch.object(r,'command',return_value='src/.env/private.cs'), \
                     patch.object(r,'create_resource',side_effect=AssertionError('Dispatch forbidden')) as dispatch:
                    with self.assertRaisesRegex(r.Unsafe,'^Unsafe build input\\.$'):
                        route(pathlib.Path(directory),dict(nonce='c'*32,code_sha='a'*40),[])
                    dispatch.assert_not_called()

    def test_transport_repeat_is_deterministic_and_operational_full_stays_blocked(self):
        import tempfile
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as directory:
            path=pathlib.Path(directory)/'context.tar';path.write_bytes(b'exact tar bytes')
            first=r.compressed_build_context(path)
            self.assertEqual(first,r.compressed_build_context(path))
        with patch.object(r.subprocess,'run',side_effect=AssertionError('Native process forbidden')) as process:
            with self.assertRaisesRegex(r.Unsafe,'^operational-full remains blocked;'):
                r.operational_full_prepared()
            process.assert_not_called()

    def test_nonzero_gzip_build_retains_unresolved_protocol_and_negative_diagnostic(self):
        import contextlib,io,subprocess,tempfile,gzip
        from unittest.mock import patch
        def check(case):
            r._admit_session(case.s)
            with tempfile.TemporaryDirectory(dir=r.os.environ['RUNNER_TEMP']) as directory:
                root,files,_,expected=self.context(directory)
                def command(args,**kw):
                    if args[0]=='git':return '\0'.join(files)
                    return _H4_ORIGINAL_COMMAND(args,**kw)
                with patch.object(r,'command',side_effect=command), \
                     patch.object(r,'image_build_command',_H4_ORIGINAL_IMAGE_COMMAND), \
                     patch.object(r.subprocess,'run',return_value=subprocess.CompletedProcess([],125,b'PRIVATE_SECRET',b'PRIVATE_SECRET')) as process, \
                     contextlib.redirect_stderr(io.StringIO()) as stderr:
                    with case.assertRaisesRegex(r.Unsafe,'^Bounded operation failed; raw output withheld\\.$'):
                        _H5_ORIGINAL_BUILD(root,case.s,[])
                    case.assertEqual(1,process.call_count)
                    case.assertEqual(expected,gzip.decompress(process.call_args.kwargs['input']))
                    case.assertNotIn('PRIVATE_SECRET',stderr.getvalue())
                case.assertTrue(case.c.creation_attempted)
                case.assertEqual({'app_image_id'},case.c.controller.pending)
                case.assertEqual({'app_image_id'},case.c.controller.dispatched)
                case.assertTrue(case.c.controller.ambiguous)
                case.assertEqual(dict(stage='resource_image',code='RESOURCE_UNVERIFIED',reason='BUILD_EXIT_NONZERO'),case.c.first_failure)
                r._freeze_producer(case.c)
                case.assertEqual('unresolved',case.c.session['producer_state'])
                case.c.execution='failed_after_creation_attempt'
                record=r.make_diagnostic('execution',case.c);r.validate_diagnostic(record)
                case.assertNotIn('PRIVATE_SECRET',str(record))
                case.assertEqual('journal_write',case.c.last_completed_stage)
                first=case.c.first_failure.copy();case.c.cleaning=True
                with contextlib.redirect_stderr(io.StringIO()):
                    case.c.fail('cleanup','CLEANUP_UNVERIFIED','UNKNOWN')
                case.assertEqual(first,case.c.first_failure)
                case.assertEqual('uncertain',case.c.cleanup)
        _with_protocol_fixture(check)


class C83PidDefaultTests(unittest.TestCase):
    """Pure argv contracts: real isolation AST, no Docker execution."""
    H5_OUTSIDE_ISOLATION_SHA256 = "827fbf5bbbb5a9c8d49eb2dcd832494f99b5ac7a88ebc5a93f8d0d7b809f6771"
    H6_ISOLATION_SHA256 = "7467cfa4248e3d8d6719f6ac8ac7f5dd517d034196a5fd81cbb1294ca19a53a5"
    METADATA = ("GITHUB_ACTIONS", "RUNNER_ENVIRONMENT", "GITHUB_EVENT_NAME", "GITHUB_RUN_ATTEMPT",
                "GITHUB_SHA", "GITHUB_WORKFLOW_SHA", "GITHUB_WORKFLOW_REF", "GITHUB_REF",
                "GITHUB_RUN_ID", "CODE_SHA", "GITHUB_ACTOR_ID", "CONSTIA_B3_APPROVED_ACTOR_ID",
                "CONSTIA_B3_APPROVED_SHA", "C3C02B_PHASE", "CONSTIA_B3_APPROVED_PHASE",
                "CONSTIA_B3_INVENTORY_SHA256")

    def parts(self):
        import ast
        source = pathlib.Path(__file__).with_name("c3c02b_runner.py").read_text(encoding="utf-8")
        function = next(n for n in ast.parse(source).body
                        if isinstance(n, ast.FunctionDef) and n.name == "run_isolation")
        loop = next(n for n in function.body if isinstance(n, ast.For)
                    and isinstance(n.iter, ast.Tuple)
                    and ast.literal_eval(n.iter) == ("app_id", "peer_id", "denied_id"))
        return source, function, loop

    def fixture(self):
        state = SafetyTests().session()
        state.update(phase="isolation-only", app_image_id="sha256:" + "d" * 64)
        return state, IsolationPhaseTests().environment()

    def commands(self, state=None, environment=None):
        import ast
        state, environment = self.fixture() if state is None else (state, environment)
        _, function, loop = self.parts()
        wanted = ("la", "metadata", "inherited")
        assignments = []
        for name in wanted:
            assignments.append(next(n for n in function.body if isinstance(n, ast.Assign)
                                    and any(isinstance(x, ast.Name) and x.id == name for x in n.targets)))
        captured = []
        scope = dict(s=state, e=environment, resource_specs=r.resource_specs,
                     create_resource=lambda s, key, args: captured.append((key, args)))
        # Execute only the unchanged data-building assignments and actual creation loop.
        code = ast.fix_missing_locations(ast.Module(body=[*assignments, loop], type_ignores=[]))
        exec(compile(code, "H6-isolation-argv", "exec"), scope)
        return captured, scope

    def legacy_H5_command(self, state, environment, key):
        # Frozen contract from reviewed H5; intentionally includes the invalid PID pair.
        names = {"app_id": "c3c02b-app-", "peer_id": "c3c02b-peer-", "denied_id": "c3c02b-denied-"}
        labels = {"constia.owner": "c3c02b", "constia.run": state["run_id"],
                  "constia.nonce": state["nonce"], "constia.sha": state["code_sha"]}
        label_args = [x for k, v in labels.items() for x in ("--label", k + "=" + v)]
        inherited = [x for k in self.METADATA for x in ("--env", k + "=" + environment[k])]
        return ["docker", "run", "-d", "--name", names[key] + state["nonce"], *label_args, *inherited,
                "--network", state["network_name"], "--pid", "private", "--ipc", "private",
                "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--read-only",
                "--tmpfs", "/tmp:rw,noexec,nosuid,size=256m",
                "--tmpfs", "/run/c3:rw,noexec,nosuid,size=32m", state["app_image_id"], "sleep", "infinity"]

    def test_reviewed_H5_fixture_contains_the_invalid_PID_pair(self):
        state, environment = self.fixture()
        for key in ("app_id", "peer_id", "denied_id"):
            with self.subTest(role=key):
                old = self.legacy_H5_command(state, environment, key)
                self.assertEqual(1, old.count("--pid"))
                self.assertEqual("private", old[old.index("--pid") + 1])

    def test_C82_red_regression_actual_loop_omits_PID_option(self):
        calls, _ = self.commands()
        self.assertEqual(3, len(calls))
        for key, args in calls:
            with self.subTest(role=key):
                self.assertFalse(any(x == "--pid" or x.startswith("--pid=") for x in args),
                                 "H5 invalid PID literal must be removed, not replaced with host/container")

    def test_exact_H5_arguments_minus_only_the_PID_pair(self):
        state, environment = self.fixture()
        calls, _ = self.commands(state, environment)
        for key, actual in calls:
            with self.subTest(role=key):
                old = self.legacy_H5_command(state, environment, key)
                index = old.index("--pid")
                self.assertEqual(old[:index] + old[index + 2:], actual)

    def test_creation_order_and_distinct_exact_names(self):
        state, environment = self.fixture()
        calls, _ = self.commands(state, environment)
        self.assertEqual(["app_id", "peer_id", "denied_id"], [key for key, _ in calls])
        names = [args[args.index("--name") + 1] for _, args in calls]
        self.assertEqual(["c3c02b-app-" + state["nonce"], "c3c02b-peer-" + state["nonce"],
                          "c3c02b-denied-" + state["nonce"]], names)

    def test_private_network_and_IPC_are_preserved(self):
        import ast
        state, environment = self.fixture()
        calls, _ = self.commands(state, environment)
        for _, args in calls:
            self.assertEqual(state["network_name"], args[args.index("--network") + 1])
            self.assertEqual("private", args[args.index("--ipc") + 1])
        _, function, _ = self.parts()
        creation = next(n for n in ast.walk(function) if isinstance(n, ast.Call)
                        and isinstance(n.func, ast.Name) and n.func.id == "create_resource"
                        and len(n.args) > 1 and isinstance(n.args[1], ast.Constant)
                        and n.args[1].value == "network_id")
        scope = dict(s=state, la=[x for k, v in r.labels(state).items() for x in ("--label", k + "=" + v)])
        args = eval(compile(ast.Expression(creation.args[2]), "H6-network-argv", "eval"), scope)
        self.assertIn("--internal", args)
        self.assertEqual("bridge", args[args.index("--driver") + 1])

    def test_no_published_ports_or_added_privileges(self):
        calls, _ = self.commands()
        forbidden = ("--privileged", "--publish", "--publish-all", "-p", "-P", "--cap-add")
        for _, args in calls:
            self.assertFalse(any(x == key or x.startswith(key + "=") for x in args for key in forbidden))
            self.assertEqual("ALL", args[args.index("--cap-drop") + 1])
            self.assertEqual("no-new-privileges", args[args.index("--security-opt") + 1])

    def test_read_only_root_and_existing_tmpfs_are_preserved(self):
        calls, _ = self.commands()
        for _, args in calls:
            self.assertEqual(1, args.count("--read-only"))
            self.assertEqual(["/tmp:rw,noexec,nosuid,size=256m", "/run/c3:rw,noexec,nosuid,size=32m"],
                             [args[i + 1] for i, value in enumerate(args) if value == "--tmpfs"])
            self.assertNotIn("--mount", args)
            self.assertNotIn("--volume", args)
            self.assertNotIn("-v", args)

    def test_labels_CODE_SHA_environment_image_and_command_are_preserved(self):
        state, environment = self.fixture()
        calls, scope = self.commands(state, environment)
        self.assertEqual(self.METADATA, scope["metadata"])
        for _, args in calls:
            labels = [args[i + 1] for i, value in enumerate(args) if value == "--label"]
            self.assertEqual(["constia.owner=c3c02b", "constia.run=" + state["run_id"],
                              "constia.nonce=" + state["nonce"], "constia.sha=" + state["code_sha"]], labels)
            inherited = [args[i + 1] for i, value in enumerate(args) if value == "--env"]
            self.assertEqual([key + "=" + environment[key] for key in self.METADATA], inherited)
            self.assertIn("CODE_SHA=" + environment["CODE_SHA"], inherited)
            self.assertEqual([state["app_image_id"], "sleep", "infinity"], args[-3:])
            self.assertFalse(any("RESEND" in item or "SQLCONNSTR" in item for item in inherited))

    def test_default_PID_inspect_is_accepted_but_shared_namespaces_are_rejected(self):
        def check(case):
            case.populate()
            state = case.s
            state.update(app_image_id=case.rows["app_image_id"]["Id"],
                         network_id=case.rows["network_id"]["Id"],
                         subnet="172.31.240.0/28", gateway="172.31.240.1")
            _legacy_plan_fixture(state, "app_id")
            obj = copy.deepcopy(case.rows["app_id"])
            obj["HostConfig"]["PidMode"] = ""
            self.assertEqual(obj["Id"], r.verify_owned(state, "app_id", obj))
            for mode in ("host", "container:" + "f" * 64):
                with self.subTest(mode=mode), self.assertRaisesRegex(r.Unsafe, "Host namespaces/privileges"):
                    bad = copy.deepcopy(obj)
                    bad["HostConfig"]["PidMode"] = mode
                    r.verify_owned(state, "app_id", bad)
        _with_protocol_fixture(check)

    def test_only_the_authorized_pair_changes_in_productive_source(self):
        import ast, hashlib
        source, function, _ = self.parts()
        lines = source.splitlines(keepends=True)
        outside = "".join(lines[:function.lineno - 1] + lines[function.end_lineno:])
        self.assertEqual(self.H5_OUTSIDE_ISOLATION_SHA256, hashlib.sha256(outside.encode()).hexdigest())
        body = ast.get_source_segment(source, function)
        self.assertEqual(self.H6_ISOLATION_SHA256, hashlib.sha256(body.encode()).hexdigest())

    def test_creation_failure_stays_unresolved_without_operational_success(self):
        import contextlib, io, subprocess
        from unittest.mock import patch
        def check(case):
            r._admit_session(case.s)
            state = case.s
            state["app_image_id"] = "sha256:" + "d" * 64
            calls, _ = self.commands(state, case.e)
            args = calls[0][1]
            output = io.StringIO()
            result = subprocess.CompletedProcess([], 125, b"PRIVATE_SECRET", b"PRIVATE_SECRET")
            with patch.object(r, "command", _H4_ORIGINAL_COMMAND), \
                 patch.object(r.subprocess, "run", return_value=result) as process, \
                 contextlib.redirect_stderr(output), \
                 self.assertRaisesRegex(r.Unsafe, "Bounded operation failed"):
                r.create_resource(state, "app_id", args)
            process.assert_called_once_with(["docker", "--context", "default", *args[1:]],
                                           env=None, input=None, capture_output=True, timeout=60)
            self.assertEqual(dict(stage="resource_app", code="RESOURCE_UNVERIFIED", reason="UNCONFIRMED"),
                             case.c.first_failure)
            self.assertEqual({"app_id"}, case.c.controller.pending)
            self.assertTrue(case.c.controller.ambiguous)
            r._freeze_producer(case.c)
            self.assertEqual("unresolved", case.c.session["producer_state"])
            self.assertEqual("absent", case.c.proof_state)
            self.assertEqual("uncertain", case.c.cleanup)
            self.assertEqual("unknown", case.c.cleanup_scope)
            self.assertNotIn("PRIVATE_SECRET", output.getvalue())
        _with_protocol_fixture(check)


if __name__ == '__main__': unittest.main()
