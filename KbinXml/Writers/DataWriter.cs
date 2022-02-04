﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using KbinXml.Utils;

namespace KbinXml.Writers
{
    public class DataWriter : BeBinaryWriter
    {
        private int _pos32;
        private int _pos16;
        private int _pos8;
        private readonly Encoding _encoding;

        public DataWriter(Encoding encoding)
        {
            _encoding = encoding;
        }

        public override void WriteBytes(ReadOnlySpan<byte> buffer)
        {
            switch (buffer.Length)
            {
                case 1:
                    Write8BitAligned(buffer[0]);
                    break;

                case 2:
                    Write16BitAligned(buffer);
                    break;

                default:
                    Write32BitAligned(buffer);
                    break;
            }
        }

        public void WriteString(string value)
        {
            var bytes = _encoding.GetBytes(value);

            var length = bytes.Length + 1;
            if (length <= 128)
            {
                Span<byte> span = stackalloc byte[length];
                bytes.CopyTo(span);

                WriteU32((uint)span.Length);
                Write32BitAligned(span);
            }
            else
            {
                var arr = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    Span<byte> array = arr.AsSpan(0, length);
                    bytes.CopyTo(array);

                    WriteU32((uint)array.Length);
                    Write32BitAligned(array);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(arr);
                }
            }
        }

        public void WriteBinary(string value)
        {
            WriteU32((uint)value.Length / 2);
            Write32BitAligned(ConvertHexString(value));
        }

        //todo: base default impl
        public override void WriteU32(uint value)
        {
            Span<byte> span = stackalloc byte[sizeof(uint)];
            var builder = new ValueListBuilder<byte>(span);
            BitConverterHelper.WriteBeBytes(ref builder, value);
            WriteBytes(builder.AsSpan());
        }

        private void Write32BitAligned(ReadOnlySpan<byte> buffer)
        {
            Pad(_pos32);

            SetRange(buffer, ref _pos32);
            while (_pos32 % 4 != 0)
                _pos32++;

            Realign16_8();
        }

        private void Write16BitAligned(ReadOnlySpan<byte> buffer)
        {
            Pad(_pos16);

            if (_pos16 % 4 == 0)
                _pos32 += 4;

            SetRange(buffer, ref _pos16);
            Realign16_8();
        }

        private void Write8BitAligned(byte value)
        {
            Pad(_pos8);

            if (_pos8 % 4 == 0)
                _pos32 += 4;

            SetRange(new[] { value }, ref _pos8);
            Realign16_8();
        }

        private void SetRange(ReadOnlySpan<byte> buffer, ref int offset)
        {
            if (offset == Stream.Length)
            {
                Stream.WriteSpan(buffer);
                offset += buffer.Length;
            }
            else
            {
                var pos = Stream.Position;
                Stream.Position = offset;

                Stream.WriteSpan(buffer);

                offset += buffer.Length;

                // fix the problem if the buffer length is greater than list count
                if (offset <= Stream.Length)
                    Stream.Position = pos;
            }
        }

        private void Realign16_8()
        {
            if (_pos8 % 4 == 0)
                _pos8 = _pos32;

            if (_pos16 % 4 == 0)
                _pos16 = _pos32;
        }

        private void Pad(int target)
        {
            var left = target - Stream.Length;
            if (left <= 0) return;
            for (int i = 0; i < left; i++)
            {
                Stream.WriteByte(0);
            }
        }

        private static byte[] ConvertHexString(string hexString) => Enumerable.Range(0, hexString.Length)
            .Where(x => x % 2 == 0)
            .Select(x => byte.Parse(hexString.Substring(x, 2), NumberStyles.HexNumber))
            .ToArray();
    }
}