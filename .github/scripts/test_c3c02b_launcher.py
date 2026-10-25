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
    """Regression checks for a fixed textual graph, not a complete YAML parser."""
    def check(condition, diagnostic):
        # Unlike assert, this remains effective under Python -O and -OO.
        if not condition:
            raise AssertionError(diagnostic)

    trigger = text.split("on:\n", 1)[1].split("permissions:\n", 1)[0]
    check(trigger == "  workflow_dispatch:\n", "Only input-free workflow_dispatch is allowed")
    check(text.count("  C3C02B_PHASE: isolation-only\n") == 1, "Runtime phase must stay isolation-only")
    check(text.count("  CONSTIA_B3_APPROVED_PHASE: isolation-only\n") == 1, "Policy phase must stay isolation-only")
    gate, isolation = text.split("  isolation:\n", 1)
    check("uses:" not in gate and "environment:" not in gate, "Gate must remain read-only before authorization")
    gate_steps = [line.strip() for line in gate.splitlines() if line.startswith("      - name: ")]
    check(gate_steps == ["- name: Read-only identity gate"], "Unexpected step before authorization")
    check("    needs: gate\n" in isolation, "Isolation must depend on the gate")
    check("    environment: constia-c3c-isolation\n" in isolation, "Protected environment is mandatory")
    steps = isolation.split("    steps:\n", 1)[1].split("      - name: ")[1:]
    names = [step.splitlines()[0] for step in steps]
    check(names == ["Recheck trusted identity after approval", "Checkout only H",
                    "Verify H and every checkout byte from L", "Pure regressions from verified H",
                    "Run only isolation", "Cleanup only verified H resources",
                    "Validate allowlisted evidence from L", "Upload only validated evidence"],
          "Step order differs or an unexpected step was added")
    check("steps.checkout.outcome == 'success'" in steps[2], "Failed checkout cannot authorize verification")
    check("run: *trusted_verifier" in steps[2], "Verification must originate in L")
    check("always()" not in steps[3] + steps[4], "H must not run after failed verification")
    check("run --phase isolation-only\n" in steps[4], "Only isolation execution is allowed")
    check("cleanup --phase isolation-only\n" in steps[5], "Only isolation cleanup is allowed")
    check("steps.verified.outputs.verified == 'true'" in steps[5], "Cleanup needs verified H")
    check("steps.verified.outputs.verified == 'true'" in steps[6], "Evidence needs verified H")
    check("steps.evidence.outputs.safe == 'true'" in steps[7], "Upload needs validated evidence")


