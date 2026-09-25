// The German fallback of the app's texts, for the checks that compile app sources on Linux.
//
// The apps read their texts with `String(localized:defaultValue:)`, and the default is the German
// text of l10n/de.json (src/tools/l10n.py holds the two to account). Foundation on Linux has no such
// initialiser, so check_scoring.swift and check_pile.swift are compiled together with this file,
// which gives them the default - the same German an app shows when a key is missing. On macOS the
// real one is used and returns the same, because a command-line tool has no String Catalog.

#if !canImport(Darwin)
extension String {
    init(localized key: StaticString, defaultValue: String) {
        self = defaultValue
    }
}
#endif
