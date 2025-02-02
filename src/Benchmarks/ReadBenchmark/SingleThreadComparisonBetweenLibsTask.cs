﻿using System.IO;
using System.Xml;
using System.Xml.Linq;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Order;
using KbinXml.Net;
using kbinxmlcs;

#if NETCOREAPP
using KBinXML;
#endif

namespace ReadBenchmark;

[Orderer(SummaryOrderPolicy.FastestToSlowest)]
[MemoryDiagnoser]
#if !NETCOREAPP
[SimpleJob(RuntimeMoniker.Net48)]
#else
[SimpleJob(RuntimeMoniker.Net60)]
[SimpleJob(RuntimeMoniker.Net70)]
[SimpleJob(RuntimeMoniker.Net80)]
#endif
public class SingleThreadComparisonBetweenLibsTask
{
    private byte[] _kbin;
    private byte[] _xmlBytes;
    private XDocument _linq;
    private XmlDocument _xml;
    private string _xmlStr;

    [GlobalSetup]
    public void Setup()
    {
        _kbin = KbinConverter.Write(File.ReadAllText(@"data/small.xml"), KnownEncodings.UTF8);
        //_kbin = File.ReadAllBytes(@"data\test_case.bin");
        _xmlBytes = KbinConverter.ReadXmlBytes(_kbin);
        _linq = KbinConverter.ReadXmlLinq(_kbin);
        _xml = KbinConverter.ReadXml(_kbin);
        _xmlStr = _linq.ToString();
    }

    [Benchmark(Baseline = true)]
    public object? ReadLinq_NKZsmos()
    {
        return KbinConverter.ReadXmlLinq(_kbin);
    }

    [Benchmark]
    public object? ReadLinq_FSH_B()
    {
        return new KbinReader(_kbin).ReadLinq();
    }

#if NETCOREAPP
    [Benchmark]
    public object? ReadLinq_ItsNovaHere()
    {
        return new Reader(_kbin).GetDocument();
    }
#endif
}