# Training pipeline

A training run starts from the Actions tab, lands on a rented GPU, and leaves its results in Azure
Blob Storage under `training-runs/<run id>/`. The parts are deliberately few.

```
GitHub Actions          creates the pod, waits for its first manifest, and leaves.
  (minutes)             Nothing waits for the training itself.
        │
        ▼
RunPod pod              docker/start.sh: clone the commit → check the upload → rebuild the
  (hours, alone)        validation set → generate → train each variant → upload as it goes →
        │               report → terminate itself
        │
        ├──────────────► Azure Blob Storage   training-runs/<run id>/
        │                                     manifest, log, pod log, one folder per variant
        └──────────────► Telegram             at epoch ends (at most every five minutes)
                                              and on every event
```

Locally, `src/training/fetch_run.py` is the way back: list the runs, pull one down, and export it -
Core ML for the iOS app, LiteRT for the Android app - with a record of where it came from. Releases
take their model from a run the same way.

For scale: a run of variant c with 200 000 images and 20 epochs on an NVIDIA H100 80GB HBM3
generates its dataset in about 24 minutes and trains in about two and a half hours.

This document says how the pipeline works and how to operate it.

## Needs attention

- **RunPod retires its REST API v1 on 15 November 2026.** Every RunPod call in this pipeline uses
  v1; "On the API version" at the end lists what has to move.
- **The sweep acts on every pod of the RunPod account**, not only on pods this pipeline created: any
  pod older than 26 hours is terminated, and any pod older than an hour without a run folder is
  reported at every sweep.
- **The repository variable `AZURE_STORAGE_CONTAINER` must be set**, although the scripts have a
  default - see step 3 of the setup.
- **The Azure SAS carries an expiry date.** Once it has passed no run can upload, the sweep cannot
  read any manifest, and releases cannot fetch a model - see step 3 of the setup.
- **The half-hourly sweep costs Actions minutes whether or not anything trains:** about 1 440 a
  month, which matters where the repository is private and has a monthly allowance.
- **A release with a blank `run_id` ships the newest finished run**, whatever that run was for - see
  "How a release takes its model".

## The one thing to understand

**`src/scripts/run_training.sh` is the whole run**, and it is the same script on a laptop and in the
pod:

```bash
src/scripts/run_training.sh --variant all --count 500 --epochs 3 --no-upload   # a rehearsal, minutes
src/scripts/run_training.sh --variant all                                      # the real thing
```

It generates the dataset **once**, then trains each requested variant on it in turn. Everything is
collected under `artifacts/`: `manifest.json` and `train.log` at the top, and for each variant a
folder `artifacts/<variant>/` with Ultralytics' output and a `run.json`. One machine for all variants
rather than one each: they share the dataset, so it is rendered once instead of once per variant -
less GPU time than running them in parallel, at the price of wall-clock time. A failing variant does
not stop the others. Whether a variant finished is judged by what it leaves behind - `weights/best.pt`
and the metrics of every configured epoch - and not by its exit code. The run ends as `finished` when
every variant did, otherwise as `failed`, and exits with the number of variants that did not finish.

The pod adds nothing to that except a GPU, a ceiling and a place to put the output. So the whole
thing can be rehearsed in miniature before it costs a franc, and when something breaks in the cloud,
it breaks in a script that also runs on the desk.

| Option | Default | |
|---|---|---|
| `--variant` | `c` | comma separated, or `all` for `c,b1,b2,a`, trained in that order |
| `--size` | `n` | model size |
| `--count` | `200000` | synthetic images, half from each deck |
| `--seed` | `1` | dataset seed |
| `--epochs` | `20` | |
| `--imgsz` | `640` | rendered and trained image size |
| `--batch` | `64` | |
| `--workers`, `--close-mosaic`, `--warmup-epochs` | Ultralytics' own | handed to `train.py` only when given |
| `--device` | `auto` | CUDA, else Apple MPS, else CPU |
| `--out` | `artifacts/` | where the run is collected |
| `--run-id` | `<UTC date>-<UTC time>-<short commit>` | the name of the run folder |
| `--python` | `src/training/.venv` when it exists, else `python3` | |
| `--no-upload` | off | keep the results local even when Azure is configured |

