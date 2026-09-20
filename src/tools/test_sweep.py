"""Checks the judgement that decides whether a rented GPU gets terminated.

This is the most expensive code in the repository to get wrong in either direction: terminating a
healthy pod throws away hours of training, and failing to terminate a dead one bills until somebody
notices. It is also the code that is hardest to exercise for real, because doing so means renting
GPUs and then breaking them on purpose.

`src/scripts/sweep_pods.py` is therefore built around one pure function, and this is what holds it to
account. Runs in CI, needs nothing but the standard library.

    python src/tools/test_sweep.py
"""

from __future__ import annotations

import datetime
import json
import sys
import threading
from http.server import BaseHTTPRequestHandler, HTTPServer
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src" / "scripts"))
from sweep_pods import KEEP, REPORT, TERMINATE, decide  # noqa: E402

# Frozen, so a decision table reads the same on any day. The wiring test below deliberately uses
# the real clock instead, because the script it drives uses its own.
NOW = datetime.datetime(2026, 1, 1, 12, 0, tzinfo=datetime.timezone.utc)
FAILURES: list[str] = []


def ago(_from: datetime.datetime = NOW, **kwargs) -> str:
    return (_from - datetime.timedelta(**kwargs)).strftime("%Y-%m-%dT%H:%M:%SZ")


def really_ago(**kwargs) -> str:
    return ago(datetime.datetime.now(datetime.timezone.utc), **kwargs)


def manifest(state: str, quiet_minutes: int, ceiling: int | None = 3600) -> dict:
    return {"state": state, "updated_at": ago(minutes=quiet_minutes),
            "machine": {"max_run_seconds": ceiling}}


def check(what: str, pod_age_h: float, manifest_doc, expected: str) -> None:
    pod = {"id": "p", "name": "run-x", "createdAt": ago(hours=pod_age_h)}
    action, reason = decide(pod, manifest_doc, NOW, stale_minutes=60, hard_max_hours=26)
    ok = action == expected
    print(f"  {'ok  ' if ok else 'FAIL'} {what}: {action} ({reason})")
    if not ok:
        FAILURES.append(f"{what}: expected {expected}, got {action}")


def test_decisions() -> None:
    print("decide():")
    # The one that must never be wrong: a training in its second hour, reporting normally.
    check("healthy run, heartbeat two minutes old", 2, manifest("running", 2, 72000), KEEP)
    # A living run really is quiet while it generates the dataset and during an epoch.
    check("quiet for 40 min - still within a normal gap", 1, manifest("running", 40, 72000), KEEP)
    check("quiet for 90 min - dead", 3, manifest("running", 90, 72000), TERMINATE)
    check("run finished, pod still alive", 1, manifest("finished", 5), TERMINATE)
    check("run failed, pod still alive", 1, manifest("failed", 5), TERMINATE)
    check("past its own ceiling plus half an hour", 2, manifest("running", 2, 3600), TERMINATE)
    check("no ceiling recorded, otherwise healthy", 5, manifest("running", 2, None), KEEP)
    # Not killed, but never silent either.
    check("no manifest, three hours old", 3, None, REPORT)
    check("no manifest, five minutes old", 0.08, None, KEEP)
    check("past the absolute cap, whatever it claims", 30, manifest("running", 1, 72000), TERMINATE)
    check("past the absolute cap with no manifest", 30, None, TERMINATE)


class Fake(BaseHTTPRequestHandler):
    """Stands in for RunPod and for blob storage, so the wiring is exercised too."""

    deleted: list[str] = []

    def _send(self, code: int, body: bytes = b"") -> None:
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self) -> None:
        if self.path.endswith("/pods"):
            self._send(200, json.dumps([
                {"id": "alive", "name": "run-alive", "createdAt": really_ago(hours=1)},
                {"id": "dead", "name": "run-dead", "createdAt": really_ago(hours=3)},
            ]).encode())
        elif "run-alive" in self.path:
            self._send(200, json.dumps({"state": "running",
                                        "updated_at": really_ago(minutes=1),
                                        "machine": {"max_run_seconds": 72000}}).encode())
        else:
            self._send(404)

    def do_DELETE(self) -> None:
        Fake.deleted.append(self.path.rsplit("/", 1)[-1])
        self._send(200)

    def log_message(self, *args) -> None:
        pass


def test_wiring() -> None:
    import os
    import subprocess

    server = HTTPServer(("127.0.0.1", 0), Fake)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    base = f"http://127.0.0.1:{server.server_port}"

    result = subprocess.run(
        [sys.executable, str(Path(__file__).resolve().parents[2] / "src" / "scripts" / "sweep_pods.py")],
        capture_output=True, text=True,
        env={**os.environ, "RUNPOD_API_BASE": base, "RUNPOD_API_KEY": "k",
             "AZURE_STORAGE_ACCOUNT": "acct", "AZURE_STORAGE_BLOB_ENDPOINT": base,
             "AZURE_STORAGE_CONTAINER": "training-runs", "AZURE_STORAGE_SAS_TOKEN": "sas",
             "TELEGRAM_BOT_TOKEN": "", "TELEGRAM_CHAT_ID": ""})
    server.shutdown()

    print("\nend to end against a stand-in for both services:")
    print("".join(f"  {line}\n" for line in result.stdout.strip().splitlines()))
    # run-dead has no manifest and is three hours old: reported, never terminated. run-alive is
    # healthy and must be left alone. So nothing at all may be deleted here.
    if Fake.deleted:
        FAILURES.append(f"deleted a pod it should not have: {Fake.deleted}")
    if "NOT terminated" not in result.stdout:
        FAILURES.append("the pod with no training behind it was not reported")
    if "left alone" not in result.stdout:
        FAILURES.append("the healthy pod was not left alone")


def main() -> int:
    test_decisions()
    test_wiring()
    if FAILURES:
        print("\nFAILED:")
        for failure in FAILURES:
            print(f"  {failure}")
        return 1
    print("\nall sweep checks passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
