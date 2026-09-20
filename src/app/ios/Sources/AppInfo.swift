import Foundation

/// What a tester is asked to quote in a report: which build this is.
///
/// Read out of the bundle rather than written down anywhere by hand. The version and the build
/// number are set by the release script at archive time, so they cannot drift from what shipped.
enum AppInfo {

    static var version: String { string("CFBundleShortVersionString") }
    static var build: String { string("CFBundleVersion") }

    /// "0.1.0 (42)" - the two numbers App Store Connect uses to tell builds apart.
    static var versionLine: String { "\(version) (\(build))" }

    private static func string(_ key: String) -> String {
        Bundle.main.object(forInfoDictionaryKey: key) as? String ?? "?"
    }
}