Worth knowing before running it on a desk: it **deletes `output/dataset/`** before generating, so a
dataset made by hand is gone afterwards; it clears each `artifacts/<variant>/` and truncates
`artifacts/train.log`; and it rebuilds the derived parts of `data/real/val`. Ultralytics' raw output
stays in `output/training/<run id>-<variant>/`. The training itself - variants, options, figures,
export - is described in [src/training/README.md](README.md).

## Why the dataset is not stored

The dataset is regenerated on the machine that trains, rather than built once and stored.

Normally that would be wrong: comparing variants requires them to see exactly the same images, and
regenerating per run would vary model and data at the same time. Here it holds anyway, because the
generator is deterministic - [data-pipeline.md](../../context/architecture/data-pipeline.md) explains
how. A dataset is identified by the commit, which carries the generator and the card scans; the seed;
and the image count, which decides each image's camera tilt and deck. The manifest records all three,
so nothing has to be stored to guarantee comparability.

What that buys: no dataset registry, and no upload and download of the images. What it costs: the
generation time on the rented machine - about 25 minutes for 200 000 images, a franc or so - against
a much smaller pile of moving parts.

The results are the opposite case, and they *are* stored. The asymmetry is the point: a dataset that
can be recreated exactly from a commit and two numbers does not need keeping, and a trained model
cannot be recreated at all.

## Starting a run

*Actions → train → Run workflow*, on the branch whose code should train. The pod trains exactly the
commit the branch pointed at when the run was dispatched.

| Input | Default | Accepted |
|---|---|---|
| `variants` | `c` | `all`, or a comma separated list of `a`, `b1`, `b2`, `c` |
| `size` | `n` | `n`, `s`, `m` |
| `count` | `200000` | a whole number above zero |
| `seed` | `1` | a whole number above zero |
| `epochs` | `20` | a whole number above zero |
| `imgsz` | `640` | a whole number above zero |
| `batch` | `64` | a whole number above zero |
| `max_hours` | `20` | a whole number above zero: the ceiling at which the pod stops the training |
| `image_tag` | blank, meaning `latest` | letters, digits, dot, underscore and dash; a commit of `main` runs the image built from it |

Every input is checked before anything is rented. The values reach the pod as a word list, so a stray
one would become an argument to the training, and a wrong number would otherwise only show after
hours on a GPU. They reach the workflow's scripts through the environment, never through `${{ }}`
inside a script, because the step that uses them also holds the clone token and every secret. The
workflow always adds `--workers 16 --device 0`. Two dispatches from the same branch do not launch at
the same time: the second waits until the first launch job is done.

The launch job then

1. names the run, `<UTC date>-<UTC time>-<short commit>`,
2. picks the image - `:latest`, unless `image_tag` names another,
3. creates the pod through the RunPod API, named after the run and carrying everything it needs in
   its environment,
4. waits up to 20 minutes for the run's first `manifest.json` to appear in Azure - and if none does,
   deletes the pod and fails rather than leaving a GPU billing for nothing,
5. writes the pod id, the image, the results folder and the ceiling into the run summary.

That usually takes five to ten minutes, most of it the pod pulling the image. The first manifest is
worth waiting for: the run script writes it before it renders a single image, so its arrival proves
the image, the clone and the Azure credentials at once. That holds only when Azure is configured -
without an account the wait passes immediately and proves nothing. The job has a second reason to
stay: the pod clones with the job's own `GITHUB_TOKEN`, which expires when the job ends.

### The pod

`docker/start.sh` is the pod's whole life. Everything it needs arrives as environment variables when
the pod is created:

| Variable | Set by | Meaning |
|---|---|---|
| `RUN_ID` | workflow | the run; without it the pod idles, for a pod somebody opened by hand |
| `REPO_SLUG`, `REPO_TOKEN`, `GIT_SHA` | workflow | what to clone, with the launch job's short-lived token |
| `TRAIN_ARGS` | workflow | the rest of `run_training.sh`'s arguments |
| `MAX_RUN_SECONDS` | workflow | the ceiling, `max_hours` × 3600; 72 000 when absent |
| `POD_TERMINATE_KEY` | workflow | the RunPod key, so the pod can delete itself |
| `AZURE_STORAGE_*`, `TELEGRAM_*`, `GITHUB_RUN_ID` | workflow | passed through; empty means not configured |
| `RUNPOD_POD_ID`, `RUNPOD_GPU_NAME` | RunPod | the pod's own id and its GPU |

