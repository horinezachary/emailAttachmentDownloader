import XCTest

final class MimeTests: XCTestCase {
    private var folder: URL!

    override func setUpWithError() throws {
        folder = FileManager.default.temporaryDirectory.appendingPathComponent("ead-mime-" + UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    override func tearDownWithError() throws {
        try? FileManager.default.removeItem(at: folder)
    }

    private func attachment(bytes: Int, name: String = "photo.jpg") throws -> (MimeAttachment, Data) {
        let data = Data((0..<bytes).map { UInt8(truncatingIfNeeded: $0 &* 31 &+ 7) })
        let url = folder.appendingPathComponent("source-" + name)
        try data.write(to: url)
        return (MimeAttachment(url: url, fileName: name, mimeType: "image/jpeg"), data)
    }

    private func build(_ attachments: [MimeAttachment], subject: String = "[ead:a1b2c3d4e5] Photos from iPhone") throws -> String {
        let message = try MimeBuilder.build(
            from: "me@gmail.com",
            to: "me+frames@gmail.com",
            subject: subject,
            bodyText: "Sent from the app.",
            attachments: attachments,
            date: Date(timeIntervalSince1970: 1_800_000_000),
            boundary: "TESTBOUNDARY",
            into: folder)
        return String(decoding: try Data(contentsOf: message.url), as: UTF8.self)
    }

    func testHeadersAndStructure() throws {
        let (photo, _) = try attachment(bytes: 100)
        let text = try build([photo])

        XCTAssertTrue(text.hasPrefix("From: me@gmail.com\r\nTo: me+frames@gmail.com\r\n"))
        XCTAssertTrue(text.contains("Subject: [ead:a1b2c3d4e5] Photos from iPhone\r\n"))
        XCTAssertTrue(text.contains("MIME-Version: 1.0\r\n"))
        XCTAssertTrue(text.contains("Content-Type: multipart/mixed; boundary=\"TESTBOUNDARY\"\r\n"))
        XCTAssertTrue(text.contains("Content-Type: image/jpeg; name=\"photo.jpg\"\r\n"))
        XCTAssertTrue(text.contains("Content-Disposition: attachment; filename=\"photo.jpg\"\r\n"))
        XCTAssertTrue(text.hasSuffix("--TESTBOUNDARY--\r\n"))
    }

    func testEveryLineEndsInCRLFAndIsShortEnough() throws {
        let (photo, _) = try attachment(bytes: 200_000)
        let text = try build([photo])

        XCTAssertFalse(text.replacingOccurrences(of: "\r\n", with: "").contains("\n"), "a bare LF")
        XCTAssertFalse(text.replacingOccurrences(of: "\r\n", with: "").contains("\r"), "a bare CR")
        for line in text.components(separatedBy: "\r\n") {
            XCTAssertLessThanOrEqual(line.utf8.count, 998)
        }
    }

    func testBase64LinesAre76CharactersAndDecodeToTheOriginal() throws {
        // Larger than one chunk, so the line breaks across chunk boundaries are exercised too.
        let (photo, original) = try attachment(bytes: MimeBuilder.chunkSize * 2 + 1234)
        let text = try build([photo])

        let start = try XCTUnwrap(text.range(of: "Content-Disposition: attachment; filename=\"photo.jpg\"\r\n\r\n"))
        let end = try XCTUnwrap(text.range(of: "\r\n--TESTBOUNDARY--"))
        let body = String(text[start.upperBound..<end.lowerBound])
        let lines = body.components(separatedBy: "\r\n")

        XCTAssertTrue(lines.dropLast().allSatisfy { $0.count == 76 }, "all but the last base64 line are 76 characters")
        XCTAssertLessThanOrEqual(lines.last?.count ?? 0, 76)
        XCTAssertEqual(Data(base64Encoded: lines.joined()), original)
    }

    func testSeveralAttachmentsAreAllPresentInOrder() throws {
        let (first, firstData) = try attachment(bytes: 500, name: "one.jpg")
        let (second, secondData) = try attachment(bytes: 700, name: "two.jpg")
        let text = try build([first, second])

        let firstRange = try XCTUnwrap(text.range(of: "filename=\"one.jpg\""))
        let secondRange = try XCTUnwrap(text.range(of: "filename=\"two.jpg\""))
        XCTAssertLessThan(firstRange.lowerBound, secondRange.lowerBound)
        XCTAssertTrue(text.contains(firstData.base64EncodedString(options: [.lineLength76Characters, .endLineWithCarriageReturn, .endLineWithLineFeed])))
        XCTAssertTrue(text.contains(secondData.base64EncodedString(options: [.lineLength76Characters, .endLineWithCarriageReturn, .endLineWithLineFeed])))
    }

    func testHostileFileNamesAreMadeSafe() throws {
        let (photo, _) = try attachment(bytes: 50, name: "..\\..\\evil\"; x=\".jpg")
        let text = try build([photo])

        XCTAssertFalse(text.contains("evil\";"))
        XCTAssertFalse(text.contains("..\\"))
    }

    func testHeaderInjectionIsRefused() throws {
        let (photo, _) = try attachment(bytes: 10)

        XCTAssertThrowsError(try build([photo], subject: "Photos\r\nBcc: someone@example.com"))
        XCTAssertThrowsError(try MimeBuilder.build(
            from: "me@gmail.com\r\nBcc: x@example.com", to: "a@b.com", subject: "s", bodyText: "b", attachments: [], into: folder))
        XCTAssertThrowsError(try build([photo], subject: "Caf\u{00E9}"))
    }

    func testTheMessageIsSmallerThanThePhotosPlusAThird() throws {
        let (photo, _) = try attachment(bytes: 300_000)
        let message = try MimeBuilder.build(
            from: "me@gmail.com", to: "a@b.com", subject: "s", bodyText: "b", attachments: [photo], into: folder)

        XCTAssertGreaterThan(message.byteCount, 400_000)
        XCTAssertLessThan(message.byteCount, 420_000)
    }

    // MARK: splitting into messages

    func testPhotosAreGroupedBySize() {
        let sizes = [6_000_000, 6_000_000, 6_000_000, 1_000_000]

        let groups = MessagePlanner.group(sizes, size: { $0 }, maxBytes: 15_000_000)

        XCTAssertEqual(groups, [[6_000_000, 6_000_000], [6_000_000, 1_000_000]])
    }

    func testAPhotoBiggerThanTheLimitStillGetsItsOwnGroup() {
        let groups = MessagePlanner.group([20_000_000, 1], size: { $0 }, maxBytes: 15_000_000)

        XCTAssertEqual(groups, [[20_000_000], [1]])
    }

    func testNothingGivesNoGroups() {
        XCTAssertTrue(MessagePlanner.group([Int](), size: { $0 }).isEmpty)
    }
}
