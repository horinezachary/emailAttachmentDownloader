import XCTest

final class SmtpTests: XCTestCase {
    // MARK: reply parsing

    func testParsesAContinuationLine() throws {
        let parsed = try XCTUnwrap(SmtpReplyParser.parse(line: "250-SIZE 35882577\r\n"))

        XCTAssertEqual(parsed.code, 250)
        XCTAssertFalse(parsed.isLast)
        XCTAssertEqual(parsed.text, "SIZE 35882577")
    }

    func testParsesTheLastLine() throws {
        let parsed = try XCTUnwrap(SmtpReplyParser.parse(line: "250 OK\r\n"))

        XCTAssertEqual(parsed.code, 250)
        XCTAssertTrue(parsed.isLast)
        XCTAssertEqual(parsed.text, "OK")
    }

    func testParsesABareCode() throws {
        let parsed = try XCTUnwrap(SmtpReplyParser.parse(line: "354\r\n"))

        XCTAssertEqual(parsed.code, 354)
        XCTAssertTrue(parsed.isLast)
    }

    func testRejectsLinesThatAreNotReplies() {
        XCTAssertNil(SmtpReplyParser.parse(line: "hello\r\n"))
        XCTAssertNil(SmtpReplyParser.parse(line: "25\r\n"))
        XCTAssertNil(SmtpReplyParser.parse(line: "250OK\r\n"))
        XCTAssertNil(SmtpReplyParser.parse(line: "\r\n"))
    }

    // MARK: how refusals are explained

    func testAuthenticationRefusalsAskForAnAppPassword() {
        for code in [530, 534, 535, 538] {
            let error = SmtpSession.error(for: SmtpReply(code: code, lines: ["denied"]))

            XCTAssertEqual(error.kind, .authentication, "\(code)")
            XCTAssertFalse(error.isRetryable)
            XCTAssertTrue(error.message.contains("app password"))
        }
    }

    func testFourHundredsAreTemporaryAndFiveHundredsArePermanent() {
        XCTAssertEqual(SmtpSession.error(for: SmtpReply(code: 421, lines: ["busy"])).kind, .temporary)
        XCTAssertTrue(SmtpSession.error(for: SmtpReply(code: 451, lines: ["later"])).isRetryable)
        XCTAssertEqual(SmtpSession.error(for: SmtpReply(code: 550, lines: ["no"])).kind, .permanent)
        XCTAssertFalse(SmtpSession.error(for: SmtpReply(code: 550, lines: ["no"])).isRetryable)
        XCTAssertEqual(SmtpSession.error(for: SmtpReply(code: 552, lines: ["big"])).kind, .permanent)
        XCTAssertTrue(SmtpSession.error(for: SmtpReply(code: 552, lines: ["big"])).message.contains("too big"))
    }

    // MARK: dot stuffing

    func testDotsAtTheStartOfALineAreDoubled() {
        var stuffer = DotStuffer()
        let output = stuffer.process(Data("a\r\n.b\r\n..c\r\nd.e\r\n".utf8))

        XCTAssertEqual(String(decoding: output, as: UTF8.self), "a\r\n..b\r\n...c\r\nd.e\r\n")
    }

    func testADotAtTheStartOfTheMessageIsDoubled() {
        var stuffer = DotStuffer()

        XCTAssertEqual(String(decoding: stuffer.process(Data(".x".utf8)), as: UTF8.self), "..x")
    }

    func testStuffingWorksWhenALineIsSplitAcrossChunks() {
        var stuffer = DotStuffer()
        var output = Data()
        output.append(stuffer.process(Data("one\r".utf8)))
        output.append(stuffer.process(Data("\n.two".utf8)))
        output.append(stuffer.process(Data("\r\n".utf8)))
        output.append(stuffer.process(Data(".".utf8)))

        XCTAssertEqual(String(decoding: output, as: UTF8.self), "one\r\n..two\r\n..")
    }
}
