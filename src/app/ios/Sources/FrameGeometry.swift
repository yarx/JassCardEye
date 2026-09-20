import CoreGraphics

/// Turns the analysed region into the rectangle to cut out of a frame.
///
/// One place, because two things cut it and they have to agree: a still saved by `FrameCapture` and
/// the video written by `SessionRecorder`. The recording is replayed as simulator input and is the
/// basis of test cases, so if the two rectangles ever drifted apart, the video would show a
/// different square than the stills taken from the same session - and nothing would say so.
///
/// Vision's region is normalised with a bottom-left origin, which is CoreImage's convention too, so
/// it maps onto an image's extent directly, with no flip.
enum FrameGeometry {

    /// The region as pixels of `extent`, rounded outwards to whole pixels.
    static func crop(_ region: CGRect, in extent: CGRect) -> CGRect {
        CGRect(x: extent.minX + region.minX * extent.width,
               y: extent.minY + region.minY * extent.height,
               width: region.width * extent.width,
               height: region.height * extent.height).integral
    }
}
