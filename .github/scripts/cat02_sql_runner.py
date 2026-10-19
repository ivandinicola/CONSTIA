"""CAT-02 only. run/cleanup are CI-only; importing this module has no side effects."""
import argparse
import json
import os
from pathlib import Path
import re
import secrets
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET

IMAGE = "mcr.microsoft.com/mssql/server:2022-latest"
LABEL = "com.constia.cat02."
CLASS = "Constia.API.Tests.CategoriaHabitoSqlServerTests"
METHODS = {
    "BaseVacia_PrecargaExactaYMaterializacionEfConConstructorExistente",
    "BaseHistorica_PreservaHabitosFechasProgramacionesPeriodosCumplimientosYSnapshot",
    "Fk_PermiteOficialONullRechazaDesconocidaYNoEliminaHabitoEnCascada",
    "Down_SinAsignacionesRevierteSoloCategoriaYPreservaDatosHistoricos",
    "Down_ConAsignacionRechazaSinPerdidaParcialNiCambiosDeEsquema",
}

class RunnerError(RuntimeError):
    pass


def require_ci(env):
    if (sys.platform != "linux" or env.get("GITHUB_ACTIONS") != "true"
            or env.get("RUNNER_ENVIRONMENT") != "github-hosted"
            or env.get("GITHUB_JOB") != "cat02-sqlserver"
            or not re.fullmatch(r"[0-9]+", env.get("GITHUB_RUN_ID", ""))
            or not re.fullmatch(r"[0-9]+", env.get("GITHUB_RUN_ATTEMPT", ""))
            or not env.get("RUNNER_TEMP") or env.get("DOCKER_HOST") or env.get("DOCKER_CONTEXT")
            or env.get("CONSTIA_CAT02_SQLSERVER_CONNECTION")):
        raise RunnerError("CAT-02 requires its dedicated hosted Linux job; arbitrary connections are forbidden.")


def command(args, *, env=None, timeout=30, check=True):
    result = subprocess.run(args, env=env, text=True, capture_output=True, timeout=timeout, check=False)
    if check and result.returncode:
        # Never expose inspect, Docker environment or subprocess exception strings.
        raise RunnerError("CAT-02 command failed: " + args[0])
    return result


def docker_json(*args):
    return json.loads(command(["docker", *args]).stdout)


def require_local_daemon():
    contexts = docker_json("context", "inspect")
    if len(contexts) != 1 or contexts[0]["Endpoints"]["docker"]["Host"] != "unix:///var/run/docker.sock":
        raise RunnerError("CAT-02 requires the local runner Docker daemon.")


def validate_container(state, container):
    labels = container["Config"]["Labels"] or {}
    expected = {"run": state["nonce"], "github-run": state["runId"],
                "attempt": state["runAttempt"], "scope": "cat02-sqlserver"}
    if (any(labels.get(LABEL + key) != value for key, value in expected.items())
            or container["Config"]["Hostname"] != state["hostname"]
            or container["Image"] != state["imageId"]
            or container["Config"]["Image"] != state["imageDigest"]
            or container["Mounts"] or container["HostConfig"]["NetworkMode"] != "bridge"
            or container["HostConfig"]["Privileged"] or container["HostConfig"]["PidMode"]
            or container["HostConfig"].get("Binds") or container["HostConfig"].get("VolumesFrom")
            or not container["State"]["Running"]):
        raise RunnerError("CAT-02 container ownership/isolation mismatch.")
    if state.get("containerId") and container["Id"] != state["containerId"]:
        raise RunnerError("CAT-02 container ID mismatch.")
    ports = container["NetworkSettings"]["Ports"]
    if set(ports) != {"1433/tcp"} or len(ports["1433/tcp"]) != 1:
        raise RunnerError("Unexpected CAT-02 port bindings.")
    binding = ports["1433/tcp"][0]
    port = int(binding["HostPort"])
    if binding["HostIp"] != "127.0.0.1" or not 1024 <= port <= 65535 or port == 1433:
        raise RunnerError("CAT-02 must use a dynamic loopback port distinct from 1433.")
    configured = container["HostConfig"]["PortBindings"]
    if (set(configured) != {"1433/tcp"} or len(configured["1433/tcp"]) != 1
            or configured["1433/tcp"][0]["HostIp"] != "127.0.0.1"
            or configured["1433/tcp"][0]["HostPort"] not in ("", "0", str(port))):
        raise RunnerError("CAT-02 configured/actual port mismatch.")
    if state.get("port") and port != state["port"]:
        raise RunnerError("CAT-02 execution port mismatch.")
    return port