The key is deliberately not called `RUNPOD_API_KEY`: RunPod fills the `RUNPOD_*` names itself, and a
variable of ours in that space is not reliably the one that arrives. The pod fetches only the one
commit and takes the token out of its git configuration straight after the checkout.

**The pod's name is the run id**, and the sweep relies on it: it finds a pod's run by the pod's name.
A pod renamed by hand can no longer be judged by its run.

The GPU types are written into `train.yml`, taken from the "GPU ID" column of
<https://docs.runpod.io/references/gpu-types>. Five are offered in this order of preference - A100
80GB PCIe, H100 80GB HBM3, H100 NVL, H100 PCIe, L40S - and RunPod takes the first that is free.
Capacity is never guaranteed, and for a model this small even the last of them is ample. Each pod
gets one GPU, a 120 GB container disk and no network volume, so whatever is on the pod is gone with
it. The disk is sized for the dataset: the generator writes every variant, about 86 KB per image, so
500 000 images take ~43 GB and a million ~86 GB.

## Where a run lives

`training-runs/<run id>/` in Azure Blob Storage, one folder per run:

```
<run-id>/
    manifest.json          what the run is, how far it got, and when it last said anything
    train.log              everything the run printed, uploaded as it grows
    pod.log                the pod's own account: clone, ceiling, the training's exit code
    c/                     one folder per variant, exactly what artifacts/<variant>/ holds
        run.json           what this variant achieved: metrics, best epoch, how it ended
        results.csv  args.yaml  weights/best.pt  weights/last.pt
        results.png  confusion_matrix.png  PR_curve.png  …   (the box heads c and b1 only)
    b1/ …
```

**A folder, not a zip.** A zip is complete only at the very end, which is the one moment a long run
cannot be relied on to reach. A folder can be written into while the run is still going: the log and
the manifest go up at every checkpoint, the manifest again after every epoch, and each variant the
moment it finishes. A run that dies in its third hour still leaves everything the first two
produced, and a failed variant still leaves the figures that show why it failed.

**The run id is `<UTC date>-<UTC time>-<short commit>`**, in the form `20260101-1200-abc1234`.
Sortable, readable, and it says which code produced the run - the three things a folder name in
object storage has to do. The Actions run id lives in the manifest instead, where nobody has to recognise it as a
number.

**`pod.log` is what the pod says about itself**, as opposed to what the training says. That matters
exactly when the part *after* the training went wrong, and by then the pod is gone. It is uploaded
as the pod begins to terminate itself, and again if the first attempt fails, so the reason a GPU is
still alive leaves the machine that is still billing for it. A termination that succeeds is
therefore never in it.

**`manifest.json` is what makes a half-uploaded folder readable,** and it is the only state file
there is. It is written once with the parameters of the run and updated in place from then on - by
the checkpoints between variants, and after every epoch from inside the training loop - and replaced
whole on every write, so a reader never sees half a file. Its `state` is what a reader goes by:
`starting`, `generating`, `running`, then `finished` when every variant finished, or `failed` when
one did not or the run stopped early, its own ceiling included. `running` or `generating` with an
`updated_at` that stopped moving is a run that died. It is also the heartbeat the watchdog reads,
which is why the training loop keeps it moving rather than only the shell around it.

**A variant is finished when its results are there,** whatever its process exited with. Three things
must be present: `weights/best.pt`, metrics in `run.json` that could be ranked, and every configured
epoch in those metrics. `src/scripts/write_manifest.py` decides this, and `src/tools/test_manifest.py`
holds it to account in CI.

- **Why not the exit code.** A variant can train every epoch and write everything, then abort with
  exit 134 while Python shuts down. Such a variant is `finished` and keeps its `exit_code`, with the
  note `aborted after completion`.
- **Why the epochs are counted.** `best.pt` and `run.json` alone would not do either: Ultralytics
  writes `best.pt` after the first epoch that improves, and the run script writes `run.json` after a
  crash as well.

