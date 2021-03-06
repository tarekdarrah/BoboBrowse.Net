﻿//* Bobo Browse Engine - High performance faceted/parametric search implementation 
//* that handles various types of semi-structured data.  Originally written in Java.
//*
//* Ported and adapted for C# by Shad Storhaug.
//*
//* Copyright (C) 2005-2015  John Wang
//*
//* Licensed under the Apache License, Version 2.0 (the "License");
//* you may not use this file except in compliance with the License.
//* You may obtain a copy of the License at
//*
//*   http://www.apache.org/licenses/LICENSE-2.0
//*
//* Unless required by applicable law or agreed to in writing, software
//* distributed under the License is distributed on an "AS IS" BASIS,
//* WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//* See the License for the specific language governing permissions and
//* limitations under the License.

// Version compatibility level: 4.0.2
namespace BoboBrowse.Net.Facets.Attribute
{
    using BoboBrowse.Net.Facets.Filter;
    using BoboBrowse.Net.Support;
    using Lucene.Net.Analysis;
    using Lucene.Net.Analysis.Core;
    using Lucene.Net.Documents;
    using Lucene.Net.Index;
    using Lucene.Net.Search;
    using Lucene.Net.Store;
    using Lucene.Net.Support;
    using Lucene.Net.Util;
    using NUnit.Framework;
    using System;
    using System.Collections.Generic;
    using System.Linq;

    [TestFixture]
    public class AttributesFacetHandlerTest
    {
        private RAMDirectory directory;
        private Analyzer analyzer;
        private List<IFacetHandler> facetHandlers;
        private AttributesFacetHandler attributesFacetHandler;
        private BoboBrowser browser;
        private BoboMultiReader boboReader;
        private IDictionary<string, string> selectionProperties;
        private const string AttributeHandlerName = "attributes";

        private void AddMetaDataField(Document doc, string name, string[] vals)
        {
            foreach (string val in vals)
            {
                Field field = new StringField(name, val, Field.Store.NO);
                doc.Add(field);
            }
        }

        [SetUp]
        public void Init()
        {
            facetHandlers = new List<IFacetHandler>();

            directory = new RAMDirectory();
            analyzer = new WhitespaceAnalyzer(LuceneVersion.LUCENE_48);
            selectionProperties = new Dictionary<string, string>();
            IndexWriterConfig conf = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            conf.SetOpenMode(OpenMode.CREATE);
            IndexWriter writer = new IndexWriter(directory, conf);

            writer.AddDocument(Doc("prop1=val1", "prop2=val1", "prop5=val1"));
            writer.AddDocument(Doc("prop1=val2", "prop3=val1", "prop7=val7"));
            writer.AddDocument(Doc("prop1=val2", "prop3=val2", "prop3=val3"));
            writer.AddDocument(Doc("prop1=val1", "prop2=val1"));
            writer.AddDocument(Doc("prop1=val1", "prop2=val1"));
            writer.AddDocument(Doc("prop1=val1", "prop2=val1", "prop4=val2", "prop4=val3"));
            writer.Commit();

            attributesFacetHandler = new AttributesFacetHandler(AttributeHandlerName, AttributeHandlerName, 
                null, null, new Dictionary<string, string>());
            facetHandlers.Add(attributesFacetHandler);
            DirectoryReader reader = DirectoryReader.Open(directory);
            boboReader = BoboMultiReader.GetInstance(reader, facetHandlers);
            foreach (BoboSegmentReader subReader in boboReader.GetSubReaders())
            {
                attributesFacetHandler.LoadFacetData(subReader);
            }
            browser = new BoboBrowser(boboReader);
        }

        [TearDown]
        public void Dispose()
        {
            facetHandlers = null;
            directory = null;
            analyzer = null;
            selectionProperties = null;
        }

        private Document Doc(params string[] terms)
        {
            Document doc = new Document();
            AddMetaDataField(doc, AttributeHandlerName, terms);
            return doc;
        }

