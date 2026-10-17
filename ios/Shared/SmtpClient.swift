import Foundation

/// What went wrong while talking to the mail server, in terms the sender can act on.
struct SmtpError: LocalizedError, Equatable {
    enum Kind: Equatable {
        /// The connection failed, dropped or timed out. Worth trying again later.
        case connection
        /// The server refused the user name or password. Only the user can fix it.
        case authentication
        /// A 4xx reply: the server asked us to try again later.
        case temporary
        /// A 5xx reply: the server will not take this message (too big, address refused, ...).
        case permanent
    }

    var kind: Kind
    var message: String

    var errorDescription: String? { message }

    /// Trying again later can help.
    var isRetryable: Bool { kind == .connection || kind == .temporary }
}

struct SmtpReply: Equatable {
    var code: Int
    var lines: [String]

    var text: String { lines.joined(separator: " ") }
}

/// One line of a reply: `250-SIZE 35882577` continues, `250 OK` ends it.
enum SmtpReplyParser {
    static func parse(line: String) -> (code: Int, isLast: Bool, text: String)? {
        let trimmed = line.trimmingCharacters(in: .newlines)
        guard trimmed.count >= 3, let code = Int(trimmed.prefix(3)) else { return nil }
        if trimmed.count == 3 { return (code, true, "") }

        let separator = trimmed[trimmed.index(trimmed.startIndex, offsetBy: 3)]
        guard separator == " " || separator == "-" else { return nil }
        return (code, separator == " ", String(trimmed.dropFirst(4)))
    }
}

/// Doubles a "." at the start of a line, as SMTP requires inside DATA, even when a line is split across two chunks.
struct DotStuffer {
    private var atLineStart = true

    mutating func process(_ chunk: Data) -> Data {
        var output = Data()
        output.reserveCapacity(chunk.count + 16)
        for byte in chunk {
            if atLineStart && byte == 0x2E {
                output.append(0x2E)
            }
            output.append(byte)
            atLineStart = (byte == 0x0A)
        }
        return output
    }
}

/// A blocking TCP connection to a mail server, optionally in TLS from the first byte or upgraded later (STARTTLS).
/// Reads and writes give up after `timeout` seconds. Use it from a background thread only.
final class SmtpConnection {
    private var input: InputStream?
    private var output: OutputStream?
    private var buffer = Data()
    private let timeout: TimeInterval
    private var timedOut = false

    init(timeout: TimeInterval = 30) {
        self.timeout = timeout
    }

    func open(host: String, port: Int, tls: Bool) throws {
        var readRef: Unmanaged<CFReadStream>?
        var writeRef: Unmanaged<CFWriteStream>?
        CFStreamCreatePairWithSocketToHost(kCFAllocatorDefault, host as CFString, UInt32(port), &readRef, &writeRef)

        guard let readStream = readRef?.takeRetainedValue(), let writeStream = writeRef?.takeRetainedValue() else {
            throw SmtpError(kind: .connection, message: "Could not connect to \(host).")
        }

        // CFReadStream and CFWriteStream are toll-free bridged to InputStream and OutputStream.
        let inputStream = readStream as InputStream
        let outputStream = writeStream as OutputStream
        input = inputStream
        output = outputStream

        if tls {
            try enableTLS()
        }

        inputStream.open()
        outputStream.open()
        try waitUntilOpen(host: host)
    }

    /// Upgrades the open connection to TLS after the server agreed to STARTTLS.
    func startTLS() throws {
        guard buffer.isEmpty else {
            throw SmtpError(kind: .connection, message: "The server sent unexpected data before the secure connection started.")
        }
        try enableTLS()
    }

    private func enableTLS() throws {
        let level = StreamSocketSecurityLevel.negotiatedSSL
        let inputOK = input?.setProperty(level, forKey: .socketSecurityLevelKey) ?? false
        let outputOK = output?.setProperty(level, forKey: .socketSecurityLevelKey) ?? false
        guard inputOK && outputOK else {
            throw SmtpError(kind: .connection, message: "Could not switch to a secure connection.")
        }
    }

    private func waitUntilOpen(host: String) throws {
        let deadline = Date().addingTimeInterval(timeout)
        while Date() < deadline {
            let inputStatus = input?.streamStatus ?? .error
            let outputStatus = output?.streamStatus ?? .error

            if inputStatus == .error || outputStatus == .error || inputStatus == .closed || outputStatus == .closed {
                throw SmtpError(kind: .connection, message: describe(input?.streamError ?? output?.streamError, host: host))
            }
            if inputStatus != .notOpen && inputStatus != .opening && outputStatus != .notOpen && outputStatus != .opening {
                return
            }
            Thread.sleep(forTimeInterval: 0.02)
        }
        close()
        throw SmtpError(kind: .connection, message: "Timed out connecting to \(host).")
    }

