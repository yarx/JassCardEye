import CoreML
import Vision
import CoreImage
import CoreImage.CIFilterBuiltins
import CoreGraphics

/// Turns a frame into recognised cards. The three variants differ only behind this one call, so the
/// live loop - framing square, timing, stability rule, pile - is written once and never learns which
/// model it is driving.
///
/// `regionOfInterest` is the framing square (Vision-normalised, origin bottom-left); Vision crops to
/// it before scaling, so the square gets the full model resolution and returned boxes are normalised
/// to that square. Called from the camera queue, so implementations must be safe to use from a
/// background thread - they are immutable after `init` and Vision models are thread-safe to share.
protocol CardRecognizer: AnyObject, Sendable {
    func recognize(_ source: ImageSource, regionOfInterest: CGRect?, minConfidence: Float) throws -> [Detection]
}

/// A CoreML model wrapped for Vision, Neural Engine where available.
func loadVisionModel(_ url: URL) throws -> VNCoreMLModel {
    let config = MLModelConfiguration()
    config.computeUnits = .all
    return try VNCoreMLModel(for: try MLModel(contentsOf: url, configuration: config))
}

// MARK: - C: one-pass detector

/// Variant C. The 72-class detector (both decks) with NMS baked in: Vision returns finished boxes and
/// labels. The only variant a release bundles.
final class DetectorRecognizer: CardRecognizer, @unchecked Sendable {
    private let model: VNCoreMLModel

    init(modelURL: URL) throws { model = try loadVisionModel(modelURL) }

    func recognize(_ source: ImageSource, regionOfInterest: CGRect?, minConfidence: Float) throws -> [Detection] {
        let request = VNCoreMLRequest(model: model)
        request.imageCropAndScaleOption = .scaleFit   // the exporter already letterboxes to 640
        if let regionOfInterest { request.regionOfInterest = regionOfInterest }
        try source.handler().perform([request])

        let observations = (request.results as? [VNRecognizedObjectObservation]) ?? []
        return observations
            .compactMap { observation -> Detection? in
                // The confidence is the best class's own score, as on Android and in Ultralytics. Vision's
                // `observation.confidence` is the sum of the box's class scores, and its labels are those
                // scores normalised to sum to 1 - so the product is the score. Read directly, a box the
                // model cannot decide on (two classes at 0.6 each) would come out above 1: the highest
                // confidence of all exactly where it hesitates.
                guard let top = observation.labels.first else { return nil }
                let confidence = observation.confidence * top.confidence
                guard confidence >= minConfidence else { return nil }
                return Detection(label: top.identifier, confidence: confidence, box: observation.boundingBox)
            }
            .sorted { $0.confidence > $1.confidence }
    }
}

// MARK: - A: whole-image classifier

/// Variant A. One classifier over the whole framing square - a label, no box. The negatives were
/// trained under a `none` class, so a top prediction of `none` means "no card", the same abstention
/// the detector gets from simply finding nothing.
final class ClassifierRecognizer: CardRecognizer, @unchecked Sendable {
    private let model: VNCoreMLModel

    init(modelURL: URL) throws { model = try loadVisionModel(modelURL) }

    func recognize(_ source: ImageSource, regionOfInterest: CGRect?, minConfidence: Float) throws -> [Detection] {
        let request = VNCoreMLRequest(model: model)
        request.imageCropAndScaleOption = .scaleFit
        if let regionOfInterest { request.regionOfInterest = regionOfInterest }
        try source.handler().perform([request])

        let observations = (request.results as? [VNClassificationObservation]) ?? []
        guard let top = observations.first,
              top.confidence >= minConfidence,
              top.identifier != "none" else { return [] }

        // A does not localise: the detection is the whole framing square, so the overlay outlines it.
        return [Detection(label: top.identifier, confidence: top.confidence,
                          box: CGRect(x: 0, y: 0, width: 1, height: 1))]
    }
}

// MARK: - B: two stages

/// A located card: its four corners in the framing-square image's pixel space (origin top-left, the
/// order TL, TR, BR, BL) and the locator's confidence.
struct LocatedCard {
    var corners: [CGPoint]
    var confidence: Float
}

