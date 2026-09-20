// Holds SessionRecorder to account: every ending has to be reported.
//
// The recorder is the one part of the app that fails where nobody is watching. It runs on the
// camera queue and writes to a file nobody opens until the evening is over, so a recording that
// breaks in second 20 must not look like one that was never started. That is what this checks for,
// because it is the failure that costs a tester their whole session and says nothing.
//
//   swiftc -o /tmp/check_recorder src/app/ios/Sources/SessionRecorder.swift src/app/ios/Sources/SessionArchive.swift \
//       src/app/ios/Sources/FrameGeometry.swift src/tools/check_recorder.swift && /tmp/check_recorder
//
// Runs on macOS against the real AVFoundation, in about a second. Unlike check_scoring.swift this
// one needs a platform with an H.264 encoder, so it does not belong on a Linux runner.
//
// It also holds the recognition log to the video: row n has to describe frame n, which is checked on
// frames that show their own number as four black and white stripes. With CHECK_RECORDER_KEEP=1 the
// recording stays behind, for trying the Dataset Tool's Analyse sessions on a video whose frames can
// be told apart at a glance.

import Foundation
import CoreVideo
import AVFoundation

@main
enum RecorderCheck {

    static var failures = 0

    static func check(_ ok: Bool, _ what: @autoclosure () -> String) {
        print((ok ? "ok   " : "FAIL ") + what())
        if !ok { failures += 1 }
    }

    static func makeBuffer(_ side: Int) -> CVPixelBuffer {
        var buffer: CVPixelBuffer?
        CVPixelBufferCreate(nil, side, side, kCVPixelFormatType_32BGRA,
                            [kCVPixelBufferIOSurfacePropertiesKey: [:]] as CFDictionary, &buffer)
        return buffer!
    }

    static func exists(_ url: URL) -> Bool { FileManager.default.fileExists(atPath: url.path) }

