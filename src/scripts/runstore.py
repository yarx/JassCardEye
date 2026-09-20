"""Where a run's records live, and how to read one.

Everything a run produces goes into `training-runs/<run id>/` in Azure Blob Storage, and several
places have to read it: the watchdog, the launch workflow's wait, the local fetch tool and the
release's model fetch. They all come through here, so the address of a blob is built in one place -
and the endpoint override, which exists so the cloud path can be rehearsed against an emulator or a
stand-in server, is honoured by every reader rather than by some of them.

Two ways in, because the callers genuinely differ:

  * With `AZURE_STORAGE_SAS_TOKEN` - the pod and the runners - a plain HTTPS GET is enough and
    brings no dependency, which matters in a container that should stay a toolchain.
  * Without one - a laptop - the Azure CLI is used, because signing a request with the account key
    by hand is a great deal of code for something `az` already does.

Standard library only.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request

CONTAINER = os.environ.get("AZURE_STORAGE_CONTAINER", "training-runs")


def account() -> str:
    return os.environ.get("AZURE_STORAGE_ACCOUNT", "")


def configured() -> bool:
    """False means "no cloud" - every caller treats that as a reason to do nothing, not an error."""
    return bool(account())


def _sas() -> str:
    return os.environ.get("AZURE_STORAGE_SAS_TOKEN", "").lstrip("?")


def blob_url(path: str) -> str:
    """The address of one blob inside the run store, SAS included when there is one."""
    service = os.environ.get("AZURE_STORAGE_BLOB_ENDPOINT",
                             f"https://{account()}.blob.core.windows.net")
    url = f"{service.rstrip('/')}/{CONTAINER}/{path}"
    sas = _sas()
    return f"{url}?{sas}" if sas else url


def az(*args: str, check: bool = True) -> str:
    result = subprocess.run(["az", *args], capture_output=True, text=True)
    if check and result.returncode != 0:
        raise SystemExit(f"az {' '.join(args)} failed:\n{result.stderr.strip()}")
    return result.stdout.strip()


def az_auth(announce: bool = False) -> list[str]:
    """The arguments that let `az storage` reach this container, and how they were found.

    The account key is looked up across the subscriptions `az` can see, because that is the path
    that needs no setup: whoever owns a subscription can already read the key, while owning it
    grants no access to the *data* in a container - a distinction that otherwise produces a
    baffling permission error on the first attempt.
    """
    if _sas():
        if announce:
            print(f"   reading {CONTAINER} with the SAS from the environment")
        return [f"--sas-token={_sas()}"]

    name = account()
    for subscription in az("account", "list", "--query",
                           "[?state=='Enabled'].id", "-o", "tsv").splitlines():
        found = az("storage", "account", "list", "--subscription", subscription,
                   "--query", f"[?name=='{name}'].name", "-o", "tsv", check=False)
        if found.strip():
            key = az("storage", "account", "keys", "list", "--subscription", subscription,
                     "--account-name", name, "--query", "[0].value", "-o", "tsv")
            if announce:
                print(f"   reading {CONTAINER} with the account key of {name}")
            return [f"--account-key={key}"]

    raise SystemExit(
        f"Cannot reach the storage account '{name}'. Either set AZURE_STORAGE_SAS_TOKEN, or "
        f"`az login` with an account that can see the subscription holding it.")


def read_json(run_id: str, name: str = "manifest.json") -> dict | None:
    """One document out of a run's folder. Absent is a normal answer, not an error.

    A pod made by hand has no run folder at all, and a run that has just started has no manifest
    yet; both are things a caller has to be able to tell apart from a failure, and both look the
    same from here: None.
    """
    if not configured() or not run_id:
        return None

    if _sas():
        try:
            with urllib.request.urlopen(blob_url(f"{run_id}/{name}"), timeout=20) as response:
                raw = response.read()
        except (urllib.error.URLError, TimeoutError, OSError):
            return None
    else:
        # Through a real file, not /dev/stdout: `az storage blob download` writes progress and
        # metadata into the stream it is given, so the JSON comes back unparseable.
        with tempfile.TemporaryDirectory() as scratch:
            target = os.path.join(scratch, "blob.json")
            result = subprocess.run(
                ["az", "storage", "blob", "download", "--account-name", account(),
                 "--container-name", CONTAINER, "--name", f"{run_id}/{name}",
                 "--file", target, "--only-show-errors", *az_auth()],
                capture_output=True)
            if result.returncode != 0 or not os.path.exists(target):
                return None
            with open(target, "rb") as handle:
                raw = handle.read()

    try:
        return json.loads(raw)
    except (json.JSONDecodeError, UnicodeDecodeError):
        return None


def run_ids(auth: list[str] | None = None) -> list[str]:
    """Every run in the container, newest first.

    The run id starts with its date, so sorting the names is sorting by time - which is the reason
    the id looks the way it does. Listing needs the CLI; nothing in a pod ever lists.
    """
    names = az("storage", "blob", "list", "--account-name", account(),
               "--container-name", CONTAINER, "--query", "[].name", "-o", "tsv",
               *(auth if auth is not None else az_auth())).split()
    return sorted({name.split("/")[0] for name in names if "/" in name}, reverse=True)


if __name__ == "__main__":  # a convenience for looking at a run from the command line
    print(json.dumps(read_json(sys.argv[1], *sys.argv[2:3]), indent=2))
