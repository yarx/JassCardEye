import AVFoundation
import CoreImage
import CoreVideo
import Foundation

/// Records a whole counting session as a square video.
///
/// What it writes is not the camera stream but the **analysed square, frame by frame as the model
/// received it** - same crop, including the frames dropped while an inference was running. Replayed
/// later it therefore follows the run rather than the camera, which is what makes it usable as
/// simulator input and as the basis of a test case. It is not a perfect copy: a frame the encoder
/// is not ready for is skipped, and encoding costs time inside the frame loop, so a recorded
/// session runs at a slightly lower analysis rate than the same session unrecorded.
///
/// Next to the video it writes the **recognition log**, a CSV of the same name: one row
/// per frame in the video, saying what the model made of it, row n for frame n. A row is written only
/// once the writer has taken the frame, with that frame's presentation time, so a skipped frame leaves
/// no row behind.
///
/// When the session ends, video, log and session info are packed into one ZIP (`SessionArchive`), so
/// one file per session is what gets copied off the phone.
///
/// Frames arrive on the camera queue and `finish()` is called from the main actor, so the little
/// state there is guarded.
final class SessionRecorder: @unchecked Sendable {

    /// How a recording ended. A failure is reported rather than swallowed: a broken recording and a
    /// successful empty one look identical from outside, and the whole point of recording is to
    /// have the evidence afterwards.
    enum Outcome {
        /// Written and closed, and packed into the ZIP of this name. `problem` says why packing failed -
        /// the files are then kept together in a folder of the session's name.
        case saved(name: String, frames: Int, problem: String?)
        /// Nothing was ever written; no file left behind.
        case nothingRecorded
        /// The recording broke. The partial file has been removed; `reason` is shown to the user.
        case failed(reason: String)
    }

    /// What the model made of one recorded frame: one row of the recognition log, with the same
    /// columns as on Android.
    struct FrameResult: Sendable {
        /// The columns, described under "Session recordings" in context/architecture/data-pipeline.md.
        static let header = "frame,t_ms,label,confidence,x,y,w,h,committed\n"

        var label: String? = nil
        var confidence: Float = 0
        /// Vision's box: normalised to the analysed square, origin bottom-left.
        var box: CGRect = .zero
        var committed: Bool = false

        /// The row for frame `index` of the video, presented at `time`. The box is written with a
        /// top-left origin, as Android has it and as the Dataset Tool draws it.
        func row(index: Int, time: CMTime) -> String {
            let head = "\(index),\(Self.number(time.seconds * 1000, digits: 3)),"
            let flag = committed ? "1" : "0"
            guard let label else { return head + ",,,,,,\(flag)\n" }
            let numbers = [Double(confidence), box.minX, 1 - box.maxY, box.width, box.height]
                .map { Self.number($0, digits: 4) }
            return head + ([label] + numbers + [flag]).joined(separator: ",") + "\n"
        }

        private static func number(_ value: Double, digits: Int) -> String {
            String(format: "%.\(digits)f", locale: Locale(identifier: "en_US_POSIX"), value)
        }
    }

    private var csvURL: URL { url.deletingPathExtension().appendingPathExtension("csv") }
    private var infoURL: URL { url.deletingPathExtension().appendingPathExtension("json") }
    /// The session info (`SessionInfo`) as JSON, written beside the log when the recording starts.
    private let info: Data?
    private var csv: FileHandle?
    private var lastTime = CMTime.invalid
    private let url: URL
    private let context = CIContext(options: [.useSoftwareRenderer: false])

    private let lock = NSLock()
    private var writer: AVAssetWriter?
    private var input: AVAssetWriterInput?
    private var adaptor: AVAssetWriterInputPixelBufferAdaptor?
    private var startedAt: CFAbsoluteTime?
    private var closed = false
    private var frameCount = 0

    /// Set once the recording has broken, and never cleared. It stops the retry loop and it is what
    /// `finish()` reports.
    private var failure: String?

    init(url: URL, info: Data? = nil) {
        self.url = url
        self.info = info
    }

