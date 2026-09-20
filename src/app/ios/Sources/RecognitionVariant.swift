import Foundation

/// The three approaches of the thesis. Only C is pursued and shipped: a release bundles exactly one
/// variant, `c`. A and B stay here and remain trainable; with their models bundled they are
/// selectable at runtime.
///
///   C  one detector, 72 classes: boxes and labels in a single pass.
///   A  one classifier over the whole framing square: a label, no localisation.
///   B  two stages: a locator finds the card, a classifier reads the rectified crop.
enum RecognitionVariant: String, CaseIterable, Identifiable, Sendable {
    case c, a, b

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .c: return "C · Detektor"
        case .a: return "A · Klassifikation"
        case .b: return "B · Zweistufig"
        }
    }

    /// The compiled model(s) this variant needs in the bundle, by base name
    /// (`JassCardEye-<name>.mlmodelc`). B is the only one that needs two.
    var requiredModels: [String] {
        switch self {
        case .c: return ["c"]
        case .a: return ["a"]
        case .b: return ["b1", "b2"]
        }
    }
}

/// What is actually in the app bundle. Xcode compiles every bundled `.mlpackage` to a `.mlmodelc`,
/// so which variants can run is decided at launch from the files present - a variant whose models
/// were never exported simply does not appear in the picker.
enum ModelCatalog {
    private static let prefix = "JassCardEye-"

    /// Base names of every bundled model, e.g. `["c", "a", "b1", "b2"]`.
    static func bundledModelNames() -> Set<String> {
        let urls = Bundle.main.urls(forResourcesWithExtension: "mlmodelc", subdirectory: nil) ?? []
        return Set(urls.compactMap { url in
            let name = url.deletingPathExtension().lastPathComponent   // "JassCardEye-c"
            guard name.hasPrefix(prefix) else { return nil }
            return String(name.dropFirst(prefix.count))
        })
    }

    /// Variants whose every required model is present, in the fixed order C, A, B.
    static func availableVariants() -> [RecognitionVariant] {
        let present = bundledModelNames()
        return RecognitionVariant.allCases.filter { variant in
            variant.requiredModels.allSatisfy(present.contains)
        }
    }

    static func modelURL(_ name: String) -> URL? {
        Bundle.main.url(forResource: "\(prefix)\(name)", withExtension: "mlmodelc")
    }

    enum CatalogError: LocalizedError {
        case missingModel(String)
        var errorDescription: String? {
            switch self { case .missingModel(let n): return "Modell '\(n)' fehlt im Bundle." }
        }
    }

    /// Builds the recogniser for a variant, loading its model(s) from the bundle. Throws when a model
    /// is missing or fails to load, so the caller can show why a variant is unavailable.
    static func makeRecognizer(_ variant: RecognitionVariant) throws -> CardRecognizer {
        func url(_ name: String) throws -> URL {
            guard let url = modelURL(name) else { throw CatalogError.missingModel(name) }
            return url
        }
        switch variant {
        case .c: return try DetectorRecognizer(modelURL: url("c"))
        case .a: return try ClassifierRecognizer(modelURL: url("a"))
        case .b: return try TwoStageRecognizer(locatorURL: try url("b1"), classifierURL: try url("b2"))
        }
    }
}
