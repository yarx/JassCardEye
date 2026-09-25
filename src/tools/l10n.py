"""Checks the app texts in l10n/ and turns them into the resources of both apps.

The texts of both apps live in one place, l10n/<language>.json, with German (de.json) as the source
and the fallback. This script is the only way from there into the apps, so a text cannot reach one
app and not the other:

  python3 src/tools/l10n.py                     check everything; what CI runs
  python3 src/tools/l10n.py --ios src/app/ios   check the files, then write Localizable.xcstrings
                                                and InfoPlist.xcstrings there (xcodegen runs this)
  python3 src/tools/l10n.py --android <res>     check the files, then write values/strings.xml and
                                                values-<language>/strings.xml under <res> (Gradle
                                                runs this)

What is checked, and fails loudly:

- every language has exactly the keys of de.json, each with the same placeholders, and a plural
  where German has one;
- every key has its line of context in l10n/keys.md;
- German is written the Swiss way, with ss and never ß;
- without --ios or --android, the code as well: every String(localized:) in the Swift sources names a
  key and repeats its German text as the default, every R.string and R.plurals in the Kotlin sources
  exists, every key is used on each platform it belongs to, and project.yml's Info.plist values are
  the German ones.

The format of the files is described in l10n/keys.md. Standard library only: it runs inside Gradle
and xcodegen, on a runner and on a desk.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[2]
L10N = ROOT / "l10n"
SOURCE = "de"
SWIFT_SOURCES = ROOT / "src/app/ios/Sources"
KOTLIN_SOURCES = ROOT / "src/app/android/app/src/main/java"
ANDROID_MANIFEST = ROOT / "src/app/android/app/src/main/AndroidManifest.xml"
PROJECT_YML = ROOT / "src/app/ios/project.yml"

# The keys the system reads from Info.plist rather than the app from its catalog.
INFO_PLIST = {"app.name": "CFBundleDisplayName", "app.camera_usage.ios": "NSCameraUsageDescription"}

KEY = re.compile(r"[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*")
PLACEHOLDER = re.compile(r"%(\d+)\$([ds])")
PLURAL_FORMS = {"zero", "one", "two", "few", "many", "other"}
LANGUAGE = re.compile(r"[a-z]{2}")


class Problems:
    """Collects every problem before failing, so one run names all of them."""

    def __init__(self) -> None:
        self.items: list[str] = []

    def add(self, text: str) -> None:
        self.items.append(text)


# MARK: - The key files

def platforms(key: str) -> set[str]:
    """A key ending in .ios or .android belongs to that app only."""
    last = key.rsplit(".", 1)[-1]
    return {last} if last in ("ios", "android") else {"ios", "android"}


def android_name(key: str) -> str:
    return key.replace(".", "_")


def placeholders(text: str) -> list[tuple[int, str]]:
    return [(int(n), kind) for n, kind in PLACEHOLDER.findall(text)]


def forms(value: str | dict[str, str]) -> dict[str, str]:
    """A plain text as a single form, so plain texts and plurals are checked alike."""
    return value if isinstance(value, dict) else {"": value}


def other(value: str | dict[str, str]) -> str:
    """The form that carries every placeholder: the text itself, or a plural's other."""
    return value["other"] if isinstance(value, dict) else value


def load(problems: Problems) -> dict[str, dict[str, str | dict[str, str]]]:
    languages = {}
    for path in sorted(L10N.glob("*.json")):
        if not LANGUAGE.fullmatch(path.stem):
            problems.add(f"{path.name}: a language file is named by its two-letter code, like fr.json")
            continue
        try:
            data = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as error:
            problems.add(f"{path.name}: not valid JSON: {error}")
            continue
        if not isinstance(data, dict):
            problems.add(f"{path.name}: must be one object of keys")
            continue
        languages[path.stem] = data
    if SOURCE not in languages:
        problems.add(f"l10n/{SOURCE}.json is missing or unreadable; it is the source every language follows")
    return languages


