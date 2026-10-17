import Darwin
import Foundation

// The host library compiled into the app (Lexi.Embed, NativeAOT), for iOS, which cannot start a
// lexi-host process. Lines go in through lexi_send and come back through a C callback, in order,
// exactly as over standard input and output. The library runs one host per app process. It also
// builds on macOS, where the tests drive a macOS build of the same library.
public final class EmbeddedTransport: HostTransport, @unchecked Sendable {
    private typealias Receive = @convention(c) (UnsafePointer<CChar>?, UnsafeMutableRawPointer?) -> Void
    private typealias Start = @convention(c) (UnsafePointer<CChar>?, UnsafePointer<CChar>?, UnsafePointer<CChar>?, Receive?, UnsafeMutableRawPointer?) -> Int32
    private typealias Send = @convention(c) (UnsafePointer<CChar>?) -> Int32
    private typealias Close = @convention(c) () -> Void

    // Receives lines on the library's thread and hands them to the client in order.
    private final class Sink: @unchecked Sendable {
        var continuation: AsyncThrowingStream<Data, Error>.Continuation?
    }

    private let library: URL, assets: URL, data: URL, user: URL
    private let sink = Sink()
    private var send: Send?
    private var finish: Close?

    public init(library: URL, assets: URL, data: URL, user: URL) {
        self.library = library; self.assets = assets; self.data = data; self.user = user
    }

    // The app bundle carries LexiEmbed.dylib in Frameworks and the model files in assets; user data
    // and dictionaries live in Application Support/LeXi, as on macOS.
    public static func app(bundle: Bundle = .main) -> EmbeddedTransport? {
        guard let frameworks = bundle.privateFrameworksURL, let resources = bundle.resourceURL,
              let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask).first else { return nil }
        let root = support.appending(path: "LeXi")
        return EmbeddedTransport(library: frameworks.appending(path: "LexiEmbed.dylib"), assets: resources.appending(path: "assets"),
                                 data: root.appending(path: "data"), user: root)
    }

    public func start(onExit: @escaping @Sendable (Int32) -> Void) throws -> AsyncThrowingStream<Data, Error> {
        guard let handle = dlopen(library.path, RTLD_NOW),
              let start = dlsym(handle, "lexi_start").map({ unsafeBitCast($0, to: Start.self) }),
              let send = dlsym(handle, "lexi_send").map({ unsafeBitCast($0, to: Send.self) }),
              let close = dlsym(handle, "lexi_close").map({ unsafeBitCast($0, to: Close.self) })
        else { throw HostError(code: "host_missing", message: "未找到内置的查询服务。请重新安装 LeXi。") }
        try? FileManager.default.createDirectory(at: data, withIntermediateDirectories: true)
        let (stream, continuation) = AsyncThrowingStream<Data, Error>.makeStream()
        sink.continuation = continuation
        let context = Unmanaged.passRetained(sink).toOpaque()
        let status = assets.path.withCString { a in data.path.withCString { d in user.path.withCString { u in
            start(a, d, u, { line, context in
                guard let line, let context else { return }
                Unmanaged<Sink>.fromOpaque(context).takeUnretainedValue().continuation?.yield(Data(bytes: line, count: strlen(line)))
            }, context)
        } } }
        guard status == 0 else {
            Unmanaged<Sink>.fromOpaque(context).release()
            throw HostError(code: "host_failed", message: status == 3 ? "无法创建 LeXi 的数据目录。" : "无法启动内置的查询服务。")
        }
        self.send = send
        self.finish = close
        return stream
    }

    public func write(_ line: Data) throws {
        guard let send else { throw HostError(code: "host_exited", message: "LeXi 查询服务已停止。") }
        // The library takes one line without its newline.
        let text = String(decoding: line.last == 0x0A ? line.dropLast() : line, as: UTF8.self)
        guard text.withCString({ send($0) }) == 0 else { throw HostError(code: "host_exited", message: "LeXi 查询服务已停止。") }
    }

    public func close() {
        finish?()
        send = nil
    }
}