def verify_trx(path):
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    root = ET.parse(path).getroot()
    definitions = {}
    for test in root.findall("./t:TestDefinitions/t:UnitTest", ns):
        method = test.find("./t:TestMethod", ns)
        if method is None or test.attrib["id"] in definitions:
            raise RunnerError("Invalid/duplicate TRX definitions.")
        definitions[test.attrib["id"]] = (method.attrib["className"], method.attrib["name"])
    results = root.findall("./t:Results/t:UnitTestResult", ns)
    identities = [definitions[result.attrib["testId"]] for result in results]
    if (len(results) != 5 or len(set(identities)) != 5
            or set(identities) != {(CLASS, method) for method in METHODS}
            or any(result.attrib.get("outcome") != "Passed" for result in results)):
        raise RunnerError("Expected exactly the five CAT-02 SQL methods, all Passed, none skipped.")
    summary = root.find("./t:ResultSummary", ns)
    if summary is None or summary.attrib.get("outcome") != "Completed":
        raise RunnerError("TRX run did not complete.")
    counters = summary.find("./t:Counters", ns)
    if counters is None:
        raise RunnerError("Missing TRX counters.")
    expected = {"total": 5, "executed": 5, "passed": 5, "failed": 0, "error": 0, "notExecuted": 0}
    if any(counters.attrib.get(key) != str(value) for key, value in expected.items()):
        raise RunnerError("CAT-02 TRX counters mismatch.")
    if any(int(value) != 0 for key, value in counters.attrib.items() if key not in {"total", "executed", "passed"}):
        raise RunnerError("TRX reports an unsuccessful result.")
    print("CAT-02 SQL: Passed: 5, Failed: 0, Skipped: 0", flush=True)


def save_state(path, state):
    temporary = path.with_suffix(".json.tmp")
    try:
        with temporary.open("x", encoding="utf-8") as stream:
            temporary.chmod(0o600)
            stream.write(json.dumps(state))
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        temporary.unlink(missing_ok=True)


def paths():
    root = Path(os.environ["RUNNER_TEMP"]) / "cat02-runner"
    return root, root / "state.json", root / "container.cid"


def cleanup():
    require_ci(os.environ)
    root, state_path, cid_path = paths()
    if not state_path.exists():
        return
    require_local_daemon()
    state = json.loads(state_path.read_text(encoding="utf-8"))
    nonce = state["nonce"]
    if (not re.fullmatch(r"[0-9a-f]{32}", nonce)
            or state["runId"] != os.environ["GITHUB_RUN_ID"]
            or state["runAttempt"] != os.environ["GITHUB_RUN_ATTEMPT"]
            or state["name"] != "cat02-" + nonce):
        raise RunnerError("Refusing cleanup with invalid CAT-02 ownership record.")
    target = state.get("containerId") or (cid_path.read_text().strip() if cid_path.exists() else state["name"])
    probe = command(["docker", "container", "inspect", target], check=False)
    if probe.returncode:
        # Missing --rm container is success; daemon/permission errors are failures.
        if "No such container" not in probe.stderr and "No such object" not in probe.stderr:
            raise RunnerError("Cannot verify CAT-02 cleanup target.")
    else:
        container = json.loads(probe.stdout)[0]
        labels = container["Config"]["Labels"] or {}
        if (container["Name"] != "/" + state["name"]
                or labels.get(LABEL + "run") != nonce
                or labels.get(LABEL + "github-run") != state["runId"]
                or labels.get(LABEL + "attempt") != state["runAttempt"]
                or labels.get(LABEL + "scope") != "cat02-sqlserver"
                or (state.get("containerId") and container["Id"] != state["containerId"])):
            raise RunnerError("Refusing to remove a container not owned by this CAT-02 execution.")
        command(["docker", "rm", "--force", container["Id"]])
    for path in (state_path, cid_path):
        path.unlink(missing_ok=True)
    print("CAT-02 owned container cleaned up.", flush=True)


