"""Terminates GPU pods that outlived their training.

The watchdog: layer three of the four that make sure a rented GPU is always given back.

  1. The pod terminates itself when its run ends (docker/start.sh).
  2. If it cannot, it says so on Telegram at once, so a person can act in minutes.
  3. This sweep, for what neither can cover: a container killed outright - out of memory, a host
     failure, a kernel that took the process with it. Nothing inside the pod runs then, not even
     the alarm, and the only thing left to go on is that its manifest stopped moving.
  4. An absolute age cap, for pods this cannot make sense of at all.

A forgotten A100 over a weekend costs more than everything else in this project put together.

**A pod is judged by its own run, not by its age.** An age limit short enough to matter would kill
the very runs the pipeline exists for, hours into their training, with nothing saved.

    python src/scripts/sweep_pods.py --dry-run
    python src/scripts/sweep_pods.py --stale-minutes 60 --hard-max-hours 26

`decide()` is a pure function of (pod, manifest, clock) so the expensive judgement can be tested
without renting anything. `src/tools/test_sweep.py` does exactly that, and runs in CI.

Standard library only, and no `date -d`: this has to run on a laptop as well as on a runner.
"""

from __future__ import annotations

import argparse
import datetime
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import runstore  # noqa: E402

RUNPOD_API = os.environ.get("RUNPOD_API_BASE", "https://rest.runpod.io/v1")

KEEP, TERMINATE, REPORT = "keep", "terminate", "report"


def parse_time(value: str | None) -> datetime.datetime | None:
    if not value:
        return None
    try:
        return datetime.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None