```
run_id  state  started_at  updated_at  finished_at  git_sha
dataset          count, seed, generate_seconds, reproduce
config           variants (in training order), model_size, epochs, imgsz, batch
current_variant  the variant training now
progress         its epoch, the number of epochs, and the fitness so far
machine          host, gpu, pod_id, github_run_id, max_run_seconds
variants         per variant: state (pending, finished, failed, unreadable), exit_code,
                 note ("aborted after completion"), train_seconds,
                 headline ("mAP@50 …" or "top-1 …"), metrics
```

`max_run_seconds` is there so the watchdog can judge a run by the ceiling it was actually given. The
`headline` is decided once, where the record is written, because which number says whether a variant
works depends on its head.

**The upload is checked before anything else.** The run's first act is to upload its manifest. A
wrong SAS found at the end of a three-hour run costs the whole run; found at the start it costs
seconds. It is the one upload allowed to end a run: a later one that fails is retried, reported, and
then left behind - the results are still on the machine, and one unreachable moment is not worth the
hours already paid for. The same reasoning puts the validation-label check ahead of rendering the
dataset.

Configured through environment variables named the way the Azure tools name them themselves:

| Variable | Meaning |
|---|---|
| `AZURE_STORAGE_ACCOUNT` | the storage account. **Unset means no upload**, and every call is a no-op |
| `AZURE_STORAGE_CONTAINER` | the container, `training-runs` when unset |
| `AZURE_STORAGE_SAS_TOKEN` | a container-scoped SAS, with or without its leading `?`; without one, `az` and `azcopy` use their own login |
| `AZURE_STORAGE_BLOB_ENDPOINT` | another endpoint, for an emulator or a stand-in server; Azure never needs it |

Doing nothing when the account is unset is deliberate: the same script has to run on a laptop with no
cloud account at all. `--no-upload` forces that even where the variables are set. A laptop that
uploads without a SAS signs in as its user, which needs a Storage Blob Data role on the account -
owning the subscription is not enough.

**Reading and writing are separate.** `src/scripts/upload_run.sh` writes, with **azcopy** where it
exists - the pod image carries it, and it moves a folder of weights far faster than the CLI - and
`az storage blob` otherwise. The two are interchangeable, which is what makes a cloud upload testable
from a desk. `src/scripts/runstore.py` reads, and everything that reads goes through it: the
watchdog, the launch job's wait, the local fetch tool and the release's model fetch. It reads a
single document with a plain HTTPS GET when a SAS is set, which is the pod's and the runners' case
and needs no dependency, and through the Azure CLI otherwise; listing and downloading runs always go
through the CLI. `AZURE_STORAGE_BLOB_ENDPOINT` redirects both the writer and the reader, which is how
`src/tools/test_sweep.py` runs the sweep end to end against a stand-in server.

## How a run reports

**Telegram** (`TELEGRAM_BOT_TOKEN`, `TELEGRAM_CHAT_ID`), and nothing else. The run started, a variant
finished, the run is done, something failed - and, from inside the training loop, progress at the
end of an epoch, throttled to one message per five minutes because a hundred-epoch run should not be
a hundred notifications. Progress arrives without a notification sound; the events come with one. The
messages are in German and are rendered from `manifest.json`, so they say what the record says. The
same bot carries the pod's alarm when it cannot delete itself, and the sweep's report when it has to
act.

Optional and silent without the two variables. Literally silent: the normal case on a laptop is that
neither is set, and a line per checkpoint saying so would bury the run's own output. Same rule the
upload follows - the identical script has to work with no bot at all.

**Nothing is written back into GitHub.** The run folder is the record, `fetch_run.py --list` is the
index, and Telegram is how a run reaches a person.

To look at a run:

- Telegram, as above;
- the launch job's summary, with the pod id, the image, the results folder and the ceiling;
- `AZURE_STORAGE_ACCOUNT=jasscardeye python3 src/scripts/runstore.py <run id>`, which prints the
  manifest;
- `python3 src/training/fetch_run.py --list`, which lists the 20 newest runs with their state,
  variants, image count and headline metric.

## Giving the GPU back

