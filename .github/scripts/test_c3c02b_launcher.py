"""Pure regression tests for the inline trusted launcher; no network/Docker/SQL."""
import copy
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import textwrap
import unittest
from unittest import mock

WORKFLOW = pathlib.Path(__file__).resolve().parents[2] / ".github/workflows/c3c02b-operational.yml"


def load():
    if not WORKFLOW.exists():
        return None
    text = WORKFLOW.read_text(encoding="utf-8")
    code = text.split("          # BEGIN TRUSTED PYTHON\n", 1)[1].split("          # END TRUSTED PYTHON", 1)[0]
    scope = {"__name__": "launcher_tests"}
    exec(compile(textwrap.dedent(code), str(WORKFLOW), "exec"), scope)
    return scope


def validate_topology(text):
    def check(condition,diagnostic):
        if not condition:raise AssertionError(diagnostic)
    trigger=text.split("on:\n",1)[1].split("permissions:\n",1)[0]
    check(trigger=="  workflow_dispatch:\n","Only input-free workflow_dispatch")
    check("continue-on-error:" not in text,"Failure cannot be masked")
    check("permissions:\n  contents: read\n" in text,"Read-only permissions")
    check(text.count("  C3C02B_PHASE: isolation-only\n")==1 and
          text.count("  CONSTIA_B3_APPROVED_PHASE: isolation-only\n")==1,"Fixed phase")
    gate,isolation=text.split("  isolation:\n",1)
    check("uses:" not in gate and "environment:" not in gate,"No execution in gate")
    check([x.strip() for x in gate.splitlines() if x.startswith("      - name: ")]==["- name: Read-only identity gate"],"Single gate")
    check("    needs: gate\n" in isolation and "    environment: constia-c3c-isolation\n" in isolation,"Protected approval")
    steps=isolation.split("    steps:\n",1)[1].split("      - name: ")[1:]
    names=[x.splitlines()[0] for x in steps]
    check(names==["Recheck trusted identity after approval","Checkout only H2","Verify H2 chain and every checkout byte from L2",
          "Pure regressions from verified H2","Run only isolation","Cleanup only verified H2 resources",
          "Validate and project allowlisted diagnostics from L2","Upload only validated projection","Require actual isolation success"],"Step order")
    check("steps.checkout.outcome == 'success'" in steps[2] and "run: *trusted_verifier" in steps[2],"Checkout guard")
    for index,stage in ((3,"tests"),(4,"run"),(5,"cleanup"),(6,"evidence"),(8,"final")):
        check("steps.verified.outputs.verified == 'true'" in steps[index] and "run: *trusted_verifier" in steps[index] and
              "LAUNCHER_STAGE: "+stage in steps[index],"Verified trusted stage")
    check("always()" not in steps[3]+steps[4],"No run after failure")
    check(all("always()" in steps[i] for i in (5,6,7,8)),"Cleanup/report/final survive failure")
    check("steps.evidence.outputs.diagnostic_valid == 'true'" in steps[7] and
          "steps.evidence.outcome == 'success'" in steps[7],"No upload without validation")
    check("steps.evidence.outputs.isolation_verified" in steps[8],"Final success guard")
    check("fetch-depth: 3" in steps[1] and "persist-credentials: false" in steps[1],"Checkout bounds")
    check("secrets." not in text and "id-token:" not in text,"No new secrets or permissions")



def topology_mutations(text):
    """Synthetic workflow variants, never written or executed as workflows."""
    return {
        "environment absent": text.replace("    environment: constia-c3c-isolation\n", ""),
        "gate dependency absent": text.replace("    needs: gate\n", ""),
        "checkout before gate": text.replace("      - name: Read-only identity gate",
            "      - name: Premature checkout\n        uses: actions/checkout@bad\n      - name: Read-only identity gate"),
        "H before verifier": text.replace("      - name: Verify H2 chain and every checkout byte from L2",
            "      - name: Premature H\n        run: python3 harness/.github/scripts/c3c02b_runner.py\n"
            "      - name: Verify H2 chain and every checkout byte from L2"),
        "verified condition absent": text.replace("steps.verified.outputs.verified == 'true'", "true"),
        "different phase": text.replace("  C3C02B_PHASE: isolation-only", "  C3C02B_PHASE: operational-full"),
        "unexpected step": text + "      - name: Unexpected step\n        run: echo forbidden\n",
        "checkout success guard absent": text.replace("steps.checkout.outcome == 'success'", "true"),
        "H forced after failure": text.replace("      - name: Pure regressions from verified H2\n",
            "      - name: Pure regressions from verified H2\n        if: ${{ always() }}\n"),
    }