/// Variant B, stage one. The oriented-box locator (B₁). Its CoreML export carries no NMS - only detect
/// heads can, so Ultralytics forces `nms=False` for OBB - and Vision therefore returns the raw tensor
/// rather than finished boxes. The shape is [1, 6, 8400]: 8400 candidate boxes, each six numbers
/// `cx, cy, w, h, confidence, angle` in the 640-pixel model space, the channel layout of the PyTorch
/// model's own output. Only the single most confident card is needed - the app
/// reads one top card - so this takes the argmax over confidence instead of running NMS.
final class CardLocator: @unchecked Sendable {
    private let model: VNCoreMLModel

    init(modelURL: URL) throws { model = try loadVisionModel(modelURL) }

    /// Runs on the framing-square image (square, so `.scaleFit` to the square model input adds no
    /// padding) and returns the card's four corners in that image's pixels, or nil when it sees none.
    func locate(_ square: CGImage, minConfidence: Float) throws -> LocatedCard? {
        let request = VNCoreMLRequest(model: model)
        request.imageCropAndScaleOption = .scaleFit
        try VNImageRequestHandler(cgImage: square).perform([request])

        guard let feature = (request.results as? [VNCoreMLFeatureValueObservation])?.first,
              let array = feature.featureValue.multiArrayValue,
              array.shape.count == 3, array.shape[1].intValue == 6,
              let best = bestBox(array, minConfidence: minConfidence) else { return nil }

        // Corners in the 640 model space (top-left origin), then scaled to the square's own pixels.
        // Ultralytics normalises w/h/angle before reporting them, but the raw values describe the same
        // rectangle, so the centre ± the rotated half-extents give the right quad directly.
        let (cx, cy, w, h, angle) = best.box
        let cosA = cos(angle), sinA = sin(angle)
        let scale = CGFloat(square.width) / Self.modelSize     // square: width == height
        let offsets: [(Float, Float)] = [(-w / 2, -h / 2), (w / 2, -h / 2), (w / 2, h / 2), (-w / 2, h / 2)]
        let corners = offsets.map { dx, dy -> CGPoint in
            CGPoint(x: CGFloat(dx * cosA - dy * sinA + cx) * scale,
                    y: CGFloat(dx * sinA + dy * cosA + cy) * scale)
        }
        return LocatedCard(corners: corners, confidence: best.confidence)
    }

    private static let modelSize: CGFloat = 640

    /// The most confident box in the raw [1, 6, 8400] tensor, above the floor. Reads the channels
    /// straight from the backing buffer via the array's own strides, for whatever element type the
    /// export used.
    private func bestBox(_ array: MLMultiArray, minConfidence: Float)
        -> (box: (cx: Float, cy: Float, w: Float, h: Float, angle: Float), confidence: Float)? {
        let anchors = array.shape[2].intValue
        let channelStride = array.strides[1].intValue
        let anchorStride = array.strides[2].intValue

        let read: (Int) -> Float
        switch array.dataType {
        case .float32:
            let p = array.dataPointer.assumingMemoryBound(to: Float.self); read = { p[$0] }
        case .float16:
            let p = array.dataPointer.assumingMemoryBound(to: Float16.self); read = { Float(p[$0]) }
        case .double:
            let p = array.dataPointer.assumingMemoryBound(to: Double.self); read = { Float(p[$0]) }
        default:
            return nil
        }
        func value(_ channel: Int, _ anchor: Int) -> Float { read(channel * channelStride + anchor * anchorStride) }

        var bestAnchor = -1
        var bestConf = minConfidence
        for anchor in 0..<anchors {
            let conf = value(4, anchor)
            if conf > bestConf { bestConf = conf; bestAnchor = anchor }
        }
        guard bestAnchor >= 0 else { return nil }
        return ((value(0, bestAnchor), value(1, bestAnchor), value(2, bestAnchor),
                 value(3, bestAnchor), value(5, bestAnchor)), bestConf)
    }
}

/// Variant B. Stage one locates the card, stage two classifies the crop rectified from its oriented
/// box. The whole call is timed by the live loop, so B's reported latency already includes both models
/// and the perspective correction between them - which is exactly the question the variant answers.
///
/// A caveat carried knowingly: the oriented box has a 180° ambiguity (angle in [-π/4, 3π/4)), so a
/// face card can be rectified upside down and misread. It is a real property of the two-stage
/// approach, not a bug to hide.
final class TwoStageRecognizer: CardRecognizer, @unchecked Sendable {
    private let locator: CardLocator
    private let classifier: VNCoreMLModel