A forgotten GPU is the most expensive failure this project can have. A pod is given back by four
layers, each covering what the one before it cannot, and a pod is judged by *its own* run rather
than by its age: an age limit short enough to matter would kill the long runs this pipeline exists
for.

| | Mechanism | Covers | Blind to |
|---|---|---|---|
| **1** | `docker/start.sh` traps its own exit and calls `DELETE /v1/pods/<id>`, ten attempts over five minutes. The training runs under `timeout $MAX_RUN_SECONDS`, which sends TERM and five minutes later KILL. | Normal end, a failing script, a hung training, a crashed variant. | A container killed outright, or one that cannot reach the RunPod API. |
| **2** | When layer 1 cannot delete the pod, the pod says so on Telegram at once - no key, or the HTTP code it got back. | A self-termination that is broken rather than absent: somebody can act within minutes. | It is an alarm, not an action. A container killed outright cannot raise it either. |
| **3** | `src/scripts/sweep_pods.py`, every half hour in `cleanup-orphan-pods`, reads the manifest of the run the pod is named after and removes the pod when the run says `finished` or `failed`, when `updated_at` is more than an hour old, or when the pod is older than the run's own ceiling plus half an hour. | A crashed or wedged container - the case nothing inside the pod can report. | Up to 90 minutes - an hour of silence plus the interval - and GitHub's scheduling slack. Any pod whose manifest it cannot read. |
| **4** | An absolute age cap in the same sweep, 26 hours, applied to every pod of the RunPod account whatever its run claims. | Everything above failing at once; pods created by hand. | Nothing. This is the floor. |

Details that matter:

- **Stop is not terminate.** A pod that merely stops still bills for its disk. Every layer issues
  `DELETE`, never a stop - and so does the launch job when a pod never writes a manifest.
- **Ending a run by hand** is the same call: *Terminate* (not *Stop*) in the RunPod console, or
  `curl -X DELETE https://rest.runpod.io/v1/pods/<pod id> -H "Authorization: Bearer $RUNPOD_API_KEY"`.
  The pod id is in the launch job's summary and in the manifest (`machine.pod_id`). What was uploaded
  until then stays in the run folder; its manifest keeps saying `running` with an `updated_at` that
  no longer moves.
- **An hour of silence, not ten minutes.** A false positive kills a healthy training; a false
  negative costs an hour of GPU. A living run really does go quiet while it generates the dataset -
  about 25 minutes at 200 000 images - and for the length of one epoch, so the threshold has to
  clear both comfortably. The tight ceiling is the pod's own `timeout`, not this sweep.
- **Whenever layer 3 or 4 has to act, layer 1 failed.** The sweep says so loudly - in the workflow
  summary and on Telegram - because a self-termination that quietly stopped working would otherwise
  only ever surface as a bill. A dry run writes the summary and sends nothing.
- **The sweep runs every half hour**, at minutes 13 and 43, rather than every few hours. With no
  completion event to catch an orphan the moment it appears, the interval plus the stale threshold
  *is* the worst case a dead pod bills for. GitHub starts scheduled runs late and now and then skips
  one, so the real interval is longer. It costs Actions minutes in return: 48 jobs a day, each billed
  as at least a minute, about 1 440 a month.
- **A pod with no readable run is not terminated** before the absolute cap. Once it is older than an
  hour it is reported instead, at every sweep, because it bills with nothing behind it that could
  ever go stale or say finished. Without a working SAS every pod looks like that: an expired token
  turns layer 3 off for all of them.
- **The sweep looks at every pod of the RunPod account**, not only at pods this pipeline created. A
  pod of any other project on the same account is reported after an hour and terminated after 26.

## What you have to set up once

1. **RunPod API key** as the secret `RUNPOD_API_KEY`. The launch job, the pod and the sweep all use
   it.

