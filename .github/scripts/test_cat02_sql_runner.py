"""Pure tests: no Docker, SQL, network or child process is invoked."""
import contextlib
import copy
import io
import json
from pathlib import Path
import re
import unittest
import tempfile
from unittest.mock import patch
import xml.etree.ElementTree as ET

import cat02_sql_runner as runner


def fixture():
    state = dict(nonce="d" * 32, runId="123", runAttempt="1", hostname="c2-" + "d" * 12,
                 imageId="sha256:" + "b" * 64,
                 imageDigest="mcr.microsoft.com/mssql/server@sha256:" + "c" * 64,
                 containerId="a" * 64, port=49152)
    ports = {"1433/tcp": [{"HostIp": "127.0.0.1", "HostPort": "49152"}]}
    container = dict(Id=state["containerId"], Image=state["imageId"], State={"Running": True}, Mounts=[],
                     Config={"Hostname": state["hostname"], "Image": state["imageDigest"], "Labels": {
                         runner.LABEL + "run": state["nonce"], runner.LABEL + "github-run": "123",
                         runner.LABEL + "attempt": "1", runner.LABEL + "scope": "cat02-sqlserver"}},
                     HostConfig={"NetworkMode": "bridge", "Privileged": False, "PidMode": "", "Binds": None,
                                 "VolumesFrom": None, "PortBindings": copy.deepcopy(ports)},
                     NetworkSettings={"Ports": ports})
    container["HostConfig"]["PortBindings"]["1433/tcp"][0]["HostPort"] = ""
    return state, container


def trx(methods=None, outcome="Passed", counters=None):
    ns = "{http://microsoft.com/schemas/VisualStudio/TeamTest/2010}"
    root = ET.Element(ns + "TestRun")
    definitions = ET.SubElement(root, ns + "TestDefinitions")
    results = ET.SubElement(root, ns + "Results")
    for i, method in enumerate(sorted(runner.METHODS) if methods is None else methods):
        test = ET.SubElement(definitions, ns + "UnitTest", id=str(i))
        ET.SubElement(test, ns + "TestMethod", className=runner.CLASS, name=method)
        ET.SubElement(results, ns + "UnitTestResult", testId=str(i), outcome=outcome)
    summary = ET.SubElement(root, ns + "ResultSummary", outcome="Completed")
    ET.SubElement(summary, ns + "Counters", **(counters or {
        "total": "5", "executed": "5", "passed": "5", "failed": "0", "error": "0", "notExecuted": "0"}))
    return io.StringIO(ET.tostring(root, encoding="unicode"))


class RunnerSafetyTests(unittest.TestCase):
    def test_fake_github_flag_cannot_enable_runner(self):
        with patch.object(runner.sys, "platform", "linux"):
            with self.assertRaises(runner.RunnerError):
                runner.require_ci({"GITHUB_ACTIONS": "true"})

    def test_hosted_context_rejects_overrides_and_arbitrary_connections(self):
        env = dict(GITHUB_ACTIONS="true", RUNNER_ENVIRONMENT="github-hosted", GITHUB_JOB="cat02-sqlserver",
                   GITHUB_RUN_ID="123", GITHUB_RUN_ATTEMPT="1", RUNNER_TEMP="/runner/temp")
        with patch.object(runner.sys, "platform", "linux"):
            runner.require_ci(env)
            for key, value in {"DOCKER_HOST": "tcp://other", "DOCKER_CONTEXT": "other",
                               "GITHUB_JOB": "build-and-test", "RUNNER_ENVIRONMENT": "self-hosted",
                               "CONSTIA_CAT02_SQLSERVER_CONNECTION": "Server=dev;Database=Constia"}.items():
                with self.subTest(key=key), self.assertRaises(runner.RunnerError):
                    runner.require_ci(dict(env, **{key: value}))

    def test_valid_identity_and_dynamic_port(self):
        state, container = fixture()
        self.assertEqual(49152, runner.validate_container(state, container))

    def test_foreign_container_or_nonisolated_target_rejected(self):
        for field in ("id", "label", "image", "hostname", "mount", "network", "public", "port", "stopped"):
            state, container = fixture()
            if field == "id": container["Id"] = "e" * 64
            elif field == "label": container["Config"]["Labels"][runner.LABEL + "run"] = "other"
            elif field == "image": container["Image"] = "other"
            elif field == "hostname": container["Config"]["Hostname"] = "development"
            elif field == "mount": container["Mounts"] = [{"Type": "volume"}]
            elif field == "network": container["HostConfig"]["NetworkMode"] = "host"
            elif field == "public": container["NetworkSettings"]["Ports"]["1433/tcp"][0]["HostIp"] = "0.0.0.0"
            elif field == "port": container["NetworkSettings"]["Ports"]["1433/tcp"][0]["HostPort"] = "1433"
            elif field == "stopped": container["State"]["Running"] = False
            with self.subTest(field=field), self.assertRaises(runner.RunnerError):
                runner.validate_container(state, container)

    def test_exact_five_passed_accepted(self):
        with contextlib.redirect_stdout(io.StringIO()):
            runner.verify_trx(trx())

    def test_fewer_more_duplicate_or_foreign_cases_rejected(self):
        methods = sorted(runner.METHODS)
        for items in (methods[:-1], methods + ["Other"], methods[:-1] + [methods[0]], methods[:-1] + ["Other"]):
            with self.subTest(items=items), self.assertRaises(runner.RunnerError):
                runner.verify_trx(trx(items))

    def test_skipped_failed_or_aborted_rejected(self):
        for outcome in ("NotExecuted", "Failed", "Aborted", "Inconclusive"):
            with self.subTest(outcome=outcome), self.assertRaises(runner.RunnerError):
                runner.verify_trx(trx(outcome=outcome))

    def test_counter_mismatch_rejected(self):
        for key, value in (("executed", "4"), ("passed", "4"), ("failed", "1"), ("notExecuted", "1"), ("aborted", "1")):
            counters = dict(total="5", executed="5", passed="5", failed="0", error="0", notExecuted="0")
            counters[key] = value
            with self.subTest(key=key), self.assertRaises(runner.RunnerError):
                runner.verify_trx(trx(counters=counters))

    def test_interrupted_manifest_write_keeps_previous_ownership_record(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "state.json"
            path.write_text(json.dumps({"nonce": "old-owner"}), encoding="utf-8")
            with patch.object(runner.os, "replace", side_effect=RuntimeError("interrupted before atomic replacement")):
                with self.assertRaises(RuntimeError):
                    runner.save_state(path, {"nonce": "new-owner"})
            self.assertEqual({"nonce": "old-owner"}, json.loads(path.read_text(encoding="utf-8")))

    def test_fixture_still_defines_exact_five_sql_facts(self):
        repo = Path(__file__).resolve().parents[2]
        source = (repo / "tests/Constia.API.Tests/CategoriaHabitoSqlServerTests.cs").read_text(encoding="utf-8-sig")
        actual = re.findall(r"\[DisposableSqlFact\]\s+public async Task (\w+)\(", source)
        self.assertEqual(5, len(actual))
        self.assertEqual(runner.METHODS, set(actual))


if __name__ == "__main__":
    unittest.main()
