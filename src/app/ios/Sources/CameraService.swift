import AVFoundation
import CoreVideo

/// Owns the capture session and delivers video frames on a serial queue.
///
/// Backpressure is handled by the output itself (`alwaysDiscardsLateVideoFrames`): while a frame is
/// being analysed, newer frames are dropped rather than queued - so the displayed FPS is the real
/// analysis rate, and latency cannot pile up.
final class CameraService: NSObject, AVCaptureVideoDataOutputSampleBufferDelegate {

    let session = AVCaptureSession()
    private let frameQueue = DispatchQueue(label: "ch.yarx.jasscardeye.frames")

    /// Called on the frame queue for every analysed frame.
    var onFrame: ((CVPixelBuffer) -> Void)?

    /// Why a camera session could not be set up. Refusal is its own case because it is the only one
    /// the person holding the phone can do something about, and the screen offers them the way there.
    enum Problem {
        case denied
        case other(String)

        var message: String {
            switch self {
            case .denied: return "Kein Kamerazugriff."
            case .other(let text): return text
            }
        }

        var isDenied: Bool { if case .denied = self { return true }; return false }
    }

    /// Which lens is currently attached, read back from the session rather than remembered - so the
    /// answer cannot drift out of step with what is actually wired up.
    private var attachedLens: CameraLens? {
        guard let input = session.inputs.compactMap({ $0 as? AVCaptureDeviceInput }).first else {
            return nil
        }
        #if os(iOS)
        return CameraLens.allCases.first { $0.deviceType == input.device.deviceType }
        #else
        return .wide
        #endif
    }

    /// The attached capture device. The torch belongs to it, not to the session, which is why it is
    /// read back from the inputs rather than remembered separately.
    private var device: AVCaptureDevice? {
        session.inputs.compactMap { $0 as? AVCaptureDeviceInput }.first?.device
    }

    /// Whether this phone can light the table. False on a Mac, false in the simulator, and false
    /// until a device is attached - the scan screen hides the button rather than offering one that
    /// does nothing.
    var hasTorch: Bool {
        #if os(iOS)
        return device?.hasTorch == true
        #else
        return false
        #endif
    }

    /// Switches the torch and reports what it is actually doing afterwards.
    ///
    /// The answer is not always what was asked. iOS refuses the torch while the phone is too warm
    /// and switches it off again on its own, so the caller takes the returned state rather than the
    /// requested one - otherwise the button would claim a light that is not on.
    @discardableResult
    func setTorch(_ on: Bool) -> Bool {
        #if os(iOS)
        guard let device, device.hasTorch else { return false }
        do {
            try device.lockForConfiguration()
            defer { device.unlockForConfiguration() }
            if on {
                // Not full power. The phone is held a hand's width above the pile, and at that
                // range the full beam blows the white of a card out until the pips go with it -
                // which would make the torch the reason the model stops recognising anything.
                try device.setTorchModeOn(level: Self.torchLevel)
            } else {
                device.torchMode = .off
            }
            return device.torchMode == .on
        } catch {
            return false
        }
        #else
        return false
        #endif
    }

    /// Bright enough for a dark room, dim enough not to burn out a card at arm's length. A guess
    /// worth revisiting with a real dark table: `AVCaptureDevice.maxAvailableTorchLevel` is 1.
    private static let torchLevel: Float = 0.6

    /// Requests permission and wires up the session. Returns the problem for the UI, nil on success.
    ///
    /// Safe to call again: the session outlives a single counting session, so every scan runs
    /// through here. Attaching the same device a second time is rejected - `canAddInput` is false
    /// once an input for it exists - so a session already wired up with the lens asked for is left
    /// alone, and any other is rebuilt from a clean slate.
    func configure(lens: CameraLens) async -> Problem? {
        guard await AVCaptureDevice.requestAccess(for: .video) else { return .denied }

        // Already wired up with the lens being asked for - nothing to do, and re-adding would fail.
        // A *different* lens falls through and the session is rebuilt around it.
        if attachedLens == lens, !session.outputs.isEmpty { return nil }

        #if os(iOS)
        // Falls back to the standard lens: a stored setting can outlive the phone it was made on.
        let device = AVCaptureDevice.default(lens.deviceType, for: .video, position: .back)
            ?? AVCaptureDevice.default(.builtInWideAngleCamera, for: .video, position: .back)
        #else
        let device = AVCaptureDevice.default(for: .video)
        #endif
        guard let device, let input = try? AVCaptureDeviceInput(device: device) else {
            return .other("Keine Kamera gefunden - die App braucht ein echtes Gerät.")
        }

        session.beginConfiguration()
        // An earlier attempt may have got half way (input attached, output refused), and a lens
        // change has to drop the old input - starting from a clean slate covers both.
        session.inputs.forEach(session.removeInput)
        session.outputs.forEach(session.removeOutput)

        guard session.canAddInput(input) else {
            session.commitConfiguration()
            return .other("Kamera konnte nicht eingebunden werden.")
        }
        session.addInput(input)

        // Set once the input is attached, so what the device supports can actually be asked.
        session.sessionPreset = session.canSetSessionPreset(lens.preset) ? lens.preset : .hd1280x720

        let output = AVCaptureVideoDataOutput()
        output.videoSettings = [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA]
        output.alwaysDiscardsLateVideoFrames = true
        output.setSampleBufferDelegate(self, queue: frameQueue)
        guard session.canAddOutput(output) else {
            session.commitConfiguration()
            return .other("Video-Ausgabe konnte nicht eingebunden werden.")
        }
        session.addOutput(output)

        // Rotate the buffers to portrait on the phone, so image, preview and overlay agree and no
        // orientation bookkeeping is needed downstream.
        #if os(iOS)
        if let connection = output.connection(with: .video),
           connection.isVideoRotationAngleSupported(90) {
            connection.videoRotationAngle = 90
        }
        #endif

        session.commitConfiguration()
        return nil
    }

    // Starting and stopping are checked *inside* the queue, not before dispatching. Closing a
    // session and opening the next one in quick succession would otherwise race: stop() has only
    // queued stopRunning, so isRunning is still true when start() looks - start() would bail out and
    // the queued stop would then leave the camera off for good. On one serial queue the checks
    // happen in the order the taps did.
    func start() {
        frameQueue.async {
            guard !self.session.isRunning else { return }
            self.session.startRunning()
        }
    }

    func stop() {
        frameQueue.async {
            guard self.session.isRunning else { return }
            self.session.stopRunning()
        }
    }

    func captureOutput(_ output: AVCaptureOutput, didOutput sampleBuffer: CMSampleBuffer,
                       from connection: AVCaptureConnection) {
        guard let pixelBuffer = CMSampleBufferGetImageBuffer(sampleBuffer) else { return }
        onFrame?(pixelBuffer)
    }
}
