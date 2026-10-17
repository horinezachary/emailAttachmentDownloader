import Foundation

struct MimeAttachment: Equatable {
    var url: URL
    var fileName: String
    var mimeType: String
}

struct BuiltMessage: Equatable {
    var url: URL
    var byteCount: Int
}

enum MimeError: LocalizedError {
    case unsafeHeader(String)

    var errorDescription: String? {
        switch self {
        case .unsafeHeader(let field): return "The \(field) contains characters that are not allowed in an e-mail header."
        }
    }
}

/// Writes an RFC 5322 message with picture attachments to a file, one chunk at a time, so a share extension (which has little
/// memory) never holds a whole photo as text. Lines end in CRLF and attachments are base64 in lines of 76 characters.
enum MimeBuilder {
    /// A multiple of 57, so every chunk encodes to whole 76-character lines.
    static let chunkSize = 57 * 400

    static func build(
        from: String,
        to: String,
        subject: String,
        bodyText: String,
        attachments: [MimeAttachment],
        date: Date = Date(),
        boundary: String = "ead-" + UUID().uuidString,
        into folder: URL
    ) throws -> BuiltMessage {
        for (field, value) in [("sender address", from), ("recipient address", to), ("subject", subject)] where !isHeaderSafe(value) {
            throw MimeError.unsafeHeader(field)
        }

        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let target = folder.appendingPathComponent("message-\(UUID().uuidString).eml")
        FileManager.default.createFile(atPath: target.path, contents: nil)

        let output = try FileHandle(forWritingTo: target)
        defer { try? output.close() }

        func write(_ text: String) throws {
            try output.write(contentsOf: Data(text.utf8))
        }

        let domain = from.split(separator: "@").last.map(String.init) ?? "ead.invalid"
        try write("From: \(from)\r\n")
        try write("To: \(to)\r\n")
        try write("Subject: \(subject)\r\n")
        try write("Date: \(rfc2822(date))\r\n")
        try write("Message-ID: <\(UUID().uuidString.lowercased())@\(domain)>\r\n")
        try write("MIME-Version: 1.0\r\n")
        try write("X-Mailer: Email Attachment Downloader iPhone app\r\n")
        try write("Content-Type: multipart/mixed; boundary=\"\(boundary)\"\r\n")
        try write("\r\n")

        try write("--\(boundary)\r\n")
        try write("Content-Type: text/plain; charset=us-ascii\r\n")
        try write("Content-Transfer-Encoding: 7bit\r\n")
        try write("\r\n")
        try write(asciiOnly(bodyText) + "\r\n")

        for attachment in attachments {
            let name = FileNameSanitizer.safe(attachment.fileName)
            try write("--\(boundary)\r\n")
            try write("Content-Type: \(attachment.mimeType); name=\"\(name)\"\r\n")
            try write("Content-Transfer-Encoding: base64\r\n")
            try write("Content-Disposition: attachment; filename=\"\(name)\"\r\n")
            try write("\r\n")

            let input = try FileHandle(forReadingFrom: attachment.url)
            defer { try? input.close() }
            while let chunk = try input.read(upToCount: chunkSize), !chunk.isEmpty {
                let encoded = chunk.base64EncodedString(options: [.lineLength76Characters, .endLineWithCarriageReturn, .endLineWithLineFeed])
                try write(encoded + "\r\n")
            }
        }

        try write("--\(boundary)--\r\n")

        let attributes = try FileManager.default.attributesOfItem(atPath: target.path)
        return BuiltMessage(url: target, byteCount: (attributes[.size] as? NSNumber)?.intValue ?? 0)
    }

    static func isHeaderSafe(_ value: String) -> Bool {
        !value.unicodeScalars.contains { $0.value < 32 || $0.value == 127 || $0.value > 126 }
    }

    static func rfc2822(_ date: Date) -> String {
        let formatter = DateFormatter()
        formatter.locale = Locale(identifier: "en_US_POSIX")
        formatter.timeZone = TimeZone.current
        formatter.dateFormat = "EEE, dd MMM yyyy HH:mm:ss Z"
        return formatter.string(from: date)
    }

    private static func asciiOnly(_ text: String) -> String {
        String(String.UnicodeScalarView(text.unicodeScalars.map { $0.isASCII ? $0 : "?" }))
    }
}

/// Splits photos into messages small enough for the mail server. Gmail's limit is 25 MB for the whole message, and base64
/// makes attachments about a third bigger, so a message carries at most about 15 MB of photos.
enum MessagePlanner {
    static let defaultMaxBytes = 15_000_000

    /// A single photo bigger than this cannot be e-mailed at all.
    static let maxSingleBytes = 18_000_000

    static func group<T>(_ items: [T], size: (T) -> Int, maxBytes: Int = defaultMaxBytes) -> [[T]] {
        var groups: [[T]] = []
        var current: [T] = []
        var currentBytes = 0

        for item in items {
            let bytes = size(item)
            if !current.isEmpty && currentBytes + bytes > maxBytes {
                groups.append(current)
                current = []
                currentBytes = 0
            }
            current.append(item)
            currentBytes += bytes
        }

        if !current.isEmpty {
            groups.append(current)
        }
        return groups
    }
}
