using System;
using System.IO;

namespace Microsoft.TemplateEngine.Core.Util
{
    internal class MultiStream : Stream
    {
        private readonly Stream _stream1;
        private readonly Stream _stream2;
        private bool _isStream1Depleted;

        public MultiStream(Stream stream1, Stream stream2)
        {
            _stream1 = stream1;
            _stream2 = stream2;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotImplementedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
            throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int read = 0;
            if (!_isStream1Depleted)
            {
                read = _stream1.Read(buffer, offset, count);

                if(read == count)
                {
                    return read;
                }

                count -= read;
                offset += read;
                _isStream1Depleted = true;
            }

            read += _stream2.Read(buffer, offset, count);
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _stream1.Dispose();
            _stream2.Dispose();
        }
    }
}