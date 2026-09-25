# CLAUDE.md

Guidance for AI agents working in this repository. It is short on purpose and points to where the
details live - read it before changing anything.

## Start here

- `context/README.md` - the reading order of the project knowledge: vision, architecture,
  conventions, glossary, features.
- `context/conventions.md` - how code, texts, workflows and documents are written here.
- `context/architecture/recognition.md` - every value between the camera and a counted card, and where
  the two apps differ. A change to one of these values is the owner's decision; name it, do not slip it in.
- The README of the area you change: `src/app/ios/README.md`, `src/app/android/README.md`,
  `src/tools/dataset/README.md`, `src/training/README.md`, `src/training/pipeline.md`, `src/web/README.md`,
  `doc/README.md`.

## The owner's working rules

- **"The app" means both apps.** Every feature, behaviour, text and design change lands in
  `src/app/ios` and `src/app/android` in the same change; the two stay identical in features and
  design. If one platform genuinely cannot do something, say so instead of skipping it.
- **Commit, push, TestFlight and Play uploads only when the owner says so.** A permission given for
  one task covers that task. Never merge a pull request unless told to.
- **One branch and one pull request per feature or bug**, on `feature/<issue>-<name>` or
  `bug/<issue>-<name>`: the issue number right after the slash when there is an issue, then a short
  English name (`feature/1-keep-screen-awake`). Pull requests are **squash-merged**: one commit per
  feature on `main`, `feat: …` or `fix: …`.
- **Developer tools stay out of user-facing text.** *Bild* and *Session aufzeichnen* are never
  mentioned in the privacy policy, the website, store texts, review notes or in-app texts. Developer
  documentation may describe them.
- **Stores:** "prepare" means filling everything in, not submitting for review; releasing an
  approved version is the owner's click. Store texts are short and plain and must not read as
  AI-written.
- **Secrets never pass through the agent.** No passwords, tokens or keys in chat, logs or files -
  pipe them (for example `az … | gh secret set …`). The owner signs in to consoles himself. Promo
  codes and phone numbers never go into the repository.
- **The thesis (`doc/`) is the owner's graded CAS thesis (HSLU, CAS Machine Learning) and his own
  work. Do not change it on your own.** Never edit, restructure, reword or "fix" anything under `doc/`
  unless the owner explicitly asks for that specific change - not as part of another task, not as a
  side effect of a refactoring or a documentation pass, and not because a fact elsewhere changed.
- **Keep the documentation true.** A change that makes a statement in a README or in `context/`
  outdated updates that document in the same pull request.

## Quick facts

- `MARKETING_VERSION` in `src/app/ios/project.yml` is the version of both apps; the build number is
  the Release workflow's run number + 1200.
- Recognition: after the thesis only variant C (the detector) is pursued; A and B stay trainable.
- Generated output is never committed: `output/`, `artifacts/`, exported models and `models.json`,
  the Xcode project, the thesis PDFs.
- The checks CI runs, and how to run them locally, are listed in the root `README.md`.