2. **Make the container package public.** Only possible *after* the `image` workflow has pushed
   successfully once - until then the package does not exist and there is nothing to find.

   It does **not** live in the repository settings, which is where most people look first. Go to
   the owner's profile → **Packages** → `jasscardeye/trainer` → **Package settings** (right-hand
   side) → *Danger Zone* → **Change visibility** → Public, confirming by typing the package name.
   Direct link for a repository owned by a user:
   `https://github.com/users/<owner>/packages/container/jasscardeye%2Ftrainer/settings`, and
   `https://github.com/orgs/<org>/packages/container/jasscardeye%2Ftrainer/settings` for an
   organisation.

   A package's visibility is independent of the repository's, so this works for a private
   repository as well - and it keeps the setup to a single RunPod secret. It is safe because **the image
   contains no project code** apart from `docker/start.sh`: the CUDA PyTorch base, the .NET SDK,
   Ultralytics and azcopy. The repository is cloned into the running pod, not baked in. The
   alternative would be registry credentials in RunPod, which GitHub Packages accepts only as a
   *classic* personal access token with `read:packages`.

3. **Azure Blob Storage.** A storage account, a container `training-runs`, and a container-scoped SAS
   that may read, add, create, write and list. In the repository settings: the secrets
   `AZURE_STORAGE_ACCOUNT` and `AZURE_STORAGE_SAS_TOKEN`, and the variable `AZURE_STORAGE_CONTAINER`.
   `train.yml` hands them to the pod, `cleanup.yml` reads manifests with them, and `release.yml`
   fetches models with them. On a laptop the same three go into the environment the run script sees.

   **In the cloud this step is not optional.** Without it the pod still trains, then deletes itself -
   and with it its disk and the only copy of the results. The launch job's wait passes without
   checking anything, the sweep cannot judge the pod and only reports it until the 26-hour cap, and
   `release.yml` refuses to start. On a laptop, unset simply means the run stays in `artifacts/`.

   **Set `AZURE_STORAGE_CONTAINER` although it has a default.** Actions hands an unset variable over
   as an empty string. The shell scripts treat that as `training-runs`; `runstore.py`, which the
   launch job's wait, the sweep and the release's model fetch use, takes it literally and looks for
   runs in a container without a name. `release.yml` checks that it is set before it builds; the
   launch job and the sweep do not.

   A SAS rather than the account key, because the key would let a pod delete every run ever made;
   the SAS grants writing into one container and expires by itself.

   **What is set up:** account `jasscardeye` (resource group `jasscardeye`, switzerlandnorth),
   container `training-runs`, private. The SAS may read, add, create, write and list, but **not
   delete** - a leaked token can add to the runs folder and cannot destroy a single finished run.
   It carries an expiry date. Once that has passed, a run stops at its first upload, before any image
   is rendered, and the launch job turns red after its wait; the sweep can no longer read any
   manifest; and releases cannot fetch a model. Regenerate it with

   ```bash
   az storage container generate-sas --account-name jasscardeye --name training-runs \
       --permissions racwl --expiry <yyyy-mm-dd>T00:00Z --https-only \
       --account-key "$(az storage account keys list --subscription '<subscription>' \
           --resource-group jasscardeye --account-name jasscardeye --query '[0].value' -o tsv)" -o tsv
   ```

   and put the output straight into the `AZURE_STORAGE_SAS_TOKEN` secret, for example by piping it
   into `gh secret set AZURE_STORAGE_SAS_TOKEN`, so the token never lands in a file or a log. The key
   lookup names the subscription because `az` otherwise looks only in its default one.

4. **Actions minutes.** A private repository gets 2 000 Linux minutes a month; a public one is not
   metered. The half-hourly `cleanup-orphan-pods` uses about 1 440 minutes on its own, and
   `release.yml` bills its macOS minutes at ten times the rate. Once an allowance is used up, jobs
   stop rather than bill unless the spending limit is raised above zero - and the watchdog stops with
   them.

5. **Telegram**, to hear about a run without opening anything: talk to `@BotFather`, `/newbot`, and
   put the token in the secret `TELEGRAM_BOT_TOKEN`. Then message the bot once and read the chat id
   from `https://api.telegram.org/bot<token>/getUpdates` - the `chat.id` field, negative for a
   group - into the secret `TELEGRAM_CHAT_ID`. Unset, the run trains in silence.

That is the complete list. A cloud run that leaves anything behind needs the first three; the fourth
decides whether the jobs, the watchdog among them, keep running; and Telegram is how the run talks
to you.

## Rehearsing a change

Debugging the run script through the pod and the workflow means looking for faults through two
layers at once, so a change is tried at the smallest layer that contains it.