def decide(pod: dict, manifest: dict | None, now: datetime.datetime,
           stale_minutes: int, hard_max_hours: int) -> tuple[str, str]:
    """What to do about one pod, and why. Pure, so it can be tested without a cloud.

    The order is the order of confidence: the absolute cap first because nothing may outlive it,
    then what the run says about itself, then how long ago it last said anything.
    """
    created = parse_time(pod.get("createdAt") or pod.get("created_at"))
    age = now - created if created else datetime.timedelta(0)
    age_h = int(age.total_seconds() // 3600)

    if created and age > datetime.timedelta(hours=hard_max_hours):
        return TERMINATE, f"past the absolute {hard_max_hours}h cap ({age_h}h old)"

    if manifest is None:
        # Not terminated. Killing what you cannot identify is how somebody's debugging session gets
        # destroyed, and the cap above still bounds it. But it must not bill in silence either, and
        # this is the worst version of "a GPU is running and nothing is training on it": with no run
        # folder, no heartbeat can go stale and no state can ever say finished. The launch job gives
        # up after twenty minutes, so an hour without one is worth waking somebody for.
        if age > datetime.timedelta(hours=1):
            return REPORT, f"{age_h}h old with no training behind it - NOT terminated, please check"
        return KEEP, "no manifest yet, and too young to worry about"

    state = manifest.get("state", "unknown")
    if state in ("finished", "failed"):
        # The pod should have been gone before this sweep ever saw it. Worth naming rather than
        # quietly cleaning up: a self-termination that stopped working would otherwise only ever
        # surface as a bill.
        return TERMINATE, f"run was already {state}, so self-termination failed"

    updated = parse_time(manifest.get("updated_at"))
    if updated:
        quiet = int((now - updated).total_seconds() // 60)
        if quiet > stale_minutes:
            return TERMINATE, f"no heartbeat for {quiet} min"

    ceiling = (manifest.get("machine") or {}).get("max_run_seconds") or 0
    if ceiling and age > datetime.timedelta(seconds=ceiling + 1800):
        return TERMINATE, f"past its own {ceiling}s ceiling plus half an hour"

    quiet = int((now - updated).total_seconds() // 60) if updated else -1
    return KEEP, f"{state}, last heard from {quiet} min ago"


def get(url: str, headers: dict | None = None, timeout: int = 20) -> bytes | None:
    try:
        request = urllib.request.Request(url, headers=headers or {})
        with urllib.request.urlopen(request, timeout=timeout) as response:
            return response.read()
    except (urllib.error.URLError, TimeoutError, OSError):
        return None


class Cloud:
    """The RunPod side. Reading a run's manifest is runstore's job, not this one's."""

    def __init__(self) -> None:
        self.key = os.environ.get("RUNPOD_API_KEY", "")

    def pods(self) -> list[dict]:
        raw = get(f"{RUNPOD_API}/pods", {"Authorization": f"Bearer {self.key}"})
        if not raw:
            return []
        listing = json.loads(raw)
        # The listing is sometimes a bare array, sometimes wrapped - accept both.
        if isinstance(listing, dict):
            listing = listing.get("data") or listing.get("pods") or []
        return [p for p in listing if p.get("id")]

    def terminate(self, pod_id: str) -> bool:
        request = urllib.request.Request(f"{RUNPOD_API}/pods/{pod_id}", method="DELETE",
                                         headers={"Authorization": f"Bearer {self.key}"})
        try:
            with urllib.request.urlopen(request, timeout=20):
                return True
        except urllib.error.HTTPError as error:
            return error.code in (404, 410)  # already gone is success
        except (urllib.error.URLError, TimeoutError, OSError):
            return False

    def shout(self, text: str) -> None:
        token = os.environ.get("TELEGRAM_BOT_TOKEN", "")
        chat = os.environ.get("TELEGRAM_CHAT_ID", "")
        if not token or not chat:
            return
        base = os.environ.get("TELEGRAM_API_BASE", "https://api.telegram.org")
        body = json.dumps({"chat_id": chat, "text": text}).encode()
        request = urllib.request.Request(f"{base}/bot{token}/sendMessage", data=body,
                                         headers={"Content-Type": "application/json"})
        try:
            urllib.request.urlopen(request, timeout=20).close()
        except Exception:  # noqa: BLE001 - an alarm may not be the thing that fails a sweep
            print("!  could not send the Telegram alarm", file=sys.stderr)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    # Generous on purpose. A false positive kills a healthy three-hour training; a false negative
    # costs an hour of GPU. A living run really is quiet while it generates the dataset and during
    # an epoch, so the threshold has to clear both - the tight ceiling is the pod's own timeout.
    p.add_argument("--stale-minutes", type=int, default=60)
    p.add_argument("--hard-max-hours", type=int, default=26)
    p.add_argument("--dry-run", action="store_true")
    args = p.parse_args()

    cloud = Cloud()
    now = datetime.datetime.now(datetime.timezone.utc)
    counts = {KEEP: 0, TERMINATE: 0, REPORT: 0}
    flagged = []

    for pod in cloud.pods():
        name = pod.get("name") or ""
        action, reason = decide(pod, runstore.read_json(name), now,
                                args.stale_minutes, args.hard_max_hours)
        counts[action] += 1
        print(f"pod {pod['id']} ({name or 'unnamed'}): {reason}")
        if action == KEEP:
            continue
        if action == TERMINATE and not args.dry_run:
            print("   terminated" if cloud.terminate(pod["id"]) else "   COULD NOT TERMINATE")
        elif action == TERMINATE:
            print("   (dry run - not terminating)")
        flagged.append(f"- {pod['id']} {name}: {reason}")

    summary = (f"Pod sweep: {counts[TERMINATE]} to terminate, {counts[REPORT]} to look at, "
               f"{counts[KEEP]} left alone.")
    print(summary)
    step_summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if step_summary:
        with open(step_summary, "a") as handle:
            handle.write(summary + "\n" + "\n".join(flagged) + "\n")

    # Anything flagged means either a layer above this failed or a GPU is running with nothing
    # behind it. Both are worth a message rather than a line in a log nobody opens.
    if flagged and not args.dry_run:
        cloud.shout("JassCardEye Pod-Sweep:\n" + "\n".join(flagged))
    return 0


if __name__ == "__main__":
    sys.exit(main())
