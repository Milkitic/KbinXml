﻿using System;
using System.Text;
using KbinXml.Utils;

namespace KbinXml.Readers;

internal class NodeReader : BeBinaryReader
{
    private readonly bool _compressed;
    private readonly Encoding _encoding;

    public NodeReader(Memory<byte> buffer, bool compressed, Encoding encoding)
        : base(buffer)
    {
        _compressed = compressed;
        _encoding = encoding;
    }

    public string ReadString()
    {
        int length = ReadU8();

        if (_compressed)
        {
            var memory = ReadBytes((int)Math.Ceiling(length * 6 / 8.0));
            return SixbitHelper.Decode(memory.Span, length);
        }

        var mem = ReadBytes((length & 0xBF) + 1);
#if NETSTANDARD2_1 || NETCOREAPP3_1_OR_GREATER
        return _encoding.GetString(mem.Span);
#elif NETSTANDARD2_0
        unsafe
        {
            fixed (byte* p = mem.Span)
                return _encoding.GetString(p, mem.Length);
        }
#endif
    }
}