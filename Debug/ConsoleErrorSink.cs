using System;
using System.IO;
using System.Threading.Tasks;

namespace ScapeCore.Traceability.Logging
{
    public sealed class ConsoleErrorSink(string name, LoggingColor color) : ISink
    {
        private readonly string _name = name;
        private readonly LoggingColor _color = color;
        private BufferedStream? _stream = new(Console.OpenStandardError());
        private BufferedStream? _selfStream = new(new MemoryStream());
        private bool disposedValue;

        public string Name { get => _name; }
        public LoggingColor Color { get => _color; }
        public Stream? OutputStream { get => _stream; }
        public Stream? SelfStream { get => _selfStream; }

        private void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _stream?.Dispose();
                    _stream = null;
                    _selfStream?.Dispose();
                    _selfStream = null;
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await DisposeAsyncCore().ConfigureAwait(false);

            Dispose(disposing: false);
            GC.SuppressFinalize(this);
        }

        private async ValueTask DisposeAsyncCore()
        {
            if (_stream is not null)
                await _stream.DisposeAsync().ConfigureAwait(false);
            _stream = null;
            if (_selfStream is not null)
                await _selfStream.DisposeAsync().ConfigureAwait(false);
            _selfStream = null;
        }

    }
}