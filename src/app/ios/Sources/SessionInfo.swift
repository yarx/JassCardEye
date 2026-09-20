import Foundation

/// What a session was recorded with, written as JSON beside its recording and recognition log:
/// device, system, app and model, and the settings the pile was counting with. Sessions from
/// different phones can only be compared - in the dataset tool's *Analyse sessions* - when each says what
/// it was recorded on and with.
///
/// The keys are the same on Android (`SessionInfo.kt`) and described under "Session recordings" in
/// context/architecture/data-pipeline.md.
struct SessionInfo: Encodable {
    var platform: String
    var appVersion: String
    var appBuild: String
    var device: String
    var system: String
    var modelVariant: String
    var modelRun: String?
    var compute: String
    var confidenceThreshold: Double
    var stabilityRule: String
    var stabilityFrames: Int
    var deck: String
    var cameraLens: String
    var discipline: String
    var startedAt: String

    /// Snake case, sorted and indented, so a file reads the same whichever app wrote it.
    func json() throws -> Data {
        let encoder = JSONEncoder()
        encoder.keyEncodingStrategy = .convertToSnakeCase
        encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
        return try encoder.encode(self)
    }

    static var platform: String {
        #if os(iOS)
        "iOS"
        #else
        "macOS"
        #endif
    }

    /// The machine identifier, "iPhone17,1", rather than a marketing name: it is what tells two phones of
    /// the same year apart.
    static var deviceModel: String {
        var system = utsname()
        uname(&system)
        return withUnsafeBytes(of: &system.machine) { bytes in
            String(decoding: bytes.prefix { $0 != 0 }, as: UTF8.self)
        }
    }

    static var systemVersion: String {
        let version = ProcessInfo.processInfo.operatingSystemVersion
        return "\(version.majorVersion).\(version.minorVersion).\(version.patchVersion)"
    }

    /// A threshold as it was meant, not as a `Float` stores it: 0.6 rather than 0.6000000238.
    static func rounded(_ value: Float) -> Double { (Double(value) * 1000).rounded() / 1000 }

    static func timestamp(_ date: Date = Date()) -> String {
        let formatter = ISO8601DateFormatter()
        formatter.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        return formatter.string(from: date)
    }

    /// The training run a bundled model came from, out of the `models.json` the export writes beside it.
    static func modelRun(variant: String) -> String? {
        guard let url = Bundle.main.url(forResource: "models", withExtension: "json"),
              let data = try? Data(contentsOf: url),
              let root = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
              let models = root["models"] as? [String: Any],
              let model = models[variant] as? [String: Any] else { return nil }
        return model["run_id"] as? String
    }
}