def run():
    require_ci(os.environ)
    require_local_daemon()
    root, state_path, cid_path = paths()
    # A previous state is never overwritten, adopted or silently removed.
    root.mkdir(mode=0o700, exist_ok=False)
    nonce = secrets.token_hex(16)
    password = "C2!" + secrets.token_hex(24)
    print("::add-mask::" + password, flush=True)
    state = {"nonce": nonce, "name": "cat02-" + nonce, "hostname": "c2-" + nonce[:12],
             "runId": os.environ["GITHUB_RUN_ID"], "runAttempt": os.environ["GITHUB_RUN_ATTEMPT"]}
    # Persist ownership before Docker can create anything; cleanup can inspect the unique owned name.
    save_state(state_path, state)
    def interrupted(signum, frame):
        raise RunnerError("CAT-02 execution interrupted.")
    for sig in (signal.SIGTERM, signal.SIGINT):
        signal.signal(sig, interrupted)
    try:
        command(["docker", "pull", IMAGE], timeout=240)
        image = docker_json("image", "inspect", IMAGE)[0]
        digest = next(d for d in image["RepoDigests"] if re.fullmatch(r"mcr\.microsoft\.com/mssql/server@sha256:[0-9a-f]{64}", d))
        state.update(imageId=image["Id"], imageDigest=digest)
        save_state(state_path, state)
        child_env = dict(os.environ, MSSQL_SA_PASSWORD=password)
        command(["docker", "run", "--detach", "--rm", "--cidfile", str(cid_path),
                 "--name", state["name"], "--hostname", state["hostname"],
                 "--label", LABEL + "run=" + nonce,
                 "--label", LABEL + "github-run=" + state["runId"],
                 "--label", LABEL + "attempt=" + state["runAttempt"],
                 "--label", LABEL + "scope=cat02-sqlserver",
                 "--publish", "127.0.0.1::1433", "--env", "MSSQL_SA_PASSWORD",
                 "--env", "ACCEPT_EULA=Y", "--env", "MSSQL_PID=Developer", digest], env=child_env, timeout=60)
        state["containerId"] = cid_path.read_text().strip()
        if not re.fullmatch(r"[0-9a-f]{64}", state["containerId"]):
            raise RunnerError("Missing/invalid new container ID.")
        save_state(state_path, state)
        inspected = docker_json("container", "inspect", state["containerId"])[0]
        state["port"] = validate_container(state, inspected)
        save_state(state_path, state)
        query = "SET NOCOUNT ON; SELECT CONCAT(CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),'|',CONVERT(int,SERVERPROPERTY('ProductMajorVersion')),'|',DB_NAME());"
        shell = 'SQLCMDPASSWORD="$MSSQL_SA_PASSWORD" /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -C -b -l 3 -h -1 -W -Q "$1"'
        deadline = time.monotonic() + 180
        while True:
            ready = command(["docker", "exec", state["containerId"], "/bin/bash", "-c", shell, "cat02", query], check=False, timeout=10)
            if ready.returncode == 0:
                if ready.stdout.strip() != state["hostname"] + "|16|master":
                    raise RunnerError("Unexpected SQL engine identity before database creation.")
                break
            if time.monotonic() >= deadline:
                raise RunnerError("CAT-02 SQL engine readiness timeout.")
            time.sleep(2)
        results = root / "raw-results"
        results.mkdir(mode=0o700, exist_ok=False)
        test_env = dict(os.environ, CONSTIA_CAT02_SQLSERVER_TESTS="true",
                        CONSTIA_CAT02_MANIFEST=str(state_path), CONSTIA_CAT02_PASSWORD=password)
        result = command(["dotnet", "test", "tests/Constia.API.Tests/Constia.API.Tests.csproj",
                          "--no-build", "--no-restore", "--configuration", "Release",
                          "--filter", "FullyQualifiedName~" + CLASS,
                          "--logger", "trx;LogFileName=cat02-sqlserver.trx",
                          "--results-directory", str(results)], env=test_env, timeout=600, check=False)
        sanitized = publish_trx(root, password)
        print((result.stdout + result.stderr).replace(password, "REDACTED"), flush=True)
        verify_trx(sanitized)
        if result.returncode:
            raise RunnerError("CAT-02 SQL test process failed despite TRX validation.")
    finally:
        try:
            publish_trx(root, password)
        finally:
            cleanup()


def publish_trx(root, password):
    target = Path(os.environ["RUNNER_TEMP"]) / "cat02-results" / "cat02-sqlserver.trx"
    raw = root / "raw-results" / "cat02-sqlserver.trx"
    if raw.exists():
        target.parent.mkdir(mode=0o700, exist_ok=True)
        target.write_text(raw.read_text(encoding="utf-8-sig").replace(password, "REDACTED"), encoding="utf-8")
        raw.unlink()
    return target


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("mode", choices=["run", "cleanup"])
    args = parser.parse_args()
    try:
        (run if args.mode == "run" else cleanup)()
    except RunnerError as error:
        print(str(error), file=sys.stderr)
        return 1
    except Exception:
        # Exceptions from processes/connection drivers can carry secrets: emit only a fixed diagnostic.
        print("CAT-02 runner failed safely; inspect sanitized test results and CI step status.", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
