import XCTest

final class PairingTests: XCTestCase {
    /// The Windows app's PhoneShareTests builds exactly this string. If one side changes the format, both tests must change.
    private let link = "ead://pair?v=1&name=Family%20frames&to=me%2Bframes%40gmail.com&tag=a1b2c3d4e5"

    func testParsesTheLinkTheWindowsAppBuilds() throws {
        let destination = try Pairing.parse(link)

        XCTAssertEqual(destination.name, "Family frames")
        XCTAssertEqual(destination.toAddress, "me+frames@gmail.com")
        XCTAssertEqual(destination.tag, "a1b2c3d4e5")
        XCTAssertEqual(destination.subjectMarker, "[ead:a1b2c3d4e5]")
    }

    func testIgnoresSurroundingWhitespace() throws {
        XCTAssertEqual(try Pairing.parse("  \n" + link + "\n ").tag, "a1b2c3d4e5")
    }

    func testNamesWithSymbolsAndAccentsSurvive() throws {
        let text = "ead://pair?v=1&name=Mum%20%26%20Dad%27s%20frame%20%231%20%28Zo%C3%AB%29&to=me%40gmail.com&tag=a1b2c3d4e5"

        XCTAssertEqual(try Pairing.parse(text).name, "Mum & Dad's frame #1 (Zoë)")
    }

    func testRejectsLinksThatAreNotPairingCodes() {
        let bad = [
            "",
            "hello",
            "https://example.com/pair?v=1&name=x&to=me%40gmail.com&tag=a1b2c3d4e5",
            "ead://other?v=1&name=x&to=me%40gmail.com&tag=a1b2c3d4e5",
            "ead://pair?name=x&to=me%40gmail.com&tag=a1b2c3d4e5",
            "ead://pair?v=0&name=x&to=me%40gmail.com&tag=a1b2c3d4e5",
        ]
        for text in bad {
            XCTAssertThrowsError(try Pairing.parse(text), text) { error in
                XCTAssertEqual(error as? PairingError, .notAPairingLink, text)
            }
        }
    }

    func testRejectsANewerVersion() {
        XCTAssertThrowsError(try Pairing.parse("ead://pair?v=2&name=x&to=me%40gmail.com&tag=a1b2c3d4e5")) { error in
            XCTAssertEqual(error as? PairingError, .unsupportedVersion)
        }
    }

    func testRejectsMissingOrInvalidFields() {
        XCTAssertThrowsError(try Pairing.parse("ead://pair?v=1&to=me%40gmail.com&tag=a1b2c3d4e5")) {
            XCTAssertEqual($0 as? PairingError, .missing("name"))
        }
        XCTAssertThrowsError(try Pairing.parse("ead://pair?v=1&name=x&tag=a1b2c3d4e5")) {
            XCTAssertEqual($0 as? PairingError, .missing("address"))
        }
        XCTAssertThrowsError(try Pairing.parse("ead://pair?v=1&name=x&to=me%40gmail.com")) {
            XCTAssertEqual($0 as? PairingError, .missing("tag"))
        }
        XCTAssertThrowsError(try Pairing.parse("ead://pair?v=1&name=x&to=nobody&tag=a1b2c3d4e5")) {
            XCTAssertEqual($0 as? PairingError, .invalid("address"))
        }
        XCTAssertThrowsError(try Pairing.parse("ead://pair?v=1&name=x&to=me%40gmail.com&tag=BAD")) {
            XCTAssertEqual($0 as? PairingError, .invalid("tag"))
        }
    }

    func testTagRule() {
        XCTAssertTrue(Pairing.isValidTag("a1b2c3d4e5"))
        XCTAssertTrue(Pairing.isValidTag("abcdef"))
        XCTAssertFalse(Pairing.isValidTag("abcde"))
        XCTAssertFalse(Pairing.isValidTag("UPPERCASE12"))
        XCTAssertFalse(Pairing.isValidTag("has space 12"))
        XCTAssertFalse(Pairing.isValidTag("[ead:abcdef12]"))
        XCTAssertFalse(Pairing.isValidTag(String(repeating: "a", count: 33)))
    }

    func testAddressesThatCouldBreakACommandAreRefused() {
        XCTAssertTrue(SmtpAddress.isSafe("me@gmail.com"))
        XCTAssertTrue(SmtpAddress.isSafe("me+frames@gmail.com"))
        XCTAssertFalse(SmtpAddress.isSafe("me@gmail.com\r\nRCPT TO:<evil@example.com>"))
        XCTAssertFalse(SmtpAddress.isSafe("a@b.com>, <c@d.com"))
        XCTAssertFalse(SmtpAddress.isSafe("two@@example.com"))
        XCTAssertFalse(SmtpAddress.isSafe("no-at-sign"))
        XCTAssertFalse(SmtpAddress.isSafe("space in@example.com"))
    }
}
