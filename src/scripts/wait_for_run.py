"""Waits until a run has written its first manifest, and reports what it says.

This is the only thing the launch workflow waits for, and it is worth waiting for: the run script
writes its manifest before it generates a single image, so the file appearing proves four things at
once - the pod booted, the image is the right one, the clone worked, and the storage credentials are
good. Each of the four would otherwise fail silently and be discovered an hour later.

It also keeps the clone credential alive. The token the pod clones with is the launch job's own and
expires with that job, so the job has to outlive the pod's first seconds - which is exactly as long
as this takes.

    python src/scripts/wait_for_run.py --run-id <run-id> --timeout-minutes 20
"""

from __future__ import annotations

import argparse
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import runstore  # noqa: E402


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__,
                                formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--run-id", required=True)
    # A cold pod pulls several gigabytes of image before it runs a line of our code, and that is the
    # slow part - not the clone and not the manifest.
    p.add_argument("--timeout-minutes", type=int, default=20)
    p.add_argument("--poll-seconds", type=int, default=30)
    args = p.parse_args()

    if not runstore.configured():
        print("No storage account configured - nothing to watch for. The pod is on its own.")
        return 0

    deadline = time.time() + args.timeout_minutes * 60
    while time.time() < deadline:
        manifest = runstore.read_json(args.run_id)
        if manifest:
            machine = manifest.get("machine", {})
            print("The run is alive:")
            print(f"  state:  {manifest.get('state')}")
            print(f"  commit: {str(manifest.get('git_sha'))[:7]}")
            print(f"  gpu:    {machine.get('gpu')}")
            return 0
        time.sleep(args.poll_seconds)

    print(f"No manifest after {args.timeout_minutes} minutes - the pod never got as far as "
          f"starting the run.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