    func write(_ text: String) throws {
        try write(Data(text.utf8))
    }

    func write(_ data: Data) throws {
        guard let output else {
            throw SmtpError(kind: .connection, message: "The connection is closed.")
        }

        try data.withUnsafeBytes { (raw: UnsafeRawBufferPointer) in
            guard let base = raw.bindMemory(to: UInt8.self).baseAddress else { return }
            var offset = 0
            while offset < data.count {
                let written = guarded { output.write(base + offset, maxLength: data.count - offset) }
                if written <= 0 {
                    throw failure(output.streamError, fallback: "The connection was lost while sending.")
                }
                offset += written
            }
        }
    }

    func readLine() throws -> String {
        guard let input else {
            throw SmtpError(kind: .connection, message: "The connection is closed.")
        }

        while true {
            if let newline = buffer.firstIndex(of: 0x0A) {
                let line = buffer[buffer.startIndex...newline]
                buffer.removeSubrange(buffer.startIndex...newline)
                return String(decoding: line, as: UTF8.self)
            }

            var chunk = [UInt8](repeating: 0, count: 4096)
            let count = guarded { input.read(&chunk, maxLength: chunk.count) }
            if count > 0 {
                buffer.append(contentsOf: chunk[0..<count])
            } else if count == 0 {
                throw timedOut
                    ? SmtpError(kind: .connection, message: "The mail server stopped answering.")
                    : SmtpError(kind: .connection, message: "The mail server closed the connection.")
            } else {
                throw failure(input.streamError, fallback: "The connection was lost while reading.")
            }
        }
    }

    func close() {
        input?.close()
        output?.close()
        input = nil
        output = nil
        buffer.removeAll()
    }

    /// Runs one blocking call and closes the connection if it takes longer than the timeout, which makes the call return.
    private func guarded<T>(_ body: () -> T) -> T {
        timedOut = false
        let watchdog = DispatchWorkItem { [weak self] in
            self?.timedOut = true
            self?.input?.close()
            self?.output?.close()
        }
        DispatchQueue.global().asyncAfter(deadline: .now() + timeout, execute: watchdog)
        defer { watchdog.cancel() }
        return body()
    }

    private func failure(_ error: Error?, fallback: String) -> SmtpError {
        if timedOut {
            return SmtpError(kind: .connection, message: "The mail server stopped answering.")
        }
        return SmtpError(kind: .connection, message: error.map { describe($0, host: nil) } ?? fallback)
    }

    private func describe(_ error: Error?, host: String?) -> String {
        guard let error else { return "Could not connect" + (host.map { " to \($0)" } ?? "") + "." }
        // -9800 to -9899 are the Secure Transport (TLS) errors, such as an untrusted certificate or a name mismatch.
        if (-9899 ... -9800).contains((error as NSError).code) {
            return "The secure connection to the mail server failed (its certificate or protocol was not accepted)."
        }
        return error.localizedDescription
    }
}

/// Sends messages through one SMTP login. Blocking: call it from a background thread.
final class SmtpSession {
    private let settings: SmtpSettings
    private let password: String
    private var connection: SmtpConnection?

    init(settings: SmtpSettings, password: String) {
        self.settings = settings
        self.password = password
    }

    /// Connects, starts TLS if needed and signs in.
    func connect() throws {
        close()
        let connection = SmtpConnection()
        self.connection = connection

        do {
            try connection.open(host: settings.host.trimmed, port: settings.port, tls: settings.security == .implicitTLS)
            try expect(connection, [220])

            var capabilities = try greet(connection)
            if settings.security == .startTLS {
                guard capabilities.contains(where: { $0.uppercased().hasPrefix("STARTTLS") }) else {
                    throw SmtpError(kind: .connection, message: "The server does not offer STARTTLS. Choose SSL/TLS or another port.")
                }
                try command(connection, "STARTTLS", expect: [220])
                try connection.startTLS()
                capabilities = try greet(connection)
            }

            try authenticate(connection, capabilities: capabilities)
        } catch {
            close()
            throw error
        }
    }

