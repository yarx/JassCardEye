import AVFoundation

/// Which back camera films the pile.
///
/// The default 1× lens makes a phone held over a table feel too close: to get a whole pile into the
/// framing square you have to hold it uncomfortably high. The ultra-wide sees roughly twice as much
/// at the same height - at the price of the card landing on fewer pixels, which is why choosing it
/// also raises the capture resolution.
enum CameraLens: String, CaseIterable, Identifiable, Codable {
    /// The standard lens, confusingly called "wide angle" by AVFoundation.
    case wide
    /// The 0.5× lens. Not on every model, so it is only offered where it exists.
    case ultraWide

    var id: String { rawValue }

    var displayName: String {
        switch self {
        case .wide:      return "Normal (1×)"
        case .ultraWide: return "Weitwinkel (0,5×)"
        }
    }

    var explanation: String {
        switch self {
        case .wide:
            return "Der übliche Bildausschnitt. Das Telefon muss höher über den Tisch, damit der ganze Stapel ins Quadrat passt."
        case .ultraWide:
            return "Sieht bei gleicher Höhe deutlich mehr Tisch. Die Karte belegt dafür weniger Pixel – die Aufnahme läuft darum in höherer Auflösung, damit dem Modell gleich viel Detail bleibt."
        }
    }

    #if os(iOS)
    var deviceType: AVCaptureDevice.DeviceType {
        switch self {
        case .wide:      return .builtInWideAngleCamera
        case .ultraWide: return .builtInUltraWideCamera
        }
    }
    #endif

    /// Capturing wider means the card covers less of the frame, so the square that reaches the model
    /// carries less of it. More capture resolution puts that detail back before the downscale to 640.
    var preset: AVCaptureSession.Preset {
        self == .ultraWide ? .hd1920x1080 : .hd1280x720
    }

    /// The lenses this device actually has, in the order they are offered. Asked of the hardware
    /// rather than assumed: several iPhones have no ultra-wide at all, and a picker that offers one
    /// that is not there would just fail at the next session.
    static func available() -> [CameraLens] {
        #if os(iOS)
        let session = AVCaptureDevice.DiscoverySession(
            deviceTypes: allCases.map(\.deviceType), mediaType: .video, position: .back)
        let present = Set(session.devices.map(\.deviceType))
        return allCases.filter { present.contains($0.deviceType) }
        #else
        return AVCaptureDevice.default(for: .video) == nil ? [] : [.wide]
        #endif
    }
}