    /// Appends the analysed square of this frame. Called on the camera queue; does nothing once the
    /// recording is closed or has failed, and skips a frame while the encoder is not ready - a
    /// dropped frame is far better than a stalled capture.
    func append(_ buffer: CVPixelBuffer, region: CGRect, result: FrameResult = .init()) {
        lock.lock()
        defer { lock.unlock() }
        // A recording that has already broken must not keep paying for itself. This runs on the
        // camera queue, which is the inference loop: without this guard a writer that fails in
        // second 20 of a count would go on rendering a CIImage and taking a pool buffer per frame
        // for the remaining 40 seconds, for nothing.
        guard !closed, failure == nil else { return }

        let image = CIImage(cvPixelBuffer: buffer)
        let extent = image.extent
        guard extent.width > 0, extent.height > 0 else { return }

        // The same rectangle FrameCapture cuts - literally, so the recording and a still taken from
        // the same session can never show different squares.
        let rect = FrameGeometry.crop(region, in: extent)
        guard rect.width >= 16, rect.height >= 16 else { return }

        // start() records its own failure, which the guard above then catches on the next frame.
        if writer == nil, !start(side: Int(min(rect.width, rect.height))) { return }
        guard let writer, let input, let adaptor else { return }

        // The encoder reports a file it can no longer write - a full disk is the common one - in
        // its status rather than by refusing the next append.
        guard writer.status != .failed else {
            fail(writer.error.map { "Aufnahme abgebrochen: \($0.localizedDescription)" }
                 ?? "Aufnahme abgebrochen.", cancelling: writer)
            return
        }

        guard input.isReadyForMoreMediaData, let pool = adaptor.pixelBufferPool else { return }

        var target: CVPixelBuffer?
        guard CVPixelBufferPoolCreatePixelBuffer(nil, pool, &target) == kCVReturnSuccess,
              let target else { return }

        // Moved to the origin so the crop fills the frame; rendering a CIImage into a pixel buffer
        // keeps CoreImage's bottom-left origin on both sides, so the picture stays the right way up.
        let moved = image.transformed(by: .init(translationX: -rect.minX, y: -rect.minY))
        context.render(moved, to: target)

        let now = CFAbsoluteTimeGetCurrent()
        if startedAt == nil { startedAt = now }
        let elapsed = now - (startedAt ?? now)
        // The return value is the only report of a rejected frame - a timestamp collision, say,
        // since timescale 600 quantises to 1.67 ms. Counting the frame regardless would leave a
        // number that says the recording went fine.
        var presentation = CMTime(seconds: max(0, elapsed), preferredTimescale: 600)
        if lastTime.isValid, presentation <= lastTime {
            presentation = lastTime + CMTime(value: 1, timescale: 600)
        }
        guard adaptor.append(target, withPresentationTime: presentation) else {
            fail(writer.error.map { "Aufnahme abgebrochen: \($0.localizedDescription)" }
                 ?? "Aufnahme abgebrochen - Bild abgewiesen.", cancelling: writer)
            return
        }
        do {
            guard let csv else { throw CocoaError(.fileWriteUnknown) }
            try csv.write(contentsOf: Data(result.row(index: frameCount, time: presentation).utf8))
        } catch {
            fail("Erkennungsprotokoll konnte nicht geschrieben werden: \(error.localizedDescription)", cancelling: writer)
            return
        }
        lastTime = presentation
        frameCount += 1
    }

    /// What `finish()` found when it closed the recording, taken under the lock.
    private struct Closing {
        let writer: AVAssetWriter?
        let input: AVAssetWriterInput?
        let frames: Int
        let failure: String?
    }

    /// Closes the gate and hands back what was there. Separate from `finish()` and deliberately
    /// synchronous: a lock must not be taken in an async function - it is held across a suspension
    /// point the compiler cannot see, and from Swift 6 on it is an error rather than a warning.
    /// Nothing in here awaits, so the whole critical section belongs on this side of the boundary.
    private func close() -> Closing? {
        lock.lock()
        defer { lock.unlock() }
        guard !closed else { return nil }
        closed = true
        do { try csv?.synchronize(); try csv?.close(); csv = nil }
        catch { fail("Erkennungsprotokoll konnte nicht abgeschlossen werden: \(error.localizedDescription)", cancelling: writer) }
        return Closing(writer: writer, input: input, frames: frameCount, failure: failure)
    }