        public BrowseRequest CreateRequest(int minHitCount, params string[] terms)
        {
            return CreateRequest(minHitCount, BrowseSelection.ValueOperation.ValueOperationOr, terms);
        }

        public BrowseRequest CreateRequest(int minHitCount, BrowseSelection.ValueOperation operation, 
            params string[] terms)
        {
            BrowseRequest req = new BrowseRequest();

            BrowseSelection sel = new BrowseSelection(AttributeHandlerName);
            foreach (String term in terms)
            {
                sel.AddValue(term);
            }
            sel.SetSelectionProperties(selectionProperties);
            sel.SelectionOperation = (operation);
            req.AddSelection(sel);
            req.Count = (50);
            FacetSpec fs = new FacetSpec();
            fs.MinHitCount = (minHitCount);
            req.SetFacetSpec(AttributeHandlerName, fs);
            return req;
        }


        [Test]
        public void Test1Filter()
        {
            BrowseRequest request = CreateRequest(1, "prop3");
            RandomAccessFilter randomAccessFilter = attributesFacetHandler.BuildFilter(request
                .GetSelection(AttributeHandlerName));
            DocIdSetIterator iterator = randomAccessFilter.GetDocIdSet(
                boboReader.GetSubReaders().Get(0).AtomicContext, null).GetIterator();
            int docId = iterator.NextDoc();
            int[] docIds = new int[2];
            int i = 0;
            while (docId != DocIdSetIterator.NO_MORE_DOCS)
            {
                docIds[i] = docId;
                i++;
                docId = iterator.NextDoc();
            }
            Assert.AreEqual(Arrays.ToString(new int[] { 1, 2 }), Arrays.ToString(docIds));

            BrowseResult res = browser.Browse(request);
            Assert.AreEqual(res.NumHits, 2);
            IFacetAccessible fa = res.GetFacetAccessor(AttributeHandlerName);
            ICollection<BrowseFacet> facets = fa.GetFacets();
            Console.WriteLine(facets);
            Assert.AreEqual(3, facets.Count);
            BrowseFacet facet = facets.Get(0);
            Assert.AreEqual(1, facet.FacetValueHitCount);
        }

        [Test]
        public void Test2PropertyRetrieval()
        {
            BrowseRequest request = CreateRequest(1, "prop3");
            BrowseResult res = browser.Browse(request);
            Assert.AreEqual(res.NumHits, 2);
            Assert.AreEqual(res.Hits[0].DocId, 1);
            Assert.AreEqual(res.Hits[1].DocId, 2);
            ICollection<BrowseFacet> facets = res.GetFacetAccessor(AttributeHandlerName).GetFacets();
            Assert.AreEqual(facets.Count, 3);
            Assert.AreEqual(facets.Get(0).Value, "prop3=val1");
            Assert.AreEqual(facets.Get(0).FacetValueHitCount, 1);
            Assert.AreEqual(facets.Get(2).Value, "prop3=val3");
            Assert.AreEqual(facets.Get(2).FacetValueHitCount, 1);
        }

        [Test]
        public void Test3PropertyInEachDocRetrieval()
        {
            BrowseRequest request = CreateRequest(1, "prop1");
            BrowseResult res = browser.Browse(request);
            Assert.AreEqual(res.NumHits, 6);
            Assert.AreEqual(res.Hits[0].DocId, 0);
            Assert.AreEqual(res.Hits[5].DocId, 5);
            ICollection<BrowseFacet> facets = res.GetFacetAccessor(AttributeHandlerName).GetFacets();
            Assert.AreEqual(facets.Count, 2);
            Assert.AreEqual(facets.Get(0).Value, "prop1=val1");
            Assert.AreEqual(facets.Get(0).FacetValueHitCount, 4);
            Assert.AreEqual(facets.Get(1).Value, "prop1=val2");
            Assert.AreEqual(facets.Get(1).FacetValueHitCount, 2);
        }