class LauncherTests(unittest.TestCase):
    def setUp(self):
        self.l = load()
        self.assertIsNotNone(self.l, "The trusted launcher is not implemented")

    def context(self):
        return dict(GITHUB_ACTIONS="true", RUNNER_ENVIRONMENT="github-hosted", RUNNER_OS="Linux",
                    GITHUB_REPOSITORY="ivandinicola/CONSTIA", GITHUB_REPOSITORY_ID="1407527987",
                    GITHUB_EVENT_NAME="workflow_dispatch", GITHUB_REF="refs/heads/main",
                    GITHUB_WORKFLOW_REF="ivandinicola/CONSTIA/.github/workflows/c3c02b-operational.yml@refs/heads/main",
                    GITHUB_SHA="a"*40, GITHUB_WORKFLOW_SHA="a"*40,
                    GITHUB_ACTOR_ID="147414572", GITHUB_RUN_ATTEMPT="1", GITHUB_RUN_ID="123",
                    CODE_SHA=self.l["H"], C3C02B_PHASE="isolation-only",
                    CONSTIA_B3_APPROVED_PHASE="isolation-only", CONSTIA_B3_APPROVED_SHA=self.l["H"],
                    CONSTIA_B3_APPROVED_ACTOR_ID="147414572",
                    CONSTIA_B3_INVENTORY_SHA256=self.l["D"])

    def test_valid_context_and_empty_inputs(self):
        self.l["validate_context"](self.context(), {"inputs": {}}, "a"*40)

    def test_reject_wrong_repository_ref_actor_attempt_or_workflow(self):
        for key, value in [
            ("GITHUB_REPOSITORY", "attacker/CONSTIA"), ("GITHUB_REPOSITORY_ID", "1"),
            ("GITHUB_REF", "refs/heads/feature/test"), ("GITHUB_ACTOR_ID", "42"),
            ("GITHUB_RUN_ATTEMPT", "2"), ("GITHUB_EVENT_NAME", "push"),
            ("GITHUB_WORKFLOW_REF", "ivandinicola/CONSTIA/.github/workflows/other.yml@refs/heads/main"),
            ("GITHUB_WORKFLOW_SHA", "b"*40), ("RUNNER_ENVIRONMENT", "self-hosted"),
        ]:
            with self.subTest(key=key):
                env = self.context()
                env[key] = value
                with self.assertRaises(self.l["Unsafe"]):
                    self.l["validate_context"](env, {"inputs": {}}, "a"*40)

    def test_reject_changed_H_D_phase_or_unexpected_inputs(self):
        for key, value in [("CODE_SHA", "b"*40), ("CONSTIA_B3_APPROVED_SHA", "b"*40),
                           ("CONSTIA_B3_INVENTORY_SHA256", "0"*64),
                           ("C3C02B_PHASE", "operational-full"), ("CONSTIA_B3_APPROVED_PHASE", "operational-full")]:
            env = self.context()
            env[key] = value
            with self.subTest(key=key), self.assertRaises(self.l["Unsafe"]):
                self.l["validate_context"](env, {"inputs": {}}, "a"*40)
        for inputs in [{"approved": True}, {"code_sha": self.l["H"]}, {"phase": "isolation-only"}]:
            with self.assertRaises(self.l["Unsafe"]):
                self.l["validate_context"](self.context(), {"inputs": inputs}, "a"*40)

    def test_reject_main_moved_and_inherited_connections(self):
        with self.assertRaises(self.l["Unsafe"]):
            self.l["validate_context"](self.context(), {}, "b"*40)
        for key in ["HTTPS_PROXY", "RESEND_API_KEY", "DOCKER_HOST", "SQLCONNSTR_DefaultConnection"]:
            env = self.context()
            env[key] = "forbidden"
            with self.assertRaises(self.l["Unsafe"]):
                self.l["validate_context"](env, {}, "a"*40)

    def test_canonical_policy_is_the_approved_blob_digest(self):
        table = self.l["TABLE"]
        self.assertEqual(17, len(table))
        self.assertEqual("b31273e6cb9f4177e32cba0c09af254b73db1ee22c862ffd8043bcca564489a4",
                         hashlib.sha256(json.dumps(table, sort_keys=True, separators=(",", ":")).encode()).hexdigest())
        self.assertEqual("a6791f91f5c6a6654997025e2b71038161e24d4481141fdd01e24682f990fb81",
                         table["tests/sandbox/c3c02b/coverage.json"])

    def test_reject_incomplete_or_expanded_policy(self):
        for table in [{}, {**self.l["TABLE"], "extra.py": "a"*64}]:
            with self.assertRaises(self.l["Unsafe"]):
                self.l["validate_policy"](table)

    def repository(self, directory):
        root=pathlib.Path(directory)
        env=dict(os.environ,GIT_CONFIG_NOSYSTEM="1",GIT_CONFIG_GLOBAL=os.devnull)
        def git(*args):
            return subprocess.check_output(["git","-C",str(root),*args],env=env,stderr=subprocess.DEVNULL).strip().decode()
        git("init","--quiet")
        (root/"base.txt").write_bytes(b"base\n");git("add","--","base.txt")
        git("-c","user.name=Local Test","-c","user.email=test@example.invalid","commit","-qm","base")
        base=git("rev-parse","HEAD")
        (root/"option.txt").write_bytes(b"approved\n");git("add","--","option.txt")
        git("-c","user.name=Local Test","-c","user.email=test@example.invalid","commit","-qm","H")
        parent=git("rev-parse","HEAD");parent_tree=git("rev-parse","HEAD^{tree}")
        (root/"option.txt").write_bytes(b"approved-v2\n");git("add","--","option.txt")
        git("-c","user.name=Local Test","-c","user.email=test@example.invalid","commit","-qm","H2")
        return root,git,dict(h=git("rev-parse","HEAD"),base=base,tree=git("rev-parse","HEAD^{tree}"),
            parent=parent,parent_tree=parent_tree,changes=("option.txt",),
            table={"option.txt":hashlib.sha256(b"approved-v2\n").hexdigest()},
            historical_table={"option.txt":hashlib.sha256(b"approved\n").hexdigest()})


    def verify(self, root, policy):
        return self.l["verify_checkout"](root, **policy)

    def test_real_git_checkout_is_verified_without_executing_hooks(self):
        with tempfile.TemporaryDirectory() as t:
            root, git, p = self.repository(t)
            hook = root / ".git/hooks/post-checkout"
            # Git's shell runs this sentinel on Windows too. All paths belong
            # to this disposable repository, never the real CONSTIA worktree.
            hook.write_bytes(b"#!/bin/sh\nprintf 'hook-ran\\n' >> executed\n")
            hook.chmod(0o755)
            marker = root / "executed"
            git("checkout", "--detach", p["h"])
            self.assertEqual(b"hook-ran\n", marker.read_bytes())
            marker.unlink()
            # A hook-triggering command through the real protected wrapper.
            self.l["git"](root, "checkout", "--detach", p["h"])
            self.assertFalse(marker.exists())
            # Verification itself uses only read-only Git commands.
            self.verify(root, p)
            self.assertFalse(marker.exists())
            # The hook is still functional: protection was per-command.
            git("checkout", "--detach", p["h"])
            self.assertEqual(b"hook-ran\n", marker.read_bytes())

    def test_reject_checkout_error_wrong_head_parent_tree_or_inventory(self):
        with tempfile.TemporaryDirectory() as t:
            root, git, p = self.repository(t)
            for key in ["h", "base", "tree", "parent", "parent_tree"]:
                bad = dict(p, **{key: "0"*40})
                with self.subTest(key=key), self.assertRaises(self.l["Unsafe"]):
                    self.verify(root, bad)
            for table in [{}, {"other.txt": "0"*64}]:
                with self.assertRaises(self.l["Unsafe"]):
                    self.verify(root, dict(p, table=table))
        with tempfile.TemporaryDirectory() as t, self.assertRaises(self.l["Unsafe"]):
            self.verify(pathlib.Path(t), p)

    def test_reject_blob_altered_additional_ignored_file_and_base_tampering(self):
        for rel in ["option.txt", "base.txt", "extra.py", "ignored.tmp"]:
            with self.subTest(rel=rel), tempfile.TemporaryDirectory() as t:
                root, git, p = self.repository(t)
                (root / rel).write_bytes(b"altered\n")
                if rel == "ignored.tmp":
                    (root / ".git/info/exclude").write_text("ignored.tmp\n")
                with self.assertRaises(self.l["Unsafe"]):
                    self.verify(root, p)

    def test_reject_bad_blob_hash(self):
        with tempfile.TemporaryDirectory() as t:
            root, git, p = self.repository(t)
            with self.assertRaises(self.l["Unsafe"]):
                self.verify(root, dict(p, table={"option.txt": "0"*64}))

    def test_CRLF_is_not_treated_as_identical_checkout_bytes(self):
        with tempfile.TemporaryDirectory() as t:
            root, git, p = self.repository(t)
            (root / "option.txt").write_bytes(b"approved-v2\r\n")
            with self.assertRaises(self.l["Unsafe"]):
                self.verify(root, p)

    def test_artifact_allowlist_rejects_missing_or_extra_files(self):
        with tempfile.TemporaryDirectory() as t:
            root = pathlib.Path(t)
            for names in [[], ["operational.trx"], ["isolation.json", "isolation-execution.json", "isolation-sha256.json", "extra.txt"]]:
                for f in root.iterdir():
                    f.unlink()
                for name in names:
                    (root / name).write_text("{}")
                with self.assertRaises(self.l["Unsafe"]):
                    self.l["read_set"](root, self.l["DIAGNOSTIC_FILES"])

    def symlink(self, link, target, directory=False):
        try:
            link.symlink_to(target, target_is_directory=directory)
        except (NotImplementedError, OSError) as ex:
            if isinstance(ex, NotImplementedError) or getattr(ex, "winerror", None) == 1314 or getattr(ex, "errno", None) in (1, 13, 38, 95):
                self.skipTest("Real symlink creation unavailable: " + str(ex))
            raise
        self.assertTrue(link.is_symlink(), "The fixture must be a real symlink")

    def evidence_files(self, root):
        root.mkdir()
        for name in ("isolation-execution.json", "isolation-cleanup-primary.json", "isolation-cleanup-final.json"):
            (root / name).write_bytes(b"{}")

    def test_allowed_artifact_symlink_is_rejected_before_reading_target(self):
        with tempfile.TemporaryDirectory() as t:
            parent = pathlib.Path(t)
            evidence = parent / "evidence"
            self.evidence_files(evidence)
            target = parent / "synthetic.txt"
            target.write_bytes(b"must not be read")
            (evidence / "isolation-execution.json").unlink()
            self.symlink(evidence / "isolation-execution.json", target)
            with self.assertRaisesRegex(self.l["Unsafe"], "Unsafe artifact"):
                self.l["read_set"](evidence, self.l["DIAGNOSTIC_FILES"])

    def test_unexpected_artifact_symlink_is_rejected_by_allowlist(self):
        with tempfile.TemporaryDirectory() as t:
            parent = pathlib.Path(t)
            evidence = parent / "evidence"
            self.evidence_files(evidence)
            target = parent / "synthetic.txt"
            target.write_bytes(b"must not be read")
            self.symlink(evidence / "unexpected.json", target)
            with self.assertRaisesRegex(self.l["Unsafe"], "Artifact allowlist differs"):
                self.l["read_set"](evidence, self.l["DIAGNOSTIC_FILES"])

    def test_physical_checkout_symlink_is_rejected_before_reading_target(self):
        with tempfile.TemporaryDirectory() as t:
            parent = pathlib.Path(t)
            checkout = parent / "checkout"
            checkout.mkdir()
            root, git, p = self.repository(checkout)
            target = parent / "synthetic.txt"
            target.write_bytes(b"must not be read")
            (root / "option.txt").unlink()
            self.symlink(root / "option.txt", target)
            with self.assertRaisesRegex(self.l["Unsafe"], "Checkout symlink forbidden"):
                self.verify(root, p)

    def test_checkout_root_symlink_outside_authorized_directory_is_rejected(self):
        with tempfile.TemporaryDirectory() as t:
            parent = pathlib.Path(t)
            target = parent / "synthetic-directory"
            target.mkdir()
            link = parent / "checkout-link"
            self.symlink(link, target, directory=True)
            with self.assertRaisesRegex(self.l["Unsafe"], "Checkout missing"):
                self.l["verify_checkout"](link)

    def test_evidence_directory_symlink_is_rejected(self):
        with tempfile.TemporaryDirectory() as t:
            parent = pathlib.Path(t)
            target = parent / "synthetic-directory"
            self.evidence_files(target)
            link = parent / "evidence-link"
            self.symlink(link, target, directory=True)
            with self.assertRaisesRegex(self.l["Unsafe"], "Evidence absent"):
                self.l["read_set"](link, self.l["DIAGNOSTIC_FILES"])

    def test_artifacts_require_matching_hashes_identity_and_cleanup(self):
        case=L2EvidenceTests("test_valid_failure_is_publishable_but_not_success")
        case.setUp()
        try:
            case.write();self.assertTrue(case.validate()["diagnostic_valid"])
            legacy=dict(status="passed",cleanup_verified=True,resources=[])
            (case.diag/"isolation-execution.json").write_text(json.dumps(legacy))
            with self.assertRaises(case.l["Unsafe"]):case.validate()
        finally:case.doCleanups()


    def test_workflow_has_no_path_before_gate_environment_and_verifier(self):
        # Parse the small step topology to reject mutations which could run H early.
        text = WORKFLOW.read_text(encoding="utf-8")
        self.assertIn("on:\n  workflow_dispatch:\n", text)
        self.assertNotIn("inputs:", text)
        self.assertIn("environment: constia-c3c-isolation", text)
        self.assertIn("needs: gate", text)
        self.assertIn("persist-credentials: false", text)
        self.assertNotIn("secrets.", text)
        self.assertNotIn("id-token:", text)
        steps = text.split("  isolation:\n", 1)[1].split("    steps:\n", 1)[1]
        names = [line.strip()[len("- name: "):] for line in steps.splitlines() if line.strip().startswith("- name: ")]
        self.assertEqual(["Recheck trusted identity after approval", "Checkout only H2",
                          "Verify H2 chain and every checkout byte from L2", "Pure regressions from verified H2",
                          "Run only isolation", "Cleanup only verified H2 resources",
                          "Validate and project allowlisted diagnostics from L2", "Upload only validated projection", "Require actual isolation success"], names)
        self.assertEqual(5, text.count("steps.verified.outputs.verified == 'true'"))
        self.assertIn("steps.evidence.outputs.diagnostic_valid == 'true'", text)
        self.assertIn("steps.checkout.outcome == 'success'", text)
        self.assertIn("shell: python3 -I -B {0}", text)
        validate_topology(text)

    def test_missing_environment_wrong_step_order_or_checkout_guard_is_rejected(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        for name, mutated in topology_mutations(text).items():
            with self.subTest(mutation=name), self.assertRaises(AssertionError):
                validate_topology(mutated)

    def test_topology_rejects_mutations_with_optimized_python(self):
        text = WORKFLOW.read_text(encoding="utf-8")
        script = """
import importlib.util, json, sys
spec = importlib.util.spec_from_file_location("launcher_regressions", sys.argv[1])
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
outcomes = {}
for name, text in json.load(sys.stdin).items():
    try:
        module.validate_topology(text)
    except AssertionError:
        outcomes[name] = "rejected"
    else:
        outcomes[name] = "accepted"
print(json.dumps(outcomes))
"""
        mutations = topology_mutations(text)
        for flag in ("-O", "-OO"):
            with self.subTest(optimization=flag):
                result = subprocess.run([sys.executable, "-I", "-B", flag, "-c", script, str(pathlib.Path(__file__).resolve())],
                    input=json.dumps(mutations), text=True, capture_output=True, timeout=15, check=True)
                self.assertEqual({name: "rejected" for name in mutations}, json.loads(result.stdout))

    def test_failed_checkout_cannot_emit_verification_authority(self):
        with tempfile.TemporaryDirectory() as t:
            root = pathlib.Path(t)
            event = root / "event.json"
            event.write_text('{"inputs":{}}')
            env = dict(self.context(), GITHUB_EVENT_PATH=str(event), GITHUB_WORKSPACE=t,
                       LAUNCHER_STAGE="verify", GATE_RESULT="success",
                       GITHUB_OUTPUT=str(root / "output"), GITHUB_ENV=str(root / "env"))
            old = self.l["current_main"]
            self.l["current_main"] = lambda: "a"*40
            try:
                with mock.patch.dict(__import__("os").environ, env, clear=True):
                    with self.assertRaises(self.l["Unsafe"]):
                        self.l["main"]()
                self.assertFalse((root / "output").exists())
                self.assertFalse((root / "env").exists())
            finally:
                self.l["current_main"] = old

    def test_post_approval_stage_rejects_absent_prior_gate(self):
        with tempfile.TemporaryDirectory() as t:
            event = pathlib.Path(t) / "event.json"
            event.write_text("{}")
            env = dict(self.context(), GITHUB_EVENT_PATH=str(event), LAUNCHER_STAGE="approved")
            old = self.l["current_main"]
            self.l["current_main"] = lambda: "a"*40
            try:
                with mock.patch.dict(__import__("os").environ, env, clear=True):
                    with self.assertRaises(self.l["Unsafe"]):
                        self.l["main"]()
            finally:
                self.l["current_main"] = old


class L2ContractTests(unittest.TestCase):
    def setUp(self):
        self.l=load()

    def test_exact_H2_tree_parent_and_D2_are_pinned(self):
        self.assertEqual("62543ba874eb4d080ca68913d72dac944417ad7f",self.l["H"])
        self.assertEqual("5eb98f07728bfa8602930af04a286a21a78b6cf9",self.l["PARENT"])
        self.assertEqual("7e1b5989d29e07f53aa2c12e70dd04b5d0e8d4b5",self.l["TREE"])
        self.assertEqual("b31273e6cb9f4177e32cba0c09af254b73db1ee22c862ffd8043bcca564489a4",self.l["D"])

    def test_diagnostic_contract_is_independent_from_H2_code(self):
        self.assertTrue(callable(self.l.get("validate_bundle")))
        self.assertTrue(callable(self.l.get("validate_record")))
        self.assertTrue(callable(self.l.get("publish_projection")))

    def test_failed_operation_never_becomes_success(self):
        self.assertIn("isolation_verified",self.l)
        self.assertFalse(self.l["isolation_verified"]("failure","success","success",{}))



class L2EvidenceTests(unittest.TestCase):
    context=LauncherTests.context
    def setUp(self):
        self.l=load()
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup)
        self.root=pathlib.Path(self.tmp.name);self.e=self.context()
        self.diag=self.root/"c3c02b-diagnostics";self.diag.mkdir()
        self.public=self.root/"c3c02b-public";self.public.mkdir()
        self.nonce="c"*32
        identity=dict(repository=self.e["GITHUB_REPOSITORY"],run_id="123",attempt="1",
                      code_sha=self.e["CODE_SHA"],workflow_sha="a"*40,policy_sha256=self.e["CONSTIA_B3_INVENTORY_SHA256"])
        resources=dict(network_id=dict(kind="network",name="c3c02b-"+self.nonce),
                       app_image_id=dict(kind="image",name="c3c02b:"+self.nonce),
                       app_id=dict(kind="container",name="c3c02b-app-"+self.nonce),
                       peer_id=dict(kind="container",name="c3c02b-peer-"+self.nonce),
                       denied_id=dict(kind="container",name="c3c02b-denied-"+self.nonce))
        self.manifest=dict(schema=3,phase="isolation-only",identity=dict(identity,workflow_ref=self.e["GITHUB_WORKFLOW_REF"],actor_id="147414572"),
                           nonce=self.nonce,resources=resources)
        failure=dict(stage="connectivity",code="CONNECTIVITY_UNVERIFIED",reason="CHECK_FAILED")
        session=dict(nonce=self.nonce,manifest_sha256=None,producer_state="quiescent")
        self.record=dict(schema=2,kind="execution",phase="isolation-only",identity=identity,
            session=session,last_completed_stage="connectivity",first_failure=failure,operation_failure=None,
            execution="failed_after_creation_attempt",creation_attempted=True,journal_state="valid",journal_seen=True,
            cleanup="uncertain",cleanup_scope="unknown",evidence="incomplete",proof_state="absent",proof_sha256=None,
            execution_sha256=None,primary_sha256=None)
        self.primary=None;self.final=None;self.proof=None
        self.outcomes=dict(tests="success",run="failure",cleanup="success")
    def body(self,value):
        return json.dumps(value,sort_keys=True,separators=(",",":")).encode()
    def write(self):
        raw=self.body(self.manifest);(self.root/"c3c02b-123.json").write_bytes(raw)
        self.record["session"]["manifest_sha256"]=hashlib.sha256(raw).hexdigest()
        if self.proof is not None:
            proof_raw=self.body(self.proof);(self.public/"isolation.json").write_bytes(proof_raw)
            self.record.update(proof_state="candidate_valid",proof_sha256=hashlib.sha256(proof_raw).hexdigest())
        original=self.body(self.record);(self.diag/"isolation-execution.json").write_bytes(original)
        primary=copy.deepcopy(self.primary if self.primary is not None else self.record)
        if primary["session"]["manifest_sha256"] is None:primary["session"]["manifest_sha256"]=self.record["session"]["manifest_sha256"]
        primary.update(kind="cleanup_primary",execution_sha256=hashlib.sha256(original).hexdigest(),primary_sha256=None)
        if self.primary is None:primary.update(cleanup="verified",cleanup_scope="managed_session_resources",last_completed_stage="cleanup")
        raw_primary=self.body(primary);(self.diag/"isolation-cleanup-primary.json").write_bytes(raw_primary)
        final=copy.deepcopy(self.final if self.final is not None else primary)
        if self.final is None:final["last_completed_stage"]="cleanup" if final["cleanup"]=="verified" else "authorization"
        final.update(kind="cleanup_final",execution_sha256=hashlib.sha256(original).hexdigest(),primary_sha256=hashlib.sha256(raw_primary).hexdigest())
        (self.diag/"isolation-cleanup-final.json").write_bytes(self.body(final))
    def validate(self):
        return self.l["validate_bundle"](self.root,self.e,self.outcomes)
    def successful(self):
        self.record.update(execution="completed",first_failure=None)
        self.outcomes["run"]="success"
        self.proof=dict(phase="isolation-only",allowed_peer=True,denied_peer=True,gateway_blocked=True,
            controls_positive=True,simulated_transport=True,routes_sha256="b"*64,sql="not_applicable",workers_started=0,
            identity=dict(phase="isolation-only",repository="ivandinicola/CONSTIA",code_sha=self.e["CODE_SHA"],
            policy_sha256=self.e["CONSTIA_B3_INVENTORY_SHA256"],workflow_sha="a"*40,
            workflow_ref=self.e["GITHUB_WORKFLOW_REF"],actor_id="147414572",run_id="123",attempt="1",nonce=self.nonce,
            network_id="e"*64,network_name="c3c02b-"+self.nonce,subnet="172.31.240.0/28",gateway="172.31.240.1",
            app_id="1"*64,app_ip="172.31.240.2",peer_id="2"*64,peer_ip="172.31.240.3",
            denied_id="3"*64,denied_ip="172.31.240.4",image_id="sha256:"+"a"*64,
            sdk_image="mcr.microsoft.com/dotnet/sdk@sha256:8dd1a57fe6b7b8b1b357635db67e272c2f9e9d76d1dd0a32711c4c33c57d7b43",
            namespace="net:[123]",rules_sha256="b"*64,proof_stage="isolated",gateway_port=34567))
    def test_valid_failure_is_publishable_but_not_success(self):
        self.write();p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])
        self.assertEqual(self.record["first_failure"],p["first_failure"])
        raw=self.body(p)
        for private in (self.nonce,"172.31.","net:[",'"resources":',"manifest_sha256"):
            self.assertNotIn(private.encode(),raw)
    def test_success_requires_all_step_results_and_proof(self):
        self.successful();self.write();self.assertTrue(self.validate()["isolation_verified"])
        for key in self.outcomes:
            old=self.outcomes[key];self.outcomes[key]="failure"
            self.assertFalse(self.validate()["isolation_verified"]);self.outcomes[key]=old
    def test_cleanup_failure_is_not_hidden_by_valid_diagnostic(self):
        self.primary=copy.deepcopy(self.record)
        self.primary.update(cleanup="failed",last_completed_stage="evidence_write",operation_failure=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED"))
        self.outcomes["cleanup"]="failure";self.write()
        p=self.validate();self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])
        self.assertEqual("failed",p["cleanup"])
    def test_missing_record_or_manifest_never_validates(self):
        self.write()
        for path in [self.diag/"isolation-execution.json",self.diag/"isolation-cleanup-primary.json",
                     self.diag/"isolation-cleanup-final.json",self.root/"c3c02b-123.json"]:
            raw=path.read_bytes();path.unlink()
            with self.subTest(path=path.name),self.assertRaises(self.l["Unsafe"]):self.validate()
            path.write_bytes(raw)
    def test_wrong_schema_type_enum_identity_or_extra_key_rejects(self):
        self.write()
        for key,value in [("schema",True),("schema",1),("execution","passed"),("creation_attempted",1),
                          ("cleanup","complete"),("evidence","complete_validated"),("private","secret")]:
            old=copy.deepcopy(self.record);self.record[key]=value;self.write()
            with self.subTest(key=key),self.assertRaises(self.l["Unsafe"]):self.validate()
            self.record=old
        self.record["identity"]["run_id"]="999";self.write()
        with self.assertRaises(self.l["Unsafe"]):self.validate()
    def test_session_or_first_failure_mismatch_rejects_even_with_hashes(self):
        for field in ("session","first_failure"):
            self.primary=copy.deepcopy(self.record)
            if field=="session":self.primary[field]["producer_state"]="unresolved"
            else:self.primary[field]=dict(stage="unexpected",code="UNEXPECTED_FAILURE",reason="UNCLASSIFIED")
            self.write()
            with self.subTest(field=field),self.assertRaises(self.l["Unsafe"]):self.validate()
    def test_transitive_hash_and_proof_corruption_rejected(self):
        self.successful();self.write()
        for path in [self.diag/"isolation-execution.json",self.diag/"isolation-cleanup-primary.json",self.public/"isolation.json"]:
            raw=path.read_bytes();path.write_bytes(raw+b" ")
            with self.subTest(path=path.name),self.assertRaises(self.l["Unsafe"]):self.validate()
            path.write_bytes(raw)
    def test_extra_files_duplicate_keys_oversize_and_sensitive_rejected(self):
        self.write();p=self.diag/"unexpected.json";p.write_bytes(b"{}")
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        p.unlink();p=self.diag/"isolation-execution.json";original=p.read_bytes()
        for raw in [b'{"schema":2,"schema":2}',b"X"*8193,original.replace(b'"connectivity"',b'"private@example.invalid"')]:
            p.write_bytes(raw)
            with self.assertRaises(self.l["Unsafe"]):self.validate()
        p.write_bytes(original)
        (self.public/"operational.trx").write_bytes(b"private")
        with self.assertRaises(self.l["Unsafe"]):self.validate()
    def test_unresolved_and_unknown_never_certify_isolation(self):
        for state in ("unresolved","unknown"):
            self.record["session"]["producer_state"]=state
            self.primary=copy.deepcopy(self.record);self.primary.update(cleanup="uncertain",cleanup_scope="unknown",
                last_completed_stage="evidence_write",operation_failure=dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN"))
            self.outcomes["cleanup"]="failure";self.write()
            if state=="unknown":
                with self.assertRaises(self.l["Unsafe"]):self.validate()
            else:self.assertFalse(self.validate()["isolation_verified"])
    def test_no_dispatch_failure_without_manifest_has_explicit_justification(self):
        write_h2_early_failure(self)
        self.assertTrue(self.validate()["diagnostic_valid"])
        self.assertFalse(self.validate()["isolation_verified"])
    def test_publication_uses_only_validated_projection_bytes(self):
        self.write();projection=self.validate()
        destination,digest=self.l["publish_projection"](self.root,projection)
        path=pathlib.Path(destination)/"diagnostic.json"
        self.assertEqual({"diagnostic.json"},{p.name for p in path.parent.iterdir()})
        self.assertEqual(self.body(projection),path.read_bytes())
        self.assertEqual(digest,hashlib.sha256(path.read_bytes()).hexdigest())
        bad=dict(projection,private="token")
        with self.assertRaises(self.l["Unsafe"]):self.l["publish_projection"](self.root,bad)


    def test_late_manifest_or_public_file_cannot_escape_allowlist(self):
        self.record.update(execution="failed_before_creation",creation_attempted=False,journal_state="absent",journal_seen=False,
            cleanup="not_applicable",cleanup_scope="this_invocation_no_dispatch",session=dict(nonce=None,manifest_sha256=None,producer_state="no_dispatch"))
        # Reuse the no-dispatch writer to ensure this starts as a valid closed diagnostic.
        self.test_no_dispatch_failure_without_manifest_has_explicit_justification()
        actual=self.l["safe_read"];count=[0]
        def read(path,maximum):
            raw=actual(path,maximum);count[0]+=1
            if count[0]==4:(self.root/"c3c02b-123.json").write_bytes(b"{}")
            return raw
        self.l["safe_read"]=read
        try:
            with self.assertRaises(self.l["Unsafe"]):self.validate()
        finally:self.l["safe_read"]=actual

    def test_projection_cannot_claim_completed_without_creation(self):
        self.successful();self.write();projection=self.validate();projection["creation_attempted"]=False
        with self.assertRaises(self.l["Unsafe"]):self.l["validate_projection"](projection)

class L2StageTests(unittest.TestCase):
    context=LauncherTests.context
    def setUp(self):self.l=load()
    def main_env(self,directory,stage):
        root=pathlib.Path(directory);event=root/"event.json";event.write_text('{"inputs":{}}')
        return dict(self.context(),GITHUB_EVENT_PATH=str(event),GITHUB_WORKSPACE=str(root),RUNNER_TEMP=str(root),
            GITHUB_OUTPUT=str(root/"output"),LAUNCHER_STAGE=stage,GATE_RESULT="success",
            L2_ENVIRONMENT="constia-c3c-isolation",CHECKOUT_VERIFIED="true")
    def test_execution_revalidates_before_child_and_strips_tokens(self):
        e=self.context();e.update(LAUNCHER_READ_TOKEN="synthetic",GITHUB_TOKEN="synthetic",
            ACTIONS_RUNTIME_TOKEN="synthetic",GITHUB_OUTPUT="forbidden",GITHUB_ENV="forbidden",PATH=os.environ.get("PATH",""))
        calls=[];actual=self.l["verify_checkout"]
        self.l["verify_checkout"]=lambda root:calls.append("verify")
        def child(*args,**kw):
            calls.append("child")
            for key in ("LAUNCHER_READ_TOKEN","GITHUB_TOKEN","ACTIONS_RUNTIME_TOKEN","GITHUB_OUTPUT","GITHUB_ENV"):
                self.assertNotIn(key,kw["env"])
            self.assertEqual("isolation-only",kw["env"]["C3C02B_PHASE"])
            self.assertEqual("isolation-only",args[0][-1])
            return type("Completed",(),{"returncode":17})()
        try:
            with mock.patch.object(subprocess,"run",side_effect=child):
                self.assertEqual(1,self.l["execute_verified"]("synthetic","run",e))
            self.assertEqual(["verify","child"],calls)
            self.l["verify_checkout"]=mock.Mock(side_effect=self.l["Unsafe"]("rejected"))
            with mock.patch.object(subprocess,"run") as process:
                with self.assertRaises(self.l["Unsafe"]):self.l["execute_verified"]("synthetic","run",e)
                process.assert_not_called()
            with self.assertRaises(self.l["Unsafe"]):self.l["execute_verified"]("synthetic","operational-full",e)
        finally:self.l["verify_checkout"]=actual
    def test_environment_and_verified_authority_are_mandatory(self):
        with tempfile.TemporaryDirectory() as d:
            original=self.l["current_main"];self.l["current_main"]=lambda:"a"*40
            try:
                for key,bad in (("L2_ENVIRONMENT",None),("L2_ENVIRONMENT","other"),("GATE_RESULT","failure"),("CHECKOUT_VERIFIED",None)):
                    env=self.main_env(d,"evidence")
                    if bad is None:env.pop(key)
                    else:env[key]=bad
                    with mock.patch.dict(os.environ,env,clear=True):
                        with self.assertRaises(self.l["Unsafe"]):self.l["main"]()
                self.assertFalse((pathlib.Path(d)/"output").exists())
            finally:self.l["current_main"]=original
    def test_invalid_or_missing_evidence_cannot_authorize_upload(self):
        with tempfile.TemporaryDirectory() as d:
            env=self.main_env(d,"evidence");original=self.l["publish_projection"]
            self.l["publish_projection"]=mock.Mock(side_effect=AssertionError("Must not publish"))
            try:
                with mock.patch.dict(os.environ,env,clear=True):
                    with self.assertRaises(self.l["Unsafe"]):self.l["main"]()
                self.l["publish_projection"].assert_not_called()
                self.assertFalse((pathlib.Path(d)/"output").exists())
            finally:self.l["publish_projection"]=original
    def test_valid_failure_publishes_without_authorizing_global_success(self):
        case=L2EvidenceTests("test_valid_failure_is_publishable_but_not_success");case.setUp()
        try:
            case.write();env=self.main_env(case.root,"evidence")
            env.update(TESTS_OUTCOME="success",RUN_OUTCOME="failure",CLEANUP_OUTCOME="success")
            with mock.patch.dict(os.environ,env,clear=True):self.l["main"]()
            output=(case.root/"output").read_text()
            self.assertIn("diagnostic_valid=true",output);self.assertIn("isolation_verified=false",output)
            env.update(LAUNCHER_STAGE="final",DIAGNOSTIC_VALID="true",ISOLATION_VERIFIED="false")
            with mock.patch.dict(os.environ,env,clear=True):
                with self.assertRaises(self.l["Unsafe"]):self.l["main"]()
            env["ISOLATION_VERIFIED"]="true"
            with mock.patch.dict(os.environ,env,clear=True):
                with self.assertRaises(self.l["Unsafe"]):self.l["main"]()
        finally:case.doCleanups()
    def test_cleanup_remains_authorized_after_original_main_moves(self):
        with tempfile.TemporaryDirectory() as d:
            env=self.main_env(d,"cleanup")
            original=self.l["execute_verified"];api=self.l["current_main"];calls=[]
            self.l["execute_verified"]=lambda root,stage,e:calls.append(stage) or 0
            self.l["current_main"]=mock.Mock(side_effect=AssertionError("Do not block cleanup on main drift"))
            try:
                with mock.patch.dict(os.environ,env,clear=True):
                    with self.assertRaises(SystemExit) as ex:self.l["main"]()
                self.assertEqual(0,ex.exception.code);self.assertEqual(["cleanup"],calls)
            finally:self.l["execute_verified"]=original;self.l["current_main"]=api
    def test_inherited_proxy_is_rejected_before_any_tokened_api_call(self):
        with tempfile.TemporaryDirectory() as d:
            env=self.main_env(d,"gate");env["HTTPS_PROXY"]="http://synthetic.invalid"
            original=self.l["current_main"]
            self.l["current_main"]=mock.Mock(return_value="a"*40)
            try:
                with mock.patch.dict(os.environ,env,clear=True):
                    with self.assertRaises(self.l["Unsafe"]):self.l["main"]()
                self.l["current_main"].assert_not_called()
            finally:self.l["current_main"]=original

    def test_topology_rejects_upload_bypass_and_failure_masking(self):
        text=WORKFLOW.read_text(encoding="utf-8")
        for changed in [
            text.replace("    environment: constia-c3c-isolation","    environment: other"),
            text.replace("steps.evidence.outputs.diagnostic_valid == 'true'","true"),
            text.replace("steps.evidence.outcome == 'success'","true"),
            text.replace("      - name: Run only isolation","      - name: Run only isolation\n        continue-on-error: true")]:
            with self.assertRaises(AssertionError):validate_topology(changed)
    def test_H2_diff_cannot_expand_beyond_reviewed_paths(self):
        case=LauncherTests("test_real_git_checkout_is_verified_without_executing_hooks");case.setUp()
        with tempfile.TemporaryDirectory() as d:
            root,git,p=case.repository(d)
            (root/"extra.txt").write_bytes(b"extra")
            git("add","--","extra.txt")
            git("-c","user.name=Local Test","-c","user.email=test@example.invalid","commit","--amend","--no-edit","--quiet")
            p.update(h=git("rev-parse","HEAD"),tree=git("rev-parse","HEAD^{tree}"))
            with self.assertRaisesRegex(self.l["Unsafe"],"Unexpected changed paths"):self.l["verify_checkout"](root,**p)


def write_h2_early_failure(case, original_changes=None, primary_changes=None, final_changes=None):
    """Mirror H2 diagnosed_main: immutable execution BEFORE primary/final cleanup."""
    original=copy.deepcopy(case.record)
    original.update(execution="failed_before_creation",creation_attempted=False,journal_state="absent",journal_seen=False,
        cleanup="uncertain",cleanup_scope="unknown",operation_failure=None,proof_state="absent",proof_sha256=None,
        first_failure=dict(stage="engine_context",code="ENGINE_REJECTED",reason="CONTEXT_OR_QUERY"),
        last_completed_stage="journal_check",session=dict(nonce=None,manifest_sha256=None,producer_state="no_dispatch"))
    if original_changes:original.update(copy.deepcopy(original_changes))
    raw=case.body(original)
    (case.diag/"isolation-execution.json").write_bytes(raw)
    primary=copy.deepcopy(original)
    primary.update(kind="cleanup_primary",execution_sha256=hashlib.sha256(raw).hexdigest(),last_completed_stage="evidence_write",
        cleanup="not_applicable",cleanup_scope="this_invocation_no_dispatch",journal_state="absent",journal_seen=False)
    if primary_changes:primary.update(copy.deepcopy(primary_changes))
    primary_raw=case.body(primary);(case.diag/"isolation-cleanup-primary.json").write_bytes(primary_raw)
    final=copy.deepcopy(primary)
    final.update(kind="cleanup_final",primary_sha256=hashlib.sha256(primary_raw).hexdigest(),last_completed_stage="authorization")
    if final_changes:final.update(copy.deepcopy(final_changes))
    (case.diag/"isolation-cleanup-final.json").write_bytes(case.body(final))
    return original,primary,final

class C2TemporalTests(unittest.TestCase):
    context=LauncherTests.context
    setUp=L2EvidenceTests.setUp
    body=L2EvidenceTests.body
    validate=L2EvidenceTests.validate
    main_env=L2StageTests.main_env

    def test_real_early_failure_sequence_is_valid_without_rewriting_execution(self):
        frames=write_h2_early_failure(self)
        path=self.diag/"isolation-execution.json";before=path.read_bytes()
        self.assertFalse((self.root/"c3c02b-123.json").exists())
        self.assertEqual("uncertain",frames[0]["cleanup"])
        self.assertEqual("unknown",frames[0]["cleanup_scope"])
        self.assertEqual("not_applicable",frames[1]["cleanup"])
        self.assertEqual("not_applicable",frames[2]["cleanup"])
        projection=self.validate()
        self.assertTrue(projection["diagnostic_valid"]);self.assertFalse(projection["isolation_verified"])
        self.assertEqual("failure",projection["outcomes"]["run"])
        self.assertEqual(frames[0]["first_failure"],projection["first_failure"])
        self.assertEqual(before,path.read_bytes())

    def test_failure_before_journal_check_is_not_invented_as_already_absent(self):
        write_h2_early_failure(self,dict(journal_state="unknown",last_completed_stage="git_status",
            first_failure=dict(stage="inventory",code="INVENTORY_REJECTED",reason="INVALID")))
        self.assertTrue(self.validate()["diagnostic_valid"])

    def test_failed_manifest_write_before_link_has_no_dispatch_proof(self):
        write_h2_early_failure(self,dict(journal_state="write_failed",last_completed_stage="engine_context",
            first_failure=dict(stage="journal_write",code="JOURNAL_WRITE_FAILED",reason="IO_FAILED")))
        self.assertTrue(self.validate()["diagnostic_valid"])

    def test_broken_execution_or_primary_hash_is_rejected(self):
        write_h2_early_failure(self)
        for name in ("isolation-execution.json","isolation-cleanup-primary.json"):
            path=self.diag/name;raw=path.read_bytes();path.write_bytes(raw+b" ")
            with self.subTest(name=name),self.assertRaises(self.l["Unsafe"]):self.validate()
            path.write_bytes(raw)

    def test_initial_cleanup_cannot_be_retroactively_completed(self):
        write_h2_early_failure(self,dict(cleanup="not_applicable",cleanup_scope="this_invocation_no_dispatch"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_initial_completed_execution_without_dispatch_is_impossible(self):
        write_h2_early_failure(self,dict(execution="completed"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_missing_or_changed_first_failure_is_rejected_with_rehashed_chain(self):
        write_h2_early_failure(self,dict(first_failure=None))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_early_failure(self,primary_changes=dict(first_failure=dict(stage="unexpected",code="UNEXPECTED_FAILURE",reason="UNCLASSIFIED")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_uncertainty_is_preserved_when_a_journal_was_seen(self):
        uncertain=dict(cleanup="uncertain",cleanup_scope="unknown",journal_state="invalid",journal_seen=True,
            operation_failure=dict(stage="journal_check",code="JOURNAL_REJECTED",reason="INVALID"))
        write_h2_early_failure(self,dict(journal_state="preexisting",journal_seen=True,last_completed_stage="inventory",
            first_failure=dict(stage="journal_check",code="JOURNAL_REJECTED",reason="PREEXISTING")),uncertain)
        self.outcomes["cleanup"]="failure"
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertEqual("uncertain",p["cleanup"])
        self.assertFalse(p["isolation_verified"])

    def test_missing_manifest_cannot_erase_prior_observation_or_claim_verified(self):
        write_h2_early_failure(self,dict(journal_seen=True,journal_state="preexisting"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_early_failure(self,final_changes=dict(cleanup="verified",cleanup_scope="managed_session_resources",journal_state="valid",journal_seen=True))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_primary_final_uncertainty_requires_an_observed_failure(self):
        write_h2_early_failure(self,final_changes=dict(cleanup="uncertain",cleanup_scope="unknown",journal_state="invalid",
            operation_failure=None))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_incompatible_later_cleanup_and_false_initial_journal_state_rejected(self):
        write_h2_early_failure(self,primary_changes=dict(operation_failure=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_early_failure(self,dict(journal_state="valid"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_missing_execution_witness_or_unknown_producer_rejected(self):
        write_h2_early_failure(self);(self.diag/"isolation-execution.json").unlink()
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_early_failure(self,dict(session=dict(nonce=None,manifest_sha256=None,producer_state="unknown")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_duplicate_record_kind_extra_keys_and_duplicate_json_keys_rejected(self):
        write_h2_early_failure(self,primary_changes=dict(kind="execution"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_early_failure(self,dict(extra="private@example.invalid"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_early_failure(self)
        path=self.diag/"isolation-execution.json";raw=path.read_bytes()
        path.write_bytes(raw.replace(b'"schema":2',b'"schema":2,"schema":2',1))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_valid_failure_publication_does_not_make_final_step_successful(self):
        write_h2_early_failure(self)
        env=self.main_env(self.root,"evidence")
        env.update(TESTS_OUTCOME="success",RUN_OUTCOME="failure",CLEANUP_OUTCOME="success")
        with mock.patch.dict(os.environ,env,clear=True):self.l["main"]()
        output=(self.root/"output").read_text()
        self.assertIn("diagnostic_valid=true",output);self.assertIn("isolation_verified=false",output)
        env.update(LAUNCHER_STAGE="final",DIAGNOSTIC_VALID="true",ISOLATION_VERIFIED="false")
        with mock.patch.dict(os.environ,env,clear=True):
            with self.assertRaises(self.l["Unsafe"]):self.l["main"]()

    def test_invalid_failure_evidence_never_reaches_publication(self):
        write_h2_early_failure(self)
        (self.diag/"isolation-execution.json").unlink()
        env=self.main_env(self.root,"evidence")
        env.update(TESTS_OUTCOME="success",RUN_OUTCOME="failure",CLEANUP_OUTCOME="success")
        original=self.l["publish_projection"];self.l["publish_projection"]=mock.Mock()
        try:
            with mock.patch.dict(os.environ,env,clear=True):
                with self.assertRaises(self.l["Unsafe"]):self.l["main"]()
            self.l["publish_projection"].assert_not_called()
            self.assertFalse((self.root/"output").exists())
        finally:self.l["publish_projection"]=original


def write_h2_manifest_failure(case, original_changes=None, primary_changes=None, final_changes=None):
    """Wire snapshots contrasted with H2 diagnosed_main under mocked resource boundaries."""
    case.record["last_completed_stage"]="isolation_setup"
    case.record["session"]["manifest_sha256"]=hashlib.sha256(case.body(case.manifest)).hexdigest()
    if case.proof is not None:
        case.record.update(proof_state="candidate_valid",proof_sha256=hashlib.sha256(case.body(case.proof)).hexdigest())
    if original_changes:case.record.update(copy.deepcopy(original_changes))
    if not original_changes or "last_completed_stage" not in original_changes:
        stage=case.record["first_failure"].get("stage") if type(case.record["first_failure"]) is dict else None
        case.record["last_completed_stage"]="connectivity" if case.record["execution"]=="completed" else {
            "build_context":"journal_write","resource_image":"journal_write","resource_network":"build_context",
            "resource_app":"isolation_setup","resource_peer":"resource_app","resource_denied":"resource_peer",
            "isolation_setup":"resource_network","connectivity":"isolation_setup","evidence":"isolation_setup",
            "evidence_write":"evidence"}.get(stage,"isolation_setup")
    case.primary=copy.deepcopy(case.record)
    case.primary.update(cleanup="verified",cleanup_scope="managed_session_resources",last_completed_stage="cleanup")
    if primary_changes:case.primary.update(copy.deepcopy(primary_changes))
    if not primary_changes or "last_completed_stage" not in primary_changes:
        case.primary["last_completed_stage"]="cleanup" if case.primary["cleanup"]=="verified" else "evidence_write"
    case.final=copy.deepcopy(case.primary)
    if final_changes:case.final.update(copy.deepcopy(final_changes))
    if not final_changes or "last_completed_stage" not in final_changes:
        case.final["last_completed_stage"]="cleanup" if case.final["cleanup"]=="verified" else "authorization"
    case.write()


class C3FirstFailureTests(unittest.TestCase):
    context=LauncherTests.context
    setUp=L2EvidenceTests.setUp
    body=L2EvidenceTests.body
    write=L2EvidenceTests.write
    validate=L2EvidenceTests.validate
    successful=L2EvidenceTests.successful
    main_env=L2StageTests.main_env

    def test_failed_manifest_execution_without_first_failure_is_rejected(self):
        write_h2_manifest_failure(self,dict(first_failure=None))
        self.assertTrue((self.root/"c3c02b-123.json").is_file())
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_failed_manifest_execution_preserves_valid_first_failure(self):
        write_h2_manifest_failure(self)
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])
        self.assertEqual(dict(stage="connectivity",code="CONNECTIVITY_UNVERIFIED",reason="CHECK_FAILED"),p["first_failure"])

    def test_later_record_cannot_change_or_forget_first_failure(self):
        for name in ("primary","final"):
            for failure in (None,dict(stage="unexpected",code="UNEXPECTED_FAILURE",reason="UNCLASSIFIED")):
                with self.subTest(record=name,failure=failure):
                    changes={name+"_changes":dict(first_failure=failure)}
                    write_h2_manifest_failure(self,**changes)
                    with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_cleanup_failure_cannot_replace_execution_failure(self):
        cleanup=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")
        write_h2_manifest_failure(self,primary_changes=dict(first_failure=cleanup,operation_failure=cleanup,cleanup="failed"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_unknown_code_impossible_triple_or_wrong_type_rejected(self):
        for failure in (dict(stage="connectivity",code="NEW_CODE",reason="CHECK_FAILED"),
                        dict(stage="cleanup",code="CONNECTIVITY_UNVERIFIED",reason="CHECK_FAILED"),
                        "CONNECTIVITY_UNVERIFIED",[],True):
            with self.subTest(failure=failure):
                write_h2_manifest_failure(self,dict(first_failure=failure))
                with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_completed_execution_cannot_claim_an_earlier_connectivity_failure(self):
        self.successful()
        write_h2_manifest_failure(self,dict(first_failure=dict(stage="connectivity",code="CONNECTIVITY_UNVERIFIED",reason="CHECK_FAILED")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_completed_execution_without_first_failure_remains_valid(self):
        self.successful();write_h2_manifest_failure(self)
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertTrue(p["isolation_verified"])
        self.assertIsNone(p["first_failure"])

    def test_completed_execution_can_fail_later_during_candidate_proof_validation(self):
        self.record.update(execution="completed",proof_state="invalid")
        write_h2_manifest_failure(self,dict(first_failure=dict(stage="evidence",code="EVIDENCE_INVALID",reason="INVALID")))
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])

    def test_completed_execution_cleanup_failure_does_not_invent_first_failure(self):
        self.successful()
        failure=dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN")
        write_h2_manifest_failure(self,primary_changes=dict(cleanup="uncertain",cleanup_scope="unknown",operation_failure=failure))
        self.outcomes["cleanup"]="failure"
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])
        self.assertIsNone(p["first_failure"]);self.assertEqual(failure,p["cleanup_failure"])

    def test_early_failure_keeps_c2_temporal_contract(self):
        frames=write_h2_early_failure(self)
        self.assertEqual("uncertain",frames[0]["cleanup"])
        self.assertTrue(self.validate()["diagnostic_valid"])
        write_h2_early_failure(self,dict(cleanup="not_applicable",cleanup_scope="this_invocation_no_dispatch"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_incomplete_evidence_never_certifies_isolation(self):
        write_h2_manifest_failure(self)
        (self.diag/"isolation-cleanup-primary.json").unlink()
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_invalid_failure_cannot_emit_positive_outputs_or_artifact(self):
        write_h2_manifest_failure(self,dict(first_failure=None))
        env=self.main_env(self.root,"evidence")
        env.update(TESTS_OUTCOME="success",RUN_OUTCOME="failure",CLEANUP_OUTCOME="success")
        with mock.patch.dict(os.environ,env,clear=True):
            with self.assertRaises(self.l["Unsafe"]):self.l["main"]()
        self.assertFalse((self.root/"output").exists())
        self.assertFalse(list(self.root.glob("c3c02b-l2-public-*")))

    def test_projection_revalidation_rejects_missing_failure_and_false_success(self):
        write_h2_manifest_failure(self)
        p=self.validate();p["first_failure"]=None
        with self.assertRaises(self.l["Unsafe"]):self.l["validate_projection"](p)
        p=self.validate();p["isolation_verified"]=True
        with self.assertRaises(self.l["Unsafe"]):self.l["validate_projection"](p)


# Independent literal matrix from H2 62543ba8 emission sites, not from L2 policy.
# tuple: (stage, code, reason), first_failure location, primary/final operation roles, H2 lines.
H2_FAILURE_EMISSIONS = (
    (("cli","INPUT_REJECTED","INVALID"),"never",(),(1527,1529)),
    (("authorization","AUTH_REJECTED","POLICY"),"never",(),(1545,1547)),
    (("git_head","GIT_REJECTED","QUERY_FAILED"),"early",(),(940,1140)),
    (("git_head","GIT_REJECTED","HEAD_MISMATCH"),"early",(),(941,1152)),
    (("git_status","GIT_REJECTED","QUERY_FAILED"),"early",(),(942,1140)),
    (("git_status","GIT_REJECTED","DIRTY"),"early",(),(943,1152)),
    (("inventory","INVENTORY_REJECTED","INVALID"),"early",(),(944,1140)),
    (("journal_check","JOURNAL_REJECTED","PREEXISTING"),"early",(),(945,1653)),
    (("journal_check","JOURNAL_REJECTED","INVALID"),"early",("unbound_primary","final"),(724,1648)),
    (("journal_write","JOURNAL_WRITE_FAILED","IO_FAILED"),"early",(),(217,1141)),
    (("engine_context","ENGINE_REJECTED","CONTEXT_OR_QUERY"),"early",(),(946,1140)),
    (("build_context","BUILD_CONTEXT_FAILED","PREPARATION"),"bound",(),(916,952)),
    (("resource_image","RESOURCE_UNVERIFIED","UNCONFIRMED"),"bound",(),(497,510,928)),
    (("resource_network","RESOURCE_UNVERIFIED","UNCONFIRMED"),"bound",(),(497,510,953)),
    (("resource_app","RESOURCE_UNVERIFIED","UNCONFIRMED"),"bound",(),(497,510,960)),
    (("resource_peer","RESOURCE_UNVERIFIED","UNCONFIRMED"),"bound",(),(497,510,960)),
    (("resource_denied","RESOURCE_UNVERIFIED","UNCONFIRMED"),"bound",(),(497,510,960)),
    (("isolation_setup","ISOLATION_UNVERIFIED","CHECK_FAILED"),"bound",(),(954,965,1022)),
    (("connectivity","CONNECTIVITY_UNVERIFIED","CHECK_FAILED"),"bound",(),(997,1008,1033)),
    (("cleanup","CLEANUP_UNVERIFIED","UNKNOWN"),"never",("primary","final"),(715,725)),
    (("cleanup_remove","CLEANUP_FAILED","REMOVE_FAILED"),"never",("primary","final"),(683,699)),
    (("evidence","EVIDENCE_INVALID","INVALID"),"both",("final",),(1514,1553,1596,1612,1623,1627)),
    (("evidence_write","EVIDENCE_WRITE_FAILED","IO_FAILED"),"bound",(),(1026,1520)),
    (("unexpected","UNEXPECTED_FAILURE","UNCLASSIFIED"),"never",(),(1089,1558)),
)


class C4FailureContextTests(unittest.TestCase):
    context=LauncherTests.context
    setUp=L2EvidenceTests.setUp
    body=L2EvidenceTests.body
    write=L2EvidenceTests.write
    validate=L2EvidenceTests.validate
    successful=L2EvidenceTests.successful
    main_env=L2StageTests.main_env

    def test_cleanup_code_cannot_be_first_failure_even_in_coherent_chain(self):
        write_h2_manifest_failure(self,dict(first_failure=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_real_cleanup_failure_is_separate_from_original_first_failure(self):
        failure=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")
        write_h2_manifest_failure(self,primary_changes=dict(operation_failure=failure,cleanup="failed",cleanup_scope="unknown"))
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])
        self.assertEqual("CONNECTIVITY_UNVERIFIED",p["first_failure"]["code"])
        self.assertEqual(failure,p["cleanup_failure"])

    def test_literal_emission_matrix_matches_contextual_acceptance(self):
        for triple,location,roles,lines in H2_FAILURE_EMISSIONS:
            failure=dict(zip(("stage","code","reason"),triple))
            for bound in (False,True):
                record=copy.deepcopy(self.record)
                record["session"]["manifest_sha256"]="d"*64
                if not bound:
                    record.update(execution="failed_before_creation",creation_attempted=False,
                                  journal_state="unknown",journal_seen=False,
                                  session=dict(nonce=None,manifest_sha256=None,producer_state="no_dispatch"))
                record["first_failure"]=failure
                if not bound and location in ("early","both"):
                    # Literal predecessor table from actual successful calls before each early failure.
                    previous={("git_head","QUERY_FAILED"):"evidence",("git_head","HEAD_MISMATCH"):"git_head",
                              ("git_status","QUERY_FAILED"):"git_head",("git_status","DIRTY"):"git_status",
                              ("inventory","INVALID"):"git_status",("journal_check","PREEXISTING"):"inventory",
                              ("journal_check","INVALID"):"inventory",("journal_write","IO_FAILED"):"engine_context",
                              ("engine_context","CONTEXT_OR_QUERY"):"journal_check",("evidence","INVALID"):"authorization"}
                    record["last_completed_stage"]=previous[(triple[0],triple[2])]
                    if triple[0]=="engine_context":record["journal_state"]="absent"
                    elif triple[0]=="journal_write":record["journal_state"]="write_failed"
                    elif triple==("journal_check","JOURNAL_REJECTED","PREEXISTING"):
                        record.update(journal_state="preexisting",journal_seen=True)
                if bound and location in ("bound","both"):
                    record["last_completed_stage"]={"build_context":"journal_write","resource_image":"journal_write",
                        "resource_network":"build_context","resource_app":"isolation_setup","resource_peer":"resource_app",
                        "resource_denied":"resource_peer","isolation_setup":"resource_network","connectivity":"isolation_setup",
                        "evidence":"isolation_setup","evidence_write":"evidence"}[triple[0]]
                permitted=location in (("bound","both") if bound else ("early","both"))
                with self.subTest(triple=triple,field="first_failure",bound=bound,source_lines=lines):
                    if permitted:self.l["validate_record"](record,"execution",self.e)
                    else:
                        with self.assertRaises(self.l["Unsafe"]):self.l["validate_record"](record,"execution",self.e)
            for kind,role in (("execution","execution"),("cleanup_primary","primary"),("cleanup_final","final")):
                record=copy.deepcopy(self.record);record.update(kind=kind,operation_failure=failure)
                record["session"]["manifest_sha256"]="d"*64
                if kind!="execution":
                    record.update(cleanup="uncertain",cleanup_scope="unknown",execution_sha256="e"*64,
                        last_completed_stage="evidence_write" if kind=="cleanup_primary" else "authorization")
                    if kind=="cleanup_final":record["primary_sha256"]="f"*64
                    if triple[0]=="cleanup_remove":record["cleanup"]="failed"
                    if triple[0]=="journal_check":record["journal_state"]="invalid"
                    if triple[0]=="evidence":record["proof_state"]="invalid"
                with self.subTest(triple=triple,field="operation_failure",kind=kind,source_lines=lines):
                    if role in roles:self.l["validate_record"](record,kind,self.e)
                    else:
                        with self.assertRaises(self.l["Unsafe"]):self.l["validate_record"](record,kind,self.e)

    def test_execution_codes_are_not_cleanup_operation_failures(self):
        for stage,code,reason in (("connectivity","CONNECTIVITY_UNVERIFIED","CHECK_FAILED"),
                                  ("evidence_write","EVIDENCE_WRITE_FAILED","IO_FAILED"),
                                  ("journal_write","JOURNAL_WRITE_FAILED","IO_FAILED")):
            write_h2_manifest_failure(self,primary_changes=dict(cleanup="uncertain",cleanup_scope="unknown",
                operation_failure=dict(stage=stage,code=code,reason=reason)))
            with self.subTest(stage=stage),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_final_cleanup_can_recover_without_repeating_primary_operation_failure(self):
        failure=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")
        write_h2_manifest_failure(self,primary_changes=dict(operation_failure=failure,cleanup="failed",cleanup_scope="unknown"),
            final_changes=dict(operation_failure=None,cleanup="verified",cleanup_scope="managed_session_resources"))
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])
        self.assertEqual("verified",p["cleanup"]);self.assertEqual(failure,p["cleanup_failure"])

    def test_initial_snapshot_cannot_claim_completed_cleanup(self):
        for state,scope in (("verified","managed_session_resources"),("failed","unknown")):
            write_h2_manifest_failure(self,dict(cleanup=state,cleanup_scope=scope))
            with self.subTest(state=state),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_closed_producer_must_match_dispatch_evidence(self):
        write_h2_manifest_failure(self,dict(session=dict(nonce=self.nonce,manifest_sha256=None,producer_state="unknown")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_manifest_failure(self,dict(execution="failed_before_creation",creation_attempted=False))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_unresolved_producer_has_uncertain_cleanup_with_observed_failure(self):
        self.record["session"]["producer_state"]="unresolved"
        write_h2_manifest_failure(self,primary_changes=dict(cleanup="uncertain",cleanup_scope="unknown"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_manifest_failure(self,primary_changes=dict(cleanup="uncertain",cleanup_scope="unknown",
            operation_failure=dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN")))
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])

    def test_cleanup_result_must_match_actual_failure_classification(self):
        failure=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")
        for cleanup,operation in (("verified",failure),("uncertain",failure),
            ("failed",dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN"))):
            write_h2_manifest_failure(self,primary_changes=dict(cleanup=cleanup,cleanup_scope="unknown",operation_failure=operation))
            with self.subTest(cleanup=cleanup,operation=operation),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_primary_does_not_emit_final_evidence_read_failure(self):
        write_h2_manifest_failure(self,primary_changes=dict(cleanup="uncertain",cleanup_scope="unknown",
            operation_failure=dict(stage="evidence",code="EVIDENCE_INVALID",reason="INVALID")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_final_evidence_operation_is_not_recast_as_initial_failure(self):
        # Per-record reachability: full bundle certification still requires proof/hash continuity.
        rec=copy.deepcopy(self.record);rec.update(kind="cleanup_final",execution_sha256="e"*64,primary_sha256="f"*64,
            cleanup="uncertain",last_completed_stage="authorization",proof_state="invalid",operation_failure=dict(stage="evidence",code="EVIDENCE_INVALID",reason="INVALID"))
        rec["session"]["manifest_sha256"]="d"*64
        self.l["validate_record"](rec,"cleanup_final",self.e)
        self.assertEqual("CONNECTIVITY_UNVERIFIED",rec["first_failure"]["code"])

    def test_early_failure_cannot_retain_a_post_admission_first_failure(self):
        write_h2_early_failure(self,dict(first_failure=dict(stage="connectivity",code="CONNECTIVITY_UNVERIFIED",reason="CHECK_FAILED")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_admitted_session_cannot_retain_a_pre_admission_first_failure(self):
        write_h2_manifest_failure(self,dict(first_failure=dict(stage="engine_context",code="ENGINE_REJECTED",reason="CONTEXT_OR_QUERY")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_bound_pre_dispatch_build_failure_remains_legitimate(self):
        self.record["session"]["producer_state"]="no_dispatch"
        write_h2_manifest_failure(self,dict(execution="failed_before_creation",creation_attempted=False,
            first_failure=dict(stage="build_context",code="BUILD_CONTEXT_FAILED",reason="PREPARATION")))
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])

    def test_unknown_types_phase_and_broken_references_remain_rejected(self):
        for change in (dict(first_failure=False),dict(first_failure={"stage":"cleanup_remove","code":"PRIVATE","reason":"INVALID"}),
                       dict(phase="operational-full")):
            write_h2_manifest_failure(self,change)
            with self.subTest(change=change),self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_manifest_failure(self)
        path=self.diag/"isolation-cleanup-primary.json";raw=path.read_bytes()
        path.write_bytes(raw+b" ")
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_publication_revalidation_rejects_misattributed_cleanup_failure(self):
        write_h2_manifest_failure(self)
        p=self.validate()
        p["first_failure"]=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")
        with self.assertRaises(self.l["Unsafe"]):self.l["publish_projection"](self.root,p)
        self.assertFalse(list(self.root.glob("c3c02b-l2-public-*")))

    def test_publication_revalidation_rejects_execution_as_cleanup_failure(self):
        write_h2_manifest_failure(self)
        p=self.validate();p["cleanup_failure"]=dict(stage="connectivity",code="CONNECTIVITY_UNVERIFIED",reason="CHECK_FAILED")
        with self.assertRaises(self.l["Unsafe"]):self.l["publish_projection"](self.root,p)

    def test_completed_stage_must_belong_to_snapshot_time(self):
        mutations=[(dict(last_completed_stage="cleanup"),None,None),
                   (None,dict(last_completed_stage="authorization"),None),
                   (None,None,dict(last_completed_stage="evidence_write"))]
        for original,primary,final in mutations:
            write_h2_manifest_failure(self,original,primary,final)
            with self.subTest(mutation=(original,primary,final)),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_completed_state_cannot_hide_unresolved_producer(self):
        self.record.update(execution="completed",first_failure=None)
        self.record["session"]["producer_state"]="unresolved"
        write_h2_manifest_failure(self,primary_changes=dict(cleanup="uncertain",cleanup_scope="unknown",
            operation_failure=dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_manifest_cleanup_cannot_report_pre_admission_journal_states(self):
        for kind in ("primary","final"):
            for state in ("absent","write_failed"):
                changes={kind+"_changes":dict(cleanup="uncertain",cleanup_scope="unknown",journal_state=state,
                    operation_failure=dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN"))}
                write_h2_manifest_failure(self,**changes)
                with self.subTest(kind=kind,state=state),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_early_first_failure_requires_actual_predecessor_stage(self):
        failures=[dict(stage="git_head",code="GIT_REJECTED",reason="QUERY_FAILED"),
                  dict(stage="inventory",code="INVENTORY_REJECTED",reason="INVALID")]
        for failure in failures:
            write_h2_early_failure(self,dict(first_failure=failure,last_completed_stage="connectivity"))
            with self.subTest(failure=failure),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_absence_cleanup_stage_is_specific_to_each_invocation(self):
        write_h2_early_failure(self,primary_changes=dict(last_completed_stage="cleanup_remove"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_early_failure(self,final_changes=dict(last_completed_stage="cleanup"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_verified_cleanup_cannot_precede_completed_cleanup_stage(self):
        write_h2_manifest_failure(self,primary_changes=dict(last_completed_stage="evidence_write"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_manifest_failure(self,final_changes=dict(last_completed_stage="authorization"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_completed_evidence_failure_requires_failed_candidate_state(self):
        self.successful()
        write_h2_manifest_failure(self,dict(first_failure=dict(stage="evidence",code="EVIDENCE_INVALID",reason="INVALID")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_original_cannot_import_final_proof_unavailability(self):
        write_h2_manifest_failure(self,dict(proof_state="unavailable",proof_sha256=None))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_proof_changes_require_the_actual_revalidation_phase(self):
        write_h2_manifest_failure(self,primary_changes=dict(proof_state="invalid"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()
        write_h2_manifest_failure(self,final_changes=dict(proof_state="invalid",cleanup="uncertain",cleanup_scope="unknown",
            operation_failure=dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_removal_failure_does_not_complete_outer_cleanup_call(self):
        failure=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")
        for kind in ("primary","final"):
            write_h2_manifest_failure(self,**{kind+"_changes":dict(operation_failure=failure,cleanup="failed",
                cleanup_scope="unknown",last_completed_stage="cleanup")})
            with self.subTest(kind=kind),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_error_after_engine_check_can_keep_that_completed_stage(self):
        write_h2_early_failure(self,dict(last_completed_stage="engine_context"))
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])

    def test_invalid_candidate_cannot_omit_the_failure_it_emitted(self):
        write_h2_manifest_failure(self,dict(execution="completed",first_failure=None,proof_state="invalid"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_pre_dispatch_admitted_producer_can_close_unresolved(self):
        self.record["session"]["producer_state"]="unresolved"
        write_h2_manifest_failure(self,dict(execution="failed_before_creation",creation_attempted=False,
            first_failure=dict(stage="resource_image",code="RESOURCE_UNVERIFIED",reason="UNCONFIRMED")),
            primary_changes=dict(cleanup="uncertain",cleanup_scope="unknown",
                operation_failure=dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN")))
        p=self.validate()
        self.assertTrue(p["diagnostic_valid"]);self.assertFalse(p["isolation_verified"])

    def test_early_failure_cannot_claim_a_later_journal_state(self):
        first=dict(stage="git_head",code="GIT_REJECTED",reason="QUERY_FAILED")
        for state,seen in (("write_failed",False),("preexisting",True),("absent",False)):
            write_h2_early_failure(self,dict(first_failure=first,last_completed_stage="evidence",journal_state=state,journal_seen=seen))
            with self.subTest(state=state),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_final_context_cannot_recover_old_write_failed_journal_state(self):
        failure=dict(stage="cleanup",code="CLEANUP_UNVERIFIED",reason="UNKNOWN")
        write_h2_early_failure(self,dict(first_failure=dict(stage="journal_write",code="JOURNAL_WRITE_FAILED",reason="IO_FAILED"),
            last_completed_stage="engine_context",journal_state="write_failed",journal_seen=True),
            dict(journal_state="write_failed",journal_seen=True,cleanup="uncertain",cleanup_scope="unknown",operation_failure=failure),
            dict(journal_state="write_failed",journal_seen=True,cleanup="uncertain",cleanup_scope="unknown",operation_failure=failure))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_final_evidence_error_requires_its_candidate_revalidation_state(self):
        record=copy.deepcopy(self.record)
        record.update(kind="cleanup_final",last_completed_stage="authorization",execution_sha256="e"*64,primary_sha256="f"*64,
            cleanup="uncertain",operation_failure=dict(stage="evidence",code="EVIDENCE_INVALID",reason="INVALID"))
        record["session"]["manifest_sha256"]="d"*64
        with self.assertRaises(self.l["Unsafe"]):self.l["validate_record"](record,"cleanup_final",self.e)

    def test_bound_failure_must_follow_actual_resource_order(self):
        write_h2_manifest_failure(self,dict(first_failure=dict(stage="resource_denied",code="RESOURCE_UNVERIFIED",reason="UNCONFIRMED"),
            last_completed_stage="journal_write"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_completed_execution_must_have_completed_receiver_stop(self):
        self.successful()
        write_h2_manifest_failure(self,dict(last_completed_stage="journal_write"))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_admitted_no_dispatch_cannot_claim_post_dispatch_evidence_failure(self):
        self.record["session"]["producer_state"]="no_dispatch"
        write_h2_manifest_failure(self,dict(execution="failed_before_creation",creation_attempted=False,
            first_failure=dict(stage="evidence",code="EVIDENCE_INVALID",reason="INVALID")))
        with self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_pre_dispatch_snapshot_cannot_include_a_completed_image_dispatch(self):
        self.record["session"]["producer_state"]="no_dispatch"
        for stage in ("resource_image","build_context"):
            write_h2_manifest_failure(self,dict(execution="failed_before_creation",creation_attempted=False,
                first_failure=dict(stage="build_context",code="BUILD_CONTEXT_FAILED",reason="PREPARATION"),
                last_completed_stage=stage))
            with self.subTest(last=stage),self.assertRaises(self.l["Unsafe"]):self.validate()

    def test_contextual_failure_diagnostic_does_not_authorize_global_success(self):
        failure=dict(stage="cleanup_remove",code="CLEANUP_FAILED",reason="REMOVE_FAILED")
        write_h2_manifest_failure(self,primary_changes=dict(operation_failure=failure,cleanup="failed",cleanup_scope="unknown"))
        env=self.main_env(self.root,"evidence")
        env.update(TESTS_OUTCOME="success",RUN_OUTCOME="failure",CLEANUP_OUTCOME="failure")
        with mock.patch.dict(os.environ,env,clear=True):self.l["main"]()
        self.assertIn("diagnostic_valid=true",(self.root/"output").read_text())
        self.assertIn("isolation_verified=false",(self.root/"output").read_text())
        env.update(LAUNCHER_STAGE="final",DIAGNOSTIC_VALID="true",ISOLATION_VERIFIED="false")
        with mock.patch.dict(os.environ,env,clear=True):
            with self.assertRaises(self.l["Unsafe"]):self.l["main"]()

if __name__ == "__main__":
    unittest.main()
