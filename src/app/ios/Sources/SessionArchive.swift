import Foundation

/// One ZIP per recording: `session_<time>_<discipline>.zip`, holding a folder of the same name with
/// the video, the recognition log and the session info. One file per session is what gets
/// copied off the phone, and Android writes the same layout, so the dataset tool opens either.
enum SessionArchive {

    /// Moves `files` into a folder called `name` beside them, zips that folder into `name.zip` and
    /// removes the folder. When packing fails the folder stays, with the files in it, and the error says
    /// why - the recording is not lost, it is only not packed.
    static func pack(_ files: [URL], name: String) throws -> URL {
        let manager = FileManager.default
        guard let first = files.first else { throw CocoaError(.fileNoSuchFile) }
        let base = first.deletingLastPathComponent()
        let folder = base.appendingPathComponent(name, isDirectory: true)
        let archive = base.appendingPathComponent(name).appendingPathExtension("zip")

        try manager.createDirectory(at: folder, withIntermediateDirectories: false)
        for file in files {
            try manager.moveItem(at: file, to: folder.appendingPathComponent(file.lastPathComponent))
        }

        // A folder read "for uploading" is handed over zipped - the documented way to a ZIP without a
        // library of our own. The zipped copy is temporary, so it is copied out inside the block.
        var coordination: NSError?
        var copying: Error?
        NSFileCoordinator().coordinate(readingItemAt: folder, options: .forUploading, error: &coordination) { zipped in
            do {
                try manager.copyItem(at: zipped, to: archive)
            } catch {
                copying = error
            }
        }
        if let problem = coordination ?? copying {
            try? manager.removeItem(at: archive)
            throw problem
        }
        try manager.removeItem(at: folder)
        return archive
    }
}