def check_source(source: dict, problems: Problems) -> None:
    names: dict[str, str] = {}
    for key, value in source.items():
        where = f"{SOURCE}.json: {key}"
        if not KEY.fullmatch(key):
            problems.add(f"{where}: a key is lower case, with dots between the parts and _ inside them")
        clash = names.setdefault(android_name(key), key)
        if clash != key:
            problems.add(f"{where}: becomes the same Android name as {clash}")
        if not check_shape(where, value, problems):
            continue
        for form, text in forms(value).items():
            if "ß" in text:
                problems.add(f"{where}: Swiss spelling, ss instead of ß")
            numbers = [n for n, _ in placeholders(text)]
            if numbers != list(range(1, len(numbers) + 1)):
                problems.add(f"{where}: German numbers its placeholders %1$, %2$, … in the order they appear")
        if isinstance(value, dict):
            if placeholders(other(value)) != [(1, "d")]:
                problems.add(f"{where}: a plural has exactly one placeholder, the number %1$d")


def check_shape(where: str, value: object, problems: Problems) -> bool:
    """A text or a plural of texts, with nothing that looks like a placeholder but is not one."""
    if isinstance(value, dict):
        unknown = set(value) - PLURAL_FORMS
        if unknown or "other" not in value or not all(isinstance(v, str) for v in value.values()):
            problems.add(f"{where}: a plural has the forms {', '.join(sorted(PLURAL_FORMS))} as texts, "
                         f"and always other")
            return False
    elif not isinstance(value, str):
        problems.add(f"{where}: must be a text, or a plural with one and other")
        return False
    for text in forms(value).values():
        if "%" in PLACEHOLDER.sub("", text):
            problems.add(f"{where}: % is only allowed in a placeholder such as %1$d or %1$s")
            return False
    return True


def check_translation(language: str, data: dict, source: dict, problems: Problems) -> None:
    for key in source.keys() - data.keys():
        problems.add(f"{language}.json: {key} is missing")
    for key in data.keys() - source.keys():
        problems.add(f"{language}.json: {key} is not in {SOURCE}.json")
    for key in source.keys() & data.keys():
        where = f"{language}.json: {key}"
        value, german = data[key], source[key]
        if not check_shape(where, value, problems):
            continue
        if isinstance(german, dict) != isinstance(value, dict):
            problems.add(f"{where}: {'a plural' if isinstance(german, dict) else 'a plain text'} as in {SOURCE}.json")
            continue
        expected = set(placeholders(other(german)))
        found = forms(value)
        if isinstance(value, dict):
            # A form may leave the number out ("eine Karte"), but other always carries it.
            if set(placeholders(other(value))) != expected or any(
                    not set(placeholders(text)) <= expected for text in found.values()):
                problems.add(f"{where}: placeholders differ from {SOURCE}.json")
        elif set(placeholders(value)) != expected:
            problems.add(f"{where}: placeholders differ from {SOURCE}.json, which has "
                         f"{', '.join(f'%{n}${k}' for n, k in sorted(expected)) or 'none'}")


def load_context(source: dict, problems: Problems) -> dict[str, str]:
    """The context column of l10n/keys.md, one row per key."""
    path = L10N / "keys.md"
    if not path.exists():
        problems.add("l10n/keys.md is missing; every key needs its line of context there")
        return {}
    context: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        row = re.fullmatch(r"\|\s*`([^`]+)`\s*\|(.*)\|\s*", line)
        if not row:
            continue
        key = row.group(1)
        if key in context:
            problems.add(f"keys.md: {key} has more than one row")
        context[key] = row.group(2).strip()
    for key in source.keys() - context.keys():
        problems.add(f"keys.md: {key} has no line of context")
    for key in context.keys() - source.keys():
        problems.add(f"keys.md: {key} is not in {SOURCE}.json")
    return context


# MARK: - The code