    /// Finding a card is easier than reading it, so stage one keeps its own low floor and the caller's
    /// threshold is applied to the final class.
    private static let locateFloor: Float = 0.25

    private static let ciContext = CIContext(options: [.useSoftwareRenderer: false])

    init(locatorURL: URL, classifierURL: URL) throws {
        locator = try CardLocator(modelURL: locatorURL)
        classifier = try loadVisionModel(classifierURL)
    }

    func recognize(_ source: ImageSource, regionOfInterest: CGRect?, minConfidence: Float) throws -> [Detection] {
        // Cut the framing square out first and work entirely in its pixel space: stage one, the
        // rectification and the overlay box all share one coordinate system, so no Vision region
        // transform has to be unwound.
        let region = regionOfInterest ?? CGRect(x: 0, y: 0, width: 1, height: 1)
        guard let square = croppedCGImage(source, normalizedRect: region),
              let located = try locator.locate(square, minConfidence: Self.locateFloor),
              let crop = rectify(square, corners: located.corners) else { return [] }

        let request = VNCoreMLRequest(model: classifier)
        request.imageCropAndScaleOption = .scaleFit
        try VNImageRequestHandler(cgImage: crop).perform([request])

        let observations = (request.results as? [VNClassificationObservation]) ?? []
        guard let top = observations.first, top.confidence >= minConfidence else { return [] }

        // The label is stage two's; the box is the axis-aligned hull of stage one's quad, normalised
        // to the square with a bottom-left origin - the same space variant C reports, so the overlay
        // draws it unchanged. Confidence is the weaker of the two: the chain is only as sure as its
        // least sure stage.
        return [Detection(label: top.identifier,
                          confidence: min(located.confidence, top.confidence),
                          box: boundingBox(located.corners, width: square.width, height: square.height))]
    }

    /// Rectifies the card to an upright image via its four corners. CoreImage works bottom-left, the
    /// corners come top-left, so each is flipped in y before being handed to the perspective filter.
    private func rectify(_ image: CGImage, corners: [CGPoint]) -> CGImage? {
        guard corners.count == 4 else { return nil }
        let height = CGFloat(image.height)
        func flip(_ p: CGPoint) -> CGPoint { CGPoint(x: p.x, y: height - p.y) }

        let filter = CIFilter.perspectiveCorrection()
        filter.inputImage = CIImage(cgImage: image)
        filter.topLeft = flip(corners[0])
        filter.topRight = flip(corners[1])
        filter.bottomRight = flip(corners[2])
        filter.bottomLeft = flip(corners[3])
        guard let output = filter.outputImage, !output.extent.isEmpty else { return nil }
        return Self.ciContext.createCGImage(output, from: output.extent)
    }

    /// Axis-aligned box around the corners, normalised to the square and flipped to a bottom-left
    /// origin so it lands in the same space the overlay uses for every variant.
    private func boundingBox(_ corners: [CGPoint], width: Int, height: Int) -> CGRect {
        let w = CGFloat(width), h = CGFloat(height)
        let xs = corners.map { $0.x / w }
        let ysFromTop = corners.map { $0.y / h }
        let minX = xs.min() ?? 0, maxX = xs.max() ?? 0
        let minTop = ysFromTop.min() ?? 0, maxTop = ysFromTop.max() ?? 0
        return CGRect(x: minX, y: 1 - maxTop, width: maxX - minX, height: maxTop - minTop)
    }

    /// Cuts a normalised rectangle (origin bottom-left, matching CoreImage) out of the source frame.
    private func croppedCGImage(_ source: ImageSource, normalizedRect rect: CGRect) -> CGImage? {
        let image = source.ciImage()
        let extent = image.extent
        guard extent.width > 0, extent.height > 0 else { return nil }

        let pixels = CGRect(
            x: extent.minX + rect.minX * extent.width,
            y: extent.minY + rect.minY * extent.height,
            width: rect.width * extent.width,
            height: rect.height * extent.height
        ).integral
        let cropped = image.cropped(to: pixels)
        guard !cropped.extent.isEmpty else { return nil }
        return Self.ciContext.createCGImage(cropped, from: cropped.extent)
    }
}
