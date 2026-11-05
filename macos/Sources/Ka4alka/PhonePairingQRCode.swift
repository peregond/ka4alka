import AppKit
import CoreImage.CIFilterBuiltins

enum PhonePairingQRCode {
    static func link(address: String, code: String) -> String { address + "#pair=" + code }

    static func image(link: String) -> NSImage? {
        let filter = CIFilter.qrCodeGenerator()
        filter.message = Data(link.utf8)
        filter.correctionLevel = "M"
        guard let output = filter.outputImage else { return nil }
        // Four white modules around the code keep it readable in dark and light themes.
        let bounds = output.extent.insetBy(dx: -4, dy: -4)
        let padded = output.composited(over: CIImage(color: .white).cropped(to: bounds))
            .cropped(to: bounds).transformed(by: CGAffineTransform(scaleX: 8, y: 8))
        guard let image = CIContext(options: [.cacheIntermediates: false]).createCGImage(padded, from: padded.extent) else { return nil }
        return NSImage(cgImage: image, size: NSSize(width: CGFloat(image.width), height: CGFloat(image.height)))
    }
}
