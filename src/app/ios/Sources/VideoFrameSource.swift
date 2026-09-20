#if targetEnvironment(simulator)
import AVFoundation
import SwiftUI
import CoreVideo
import Foundation

/// Simulator-only stand-in for the camera: plays a video file in a loop and hands its frames to the
/// same detection path the camera feeds. The simulator has no camera, so this is the only way to
/// exercise the live loop - stability rule, pile, score - without a device.
///
/// The file is **never bundled**. A simulator app can read the host filesystem directly, so the video
/// is loaded from an absolute path and the shipping app carries neither the video nor this code.
/// The path comes from the `JASSCARDEYE_VIDEO` environment variable (Xcode: Edit Scheme → Run →
/// Arguments → Environment Variables).
final class VideoFrameSource {

    let player = AVPlayer()
    private var output: AVPlayerItemVideoOutput?
    private var endObserver: NSObjectProtocol?
    private var timer: DispatchSourceTimer?
    private let frameQueue = DispatchQueue(label: "ch.yarx.jasscardeye.videoframes")

    /// Called on the frame queue for every frame that is pulled.
    var onFrame: ((CVPixelBuffer) -> Void)?

    /// Loads the video. Returns an error text for the UI, nil on success.
    func configure() async -> String? {
        guard let path = ProcessInfo.processInfo.environment["JASSCARDEYE_VIDEO"], !path.isEmpty else {
            return "Kein Testvideo gesetzt: Umgebungsvariable JASSCARDEYE_VIDEO fehlt."
        }
        guard FileManager.default.fileExists(atPath: path) else {
            return "Testvideo nicht gefunden: \(path)"
        }

        let asset = AVURLAsset(url: URL(fileURLWithPath: path))
        let tracks = (try? await asset.loadTracks(withMediaCharacteristic: .visual)) ?? []
        guard !tracks.isEmpty else {
            return "Testvideo enthält keine Videospur."
        }

        // A composition does two jobs here. It bakes in the recorded orientation, so frames arrive
        // upright like the rotated camera buffers do. And it pins the colour space to Rec. 709:
        // iPhone recordings are HDR, and without this they are read as SDR and come out heavily
        // shifted towards pink - which would hand the detector colours no card ever has.
        let item = AVPlayerItem(asset: asset)
        if let composition = try? await AVMutableVideoComposition.videoComposition(withPropertiesOf: asset) {
            composition.colorPrimaries = AVVideoColorPrimaries_ITU_R_709_2
            composition.colorTransferFunction = AVVideoTransferFunction_ITU_R_709_2
            composition.colorYCbCrMatrix = AVVideoYCbCrMatrix_ITU_R_709_2
            item.videoComposition = composition
        }

        // IOSurface backing is not optional here: Vision rejects pixel buffers without it, and the
        // failure is silent - detection simply returns nothing.
        // Spelled out as [String: Any] rather than left to inference: an inline literal here is
        // read as [AnyHashable: Any], which does not survive the crossing out of this async
        // function - a warning today, an error in Swift 6.
        let attributes: [String: Any] = [
            kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA,
            kCVPixelBufferIOSurfacePropertiesKey as String: [String: Any](),
        ]
        let output = AVPlayerItemVideoOutput(pixelBufferAttributes: attributes)
        item.add(output)
        self.output = output

        // Looping is done by hand instead of with AVPlayerLooper: the looper plays *copies* of the
        // template item, and the video output stays attached to the original - so no frame would
        // ever arrive. Rewinding one item keeps the output where it belongs.
        player.replaceCurrentItem(with: item)
        player.actionAtItemEnd = .none
        player.isMuted = true
        // Captures the player, not the source: the observer only ever rewinds, and the closure is
        // @Sendable, which a whole non-Sendable VideoFrameSource has no business crossing into.
        let player = self.player
        endObserver = NotificationCenter.default.addObserver(
            forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main
        ) { _ in
            player.seek(to: .zero)
            player.play()
        }
        return nil
    }

    func start() {
        player.play()

        // Polls at 30 Hz and skips whenever no new frame is ready. Frames that arrive while the
        // detector is busy are simply not fetched - the same backpressure the camera output applies.
        let timer = DispatchSource.makeTimerSource(queue: frameQueue)
        timer.schedule(deadline: .now(), repeating: .milliseconds(33))
        timer.setEventHandler { [weak self] in
            guard let self, let output = self.output else { return }
            let time = output.itemTime(forHostTime: CACurrentMediaTime())
            guard output.hasNewPixelBuffer(forItemTime: time),
                  let buffer = output.copyPixelBuffer(forItemTime: time, itemTimeForDisplay: nil)
            else { return }
            self.onFrame?(buffer)
        }
        timer.resume()
        self.timer = timer
    }

    func stop() {
        timer?.cancel()
        timer = nil
        player.pause()
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        endObserver = nil
    }
}

/// Shows the looping test video where the camera preview would be, with the same resize-aspect
/// behaviour so the overlay geometry stays valid.
struct VideoPreview: UIViewRepresentable {
    let player: AVPlayer

    final class PlayerView: UIView {
        override class var layerClass: AnyClass { AVPlayerLayer.self }
        var playerLayer: AVPlayerLayer { layer as! AVPlayerLayer }
    }

    func makeUIView(context: Context) -> PlayerView {
        let view = PlayerView()
        view.playerLayer.player = player
        // The preview lives in a square view, and filling it crops to the centred square of the
        // source - exactly the region Vision is given, so picture and analysis agree by construction.
        view.playerLayer.videoGravity = .resizeAspectFill
        return view
    }

    func updateUIView(_ view: PlayerView, context: Context) {}
}
#endif