def swift_literal(text: str, start: int) -> tuple[list[str], int] | None:
    """Reads the Swift string literal opening at `start` into its literal pieces, one more than it has
    interpolations. Only what the apps use: single-line literals, \\( ), \\" and \\\\."""
    if text.startswith('"""', start) or text[start] != '"':
        return None
    pieces, current, i = [], "", start + 1
    while i < len(text):
        c = text[i]
        if c == "\n":
            return None
        if c == '"':
            pieces.append(current)
            return pieces, i + 1
        if c == "\\":
            follow = text[i + 1]
            if follow == "(":
                depth, i = 1, i + 2
                while depth:
                    depth += {"(": 1, ")": -1}.get(text[i], 0)
                    i += 1
                pieces.append(current)
                current = ""
                continue
            current += {"n": "\n", "t": "\t"}.get(follow, follow)
            i += 2
            continue
        current += c
        i += 1
    return None


def check_swift(source: dict, problems: Problems) -> set[str]:
    used = set()
    call = re.compile(r'String\(localized:\s*"([^"]*)"(\s*,\s*defaultValue:\s*)?')
    for path in sorted(SWIFT_SOURCES.rglob("*.swift")):
        text = path.read_text(encoding="utf-8")
        for match in call.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            where, key = f"{path.relative_to(ROOT)}:{line}", match.group(1)
            used.add(key)
            if key not in source:
                problems.add(f"{where}: {key} is not in {SOURCE}.json")
                continue
            if "ios" not in platforms(key):
                problems.add(f"{where}: {key} belongs to Android only")
            literal = swift_literal(text, match.end()) if match.group(2) else None
            if literal is None:
                problems.add(f"{where}: {key} needs its German text as a one-line defaultValue")
                continue
            german = other(source[key])
            if literal[0] != PLACEHOLDER.split(german)[::3]:
                problems.add(f"{where}: defaultValue differs from {SOURCE}.json: {german}")
    for key in source:
        if "ios" in platforms(key) and key not in used and key not in INFO_PLIST:
            problems.add(f"{key}: not used in the iOS app")
    return used


def check_kotlin(source: dict, problems: Problems) -> None:
    by_name = {android_name(key): key for key in source}
    used = set()
    reference = re.compile(r"R\.(string|plurals)\.(\w+)|@string/(\w+)")
    files = sorted(KOTLIN_SOURCES.rglob("*.kt")) + [ANDROID_MANIFEST]
    for path in files:
        text = path.read_text(encoding="utf-8")
        for match in reference.finditer(text):
            line = text.count("\n", 0, match.start()) + 1
            where = f"{path.relative_to(ROOT)}:{line}"
            kind, name = match.group(1) or "string", match.group(2) or match.group(3)
            key = by_name.get(name)
            if key is None:
                problems.add(f"{where}: R.{kind}.{name} is no key of {SOURCE}.json")
                continue
            used.add(key)
            if (kind == "plurals") != isinstance(source[key], dict):
                problems.add(f"{where}: {key} is {'a plural' if isinstance(source[key], dict) else 'a plain text'}")
            if "android" not in platforms(key):
                problems.add(f"{where}: {key} belongs to iOS only")
    for key in source:
        if "android" in platforms(key) and key not in used:
            problems.add(f"{key}: not used in the Android app")


def check_info_plist(source: dict, problems: Problems) -> None:
    """The Info.plist needs its values even with a catalog beside it; they have to be the German ones."""
    text = PROJECT_YML.read_text(encoding="utf-8")
    for key, plist_key in INFO_PLIST.items():
        found = re.search(rf"INFOPLIST_KEY_{plist_key}:\s*(?:>-\s*\n\s*)?(.+)", text)
        if not found or found.group(1).strip() != source[key]:
            problems.add(f"project.yml: INFOPLIST_KEY_{plist_key} has to be {SOURCE}.json's {key}: {source[key]}")


# MARK: - Writing

