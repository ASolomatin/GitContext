using System.Text;

using Ionic.Zlib;

namespace GitContext;

#pragma warning disable RS1035 // Do not use APIs banned for analyzers

internal class ObjectFileEnumerator : IDisposable
{
    private Stream _stream;
    private StreamReader? _reader;

    public ObjectFileEnumerator(string objectsDirectory, string hash)
    {
        var objectPath = Path.Combine(objectsDirectory, hash.Substring(0, 2), hash.Substring(2));

        if (!File.Exists(objectPath))
            throw new InvalidOperationException("Object not found");

        _stream = new ZlibStream(File.OpenRead(objectPath), CompressionMode.Decompress);
    }

    public void Dispose()
    {
        var stream = Interlocked.Exchange(ref _stream!, null);
        var reader = Interlocked.Exchange(ref _reader, null);

        reader?.Dispose();
        stream?.Dispose();

        GC.SuppressFinalize(this);
    }

    public async ValueTask<string> ReadHeaderAsync()
    {
        var buffer = new byte[1024];
        var bytesRead = 0;

        while (await _stream.ReadAsync(buffer, bytesRead, 1) is 1)
        {
            if (buffer[bytesRead] == 0)
                break;

            bytesRead++;
            if (bytesRead == buffer.Length)
            {
                var newBuffer = new byte[buffer.Length * 2];
                Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
                buffer = newBuffer;
            }
        }

        var spaceIndex = Array.IndexOf(buffer, (byte)' ');

        if (spaceIndex <= 0)
            throw new InvalidOperationException("Invalid object format");

        var objectType = Encoding.UTF8.GetString(buffer, 0, spaceIndex);

        _reader = new StreamReader(_stream);

        return objectType;
    }

    public async Task<KeyValuePair<string, string>?> ReadValueAsync()
    {
        if (_reader is null)
            throw new InvalidOperationException("ReadHeaderAsync must be called first");

        var line = await _reader.ReadLineAsync() ?? throw new InvalidOperationException("Invalid object format");

        if (line.Length == 0)
            return null;

        var spaceIndex = line.IndexOf(' ');
        if (spaceIndex <= 0)
            throw new InvalidOperationException("Invalid object format");

        var key = line.Substring(0, spaceIndex);
        var value = line.Substring(spaceIndex + 1);

        return new KeyValuePair<string, string>(key, value);
    }

    public async ValueTask<string> ReadMessageAsync()
    {
        if (_reader is null)
            throw new InvalidOperationException("ReadHeaderAsync must be called first");

        var content = await _reader.ReadToEndAsync();

        return content;
    }
}