- **The run script, the training or the upload.** On the desk,
  `src/scripts/run_training.sh --variant all --count 500 --epochs 3 --no-upload`, and once more
  without `--no-upload` and with the account and the SAS in the environment. Done when every
  `artifacts/<variant>/run.json` has metrics and the manifest says `finished` - locally, and then in
  `training-runs/<run id>/`.
- **The pod** (`train.yml`). Run `train` from the branch with small numbers: `variants: c`,
  `count: 500`, `epochs: 1`. Done when the launch job is green within minutes, the folder appears in
  Azure, and **the pod is gone by itself**. That last point is the one to watch: it is the moment
  nobody is holding the pod's hand.
- **The image** (`docker/`). It is built only from `main`, never for a pull request or a branch, so
  a change is tried on the desk first:
  `docker build --platform linux/amd64 -f docker/Dockerfile .` shows that it builds, and the run
  script above is what it runs. After the merge `image` pushes `:latest` and `:<commit>`, and the same
  small run from `main` confirms it. If the new image breaks the pod, `image_tag` set to the commit of
  the previous build keeps training possible until the fix is merged.
- **The watchdog.** `python3 src/tools/test_sweep.py`, which CI runs as well, then
  `cleanup-orphan-pods` by hand with `dry_run`: it lists every pod and says what it would do to each.
- **When a run counts as finished.** `python3 src/tools/test_manifest.py`, which CI runs as well.
  The rule also takes a short run on the desk that is broken on purpose: a variant killed in its
  second epoch has `best.pt` and `run.json` and still has to end `failed`.

## Traceability

Each variant writes `run.json`: its exit code, how long it trained, and the metrics of its best
epoch. It holds nothing else on purpose. Which commit, which seed, how many images, which GPU - those
describe the run, and the run records them once in `manifest.json` beside it. A copy per variant
would be a copy that can be wrong on its own.

The best epoch is ranked by a fitness score, so that the numbers belong to the epoch whose weights
are in `best.pt`. For the box heads (c, b1) that is exactly Ultralytics' own score,
0.1 × mAP@50 + 0.9 × mAP@50-95. For the classifiers (a, b2) `src/scripts/write_run_json.py` ranks by
top-1 alone, while Ultralytics keeps `best.pt` by the mean of top-1 and top-5, so the two can name
different epochs. That touches only a and b2; for the detector the apps ship, the reported epoch is
the epoch in `best.pt`.

Above it, `manifest.json` says the same for the run as a whole, and `src/training/fetch_run.py
--list` reads every one of them back as a table - the container is the index of what has been
trained, and it needs no second copy kept in step by hand.

Below it, `models.json` beside the bundled models - in `src/app/ios/Models/` and in
`src/app/android/app/src/main/assets/models/` - closes the last gap: for each variant the run id, the
commit, the dataset count and seed, the metrics and the time of the export. Only `fetch_run.py
--export` writes it; `export.py` on local weights does not. The file is not versioned, it travels
into the build, and the release scripts warn when it is missing. Without it a tester's report cannot
be tied to a model, and the app ships a file whose origin nobody can name.

Without that chain a figure in the thesis cannot be traced back to the code and data that produced
it.

## Getting a model back onto the phone

```bash
# what has been trained
python3 src/training/fetch_run.py --list
# the whole run into output/runs/<id>/
python3 src/training/fetch_run.py --run-id <id>
# Core ML into src/app/ios/Models
src/training/.venv-export/bin/python src/training/fetch_run.py \
    --run-id <id> --variant c --weights-only --export
# LiteRT into src/app/android/app/src/main/assets/models
src/training/.venv-export-android/bin/python src/training/fetch_run.py \
    --run-id <id> --variant c --weights-only --export --format litert
```

Listing and fetching need Python and the Azure CLI; exporting needs the export environment of the
format, which [src/training/README.md](README.md) sets up. The account defaults to
`jasscardeye`. `--out <dir>` exports somewhere else, to try a model without touching what an app
bundles. A run that is still going can be fetched as well; what comes back is a snapshot.

