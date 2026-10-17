import ImageIO
import UIKit
import XCTest

final class ImagePreparerTests: XCTestCase {
    private var folder: URL!

    override func setUpWithError() throws {
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("ead-img-" + UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: folder)
    }

    private func makeImage(width: Int, height: Int, png: Bool = false, name: String) throws -> URL {
        let format = UIGraphicsImageRendererFormat()
        format.scale = 1
        let renderer = UIGraphicsImageRenderer(size: CGSize(width: width, height: height), format: format)
        let data: Data
        if png {
            data = renderer.pngData { context in
                UIColor.systemTeal.setFill()
                context.fill(CGRect(x: 0, y: 0, width: width, height: height))
            }
        } else {
            data = renderer.jpegData(withCompressionQuality: 0.9) { context in
                UIColor.systemOrange.setFill()
                context.fill(CGRect(x: 0, y: 0, width: width, height: height))
            }
        }
        let url = folder.appendingPathComponent(name)
        try data.write(to: url)
        return url
    }

    private func pixelSize(of url: URL) throws -> (Int, Int) {
        let source = try XCTUnwrap(CGImageSourceCreateWithURL(url as CFURL, nil))
        let properties = try XCTUnwrap(CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any])
        let width = try XCTUnwrap((properties[kCGImagePropertyPixelWidth] as? NSNumber)?.intValue)
        let height = try XCTUnwrap((properties[kCGImagePropertyPixelHeight] as? NSNumber)?.intValue)
        return (width, height)
    }

    func testASmallJpegIsSentUnchanged() throws {
        let source = try makeImage(width: 400, height: 200, name: "small.jpg")
        let work = folder.appendingPathComponent("work")

        let prepared = try ImagePreparer.prepare(source: source, displayName: "small.jpg", options: ImageOptions(), into: work)

        XCTAssertEqual(prepared.fileName, "small.jpg")
        XCTAssertEqual(prepared.mimeType, "image/jpeg")
        XCTAssertEqual(try Data(contentsOf: prepared.url), try Data(contentsOf: source))
    }

    func testABigJpegIsScaledDownKeepingItsShape() throws {
        let source = try makeImage(width: 2000, height: 1000, name: "big.jpg")
        var options = ImageOptions()
        options.maxDimension = 500

        let prepared = try ImagePreparer.prepare(
            source: source, displayName: "big.jpg", options: options, into: folder.appendingPathComponent("work"))

        let (width, height) = try pixelSize(of: prepared.url)
        XCTAssertEqual(max(width, height), 500)
        XCTAssertEqual(Double(width) / Double(height), 2.0, accuracy: 0.05)
        XCTAssertEqual(prepared.mimeType, "image/jpeg")
        XCTAssertGreaterThan(prepared.byteCount, 0)
    }

    func testZeroMeansNeverShrink() throws {
        let source = try makeImage(width: 2000, height: 1000, name: "big.jpg")
        var options = ImageOptions()
        options.maxDimension = 0

        let prepared = try ImagePreparer.prepare(
            source: source, displayName: "big.jpg", options: options, into: folder.appendingPathComponent("work"))

        XCTAssertEqual(try Data(contentsOf: prepared.url), try Data(contentsOf: source))
    }

    func testAPngIsLeftAlone() throws {
        let source = try makeImage(width: 3000, height: 1500, png: true, name: "shot.png")
        var options = ImageOptions()
        options.maxDimension = 500

        let prepared = try ImagePreparer.prepare(
            source: source, displayName: "shot.png", options: options, into: folder.appendingPathComponent("work"))

        XCTAssertEqual(prepared.fileName, "shot.png")
        XCTAssertEqual(prepared.mimeType, "image/png")
        XCTAssertEqual(try Data(contentsOf: prepared.url), try Data(contentsOf: source))
    }

    func testAFileThatIsNotAPictureIsRefused() throws {
        let source = folder.appendingPathComponent("notes.jpg")
        try Data("this is text".utf8).write(to: source)

        XCTAssertThrowsError(try ImagePreparer.prepare(
            source: source, displayName: "notes.jpg", options: ImageOptions(), into: folder.appendingPathComponent("work")))
    }

    func testTwoPhotosWithTheSameNameDoNotOverwriteEachOther() throws {
        let source = try makeImage(width: 100, height: 100, name: "a.jpg")
        let work = folder.appendingPathComponent("work")

        let first = try ImagePreparer.prepare(source: source, displayName: "IMG_0001.jpg", options: ImageOptions(), into: work)
        let second = try ImagePreparer.prepare(source: source, displayName: "IMG_0001.jpg", options: ImageOptions(), into: work)

        XCTAssertNotEqual(first.url, second.url)
        XCTAssertEqual(second.fileName, "IMG_0001-2.jpg")
    }

    func testMimeTypesByExtension() {
        XCTAssertEqual(ImagePreparer.mimeType(forExtension: "jpg"), "image/jpeg")
        XCTAssertEqual(ImagePreparer.mimeType(forExtension: "PNG"), "image/png")
        XCTAssertEqual(ImagePreparer.mimeType(forExtension: "zzzunknown"), "application/octet-stream")
    }
}