        [Test]
        public void Test4PropertyInFirstDocRetrieval()
        {
            BrowseRequest request = CreateRequest(1, "prop5");
            BrowseResult res = browser.Browse(request);
            Assert.AreEqual(res.NumHits, 1);
            Assert.AreEqual(res.Hits[0].DocId, 0);

            ICollection<BrowseFacet> facets = res.GetFacetAccessor(AttributeHandlerName).GetFacets();
            Assert.AreEqual(facets.Count, 1);
            Assert.AreEqual(facets.Get(0).Value, "prop5=val1");
            Assert.AreEqual(facets.Get(0).FacetValueHitCount, 1);
        }

        [Test]
        public void Test5PropertyInLastDocRetrieval()
        {
            BrowseRequest request = CreateRequest(1, "prop4");
            BrowseResult res = browser.Browse(request);
            Console.WriteLine(res);
            Assert.AreEqual(res.NumHits, 1);
            Assert.AreEqual(res.Hits[0].DocId, 5);

            ICollection<BrowseFacet> facets = res.GetFacetAccessor(AttributeHandlerName).GetFacets();
            Assert.AreEqual(facets.Count, 2);
            Assert.AreEqual(facets.Get(0).Value, "prop4=val2");
            Assert.AreEqual(facets.Get(0).FacetValueHitCount, 1);
            Assert.AreEqual(facets.Get(1).Value, "prop4=val3");
            Assert.AreEqual(facets.Get(1).FacetValueHitCount, 1);
        }

        [Test]
        public void Test6NonExisitngPropertyDocRetrieval()
        {
            BrowseRequest request = CreateRequest(1, "propMissing");
            BrowseResult res = browser.Browse(request);
            Assert.AreEqual(res.NumHits, 0);
        }

        [Test]
        public void Test7AndProperties()
        {
            BrowseRequest request = CreateRequest(1, BrowseSelection.ValueOperation.ValueOperationAnd, "prop1", "prop3");
            BrowseResult res = browser.Browse(request);
            Console.WriteLine(res);
            Assert.AreEqual(res.NumHits, 2);
            Assert.AreEqual(res.Hits[0].DocId, 1);
            Assert.AreEqual(res.Hits[1].DocId, 2);
            ICollection<BrowseFacet> facets = res.GetFacetAccessor(AttributeHandlerName).GetFacets();
            Assert.AreEqual(facets.Count, 4);
            Assert.AreEqual(facets.Get(0).Value, "prop1=val2");
            Assert.AreEqual(facets.Get(0).FacetValueHitCount, 2);
            Assert.AreEqual(facets.Get(1).Value, "prop3=val1");
            Assert.AreEqual(facets.Get(1).FacetValueHitCount, 1);
        }

        [Test]
        public void Test9ModifiedNumberOfFacetsPerKey()
        {
            ModifiedSetup();
            BrowseRequest request = CreateRequest(1, BrowseSelection.ValueOperation.ValueOperationOr);
            request.GetFacetSpec(AttributeHandlerName).OrderBy = FacetSpec.FacetSortSpec.OrderHitsDesc;
            BrowseResult res = browser.Browse(request);
            Console.WriteLine(res);
            ICollection<BrowseFacet> facets = res.GetFacetAccessor(AttributeHandlerName).GetFacets();
            Assert.AreEqual(facets.Count, 6);
            Assert.AreEqual(facets.Get(0).Value, "prop1=val1");
            Assert.AreEqual(facets.Get(0).FacetValueHitCount, 4);
            Assert.AreEqual(facets.Get(1).Value, "prop2=val1");
            Assert.AreEqual(facets.Get(1).FacetValueHitCount, 4);
            Assert.AreEqual(facets.Get(2).Value, "prop3=val1");
            Assert.AreEqual(facets.Get(2).FacetValueHitCount, 1);
        }