def topology_mutations(text):
    """Synthetic workflow variants, never written or executed as workflows."""
    return {
        "environment absent": text.replace("    environment: constia-c3c-isolation\n", ""),
        "gate dependency absent": text.replace("    needs: gate\n", ""),
        "checkout before gate": text.replace("      - name: Read-only identity gate",
            "      - name: Premature checkout\n        uses: actions/checkout@bad\n      - name: Read-only identity gate"),
        "H before verifier": text.replace("      - name: Verify H and every checkout byte from L",
            "      - name: Premature H\n        run: python3 harness/.github/scripts/c3c02b_runner.py\n"
            "      - name: Verify H and every checkout byte from L"),
        "verified condition absent": text.replace("steps.verified.outputs.verified == 'true'", "true"),
        "different phase": text.replace("  C3C02B_PHASE: isolation-only", "  C3C02B_PHASE: operational-full"),
        "unexpected step": text + "      - name: Unexpected step\n        run: echo forbidden\n",
        "checkout success guard absent": text.replace("steps.checkout.outcome == 'success'", "true"),
        "H forced after failure": text.replace("      - name: Pure regressions from verified H\n",
            "      - name: Pure regressions from verified H\n        if: ${{ always() }}\n"),
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
        self.assertEqual("435d97ef9c715ad7b8198b27b534122cef8d71d485628528e902ae3366d496b6",
                         hashlib.sha256(json.dumps(table, sort_keys=True, separators=(",", ":")).encode()).hexdigest())
        self.assertEqual("a6791f91f5c6a6654997025e2b71038161e24d4481141fdd01e24682f990fb81",
                         table["tests/sandbox/c3c02b/coverage.json"])

    def test_reject_incomplete_or_expanded_policy(self):
        for table in [{}, {**self.l["TABLE"], "extra.py": "a"*64}]:
            with self.assertRaises(self.l["Unsafe"]):
                self.l["validate_policy"](table)

    def repository(self, directory):
        root = pathlib.Path(directory)
        env = dict(__import__("os").environ, GIT_CONFIG_NOSYSTEM="1", GIT_CONFIG_GLOBAL=__import__("os").devnull)
        def git(*args):
            return subprocess.check_output(["git", "-C", str(root), *args], env=env, stderr=subprocess.DEVNULL).strip().decode()
        git("init", "--quiet")
        (root / "base.txt").write_bytes(b"base\n")
        git("add", "--", "base.txt")
        git("-c", "user.name=Local Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "base")
        base = git("rev-parse", "HEAD")
        (root / "option.txt").write_bytes(b"approved\n")
        git("add", "--", "option.txt")
        git("-c", "user.name=Local Test", "-c", "user.email=test@example.invalid", "commit", "-qm", "addition")
        return root, git, dict(h=git("rev-parse", "HEAD"), base=base, tree=git("rev-parse", "HEAD^{tree}"),
                               table={"option.txt": hashlib.sha256(b"approved\n").hexdigest()})

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
            for key in ["h", "base", "tree"]:
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
            (root / "option.txt").write_bytes(b"approved\r\n")
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
                    self.l["validate_artifacts"](root, self.context())

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
        for name in ("isolation.json", "isolation-execution.json", "isolation-sha256.json"):
            (root / name).write_bytes(b"{}")

    def test_allowed_artifact_symlink_is_rejected_before_reading_target(self):
        with tempfile.TemporaryDirectory() as t:
            parent = pathlib.Path(t)
            evidence = parent / "evidence"
            self.evidence_files(evidence)
            target = parent / "synthetic.txt"
            target.write_bytes(b"must not be read")
            (evidence / "isolation.json").unlink()
            self.symlink(evidence / "isolation.json", target)
            with self.assertRaisesRegex(self.l["Unsafe"], "Unsafe artifact"):
                self.l["validate_artifacts"](evidence, self.context())

    def test_unexpected_artifact_symlink_is_rejected_by_allowlist(self):
        with tempfile.TemporaryDirectory() as t:
            parent = pathlib.Path(t)
            evidence = parent / "evidence"
            self.evidence_files(evidence)
            target = parent / "synthetic.txt"
            target.write_bytes(b"must not be read")
            self.symlink(evidence / "unexpected.json", target)
            with self.assertRaisesRegex(self.l["Unsafe"], "Artifact allowlist differs"):
                self.l["validate_artifacts"](evidence, self.context())

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
                self.l["validate_artifacts"](link, self.context())

    def test_artifacts_require_matching_hashes_identity_and_cleanup(self):
        with tempfile.TemporaryDirectory() as t:
            root = pathlib.Path(t)
            env = self.context()
            identity = dict(code_sha=self.l["H"], policy_sha256=self.l["D"],
                            workflow_sha="a"*40, workflow_ref=env["GITHUB_WORKFLOW_REF"],
                            actor_id=env["GITHUB_ACTOR_ID"], run_id="123", attempt="1",
                            phase="isolation-only", repository=env["GITHUB_REPOSITORY"])
            report = dict(identity=identity, phase="isolation-only", sql="not_applicable", workers_started=0)
            result = dict(phase="isolation-only", code_sha=self.l["H"], policy_sha256=self.l["D"],
                          workflow_sha="a"*40, run_id="123", attempt="1", status="passed",
                          run_ok=True, cleanup_verified=True, evidence_complete=True,
                          sql="not_applicable", workers_started=0, resources=[])
            def write():
                (root / "isolation.json").write_text(json.dumps(report))
                (root / "isolation-execution.json").write_text(json.dumps(result))
                hashes = {n: hashlib.sha256((root / n).read_bytes()).hexdigest() for n in ["isolation.json", "isolation-execution.json"]}
                (root / "isolation-sha256.json").write_text(json.dumps(hashes))
            write()
            self.l["validate_artifacts"](root, env)
            result["cleanup_verified"] = False
            write()
            with self.assertRaises(self.l["Unsafe"]):
                self.l["validate_artifacts"](root, env)
            result["cleanup_verified"] = True
            report["identity"]["code_sha"] = "0"*40
            write()
            with self.assertRaises(self.l["Unsafe"]):
                self.l["validate_artifacts"](root, env)

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
        self.assertEqual(["Recheck trusted identity after approval", "Checkout only H",
                          "Verify H and every checkout byte from L", "Pure regressions from verified H",
                          "Run only isolation", "Cleanup only verified H resources",
                          "Validate allowlisted evidence from L", "Upload only validated evidence"], names)
        self.assertEqual(2, text.count("steps.verified.outputs.verified == 'true'"))
        self.assertIn("steps.evidence.outputs.safe == 'true'", text)
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


if __name__ == "__main__":
    unittest.main()