def ios_text(text: str) -> str:
    return PLACEHOLDER.sub(lambda m: f"%{m.group(1)}${'lld' if m.group(2) == 'd' else '@'}", text)


def ios_unit(value: str | dict[str, str]) -> dict:
    if isinstance(value, dict):
        return {"variations": {"plural": {form: {"stringUnit": {"state": "translated", "value": ios_text(text)}}
                                          for form, text in value.items()}}}
    return {"stringUnit": {"state": "translated", "value": ios_text(value)}}


def catalog(entries: dict[str, dict[str, str | dict[str, str]]], comments: dict[str, str]) -> str:
    strings = {}
    for key in sorted(entries):
        entry = {"extractionState": "manual",
                 "localizations": {language: ios_unit(value) for language, value in sorted(entries[key].items())}}
        if comments.get(key):
            entry = {"comment": comments[key], **entry}
        strings[key] = entry
    return json.dumps({"sourceLanguage": SOURCE, "strings": strings, "version": "1.0"},
                      ensure_ascii=False, indent=2) + "\n"


def write_ios(folder: Path, languages: dict, context: dict[str, str]) -> None:
    keys = [key for key in languages[SOURCE] if "ios" in platforms(key)]
    texts = {key: {language: data[key] for language, data in languages.items()} for key in keys}
    (folder / "Localizable.xcstrings").write_text(catalog(texts, context), encoding="utf-8")
    plist = {INFO_PLIST[key]: texts[key] for key in INFO_PLIST}
    (folder / "InfoPlist.xcstrings").write_text(catalog(plist, {INFO_PLIST[k]: v for k, v in context.items() if k in INFO_PLIST}),
                                                encoding="utf-8")


def android_text(text: str) -> str:
    text = escape(text).replace("\\", "\\\\").replace("'", "\\'").replace('"', '\\"').replace("\n", "\\n")
    return "\\" + text if text[:1] in ("@", "?") else text


def write_android(folder: Path, languages: dict) -> None:
    for language, data in languages.items():
        lines = ['<?xml version="1.0" encoding="utf-8"?>',
                 f"<!-- Written by src/tools/l10n.py from l10n/{language}.json - change that file, not this one. -->",
                 "<resources>"]
        for key in sorted(k for k in languages[SOURCE] if "android" in platforms(k)):
            value, name = data[key], android_name(key)
            if isinstance(value, dict):
                lines.append(f'    <plurals name="{name}">')
                lines += [f'        <item quantity="{form}">{android_text(text)}</item>' for form, text in value.items()]
                lines.append("    </plurals>")
            else:
                lines.append(f'    <string name="{name}">{android_text(value)}</string>')
        lines.append("</resources>")
        values = folder / ("values" if language == SOURCE else f"values-{language}")
        values.mkdir(parents=True, exist_ok=True)
        (values / "strings.xml").write_text("\n".join(lines) + "\n", encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--ios", type=Path, metavar="DIR", help="write the two String Catalogs into DIR")
    parser.add_argument("--android", type=Path, metavar="RES", help="write strings.xml per language under RES")
    args = parser.parse_args()

    problems = Problems()
    languages = load(problems)
    source = languages.get(SOURCE, {})
    check_source(source, problems)
    for language, data in languages.items():
        if language != SOURCE:
            check_translation(language, data, source, problems)
    context = load_context(source, problems)
    if not args.ios and not args.android and not problems.items:
        check_swift(source, problems)
        check_kotlin(source, problems)
        check_info_plist(source, problems)

    if problems.items:
        print(f"l10n: {len(problems.items)} problem(s)", file=sys.stderr)
        for item in problems.items:
            print(f"  - {item}", file=sys.stderr)
        return 1

    if args.ios:
        write_ios(args.ios, languages, context)
    if args.android:
        write_android(args.android, languages)
    print(f"l10n: {len(source)} keys in {', '.join(sorted(languages))}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
