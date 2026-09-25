import CoreImage
import CoreVideo
import Foundation
import ImageIO
import UniformTypeIdentifiers

/// Saves the square the model was actually given, for looking at afterwards.
///
/// Real sessions throw up cards the detector will not name, and the useful evidence is not a photo
/// of the table: it is *what the model saw* - the centred square, at the resolution Vision analysed.
/// That square is the same thing a dataset image is, so a capture can go straight into the labelling
/// tool and become a validation case for the very situation that failed.
///
/// Files land in the app's Documents folder, which the Files app lists under "Auf meinem iPhone →
/// JassCardEye", so a session's worth can be taken off the phone as one folder.
enum FrameCapture {

    enum Failure: LocalizedError {
        case render, write

        var errorDescription: String? {
            switch self {
            case .render: return "Bild konnte nicht erzeugt werden."
            case .write:  return "Datei konnte nicht geschrieben werden."
            }
        }
    }

    private static let context = CIContext(options: [.useSoftwareRenderer: false])

    /// Where captures go.
    ///
    /// On iOS that is the app's own Documents folder, which the Files app lists. On the Mac the app
    /// is not sandboxed, so the same call would return the user's *real* ~/Documents - a diagnostic
    /// tool has no business writing there, hence its own folder in Application Support.
    static var folder: URL {
        let manager = FileManager.default
        #if os(iOS)
        let base = manager.urls(for: .documentDirectory, in: .userDomainMask).first
        let url = base ?? manager.temporaryDirectory
        #else
        let support = manager.urls(for: .applicationSupportDirectory, in: .userDomainMask).first
        let url = (support ?? manager.temporaryDirectory).appendingPathComponent("JassCardEye")
        #endif
        // Created on demand: Application Support has no folder of ours until something writes one,
        // and a missing directory would fail the write with a puzzling message.
        try? manager.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    /// Crops `buffer` to the analysed region, writes it as JPEG and returns the file name.
    ///
    /// Called on the camera queue: cropping and encoding are not free, and doing them there keeps
    /// them off the main thread. The buffer is used and released within the call - holding on to one
    /// would take it out of the capture pool and stall the camera.
    static func save(_ buffer: CVPixelBuffer, region: CGRect,
                     label: String?, confidence: Float, mode: String) throws -> String {
        let image = CIImage(cvPixelBuffer: buffer)
        let extent = image.extent
        guard extent.width > 0, extent.height > 0 else { throw Failure.render }

        let cropped = image.cropped(to: FrameGeometry.crop(region, in: extent))
        guard !cropped.extent.isEmpty,
              let cgImage = context.createCGImage(cropped, from: cropped.extent) else {
            throw Failure.render
        }

        let name = fileName(label: label, confidence: confidence, mode: mode)
        let url = folder.appendingPathComponent(name)
        guard let destination = CGImageDestinationCreateWithURL(
            url as CFURL, UTType.jpeg.identifier as CFString, 1, nil) else { throw Failure.write }

        // High quality: these are diagnostic images, and compression artefacts would be one more
        // thing to argue about when a card turns out to be unrecognisable.
        CGImageDestinationAddImage(destination, cgImage,
                                   [kCGImageDestinationLossyCompressionQuality: 0.95] as CFDictionary)
        guard CGImageDestinationFinalize(destination) else { throw Failure.write }
        return name
    }

    /// Name for a recorded session, in the same folder and the same shape of name as a still.
    static func sessionFileName(mode: String) -> String {
        "session_\(stamp())_\(modeToken(mode)).mov"
    }

    /// The whole context travels in the name, so it survives AirDrop and a copy into any folder:
    /// when it was taken, what the model made of it (or that it saw nothing), and in which mode.
    private static func fileName(label: String?, confidence: Float, mode: String) -> String {
        let verdict = label.map { "\($0)-\(Int(confidence * 100))" } ?? "nichts"
        return "capture_\(stamp())_\(verdict)_\(modeToken(mode)).jpg"
    }

    /// A discipline's token - see `CountingMode.token(deck:)` - may carry a dot ("slalom.obe"),
    /// which would read as a second file extension.
    private static func modeToken(_ mode: String) -> String {
        mode.lowercased().filter { $0.isLetter || $0.isNumber }
    }

    /// Down to milliseconds: two taps in the same second must not overwrite each other.
    private static func stamp() -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.dateFormat = "yyyyMMdd-HHmmss-SSS"
        return formatter.string(from: Date())
    }
}
