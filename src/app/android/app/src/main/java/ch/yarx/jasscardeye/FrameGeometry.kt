package ch.yarx.jasscardeye

import android.graphics.Rect

// Port of src/app/ios/Sources/FrameGeometry.swift and of LiveDetectionModel.region(forAspect:).

/**
 * The analysed square, in one place.
 *
 * Three things use it and they have to agree: the model input, a still saved by `FrameCapture` and
 * the video written by `SessionRecorder`. On the camera path CameraX's 1:1 viewport cuts the square (see
 * `CameraService`); this object cuts the same square out of the emulator's test video. The preview draws
 * it too (see `ScanScreen`), so picture, analysis and overlay share one coordinate system - the property
 * the iOS viewfinder is built on.
 */
object FrameGeometry {

    /**
     * The largest centred square of a `width` × `height` frame, in pixels. In portrait it spans the
     * full width; once the frame is square or wider the height becomes the limit instead - a region
     * overshooting the frame would make every frame look empty.
     */
    fun square(width: Int, height: Int): Rect {
        val side = minOf(width, height)
        val left = (width - side) / 2
        val top = (height - side) / 2
        return Rect(left, top, left + side, top + side)
    }
}
