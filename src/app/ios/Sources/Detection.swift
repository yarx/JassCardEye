import CoreML
import Vision
import CoreImage
import CoreGraphics

/// One recognised card: class label, confidence and the box in Vision's normalised coordinates
/// (origin bottom-left). Every variant produces this same shape, so the overlay and the stability
/// rule stay identical no matter which model is running.
struct Detection: Identifiable {
    let id = UUID()
    let label: String
    let confidence: Float
    let box: CGRect
}

/// A frame to run a model on, either straight from the camera or a still image. Kept as one type so a
/// recogniser can build a Vision handler and - for the two-stage variant - a CoreImage image for the
/// crop from the same source. The live loop only ever passes camera buffers; the still case is for
/// running a recogniser on a single image and is not created by the app itself.
enum ImageSource {
    case pixelBuffer(CVPixelBuffer)
    case cgImage(CGImage)

    func handler() -> VNImageRequestHandler {
        switch self {
        case .pixelBuffer(let buffer): return VNImageRequestHandler(cvPixelBuffer: buffer)
        case .cgImage(let image):      return VNImageRequestHandler(cgImage: image)
        }
    }

    /// CoreImage image with a bottom-left origin - the same convention Vision reports boxes in, so a
    /// normalised box maps into it without flipping.
    func ciImage() -> CIImage {
        switch self {
        case .pixelBuffer(let buffer): return CIImage(cvPixelBuffer: buffer)
        case .cgImage(let image):      return CIImage(cgImage: image)
        }
    }
}