final class StorageTests: XCTestCase {
    private var folder: URL!

    override func setUpWithError() throws {
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("ead-store-" + UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: folder)
    }

    private let destination = Destination(name: "Frames", toAddress: "me+frames@gmail.com", tag: "a1b2c3d4e5")

    private func stage(_ names: [String]) throws -> [StagedFile] {
        let staging = folder.appendingPathComponent("staging-" + UUID().uuidString)
        try FileManager.default.createDirectory(at: staging, withIntermediateDirectories: true)
        return try names.map { name in
            let url = staging.appendingPathComponent(name)
            try Data(name.utf8).write(to: url)
            return StagedFile(url: url, name: name)
        }
    }

    // MARK: config

    func testSettingsRoundTrip() throws {
        let store = ConfigStore(directory: folder)
        var config = StoredConfig()
        config.destinations = [destination]
        config.smtp.username = "me@gmail.com"
        config.options.maxDimension = 1920
        config.lastDestinationID = destination.id

        try store.save(config)

        XCTAssertEqual(ConfigStore(directory: folder).load(), config)
    }

    func testAMissingOrDamagedFileGivesDefaults() throws {
        XCTAssertEqual(ConfigStore(directory: folder).load(), StoredConfig())

        try Data("{ not json".utf8).write(to: folder.appendingPathComponent("config.json"))

        XCTAssertEqual(ConfigStore(directory: folder).load(), StoredConfig())
    }