    /// Sends one message that is already on disk. Call `connect()` first.
    func send(file: URL, from: String, to: String, progress: ((Double) -> Void)? = nil) throws {
        guard let connection else {
            throw SmtpError(kind: .connection, message: "Not connected.")
        }
        guard SmtpAddress.isSafe(from), SmtpAddress.isSafe(to) else {
            throw SmtpError(kind: .permanent, message: "The sender or recipient address is not valid.")
        }

        do {
            try command(connection, "MAIL FROM:<\(from.trimmed)>", expect: [250])
            try command(connection, "RCPT TO:<\(to.trimmed)>", expect: [250, 251])
            try command(connection, "DATA", expect: [354])

            let total = ((try? FileManager.default.attributesOfItem(atPath: file.path))?[.size] as? NSNumber)?.doubleValue ?? 0
            let handle = try FileHandle(forReadingFrom: file)
            defer { try? handle.close() }

            var stuffer = DotStuffer()
            var sent = 0.0
            while let chunk = try handle.read(upToCount: 32 * 1024), !chunk.isEmpty {
                try connection.write(stuffer.process(chunk))
                sent += Double(chunk.count)
                if total > 0 { progress?(min(sent / total, 1)) }
            }

            // The builder always ends the message with CRLF, so the terminator can follow directly.
            try connection.write(".\r\n")
            try expect(connection, [250])
        } catch let error as SmtpError where error.kind == .permanent || error.kind == .temporary {
            // The server said no to this message but the connection is fine: clear the half-finished
            // transaction so the next message can use the same connection.
            try? connection.write("RSET\r\n")
            _ = try? readReply(connection)
            throw error
        }
    }

    func close() {
        if let connection {
            try? connection.write("QUIT\r\n")
            connection.close()
        }
        connection = nil
    }

    // MARK: - protocol steps

    private func greet(_ connection: SmtpConnection) throws -> [String] {
        let reply = try command(connection, "EHLO ead-iphone.local", expect: [250])
        return Array(reply.lines.dropFirst())
    }

    private func authenticate(_ connection: SmtpConnection, capabilities: [String]) throws {
        let line = capabilities.first { $0.uppercased().hasPrefix("AUTH ") || $0.uppercased().hasPrefix("AUTH=") }?.uppercased() ?? ""
        let user = settings.username.trimmed

        if line.contains("PLAIN") {
            let token = Data(("\u{0}" + user + "\u{0}" + password).utf8).base64EncodedString()
            try command(connection, "AUTH PLAIN \(token)", expect: [235], isSecret: true)
        } else if line.contains("LOGIN") {
            try command(connection, "AUTH LOGIN", expect: [334])
            try command(connection, Data(user.utf8).base64EncodedString(), expect: [334], isSecret: true)
            try command(connection, Data(password.utf8).base64EncodedString(), expect: [235], isSecret: true)
        } else {
            throw SmtpError(kind: .authentication, message: "The mail server does not offer a sign-in method this app supports (PLAIN or LOGIN).")
        }
    }

    @discardableResult
    private func command(_ connection: SmtpConnection, _ text: String, expect codes: Set<Int>, isSecret: Bool = false) throws -> SmtpReply {
        _ = isSecret
        try connection.write(text + "\r\n")
        return try expect(connection, codes)
    }

    @discardableResult
    private func expect(_ connection: SmtpConnection, _ codes: Set<Int>) throws -> SmtpReply {
        let reply = try readReply(connection)
        guard codes.contains(reply.code) else {
            throw Self.error(for: reply)
        }
        return reply
    }

    private func readReply(_ connection: SmtpConnection) throws -> SmtpReply {
        var lines: [String] = []
        while true {
            let raw = try connection.readLine()
            guard let parsed = SmtpReplyParser.parse(line: raw) else {
                throw SmtpError(kind: .connection, message: "The mail server sent something that is not an SMTP reply.")
            }
            lines.append(parsed.text)
            if parsed.isLast {
                return SmtpReply(code: parsed.code, lines: lines)
            }
        }
    }

    /// Turns a refusal into an error that says what to do about it.
    static func error(for reply: SmtpReply) -> SmtpError {
        let detail = reply.text.isEmpty ? "" : " " + reply.text
        switch reply.code {
        case 530, 534, 535, 538:
            return SmtpError(
                kind: .authentication,
                message: "The mail server rejected the user name or password (\(reply.code)). For Gmail, use an app password.")
        case 552:
            return SmtpError(kind: .permanent, message: "The mail server says the message is too big (\(reply.code)).\(detail)")
        case 400..<500:
            return SmtpError(kind: .temporary, message: "The mail server asked to try again later (\(reply.code)).\(detail)")
        default:
            return SmtpError(kind: .permanent, message: "The mail server refused the message (\(reply.code)).\(detail)")
        }
    }
}
