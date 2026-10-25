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
        import tempfile
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d:
            p=pathlib.Path(d)/'state.json';p.write_text('{"old":true}')
            with patch.object(r,'state_path',return_value=p), patch.object(r.os,'replace',side_effect=OSError('synthetic')):
                with self.assertRaises(OSError):r.save({'new':True})
            self.assertEqual('{"old":true}',p.read_text())

    def test_cleanup_rejects_foreign_container_before_mutation(self):
        import tempfile,json
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d:
            p=pathlib.Path(d)/'state.json'
            s=self.session()
            s['planned']['app_id']={'kind':'container','name':r.resource_specs(s)['app_id'][1],'id':'d'*64,'phase':'owned'}
            p.write_text(json.dumps(s))
            with patch.object(r,'state_path',return_value=p), patch.object(r,'lookup',return_value={'Id':'d'*64,'Config':{'Labels':{'constia.owner':'other'}}}) as looked, patch.object(r,'command') as cmd:
                with self.assertRaises(r.Unsafe):r.cleanup()
                looked.assert_called_once_with('container','d'*64)
                cmd.assert_not_called()

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
        from unittest.mock import patch
        s=self.session()
        with patch.object(r,'save') as saved:
            r.plan_resource(s,'network_id');saved.assert_called_once_with(s)
        self.assertEqual('creating',s['planned']['network_id']['phase'])
        self.assertEqual('c3c02b-'+s['nonce'],s['planned']['network_id']['name'])
        with self.assertRaises(r.Unsafe):r.plan_resource(s,'unknown')


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
        s=self.session()
        volume='c3c02b-data-'+s['nonce']
        owned={'Name':volume,'Driver':'local','Options':None,'Labels':s['labels']}
        events=[]
        def saved(state): events.append(('saved',state['planned'].get('volume_id',{}).get('phase')))
        def lost(*args,**kwargs):
            self.assertIn(('saved','creating'),events)
            raise subprocess.TimeoutExpired(['docker'],1)
        with patch.object(r,'save',side_effect=saved),patch.object(r,'command',side_effect=lost),patch.object(r,'lookup',return_value=owned):
            with self.assertRaises(subprocess.TimeoutExpired):r.create_resource(s,'volume_id',['docker','volume','create',volume])
        self.assertEqual('owned',s['planned']['volume_id']['phase'])
        self.assertEqual(volume,s['planned']['volume_id']['id'])

    def test_h02_absent_after_timeout_remains_uncertain(self):
        import subprocess
        from unittest.mock import patch
        s=self.session()
        with patch.object(r,'save'),patch.object(r,'command',side_effect=subprocess.TimeoutExpired(['docker'],1)),patch.object(r,'lookup',return_value=None):
            with self.assertRaises(subprocess.TimeoutExpired):r.create_resource(s,'volume_id',['docker','volume','create'])
        self.assertEqual('uncertain',s['planned']['volume_id']['phase'])
        with patch.object(r,'save'),patch.object(r,'lookup',return_value=None):
            with self.assertRaises(r.Unsafe):r.reconcile_resource(s,'volume_id')

    def test_h02_matching_name_with_foreign_labels_is_not_owned(self):
        from unittest.mock import patch
        s=self.session()
        with patch.object(r,'save'):r.plan_resource(s,'volume_id')
        obj={'Name':r.resource_specs(s)['volume_id'][1],'Driver':'local','Options':None,'Labels':{'constia.owner':'elsewhere'}}
        with self.assertRaises(r.Unsafe):r.verify_owned(s,'volume_id',obj)
        obj['Labels']=s['labels'];obj['Options']={'device':'/host/data','o':'bind'}
        with self.assertRaises(r.Unsafe):r.verify_owned(s,'volume_id',obj)

    def test_h02_cleanup_failure_is_not_success(self):
        import json,tempfile
        from unittest.mock import patch
        s=self.session()
        with patch.object(r,'save'):r.plan_resource(s,'volume_id')
        volume=r.resource_specs(s)['volume_id'][1]
        s['planned']['volume_id'].update(id=volume,phase='owned')
        obj={'Name':volume,'Driver':'local','Options':None,'Labels':s['labels']}
        with tempfile.TemporaryDirectory() as d:
            p=pathlib.Path(d)/'state.json';p.write_text(json.dumps(s))
            with patch.object(r,'state_path',return_value=p),patch.object(r,'save'),patch.object(r,'lookup',return_value=obj),patch.object(r,'command',side_effect=r.Unsafe('synthetic')):
                with self.assertRaises(r.Unsafe):r.cleanup()
        self.assertEqual('failed',r.final_status(True,False,True))

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
        import json,tempfile
        from unittest.mock import patch
        s=self.session()
        with patch.object(r,'save'):r.plan_resource(s,'volume_id')
        volume=r.resource_specs(s)['volume_id'][1]
        s['planned']['volume_id'].update(id=volume,phase='owned')
        obj={'Name':volume,'Driver':'local','Options':None,'Labels':s['labels']}
        with tempfile.TemporaryDirectory() as d:
            p=pathlib.Path(d)/'state.json';p.write_text(json.dumps(s))
            with patch.object(r,'state_path',return_value=p),patch.object(r,'lookup',side_effect=[obj,None]),patch.object(r,'command',return_value='') as cmd:
                self.assertTrue(r.cleanup())
                cmd.assert_called_once_with(['docker','volume','rm',volume])
            recorded=json.loads(p.read_text());self.assertTrue(recorded['cleanup_complete'])
            with patch.object(r,'state_path',return_value=p),patch.object(r,'lookup',return_value=None),patch.object(r,'command') as cmd:
                self.assertTrue(r.cleanup());cmd.assert_not_called()

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
        import json,tempfile,os
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d);out=root/'c3c02b-public';out.mkdir()
            private=root/'journal.json';s=self.session();s['cleanup_complete']=False;private.write_text(json.dumps(s))
            for name in ('operational.trx','query-blocks.json'): (out/name).write_text('{}')
            (out/'identity-and-isolation.json').write_text('{"status":"evidence_complete_cleanup_pending"}')
            env={'RUNNER_TEMP':d,'GITHUB_RUN_ID':'123','CODE_SHA':'a'*40,'GITHUB_WORKFLOW_SHA':'b'*40}
            with patch.dict(os.environ,env,clear=True),patch.object(r,'state_path',return_value=private):
                self.assertEqual('failed',r.publish_execution(True,False,'run'))
                self.assertEqual('failed',json.loads((out/'identity-and-isolation.json').read_text())['status'])
                s['cleanup_complete']=True;private.write_text(json.dumps(s))
                self.assertEqual('failed',r.publish_execution(False,True,'cleanup'))


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
        import json,tempfile,os
        from unittest.mock import patch
        with tempfile.TemporaryDirectory() as d:
            root=pathlib.Path(d);out=root/'c3c02b-public';out.mkdir()
            private=root/'journal.json';s=self.session();s['cleanup_complete']=True;private.write_text(json.dumps(s))
            for name in ('operational.trx','query-blocks.json'):(out/name).write_text('{}')
            (out/'identity-and-isolation.json').write_text('{}')
            env={'RUNNER_TEMP':d,'GITHUB_RUN_ID':'123','CODE_SHA':'a'*40,'GITHUB_WORKFLOW_SHA':'b'*40}
            with patch.dict(os.environ,env,clear=True),patch.object(r,'state_path',return_value=private):
                self.assertEqual('passed',r.publish_execution(True,True,'run'))
                (out/'operational.trx').write_text('changed-after-validation')
                self.assertEqual('failed',r.publish_execution(False,True,'cleanup'))

    def test_h02_replaced_recorded_id_is_rejected(self):
        from unittest.mock import patch
        s=self.session()
        with patch.object(r,'save'):r.plan_resource(s,'network_id')
        s['planned']['network_id'].update(id='d'*64,phase='owned')
        obj={'Id':'e'*64,'Name':s['network_name'],'Internal':True,'Driver':'bridge','Labels':s['labels']}
        with self.assertRaises(r.Unsafe):r.verify_owned(s,'network_id',obj)


    def test_h02_lookup_is_exact_and_does_not_treat_daemon_errors_as_absence(self):
        import subprocess,json
        from unittest.mock import patch
        name='c3c02b-'+'c'*32
        missing=subprocess.CompletedProcess([],1,stdout=b'[]',stderr=('Error response from daemon: network '+name+' not found').encode())
        with patch.object(r.subprocess,'run',return_value=missing) as called:
            self.assertIsNone(r.lookup('network',name))
            self.assertEqual(['docker','--context','default','network','inspect',name],called.call_args.args[0])
        broken=subprocess.CompletedProcess([],1,stdout=b'',stderr=b'Cannot connect to daemon')
        with patch.object(r.subprocess,'run',return_value=broken),self.assertRaises(r.Unsafe):r.lookup('network',name)
        ambiguous=subprocess.CompletedProcess([],0,stdout=json.dumps([{'Id':'d'*64},{'Id':'e'*64}]).encode(),stderr=b'')
        with patch.object(r.subprocess,'run',return_value=ambiguous),self.assertRaises(r.Unsafe):r.lookup('network',name)

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
        with patch.object(r,'save'):r.plan_resource(s,'peer_id')
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
        import os,tempfile,json
        from unittest.mock import patch
        for include,clean,expected in [(True,True,'passed'),(False,True,'failed'),(True,False,'failed')]:
            with tempfile.TemporaryDirectory() as d:
                out=pathlib.Path(d)/'c3c02b-public';out.mkdir()
                e=self.environment();e['RUNNER_TEMP']=d
                s=SafetyTests().session();s.update(phase='isolation-only',cleanup_complete=clean,policy_sha256='f'*64,workflow_sha='b'*40,actor_id='1234')
                state=pathlib.Path(d)/'journal.json';state.write_text(json.dumps(s))
                if include:(out/'isolation.json').write_text(json.dumps(self.report()))
                with patch.dict(os.environ,e,clear=True),patch.object(r,'state_path',return_value=state):
                    self.assertEqual(expected,r.publish_isolation(True,True,'run'))
                    if expected=='failed':self.assertEqual('failed',r.publish_isolation(False,True,'cleanup'))

if __name__ == '__main__': unittest.main()