    func testAnOlderFileWithFewerKeysStillLoads() throws {
        try Data(#"{"destinations":[]}"#.utf8).write(to: folder.appendingPathComponent("config.json"))

        let config = ConfigStore(directory: folder).load()

        XCTAssertEqual(config.smtp.host, "smtp.gmail.com")
        XCTAssertEqual(config.options.maxDimension, 3840)
    }

    func testUpdateKeepsOtherChanges() throws {
        let store = ConfigStore(directory: folder)
        try store.update { $0.destinations = [destination] }
        try store.update { $0.smtp.username = "me@gmail.com" }

        let config = store.load()

        XCTAssertEqual(config.destinations, [destination])
        XCTAssertEqual(config.smtp.username, "me@gmail.com")
    }

    func testPreferredDestinationIsTheLastUsedThenTheFirst() {
        let other = Destination(name: "Other", toAddress: "me@gmail.com", tag: "f6e5d4c3b2")
        var config = StoredConfig()
        config.destinations = [destination, other]
        XCTAssertEqual(config.preferredDestination, destination)

        config.lastDestinationID = other.id
        XCTAssertEqual(config.preferredDestination, other)

        config.lastDestinationID = UUID()
        XCTAssertEqual(config.preferredDestination, destination)
    }

    func testSmtpSettingsNeedServerUserAndAValidFromAddress() {
        var smtp = SmtpSettings()
        XCTAssertFalse(smtp.isConfigured)

        smtp.username = "me@gmail.com"
        XCTAssertTrue(smtp.isConfigured)
        XCTAssertEqual(smtp.effectiveFrom, "me@gmail.com")

        smtp.username = "plainuser"
        XCTAssertFalse(smtp.isConfigured, "a user name that is not an address needs a From address")
        smtp.fromAddress = "me@example.com"
        XCTAssertTrue(smtp.isConfigured)
        XCTAssertEqual(smtp.effectiveFrom, "me@example.com")

        smtp.port = 0
        XCTAssertFalse(smtp.isConfigured)
    }

    // MARK: outbox

    func testCreatingABatchMovesTheFilesIn() throws {
        let store = BatchStore(root: folder.appendingPathComponent("Outbox"))
        let files = try stage(["a.jpg", "b.jpg"])

        let batch = try store.create(destination: destination, files: files, move: true)

        XCTAssertEqual(batch.items.map(\.fileName), ["a.jpg", "b.jpg"])
        XCTAssertEqual(batch.unsentCount, 2)
        for file in files {
            XCTAssertFalse(FileManager.default.fileExists(atPath: file.url.path), "moved, not copied")
        }
        for item in batch.items {
            XCTAssertTrue(FileManager.default.fileExists(atPath: store.fileURL(batch: batch, item: item).path))
        }
        // The record stores dates to the second, so compare everything but the timestamp.
        let loaded = try XCTUnwrap(store.load(batch.id))
        XCTAssertEqual(loaded.id, batch.id)
        XCTAssertEqual(loaded.destination, destination)
        XCTAssertEqual(loaded.items, batch.items)
        XCTAssertEqual(loaded.createdAt.timeIntervalSince1970, batch.createdAt.timeIntervalSince1970, accuracy: 1.0)
    }

    func testCreatingABatchCanCopyInstead() throws {
        let store = BatchStore(root: folder.appendingPathComponent("Outbox"))
        let files = try stage(["a.jpg"])

        _ = try store.create(destination: destination, files: files, move: false)

        XCTAssertTrue(FileManager.default.fileExists(atPath: files[0].url.path))
    }

    func testStoredNamesAreSafeEvenForHostileNames() throws {
        let store = BatchStore(root: folder.appendingPathComponent("Outbox"))
        let files = try stage(["ok.jpg"])
        let hostile = [StagedFile(url: files[0].url, name: "../../../etc/passwd")]

        let batch = try store.create(destination: destination, files: hostile, move: true)

        XCTAssertFalse(batch.items[0].storedName.contains("/"))
        XCTAssertTrue(store.fileURL(batch: batch, item: batch.items[0]).path.hasPrefix(store.root.path))
    }

    func testABatchWithNoUsableFilesIsRefused() {
        let store = BatchStore(root: folder.appendingPathComponent("Outbox"))
        let missing = [StagedFile(url: folder.appendingPathComponent("nope.jpg"), name: "nope.jpg")]

        XCTAssertThrowsError(try store.create(destination: destination, files: missing, move: true))
        XCTAssertTrue(store.all().isEmpty)
    }

    func testBatchesAreListedNewestFirstAndCanBeDeleted() throws {
        let store = BatchStore(root: folder.appendingPathComponent("Outbox"))
        var older = try store.create(destination: destination, files: try stage(["1.jpg"]), move: true)
        older.createdAt = Date(timeIntervalSinceNow: -3600)
        try store.save(older)
        let newer = try store.create(destination: destination, files: try stage(["2.jpg"]), move: true)

        XCTAssertEqual(store.all().map(\.id), [newer.id, older.id])

        store.delete(newer.id)
        XCTAssertEqual(store.all().map(\.id), [older.id])
    }

    func testOnlyOneProcessCanSendABatchAtATime() throws {
        let store = BatchStore(root: folder.appendingPathComponent("Outbox"))
        let batch = try store.create(destination: destination, files: try stage(["a.jpg"]), move: true)

        XCTAssertTrue(store.claim(batch.id))
        XCTAssertFalse(store.claim(batch.id))

        store.release(batch.id)
        XCTAssertTrue(store.claim(batch.id))
    }

    func testAStaleLockLeftByAKilledProcessIsTakenOver() throws {
        let store = BatchStore(root: folder.appendingPathComponent("Outbox"))
        let batch = try store.create(destination: destination, files: try stage(["a.jpg"]), move: true)
        XCTAssertTrue(store.claim(batch.id))

        let lock = store.folder(for: batch.id).appendingPathComponent("sending.lock")
        let old = Date(timeIntervalSinceNow: -(BatchStore.staleLockSeconds + 60))
        try FileManager.default.setAttributes([.modificationDate: old], ofItemAtPath: lock.path)

        XCTAssertTrue(store.claim(batch.id))
    }

    func testFinishedBatchesAreForgottenAfterAWeek() throws {
        let store = BatchStore(root: folder.appendingPathComponent("Outbox"))
        var done = try store.create(destination: destination, files: try stage(["a.jpg"]), move: true)
        done.items[0].state = .sent
        done.createdAt = Date(timeIntervalSinceNow: -8 * 24 * 3600)
        try store.save(done)
        var oldButWaiting = try store.create(destination: destination, files: try stage(["b.jpg"]), move: true)
        oldButWaiting.createdAt = Date(timeIntervalSinceNow: -8 * 24 * 3600)
        try store.save(oldButWaiting)

        store.pruneSent()

        XCTAssertEqual(store.all().map(\.id), [oldButWaiting.id], "unsent photos are never pruned")
    }

    // MARK: file names

    func testFileNamesAreMadeSafeForWindows() {
        XCTAssertEqual(FileNameSanitizer.safe("IMG_0001.HEIC"), "IMG_0001.HEIC")
        XCTAssertEqual(FileNameSanitizer.safe("a/b/c.jpg"), "c.jpg")
        XCTAssertEqual(FileNameSanitizer.safe("bad:name?.jpg"), "bad_name_.jpg")
        XCTAssertEqual(FileNameSanitizer.safe("..."), "photo")
        XCTAssertEqual(FileNameSanitizer.safe(""), "photo")
        XCTAssertEqual(FileNameSanitizer.safe("Caf\u{00E9}.jpg"), "Caf_.jpg")
        XCTAssertEqual(FileNameSanitizer.safe("???"), "photo")
    }

    func testLongNamesKeepTheirExtension() {
        let name = FileNameSanitizer.safe(String(repeating: "x", count: 300) + ".jpg")

        XCTAssertLessThanOrEqual(name.count, 100)
        XCTAssertTrue(name.hasSuffix(".jpg"))
    }
}
