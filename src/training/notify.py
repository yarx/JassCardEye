"""Says what a training run is doing, to whoever is not watching it.

Nobody has a log of the run open: the pod runs on its own for hours and the job that started it is
long gone. So the run has to speak up by itself.

One channel, Telegram, configured with `TELEGRAM_BOT_TOKEN` and `TELEGRAM_CHAT_ID` - and silent when
they are absent. Literally silent: the normal case on a laptop is that neither is set, and a line
per checkpoint saying so would bury the run's own output. It is the same rule the upload follows,
because the identical script has to work with no bot at all.

Everything is rendered from manifest.json, so there is one description of what a run is and the
message reads from it rather than being told the same numbers twice. Standard library only - this
runs in the pod.

    python src/training/notify.py --manifest artifacts/manifest.json --kind started
    python src/training/notify.py --manifest artifacts/manifest.json --kind progress --text "epoch 7/20"
    python src/training/notify.py --manifest artifacts/manifest.json --kind finished
"""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
import urllib.request
from pathlib import Path

# Overridable so the delivery path can be exercised against a local server. Telegram does not need
# this; a test does, and a notifier that has never actually sent anything is not worth having.
TELEGRAM_API = os.environ.get("TELEGRAM_API_BASE", "https://api.telegram.org")

# How often a heartbeat may speak. Everything else is an event and always goes out: a variant that
# finished, a run that failed. Progress is the only thing that repeats, so it is the only thing
# throttled - a hundred-epoch run should not be a hundred notifications.
PROGRESS_SECONDS = int(os.environ.get("NOTIFY_PROGRESS_SECONDS", "300"))


def post(url: str, payload: dict) -> dict:
    request = urllib.request.Request(
        url, data=json.dumps(payload).encode(), method="POST",
        headers={"Content-Type": "application/json"})
    with urllib.request.urlopen(request, timeout=30) as response:
        raw = response.read().decode()
    return json.loads(raw) if raw else {}


def summary(manifest: dict) -> str:
    rows = []
    for name, record in manifest.get("variants", {}).items():
        state = record.get("state", "?")
        mark = {"finished": "ok", "failed": "FAILED", "pending": "waiting"}.get(state, state)
        if state == "finished" and record.get("exit_code") not in (0, None):
            mark = f"ok, exit {record['exit_code']} after completion"
        seconds = record.get("train_seconds")
        took = f", {seconds // 60} min" if isinstance(seconds, int) and seconds else ""
        rows.append(f"  {name}: {mark} ({record.get('headline') or '-'}{took})")
    return "\n".join(rows) if rows else "  (no variants yet)"


def text_for(manifest: dict, kind: str, extra: str) -> str:
    config = manifest.get("config", {})
    head = {
        "started": "Training gestartet",
        "progress": "Training läuft",
        "variant": "Variante fertig",
        "finished": "Training fertig",
        "failed": "Training FEHLGESCHLAGEN",
    }.get(kind, kind)
    lines = [f"{head}: {manifest.get('run_id')}",
             f"{','.join(config.get('variants', []))} · size {config.get('model_size')} · "
             f"{config.get('epochs')} epochs · {manifest.get('dataset', {}).get('count')} images"]
    # Read from the manifest rather than passed in: the caller already wrote it there, and a second
    # description of where a run has got to is a second thing that can disagree with the first.
    progress = manifest.get("progress")
    if progress:
        lines.append(f"{manifest.get('current_variant')}: epoch {progress.get('epoch')}"
                     f"/{progress.get('epochs')}")
    if extra:
        lines.append(extra)
    lines.append(summary(manifest))
    machine = manifest.get("machine", {})
    if machine.get("gpu") and machine["gpu"] != "unknown":
        lines.append(f"GPU {machine['gpu']}")
    if kind in ("finished", "failed"):
        lines.append(f"{os.environ.get('AZURE_STORAGE_CONTAINER', 'training-runs')}/"
                     f"{manifest.get('run_id')}/")
    return "\n".join(lines)


def send(manifest: dict, kind: str, extra: str) -> str:
    token = os.environ.get("TELEGRAM_BOT_TOKEN", "")
    chat = os.environ.get("TELEGRAM_CHAT_ID", "")
    if not token or not chat:
        return ""
    post(f"{TELEGRAM_API}/bot{token}/sendMessage",
         {"chat_id": chat, "text": text_for(manifest, kind, extra),
          # A progress message should not buzz a phone; an event should.
          "disable_notification": kind == "progress"})
    return "telegram: sent"


def throttled(state_dir: Path, kind: str) -> bool:
    if kind != "progress":
        return False
    stamp = state_dir / ".notify_progress_at"
    now = time.time()
    if stamp.exists():
        try:
            if now - float(stamp.read_text().strip()) < PROGRESS_SECONDS:
                return True
        except ValueError:
            pass
    stamp.write_text(str(now))
    return False


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--manifest", type=Path, required=True)
    p.add_argument("--kind", required=True,
                   choices=["started", "progress", "variant", "finished", "failed"])
    p.add_argument("--text", default="", help="one extra line, e.g. which epoch")
    args = p.parse_args()

    if not args.manifest.exists():
        print(f"notify: no manifest at {args.manifest}", file=sys.stderr)
        return 0

    manifest = json.loads(args.manifest.read_text())
    if throttled(args.manifest.parent, args.kind):
        return 0

    # Never fatal. A training that died because a chat service had a bad minute would be the most
    # expensive possible way to learn about notifications.
    try:
        message = send(manifest, args.kind, args.text)
        if message:
            print(f"   {message}")
    except Exception as error:  # noqa: BLE001 - deliberately everything
        print(f"!  notification failed: {error}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