    /// Closes the file and says what became of it. Safe to call more than once.
    func finish() async -> Outcome {
        guard let closing = close() else { return .nothingRecorded }
        let wrote = closing.frames

        // fail() has already cancelled the writer and removed the file.
        if let failure = closing.failure { return .failed(reason: failure) }

        guard let writer = closing.writer, let input = closing.input, wrote > 0 else {
            // Nothing recorded - leave no empty file behind.
            try? FileManager.default.removeItem(at: url)
            try? FileManager.default.removeItem(at: csvURL)
            try? FileManager.default.removeItem(at: infoURL)
            return .nothingRecorded
        }

        input.markAsFinished()
        await writer.finishWriting()
        guard writer.status == .completed else {
            try? FileManager.default.removeItem(at: url)
            try? FileManager.default.removeItem(at: csvURL)
            try? FileManager.default.removeItem(at: infoURL)
            return .failed(reason: writer.error.map {
                "Aufnahme nicht abgeschlossen: \($0.localizedDescription)"
            } ?? "Aufnahme nicht abgeschlossen.")
        }
        // One file per session to copy off the phone. A recording that cannot be packed is still a
        // recording: its files stay together in a folder, and the note says why.
        let name = url.deletingPathExtension().lastPathComponent
        let files = [url, csvURL, infoURL].filter { FileManager.default.fileExists(atPath: $0.path) }
        do {
            let archive = try SessionArchive.pack(files, name: name)
            return .saved(name: archive.lastPathComponent, frames: wrote, problem: nil)
        } catch {
            return .saved(name: name, frames: wrote, problem: "nicht gepackt: \(error.localizedDescription)")
        }
    }

    /// Gives up on this recording and takes the half-written file with it. Call with the lock held.
    ///
    /// Removing the file matters as much as the flag: `AVAssetWriter(outputURL:fileType:)` throws
    /// when something is already at the URL, so a leftover would turn every later start into a
    /// throw.
    private func fail(_ reason: String, cancelling target: AVAssetWriter?) {
        failure = reason
        if target?.status == .writing { target?.cancelWriting() }
        writer = nil
        input = nil
        adaptor = nil
        try? csv?.close()
        csv = nil
        try? FileManager.default.removeItem(at: csvURL)
        try? FileManager.default.removeItem(at: infoURL)
        try? FileManager.default.removeItem(at: url)
    }

    /// Builds the writer once the square's size is known - it depends on the lens, so it cannot be
    /// decided before the first frame arrives. Returns false and records the reason on failure.
    ///
    /// Every exit here has to clean up. `startWriting()` fails only *after* the writer has created
    /// the file, so a start that gives up without removing it leaves a zero-byte .mov that makes
    /// the next frame's attempt throw "file exists" - and the frame after that, for the rest of the
    /// session, at thirty throwing allocations a second, without anyone being told.
    private func start(side: Int) -> Bool {
        guard !FileManager.default.fileExists(atPath: url.path),
              !FileManager.default.fileExists(atPath: csvURL.path) else {
            failure = "Aufnahme existiert bereits. Bitte erneut starten."
            return false
        }
        // H.264 wants even dimensions.
        let size = max(16, side - side % 2)
        let settings: [String: Any] = [
            AVVideoCodecKey: AVVideoCodecType.h264,
            AVVideoWidthKey: size,
            AVVideoHeightKey: size,
        ]

        let writer: AVAssetWriter
        do {
            writer = try AVAssetWriter(outputURL: url, fileType: .mov)
        } catch {
            fail("Aufnahme konnte nicht angelegt werden: \(error.localizedDescription)",
                 cancelling: nil)
            return false
        }

        let input = AVAssetWriterInput(mediaType: .video, outputSettings: settings)
        input.expectsMediaDataInRealTime = true
        guard writer.canAdd(input) else {
            fail("Aufnahme: Video-Eingang abgelehnt.", cancelling: writer)
            return false
        }
        writer.add(input)

        let adaptor = AVAssetWriterInputPixelBufferAdaptor(
            assetWriterInput: input,
            sourcePixelBufferAttributes: [
                kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
                kCVPixelBufferWidthKey as String: size,
                kCVPixelBufferHeightKey as String: size,
                // Vision needs IOSurface-backed buffers, and a recording is meant to be replayed
                // through the same path - so the frames are made the same way.
                kCVPixelBufferIOSurfacePropertiesKey as String: [:],
            ])

        guard writer.startWriting() else {
            fail(writer.error.map { "Aufnahme konnte nicht gestartet werden: \($0.localizedDescription)" }
                 ?? "Aufnahme konnte nicht gestartet werden.", cancelling: writer)
            return false
        }
        do {
            try Data(FrameResult.header.utf8).write(to: csvURL, options: .withoutOverwriting)
            csv = try FileHandle(forWritingTo: csvURL)
            try csv?.seekToEnd()
            try info?.write(to: infoURL, options: .withoutOverwriting)
        } catch {
            fail("Erkennungsprotokoll konnte nicht angelegt werden: \(error.localizedDescription)", cancelling: writer)
            return false
        }
        writer.startSession(atSourceTime: .zero)

        self.writer = writer
        self.input = input
        self.adaptor = adaptor
        // The first accepted frame starts at zero; a leading gap creates an extra
        // empty sample in AVAssetReader and would offset every CSV index.
        return true
    }
}
