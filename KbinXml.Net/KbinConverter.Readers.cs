﻿using System;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using KbinXml.Net.Internal;
using KbinXml.Net.Internal.Providers;
using KbinXml.Net.Readers;
using KbinXml.Net.Utils;

namespace KbinXml.Net;

public static partial class KbinConverter
{
    /// <summary>
    /// Reads the KBin bytes into an XML <see cref="XDocument"/>.
    /// </summary>
    /// <param name="sourceBuffer">The KBin bytes to convert.</param>
    /// <returns>Returns the <see cref="XDocument"/>.</returns>
    public static XDocument ReadXmlLinq(Memory<byte> sourceBuffer)
    {
        var xDocument = (XDocument)Read(sourceBuffer, e => new XDocumentProvider(e));
        return xDocument;
    }

    /// <summary>
    /// Reads the KBin bytes into an XML <see cref="T:byte[]"/>.
    /// </summary>
    /// <param name="sourceBuffer">The KBin bytes convert.</param>
    /// <returns>Returns the <see cref="T:byte[]"/>.</returns>
    public static byte[] ReadXmlBytes(Memory<byte> sourceBuffer)
    {
        var bytes = (byte[])Read(sourceBuffer, e => new XmlWriterProvider(e));
        return bytes;
    }

    /// <summary>
    /// Reads the KBin bytes into an XML <see cref="XmlDocument"/>.
    /// </summary>
    /// <param name="sourceBuffer">The KBin bytes convert.</param>
    /// <returns>Returns the <see cref="XmlDocument"/>.</returns>
    public static XmlDocument ReadXml(Memory<byte> sourceBuffer)
    {
        var xmlDocument = (XmlDocument)Read(sourceBuffer, e => new XmlDocumentProvider(e));
        return xmlDocument;
    }

    private static object Read(Memory<byte> sourceBuffer, Func<Encoding, WriterProvider> createWriterProvider)
    {
        using var readContext = GetReadContext(sourceBuffer, createWriterProvider);
        var writerProvider = readContext.WriterProvider;
        var nodeReader = readContext.NodeReader;
        var dataReader = readContext.DataReader;