It authenticates with `AZURE_STORAGE_SAS_TOKEN` if that is set, and otherwise finds the account key
by locating the storage account across the subscriptions `az` can see. That second path is the one
that needs no setup: owning a subscription lets you read the account key, while it grants no access
to the *data* in a container - a distinction that otherwise produces a baffling permission error on
the first attempt.

## How a release takes its model

The model is not in the repository. Both platform jobs of `release.yml` run
`.github/actions/fetch-model`, which sets up the pinned export environment of its format, downloads
`best.pt` of one variant of one training run, and exports it into the app's model folder together
with `models.json`. So the build in TestFlight and the one in Play carry the same weights, and both
name the run they came from.

The workflow's `run_id` input decides which run. A release meant for the stores names its run there,
with variant `c`.

**A blank `run_id` is a release risk worth knowing.** It takes the newest run whose manifest says
`finished` among the 20 newest in the container, whatever that run was for: a small rehearsal that
finished yesterday would ship. If that run lacks the requested variant, nothing is exported and the
release fails. A release started by creating a GitHub release always takes this path, because such a
run has no inputs.

## Files

| Path | Purpose |
|---|---|
| `src/scripts/run_training.sh` | the entire run; works on a laptop and in the pod |
| `src/training/train.py` | trains one variant with Ultralytics |
| `src/scripts/report_epoch.sh` | the one thing the training loop knows about the pipeline |
| `src/scripts/write_run_json.py` | what one variant achieved |
| `src/scripts/write_manifest.py` | the run-level record: state, parameters, every variant at a glance |
| `src/scripts/upload_run.sh` | one file or folder into the run's folder in Azure; a no-op without an account |
| `src/training/notify.py` | the Telegram messages; silent when unconfigured |
| `src/scripts/runstore.py` | the one place that knows where a run's records live and how to read one |
| `src/scripts/wait_for_run.py` | what the launch job waits for: the run's first manifest |
| `src/scripts/sweep_pods.py` | the watchdog's judgement, testable without renting anything |
| `src/tools/test_sweep.py` | holds that judgement to account in CI: eleven decisions, and one sweep against a stand-in server |
| `src/tools/test_manifest.py` | holds the manifest's verdict to account in CI: when a variant and a run count as finished |
| `src/tools/check_dataset.py` | the consistency check the run applies to both datasets before training |
| `src/training/fetch_run.py` | the way back: list runs, fetch one, export it with its provenance |
| `src/training/export.py` | Core ML and LiteRT export, also used by `fetch_run.py` |
| `docker/` | the toolchain image (PyTorch with CUDA, .NET SDK, Ultralytics, azcopy) and `start.sh`, the pod's whole life |
| `.github/workflows/train.yml` | create the pod and confirm it started - minutes, not hours |
| `.github/workflows/image.yml` | build the image when a change to `docker/` or the workflow reaches `main` |
| `.github/workflows/cleanup.yml` | `cleanup-orphan-pods`: the sweep, every half hour |
| `.github/actions/fetch-model/action.yml` | how a release takes its model from a run |

Creating a pod is orchestration and lives in `train.yml` as `curl` and `jq`, where its only caller
is. Deciding whether to *destroy* one is not orchestration, and it does not live there: it is the
most expensive judgement in the project in both directions, so it lives in a script with a test. It
also has to run on a laptop, which rules out `date -d`.

The pipeline's own Python - `write_run_json.py`, `write_manifest.py`, `runstore.py`,
`wait_for_run.py`, `sweep_pods.py`, `notify.py` - uses the standard library only, because it runs on
the pod's and the runners' bare Python, where no virtual environment exists. Only `train.py` and
`export.py` need Ultralytics.

**On the API version.** Every RunPod call uses `https://rest.runpod.io/v1`, which RunPod retires on
**15 November 2026**. The successor is `https://api.runpod.io/v2`, with nested request fields
(`image`, `disk`, `gpu: {id, count}`) and list responses wrapped as `{"pods": [...]}`. Five calls in
three places have to move: creating the pod and deleting it after a failed wait in `train.yml`; the
self-termination in `docker/start.sh`, which is baked into the image and so needs an image build;
and listing and deleting in `src/scripts/sweep_pods.py`, whose base URL comes from `RUNPOD_API_BASE`
and whose listing already accepts a wrapped array.