    static func main() async {
        let buffer = makeBuffer(256)
        let wholeFrame = CGRect(x: 0, y: 0, width: 1, height: 1)
        let dir = URL(fileURLWithPath: NSTemporaryDirectory())
            .appendingPathComponent("check_recorder-\(UUID().uuidString)")
        try! FileManager.default.createDirectory(at: dir, withIntermediateDirectories: true)

        // 1. A recording that worked names its file and counts its frames. The pauses matter:
        //    frames sharing a presentation timestamp are a legitimate reason to refuse one.
        let savedURL = dir.appendingPathComponent("ok.mov")
        let saved = SessionRecorder(url: savedURL, info: Data("{\"platform\" : \"check\"}".utf8))
        for index in 0..<10 {
            CVPixelBufferLockBaseAddress(buffer, [])
            let pixels = CVPixelBufferGetBaseAddress(buffer)!.assumingMemoryBound(to: UInt8.self)
            for y in 0..<256 { for x in 0..<256 {
                let offset = y * CVPixelBufferGetBytesPerRow(buffer) + x * 4
                // Four broad black/white stripes encode the frame index. Unlike a
                // colour ramp this survives codec colour-space and range conversion.
                let value: UInt8 = (index & (1 << (x / 64))) == 0 ? 0 : 255
                pixels[offset] = value; pixels[offset+1] = value; pixels[offset+2] = value; pixels[offset+3] = 255
            } }
            CVPixelBufferUnlockBaseAddress(buffer, [])
            saved.append(buffer, region: wholeFrame, result: .init(
                label: index % 2 == 0 ? "spades_6" : nil, confidence: 0.85,
                box: CGRect(x: 0.1, y: 0.2, width: 0.3, height: 0.4), committed: index == 4))
            usleep(20_000)
        }
        switch await saved.finish() {
        case .saved(let name, let frames, let problem):
            check(name == "ok.zip" && problem == nil, "a finished recording is packed into one ZIP, got \(name) \(problem ?? "")")
            check(frames == 10, "it counts the frames it wrote, got \(frames)")
            check(exists(dir.appendingPathComponent("ok.zip")) && !exists(savedURL)
                  && !exists(savedURL.deletingPathExtension().appendingPathExtension("csv"))
                  && !exists(dir.appendingPathComponent("ok")), "and only the ZIP is left on disk")
        case let other:
            check(false, "a finished recording reports .saved, got \(other)")
        }

        // Unpacked as the dataset tool does it: the session's folder, beside the ZIP.
        let unzip = Process()
        unzip.executableURL = URL(fileURLWithPath: "/usr/bin/unzip")
        unzip.arguments = ["-q", dir.appendingPathComponent("ok.zip").path, "-d", dir.path]
        try? unzip.run()
        unzip.waitUntilExit()
        let video = dir.appendingPathComponent("ok").appendingPathComponent("ok.mov")
        check((try? String(contentsOf: video.deletingPathExtension().appendingPathExtension("json"), encoding: .utf8))
              == "{\"platform\" : \"check\"}", "the ZIP holds the video, the log and the session info in a folder of the session's name")

        // Compare accepted CSV rows with actually decoded video samples, not append attempts.
        do {
            let csvURL = video.deletingPathExtension().appendingPathExtension("csv")
            let lines = try String(contentsOf: csvURL, encoding: .utf8).split(separator: "\n")
            check(lines.first == "frame,t_ms,label,confidence,x,y,w,h,committed", "CSV has the shared column order")
            let rows = lines.dropFirst().map { $0.split(separator: ",", omittingEmptySubsequences: false).map(String.init) }
            let asset = AVURLAsset(url: video)
            let track = try await asset.loadTracks(withMediaType: .video).first!
            let reader = try AVAssetReader(asset: asset)
            let output = AVAssetReaderTrackOutput(track: track, outputSettings: [kCVPixelBufferPixelFormatTypeKey as String: kCVPixelFormatType_32BGRA])
            reader.add(output); check(reader.startReading(), "recorded video can be decoded")
            var count = 0
            while let sample = output.copyNextSampleBuffer() {
                guard count < rows.count else { check(false, "every decoded video frame has a CSV row"); break }
                let row = rows[count]
                check(row.count == 9 && Int(row[0]) == count, "CSV index \(count) matches decoded frame")
                let milliseconds = CMSampleBufferGetPresentationTimeStamp(sample).seconds * 1000
                check(abs(Double(row[1])! - milliseconds) < 0.002, "CSV time matches encoded PTS at \(count): csv=\(row[1]), video=\(milliseconds)")
                if [0, 4, 9].contains(count), let image = CMSampleBufferGetImageBuffer(sample) {
                    CVPixelBufferLockBaseAddress(image, .readOnly)
                    let pixel = CVPixelBufferGetBaseAddress(image)!.assumingMemoryBound(to: UInt8.self)
                    var marker = 0
                    for bit in 0..<4 {
                        if pixel[128 * CVPixelBufferGetBytesPerRow(image) + (32 + bit*64) * 4] > 128 { marker |= 1 << bit }
                    }
                    CVPixelBufferUnlockBaseAddress(image, .readOnly)
                    check(marker == count, "visual marker matches row at sample \(count): decoded=\(marker)")
                }
                if count == 4 {
                    check(row[2] == "spades_6" && row[8] == "1", "commit belongs to the correct video frame")
                    check(row[5] == "0.4000", "Vision Y is converted to top-left square coordinates, got \(row[5])")
                }
                if count == 9 { check(row[2...7].allSatisfy { $0.isEmpty }, "empty detection still produces a full CSV row") }
                count += 1
            }
            check(reader.status == .completed && rows.count == count && count == 10, "CSV row count equals actual video frame count")
        } catch { check(false, "video/CSV validation failed: \(error)") }

        // 2. Started and never fed - no file may be left behind.
        let emptyURL = dir.appendingPathComponent("empty.mov")
        if case .nothingRecorded = await SessionRecorder(url: emptyURL, info: Data("{}".utf8)).finish() {
            check(!exists(emptyURL) && !exists(emptyURL.deletingPathExtension().appendingPathExtension("csv"))
                  && !exists(emptyURL.deletingPathExtension().appendingPathExtension("json")),
                  "a recording without frames leaves no file behind")
        } else {
            check(false, "a recording without frames reports .nothingRecorded")
        }

        // 3. The writer cannot even be created. This is the case that must not be silent: it has to
        //    be told apart from case 2, and the start must not be attempted again on every frame.
        let deadURL = URL(fileURLWithPath: "/nonexistent-\(UUID().uuidString)/session.mov")
        let broken = SessionRecorder(url: deadURL)
        let started = CFAbsoluteTimeGetCurrent()
        for _ in 0..<200 { broken.append(buffer, region: wholeFrame) }
        let perFrame = (CFAbsoluteTimeGetCurrent() - started) * 1000 / 200
        if case .failed(let reason) = await broken.finish() {
            check(!reason.isEmpty, "a broken recording says why: \(reason)")
            check(!exists(deadURL), "and leaves nothing behind")
        } else {
            check(false, "a recording that cannot start reports .failed")
        }
        // The failure is latched. Retrying the start on every frame would cost milliseconds each
        // time, on the camera queue for the rest of the session, only to fail again.
        check(perFrame < 0.1,
              String(format: "a failed recording then costs nothing per frame, %.4f ms", perFrame))

        // 4. Closing twice is what a session that ends in two ways does.
        let twice = SessionRecorder(url: dir.appendingPathComponent("twice.mov"))
        for _ in 0..<3 {
            twice.append(buffer, region: wholeFrame)
            usleep(20_000)
        }
        _ = await twice.finish()
        if case .nothingRecorded = await twice.finish() {
            check(true, "closing a second time is harmless")
        } else {
            check(false, "closing a second time is harmless")
        }

        if ProcessInfo.processInfo.environment["CHECK_RECORDER_KEEP"] == "1" {
            print("Recording artifacts: \(dir.path)")
        } else {
            try? FileManager.default.removeItem(at: dir)
        }

        guard failures == 0 else {
            print("\n\(failures) check(s) failed")
            exit(1)
        }
        print("\nOK: the recorder reports every ending")
    }
}