        writerProvider.WriteStartDocument();
        string? currentType = null;
        string? holdValue = null;
        while (true)
        {
            var nodeType = nodeReader.ReadU8();

            //Array flag is on the second bit
            var array = (nodeType & 0x40) > 0;
            nodeType = (byte)(nodeType & ~0x40);
            if (ControlTypes.Contains(nodeType))
            {
                var controlType = (ControlType)nodeType;
                switch (controlType)
                {
                    case ControlType.NodeStart:
                        if (holdValue != null)
                        {
                            writerProvider.WriteElementValue(holdValue);
                            holdValue = null;
                        }

                        var elementName = nodeReader.ReadString();
                        writerProvider.WriteStartElement(elementName);
                        break;
                    case ControlType.Attribute:
                        var attr = nodeReader.ReadString();
                        var value = dataReader.ReadString(dataReader.ReadS32());
                        // Size has been written below
                        if (currentType != "bin" || attr != "__size")
                        {
                            writerProvider.WriteStartAttribute(attr);
                            writerProvider.WriteAttributeValue(value);
                            writerProvider.WriteEndAttribute();
                        }

                        break;
                    case ControlType.NodeEnd:
                        if (holdValue != null)
                        {
                            writerProvider.WriteElementValue(holdValue);
                            holdValue = null;
                        }

                        writerProvider.WriteEndElement();
                        break;
                    case ControlType.FileEnd:
                        return writerProvider.GetResult();
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            else if (TypeDictionary.TypeMap.TryGetValue(nodeType, out var propertyType))
            {
                if (holdValue != null)
                {
                    writerProvider.WriteElementValue(holdValue);
                    holdValue = null;
                }

                var elementName = nodeReader.ReadString();
                writerProvider.WriteStartElement(elementName);

                writerProvider.WriteStartAttribute("__type");
                writerProvider.WriteAttributeValue(propertyType.Name);
                writerProvider.WriteEndAttribute();

                currentType = propertyType.Name;

                int arraySize;
                if (array || propertyType.Name is "str" or "bin")
                    arraySize = dataReader.ReadS32(); //Total size.
                else
                    arraySize = propertyType.Size * propertyType.Count;

                if (propertyType.Name == "str")
                    holdValue = dataReader.ReadString(arraySize);
                else if (propertyType.Name == "bin")
                {
                    writerProvider.WriteStartAttribute("__size");
                    writerProvider.WriteAttributeValue(arraySize.ToString());
                    writerProvider.WriteEndAttribute();
                    holdValue = dataReader.ReadBinary(arraySize);
                }
                else
                {
                    if (array)
                    {
                        var size = (arraySize / (propertyType.Size * propertyType.Count)).ToString();
                        writerProvider.WriteStartAttribute("__count");
                        writerProvider.WriteAttributeValue(size);
                        writerProvider.WriteEndAttribute();
                    }

                    var span = dataReader.ReadBytes(arraySize);
                    var stringBuilder = new StringBuilder();
                    var loopCount = arraySize / propertyType.Size;
                    for (var i = 0; i < loopCount; i++)
                    {
                        var subSpan = span.Slice(i * propertyType.Size, propertyType.Size);
                        stringBuilder.Append(propertyType.GetString(subSpan.Span));
                        if (i != loopCount - 1)
                        {
#if NETCOREAPP3_1_OR_GREATER
                            stringBuilder.Append(' ');
#else
                            stringBuilder.Append(" ");
#endif
                        }
                    }

                    holdValue = stringBuilder.ToString();
                }
            }
            else
            {
                throw new KbinException($"Unknown node type: {nodeType}");
            }
        }
    }

    private static ReadContext GetReadContext(Memory<byte> sourceBuffer, Func<Encoding, WriterProvider> createWriterProvider)
    {
        //Read header section.
        var binaryBuffer = new BeBinaryReader(sourceBuffer);
        var signature = binaryBuffer.ReadU8();
        var compressionFlag = binaryBuffer.ReadU8();
        var encodingFlag = binaryBuffer.ReadU8();
        var encodingFlagNot = binaryBuffer.ReadU8();

        //Verify magic.
        if (signature != 0xA0)
            throw new KbinException($"Signature was invalid. 0x{signature:X2} != 0xA0");

        //Encoding flag should be an inverse of the fourth byte.
        if ((byte)~encodingFlag != encodingFlagNot)
            throw new KbinException($"Third byte was not an inverse of the fourth. {~encodingFlag} != {encodingFlagNot}");

        var compressed = compressionFlag == 0x42;
        var encoding = EncodingDictionary.EncodingMap[encodingFlag];

        //Get buffer lengths and load.
        var nodeLength = binaryBuffer.ReadS32();
        var nodeReader = new NodeReader(sourceBuffer.Slice(8, nodeLength), compressed, encoding);

        var dataLength = BitConverterHelper.ToBeInt32(sourceBuffer.Slice(nodeLength + 8, 4).Span);
        var dataReader = new DataReader(sourceBuffer.Slice(nodeLength + 12, dataLength), encoding);

        var readProvider = createWriterProvider(encoding);

        var readContext = new ReadContext(nodeReader, dataReader, readProvider);
        return readContext;
    }

    private class ReadContext : IDisposable
    {
        public ReadContext(NodeReader nodeReader, DataReader dataReader, WriterProvider writerProvider)
        {
            NodeReader = nodeReader;
            DataReader = dataReader;
            WriterProvider = writerProvider;
        }

        public NodeReader NodeReader { get; set; }
        public DataReader DataReader { get; set; }
        public WriterProvider WriterProvider { get; set; }

        public void Dispose()
        {
            WriterProvider.Dispose();
        }
    }
}