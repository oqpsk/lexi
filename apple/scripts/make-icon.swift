// Renders docs/brandmark.svg into a macOS .iconset (then: iconutil -c icns), or with --ios into one
// full-bleed 1024-pixel PNG for an iOS asset catalog.
// usage: swift make-icon.swift <brandmark.svg> <output.iconset>
//        swift make-icon.swift <brandmark.svg> <output.png> --ios
import AppKit

let arguments = CommandLine.arguments
guard arguments.count == 3 || arguments.count == 4 && arguments[3] == "--ios",
      let mark = NSImage(contentsOf: URL(fileURLWithPath: arguments[1])) else {
    FileHandle.standardError.write(Data("usage: make-icon.swift <brandmark.svg> <output.iconset | output.png --ios>\n".utf8)); exit(2)
}
let output = URL(fileURLWithPath: arguments[2])

// iOS masks the icon itself: fill the square with the mark's black and scale the 256-unit artwork so the
// canvas sits inside its pink outline, rounded corners included (22...234), leaving the ξ centred on black.
if arguments.count == 4 {
    let pixels = 1024
    guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
                                        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)
    else { exit(1) }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    NSGraphicsContext.current?.imageInterpolation = .high
    NSColor.black.setFill()
    NSRect(x: 0, y: 0, width: pixels, height: pixels).fill()
    let scale = CGFloat(pixels) / 212
    mark.draw(in: NSRect(x: -22 * scale, y: -22 * scale, width: 256 * scale, height: 256 * scale))
    NSGraphicsContext.restoreGraphicsState()
    try bitmap.representation(using: .png, properties: [:])!.write(to: output)
    exit(0)
}
try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
// macOS icon grid: the artwork occupies 824 of 1024 points, centered, leaving the system shadow margin.
for (points, scale) in [(16, 1), (16, 2), (32, 1), (32, 2), (128, 1), (128, 2), (256, 1), (256, 2), (512, 1), (512, 2)] {
    let pixels = points * scale
    guard let bitmap = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: pixels, pixelsHigh: pixels, bitsPerSample: 8,
                                        samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0)
    else { exit(1) }
    NSGraphicsContext.saveGraphicsState()
    NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: bitmap)
    NSGraphicsContext.current?.imageInterpolation = .high
    let inset = CGFloat(pixels) * 100 / 1024
    mark.draw(in: NSRect(x: inset, y: inset, width: CGFloat(pixels) - inset * 2, height: CGFloat(pixels) - inset * 2))
    NSGraphicsContext.restoreGraphicsState()
    let name = "icon_\(points)x\(points)" + (scale == 2 ? "@2x" : "") + ".png"
    try bitmap.representation(using: .png, properties: [:])!.write(to: output.appending(path: name))
}
