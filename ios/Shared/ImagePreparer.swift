import Foundation
import ImageIO
import UniformTypeIdentifiers

struct PreparedImage: Equatable {
    var url: URL
    var fileName: String
    var mimeType: String
    var byteCount: Int
}

enum ImagePrepareError: LocalizedError {
    case unreadable(String)
    case couldNotWrite(String)

    var errorDescription: String? {
        switch self {
        case .unreadable(let name): return "\(name) is not a picture that can be read."
        case .couldNotWrite(let name): return "\(name) could not be prepared for sending."
        }
    }
}

/// Gets a photo ready to attach: HEIC becomes JPEG (Windows cannot show HEIC without an add-on) and very large photos are
/// scaled down. Everything else (JPEG at a sensible size, PNG, GIF) is sent as it is. Re-encoding uses ImageIO's thumbnail
/// path, which applies the photo's rotation and never loads the full image into memory, so it is safe inside a share extension.
/// Re-encoded photos carry no Exif block, so no location data leaves the phone that way.
enum ImagePreparer {
    static func prepare(source: URL, displayName: String, options: ImageOptions, into folder: URL) throws -> PreparedImage {
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)

        guard let imageSource = CGImageSourceCreateWithURL(source as CFURL, nil), CGImageSourceGetCount(imageSource) > 0 else {
            throw ImagePrepareError.unreadable(displayName)
        }

        let identifier = (CGImageSourceGetType(imageSource) as String?) ?? ""
        let type = UTType(identifier)
        let isHEIC = type?.conforms(to: .heic) == true || type?.conforms(to: .heif) == true
        let isJPEG = type?.conforms(to: .jpeg) == true
        let longest = longestSide(of: imageSource)
        let needsShrink = options.maxDimension > 0 && longest > options.maxDimension
        let reencode = (isHEIC && options.convertHEICToJPEG) || ((isHEIC || isJPEG) && needsShrink)

        let safeName = FileNameSanitizer.safe(displayName)
        guard reencode else {
            return try copy(source: source, safeName: safeName, fallbackExtension: type?.preferredFilenameExtension, into: folder)
        }

        var maxPixel = longest > 0 ? longest : max(options.maxDimension, 4096)
        if options.maxDimension > 0 {
            maxPixel = min(maxPixel, options.maxDimension)
        }

        let thumbnailOptions: [CFString: Any] = [
            kCGImageSourceCreateThumbnailFromImageAlways: true,
            kCGImageSourceCreateThumbnailWithTransform: true,
            kCGImageSourceThumbnailMaxPixelSize: maxPixel,
        ]
        guard let image = CGImageSourceCreateThumbnailAtIndex(imageSource, 0, thumbnailOptions as CFDictionary) else {
            throw ImagePrepareError.unreadable(displayName)
        }

        let stem = (safeName as NSString).deletingPathExtension
        let target = uniqueURL(in: folder, name: stem + ".jpg")
        guard let destination = CGImageDestinationCreateWithURL(target as CFURL, UTType.jpeg.identifier as CFString, 1, nil) else {
            throw ImagePrepareError.couldNotWrite(displayName)
        }

        let quality = min(max(options.jpegQuality, 0.1), 1.0)
        let properties: [CFString: Any] = [kCGImageDestinationLossyCompressionQuality: quality]
        CGImageDestinationAddImage(destination, image, properties as CFDictionary)
        guard CGImageDestinationFinalize(destination) else {
            throw ImagePrepareError.couldNotWrite(displayName)
        }

        return PreparedImage(
            url: target,
            fileName: target.lastPathComponent,
            mimeType: "image/jpeg",
            byteCount: fileSize(target))
    }

    /// The MIME type for a file name's extension, `application/octet-stream` when it is not known.
    static func mimeType(forExtension ext: String) -> String {
        UTType(filenameExtension: ext.lowercased())?.preferredMIMEType ?? "application/octet-stream"
    }

    private static func copy(source: URL, safeName: String, fallbackExtension: String?, into folder: URL) throws -> PreparedImage {
        var name = safeName
        if (name as NSString).pathExtension.isEmpty {
            let ext = source.pathExtension.isEmpty ? (fallbackExtension ?? "jpg") : source.pathExtension
            name += "." + ext
        }

        let target = uniqueURL(in: folder, name: name)
        do {
            try FileManager.default.copyItem(at: source, to: target)
        } catch {
            throw ImagePrepareError.couldNotWrite(name)
        }

        return PreparedImage(
            url: target,
            fileName: target.lastPathComponent,
            mimeType: mimeType(forExtension: target.pathExtension),
            byteCount: fileSize(target))
    }

    private static func longestSide(of source: CGImageSource) -> Int {
        guard let properties = CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [CFString: Any] else { return 0 }
        let width = (properties[kCGImagePropertyPixelWidth] as? NSNumber)?.intValue ?? 0
        let height = (properties[kCGImagePropertyPixelHeight] as? NSNumber)?.intValue ?? 0
        return max(width, height)
    }

    private static func fileSize(_ url: URL) -> Int {
        let attributes = try? FileManager.default.attributesOfItem(atPath: url.path)
        return (attributes?[.size] as? NSNumber)?.intValue ?? 0
    }

    private static func uniqueURL(in folder: URL, name: String) -> URL {
        let stem = (name as NSString).deletingPathExtension
        let ext = (name as NSString).pathExtension
        var candidate = folder.appendingPathComponent(name)
        var counter = 2
        while FileManager.default.fileExists(atPath: candidate.path) {
            candidate = folder.appendingPathComponent(ext.isEmpty ? "\(stem)-\(counter)" : "\(stem)-\(counter).\(ext)")
            counter += 1
        }
        return candidate
    }
}