        [Test]
        public void Test8AndPropertiesPlsExclusion()
        {
            BrowseRequest request = CreateRequest(1, BrowseSelection.ValueOperation.ValueOperationAnd, "prop1", "prop3");
            request.GetSelection(AttributeHandlerName).AddNotValue("prop7");
            BrowseResult res = browser.Browse(request);
            Console.WriteLine(res);
            Assert.AreEqual(res.NumHits, 1);
            Assert.AreEqual(res.Hits[0].DocId, 2);
            ICollection<BrowseFacet> facets = res.GetFacetAccessor(AttributeHandlerName).GetFacets();
            Assert.AreEqual(facets.Count, 3);
            Assert.AreEqual(facets.Get(0).Value, "prop1=val2");
            Assert.AreEqual(facets.Get(0).FacetValueHitCount, 1);
            Assert.AreEqual(facets.Get(1).Value, "prop3=val2");
            Assert.AreEqual(facets.Get(1).FacetValueHitCount, 1);
            Assert.AreEqual(facets.Get(2).Value, "prop3=val3");
            Assert.AreEqual(facets.Get(2).FacetValueHitCount, 1);
        }

        [Test]
        public void Test10ModifiedNumberOfFacetsPerKeyInSelection()
        {
            ModifiedSetup();
            selectionProperties.Put(AttributesFacetHandler.MAX_FACETS_PER_KEY_PROP_NAME, "2");
            BrowseRequest request = CreateRequest(1, BrowseSelection.ValueOperation.ValueOperationOr, "prop1", "prop2", 
                "prop3", "prop4", "prop5", "prop6", "prop7");
            request.GetFacetSpec(AttributeHandlerName).OrderBy = FacetSpec.FacetSortSpec.OrderHitsDesc;
            BrowseResult res = browser.Browse(request);
            Console.WriteLine(res);
            ICollection<BrowseFacet> facets = res.GetFacetAccessor(AttributeHandlerName).GetFacets();
            Assert.AreEqual(facets.Count, 9);
            Assert.AreEqual(facets.Get(0).Value, "prop1=val1");
            Assert.AreEqual(facets.Get(0).FacetValueHitCount, 4);
            Assert.AreEqual(facets.Get(1).Value, "prop2=val1");
            Assert.AreEqual(facets.Get(1).FacetValueHitCount, 4);
            Assert.AreEqual(facets.Get(2).Value, "prop1=val2");
            Assert.AreEqual(facets.Get(2).FacetValueHitCount, 2);
            Assert.AreEqual(facets.Get(3).Value, "prop3=val1");
            Assert.AreEqual(facets.Get(3).FacetValueHitCount, 1);
        }

        private void ModifiedSetup()
        {
            directory = new RAMDirectory();
            analyzer = new WhitespaceAnalyzer(LuceneVersion.LUCENE_48);
            IndexWriterConfig conf = new IndexWriterConfig(LuceneVersion.LUCENE_48, analyzer);
            conf.SetOpenMode(OpenMode.CREATE);
            IndexWriter writer = new IndexWriter(directory, conf);

            writer.AddDocument(Doc("prop1=val1", "prop2=val1", "prop5=val1"));
            writer.AddDocument(Doc("prop1=val2", "prop3=val1", "prop7=val7"));
            writer.AddDocument(Doc("prop1=val2", "prop3=val2", "prop3=val3"));
            writer.AddDocument(Doc("prop1=val1", "prop2=val1"));
            writer.AddDocument(Doc("prop1=val1", "prop2=val1"));
            writer.AddDocument(Doc("prop1=val1", "prop2=val1", "prop4=val2", "prop4=val3"));
            writer.Commit();

            IDictionary<string, string> facetProps = new Dictionary<string, string>();
            facetProps.Put(AttributesFacetHandler.MAX_FACETS_PER_KEY_PROP_NAME, "1");
            attributesFacetHandler = new AttributesFacetHandler(AttributeHandlerName, AttributeHandlerName, 
                null, null, facetProps);
            facetHandlers.Add(attributesFacetHandler);
            DirectoryReader reader = DirectoryReader.Open(directory);
            boboReader = BoboMultiReader.GetInstance(reader, facetHandlers);
            foreach (BoboSegmentReader subReader in boboReader.GetSubReaders())
            {
                attributesFacetHandler.LoadFacetData(subReader);
            }
            browser = new BoboBrowser(boboReader);
        }
    }
}
